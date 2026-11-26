using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MBExtreme.Core.Licensing;
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
        // Visão geral primeiro; o resto em ordem alfabética. Tudo de rede do programa fica aqui dentro.
        ("overview", "Visão geral", ""),
        ("netanalyzer", "Analisador (ping e MTR)", "\uE9D9"),
        ("dns", "DNS", ""),
        ("net-tweaks", "Otimizações de rede", "\uE713"),
        ("games", "Ping dos jogos", "\uE7FC"),
        ("lan", "Rede local e conflitos", ""),
        ("reports", "Relatórios e histórico", ""),
        ("repair", "Reparação", ""),
        ("route", "Rotas (traceroute)", ""),
        ("speedtest", "Teste de internet", "\uEC4A"),
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
        // topo como na prévia: logo MB, "Central de Rede" com "Rede" em amarelo, linha AO VIVO e os quatro botões
        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        var brand = new StackPanel { Orientation = Orientation.Horizontal };
        brand.Children.Add(new Border
        {
            Width = 44, Height = 44, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Yellow), Margin = new Thickness(0, 0, 12, 0),
            Child = new TextBlock { Text = "MB", FontSize = 17, FontWeight = FontWeights.Black, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        });
        var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var t1 = new TextBlock { FontSize = 25, FontWeight = FontWeights.Bold };
        t1.Inlines.Add(new System.Windows.Documents.Run("Central de "));
        t1.Inlines.Add(new System.Windows.Documents.Run("Rede") { Foreground = new SolidColorBrush(Yellow) });
        title.Children.Add(t1);
        _liveLine.FontFamily = new FontFamily("Consolas");
        _liveLine.FontSize = 11.5;
        _liveLine.FontWeight = FontWeights.Bold;
        title.Children.Add(_liveLine);
        brand.Children.Add(title);
        head.Children.Add(brand);
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        actions.Children.Add(Btn("Diagnosticar agora", async () => await RunFullAsync(), true));
        _monitorBtn = Btn("Monitorar 1 hora", () => { ToggleMonitor(); return Task.CompletedTask; });
        actions.Children.Add(_monitorBtn);
        actions.Children.Add(Btn("Corrigir com segurança", () => { Show("repair"); return Task.CompletedTask; }));
        actions.Children.Add(Btn("Relatório", () => { Show("reports"); return Task.CompletedTask; }));
        _last.Margin = new Thickness(4, 0, 0, 0);
        actions.Children.Add(_last);
        head.Children.Add(actions);
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
            if (_diag is null) { _diag = NetHistory.List(1).Select(x => x.Report).FirstOrDefault(); UpdateLast(); }
            Show(_section);
            StartLive();
        };
        Unloaded += (_, _) => { _cts?.Cancel(); if (!Monitoring) StopLive(); };
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
            if (!Has(NetCenterAccess.SectionPlan(key)))
                row.Children.Add(new TextBlock { Text = "\uE72E", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 11, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                    Foreground = on ? Brushes.Black : new SolidColorBrush(Yellow), ToolTip = "Plano " + FeaturePolicy.Title(NetCenterAccess.SectionPlan(key)) });
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

    /// <summary>Abre uma seção (usado quando outra tela pede uma ferramenta de rede: DNS, Teste de internet, Wi-Fi...).</summary>
    internal void Open(string key)
    {
        if (Sections.Any(x => x.Key == key)) Show(key);
    }

    /// <summary>Ferramentas que já existiam no programa e agora abrem aqui dentro (criadas uma vez só).</summary>
    private readonly Dictionary<string, UIElement> _embedded = new();

    private UIElement Embedded(string key)
    {
        if (!_embedded.TryGetValue(key, out var page))
        {
            page = key switch
            {
                "speedtest" => new InternetTestPage("speed", "history"),
                "games" => new InternetTestPage("games"),
                _ => MainWindow.CreateInner(key, NavigationMap.TileOf(key)),
            };
            _embedded[key] = page;
        }
        if (page is FrameworkElement { Parent: ContentControl old }) old.Content = null;
        return new ContentControl { Content = page };
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
        page.Children.Add(NetCenterAccess.SectionPlan(key) is { } need && !Has(need) ? LockedSection(Sections.First(x => x.Key == key).Title, need) : key switch
        {
            "dns" => DnsSection(),
            "games" or "net-tweaks" or "netanalyzer" or "speedtest" => Embedded(key),
            "lan" => LanSection(),
            "reports" => ReportsSection(),
            "repair" => RepairSection(),
            "route" => RouteSection(),
            "wifi" => WifiSection(),
            _ => LiveSection(),
        });
        _content.Content = page;
    }

    // ---------------------------------------------------------------- planos

    /// <summary>O plano de agora libera o recurso? (no beta aberto e no teste grátis, tudo.)</summary>
    private static bool Has(string? need) => NetCenterAccess.Allows(AppServices.License.EffectivePlan, need);

    private static string PlanText(string need) => need == FeaturePolicy.Tecnico ? "do plano Técnico" : "dos planos pagos (Essencial, Gamer, Creator e Técnico)";

    /// <summary>Aviso de recurso de outro plano, com o botão para ver os planos.</summary>
    private Border Locked(string title, string need, string what) => Panel(title, new StackPanel
    {
        Children =
        {
            new TextBlock { Text = "Recurso " + PlanText(need) + ".", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Yellow), TextWrapping = TextWrapping.Wrap },
            Muted(what, 12.5),
            Wrap(Btn("Ver planos", () => { MainWindow.Current?.Go("license"); return Task.CompletedTask; }, true)),
        },
    }, "\uE72E");

    private UIElement LockedSection(string title, string need)
    {
        var page = new StackPanel();
        page.Children.Add(new TextBlock { Text = title, FontSize = 19, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10) });
        page.Children.Add(Locked("Liberado em outro plano", need, need == FeaturePolicy.Tecnico
            ? "Rotas, rede local, conflitos de IP, Wi-Fi completo, Winsock, TCP/IP e relatórios para o cliente ficam no Técnico, para até 2 PCs."
            : "No plano grátis você tem a Visão geral e o Teste de internet. Assine para trocar o DNS, analisar o Wi-Fi e reparar a rede."));
        return page;
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
        LogDiag(_diag);
        UpdateLast();
        UpdateLiveLine();
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

    private static Color Brush2() => Color.FromRgb(0x8E, 0x95, 0xA1);
}
