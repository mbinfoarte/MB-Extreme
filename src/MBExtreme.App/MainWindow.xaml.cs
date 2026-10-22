using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Tweaks;
using MBExtreme.UI.Pages;

namespace MBExtreme.UI;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, UserControl> _pages = new();
    private readonly Dictionary<string, UserControl> _inner = new();
    private bool _suppressNav;

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
        };
        Go("home");
        if (AppServices.Boost.Settings.BoostAuto) AppServices.Boost.SetAuto(true);
        AppServices.AutoMemory.StartIfEnabled();
        AppServices.License.Changed += (_, _) => Dispatcher.BeginInvoke(new Action(UpdateFooter));
        AppServices.License.Start();
        UpdateFooter();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Maximizada, a janela sem moldura ultrapassa a tela pela borda de redimensionamento: compensa com margem e troca o ícone.</summary>
    private void UpdateMaximizeState()
    {
        bool maximized = WindowState == WindowState.Maximized;
        RootGrid.Margin = maximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = maximized ? "Restaurar" : "Maximizar";
    }

    /// <summary>Janela principal, para as páginas pedirem navegação (ex.: o painel abre a limpeza).</summary>
    internal static MainWindow? Current { get; private set; }

    /// <summary>Abre uma página pelo nome e destaca o módulo dela no menu lateral.</summary>
    internal void Go(string key, bool autoStart = false)
    {
        string moduleKey = key == "home" ? "home" : NavigationMap.ModuleOf(key)?.Key ?? "home";

        _suppressNav = true;
        foreach (var radio in NavRadios())
            radio.IsChecked = (string?)radio.Tag == moduleKey;
        _suppressNav = false;

        ShowPage(key);

        if (autoStart && _inner.TryGetValue(key, out var inner) && inner is CleanupPage cleanup)
            cleanup.StartScan();
    }

    private void BuildMenu()
    {
        AddNavItem("home", "Painel", "\uE80F", true);
        foreach (var module in NavigationMap.Modules)
            AddNavItem(module.Key, module.Title, module.Icon, false);
    }

    private void AddNavItem(string key, string title, string glyph, bool selected)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock
        {
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            FontSize = 18,
            Text = glyph,
            Width = 28,
            VerticalAlignment = VerticalAlignment.Center,
        });
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var radio = new RadioButton
        {
            Tag = key,
            GroupName = "nav",
            IsChecked = selected,
            Style = (Style)FindResource("NavItem"),
            Content = content,
        };
        radio.Checked += Nav_Checked;
        NavPanel.Children.Add(radio);
    }

    private IEnumerable<RadioButton> NavRadios() => NavPanel.Children.OfType<RadioButton>();

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        // O evento dispara durante o carregamento do XAML, antes de ContentHost existir.
        if (_suppressNav || ContentHost is null) return;
        if (sender is RadioButton { Tag: string key }) ShowPage(key);
    }

    private void UpdateFooter() => FooterText.Text = "Beta 01 · " + AppServices.License.Summary;

    private void ShowPage(string key)
    {
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
    }

    private UserControl Create(string key)
    {
        if (key == "home") return new DashboardPage();

        var module = NavigationMap.Modules.FirstOrDefault(m => m.Key == key);
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
        "record" => new StudioPage(),
        "storage" => new StoragePage(),
        "netanalyzer" => new NetAnalyzerPage(),
        "devices" => new DevicesPage(),
        "ghosts" => new GhostDevicesPage(),
        "gamesettings" => new GameSettingsPage(),
        "drivers" => new DriversPage(),
        "programs" => new ProgramsPage(),
        "hardware" => new HardwarePage(),
        "bios" => new BiosPage(),
        "recipe" => new RecipePage(),
        "monitor" => new LiveMonitorPage(),
        "compare" => new ComparePage(),
        "profiles" => new ProfilesPage(),
        "startup" => new StartupPage(),
        "repair" => new RepairPage(),
        "license" => new LicensePage(),
        "processes" => new ProcessesPage(),
        "largefiles" => new LargeFilesPage(),
        "duplicates" => new DuplicatesPage(),
        "cleanextras" => new CleanExtrasPage(),
        "shredder" => new ShredderPage(),
        "autoshutdown" => new AutoShutdownPage(),
        "security" => new SecurityPage(),
        "recommended" => new RecommendedPage(),
        "cpu-detail" => new ComponentMonitorPage(gpu: false),
        "gpu-detail" => new ComponentMonitorPage(gpu: true),
        "ram-detail" => new RamPage(),
        "disks" => new DisksPage(),
        "removable" => new RemovablePage(),
        "latency" => new LatencyPage(),
        "gameboost" => new GameBoostPage(),
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
        "net-tweaks" => new TweaksPage("Otimizações de rede",
            "Ajustes do Windows que afetam a rede. O ganho varia; meça o ping antes e depois no Painel.",
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
