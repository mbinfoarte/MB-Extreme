using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MBExtreme.Core.Licensing;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Conta e licença, no formato do esboço aprovado: perfil no topo (foto com botão de câmera, nome, selo do plano e os botões principais),
/// cartão "Seu plano" (situação, grátis × Pro ou o tempo restante do Pro), cartão "Este computador" (nome, situação, versão, última verificação)
/// e as opções do servidor de licenças recolhidas em "Opções avançadas" (só para testes).
/// </summary>
public sealed class LicensePage : ToolPageBase
{
    private readonly LicenseManager _license = AppServices.License;
    private readonly ContentControl _profile = new();
    private readonly ContentControl _plan = new();
    private readonly ContentControl _computer = new();
    private readonly TextBlock _code = new() { FontFamily = new FontFamily("Consolas"), FontSize = 34, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Border _codeCard;
    private readonly TextBox _url = new() { MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 6, 0, 8) };
    private readonly TextBox _key = new() { MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 6, 0, 8) };
    private CancellationTokenSource? _loginCts;

    public LicensePage() : base("Conta e licença", "Seu perfil, o plano deste computador e onde gerenciar a assinatura.")
    {
        Body.Children.Add(_profile);

        var codePanel = new StackPanel();
        codePanel.Children.Add(new TextBlock { Text = "Confirme este código no navegador", FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brush("MutedBrush") });
        codePanel.Children.Add(_code);
        _codeCard = CardOf(codePanel);
        _codeCard.Visibility = Visibility.Collapsed;
        Body.Children.Add(_codeCard);

        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 260 });
        _plan.Margin = new Thickness(0, 0, 10, 0);
        grid.Children.Add(_plan);
        Grid.SetColumn(_computer, 1);
        grid.Children.Add(_computer);
        Body.Children.Add(grid);

        // Endereço do servidor e chave pública já vêm no programa; o painel de testes só aparece com MBX_LICENSE_DEV=1.
        if (Environment.GetEnvironmentVariable("MBX_LICENSE_DEV") == "1") Body.Children.Add(BuildAdvanced());

        Loaded += (_, _) => { _license.Changed += OnChanged; Render(); };
        Unloaded += (_, _) => _license.Changed -= OnChanged;
        Render();
    }

    private void OnChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(Render));

    private bool SignedIn => _license.Status.Payload is not null && _license.Status.State != LicenseState.Invalid;

    private void Render()
    {
        AccountPhoto.Sync(_license.Status.Payload?.Picture, () => { Render(); MainWindow.Current?.UpdateFooter(); });
        _profile.Content = BuildProfile();
        _plan.Content = BuildPlan();
        _computer.Content = BuildComputer();
    }

    // ---------------------------------------------------------------- perfil

    private UIElement BuildProfile()
    {
        var status = _license.Status;
        var config = _license.Config;
        var info = LicenseProfile.Describe(status, config, DateTimeOffset.UtcNow, Environment.UserName);

        var row = new DockPanel { LastChildFill = true };

        // foto com o botão de câmera (trocar) e um menu para remover
        var photoHost = new Grid { Width = 84, Height = 84, Margin = new Thickness(0, 0, 22, 0), VerticalAlignment = VerticalAlignment.Center };
        var ring = new System.Windows.Shapes.Ellipse { Stroke = Brush("AccentBrush"), StrokeThickness = 2 };
        if (AccountPhoto.Current() is { } photo) ring.Fill = new ImageBrush(photo) { Stretch = Stretch.UniformToFill };
        else ring.Fill = Brush("SurfaceBrush");
        photoHost.Children.Add(ring);
        if (ring.Fill is not ImageBrush)
            photoHost.Children.Add(new TextBlock { Text = info.Initials, FontSize = 28, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        var camera = new Button
        {
            Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Cursor = System.Windows.Input.Cursors.Hand,
            Style = (Style)FindResource("PrimaryButton"), Padding = new Thickness(0), ToolTip = "Trocar a foto (clique com o botão direito para remover)",
            Content = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13 },
        };
        camera.Template = RoundTemplate();
        System.Windows.Automation.AutomationProperties.SetName(camera, "Trocar foto do perfil");
        camera.Click += (_, _) => ChoosePhoto();
        var menu = new ContextMenu();
        var remove = new MenuItem { Header = "Remover foto" };
        remove.Click += (_, _) => { ProfileStore.Remove(); MainWindow.Current?.UpdateFooter(); Render(); };
        remove.ToolTip = "Remove a foto escolhida no programa (a da conta do site continua).";
        menu.Items.Add(remove);
        camera.ContextMenu = menu;
        photoHost.Children.Add(camera);
        DockPanel.SetDock(photoHost, Dock.Left);
        row.Children.Add(photoHost);

        // botões à direita
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        if (SignedIn)
        {
            actions.Children.Add(ActionButton("Minha conta no site", OpenAccount, false));
            actions.Children.Add(ActionButton("Sair", async () => await SignOutAsync(), false));
        }
        else
        {
            if (config.Configured) actions.Children.Add(ActionButton("Criar conta grátis", () => OpenSite("/cadastro"), false));
            var login = ActionButton("Entrar e ativar este PC", async () => await LoginAsync(), true);
            login.IsEnabled = config.Configured && _loginCts is null;
            if (!config.Configured) login.ToolTip = "Disponível quando o licenciamento for ligado (no beta tudo já está liberado).";
            actions.Children.Add(login);
        }
        DockPanel.SetDock(actions, Dock.Right);
        row.Children.Add(actions);

        // nome, selo e linha de baixo
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        nameRow.Children.Add(new TextBlock { Text = info.Name, FontSize = 20, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        bool pro = LicenseExpiry.IsPro(status);
        nameRow.Children.Add(Pill(pro ? FeaturePolicy.Title(status.Plan).ToUpperInvariant() : config.Configured ? "GRÁTIS" : "BETA ABERTO", pro));
        text.Children.Add(nameRow);
        string under = SignedIn ? $"{status.Payload!.Email} · conta conectada" : "Conta não conectada · a foto e o nome ficam só neste computador";
        text.Children.Add(new TextBlock { Text = under, FontSize = 13, Foreground = Brush("MutedBrush"), Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        row.Children.Add(text);

        var card = CardOf(row);
        card.Padding = new Thickness(24, 20, 24, 20);
        card.Margin = new Thickness(0, 0, 0, 14);
        return card;
    }

    private Border Pill(string text, bool filled) => new()
    {
        Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(9, 3, 9, 3), CornerRadius = new CornerRadius(999), VerticalAlignment = VerticalAlignment.Center,
        Background = filled ? Brush("AccentBrush") : new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xC4, 0x00)),
        Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = filled ? Brushes.Black : Brush("AccentBrush") },
    };

    private Button ActionButton(string text, Action click, bool primary)
    {
        var b = new Button { Content = text, Style = (Style)FindResource(primary ? "PrimaryButton" : "GhostButton"), Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(18, 10, 18, 10), MinHeight = 44 };
        b.Click += (_, _) => click();
        return b;
    }

    private static ControlTemplate RoundTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(16));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x15, 0x17, 0x1B)));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(2));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    private void ChoosePhoto()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp", Title = "Escolha sua foto" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            ProfileStore.Save(dialog.FileName);
            MainWindow.Current?.UpdateFooter();
            SetStatus("Foto atualizada.", "OkBrush");
            Render();
        }
        catch (Exception ex) when (ex is System.IO.IOException or NotSupportedException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            SetStatus("Não foi possível usar esta imagem: " + ex.Message, "BadBrush");
        }
    }

    // ---------------------------------------------------------------- seu plano

    private UIElement BuildPlan()
    {
        var status = _license.Status;
        var config = _license.Config;
        var panel = new StackPanel();
        var head = new DockPanel();
        bool pro = LicenseExpiry.IsPro(status);
        bool trial = status.Payload?.Status == "trialing";
        var link = pro && !trial ? ActionButton("Gerenciar assinatura", OpenAccount, false) : ActionButton(trial ? "Assinar um plano" : "Ver planos e preços", () => OpenSite("/precos?produto=mb-extreme"), false);
        link.Margin = new Thickness(12, 0, 0, 0);
        link.VerticalAlignment = VerticalAlignment.Top;
        link.IsEnabled = config.Configured;
        DockPanel.SetDock(link, Dock.Right);
        head.Children.Add(link);

        (string title, string detail, string brush) = (status.State, config.Configured) switch
        {
            (LicenseState.Valid, _) when pro && trial => ($"Teste grátis · Plano {FeaturePolicy.Title(status.Plan)}", status.Payload!.PeriodEnd is { } tEnd ? $"Termina em {tEnd.LocalDateTime:dd/MM/yyyy HH:mm}. Depois, assine um plano para continuar com os recursos pagos." : "Teste grátis ativo.", "TextBrush"),
            (LicenseState.Valid, _) when pro => ($"Plano {FeaturePolicy.Title(status.Plan)}", status.Payload!.PeriodEnd is { } end ? $"Renova em {end.LocalDateTime:dd/MM/yyyy}. Sem internet, a licença continua valendo até {status.Payload.ExpiresAt.LocalDateTime:dd/MM/yyyy}." : "A licença se renova sozinha quando há internet.", "TextBrush"),
            (LicenseState.Valid, _) => ("Grátis", "Os recursos gratuitos estão liberados. Os planos Essencial, Gamer, Creator e Técnico liberam o resto.", "TextBrush"),
            (LicenseState.Expired, _) => ("Licença vencida", status.Message, "WarnBrush"),
            (LicenseState.ClockTampered, _) => ("Relógio incorreto", status.Message, "WarnBrush"),
            (LicenseState.WrongDevice, _) => ("Licença de outro computador", status.Message, "BadBrush"),
            (LicenseState.Invalid, _) => ("Licença inválida", status.Message, "BadBrush"),
            (_, false) => ("Beta aberto · tudo liberado", "Durante o beta todos os recursos funcionam sem licença. Quando o beta acabar, os recursos Pro pedem o plano.", "TextBrush"),
            _ => ("Sem licença", config.Enforce ? "Entre com a conta do site para liberar os recursos do plano Pro." : "Entre com a conta do site para ativar este PC. Enquanto isso, tudo está liberado (beta aberto).", "WarnBrush"),
        };
        var titles = new StackPanel();
        titles.Children.Add(Label("SEU PLANO"));
        titles.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 6, 0, 0), Foreground = Brush(brush) });
        titles.Children.Add(new TextBlock { Text = detail, FontSize = 13, Foreground = Brush("MutedBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        head.Children.Add(titles);
        panel.Children.Add(head);
        if (_license.LastNotice is { Length: > 0 } notice) panel.Children.Add(Muted(notice, 12));

        if (pro && status.DaysLeft(DateTimeOffset.UtcNow) is int days)
        {
            var bar = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            var labels = new DockPanel();
            var right = new TextBlock { Text = days == 1 ? "1 dia restante" : $"{days} dias restantes", FontSize = 12, Foreground = Brush("MutedBrush") };
            DockPanel.SetDock(right, Dock.Right);
            labels.Children.Add(right);
            labels.Children.Add(new TextBlock { Text = "Tempo de plano", FontSize = 12, Foreground = Brush("MutedBrush") });
            bar.Children.Add(labels);
            int max = trial ? MBExtreme.Core.AppInfo.TrialDays : days > 31 ? 365 : 31;
            var progress = new ProgressBar { Maximum = max, Value = Math.Clamp(days, 0, max), Height = 8, Margin = new Thickness(0, 8, 0, 0) };
            progress.Style = (Style)FindResource("GradientProgress");
            bar.Children.Add(progress);
            panel.Children.Add(bar);
        }

        // grátis × Pro (ou o que o Pro inclui)
        var columns = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        if (!pro)
        {
            columns.Children.Add(FeatureBox("Grátis para sempre", new[] { "Painel e diagnóstico", "Limpeza e otimizações seguras", "Drivers e programas", "Suporte e acesso remoto" }, false));
        }
        var proBox = FeatureBox(pro ? $"Incluído no plano {FeaturePolicy.Title(status.Plan)}" : "Planos pagos", PlanFeatures(pro ? status.Plan : null), true);
        if (pro) Grid.SetColumnSpan(proBox, 2); else Grid.SetColumn(proBox, 1);
        columns.Children.Add(proBox);
        panel.Children.Add(columns);

        var card = CardOf(panel);
        card.Padding = new Thickness(24, 20, 24, 20);
        return card;
    }

    /// <summary>O que cada plano libera (cada um inclui o anterior).</summary>
    private static IEnumerable<string> PlanFeatures(string? plan)
    {
        int rank = FeaturePolicy.Rank(plan);
        if (rank == 0)
        {
            yield return "Essencial (R$ 24,90/mês): otimização completa, drivers e limpeza avançada";
            yield return "Gamer (R$ 39,90/mês): jogos, Modo Jogo 2.0, DLSS/FSR e benchmark 3D";
            yield return "Creator (R$ 69,90/mês): estúdio de live e gravação";
            yield return "Técnico (R$ 99,90/mês): pós-formatação, backup e 5 PCs";
            yield break;
        }
        yield return "Otimização completa, drivers, limpeza avançada e rede";
        if (rank >= 2) yield return "Jogos: Modo Jogo 2.0, biblioteca com DLSS/FSR, benchmark 3D e FPS";
        if (rank >= 3) yield return "Estúdio: live, gravação e cortes verticais";
        if (rank >= 4) yield return "Técnico: pós-formatação, backup para formatar e até 5 PCs";
        yield return "Atualizações automáticas";
    }

    private Border FeatureBox(string title, IEnumerable<string> items, bool highlight)
    {
        var p = new StackPanel();
        p.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = highlight ? Brush("AccentBrush") : Brush("TextBrush"), Margin = new Thickness(0, 0, 0, 8) });
        foreach (string item in items)
            p.Children.Add(new TextBlock { Text = "✓  " + item, FontSize = 13, Margin = new Thickness(0, 2, 0, 2), TextWrapping = TextWrapping.Wrap });
        return new Border
        {
            Child = p, Padding = new Thickness(16, 14, 16, 14), CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, highlight ? 0 : 6, 0),
            Background = highlight ? new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xC4, 0x00)) : Brush("BgBrush"),
            BorderBrush = highlight ? new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xC4, 0x00)) : Brush("BorderBrush"), BorderThickness = new Thickness(1),
        };
    }

    private TextBlock Label(string text) => new() { Text = text, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Brush("MutedBrush") };

    // ---------------------------------------------------------------- este computador

    private UIElement BuildComputer()
    {
        var status = _license.Status;
        var config = _license.Config;
        var panel = new DockPanel();
        var top = new StackPanel();
        top.Children.Add(Label("ESTE COMPUTADOR"));
        string situation = status.State switch
        {
            LicenseState.Valid => "Ativado",
            LicenseState.Expired => "Vencido",
            LicenseState.WrongDevice => "Licença de outro PC",
            LicenseState.Invalid => "Licença inválida",
            LicenseState.ClockTampered => "Corrija a data e a hora",
            _ => config.Configured ? "Não ativado" : "Liberado (beta)",
        };
        string situationBrush = status.State == LicenseState.Valid || !config.Configured ? "OkBrush" : status.State is LicenseState.Expired or LicenseState.ClockTampered ? "WarnBrush" : config.Configured && status.Payload is null ? "MutedBrush" : "BadBrush";
        var device = _license.Device;
        top.Children.Add(Fact("Nome", Environment.MachineName));
        top.Children.Add(Fact("Situação", situation, situationBrush));
        if (device.Os.Length > 0) top.Children.Add(Fact("Windows", device.Os));
        if (device.Cpu.Length > 0) top.Children.Add(Fact("Processador", device.Cpu));
        if (device.Gpu.Length > 0) top.Children.Add(Fact("Placa de vídeo", device.Gpu));
        if (device.RamGb > 0) top.Children.Add(Fact("Memória", $"{device.RamGb} GB"));
        if ((_mac ??= DeviceFacts.PrimaryMac()) is { Length: > 0 } mac) top.Children.Add(Fact("Endereço MAC", mac));
        top.Children.Add(Fact("ID do PC na conta", DeviceFacts.ShortId(_license.DeviceId)));
        if (status.Payload is { } payload) top.Children.Add(Fact("PCs na conta", $"até {payload.MaxDevices}"));
        top.Children.Add(Fact("Versão", $"{MBExtreme.Core.AppInfo.Version} · compilação {MBExtreme.Core.Update.AppUpdates.CurrentBuild()}"));
        long ticks = AppServices.Settings.LicenseLastSeenUtcTicks;
        top.Children.Add(Fact("Última verificação", ticks > 0 ? Ago(new DateTime(ticks, DateTimeKind.Utc)) : "—"));
        DockPanel.SetDock(top, Dock.Top);
        panel.Children.Add(top);

        var buttons = new Grid { Margin = new Thickness(0, 16, 0, 0), VerticalAlignment = VerticalAlignment.Bottom };
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        var refresh = ActionButton("Atualizar", async () => await RefreshAsync(), false);
        refresh.Margin = new Thickness(0, 0, 4, 0);
        refresh.IsEnabled = config.Configured && SignedIn;
        var remove = ActionButton("Remover deste PC", async () => await SignOutAsync(), false);
        remove.Margin = new Thickness(4, 0, 0, 0);
        remove.Foreground = Brush("BadBrush");
        remove.IsEnabled = SignedIn;
        Grid.SetColumn(remove, 1);
        buttons.Children.Add(refresh);
        buttons.Children.Add(remove);
        panel.Children.Add(buttons);

        var card = CardOf(panel);
        card.Padding = new Thickness(22, 20, 22, 20);
        card.VerticalAlignment = VerticalAlignment.Stretch;
        return card;
    }

    private string? _mac;

    private DockPanel Fact(string label, string value, string brush = "TextBrush")
    {
        var row = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var v = new TextBlock { Text = value, FontSize = 13, Foreground = Brush(brush), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(12, 0, 0, 0) };
        DockPanel.SetDock(v, Dock.Right);
        row.Children.Add(v);
        row.Children.Add(new TextBlock { Text = label, FontSize = 13, Foreground = Brush("MutedBrush") });
        return row;
    }

    private static string Ago(DateTime utc)
    {
        var span = DateTime.UtcNow - utc;
        if (span < TimeSpan.FromMinutes(1)) return "agora";
        if (span < TimeSpan.FromHours(1)) return $"há {(int)span.TotalMinutes} min";
        if (span < TimeSpan.FromDays(1)) return $"há {(int)span.TotalHours} h";
        return utc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    }

    // ---------------------------------------------------------------- avançado (recolhido)

    private UIElement BuildAdvanced()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(Muted("Para testes locais: informe o endereço do servidor e a chave pública (veja docs/LICENCIAMENTO.md). Na versão de venda estes valores já vêm no programa.", 12));
        _url.Text = AppServices.Settings.LicenseServerUrl;
        _key.Text = AppServices.Settings.LicensePublicKey;
        panel.Children.Add(Muted("Endereço do servidor (ex.: http://localhost:5100):", 12));
        panel.Children.Add(_url);
        panel.Children.Add(Muted("Chave pública:", 12));
        panel.Children.Add(_key);
        var enforce = Check("Exigir licença para os recursos do plano Pro", "Desligado, nada é bloqueado.");
        enforce.IsChecked = AppServices.Settings.LicenseEnforce;
        panel.Children.Add(enforce);
        var save = SmallButton("Salvar configuração", (_, _) =>
        {
            AppServices.Settings.LicenseServerUrl = _url.Text.Trim();
            AppServices.Settings.LicensePublicKey = _key.Text.Trim();
            AppServices.Settings.LicenseEnforce = enforce.IsChecked == true;
            AppServices.Settings.Save();
            _license.Start();
            SetStatus("Configuração salva.", "OkBrush");
            Render();
        });
        save.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(save);
        var expander = new Expander { Header = "Opções avançadas (servidor de licenças, só para testes)", Content = panel, Foreground = Brush("TextBrush"), IsExpanded = false };
        var card = CardOf(expander);
        card.Margin = new Thickness(0, 14, 0, 0);
        return card;
    }

    // ---------------------------------------------------------------- ações

    private async Task LoginAsync()
    {
        _loginCts = new CancellationTokenSource();
        Render();
        SetStatus("Abrindo o navegador...");
        try
        {
            var (ok, message) = await _license.LoginAsync(code =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _code.Text = code.UserCode;
                    _codeCard.Visibility = Visibility.Visible;
                    SetStatus($"Confirme no navegador que o código é {code.UserCode} e clique em Aprovar. Se o navegador não abriu: {code.VerificationUri}");
                }));
            }, _loginCts.Token);
            SetStatus(message, ok ? "OkBrush" : "WarnBrush");
        }
        finally
        {
            _codeCard.Visibility = Visibility.Collapsed;
            _loginCts = null;
            Render();
        }
    }

    private async Task RefreshAsync()
    {
        SetStatus("Renovando...");
        var (ok, message) = await _license.RefreshAsync();
        SetStatus(message, ok ? "OkBrush" : "WarnBrush");
    }

    private async Task SignOutAsync()
    {
        if (MessageBox.Show("Remover a licença deste computador?\n\nA vaga é liberada no site e você pode ativar outro PC.", "Conta e licença",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        SetStatus(await _license.SignOutAsync(), "OkBrush");
    }

    private void OpenAccount() => OpenSite("/painel");

    /// <summary>Abre uma página do site principal (MB Hub). O servidor de licenças é outro endereço e não tem páginas.</summary>
    private void OpenSite(string path) => Open(MBExtreme.Core.AppInfo.SiteUrl + path);
}

/// <summary>Mostrada no lugar de uma página do plano Pro quando a licença não vale.</summary>
public sealed class UpgradePage : ToolPageBase
{
    public UpgradePage(string title, string key) : base(title,
        $"Este recurso faz parte do plano {FeaturePolicy.Title(FeaturePolicy.RequiredPlan(key) ?? FeaturePolicy.Gamer)}. Assine no site e clique em Atualizar em Conta e licença.")
    {
        AddButton("Ver planos", (_, _) => OpenPlans());
        AddButton("Ver licença", (_, _) => MainWindow.Current?.Go("license"));
        Body.Children.Add(Muted("Planos: Essencial (otimização), Gamer (jogos e benchmark), Creator (estúdio de live) e Técnico (assistências, 5 PCs). " +
                                "Os recursos gratuitos continuam disponíveis no menu."));
    }

    private static void OpenPlans()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://www.mbinfoarte.com.br/precos?produto=mb-extreme") { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }
}
