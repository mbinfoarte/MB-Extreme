using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MBExtreme.Core.NetDiag;
using MBExtreme.Core.Network;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Central de Rede: tudo de rede numa tela, com menu lateral próprio (Visão geral, DNS, Rede local, Relatórios, Reparação, Rotas e
/// Wi-Fi). Só lê a rede; as correções da Reparação rodam uma por vez, depois de confirmar, com foto de antes e registro no diário.
/// </summary>
public sealed partial class NetCenterPage : ToolPageBase
{
    private static readonly Color Yellow = Color.FromRgb(0xFF, 0xC4, 0x00), Green = Color.FromRgb(0x3D, 0xDC, 0x97),
        Orange = Color.FromRgb(0xFF, 0xB0, 0x2E), Red = Color.FromRgb(0xFF, 0x5D, 0x5D), Blue = Color.FromRgb(0x4F, 0xC3, 0xFF);

    private static readonly (string Key, string Title, string Glyph)[] Sections =
    {
        ("overview", "Visão geral", ""),
        ("dns", "DNS", ""),
        ("lan", "Rede local e conflitos", ""),
        ("reports", "Relatórios e histórico", ""),
        ("repair", "Reparação", ""),
        ("route", "Rotas (traceroute)", ""),
        ("wifi", "Wi-Fi", ""),
    };

    private readonly StackPanel _nav = new() { Width = 210, Margin = new Thickness(0, 0, 14, 0) };
    private readonly ContentControl _content = new();
    private readonly TextBlock _last = new() { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    private string _section = "overview";
    private string _busyText = "";
    private CancellationTokenSource? _cts;

    private NetDiagReport? _diag;
    private WifiScan? _wifi;
    private WifiAnalysis? _wifiA;

    public NetCenterPage() : base("Central de Rede", "Diagnóstico, rotas, Wi-Fi, DNS, rede local e reparação, numa tela só.")
    {
        HideHeader();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(Btn("Executar diagnóstico completo", async () => await RunFullAsync(), true));
        actions.Children.Add(_last);
        DockPanel.SetDock(actions, Dock.Right);
        head.Children.Add(actions);
        var title = new StackPanel();
        title.Children.Add(new TextBlock { Text = "Central de Rede", FontSize = 24, FontWeight = FontWeights.Bold });
        title.Children.Add(Muted("Diagnóstico · Análise · Correção de rede. Só lê a sua rede; correção só quando você confirmar.", 12.5));
        head.Children.Add(title);
        Body.Children.Add(head);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(_nav);
        Grid.SetColumn(_content, 1);
        grid.Children.Add(_content);
        Body.Children.Add(grid);

        BuildNav();
        Loaded += (_, _) =>
        {
            Show(_section);
            if (_diag is null) { _diag = NetHistory.List(1).Select(x => x.Report).FirstOrDefault(); UpdateLast(); Show(_section); }
        };
        Unloaded += (_, _) => _cts?.Cancel();
    }

    // ---------------------------------------------------------------- navegação

    private void BuildNav()
    {
        _nav.Children.Clear();
        foreach (var (key, text, glyph) in Sections)
        {
            bool on = key == _section;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14, Width = 24, VerticalAlignment = VerticalAlignment.Center,
                Foreground = on ? Brushes.Black : new SolidColorBrush(Yellow) });
            row.Children.Add(new TextBlock { Text = text, FontSize = 13.5, FontWeight = on ? FontWeights.Bold : FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            var b = new Button
            {
                Content = row, Style = (Style)FindResource(on ? "PrimaryButton" : "GhostButton"), HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 6),
            };
            string k = key;
            b.Click += (_, _) => Show(k);
            _nav.Children.Add(b);
        }
        var foot = new Border { Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(12), CornerRadius = new CornerRadius(10), Background = Brush("Surface2Brush"),
            BorderBrush = Brush("BorderBrush"), BorderThickness = new Thickness(1) };
        var fs = new StackPanel();
        var p = _diag?.Evidence.Primary;
        fs.Children.Add(new TextBlock { Text = p is null ? "Rede: não testada" : "Rede: " + (p.Up ? "conectada" : "desligada"), FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(p is { Up: true } ? Green : Orange) });
        if (p is not null)
        {
            fs.Children.Add(Muted("IP: " + string.Join(", ", p.Ipv4.Take(1)), 11.5));
            fs.Children.Add(Muted("Roteador: " + string.Join(", ", p.Gateways.Take(1)), 11.5));
            fs.Children.Add(Muted("DNS: " + string.Join(" / ", p.Dns.Take(2)), 11.5));
        }
        foot.Child = fs;
        _nav.Children.Add(foot);
    }

    private void Show(string key)
    {
        _section = key;
        BuildNav();
        var page = new StackPanel();
        if (_busyText.Length > 0)
        {
            page.Children.Add(new TextBlock { Text = _busyText, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Yellow), Margin = new Thickness(0, 0, 0, 4) });
            page.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 5, Margin = new Thickness(0, 0, 0, 12), Foreground = new SolidColorBrush(Yellow), Background = Brush("Surface2Brush"), BorderThickness = new Thickness(0) });
        }
        page.Children.Add(key switch
        {
            "dns" => DnsSection(),
            "lan" => LanSection(),
            "reports" => ReportsSection(),
            "repair" => RepairSection(),
            "route" => RouteSection(),
            "wifi" => WifiSection(),
            _ => OverviewSection(),
        });
        _content.Content = page;
    }

    private void UpdateLast() => _last.Text = _diag is null ? "Ainda sem diagnóstico" : $"Última análise: {_diag.FinishedUtc.ToLocalTime():dd/MM/yyyy 'às' HH:mm}";

    private async Task Busy(string text, Func<CancellationToken, Task> work)
    {
        if (_busyText.Length > 0) return;
        _cts = new CancellationTokenSource();
        _busyText = text;
        Show(_section);
        try { await work(_cts.Token); }
        catch (OperationCanceledException) { SetStatus("Parado.", "WarnBrush"); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Net.Sockets.SocketException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            SetStatus("Não foi possível concluir: " + ex.Message, "BadBrush");
        }
        finally
        {
            _busyText = "";
            if (IsLoaded) Show(_section);
        }
    }

    private async Task RunFullAsync() => await Busy("Diagnosticando: PC, roteador, internet, DNS e caminho...", async ct =>
    {
        var progress = new Progress<string>(s => { _busyText = s; if (IsLoaded) Show(_section); });
        _diag = await Task.Run(() => new NetDiagRunner(new SystemNetProbe()).RunAsync(new NetDiagOptions(), progress, ct), ct);
        if (_diag.Evidence.Primary is { IsWifi: true }) await LoadWifiAsync();
        NetHistory.Save(_diag);
        UpdateLast();
    });

    // ---------------------------------------------------------------- peças visuais

    private Button Btn(string text, Func<Task> click, bool primary = false)
    {
        var b = new Button { Content = text, Style = (Style)FindResource(primary ? "PrimaryButton" : "GhostButton"), Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0) };
        b.Click += async (_, _) => await click();
        return b;
    }

    private Border Panel(string title, UIElement body, string? glyph = null, UIElement? right = null)
    {
        var st = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        if (right is not null) { DockPanel.SetDock(right, Dock.Right); head.Children.Add(right); }
        var t = new StackPanel { Orientation = Orientation.Horizontal };
        if (glyph is not null)
            t.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 15, Foreground = new SolidColorBrush(Yellow), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        t.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(t);
        st.Children.Add(head);
        st.Children.Add(body);
        var card = CardOf(st);
        card.Margin = new Thickness(0, 0, 0, 12);
        return card;
    }

    private Border Tile(string label, string value, string sub, Color color, string? glyph = null)
    {
        var st = new StackPanel();
        var top = new StackPanel { Orientation = Orientation.Horizontal };
        if (glyph is not null)
            top.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 16, Foreground = new SolidColorBrush(Yellow), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        top.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Brush("MutedBrush"), VerticalAlignment = VerticalAlignment.Center });
        st.Children.Add(top);
        st.Children.Add(new TextBlock { Text = value, FontSize = 21, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        st.Children.Add(new TextBlock { Text = sub, FontSize = 11.5, Foreground = new SolidColorBrush(color), TextWrapping = TextWrapping.Wrap });
        return new Border
        {
            Child = st, Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 10, 10), CornerRadius = new CornerRadius(12), MinWidth = 180,
            Background = Brush("Surface2Brush"), BorderBrush = Brush("BorderBrush"), BorderThickness = new Thickness(1),
        };
    }

    private static Border Pill(string text, Color color) => new()
    {
        CornerRadius = new CornerRadius(999), Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        BorderBrush = new SolidColorBrush(color), BorderThickness = new Thickness(1), Background = new SolidColorBrush(Color.FromArgb(0x22, color.R, color.G, color.B)),
        Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(color) },
    };

    private static Color SevColor(FindingSeverity s) => s switch { FindingSeverity.Problema => Red, FindingSeverity.Atencao => Orange, _ => Green };

    private Border FindingRow(Finding f)
    {
        var st = new StackPanel();
        var top = new WrapPanel();
        top.Children.Add(new TextBlock { Text = f.Title, FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        top.Children.Add(Pill(f.Status switch { FindingStatus.Confirmado => "Confirmado", FindingStatus.Provavel => "Provável", FindingStatus.Indeterminado => "Incerto", _ => "Não testado" }, SevColor(f.Severity)));
        st.Children.Add(top);
        foreach (string ev in f.Evidence.Take(2)) st.Children.Add(Muted("• " + ev, 11.5));
        if (f.Counter.Count > 0) st.Children.Add(new TextBlock { Text = "Pode não ser isso: " + string.Join(" ", f.Counter), FontSize = 11, FontStyle = FontStyles.Italic, Foreground = Brush("MutedBrush"), TextWrapping = TextWrapping.Wrap });
        st.Children.Add(new TextBlock { Text = "O que fazer: " + f.Next, FontSize = 12, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
        return new Border
        {
            Child = st, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 6), CornerRadius = new CornerRadius(10), Background = Brush("Surface2Brush"),
            BorderBrush = new SolidColorBrush(SevColor(f.Severity)), BorderThickness = new Thickness(4, 1, 1, 1),
        };
    }

    /// <summary>Gráfico de linha simples (valores na ordem), com eixo à esquerda e área preenchida.</summary>
    private Viewbox LineChart(IReadOnlyList<double> values, double max, Color color, string unit, IReadOnlyList<string>? labels = null)
    {
        const double W = 560, H = 160, L = 40, B = 22, T = 8;
        var c = new Canvas { Width = W, Height = H };
        max = Math.Max(max, 1);
        for (int i = 0; i <= 4; i++)
        {
            double v = max * i / 4, y = H - B - (H - B - T) * i / 4;
            c.Children.Add(new Line { X1 = L, X2 = W, Y1 = y, Y2 = y, Stroke = new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255)), StrokeThickness = 1 });
            var tb = new TextBlock { Text = v.ToString(v < 10 ? "0.#" : "0") + (i == 4 ? " " + unit : ""), FontSize = 10, Foreground = Brush("MutedBrush") };
            Canvas.SetLeft(tb, 0); Canvas.SetTop(tb, y - 7); c.Children.Add(tb);
        }
        if (values.Count > 0)
        {
            double X(int i) => values.Count == 1 ? (L + W) / 2 : L + 8 + (W - L - 16) * i / (values.Count - 1);
            double Y(double v) => H - B - (H - B - T) * Math.Min(v, max) / max;
            var line = new Polyline { Stroke = new SolidColorBrush(color), StrokeThickness = 2.5 };
            var area = new Polygon { Fill = new SolidColorBrush(Color.FromArgb(0x33, color.R, color.G, color.B)) };
            area.Points.Add(new Point(X(0), H - B));
            for (int i = 0; i < values.Count; i++)
            {
                var pt = new Point(X(i), Y(values[i]));
                line.Points.Add(pt); area.Points.Add(pt);
                var dot = new Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(color) };
                Canvas.SetLeft(dot, pt.X - 3.5); Canvas.SetTop(dot, pt.Y - 3.5); c.Children.Add(dot);
                if (labels is not null && i < labels.Count && (values.Count <= 16 || i % 2 == 0))
                {
                    var lb = new TextBlock { Text = labels[i], FontSize = 10, Foreground = Brush("MutedBrush") };
                    Canvas.SetLeft(lb, pt.X - 4); Canvas.SetTop(lb, H - B + 4); c.Children.Add(lb);
                }
            }
            area.Points.Add(new Point(X(values.Count - 1), H - B));
            c.Children.Insert(0, area);
            c.Children.Add(line);
        }
        return new Viewbox { Child = c, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, MaxHeight = 220, HorizontalAlignment = HorizontalAlignment.Left };
    }

    private static WrapPanel Wrap(params UIElement[] items)
    {
        var w = new WrapPanel();
        foreach (var i in items) w.Children.Add(i);
        return w;
    }

    private void OpenHtml(string html, string name)
    {
        try
        {
            string folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MB Extreme", "Diagnósticos");
            Directory.CreateDirectory(folder);
            string file = System.IO.Path.Combine(folder, name + ".html");
            File.WriteAllText(file, html);
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
            SetStatus("Relatório salvo em Documentos › MB Extreme › Diagnósticos.", "OkBrush");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            SetStatus("Não foi possível salvar o relatório: " + ex.Message, "BadBrush");
        }
    }

    // ---------------------------------------------------------------- Visão geral

    private UIElement OverviewSection()
    {
        var page = new StackPanel();
        page.Children.Add(new TextBlock { Text = "Visão geral da rede", FontSize = 19, FontWeight = FontWeights.Bold });
        page.Children.Add(Muted("Status da conexão, informações da placa e diagnóstico resumido.", 12.5));
        page.Children.Add(new Border { Height = 10 });
        if (_diag is not { } r)
        {
            page.Children.Add(Panel("Ainda sem diagnóstico", new StackPanel
            {
                Children = { Muted("Clique em \"Executar diagnóstico completo\". Leva uns 30 a 60 segundos e não muda nada na rede.", 12.5), Wrap(Btn("Executar agora", async () => await RunFullAsync(), true)) },
            }, ""));
            return page;
        }
        var o = NetOverviewRules.From(r, _wifiA);
        var e = r.Evidence;
        Color main = SevColor(o.Level);

        // faixa de status
        var banner = new Grid();
        banner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        banner.ColumnDefinitions.Add(new ColumnDefinition());
        banner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        banner.Children.Add(new Border
        {
            Width = 54, Height = 54, CornerRadius = new CornerRadius(27), Background = new SolidColorBrush(main), Margin = new Thickness(0, 0, 14, 0),
            Child = new TextBlock { Text = o.Level == FindingSeverity.Info ? "" : "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 24, Foreground = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        });
        var bt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        bt.Children.Add(new TextBlock { Text = o.Headline, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(main) });
        bt.Children.Add(new TextBlock { Text = o.Sub, FontSize = 13, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(bt, 1); banner.Children.Add(bt);
        var q = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
        q.Children.Add(Muted("Qualidade da conexão", 12));
        q.Children.Add(new TextBlock { Text = $"{o.QualityText} ({o.Quality}%)", FontSize = 20, FontWeight = FontWeights.Bold });
        Grid.SetColumn(q, 2); banner.Children.Add(q);
        page.Children.Add(new Border
        {
            Child = banner, Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(0, 0, 0, 12), CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(0x22, main.R, main.G, main.B)), BorderBrush = new SolidColorBrush(main), BorderThickness = new Thickness(1.5),
        });

        // placa, IP, roteador e DNS
        var p = e.Primary;
        page.Children.Add(Wrap(
            Tile("Placa de rede", p?.Kind ?? "—", p is null ? "sem placa ativa" : (p.Up ? "Conectada" : "Desligada") + (p.SpeedBps > 0 ? $" · {p.SpeedBps / 1_000_000} Mbps" : ""), p is { Up: true } ? Green : Red, ""),
            Tile("Endereço IP", p?.Ipv4.FirstOrDefault() ?? "—", p is null ? "" : "IPv4 · " + (p.Dhcp ? "DHCP" : "IP fixo"), Brush2(), ""),
            Tile("Roteador (gateway)", e.GatewayAddress ?? "—", e.Gateway is { } g ? (g.AllLost ? "sem resposta" : $"{g.Median:0.#} ms") : "não testado", e.Gateway is { AllLost: false } ? Green : Orange, ""),
            Tile("Servidores DNS", string.Join(" · ", p?.Dns.Take(2) ?? Array.Empty<string>()), e.DnsSystem.All(d => d.Ok) ? "respondendo" : "com falha", e.DnsSystem.All(d => d.Ok) ? Green : Red, "")));

        // latência, perda, variação e velocidade
        var ext = e.External.Values.Where(v => !v.AllLost).ToList();
        page.Children.Add(Wrap(
            Tile("Latência (ping)", ext.Count > 0 ? $"{ext.Min(v => v.Median):0} ms" : "—", ext.Count > 0 ? $"mediana de {ext.Sum(v => v.Sent)} testes" : "sem resposta", ext.Count > 0 && ext.Min(v => v.Median) < 60 ? Green : Orange, ""),
            Tile("Perda de pacotes", ext.Count > 0 ? $"{ext.Max(v => v.LossPct):0.#} %" : "—", ext.Count > 0 ? $"({ext.Sum(v => v.Sent - v.Received)} de {ext.Sum(v => v.Sent)})" : "", ext.Count > 0 && ext.Max(v => v.LossPct) < 1 ? Green : Orange, ""),
            Tile("Variação (jitter)", ext.Count > 0 ? $"{ext.Max(v => v.Jitter ?? 0):0.#} ms" : "—", "variação da latência", ext.Count > 0 && ext.Max(v => v.Jitter ?? 0) < 15 ? Green : Orange, ""),
            Tile("Velocidade", "Testar", "download e upload no Teste de internet", Blue, "")));

        // resumo + conclusão
        var rows = new StackPanel();
        foreach (var row in o.Rows)
        {
            var line = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            Color c = row.State is "Não aplicável" or "Não testado" ? Color.FromRgb(0x8E, 0x95, 0xA1) : SevColor(row.Level);
            var icon = new TextBlock { Text = row.Level == FindingSeverity.Info ? "" : "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13, Width = 24, Foreground = new SolidColorBrush(c), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(icon, Dock.Left);
            line.Children.Add(icon);
            var area = new TextBlock { Text = row.Area + ":", Width = 170, FontSize = 13, FontWeight = FontWeights.SemiBold };
            DockPanel.SetDock(area, Dock.Left);
            line.Children.Add(area);
            var txt = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap };
            txt.Inlines.Add(new System.Windows.Documents.Run(row.State) { Foreground = new SolidColorBrush(c), FontWeight = FontWeights.Bold });
            txt.Inlines.Add(new System.Windows.Documents.Run(" – " + row.Text));
            line.Children.Add(txt);
            rows.Children.Add(line);
        }
        var concl = new StackPanel();
        concl.Children.Add(new Border
        {
            Padding = new Thickness(12, 8, 12, 8), CornerRadius = new CornerRadius(10), BorderBrush = new SolidColorBrush(main), BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(0x18, main.R, main.G, main.B)),
            Child = new TextBlock { Text = o.Level == FindingSeverity.Info ? "Nenhum problema crítico detectado." : r.Findings.OrderByDescending(f => f.Severity).First().Title, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(main), TextWrapping = TextWrapping.Wrap },
        });
        concl.Children.Add(new TextBlock { Text = "Recomendações", FontSize = 13.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) });
        foreach (string rec in o.Recommendations) concl.Children.Add(new TextBlock { Text = "• " + rec, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1) });
        var two = new Grid();
        two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        two.ColumnDefinitions.Add(new ColumnDefinition());
        two.Children.Add(Panel("Resumo do diagnóstico", rows, ""));
        var cp = Panel("Conclusão", concl, "");
        Grid.SetColumn(cp, 2); two.Children.Add(cp);
        page.Children.Add(two);

        // ações rápidas
        page.Children.Add(Panel("Ações rápidas", Wrap(
            Btn("Teste de velocidade", () => { MainWindow.Current?.Go("speedtest"); return Task.CompletedTask; }),
            Btn("Traçar rota", () => { Show("route"); return Task.CompletedTask; }),
            Btn("Ver dispositivos da rede", () => { Show("lan"); return Task.CompletedTask; }),
            Btn("Limpar cache DNS", async () => await RepairAsync(RepairKind.FlushDns)),
            Btn("Reparar rede", () => { Show("repair"); return Task.CompletedTask; })), ""));
        return page;
    }

    private static Color Brush2() => Color.FromRgb(0x8E, 0x95, 0xA1);
}
