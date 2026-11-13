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
        WorkArea.Attach(this);   // maximizada, respeita a barra de tarefas
        SourceInitialized += (_, _) => GlassEffect.Apply(this, WindowFrame, AppServices.Settings.GlassEffect);   // efeito cristal
        SizeChanged += (_, _) => GlassEffect.UpdateRegion(this, WindowFrame);
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
        Closed += (_, _) => _updates?.InstallPendingOnExit();   // atualização baixada: instala ao fechar
        AppServices.Videos.Start();   // vídeo novo do canal: aviso no canto e faixa no Painel
        Loaded += async (_, _) => await _updates.CheckAsync();
        ContentRendered += (_, _) => { Pages.WhatsNew.ShowOnceAfterUpdate(this); TrialEnd.ShowIfNeeded(this); };
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
                if (e.Key == System.Windows.Input.Key.F && !(e.OriginalSource is System.Windows.Controls.TextBox))
                {
                    NavSearch.Focus();   // Ctrl + F: busca de ferramentas no menu
                    NavSearch.SelectAll();
                    e.Handled = true;
                    return;
                }
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
        VersionButton.ToolTip = VersionTip;
        Loaded += (_, _) => SetupTrayAndWidget();
        if (App.StartInTray)
        {
            // aberto pelo Windows ao entrar: só a bandeja (e a barra flutuante, se ligada)
            ShowInTaskbar = false;
            WindowState = WindowState.Minimized;
            Loaded += (_, _) => { Hide(); ShowInTaskbar = true; WindowState = WindowState.Normal; };
        }
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
    private Widget.TrayPanel? _panel;

    /// <summary>Abre (ou fecha) o painel MB ao lado do relógio.</summary>
    internal void ShowTrayPanel() => ShowTrayPanel(null);

    /// <summary>Abre o Painel MB; com <paramref name="anchor"/> (a barra flutuante), aparece colado a ela.</summary>
    internal void ShowTrayPanel(Rect? anchor)
    {
        if (_panel is not null) { _panel.Close(); return; }
        _panel = new Widget.TrayPanel((key, auto) => { ShowFromTray(); Go(key, auto); }, ShowFromTray, anchor);
        _panel.Closed += (_, _) => _panel = null;
        _panel.Show();
        _panel.Activate();
    }

    /// <summary>Executa uma ação pedida de fora (botão direito do Windows).</summary>
    internal async void RunAction(MBExtreme.Core.Windows.ShellAction action, string? path)
    {
        if (action.Key == "modojogo")
        {
            if (AppServices.Boost.IsActive) await AppServices.Boost.DeactivateAsync();
            else await AppServices.Boost.ActivateNowAsync();
            ShowTrayPanel();
            return;
        }
        ShowFromTray();
        Go(action.Page, action.AutoStart);
        if (path is not null) SetStatusText($"{action.Title}: {path}");
    }

    private void SetStatusText(string text)
    {
        if (_inner.Values.OfType<Pages.ToolPageBase>().FirstOrDefault(p => p.IsVisible) is { } page) page.ShowStatus(text);
    }

    internal void ActivateFromOther()
    {
        if (App.TakeQueuedAction() is { } queued) { RunAction(queued.Action, queued.Path); return; }
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

    private TrayIcon? _tray;
    private Widget.FloatingWidget? _widget;
    private bool _reallyExit;

    /// <summary>Liga a bandeja e o widget (chamado uma vez, ao abrir).</summary>
    private void SetupTrayAndWidget()
    {
        _tray = new TrayIcon(this, ShowFromTray, ToggleWidget, () => AppServices.Settings.WidgetEnabled, ExitApp, ShowTrayPanel);
        if (AppServices.Settings.WidgetEnabled) ShowWidget();
        // botão direito do Windows ligado: regrava (o caminho do programa pode ter mudado numa atualização)
        if (AppServices.Settings.ShellMenuEnabled && Environment.ProcessPath is { } exe)
        {
            try { MBExtreme.Core.Windows.ShellActions.Install(exe); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException) { }
        }
        if (App.PendingAction is { } pending) { App.PendingAction = null; RunAction(pending.Action, pending.Path); }
        Closing += MainWindow_Closing;
        Closed += (_, _) => { _tray?.Dispose(); _widget?.Close(); };
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // X com "ir para a bandeja": esconde em vez de fechar (a não ser que seja "Sair" de verdade)
        if (_reallyExit || !AppServices.Settings.MinimizeToTray) return;
        e.Cancel = true;
        Hide();
    }

    internal void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true; Topmost = false;
    }

    /// <summary>Liga/desliga o widget pela tela de Acessibilidade.</summary>
    internal void SetWidget(bool on)
    {
        AppServices.Settings.WidgetEnabled = on;
        AppServices.Settings.Save();
        if (on) ShowWidget();
        else { _widget?.Close(); _widget = null; }
    }

    private void ToggleWidget()
    {
        if (_widget is not null) { AppServices.Settings.WidgetEnabled = false; AppServices.Settings.Save(); _widget.Close(); _widget = null; }
        else { AppServices.Settings.WidgetEnabled = true; AppServices.Settings.Save(); ShowWidget(); }
    }

    private void ShowWidget()
    {
        if (_widget is not null) { _widget.Activate(); return; }
        _widget = new Widget.FloatingWidget(ShowFromTray, ShowTrayPanel);
        _widget.Closed += (_, _) => _widget = null;
        _widget.Show();
    }

    private void ExitApp()
    {
        _reallyExit = true;
        Close();
        Application.Current.Shutdown();
    }

    /// <summary>Maximizada, a janela sem moldura ultrapassa a tela pela borda de redimensionamento: compensa com margem e troca o ícone.</summary>
    private void UpdateMaximizeState()
    {
        bool maximized = WindowState == WindowState.Maximized;
        // o tamanho maximizado já vem certo do Windows (WorkArea): sem margem extra, senão sobra uma faixa
        WindowFrame.Margin = new Thickness(0);
        WindowFrame.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(22);
        GlassSheen.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(21);
        GlassEffect.UpdateRegion(this, WindowFrame);
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = maximized ? "Restaurar" : "Maximizar";
    }

    /// <summary>Liga ou desliga o efeito cristal (tela Acessibilidade).</summary>
    internal void SetGlass(bool on) => GlassEffect.Apply(this, WindowFrame, on);

    /// <summary>Janela principal, para as páginas pedirem navegação (ex.: o painel abre a limpeza).</summary>
    internal static MainWindow? Current { get; private set; }

    /// <summary>Abre uma página pelo nome e destaca o módulo dela no menu lateral.</summary>
    internal void Go(string key, bool autoStart = false, bool deep = false)
    {
        string? sideKey = key == "home" ? "home" : NavigationMap.SideKeyOf(key);

        _suppressNav = true;
        foreach (var radio in NavRadios())
            radio.IsChecked = (string?)radio.Tag == sideKey;
        if (sideKey is not null) RevealInMenu(sideKey);
        _suppressNav = false;

        // ferramenta que fica numa tela de abas: abre a tela e escolhe a aba
        UserControl? inner = null;
        if (NavigationMap.HubOf(key) is { } hub && !NavigationMap.Hubs.ContainsKey(key))
        {
            ShowPage(hub);
            if (_inner.TryGetValue(hub, out var hubPage) && hubPage is TabsPage tabs) inner = tabs.Select(key);
        }
        else
        {
            ShowPage(key);
            _inner.TryGetValue(key, out inner);
        }

        if (autoStart && inner is not null)
        {
            if (inner is CleanupPage cleanup) cleanup.StartScan();
            else if (inner is DiagnosticPage diagnose) _ = deep ? diagnose.StartDeepAsync() : diagnose.StartAsync();
        }
    }

    private readonly Dictionary<string, (StackPanel Items, Action<bool> Set)> _sections = new();

    private void BuildMenu()
    {
        // Menu em seções, como um painel de controle: cada item abre a tela direto. Cada seção recolhe pela setinha (fica guardado).
        bool first = true;
        foreach (var section in NavigationMap.Sidebar)
        {
            var items = new StackPanel();
            if (section.Header.Length > 0)
            {
                string header = section.Header;
                var chevron = new TextBlock
                {
                    Text = "\uE70D", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 8.5, VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Brush)FindResource("MutedBrush"),
                };
                var label = new TextBlock { Text = header, FontSize = 9.5, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("MutedBrush") };
                var row = new DockPanel { LastChildFill = true };
                DockPanel.SetDock(chevron, Dock.Right);
                row.Children.Add(chevron);
                row.Children.Add(label);
                var toggle = new Button
                {
                    Content = row, Style = (Style)FindResource("SideHeaderButton"), Margin = new Thickness(0, first ? 2 : 8, 0, 1),
                    ToolTip = "Clique para recolher ou abrir " + header.ToLowerInvariant(),
                };
                bool open = !AppServices.Settings.CollapsedSections.Contains(header);
                void Set(bool value)
                {
                    open = value;
                    Motion.Toggle(items, open);
                    Motion.RotateTo(chevron, open ? 0 : -90);
                    System.Windows.Automation.AutomationProperties.SetName(toggle, $"{header}: {(open ? "aberta" : "recolhida")}");
                    var list = AppServices.Settings.CollapsedSections;
                    if (open) list.Remove(header); else if (!list.Contains(header)) list.Add(header);
                }
                toggle.Click += (_, _) => { Set(!open); AppServices.Settings.Save(); };
                NavPanel.Children.Add(toggle);
                // estado inicial sem animação
                items.LayoutTransform = new ScaleTransform(1, open ? 1 : 0);
                items.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                Motion.RotateTo(chevron, open ? 0 : -90);
                System.Windows.Automation.AutomationProperties.SetName(toggle, $"{header}: {(open ? "aberta" : "recolhida")}");
                _sections[header] = (items, Set);
            }
            NavPanel.Children.Add(items);
            foreach (var item in section.Items)
                AddNavItem(items, item.Key, item.Title, item.Icon, item.Key == "home");
            first = false;
        }
    }

    // ---------------------------------------------------------------- busca no menu

    private void NavSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        string query = NavSearch.Text;
        NavSearchHint.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var results = NavigationMap.Search(query);
        bool searching = query.Trim().Length >= 2;
        NavPanel.Visibility = searching ? Visibility.Collapsed : Visibility.Visible;
        SearchResults.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
        SearchResults.Children.Clear();
        if (!searching) return;
        if (results.Count == 0)
        {
            SearchResults.Children.Add(new TextBlock { Text = "Nada encontrado. Tente outra palavra (ex.: ventoinha, DNS, BIOS).", FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 4, 8, 4), Foreground = (Brush)FindResource("MutedBrush") });
            return;
        }
        foreach (var (tile, module) in results)
        {
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = tile.Title, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = module.Title, FontSize = 10, Foreground = (Brush)FindResource("MutedBrush"), TextTrimming = TextTrimming.CharacterEllipsis });
            var row = new DockPanel();
            var icon = new TextBlock { Text = tile.Icon, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            row.Children.Add(text);
            var button = new Button { Content = row, Style = (Style)FindResource("SideFooterButton"), HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 2), Tag = tile.Key, ToolTip = tile.Description };
            System.Windows.Automation.AutomationProperties.SetName(button, tile.Title);
            button.Click += (_, _) => OpenSearchResult(tile.Key);
            SearchResults.Children.Add(button);
        }
    }

    private void NavSearch_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape) { NavSearch.Text = ""; e.Handled = true; }
        else if (e.Key == System.Windows.Input.Key.Enter && SearchResults.Children.OfType<Button>().FirstOrDefault() is { Tag: string key })
        {
            OpenSearchResult(key);
            e.Handled = true;
        }
    }

    private void OpenSearchResult(string key)
    {
        NavSearch.Text = "";
        Go(key);
    }

    /// <summary>Abre a seção do menu que tem o item (quando a tela é aberta por outro caminho com a seção recolhida).</summary>
    private void RevealInMenu(string sideKey)
    {
        foreach (var section in NavigationMap.Sidebar)
            if (section.Header.Length > 0 && section.Items.Any(i => i.Key == sideKey) && _sections.TryGetValue(section.Header, out var s) && s.Items.Visibility != Visibility.Visible)
                s.Set(true);
    }

    private static readonly Brush IconIdle = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x96));
    private static readonly Brush IconActive = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00));

    private void AddNavItem(Panel host, string key, string title, string glyph, bool selected)
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
        radio.MouseEnter += (_, _) => Motion.Pop(icon);
        Controls.MouseGlow.Attach(radio);   // brilho que acompanha o mouse
        radio.Checked += (_, _) => { icon.Foreground = IconActive; Motion.Pop(icon); };
        radio.Unchecked += (_, _) => icon.Foreground = IconIdle;
        radio.Checked += Nav_Checked;
        host.Children.Add(radio);
    }

    private void FontSize_Click(object sender, RoutedEventArgs e) => Go("a11y");

    private void CheckUpdate_Click(object sender, RoutedEventArgs e) => CheckForUpdates();

    /// <summary>Procura atualização e sempre responde (rodapé do menu e tela Sobre).</summary>
    internal async void CheckForUpdates()
    {
        if (_updates is null) return;
        VersionText.Text = "  Procurando...";
        var result = await _updates.CheckNowAsync();
        VersionText.Text = VersionLabel;
        _updates.ShowCheckResult(this, result);
    }

    /// <summary>Texto curto no rodapé; a versão completa fica na dica do botão e na tela Sobre.</summary>
    private const string VersionLabel = "  Verificar atualização";

    private static string VersionTip =>
        $"Versão {MBExtreme.Core.AppInfo.Version} · compilação {MBExtreme.Core.Update.AppUpdates.CurrentBuild()}. Clique para procurar uma versão nova.";

    private IEnumerable<RadioButton> NavRadios() => NavPanel.Children.OfType<StackPanel>().SelectMany(p => p.Children.OfType<RadioButton>());

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
        key == "home" ? "Painel" : key == "alltools" ? "Todas as ferramentas" : NavigationMap.Modules.FirstOrDefault(m => m.Key == key)?.Title ?? NavigationMap.TileOf(key)?.Title
            ?? NavigationMap.Sidebar.SelectMany(s => s.Items).FirstOrDefault(i => i.Key == key)?.Title ?? "Voltar";

    /// <summary>Volta para a tela de onde o usuário veio (o Início, se ele chegou por lá; senão, o módulo da página).</summary>
    /// <summary>Monta a página de novo (ex.: depois de carregar um perfil do Estúdio) e mostra uma mensagem nela.</summary>
    internal void Reopen(string key, string? message = null)
    {
        _pages.Remove(key);
        _inner.Remove(key);
        foreach (var module in NavigationMap.Modules.Where(m => m.Tiles.Count == 1 && m.Tiles[0].Key == key)) _pages.Remove(module.Key);
        bool backing = _backing;
        _backing = true;   // não empilha a mesma página no histórico
        try { ShowPage(_current ?? key); }
        finally { _backing = backing; }
        if (message is not null && _inner.TryGetValue(key, out var inner) && inner is MBExtreme.UI.Pages.ToolPageBase page)
            page.Dispatcher.BeginInvoke(new Action(() => page.ShowStatus(message, "OkBrush")), System.Windows.Threading.DispatcherPriority.Loaded);
    }

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
        // com a licença aplicada: durante o teste, a contagem; depois, "Grátis"
        license.RefreshTrial();
        BadgeText.Text = license.Config.Enforce && !MBExtreme.Core.Licensing.LicenseExpiry.IsPro(license.Status) && license.Trial.Active
            ? $" Teste Pro: {license.Trial.LeftText}"
            : " Licença: Grátis";
        System.Windows.Automation.AutomationProperties.SetName(FreeBadge, BadgeText.Text.Trim());
        ActivateButton.Visibility = info.ShowAction ? Visibility.Visible : Visibility.Collapsed;

        var photo = AccountPhoto.Current();
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

    private void Activate_Click(object sender, RoutedEventArgs e)
    {
        // sem conta: já abre o login (teste grátis); com conta: a tela de licença (planos)
        if (AppServices.License.Status.Payload is null && AppServices.License.Config.Configured) Pages.LicensePage.LoginOnOpen = true;
        Go("license");
    }

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

        bool changed = !ReferenceEquals(ContentHost.Content, page);
        ContentHost.Content = page;
        if (changed) Motion.Enter(page, fromY: 14);
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

    /// <summary>Tela de abas (Limpeza, Dispositivos e discos...): cada aba é a ferramenta de sempre.</summary>
    private static TabsPage CreateHub(string key) => new(key == "hub-clean" ? TurboStrip.Build() : null,
        NavigationMap.Hubs[key].Select(t => (t.Title, t.Key, (Func<UserControl>)(() => CreateInner(t.Key, NavigationMap.TileOf(t.Key))))).ToArray());

    private static UserControl CreateInner(string key, Tile? tile) => NavigationMap.Hubs.ContainsKey(key) ? CreateHub(key) : key switch
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
        "programs-install" => new InstallAppsPage(),
        "programs-installed" => new ProgramsPage(),
        "uninstaller" => new ProgramsPage(startWithInventory: true),
        "services" => new ServicesPage(),
        "mbc-overview" => new MbOverviewPage(),
        "mbc-emu" => new MbEmulationPage(),
        "mbc-kbm" => new MbKbmPage(),
        "mbc-light" => new MbLightPage(),
        "pad-info" => new PadInfoPage(),
        "pad-drivers" => new PadDriversPage(),
        "pad-profiles" => new PadProfilesPage(),
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
        "wallpaper" => new WallpaperPage(),
        "license" => new LicensePage(),
        "support" or "remote" or "remote-console" => new RemoteAccessPage(),   // Suporte: uma tela só, estilo AnyDesk
        "social" => new SocialPage(),
        "suggest" => new SuggestionsPage(),
        "news" => new NewsPage(),
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
        "padcalib" => new PadCalibrationPage(),
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
