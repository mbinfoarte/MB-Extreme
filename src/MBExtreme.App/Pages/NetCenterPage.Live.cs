using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using MBExtreme.Core.Licensing;
using MBExtreme.Core.NetDiag;
using MBExtreme.Core.Network;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Visão geral da Central de Rede como painel ao vivo: mapa da conexão (PC → roteador → provedor → internet, aparelhos da rede e
/// destinos), saúde da conexão com o anel, alertas, dados deste PC, eventos, ping contínuo, DNS mais rápido, canais do Wi-Fi e
/// conexões por programa. O ping contínuo é medido de verdade a cada segundo; o resto vem do último diagnóstico e das varreduras.
/// </summary>
public sealed partial class NetCenterPage
{
    private const string LiveTarget = "1.1.1.1";
    private static readonly Color Gray = Color.FromRgb(0x8E, 0x95, 0xA1);

    private readonly TextBlock _liveLine = new();
    private Button _monitorBtn = new();
    private readonly PingSeries _ping = new(90);
    private readonly PingWatch _watch = new(LiveTarget);
    private readonly NetEventLog _events = new();
    private readonly NetworkMonitor _traffic = new();
    private NetworkSample? _rate;
    private IReadOnlyList<(string Program, int Connections)> _byProgram = Array.Empty<(string, int)>();
    private DispatcherTimer? _timer;
    private bool _pinging;
    private int _ticks;
    private DateTime? _monitorUntil;
    private PingSeries? _monitorSeries;
    private int _monitorSpikes;

    // partes do painel que mudam a cada segundo (o resto só muda quando há diagnóstico novo)
    private readonly ContentControl _healthHost = new(), _eventsHost = new(), _pingHost = new(), _trafficHost = new();
    private ScoreCore? _ring;

    private bool Monitoring => _monitorUntil is { } u && DateTime.Now < u;

    private static bool Animate => AppServices.Settings.Animations && SystemParameters.ClientAreaAnimation;

    // ---------------------------------------------------------------- relógio do ao vivo

    private void StartLive()
    {
        UpdateLiveLine();
        if (_timer is not null) return;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) => await TickAsync();
        _timer.Start();
    }

    private void StopLive()
    {
        _timer?.Stop();
        _timer = null;
    }

    private async Task TickAsync()
    {
        _ticks++;
        if (!_pinging)
        {
            _pinging = true;
            try
            {
                double? ms = await NetworkMonitor.PingAsync(LiveTarget, 1500);
                double? median = _ping.Median;
                _ping.Add(ms);
                _monitorSeries?.Add(ms);
                if (_watch.Feed(DateTimeOffset.Now, ms, median) is { } ev)
                {
                    _events.Add(ev);
                    if (ev.Level == FindingSeverity.Atencao && Monitoring) _monitorSpikes++;
                }
            }
            finally { _pinging = false; }
        }
        try { _rate = _traffic.Sample(); } catch (Exception ex) when (ex is InvalidOperationException or System.Net.NetworkInformation.NetworkInformationException) { _rate = null; }
        if (_ticks % 5 == 1) _byProgram = await Task.Run(ReadConnections);

        if (_monitorUntil is { } until && DateTime.Now >= until) FinishMonitor();
        UpdateLiveLine();
        if (IsLoaded && _section == "overview" && _busyText.Length == 0) RefreshLive();
    }

    private static IReadOnlyList<(string, int)> ReadConnections()
    {
        try
        {
            var rows = GameConnections.ReadTcpTable().Select(r => (r.State, r.Pid)).ToList();
            var names = new Dictionary<int, string?>();
            string? NameOf(int pid)
            {
                if (names.TryGetValue(pid, out var n)) return n;
                try { using var p = Process.GetProcessById(pid); n = p.ProcessName; } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { n = null; }
                names[pid] = n;
                return n;
            }
            return TrafficRules.ByProgram(rows, NameOf);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            return Array.Empty<(string, int)>();
        }
    }

    private void UpdateLiveLine()
    {
        var p = _diag?.Evidence.Primary;
        string link = p is null ? "SEM DIAGNÓSTICO" : (p.Kind.ToUpperInvariant() + (p.SpeedBps > 0 ? " " + Speed(p.SpeedBps) : ""));
        bool live = _timer is not null && _ping.Samples.Count > 0;
        bool up = _ping.Samples.LastOrDefault() is not null;
        _liveLine.Text = (Monitoring ? $"● MONITORANDO · faltam {Math.Max(1, (int)Math.Ceiling((_monitorUntil!.Value - DateTime.Now).TotalMinutes))} min" : live ? (up ? "● AO VIVO" : "● SEM RESPOSTA") : "● PARADO") + " · " + link;
        _liveLine.Foreground = new SolidColorBrush(!live ? Gray : up ? Green : Red);
    }

    private static string Speed(long bps) => bps >= 1_000_000_000 ? $"{bps / 1_000_000_000.0:0.#} GBPS".Replace('.', ',') : $"{bps / 1_000_000} MBPS";

    // ---------------------------------------------------------------- monitorar 1 hora

    private void ToggleMonitor()
    {
        if (Monitoring) { FinishMonitor(stopped: true); return; }
        _monitorUntil = DateTime.Now.AddHours(1);
        _monitorSeries = new PingSeries(3600);
        _monitorSpikes = 0;
        _events.Add(DateTimeOffset.Now, $"Monitoramento de 1 hora começou (ping em {LiveTarget} a cada segundo)");
        _monitorBtn.Content = "Parar monitoramento";
        StartLive();
        if (_section == "overview") RefreshLive();
    }

    private void FinishMonitor(bool stopped = false)
    {
        var s = _monitorSeries;
        _monitorUntil = null;
        _monitorSeries = null;
        _monitorBtn.Content = "Monitorar 1 hora";
        if (s is { Samples.Count: > 0 })
        {
            var lvl = s.LossPct >= 2 || _monitorSpikes >= 10 ? FindingSeverity.Atencao : FindingSeverity.Info;
            _events.Add(DateTimeOffset.Now, $"Monitoramento {(stopped ? "parado" : "concluído")}: {s.Samples.Count} pings · mediana {s.Median:0} ms · perda {s.LossPct:0.#}% · {_monitorSpikes} picos", lvl);
        }
        if (!IsLoaded) StopLive();
    }

    /// <summary>Registra no painel de eventos o que o diagnóstico achou.</summary>
    private void LogDiag(NetDiagReport r)
    {
        var o = NetOverviewRules.From(r, _wifiA);
        _events.Add(r.FinishedUtc.ToLocalTime(), $"Diagnóstico completo · nota {o.Quality}", o.Level);
        foreach (var f in r.Findings.Where(f => f.Severity != FindingSeverity.Info).Take(4)) _events.Add(r.FinishedUtc.ToLocalTime(), f.Title, f.Severity);
    }

    // ---------------------------------------------------------------- tela

    private UIElement LiveSection()
    {
        var page = new StackPanel();
        page.Children.Add(Card("Mapa da conexão", MapView(), "PC → ROTEADOR → PROVEDOR → INTERNET"));
        if (_diag is null)
            page.Children.Add(Card("Ainda sem diagnóstico", new StackPanel
            {
                Children =
                {
                    Muted("O ping ao vivo já está rodando. Clique em \"Diagnosticar agora\" para medir o roteador, o provedor, o DNS e o caminho. Leva uns 30 a 60 segundos e não muda nada na rede.", 12.5),
                    Wrap(Btn("Diagnosticar agora", async () => await RunFullAsync(), true)),
                },
            }));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel();
        var right = new StackPanel();
        Grid.SetColumn(right, 2);
        grid.Children.Add(left);
        grid.Children.Add(right);

        var alerts = Alerts();
        left.Children.Add(Card("Saúde da conexão", _healthHost, "AGORA"));
        right.Children.Add(Card("Alertas", AlertsView(alerts), alerts.Count.ToString()));
        left.Children.Add(Card("Este PC", ThisPc()));
        right.Children.Add(Card("Eventos", _eventsHost));
        left.Children.Add(Card("Ping contínuo", _pingHost, LiveTarget));
        right.Children.Add(Card("DNS mais rápido", DnsBars()));
        left.Children.Add(Card("Wi-Fi · canais 2,4 GHz", WifiMini()));
        right.Children.Add(Card("Tráfego e conexões", _trafficHost));
        page.Children.Add(grid);

        if (_diag is { } r)
        {
            var o = NetOverviewRules.From(r, _wifiA);
            var rows = new StackPanel();
            foreach (var row in o.Rows)
            {
                var line = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                Color c = row.State is "Não aplicável" or "Não testado" ? Gray : SevColor(row.Level);
                var area = new TextBlock { Text = row.Area + ":", Width = 170, FontSize = 13, FontWeight = FontWeights.SemiBold };
                DockPanel.SetDock(area, Dock.Left);
                line.Children.Add(area);
                var txt = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap };
                txt.Inlines.Add(new System.Windows.Documents.Run(row.State) { Foreground = new SolidColorBrush(c), FontWeight = FontWeights.Bold });
                txt.Inlines.Add(new System.Windows.Documents.Run(" – " + row.Text));
                line.Children.Add(txt);
                rows.Children.Add(line);
            }
            if (o.Recommendations.Count > 0)
            {
                rows.Children.Add(new TextBlock { Text = "Recomendações", FontSize = 13.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) });
                foreach (string rec in o.Recommendations) rows.Children.Add(new TextBlock { Text = "• " + rec, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1) });
            }
            page.Children.Add(Card("Resumo do diagnóstico", rows));
        }

        page.Children.Add(Card("Ações rápidas", Wrap(
            Btn("Teste de internet", () => { Show("speedtest"); return Task.CompletedTask; }),
            Btn("Traçar rota", () => { Show("route"); return Task.CompletedTask; }),
            Btn("Ver aparelhos da rede", () => { Show("lan"); return Task.CompletedTask; }),
            Btn("Limpar cache DNS", async () => { if (Has(NetCenterAccess.RepairPlan(RepairKind.FlushDns))) await RepairAsync(RepairKind.FlushDns); else Show("repair"); }),
            Btn("Reparar rede", () => { Show("repair"); return Task.CompletedTask; }))));

        _ring = null;
        RefreshLive();
        return page;
    }

    private void RefreshLive()
    {
        _healthHost.Content = Health();
        _eventsHost.Content = EventsView();
        _pingHost.Content = PingChart();
        _trafficHost.Content = TrafficView();
    }

    /// <summary>Cartão no estilo da prévia: título em maiúsculas espaçadas e um rótulo amarelo à direita.</summary>
    private Border Card(string title, UIElement body, string? tag = null)
    {
        var st = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        if (tag is not null)
        {
            var tg = new TextBlock { Text = tag, FontFamily = new FontFamily("Consolas"), FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Yellow), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(tg, Dock.Right);
            head.Children.Add(tg);
        }
        head.Children.Add(new TextBlock { Text = string.Join(" ", title.ToUpperInvariant().ToCharArray()).Replace("   ", "  "), FontFamily = new FontFamily("Consolas"), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brush("MutedBrush") });
        st.Children.Add(head);
        st.Children.Add(body);
        return new Border
        {
            Child = st, Padding = new Thickness(16, 14, 16, 14), Margin = new Thickness(0, 0, 0, 14), CornerRadius = new CornerRadius(14),
            Background = Brush("SurfaceBrush"), BorderBrush = Brush("BorderBrush"), BorderThickness = new Thickness(1),
        };
    }

    // ---------------------------------------------------------------- mapa da conexão

    private static Color LevelColor(FindingSeverity l, bool tested = true) => !tested ? Gray : l switch { FindingSeverity.Problema => Red, FindingSeverity.Atencao => Orange, _ => Green };

    private UIElement MapView()
    {
        var map = ConnectionMapRules.Build(_diag, _lan, _conflicts, _ping.Samples.LastOrDefault());
        const double W = 820, H = 400, Y = 200;
        var c = new Canvas { Width = W, Height = H, ClipToBounds = true };
        for (double x = 20; x < W; x += 40) c.Children.Add(new Line { X1 = x, X2 = x, Y1 = 0, Y2 = H, Stroke = new SolidColorBrush(Color.FromArgb(0x10, 255, 255, 255)), StrokeThickness = 1 });
        for (double y = 20; y < H; y += 40) c.Children.Add(new Line { X1 = 0, X2 = W, Y1 = y, Y2 = y, Stroke = new SolidColorBrush(Color.FromArgb(0x10, 255, 255, 255)), StrokeThickness = 1 });

        var nodes = new[] { (map.Pc, 80.0), (map.Router, 240.0), (map.Isp, 410.0), (map.Internet, 570.0) };
        // ligações principais, com o "pacote" correndo
        for (int i = 0; i < nodes.Length - 1; i++)
        {
            var next = nodes[i + 1].Item1;
            Color col = LevelColor(next.Level, next.Tested);
            Flow(c, new Point(nodes[i].Item2 + 34, Y), new Point(nodes[i + 1].Item2 - 34, Y), col, i * 0.35);
        }
        if (map.IspHop is { } hop)
        {
            var warn = new TextBlock { Text = "⚠ salto " + hop, FontFamily = new FontFamily("Consolas"), FontSize = 12.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Orange) };
            Canvas.SetLeft(warn, 455); Canvas.SetTop(warn, Y - 40); c.Children.Add(warn);
        }

        // aparelhos da rede (em cima os com conflito, embaixo os outros), ligados ao roteador
        var devs = map.Devices;
        double[] dy = { 60, 120, 290, 345 };
        for (int i = 0; i < devs.Count; i++)
        {
            var d = devs[i];
            Color col = d.Conflict ? Red : Blue;
            var at = new Point(175, dy[i]);
            Curve(c, at, new Point(240, Y), col, 1.4, dotted: true);
            c.Children.Add(Ring(at, 11, col, 2, 0));
            Text(c, d.Name.Length > 18 ? d.Name[..17] + "…" : d.Name, at.X - 18, at.Y - 9, 12.5, Colors.White, right: true, bold: true);
            Text(c, d.Ip, at.X - 18, at.Y + 7, 11, d.Conflict ? Red : Blue, right: true, mono: true);
            if (d.Conflict) Text(c, "IP DUPLICADO", at.X - 18, at.Y + 21, 11, Red, right: true, mono: true, bold: true);
        }

        // destinos da internet à direita
        var tg = map.Targets;
        for (int i = 0; i < tg.Count; i++)
        {
            double ty = tg.Count == 1 ? Y : 85 + i * (230.0 / Math.Max(1, tg.Count - 1));
            var t = tg[i];
            Color col = t.Ms is null ? Red : LevelColor(t.Level);
            Curve(c, new Point(604, Y), new Point(740, ty), col, 1.6, dotted: false, dashed: true);
            Text(c, t.Name, 700, ty - 30, 12, Gray, right: true);
            var box = new Border
            {
                Width = 70, Height = 28, CornerRadius = new CornerRadius(7), BorderBrush = new SolidColorBrush(col), BorderThickness = new Thickness(1.4),
                Background = new SolidColorBrush(Color.FromArgb(0x22, col.R, col.G, col.B)),
                Child = new TextBlock { Text = t.Ms is { } ms ? $"{ms:0} ms" : "falhou", FontFamily = new FontFamily("Consolas"), FontSize = 12.5, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            Canvas.SetLeft(box, 740); Canvas.SetTop(box, ty - 14); c.Children.Add(box);
        }

        // os quatro pontos principais
        for (int i = 0; i < nodes.Length; i++)
        {
            var (n, x) = nodes[i];
            Color col = LevelColor(n.Level, n.Tested);
            var at = new Point(x, Y);
            c.Children.Add(Glow(at, 46, col));
            c.Children.Add(Ring(at, 29, col, 3, 0x18));
            var dot = Ring(at, 7, col, 0, 0xFF);
            c.Children.Add(dot);
            if (Animate) Pulse(dot, i * 0.25);
            Text(c, n.Title, x, Y + 40, 14.5, Colors.White, center: true, bold: true);
            Text(c, n.Value, x, Y + 60, 12, n.Level == FindingSeverity.Info ? Gray : col, center: true, mono: true, bold: n.Level != FindingSeverity.Info);
        }

        var legend = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var (txt, col) in new[] { ("ok", Green), ("atenção", Orange), ("problema", Red), ("aparelho da rede", Blue), ("não testado", Gray) })
        {
            legend.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(col), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            legend.Children.Add(new TextBlock { Text = txt, FontSize = 12, Foreground = Brush("MutedBrush"), Margin = new Thickness(0, 0, 16, 0) });
        }
        var st = new StackPanel();
        st.Children.Add(new Viewbox { Child = c, Stretch = Stretch.Uniform, MaxHeight = 430 });
        if (map.Devices.Count == 0) st.Children.Add(Muted("Os aparelhos da sua rede aparecem aqui depois de \"Rede local e conflitos\" › Varrer a rede.", 11.5));
        st.Children.Add(legend);
        return st;
    }

    private static Ellipse Ring(Point at, double r, Color col, double stroke, byte fillAlpha) => Place(new Ellipse
    {
        Width = r * 2, Height = r * 2, Stroke = stroke > 0 ? new SolidColorBrush(col) : null, StrokeThickness = stroke,
        Fill = fillAlpha > 0 ? new SolidColorBrush(Color.FromArgb(fillAlpha, col.R, col.G, col.B)) : null,
    }, at, r);

    private static Ellipse Glow(Point at, double r, Color col) => Place(new Ellipse
    {
        Width = r * 2, Height = r * 2,
        Fill = new RadialGradientBrush(Color.FromArgb(0x30, col.R, col.G, col.B), Color.FromArgb(0, col.R, col.G, col.B)),
    }, at, r);

    private static Ellipse Place(Ellipse e, Point at, double r)
    {
        Canvas.SetLeft(e, at.X - r);
        Canvas.SetTop(e, at.Y - r);
        return e;
    }

    private static void Text(Canvas c, string text, double x, double y, double size, Color col, bool center = false, bool right = false, bool mono = false, bool bold = false)
    {
        var tb = new TextBlock
        {
            Text = text, FontSize = size, Foreground = new SolidColorBrush(col), FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            FontFamily = new FontFamily(mono ? "Consolas" : "Segoe UI"),
        };
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double w = tb.DesiredSize.Width;
        Canvas.SetLeft(tb, center ? x - w / 2 : right ? x - w : x);
        Canvas.SetTop(tb, y - tb.DesiredSize.Height / 2);
        c.Children.Add(tb);
    }

    /// <summary>Linha tracejada que anda e um ponto correndo de um lado ao outro (parados se as animações estiverem desligadas).</summary>
    private static void Flow(Canvas c, Point a, Point b, Color col, double delay)
    {
        var line = new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = new SolidColorBrush(col), StrokeThickness = 2.4, StrokeDashArray = new DoubleCollection { 3, 3 } };
        c.Children.Add(line);
        var dot = new Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(col) };
        Canvas.SetTop(dot, a.Y - 3.5);
        Canvas.SetLeft(dot, (a.X + b.X) / 2 - 3.5);
        c.Children.Add(dot);
        if (!Animate) return;
        line.BeginAnimation(Shape.StrokeDashOffsetProperty, new DoubleAnimation(0, -12, TimeSpan.FromSeconds(0.8)) { RepeatBehavior = RepeatBehavior.Forever });
        dot.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(a.X - 3.5, b.X - 3.5, TimeSpan.FromSeconds(1.6))
        {
            RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(delay), EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
    }

    private static void Curve(Canvas c, Point a, Point b, Color col, double thick, bool dotted, bool dashed = false)
    {
        var fig = new PathFigure { StartPoint = a };
        double mx = (a.X + b.X) / 2;
        fig.Segments.Add(new BezierSegment(new Point(mx, a.Y), new Point(mx, b.Y), b, true));
        var path = new Path
        {
            Data = new PathGeometry(new[] { fig }), Stroke = new SolidColorBrush(Color.FromArgb(0xC0, col.R, col.G, col.B)), StrokeThickness = thick,
            StrokeDashArray = dotted ? new DoubleCollection { 1, 3 } : dashed ? new DoubleCollection { 4, 3 } : null,
        };
        c.Children.Add(path);
        if (Animate && (dotted || dashed))
            path.BeginAnimation(Shape.StrokeDashOffsetProperty, new DoubleAnimation(0, dotted ? -8 : -14, TimeSpan.FromSeconds(1.2)) { RepeatBehavior = RepeatBehavior.Forever });
    }

    private static void Pulse(UIElement e, double delay) =>
        e.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.45, TimeSpan.FromSeconds(0.9)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(delay) });

    // ---------------------------------------------------------------- saúde

    private UIElement Health()
    {
        var o = _diag is { } r ? NetOverviewRules.From(r, _wifiA) : null;
        var st = new StackPanel();
        var top = new DockPanel();
        _ring ??= new ScoreCore(92);
        if (_ring.Parent is Panel old) old.Children.Remove(_ring);
        var ringHost = new Grid { Width = 92, Height = 92, Margin = new Thickness(0, 0, 14, 0) };
        ringHost.Children.Add(_ring);
        ringHost.Children.Add(new TextBlock { Text = o is null ? "—" : o.Quality.ToString(), FontSize = 24, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        if (o is not null)
        {
            _ring.Accent = o.Level switch { FindingSeverity.Problema => Red, FindingSeverity.Atencao => Orange, _ => Yellow };
            _ring.AnimateTo(o.Quality);
        }
        _ring.Running = _busyText.Length > 0 || Monitoring;
        DockPanel.SetDock(ringHost, Dock.Left);
        top.Children.Add(ringHost);
        var tt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        tt.Children.Add(new TextBlock { Text = o?.Headline ?? "Ainda sem diagnóstico", FontSize = 16, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
        tt.Children.Add(Muted(o?.Sub ?? "A nota sai do diagnóstico completo. O ping ao vivo abaixo já é medido agora.", 12.5));
        top.Children.Add(tt);
        st.Children.Add(top);

        double? ping = _ping.Median ?? _diag?.Evidence.External.Values.Where(v => !v.AllLost).Select(v => v.Median).Min();
        double? jit = _ping.Jitter;
        double loss = _ping.LossPct;
        double? dns = _diag?.Evidence.DnsSystem.Where(d => d.Ok).Select(d => d.Ms).Min();
        var g = new UniformGrid2();
        g.Add(Mini("Ping (mediana)", ping is { } pv ? pv.ToString("0") : "—", "ms", ping is null ? Gray : ping < 60 ? Colors.White : Orange));
        g.Add(Mini("Variação", jit is { } jv ? jv.ToString("0.0") : "—", "ms", jit is null or < 15 ? Colors.White : Orange));
        g.Add(Mini("Perda", _ping.Samples.Count == 0 ? "—" : loss.ToString("0.0"), "%", loss > 0 ? Orange : Colors.White));
        g.Add(Mini("DNS", dns is { } dv ? dv.ToString("0") : "—", "ms", dns is null or < 60 ? Colors.White : Orange));
        st.Children.Add(g.Grid);
        return st;
    }

    private Border Mini(string label, string value, string unit, Color col)
    {
        var st = new StackPanel();
        st.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Brush("MutedBrush") });
        var v = new TextBlock { FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 2, 0, 0) };
        v.Inlines.Add(new System.Windows.Documents.Run(value.Replace('.', ',')) { Foreground = new SolidColorBrush(col), FontFamily = new FontFamily("Consolas") });
        v.Inlines.Add(new System.Windows.Documents.Run(" " + unit) { FontSize = 11, Foreground = Brush("MutedBrush") });
        st.Children.Add(v);
        return new Border
        {
            Child = st, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 12, 0, 0), CornerRadius = new CornerRadius(10),
            Background = Brush("Surface2Brush"), BorderBrush = Brush("BorderBrush"), BorderThickness = new Thickness(1),
        };
    }

    /// <summary>Grade de duas colunas com espaço entre elas.</summary>
    private sealed class UniformGrid2
    {
        private int _n;
        public Grid Grid { get; } = new();

        public UniformGrid2()
        {
            Grid.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            Grid.ColumnDefinitions.Add(new ColumnDefinition());
        }

        public void Add(UIElement e)
        {
            if (_n % 2 == 0) Grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(e, _n / 2);
            Grid.SetColumn(e, _n % 2 == 0 ? 0 : 2);
            Grid.Children.Add(e);
            _n++;
        }
    }

    // ---------------------------------------------------------------- alertas

    private List<(string Title, string Text, string Foot, FindingSeverity Level)> Alerts()
    {
        var list = new List<(string, string, string, FindingSeverity)>();
        static string Conf(double c) => c >= 0.8 ? "confiança alta" : c >= 0.5 ? "confiança média" : "confiança baixa";
        foreach (var c in _conflicts)
            list.Add(("Dois aparelhos com o mesmo IP", $"{c.Ip} respondeu com {c.Macs.Count} MACs diferentes ({string.Join(", ", c.Macs.Take(3))}).", c.Evidence + " · " + Conf(c.Confidence), FindingSeverity.Problema));
        foreach (var f in (_diag?.Findings ?? Array.Empty<Finding>()).Concat(_wifiA?.Findings ?? Array.Empty<Finding>()).Where(f => f.Severity != FindingSeverity.Info))
            list.Add((f.Title, f.Evidence.FirstOrDefault() ?? f.Next, (f.Status switch { FindingStatus.Confirmado => "confirmado", FindingStatus.Provavel => "provável", FindingStatus.Indeterminado => "incerto", _ => "não testado" }) + " · " + Conf(f.Confidence), f.Severity));
        return list.OrderByDescending(x => x.Item4).Take(6).ToList();
    }

    private UIElement AlertsView(List<(string Title, string Text, string Foot, FindingSeverity Level)> alerts)
    {
        var st = new StackPanel();
        if (alerts.Count == 0)
        {
            st.Children.Add(Muted(_diag is null ? "Sem diagnóstico ainda. Os alertas aparecem aqui com a evidência e a confiança de cada um." : "Nenhum alerta. Seu PC, o roteador e a internet responderam bem.", 12.5));
            return st;
        }
        foreach (var a in alerts)
        {
            var b = new StackPanel();
            b.Children.Add(new TextBlock { Text = a.Title, FontSize = 13.5, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
            b.Children.Add(new TextBlock { Text = a.Text, FontSize = 12.5, Foreground = Brush("MutedBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
            b.Children.Add(new TextBlock { Text = a.Foot, FontSize = 11, FontFamily = new FontFamily("Consolas"), Foreground = Brush("MutedBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
            st.Children.Add(new Border
            {
                Child = b, Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 8), CornerRadius = new CornerRadius(10), Background = Brush("Surface2Brush"),
                BorderBrush = new SolidColorBrush(SevColor(a.Level)), BorderThickness = new Thickness(4, 0, 0, 0),
            });
        }
        return st;
    }

    // ---------------------------------------------------------------- este PC e eventos

    private UIElement ThisPc()
    {
        var p = _diag?.Evidence.Primary;
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (p is null) return Muted("Clique em \"Diagnosticar agora\" para ler a placa, o IP, o roteador e o DNS.", 12.5);
        var rows = new (string, string)[]
        {
            ("Placa", p.Kind + (p.SpeedBps > 0 ? " · " + (p.SpeedBps >= 1_000_000_000 ? $"{p.SpeedBps / 1e9:0.#} Gbps" : $"{p.SpeedBps / 1_000_000} Mbps") : "")),
            ("IP", (p.Ipv4.FirstOrDefault() ?? "—") + (p.Ipv4Prefix.Count > 0 ? "/" + p.Ipv4Prefix[0] : "")),
            ("Roteador", p.Gateways.FirstOrDefault() ?? "—"),
            ("DNS", p.Dns.Count > 0 ? string.Join(" · ", p.Dns.Take(2)) : "—"),
            ("DHCP", p.Dhcp ? "sim" + (p.DhcpServer is { Length: > 0 } ds ? " · " + ds : "") : "não (IP fixo)"),
            ("MTU", p.Mtu > 0 ? p.Mtu.ToString() : "—"),
            ("IPv6", p.Ipv6.Count > 0 ? "ativo" : "desligado"),
        };
        for (int i = 0; i < rows.Length; i++)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var k = new TextBlock { Text = rows[i].Item1, FontSize = 13, Foreground = Brush("MutedBrush"), Margin = new Thickness(0, 4, 12, 4) };
            var v = new TextBlock { Text = rows[i].Item2, FontSize = 13, FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0, 4, 0, 4), HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetRow(k, i); Grid.SetRow(v, i); Grid.SetColumn(v, 1);
            g.Children.Add(k); g.Children.Add(v);
        }
        return g;
    }

    private UIElement EventsView()
    {
        var st = new StackPanel();
        if (_events.Items.Count == 0) { st.Children.Add(Muted("Sem eventos ainda. Picos e quedas do ping ao vivo, diagnósticos, varreduras e reparações aparecem aqui.", 12.5)); return st; }
        foreach (var e in _events.Items.Take(8))
        {
            var t = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
            t.Inlines.Add(new System.Windows.Documents.Run(e.At.ToLocalTime().ToString("HH:mm:ss") + " · ") { Foreground = Brush("MutedBrush") });
            t.Inlines.Add(new System.Windows.Documents.Run(e.Text) { Foreground = e.Level == FindingSeverity.Info ? Brushes.White : new SolidColorBrush(SevColor(e.Level)), FontWeight = FontWeights.Bold });
            st.Children.Add(t);
        }
        return st;
    }

    // ---------------------------------------------------------------- ping contínuo

    private UIElement PingChart()
    {
        const double W = 420, H = 150, L = 26, B = 8, T = 6;
        var s = _ping.Samples;
        double peak = s.Where(x => x.HasValue).Select(x => x!.Value).DefaultIfEmpty(0).Max();
        double max = Math.Max(60, Math.Ceiling(peak / 20) * 20);
        var c = new Canvas { Width = W, Height = H };
        for (int i = 0; i <= 3; i++)
        {
            double v = max * i / 3, y = H - B - (H - B - T) * i / 3;
            c.Children.Add(new Line { X1 = L, X2 = W, Y1 = y, Y2 = y, Stroke = new SolidColorBrush(Color.FromArgb(0x18, 255, 255, 255)), StrokeThickness = 1 });
            var tb = new TextBlock { Text = v.ToString("0"), FontSize = 10, FontFamily = new FontFamily("Consolas"), Foreground = Brush("MutedBrush") };
            Canvas.SetLeft(tb, 0); Canvas.SetTop(tb, y - 7); c.Children.Add(tb);
        }
        if (s.Count > 1)
        {
            double X(int i) => L + 4 + (W - L - 8) * i / (_ping.Capacity - 1);
            double Yv(double v) => H - B - (H - B - T) * Math.Min(v, max) / max;
            var spikes = _ping.Spikes().ToHashSet();
            var line = new Polyline { Stroke = new SolidColorBrush(Yellow), StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
            var area = new Polygon { Fill = new LinearGradientBrush(Color.FromArgb(0x40, 0xFF, 0xC4, 0x00), Color.FromArgb(0x00, 0xFF, 0xC4, 0x00), 90) };
            int first = -1, last = -1;
            for (int i = 0; i < s.Count; i++)
            {
                if (s[i] is not { } v)
                {
                    var lost = new Line { X1 = X(i), X2 = X(i), Y1 = T, Y2 = H - B, Stroke = new SolidColorBrush(Color.FromArgb(0x70, Red.R, Red.G, Red.B)), StrokeThickness = 2 };
                    c.Children.Add(lost);
                    continue;
                }
                var pt = new Point(X(i), Yv(v));
                if (first < 0) { first = i; area.Points.Add(new Point(pt.X, H - B)); }
                last = i;
                line.Points.Add(pt); area.Points.Add(pt);
            }
            if (first >= 0)
            {
                area.Points.Add(new Point(X(last), H - B));
                c.Children.Add(area);
                c.Children.Add(line);
                foreach (int i in spikes)
                {
                    var dot = Ring(new Point(X(i), Yv(s[i]!.Value)), 3.5, Red, 0, 0xFF);
                    c.Children.Add(dot);
                }
                var end = Ring(new Point(X(last), Yv(s[last]!.Value)), 4, Yellow, 0, 0xFF);
                c.Children.Add(end);
            }
        }
        var st = new StackPanel();
        st.Children.Add(new Viewbox { Child = c, Stretch = Stretch.Uniform });
        st.Children.Add(new TextBlock
        {
            Text = s.Count == 0 ? "Medindo..." : $"agora {(s[^1] is { } now ? now.ToString("0") + " ms" : "sem resposta")} · mediana {_ping.Median:0} ms · variação {(_ping.Jitter ?? 0):0.0} ms · perda {_ping.LossPct:0.#}% · {_ping.Spikes().Count} picos".Replace('.', ','),
            FontFamily = new FontFamily("Consolas"), FontSize = 11.5, Foreground = Brush("MutedBrush"), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap,
        });
        return st;
    }

    // ---------------------------------------------------------------- DNS, Wi-Fi e tráfego

    private UIElement DnsBars()
    {
        var list = new List<(string Name, double? Ms)>();
        if (_dns is { } d)
        {
            foreach (var r in d.Resolvers) list.Add((ConnectionMapRules.TargetName(r.Resolver), r.Ok ? r.Ms : null));
            if (d.Cached is { Ok: true } cc) list.Add(("Cache do PC", cc.Ms));
        }
        else if (_diag is { } r)
        {
            foreach (var x in r.Evidence.DnsSystem.Concat(r.Evidence.DnsAlternate)) list.Add((ConnectionMapRules.TargetName(x.Resolver), x.Ok ? x.Ms : null));
        }
        list = list.GroupBy(x => x.Name).Select(g => (g.Key, g.Where(x => x.Ms.HasValue).Select(x => x.Ms).DefaultIfEmpty(null).Min())).ToList();
        if (list.Count == 0) return new StackPanel { Children = { Muted("Faça o diagnóstico ou o teste de DNS para comparar os servidores.", 12.5), Wrap(Btn("Testar DNS", () => { Show("dns"); return Task.CompletedTask; })) } };
        var ok = list.Where(x => x.Ms.HasValue && x.Name != "Cache do PC").Select(x => x.Ms!.Value).ToList();
        double max = Math.Max(1, list.Where(x => x.Ms.HasValue).Select(x => x.Ms!.Value).DefaultIfEmpty(1).Max());
        double best = ok.DefaultIfEmpty(0).Min(), worst = ok.DefaultIfEmpty(0).Max();
        var st = new StackPanel();
        foreach (var (name, ms) in list.OrderBy(x => x.Name == "Cache do PC").ThenBy(x => x.Ms ?? 99999).Take(6))
        {
            Color col = ms is null ? Red : name == "Cache do PC" ? Green : ms == best ? Green : ok.Count > 2 && ms == worst ? Red : Yellow;
            st.Children.Add(Bar(name, ms is null ? "falhou" : $"{ms:0} ms", ms is null ? 1 : ms.Value / max, col));
        }
        return st;
    }

    private Grid Bar(string name, string value, double fraction, Color col)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        g.Children.Add(new TextBlock { Text = name, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        var track = new Border { Height = 9, CornerRadius = new CornerRadius(5), Background = Brush("Surface2Brush"), VerticalAlignment = VerticalAlignment.Center };
        var fill = new Border { Height = 9, CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(col), VerticalAlignment = VerticalAlignment.Center };
        var host = new Grid { Margin = new Thickness(0, 0, 10, 0) };
        host.Children.Add(track); host.Children.Add(fill);
        host.SizeChanged += (_, e) => fill.Width = Math.Max(8, e.NewSize.Width * Math.Clamp(fraction, 0, 1));
        Grid.SetColumn(host, 1); g.Children.Add(host);
        var v = new TextBlock { Text = value, FontSize = 12.5, FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Consolas"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(v, 2); g.Children.Add(v);
        return g;
    }

    private UIElement WifiMini()
    {
        if (_wifi is not { } scan)
        {
            bool wifi = _diag?.Evidence.Primary is { IsWifi: true };
            return new StackPanel
            {
                Children =
                {
                    Muted(_diag is not null && !wifi ? "Esta conexão é por cabo. O gráfico de canais aparece quando o PC usa Wi-Fi." : "Analise o Wi-Fi para ver os canais e as redes vizinhas.", 12.5),
                    Wrap(Btn("Analisar Wi-Fi", () => { Show("wifi"); return Task.CompletedTask; })),
                },
            };
        }
        if (!Has(NetCenterAccess.WifiFullPlan))
        {
            var cur = scan.Current;
            return new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = cur is null ? "Sem rede conectada" : $"{cur.Ssid} · canal {WifiRules.Channel(cur.FrequencyMhz)} · {cur.Rssi} dBm", FontSize = 14, FontWeight = FontWeights.SemiBold },
                    Muted($"O gráfico com as {scan.Networks.Count} redes vizinhas e o melhor canal é do plano Técnico.", 12),
                },
            };
        }
        var vb = new Viewbox { Child = ChannelChart(scan, WifiRules.B24), Stretch = Stretch.Uniform, MaxHeight = 170 };
        var st = new StackPanel();
        st.Children.Add(vb);
        if (_wifiA?.BestChannel.TryGetValue(WifiRules.B24, out int best) == true)
            st.Children.Add(new TextBlock { Text = $"Canal menos ocupado: {best}", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Green), Margin = new Thickness(0, 6, 0, 0) });
        return st;
    }

    private UIElement TrafficView()
    {
        var st = new StackPanel();
        var g = new UniformGrid2();
        g.Add(Mini("Baixando agora", _rate is { } r ? TrafficRules.Rate(r.DownloadBytesPerSecond).Split(' ')[0] : "—", _rate is { } r1 ? TrafficRules.Rate(r1.DownloadBytesPerSecond).Split(' ')[1] : "", Colors.White));
        g.Add(Mini("Enviando agora", _rate is { } u ? TrafficRules.Rate(u.UploadBytesPerSecond).Split(' ')[0] : "—", _rate is { } u1 ? TrafficRules.Rate(u1.UploadBytesPerSecond).Split(' ')[1] : "", Colors.White));
        st.Children.Add(g.Grid);
        st.Children.Add(new TextBlock { Text = "Conexões abertas por programa", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 2) });
        if (_byProgram.Count == 0) { st.Children.Add(Muted("Lendo as conexões...", 12)); return st; }
        int max = Math.Max(1, _byProgram.Max(x => x.Connections));
        foreach (var (prog, n) in _byProgram) st.Children.Add(Bar(prog, n == 1 ? "1 conexão" : $"{n} conexões", (double)n / max, Blue));
        st.Children.Add(Muted("O total baixado e enviado é da placa toda. Por programa, o Windows informa as conexões abertas.", 11));
        return st;
    }
}
