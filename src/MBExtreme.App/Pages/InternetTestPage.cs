using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MBExtreme.Core.Network;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Teste de internet: latência e variação, download e upload com várias conexões e aquecimento, o servidor usado e a cidade,
/// e o histórico para comparar. Usa o servidor público de velocidade da Cloudflare.
/// </summary>
public sealed class InternetTestPage : ToolPageBase
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly SpeedHistory _history = new();
    private readonly TextBlock _live = new() { FontSize = 30, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _stage = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap };
    private readonly Button _start;
    private CancellationTokenSource? _cts;
    private int _connections = 1;
    private readonly TextBox _name = new() { Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 4, 0, 10) };
    private readonly UniformGrid _steps = new() { Columns = 6, Margin = new Thickness(0, 6, 0, 6) };
    private readonly Dictionary<string, TextBlock> _stepValues = new();

    private static readonly int[] Connections = { 1, 4, 8 };
    private readonly MBExtreme.UI.Controls.SpeedGauge _gauge = new() { Width = 340, Height = 340 };
    private readonly Grid _gaugeHost = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) };
    private readonly TextBlock _downBig = new() { Text = "—", FontSize = 34, FontWeight = FontWeights.Light, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _upBig = new() { Text = "—", FontSize = 34, FontWeight = FontWeights.Light, HorizontalAlignment = HorizontalAlignment.Center };
    private Button? _go;

    /// <summary>Topo do teste, como os testes de velocidade conhecidos: download e upload grandes, o velocímetro e o botão redondo de começar.</summary>
    private UIElement GaugeCard()
    {
        var top = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 4), HorizontalAlignment = HorizontalAlignment.Center, Width = 420 };
        foreach (var (title, value, color) in new[] { ("↓ DOWNLOAD Mbps", _downBig, "#FFC400"), ("↑ UPLOAD Mbps", _upBig, "#B57CFF") })
        {
            var cell = new StackPanel();
            cell.Children.Add(new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)) });
            if (value.Parent is Panel old) old.Children.Remove(value);
            cell.Children.Add(value);
            top.Children.Add(cell);
        }
        if (_gauge.Parent is Panel g) g.Children.Remove(_gauge);
        _gaugeHost.Children.Clear();
        _gaugeHost.Children.Add(_gauge);
        _go = new Button { Width = 132, Height = 132, Cursor = System.Windows.Input.Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var t = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(66));
        border.SetValue(Border.BackgroundProperty, Application.Current.FindResource("ButtonBrush"));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        t.VisualTree = border;
        _go.Template = t;
        _go.Content = new TextBlock { Text = "INICIAR", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Brushes.Black };
        System.Windows.Automation.AutomationProperties.SetName(_go, "Iniciar o teste de internet");
        _go.Click += async (_, _) => await RunAsync();
        _gaugeHost.Children.Add(_go);
        _gauge.BeginAnimation(OpacityProperty, null);
        _gauge.Opacity = 0.35;
        Motion.Breathe(_go, 1.05, 1.5);
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        left.Children.Add(top);
        left.Children.Add(_gaugeHost);
        var right = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
        if (_map.Parent is Panel mp) mp.Children.Remove(_map);
        right.Children.Add(_map);
        if (_info.Parent is Panel ip) ip.Children.Remove(_info);
        right.Children.Add(_info);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        if (!_mapLoaded) { _mapLoaded = true; _ = LoadMapAsync(); }
        return grid;
    }

    private readonly MBExtreme.UI.Controls.NetMapControl _map = new() { Height = 300, MinWidth = 300 };
    private readonly StackPanel _info = new() { Margin = new Thickness(0, 10, 0, 0) };
    private MBExtreme.UI.Controls.MapLink? _mainLink;
    private GeoPoint? _colo;
    private bool _mapLoaded;
    private string? _publicIp, _isp, _city;

    /// <summary>Endereço IPv4 deste PC na rede de casa (o do adaptador que tem roteador).</summary>
    private static string? LocalIp()
    {
        try
        {
            foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                var props = nic.GetIPProperties();
                if (!props.GatewayAddresses.Any(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !g.Address.Equals(System.Net.IPAddress.Any))) continue;
                var v4 = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                if (v4 is not null) return v4.Address.ToString();
            }
        }
        catch (System.Net.NetworkInformation.NetworkInformationException) { }
        return null;
    }

    /// <summary>Acha você (cidade pelo IP público), o servidor da Cloudflare e liga os dois no mapa; preenche os dados da conexão.</summary>
    private async Task LoadMapAsync()
    {
        var test = new SpeedTest(Http);
        try
        {
            var trace = await test.TraceAsync(CancellationToken.None);
            _publicIp = trace.Ip;
            if (trace.Code is { } code && NetMap.Colos.TryGetValue(code, out var colo)) _colo = colo with { Name = $"{colo.Name} ({code})" };
            var where = await NetMap.LocateAsync(Http, CancellationToken.None);
            if (where is not null)
            {
                _isp = where.Isp;
                _city = string.Join(", ", new[] { where.City, where.Region }.Where(x => x.Length > 0));
                _publicIp ??= where.Ip;
                _map.User = new GeoPoint(where.City.Length > 0 ? where.City : "sua cidade", where.Lat, where.Lon);
            }
            else if (_colo is not null)
                _map.User = _colo with { Name = "perto de " + _colo.Name };   // sem localização: aproxima pelo servidor
            if (_colo is not null) _mainLink = _map.SetLink(_colo, MBExtreme.UI.Controls.LinkMode.Idle, MBExtreme.UI.Controls.NetMapControl.Download, "Servidor de teste · " + _colo.Name);
        }
        catch (HttpRequestException) { }
        FillInfo(null);
    }

    private void FillInfo(double? ping)
    {
        _info.Children.Clear();
        void Line(string label, string? value)
        {
            var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Brush("MutedBrush") });
            var v = new TextBlock { Text = string.IsNullOrWhiteSpace(value) ? "—" : value, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(v, 1);
            g.Children.Add(v);
            _info.Children.Add(g);
        }
        Line("Você", _city);
        Line("Provedor", _isp);
        Line("IP local (na sua rede)", LocalIp());
        Line("IP real (na internet)", _publicIp);
        Line("Servidor de teste", _colo is null ? null : "Cloudflare " + _colo.Name);
        if (ping is double p) Line("Ping até o servidor", $"{p:0} ms");
        _info.Children.Add(Muted("A cidade vem do seu IP público (é a do provedor, aproximada) e não é guardada. No mapa, os saltos aparecem ao longo da linha pelo tempo de resposta de cada roteador, não pela posição real dele.", 10.5));
    }

    /// <summary>Saltos até o servidor (uma rodada do traceroute) desenhados na linha do mapa.</summary>
    private async Task TraceToMapAsync(MBExtreme.UI.Controls.MapLink link, string host, CancellationToken ct)
    {
        using var round = CancellationTokenSource.CreateLinkedTokenSource(ct);
        IReadOnlyList<HopStats>? hops = null;
        try { await NetAnalyzer.TraceAsync(host, 20, h => { hops = h; round.Cancel(); }, round.Token); }
        catch (OperationCanceledException) { }
        catch (System.Net.NetworkInformation.PingException) { }
        if (hops is null || hops.Count == 0) return;
        var ms = hops.Select(h => h.Received > 0 ? (double?)h.Average : null).ToList();
        var pos = NetMap.HopPositions(ms);
        link.Hops.Clear();
        for (int i = 0; i < hops.Count; i++)
            link.Hops.Add((pos[i], $"{hops[i].Hop} · " + (ms[i] is double v ? $"{v:0} ms" : "*")));
        _map.InvalidateVisual();
    }

    // ---------------------------------------------------------------- servidores de jogos

    private string _group = "Brasil";
    private readonly HashSet<string> _picked = new() { "aws-sa-east-1" };
    private readonly StackPanel _gameList = new();
    private readonly StackPanel _gameResults = new();
    private static readonly Color[] PingColors = { Color.FromRgb(0xE8, 0xE8, 0xEC), Color.FromRgb(0x3F, 0xD0, 0xE0) };

    private UIElement GameCard()
    {
        var card = new StackPanel();
        card.Children.Add(new TextBlock { Text = "Ping para servidores de jogos", FontSize = 15, FontWeight = FontWeights.SemiBold });
        card.Children.Add(Muted("Escolha a região e marque até 2 servidores para testar ao mesmo tempo. São as regiões de nuvem (AWS) onde muitos jogos online ficam hospedados — São Paulo é a principal para quem joga no Brasil. Cada jogo tem os próprios servidores: isto mostra como está a rota até a região.", 11.5));
        var chips = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) };
        foreach (var g in NetMap.Groups)
        {
            string group = g;
            var b = new Button { Content = g, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(12, 4, 12, 4), FontSize = 12,
                Style = (Style)Application.Current.FindResource(g == _group ? "PrimaryButton" : "GhostButton") };
            b.Click += (_, _) => { _group = group; Rebuild(); };
            chips.Children.Add(b);
        }
        card.Children.Add(chips);
        if (_gameList.Parent is Panel gl) gl.Children.Remove(_gameList);
        card.Children.Add(_gameList);
        FillGameList();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
        buttons.Children.Add(SmallButton("Testar os marcados", async (_, _) => await PingGamesAsync(NetMap.GameTargets.Where(t => _picked.Contains(t.Key)).ToList()), true));
        buttons.Children.Add(SmallButton("Testar todos desta região", async (_, _) => await PingGamesAsync(NetMap.GameTargets.Where(t => t.Group == _group).ToList())));
        card.Children.Add(buttons);
        if (_gameResults.Parent is Panel gr) gr.Children.Remove(_gameResults);
        card.Children.Add(_gameResults);
        return CardOf(card);
    }

    private void Rebuild() => Build(_lastResult);
    private SpeedResult? _lastResult;

    private void FillGameList()
    {
        _gameList.Children.Clear();
        foreach (var t in NetMap.GameTargets.Where(t => t.Group == _group))
        {
            var target = t;
            var box = new CheckBox
            {
                Content = t.Name + (t.Note.Length > 0 ? "  ·  " + t.Note : ""), IsChecked = _picked.Contains(t.Key), Foreground = Brush("TextBrush"), Margin = new Thickness(0, 2, 0, 2),
            };
            box.Click += (_, _) =>
            {
                if (box.IsChecked == true)
                {
                    if (_picked.Count >= 2) { box.IsChecked = false; SetStatus("Até 2 servidores ao mesmo tempo. Desmarque um para escolher outro.", "WarnBrush"); return; }
                    _picked.Add(target.Key);
                }
                else _picked.Remove(target.Key);
            };
            _gameList.Children.Add(box);
        }
    }

    private async Task PingGamesAsync(IReadOnlyList<PingTarget> targets)
    {
        if (targets.Count == 0) { SetStatus("Marque pelo menos um servidor.", "WarnBrush"); return; }
        _gameResults.Children.Clear();
        _gameResults.Children.Add(Muted($"Medindo {targets.Count} servidor(es) ao mesmo tempo (8 tentativas cada)...", 12));
        // no mapa: os servidores com posição; até 2 com linha colorida e as bolinhas de ping
        _map.ClearLinks();
        if (_colo is not null) _mainLink = _map.SetLink(_colo, MBExtreme.UI.Controls.LinkMode.Idle, MBExtreme.UI.Controls.NetMapControl.Download, "Servidor de teste · " + _colo.Name);
        var links = new Dictionary<string, MBExtreme.UI.Controls.MapLink>();
        int color = 0;
        foreach (var t in targets.Where(t => !double.IsNaN(t.Lat)))
            links[t.Key] = _map.SetLink(new GeoPoint(t.Name, t.Lat, t.Lon), MBExtreme.UI.Controls.LinkMode.Ping, PingColors[Math.Min(color++, PingColors.Length - 1)], t.Name);
        var traces = targets.Where(t => links.ContainsKey(t.Key)).Take(2).Select(t => TraceToMapAsync(links[t.Key], t.Host, CancellationToken.None)).ToList();
        var results = await Task.WhenAll(targets.Select(t => NetMap.PingAsync(t, 8, CancellationToken.None)));
        await Task.WhenAll(traces);
        _gameResults.Children.Clear();
        foreach (var r in results.OrderBy(r => r.Median ?? double.MaxValue))
        {
            string brush = r.Median switch { null => "BadBrush", < 50 => "OkBrush", < 90 => "WarnBrush", _ => "BadBrush" };
            _gameResults.Children.Add(Row($"{r.Target.Name}:  {(r.Median is double m ? $"{m:0} ms" : "sem resposta")}",
                $"{NetMap.GameVerdict(r.Median)} · variação {(r.Jitter is double j ? $"{j:0} ms" : "—")} · perdidos {r.Lost} de {r.Sent} · {r.Target.Host}", null, brush));
            if (links.TryGetValue(r.Target.Key, out var link))
            {
                link.Mode = MBExtreme.UI.Controls.LinkMode.Idle;
                link.Label = $"{r.Target.Name} · {(r.Median is double mm ? $"{mm:0} ms" : "sem resposta")}";
            }
        }
        _map.InvalidateVisual();
        SetStatus("Ping dos servidores medido.", "OkBrush");
    }

    // ---------------------------------------------------------------- DNS

    private readonly StackPanel _dnsResults = new();

    private UIElement DnsCard()
    {
        var card = new StackPanel();
        card.Children.Add(new TextBlock { Text = "Qual DNS está mais rápido agora", FontSize = 15, FontWeight = FontWeights.SemiBold });
        card.Children.Add(Muted("O DNS transforma nomes (google.com) em endereços. Um DNS lento atrasa o começo de cada site e jogo. O teste compara o DNS que você usa hoje com os públicos mais conhecidos.", 11.5));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 6) };
        buttons.Children.Add(SmallButton("Testar DNS agora", async (_, _) => await DnsAsync(), true));
        buttons.Children.Add(SmallButton("Trocar o DNS", (_, _) => MainWindow.Current?.Go("dns")));
        card.Children.Add(buttons);
        if (_dnsResults.Parent is Panel dp) dp.Children.Remove(_dnsResults);
        card.Children.Add(_dnsResults);
        return CardOf(card);
    }

    private async Task DnsAsync()
    {
        _dnsResults.Children.Clear();
        _dnsResults.Children.Add(Muted("Testando (cada DNS responde a 5 sites, 2 vezes)...", 12));
        var adapter = _adapter ?? await Task.Run(NetworkStatus.Active);
        var current = adapter?.Dns.FirstOrDefault(d => System.Net.IPAddress.TryParse(d, out var a) && a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
        var providers = new List<MBExtreme.Core.Dns.DnsProvider>();
        if (current is not null && !MBExtreme.Core.Dns.DnsProviders.Defaults.Any(p => p.Primary == current))
            providers.Add(new MBExtreme.Core.Dns.DnsProvider("O seu DNS atual", current, "", current));
        providers.AddRange(MBExtreme.Core.Dns.DnsProviders.Defaults);
        var results = await MBExtreme.Core.Dns.DnsBenchmark.RunAsync(providers, MBExtreme.Core.Dns.DnsBenchmark.DefaultDomains, 2, TimeSpan.FromSeconds(1.5));
        _dnsResults.Children.Clear();
        var ranked = results.OrderBy(r => r.AverageMs ?? double.MaxValue).ToList();
        bool first = true;
        foreach (var r in ranked)
        {
            bool inUse = r.Provider.Primary == current;
            string title = $"{(first && r.AverageMs is not null ? "🏆 " : "")}{r.Provider.Name}  ·  {(r.AverageMs is double ms ? $"{ms:0} ms" : "sem resposta")}{(inUse ? "   (em uso)" : "")}";
            _dnsResults.Children.Add(Row(title, $"{r.Provider.Primary} · respondeu {r.Successes} de {r.Attempts}" + (r.Provider.Note.Length > 0 && r.Provider.Note != r.Provider.Primary ? " · " + r.Provider.Note : ""),
                null, first ? "OkBrush" : "MutedBrush"));
            first = false;
        }
        var best = ranked.FirstOrDefault(r => r.AverageMs is not null);
        var mine = ranked.FirstOrDefault(r => r.Provider.Primary == current);
        if (best is not null && mine?.AverageMs is double now && best.AverageMs is double fast)
            _dnsResults.Children.Add(Muted(best.Provider.Primary == current ? "O DNS que você usa já é o mais rápido agora." :
                $"Trocando para {best.Provider.Name}, cada nome resolve uns {now - fast:0} ms mais rápido. Use \"Trocar o DNS\" se quiser.", 12));
    }

    /// <summary>Some com o botão e acende o velocímetro (o teste começou).</summary>
    private void GaugeRunning()
    {
        if (_go is not null) { Motion.Stop(_go); _gaugeHost.Children.Remove(_go); _go = null; }
        _gauge.Reset();
        Motion.To(_gauge, OpacityProperty, 1, Motion.Normal);
    }

    public InternetTestPage() : base("Teste de internet",
        "Mede a latência, a variação, o download e o upload com várias conexões ao mesmo tempo, descartando os primeiros segundos (aquecimento). " +
        "O servidor é o teste público da Cloudflare, perto de você; os números podem diferir do Speedtest, que usa outros servidores e outro método.")
    {
        _start = AddButton("Testar", async (_, _) => await RunAsync());
        AddButton("Parar", (_, _) => _cts?.Cancel(), false);
        Loaded += (_, _) => { if (Body.Children.Count == 0) Build(null); };
    }

    private void Build(SpeedResult? result)
    {
        Body.Children.Clear();
        Body.Children.Add(GaugeCard());
        if (result is not null) { _downBig.Text = Fmt(result.DownloadMbps).Replace(" Mbps", ""); _upBig.Text = Fmt(result.UploadMbps).Replace(" Mbps", ""); }
        if (_adapter is not null)
            Body.Children.Add(Muted($"Conectado por {_adapter.Name}" + (_adapter.LinkSpeedBitsPerSecond > 0 ? $" · link de {_adapter.LinkSpeedBitsPerSecond / 1_000_000} Mbps" : "")
                + (_adapter.Gateway is null ? "" : $" · roteador {_adapter.Gateway}") + (_adapter.Dns.Count > 0 ? $" · DNS {string.Join(", ", _adapter.Dns.Take(2))}" : ""), 12));
        Body.Children.Add(Muted("Nome do teste (opcional, para comparar depois. Ex.: antes de trocar o roteador):", 12));
        System.Windows.Automation.AutomationProperties.SetName(_name, "Nome do teste");
        if (_name.Parent is Panel p) p.Children.Remove(_name);
        Body.Children.Add(_name);
        _steps.Children.Clear();
        _stepValues.Clear();
        foreach (var (key, label) in new[] { ("router", "Roteador"), ("ping", "Ping"), ("dns", "DNS"), ("down", "Download"), ("up", "Upload"), ("load", "Sob carga") })
        {
            var value = new TextBlock { Text = "—", FontSize = 18, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
            _stepValues[key] = value;
            var cell = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            cell.Children.Add(new TextBlock { Text = label, FontSize = 11.5, Foreground = Brush("MutedBrush"), HorizontalAlignment = HorizontalAlignment.Center });
            cell.Children.Add(value);
            _steps.Children.Add(new Border { Child = cell, Background = Brush("SurfaceBrush"), CornerRadius = new CornerRadius(10), Padding = new Thickness(8), Margin = new Thickness(0, 0, 6, 0) });
        }
        Body.Children.Add(_steps);
        if (result is not null)
        {
            Set("router", Ms(result.RouterMs)); Set("ping", Ms(result.PingMs)); Set("dns", Ms(result.DnsMs));
            Set("down", Fmt(result.DownloadMbps)); Set("up", Fmt(result.UploadMbps)); Set("load", Ms(result.LoadedPingMs));
            var (grade, text) = SpeedMath.Bufferbloat(result.PingMs, result.LoadedPingMs);
            Body.Children.Add(Row($"Ping sob carga: nota {grade}", text, null, grade is "A+" or "A" or "B" ? "OkBrush" : grade == "?" ? "MutedBrush" : "WarnBrush"));
        }
        _lastResult = result ?? _lastResult;
        Body.Children.Add(Muted("Conexões ao mesmo tempo (mais conexões medem melhor internet acima de 300 Mbps):", 12));
        Body.Children.Add(Choice(Connections.Select(c => c == 1 ? "Automático" : c + " conexões").ToList(), Array.IndexOf(Connections, _connections), i => _connections = Connections[i]));
        Body.Children.Add(_stage);
        Body.Children.Add(GameCard());
        Body.Children.Add(DnsCard());
        if (result is not null)
        {
            Body.Children.Add(Row($"Download {Fmt(result.DownloadMbps)} · Upload {Fmt(result.UploadMbps)}",
                $"Latência {Ms(result.PingMs)} · variação {Ms(result.JitterMs)} · servidor {result.ServerCode ?? "?"}{(result.City is null ? "" : " (" + result.City + ")")} · {result.Connections} conexões"));
        }
        var history = _history.Load();
        if (SpeedHistory.Compare(history) is { } compare) Body.Children.Add(Muted(compare, 12.5));
        if (history.Count > 0)
        {
            Body.Children.Add(Heading("Testes anteriores"));
            foreach (var h in history.Take(10))
                Body.Children.Add(Muted($"{h.At.ToLocalTime():dd/MM HH:mm}{(h.Name is { Length: > 0 } n ? " · " + n : "")} · ↓ {Fmt(h.DownloadMbps)} · ↑ {Fmt(h.UploadMbps)} · {Ms(h.PingMs)}{(h.LoadedPingMs is double l ? $" (sob carga {l:0} ms)" : "")} · {h.ServerCode ?? "?"}", 12));
        }
    }

    private ActiveAdapter? _adapter;

    private void Set(string key, string text) { if (_stepValues.TryGetValue(key, out var t)) t.Text = text; }

    private static string Fmt(double? mbps) => mbps is double v ? $"{v:0.0} Mbps" : "—";

    private static string Ms(double? ms) => ms is double v ? $"{v:0} ms" : "—";

    private async Task RunAsync()
    {
        _start.IsEnabled = false;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var test = new SpeedTest(Http);
        int connections = _connections == 1 ? 4 : _connections;
        _adapter = await Task.Run(NetworkStatus.Active);
        Build(null);
        GaugeRunning();
        if (_mainLink is not null) { _mainLink.Mode = MBExtreme.UI.Controls.LinkMode.Ping; _mainLink.Color = MBExtreme.UI.Controls.NetMapControl.Ping; _map.InvalidateVisual(); }
        if (_mainLink is not null) _ = TraceToMapAsync(_mainLink, "speed.cloudflare.com", token);
        _downBig.Text = _upBig.Text = "—";
        var live = new Progress<double>(v => _gauge.Show(v));
        try
        {
            _stage.Text = "Ping até o roteador...";
            double? router = _adapter?.Gateway is { } gw ? await NetAnalyzer.PingAsync(gw, 1000) : null;
            Set("router", Ms(router));
            _stage.Text = "Tempo de resposta do DNS...";
            double? dns = await SpeedTest.DnsTimeAsync(token);
            Set("dns", Ms(dns));
            _stage.Text = "Achando o servidor...";
            var (code, city) = await test.ServerAsync(token);
            _stage.Text = $"Latência ({code ?? "servidor"}{(city is null ? "" : ", " + city)})...";
            var (ping, jitter) = await test.LatencyAsync(12, token);
            if (ping is null) { SetStatus("Sem resposta do servidor de teste. Confira a internet.", "BadBrush"); return; }
            _gauge.CenterText = $"{ping:0}";
            FillInfo(ping);
            _gauge.Caption = "ms de ping";
            _gauge.InvalidateVisual();
            Set("ping", Ms(ping));
            _stage.Text = $"Download com {connections} conexões (2 s de aquecimento + 8 s de medição) e ping sob carga...";
            _gauge.CenterText = null;
            _gauge.Caption = "Mbps";
            _gauge.Upload = false;
            if (_mainLink is not null) { _mainLink.Mode = MBExtreme.UI.Controls.LinkMode.Download; _mainLink.Color = MBExtreme.UI.Controls.NetMapControl.Download; _map.InvalidateVisual(); }
            var (down, loaded) = await test.DownloadWithLoadedPingAsync(connections, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8), live, token);
            Set("down", Fmt(down));
            Set("load", Ms(loaded));
            _downBig.Text = down is double dv ? $"{dv:0.0}" : "—";
            Motion.Flash(_downBig);
            _stage.Text = $"Upload com {connections} conexões (2 s de aquecimento + 8 s de medição)...";
            _gauge.Reset();
            _gauge.Upload = true;
            if (_mainLink is not null) { _mainLink.Mode = MBExtreme.UI.Controls.LinkMode.Upload; _mainLink.Color = MBExtreme.UI.Controls.NetMapControl.Upload; _map.InvalidateVisual(); }
            var up = await test.UploadAsync(connections, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8), live, token);
            var result = new SpeedResult(DateTimeOffset.Now, down, up, ping, jitter, code, city, connections, loaded, router, dns, _name.Text.Trim());
            _history.Add(result);
            _stage.Text = "Pronto.";
            if (_mainLink is not null) { _mainLink.Mode = MBExtreme.UI.Controls.LinkMode.Idle; _mainLink.Color = MBExtreme.UI.Controls.NetMapControl.Download; _map.InvalidateVisual(); }
            _upBig.Text = up is double uv ? $"{uv:0.0}" : "—";
            Build(result);
            SetStatus($"Download {Fmt(down)}, upload {Fmt(up)}, latência {Ms(ping)}.", "OkBrush");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Teste parado.", "WarnBrush");
        }
        catch (HttpRequestException ex)
        {
            SetStatus("Não foi possível testar: " + ex.Message, "BadBrush");
        }
        finally
        {
            _start.IsEnabled = true;
            if (_mainLink is not null && _mainLink.Mode != MBExtreme.UI.Controls.LinkMode.Idle) { _mainLink.Mode = MBExtreme.UI.Controls.LinkMode.Idle; _mainLink.Color = MBExtreme.UI.Controls.NetMapControl.Download; _map.InvalidateVisual(); }
        }
    }
}
