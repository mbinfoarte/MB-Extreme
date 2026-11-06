using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MBExtreme.Core.Licensing;
using MBExtreme.Core.Tweaks;
using MBExtreme.UI.Pages;

namespace MBExtreme.UI;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, UserControl> _pages = new();
    private readonly Dictionary<string, UserControl> _inner = new();
    private bool _suppressNav;
    private UpdateController? _updates;
    private string? _current;
    private bool _backing;
    private Stack<string> _history = new();

    public MainWindow()
    {
        InitializeComponent();
        BuildMenu();
        Current = this;
        StateChanged += (_, _) => UpdateMaximizeState();
        Loaded += (_, _) =>
        {
            AppServices.Overlay.AttachHotkey(this);
            AppServices.Hotkeys.Attach(this);
            AppServices.Studio.ApplyHotkeys(AppServices.Settings);
        };
        Go("home");
        _updates = new UpdateController(RootGrid, ContentHost);
        Loaded += async (_, _) => await _updates.CheckAsync();
        ContentRendered += (_, _) => Pages.WhatsNew.ShowOnceAfterUpdate(this);
        if (AppServices.Boost.Settings.BoostAuto) AppServices.Boost.SetAuto(true);
        AppServices.AutoMemory.StartIfEnabled();
        Accessibility.ApplyScale(RootGrid, AppServices.Settings.UiScale);
        // Voltar como no navegador: Alt + seta para a esquerda e o botão lateral do mouse
        PreviewKeyDown += (_, e) =>
        {
            // Ctrl + e Ctrl − aumentam e diminuem a interface; Ctrl 0 volta ao normal
            if (System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control && !Pages.PeripheralTestPage.Active)
            {
                double? scale = e.Key switch
                {
                    System.Windows.Input.Key.OemPlus or System.Windows.Input.Key.Add => Accessibility.Step(AppServices.Settings.UiScale, +1),
                    System.Windows.Input.Key.OemMinus or System.Windows.Input.Key.Subtract => Accessibility.Step(AppServices.Settings.UiScale, -1),
                    System.Windows.Input.Key.D0 or System.Windows.Input.Key.NumPad0 => 1.0,
                    _ => null,
                };
                if (scale is double s) { SetScale(s); e.Handled = true; return; }
            }
            if (Pages.PeripheralTestPage.Active) return;   // no teste de periféricos essas teclas estão sendo testadas
            if (e.SystemKey == System.Windows.Input.Key.Left && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Alt) { GoBack(); e.Handled = true; }
            else if (e.Key == System.Windows.Input.Key.BrowserBack) { GoBack(); e.Handled = true; }
        };
        PreviewMouseDown += (_, e) => { if (!Pages.PeripheralTestPage.Active && e.ChangedButton == System.Windows.Input.MouseButton.XButton1) { GoBack(); e.Handled = true; } };
        _ = Task.Run(() => MBExtreme.Core.Operations.OperationJournal.Shared.Prune(DateTimeOffset.Now));
        AppServices.License.Changed += (_, _) => Dispatcher.BeginInvoke(new Action(() => { UpdateFooter(); _ = AppServices.ProLock.CheckAsync(); }));
        AppServices.License.Start();
        // disponível para suporte enquanto o programa estiver aberto (cada acesso exige o aceite do usuário)
        if (AppServices.Settings.RemoteAutoListen && AppServices.License.Config.ServerUrl.Length > 0) _ = AppServices.RemoteSupport.StartAsync();
        _ = AppServices.Fans.StartIfConfiguredAsync();
        UpdateFooter();
        VersionText.Text = VersionLabel;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    /// <summary>Muda a escala da interface e guarda.</summary>
    internal void SetScale(double scale)
    {
        AppServices.Settings.UiScale = scale;
        AppServices.Settings.Save();
        Accessibility.ApplyScale(RootGrid, scale);
    }

    /// <summary>Alguém abriu o programa de novo: traz esta janela para frente em vez de abrir outra.</summary>
    internal void ActivateFromOther()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
        Topmost = true;   // truque comum do WPF para vencer a regra do Windows de não roubar o foco
        Topmost = false;
        Focus();
    }

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Maximizada, a janela sem moldura ultrapassa a tela pela borda de redimensionamento: compensa com margem e troca o ícone.</summary>
    private void UpdateMaximizeState()
    {
        bool maximized = WindowState == WindowState.Maximized;
        WindowFrame.Margin = maximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
        WindowFrame.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(22);
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = maximized ? "Restaurar" : "Maximizar";
    }

    /// <summary>Janela principal, para as páginas pedirem navegação (ex.: o painel abre a limpeza).</summary>
    internal static MainWindow? Current { get; private set; }

    /// <summary>Abre uma página pelo nome e destaca o módulo dela no menu lateral.</summary>
    internal void Go(string key, bool autoStart = false, bool deep = false)
    {
        string? sideKey = key == "home" ? "home" : NavigationMap.SideKeyOf(key);

        _suppressNav = true;
        foreach (var radio in NavRadios())
            radio.IsChecked = (string?)radio.Tag == sideKey;
        _suppressNav = false;

        ShowPage(key);

        if (autoStart && _inner.TryGetValue(key, out var inner))
        {
            if (inner is CleanupPage cleanup) cleanup.StartScan();
            else if (inner is DiagnosticPage diagnose) _ = deep ? diagnose.StartDeepAsync() : diagnose.StartAsync();
        }
    }

    private void BuildMenu()
    {
        // Menu em seções, como um painel de controle: cada item abre a tela direto.
        bool first = true;
        foreach (var section in NavigationMap.Sidebar)
        {
            if (section.Header.Length > 0)
                NavPanel.Children.Add(new TextBlock
                {
                    Text = section.Header,
                    FontSize = 9.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("MutedBrush"),
                    Margin = new Thickness(10, first ? 4 : 12, 0, 3),
                });
            foreach (var item in section.Items)
                AddNavItem(item.Key, item.Title, item.Icon, item.Key == "home");
            first = false;
        }
    }

    private static readonly Brush IconIdle = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x96));
    private static readonly Brush IconActive = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00));

    private void AddNavItem(string key, string title, string glyph, bool selected)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = new TextBlock
        {
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            FontSize = 13,
            Text = glyph,
            Width = 20,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = selected ? IconActive : IconIdle,
        };
        content.Children.Add(icon);
        content.Children.Add(new TextBlock
        {
            Text = title,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var radio = new RadioButton
        {
            Tag = key,
            GroupName = "nav",
            IsChecked = selected,
            Style = (Style)FindResource("SideItem"),
            Content = content,
        };
        System.Windows.Automation.AutomationProperties.SetName(radio, title);
        radio.Checked += (_, _) => icon.Foreground = IconActive;
        radio.Unchecked += (_, _) => icon.Foreground = IconIdle;
        radio.Checked += Nav_Checked;
        NavPanel.Children.Add(radio);
    }

    private void FontSize_Click(object sender, RoutedEventArgs e) => Go("a11y");

    private void CheckUpdate_Click(object sender, RoutedEventArgs e) => CheckForUpdates();

    /// <summary>Procura atualização e sempre responde (rodapé do menu e tela Sobre).</summary>
    internal async void CheckForUpdates()
    {
        if (_updates is null) return;
        VersionText.Text = "Procurando atualização...";
        string answer = await _updates.CheckNowAsync();
        VersionText.Text = VersionLabel;
        MessageBox.Show(answer, MBExtreme.Core.AppInfo.ShortName, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static string VersionLabel =>
        $"v{MBExtreme.Core.AppInfo.Version} · compilação {MBExtreme.Core.Update.AppUpdates.CurrentBuild()} · Verificar atualização";

    private IEnumerable<RadioButton> NavRadios() => NavPanel.Children.OfType<RadioButton>();

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        // O evento dispara durante o carregamento do XAML, antes de ContentHost existir.
        if (_suppressNav || ContentHost is null) return;
        if (sender is RadioButton { Tag: string key })
        {
            _history.Clear(); // clicar no menu lateral começa um caminho novo
            ShowPage(key);
        }
    }

    /// <summary>Nome de uma tela para o botão Voltar.</summary>
    internal static string LabelOf(string key) =>
        key == "home" ? "Painel" : key == "alltools" ? "Todas as ferramentas" : NavigationMap.Modules.FirstOrDefault(m => m.Key == key)?.Title ?? NavigationMap.TileOf(key)?.Title ?? "Voltar";

    /// <summary>Volta para a tela de onde o usuário veio (o Início, se ele chegou por lá; senão, o módulo da página).</summary>
    internal void GoBack()
    {
        string target = _history.Count > 0 ? _history.Pop() : (_current is null ? "home" : NavigationMap.ModuleOf(_current)?.Key ?? "home");
        if (target == _current) target = "home";
        _backing = true;
        try { Go(target); }
        finally { _backing = false; }
    }

    /// <summary>Destino do botão Voltar na tela atual (para o rótulo).</summary>
    private string BackTarget(string key) =>
        _history.Count > 0 ? _history.Peek() : NavigationMap.ModuleOf(key)?.Key ?? "home";

    /// <summary>Atualiza o cartão do cliente: foto, nome, plano, dias restantes e o botão de ativar ou renovar.</summary>
    internal void UpdateFooter()
    {
        var license = AppServices.License;
        var info = LicenseProfile.Describe(license.Status, license.Config, DateTimeOffset.UtcNow, Environment.UserName);
        CustomerName.Text = info.Name;
        CustomerPlan.Text = info.PlanText;
        CustomerPlan.Foreground = (Brush)FindResource(info.Level switch
        {
            ProfileLevel.Ok => "OkBrush", ProfileLevel.Warn => "WarnBrush", ProfileLevel.Bad => "BadBrush", _ => "MutedBrush",
        });
        ActivateButton.Content = info.ActionText;
        // selo "Licença: Grátis" enquanto não houver assinatura Pro ativa
        FreeBadge.Visibility = MBExtreme.Core.Licensing.LicenseExpiry.IsPro(license.Status) ? Visibility.Collapsed : Visibility.Visible;
        ActivateButton.Visibility = info.ShowAction ? Visibility.Visible : Visibility.Collapsed;

        var photo = ProfileStore.Load();
        if (photo is not null)
        {
            AvatarBorder.Background = new ImageBrush(photo) { Stretch = Stretch.UniformToFill };
            AvatarInitials.Visibility = Visibility.Collapsed;
        }
        else
        {
            AvatarBorder.Background = (Brush)FindResource("AccentBrush");
            AvatarInitials.Text = info.Initials;
            AvatarInitials.Visibility = Visibility.Visible;
        }
    }

    private void Profile_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => Go("license");

    private void Activate_Click(object sender, RoutedEventArgs e) => Go("license");

    private void ShowPage(string key)
    {
        if (!_backing && _current is not null && _current != key)
        {
            _history.Push(_current);
            while (_history.Count > 30) _history = new Stack<string>(_history.Reverse().Skip(1)); // guarda só os últimos
        }
        _current = key;
        if (key == "home") _history.Clear();

        // recurso do plano Pro sem licença válida: mostra o aviso no lugar da página (nunca com a aplicação desligada)
        if (!AppServices.License.IsAllowed(key))
        {
            ContentHost.Content = new UpgradePage(NavigationMap.TileOf(key)?.Title ?? "Plano Pro", key);
            return;
        }

        if (!_pages.TryGetValue(key, out var page))
        {
            page = Create(key);
            _pages[key] = page;
        }

        ContentHost.Content = page;
        if (page is SubPageHost host)
        {
            string target = BackTarget(key);
            host.SetBack(LabelOf(target), GoBack);
        }
    }

    private UserControl Create(string key)
    {
        if (key == "home") return new DashboardPage();
        if (key == "alltools") return new AllToolsPage(k => Go(k));

        var module = NavigationMap.Modules.FirstOrDefault(m => m.Key == key);
        if (module is not null && module.Tiles.Count == 1)
        {
            // módulo de uma página só (Gravação e transmissão): abre direto, sem a tela de atalhos
            var only = CreateInner(module.Tiles[0].Key, module.Tiles[0]);
            _inner[module.Tiles[0].Key] = only;
            return only;
        }

        if (module is not null) return new ModuleHubPage(module, k => Go(k));

        var tile = NavigationMap.TileOf(key);
        var owner = NavigationMap.ModuleOf(key);
        var inner = CreateInner(key, tile);
        _inner[key] = inner;
        return tile is null || owner is null ? inner : new SubPageHost(owner, tile, inner, k => Go(k));
    }

    private static UserControl CreateInner(string key, Tile? tile) => key switch
    {
        "cleanup" => new CleanupPage(),
        "memory" => new MemoryPage(),
        "dns" => new DnsPage(),
        "debloat" => new DebloatPage(),
        "sysinfo" => new SystemInfoPage(),
        "bench" => new BenchmarkPage(),
        "stress" => new StressPage(),
        "fps" => new FpsPage(),
        "gamefps" => new GameFpsPage(),
        "record" => new StudioPage(),
        "shorts" => new ShortsPage(),
        "storage" => new StoragePage(),
        "netanalyzer" => new NetAnalyzerPage(),
        "devices" => new DevicesPage(),
        "ghosts" => new GhostDevicesPage(),
        "gamesettings" => new GameSettingsPage(),
        "software" => new SoftwareHubPage(),
        "drivers" => new DriversPage(),
        "programs" => new TabsPage(("Instalar programas", "programs", () => new InstallAppsPage()),
                                   ("Atualizar e instalados", "programs-installed", () => new ProgramsPage())),
        "uninstaller" => new TabsPage(("Programas instalados", "programs-installed", () => new ProgramsPage(startWithInventory: true)),
                                      ("Aplicativos do Windows (bloatware)", "debloat", () => new DebloatPage())),
        "win11" => new Win11Page(),
        "diskmap" => new DiskMapPage(),
        "restorepoints" => new RestorePointsPage(),
        "formatbackup" => new FormatBackupPage(),
        "fakecheck" => new FakeCapacityPage(),
        "about" => new AboutPage(),
        "tweaks-all" => new TweaksPage("Otimizações",
            "Todos os ajustes do Windows num lugar só, separados por assunto. Nada muda até você clicar em Aplicar, e cada ajuste tem volta pela Central Desfazer.",
            TweakCategory.Performance, TweakCategory.Gaming, TweakCategory.Visual, TweakCategory.Privacy, TweakCategory.Network,
            TweakCategory.Storage, TweakCategory.Boot, TweakCategory.Peripherals),
        "hardware" => new HardwarePage(),
        "bios" => new BiosPage(),
        "recipe" => new RecipePage(),
        "monitor" => new LiveMonitorPage(),
        "compare" => new ComparePage(),
        "profiles" => new ProfilesPage(),
        "startup" => new StartupPage(),
        "repair" => new RepairPage(),
        "diskmgr" => new DiskManagerPage(),
        "license" => new LicensePage(),
        "support" => new SupportPage(),
        "remote" or "remote-console" => new RemoteAccessPage(),
        "processes" => new ProcessesPage(),
        "largefiles" => new LargeFilesPage(),
        "duplicates" => new DuplicatesPage(),
        "cleanextras" => new CleanExtrasPage(),
        "shredder" => new ShredderPage(),
        "autoshutdown" => new AutoShutdownPage(),
        "security" => new SecurityPage(),
        "undo" => new UndoCenterPage(),
        "diagnose" => new DiagnosticPage(),
        "postinstall" => new PostInstallPage(),
        "speedtest" => new InternetTestPage(),
        "periphtest" => new PeripheralTestPage(),
        "a11y" => new AccessibilityPage(),
        "recommended" => new RecommendedPage(),
        "cpu-detail" => new ComponentMonitorPage(gpu: false),
        "gpu-detail" => new ComponentMonitorPage(gpu: true),
        "ram-detail" => new RamPage(),
        "disks" => new DisksPage(),
        "removable" => new RemovablePage(),
        "latency" => new LatencyPage(),
        "gameboost" => new GameBoostPage(),
        "gamelibrary" => new GameLibraryPage(),
        "bottleneck" => new BottleneckPage(),
        "gameadvisor" => new GameAdvisorPage(),
        "wifi" => new WifiPage(),
        "visual" => new TweaksPage("Efeitos visuais",
            "Transparência e animações do Windows. Em PCs fracos, desligar libera GPU e deixa o Windows mais ágil.",
            TweakCategory.Visual),
        "tweaks-perf" => new TweaksPage("Desempenho",
            "Prioridade de CPU, plano de energia e serviços do Windows. Cada ajuste mostra o risco e pode ser restaurado.",
            TweakCategory.Performance),
        "tweaks-game" => new TweaksPage("Jogos e GPU",
            "Ajustes para jogar melhor: prioridade, gravação em segundo plano, energia na tomada e agendamento de GPU. Cada ajuste mostra o risco e pode ser desfeito.",
            TweakCategory.Gaming),
        "weekly" => new WeeklyPage(),
        "fans" => new FansPage(),
        "rgb" => new RgbPage(),
        "driver-removal" => new DriverRemovalPage(),
        "tweaks-storage" => new TweaksPage("Ajustes de armazenamento",
            "TRIM do SSD, hibernação, último acesso e nomes curtos do NTFS. Cada ajuste mostra o risco e pode ser desfeito.",
            TweakCategory.Storage),
        "net-tweaks" => new TweaksPage("Otimizações de rede",
            "Ajustes do Windows que afetam a rede. O ganho varia; meça o ping antes e depois no Início.",
            TweakCategory.Network),
        "privacy" => new TweaksPage("Privacidade",
            "Anúncios, histórico, Copilot, Cortana e SmartScreen.",
            TweakCategory.Privacy),
        "boot" => new TweaksPage("Inicialização e boot",
            "Como o Windows liga e desliga.",
            TweakCategory.Boot),
        "periph" => new TweaksPage("Periféricos",
            "Mouse e teclado.",
            TweakCategory.Peripherals),
        _ => new PlaceholderPage(tile?.Title ?? key, tile?.Description ?? ""),
    };
}
