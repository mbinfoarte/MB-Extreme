using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MBExtreme.Core.Network;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Internet: topo com o provedor, o servidor do teste, as conexões e o botão de iniciar; faixa com os resultados sempre visível;
/// abas Velocidade, Jogos e servidores (mapa do Brasil ou do mundo com setas até cada servidor), DNS (testar e trocar aqui mesmo),
/// Roteador e rota, e Histórico.
/// </summary>
public sealed partial class InternetTestPage : ToolPageBase
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly SpeedHistory _history = new();
    private readonly TextBlock _stage = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock _statusLine = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 8, 0, 0) };
    private readonly TextBlock _connLine = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
    private CancellationTokenSource? _cts;
    private int _connections = 1;
    private readonly TextBox _name = new() { Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 4, 0, 0) };
    private static readonly int[] Connections = { 1, 4, 8 };

    private readonly SpeedGauge _gauge = new() { Width = 300, Height = 300 };
    private readonly Grid _gaugeHost = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private Button? _go;
    private Button _startButton = new();
    private Button _stopButton = new();
    private bool _running;

    private readonly NetMapControl _speedMap = new() { Height = 300, MinWidth = 300 };
    private MapLink? _mainLink;
    private GeoPoint? _colo;
    private string? _coloCode;
    private string? _publicIp, _isp, _city;
    private double _userLat = double.NaN, _userLon = double.NaN;
    private ActiveAdapter? _adapter;
    private SpeedResult? _lastResult;
    private double? _routerMs;

    private string _tab = "speed";
    private readonly Dictionary<string, ToggleButton> _tabs = new();
    private readonly ContentControl _tabHost = new();

    public InternetTestPage() : base("Internet", "")
    {
        HideHeader();
        _gaugeHost.Children.Add(_gauge);
        _speedMap.Arrows = false;
        _gameMap.Scope = MapScope.Brazil;
        _gameMap.Arrows = true;
        Loaded += async (_, _) =>
        {
            if (Body.Children.Count > 0) return;
            BuildShell();
            await LocateAsync();
        };
    }

    // ================================================================ moldura: topo, resultados e abas

    private void Say(string text, string brush = "MutedBrush")
    {
        _statusLine.Text = text;
        _statusLine.Foreground = Brush(brush);
    }

    private void BuildShell()
    {
        Body.Margin = new Thickness(0);
        Body.Children.Clear();

        // topo: título e conexão à esquerda; servidor, conexões e iniciar à direita
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel();
        title.Children.Add(new TextBlock { Text = "Internet", FontSize = 26, FontWeight = FontWeights.SemiBold });
        _connLine.Foreground = Brush("MutedBrush");
        title.Children.Add(Free(_connLine));
        top.Children.Add(title);

        var controls = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        controls.Children.Add(Label("Servidor"));
        controls.Children.Add(Segmented(new[] { ServerLabel(), _libre is null ? "LibreSpeed ▾" : "LibreSpeed · " + Short(_libre.Name) }, _libre is null ? 0 : 1, async i =>
        {
            if (i == 0) { _libre = null; _libreList.Children.Clear(); Say("Teste pela Cloudflare (servidor mais perto de você).", "OkBrush"); BuildShell(); }
            else { SelectTab("speed"); await FindLibreAsync(); }
        }));
        controls.Children.Add(Label("Conexões"));
        controls.Children.Add(Segmented(Connections.Select(c => c == 1 ? "Auto" : c.ToString()).ToList(), Array.IndexOf(Connections, _connections), i => _connections = Connections[i],
            "Conexões ao mesmo tempo. Auto usa 4; 8 mede melhor internet acima de 300 Mbps."));
        _startButton = new Button { Content = "INICIAR TESTE", Style = (Style)FindResource("PrimaryButton"), Padding = new Thickness(20, 8, 20, 8), FontSize = 13, Margin = new Thickness(12, 0, 0, 0), IsEnabled = !_running };
        _startButton.Click += async (_, _) => await RunAsync();
        controls.Children.Add(_startButton);
        _stopButton = new Button { Content = "Parar", Style = (Style)FindResource("GhostButton"), Padding = new Thickness(14, 7, 14, 7), FontSize = 12.5, Margin = new Thickness(8, 0, 0, 0),
            Visibility = _running ? Visibility.Visible : Visibility.Collapsed };
        _stopButton.Click += (_, _) => _cts?.Cancel();
        controls.Children.Add(_stopButton);
        Grid.SetColumn(controls, 1);
        top.Children.Add(controls);
        Body.Children.Add(top);

        Body.Children.Add(Tiles());
        Body.Children.Add(Free(_statusLine));

        // abas
        var tabs = new WrapPanel { Margin = new Thickness(0, 12, 0, 12) };
        _tabs.Clear();
        foreach (var (key, text) in new[] { ("speed", "Velocidade"), ("games", "Jogos e servidores"), ("dns", "DNS"), ("route", "Roteador e rota"), ("history", "Histórico") })
        {
            var tab = new ToggleButton { Style = (Style)FindResource("ChipToggle"), Content = text, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(14, 6, 14, 6), IsChecked = key == _tab };
            string k = key;
            tab.Click += (_, _) => SelectTab(k);
            _tabs[key] = tab;
            tabs.Children.Add(tab);
        }
        Body.Children.Add(tabs);
        Body.Children.Add(Free(_tabHost));
        SelectTab(_tab);
    }

    private static string Short(string text) => text.Length > 22 ? text[..21] + "…" : text;

    private string ServerLabel() => _coloCode is null ? "Cloudflare" : "Cloudflare · " + _coloCode;

    private TextBlock Label(string text) => new() { Text = text, FontSize = 12.5, Foreground = Brush("MutedBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 6, 0) };

    /// <summary>Botões juntos numa pílula (o escolhido fica amarelo), pequenos.</summary>
    private Border Segmented(IReadOnlyList<string> options, int selected, Action<int> onChange, string? tip = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var buttons = new List<ToggleButton>();
        for (int i = 0; i < options.Count; i++)
        {
            int index = i;
            var b = new ToggleButton { Style = (Style)FindResource("ChipToggle"), Content = options[i], IsChecked = i == selected, Margin = new Thickness(i == 0 ? 0 : 2, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3) };
            b.Click += (_, _) =>
            {
                for (int j = 0; j < buttons.Count; j++) buttons[j].IsChecked = j == index;
                onChange(index);
            };
            buttons.Add(b);
            row.Children.Add(b);
        }
        return new Border { Child = row, Background = Brush("SurfaceBrush"), CornerRadius = new CornerRadius(10), Padding = new Thickness(2), BorderBrush = Brush("Surface2Brush"), BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center, ToolTip = tip };
    }

    private void SelectTab(string key)
    {
        _tab = key;
        foreach (var (k, t) in _tabs) t.IsChecked = k == key;
        _tabHost.Content = null;
        UIElement content = key switch
        {
            "games" => GamesTab(),
            "dns" => DnsTab(),
            "route" => RouteTab(),
            "history" => HistoryTab(),
            _ => SpeedTab(),
        };
        _tabHost.Content = content;
        if (content is FrameworkElement fe) Motion.Enter(fe);
    }

    // ---------------------------------------------------------------- faixa de resultados

    private readonly Dictionary<string, TextBlock> _tileValues = new();
    private readonly Dictionary<string, Border> _tileCards = new();
    private readonly Dictionary<string, string> _tileText = new();

    private UIElement Tiles()
    {
        var grid = new UniformGrid { Columns = 6, Margin = new Thickness(0, 14, 0, 0) };
        _tileValues.Clear();
        _tileCards.Clear();
        foreach (var (key, label) in new[] { ("down", "Download"), ("up", "Upload"), ("ping", "Ping"), ("jitter", "Variação"), ("load", "Sob carga"), ("dns", "DNS em uso") })
        {
            var cell = new StackPanel();
            cell.Children.Add(new TextBlock { Text = label, FontSize = 11.5, Foreground = Brush("MutedBrush") });
            var value = new TextBlock { Text = _tileText.GetValueOrDefault(key, "—"), FontSize = 19, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            _tileValues[key] = value;
            cell.Children.Add(value);
            var card = new Border { Child = cell, Background = Brush("SurfaceBrush"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 9, 14, 10), Margin = new Thickness(0, 0, 8, 0),
                BorderThickness = new Thickness(1), BorderBrush = Brush("Surface2Brush") };
            _tileCards[key] = card;
            grid.Children.Add(card);
            Paint(key);
        }
        return grid;
    }

    /// <summary>Mostra o valor (com a cor do ping quando for tempo) e tira o destaque de "medindo".</summary>
    private void Set(string key, string text)
    {
        _tileText[key] = text;
        if (_tileValues.TryGetValue(key, out var t)) { t.Text = text; Motion.Flash(t); }
        if (_tileCards.TryGetValue(key, out var card)) { Motion.Stop(card); card.BorderBrush = Brush("Surface2Brush"); }
        Paint(key);
    }

    private void Paint(string key)
    {
        if (!_tileValues.TryGetValue(key, out var t)) return;
        t.Foreground = key switch
        {
            "ping" or "jitter" when _lastPing(key) is double ms => LevelBrush(GameServers.Level(key == "jitter" ? ms * 4 : ms)),
            "load" when _lastResult is { } r => Brush(SpeedMath.Bufferbloat(r.PingMs, r.LoadedPingMs).Grade is "A+" or "A" or "B" ? "OkBrush" : "WarnBrush"),
            _ => Brush("TextBrush"),
        };
    }

    private double? _lastPing(string key) => key == "ping" ? _lastResult?.PingMs ?? _livePing : _lastResult?.JitterMs ?? _liveJitter;
    private double? _livePing, _liveJitter;

    /// <summary>Etapa em andamento: borda amarela pulsando.</summary>
    private void Working(string key)
    {
        if (!_tileCards.TryGetValue(key, out var card)) return;
        card.BorderBrush = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xC4, 0x00));
        Motion.Breathe(card, 1.02, 0.8);
    }

    private static readonly Color[] LevelColors =
    {
        Color.FromRgb(0x3D, 0xDC, 0x97), Color.FromRgb(0xFF, 0xD2, 0x3F), Color.FromRgb(0xFF, 0xA9, 0x4D), Color.FromRgb(0xFF, 0x5A, 0x5F),
    };

    private static SolidColorBrush LevelBrush(int level) => new(LevelColors[Math.Clamp(level, 0, 3)]);

    private static string Fmt(double? mbps) => mbps is double v ? $"{v:0.0} Mbps" : "—";

    private static string Ms(double? ms) => ms is double v ? $"{v:0} ms" : "—";

    // ================================================================ onde você está

    /// <summary>Acha você (cidade pelo IP público), o servidor da Cloudflare e o DNS em uso; preenche o topo e os mapas.</summary>
    private async Task LocateAsync()
    {
        _adapter = await Task.Run(NetworkStatus.Active);
        if (!_tileText.ContainsKey("dns")) Set("dns", DnsName(CurrentDns()) ?? "—");
        var test = new SpeedTest(Http);
        try
        {
            var trace = await test.TraceAsync(CancellationToken.None);
            _publicIp = trace.Ip;
            _coloCode = trace.Code;
            if (trace.Code is { } code && NetMap.Colos.TryGetValue(code, out var colo)) _colo = colo with { Name = $"{colo.Name} ({code})" };
            var where = await NetMap.LocateAsync(Http, CancellationToken.None);
            if (where is not null)
            {
                _isp = where.Isp;
                _city = string.Join(", ", new[] { where.City, where.Region }.Where(x => x.Length > 0));
                _publicIp ??= where.Ip;
                _userLat = where.Lat;
                _userLon = where.Lon;
                var me = new GeoPoint(where.City.Length > 0 ? where.City : "sua cidade", where.Lat, where.Lon);
                _speedMap.User = me;
                _gameMap.User = me;
            }
            else if (_colo is not null)
            {
                _userLat = _colo.Lat;
                _userLon = _colo.Lon;
                _speedMap.User = _colo with { Name = "perto de " + _colo.Name };
                _gameMap.User = _speedMap.User;
            }
            _speedMap.UserDetail = _gameMap.UserDetail = _isp is { Length: > 0 } isp ? "Provedor: " + isp : null;
            if (_colo is not null) _mainLink = _speedMap.SetLink(_colo, LinkMode.Idle, NetMapControl.Download, "Servidor de teste · " + _colo.Name);
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }
        UpdateConnLine();
        PickNearestIfEmpty();
        if (_tab == "games") RefreshGames();
        BuildShell();   // o nome do servidor da Cloudflare entra no topo
    }

    private void UpdateConnLine()
    {
        var parts = new List<string>();
        if (_isp is { Length: > 0 }) parts.Add(_isp);
        if (_city is { Length: > 0 }) parts.Add(_city);
        if (_publicIp is { Length: > 0 }) parts.Add("IP " + _publicIp);
        if (_adapter is not null)
            parts.Add(_adapter.Name + (_adapter.LinkSpeedBitsPerSecond > 0 ? $" ({_adapter.LinkSpeedBitsPerSecond / 1_000_000} Mbps)" : ""));
        _connLine.Text = parts.Count > 0 ? string.Join(" · ", parts) : "Localizando a sua conexão...";
    }

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

    // ================================================================ aba Velocidade

    private UIElement SpeedTab()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var left = new StackPanel();
        if (!_running && _go is null) AddGoButton();
        left.Children.Add(Free(_gaugeHost));
        left.Children.Add(Free(_stage));
        grid.Children.Add(CardOf(left));

        var right = new StackPanel();
        right.Children.Add(Title("Servidor do teste", _libre is not null ? "LibreSpeed · " + _libre.Name : _colo is null ? "Cloudflare, o servidor mais perto de você." : "Cloudflare · " + _colo.Name));
        right.Children.Add(Free(_speedMap));
        right.Children.Add(Free(_libreList));
        right.Children.Add(Muted("Nome do teste (opcional, para comparar depois. Ex.: antes de trocar o roteador):", 11.5));
        System.Windows.Automation.AutomationProperties.SetName(_name, "Nome do teste");
        right.Children.Add(Free(_name));
        if (_lastResult is { } r)
        {
            var (grade, text) = SpeedMath.Bufferbloat(r.PingMs, r.LoadedPingMs);
            right.Children.Add(Row($"Ping sob carga: nota {grade}", text, null, grade is "A+" or "A" or "B" ? "OkBrush" : grade == "?" ? "MutedBrush" : "WarnBrush"));
            right.Children.Add(Muted($"Servidor {r.ServerCode ?? "?"}{(r.City is null ? "" : " (" + r.City + ")")} · {r.Connections} conexões · roteador {Ms(r.RouterMs)} · DNS {Ms(r.DnsMs)}", 11.5));
        }
        var rightCard = CardOf(right);
        rightCard.Margin = new Thickness(14, 0, 0, 0);
        Grid.SetColumn(rightCard, 1);
        grid.Children.Add(rightCard);
        return grid;
    }

    private StackPanel Title(string title, string subtitle)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 14.5, FontWeight = FontWeights.SemiBold });
        if (subtitle.Length > 0) panel.Children.Add(Muted(subtitle, 11.5));
        return panel;
    }

    /// <summary>Botão redondo de começar no meio do velocímetro.</summary>
    private void AddGoButton()
    {
        _go = new Button { Width = 118, Height = 118, Cursor = System.Windows.Input.Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var t = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(59));
        border.SetValue(Border.BackgroundProperty, Application.Current.FindResource("ButtonBrush"));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        t.VisualTree = border;
        _go.Template = t;
        _go.Content = new TextBlock { Text = "INICIAR", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Brushes.Black };
        System.Windows.Automation.AutomationProperties.SetName(_go, "Iniciar o teste de internet");
        _go.Click += async (_, _) => await RunAsync();
        _gaugeHost.Children.Add(_go);
        _gauge.BeginAnimation(OpacityProperty, null);
        _gauge.Opacity = 0.35;
        Motion.Breathe(_go, 1.05, 1.5);
    }

    /// <summary>Some com o botão e acende o velocímetro (o teste começou).</summary>
    private void GaugeRunning()
    {
        if (_go is not null) { Motion.Stop(_go); _gaugeHost.Children.Remove(_go); _go = null; }
        _gauge.Reset();
        Motion.To(_gauge, OpacityProperty, 1, Motion.Normal);
    }

    // ---------------------------------------------------------------- servidor LibreSpeed

    private LibreServer? _libre;
    private readonly StackPanel _libreList = new() { Margin = new Thickness(0, 8, 0, 4) };

    private async Task FindLibreAsync()
    {
        _libreList.Children.Clear();
        _libreList.Children.Add(Muted("Buscando a lista do LibreSpeed e medindo o ping dos servidores (uns segundos)...", 12));
        var all = await LibreSpeed.ListAsync(Http, CancellationToken.None);
        if (all.Count == 0) { _libreList.Children.Clear(); _libreList.Children.Add(Muted("Não foi possível buscar a lista do LibreSpeed agora. Continua a Cloudflare.", 12)); return; }
        _libreAll = all;
        var candidates = all.OrderBy(LibreSpeed.RegionRank).Take(24).ToList();
        var ranked = await LibreSpeed.RankAsync(Http, candidates, CancellationToken.None);
        _libreList.Children.Clear();
        var ok = ranked.Where(r => r.Ping is not null).Take(8).ToList();
        if (ok.Count == 0) { _libreList.Children.Add(Muted("Nenhum servidor respondeu. Continua a Cloudflare.", 12)); return; }
        _libreList.Children.Add(Muted("Os mais rápidos para você (clique para escolher):", 12));
        foreach (var (server, ping) in ok)
        {
            var s = server;
            var radio = new RadioButton
            {
                GroupName = "libre", Content = $"{s.Name}  ·  {ping:0} ms" + (s.Sponsor.Length > 0 ? $"  ·  {s.Sponsor}" : ""), Foreground = Brush("TextBrush"), Margin = new Thickness(0, 3, 0, 3),
                IsChecked = _libre?.Id == s.Id,
            };
            radio.Checked += (_, _) => { _libre = s; Say($"Servidor escolhido: {s.Name}. Clique em INICIAR TESTE.", "OkBrush"); };
            _libreList.Children.Add(radio);
        }
        if (_libre is null && _libreList.Children.OfType<RadioButton>().FirstOrDefault() is { } first) first.IsChecked = true;
        Motion.Cascade(_libreList, 30);
    }

    private IReadOnlyList<LibreServer>? _libreAll;

    // ================================================================ o teste de velocidade

    private void SetRunning(bool running)
    {
        _running = running;
        _startButton.IsEnabled = !running;
        _stopButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task RunAsync()
    {
        if (_running) return;
        SetRunning(true);
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var test = _libre is not null ? SpeedTest.ForLibreSpeed(Http, _libre) : new SpeedTest(Http);
        int connections = _connections == 1 ? 4 : _connections;
        _adapter = await Task.Run(NetworkStatus.Active);
        UpdateConnLine();
        _lastResult = null;
        _livePing = _liveJitter = null;
        foreach (var key in new[] { "down", "up", "ping", "jitter", "load" }) Set(key, "—");
        SelectTab("speed");
        GaugeRunning();
        if (_mainLink is not null && _libre is null) { _mainLink.Mode = LinkMode.Ping; _mainLink.Color = NetMapControl.Ping; _speedMap.InvalidateVisual(); }
        if (_mainLink is not null) _ = TraceToMapAsync(_speedMap, _mainLink, "speed.cloudflare.com", token);
        var live = new Progress<double>(v => _gauge.Show(v));
        try
        {
            _stage.Text = "Ping até o roteador...";
            _routerMs = _adapter?.Gateway is { } gw ? await NetAnalyzer.PingAsync(gw, 1000) : null;
            _stage.Text = "Tempo de resposta do DNS..."; Working("dns");
            double? dns = await SpeedTest.DnsTimeAsync(token);
            Set("dns", $"{DnsName(CurrentDns()) ?? "DNS"} · {Ms(dns)}");
            _stage.Text = "Achando o servidor...";
            var (code, city) = _libre is not null ? ((string?)"LibreSpeed", (string?)_libre.Name) : await test.ServerAsync(token);
            _stage.Text = $"Latência ({code ?? "servidor"}{(city is null ? "" : ", " + city)})...";
            Working("ping"); Working("jitter");
            var (ping, jitter) = await test.LatencyAsync(12, token);
            if (ping is null) { Say("Sem resposta do servidor de teste. Confira a internet.", "BadBrush"); return; }
            _livePing = ping; _liveJitter = jitter;
            Set("ping", Ms(ping)); Set("jitter", Ms(jitter));
            _gauge.CenterText = $"{ping:0}";
            _gauge.Caption = "ms de ping";
            _gauge.InvalidateVisual();
            _stage.Text = $"Download com {connections} conexões (2 s de aquecimento + 8 s de medição) e ping sob carga...";
            _gauge.CenterText = null;
            _gauge.Caption = "Mbps";
            _gauge.Upload = false;
            if (_mainLink is not null && _libre is null) { _mainLink.Mode = LinkMode.Download; _mainLink.Color = NetMapControl.Download; _speedMap.InvalidateVisual(); }
            Working("down"); Working("load");
            var (down, loaded) = await test.DownloadWithLoadedPingAsync(connections, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8), live, token);
            Set("down", Fmt(down)); Set("load", Ms(loaded));
            _stage.Text = $"Upload com {connections} conexões (2 s de aquecimento + 8 s de medição)...";
            _gauge.Reset();
            _gauge.Upload = true;
            Working("up");
            if (_mainLink is not null && _libre is null) { _mainLink.Mode = LinkMode.Upload; _mainLink.Color = NetMapControl.Upload; _speedMap.InvalidateVisual(); }
            var up = await test.UploadAsync(connections, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8), live, token);
            var result = new SpeedResult(DateTimeOffset.Now, down, up, ping, jitter, code, city, connections, loaded, _routerMs, dns, _name.Text.Trim());
            _history.Add(result);
            _lastResult = result;
            Set("up", Fmt(up));
            var (grade, _) = SpeedMath.Bufferbloat(ping, loaded);
            Set("load", loaded is double l && ping is double p0 ? $"{grade} · +{Math.Max(0, l - p0):0} ms" : Ms(loaded));
            _stage.Text = "Pronto.";
            Say($"Download {Fmt(down)}, upload {Fmt(up)}, latência {Ms(ping)}.", "OkBrush");
        }
        catch (OperationCanceledException)
        {
            Say("Teste parado.", "WarnBrush");
        }
        catch (HttpRequestException ex)
        {
            Say("Não foi possível testar: " + ex.Message, "BadBrush");
        }
        finally
        {
            foreach (var card in _tileCards.Values) { Motion.Stop(card); card.BorderBrush = Brush("Surface2Brush"); }
            if (_mainLink is not null && _mainLink.Mode != LinkMode.Idle) { _mainLink.Mode = LinkMode.Idle; _mainLink.Color = NetMapControl.Download; _speedMap.InvalidateVisual(); }
            SetRunning(false);
            if (_tab == "speed") SelectTab("speed");   // o botão redondo volta para um novo teste
        }
    }

    /// <summary>Saltos até o servidor (uma rodada do traceroute) desenhados na linha do mapa.</summary>
    private static async Task TraceToMapAsync(NetMapControl map, MapLink link, string host, CancellationToken ct)
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
        map.InvalidateVisual();
    }
}
