using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MBExtreme.Core.Gamepads;

namespace MBExtreme.UI.Pages;

/// <summary>
/// MB Control · Emulação de controle: assistente que verifica o PC (drivers do controle virtual, HidHide, FakerInput),
/// ajuda a conectar o controle e mostra o que o motor de emulação vai fazer.
/// </summary>
internal sealed class MbEmulationPage : MbControlPageBase
{
    private readonly System.Windows.Controls.Primitives.UniformGrid _parts = new() { Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock _summary = new() { FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    private readonly TextBlock _installNote = new() { FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
    private readonly Button _updateAll = PadUi.Button("Atualizar tudo", primary: true);
    private readonly TextBlock _padState = new() { FontSize = 12, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

    public MbEmulationPage() : base("Emulação de controle", "Faz os jogos enxergarem o seu controle PS4, PS5 ou Xbox como um controle nativo do Windows. Só instalamos o que for preciso, pelos sites oficiais.")
    {
        // passo 1: os drivers lado a lado, cada um com o estado e o botão
        var step = new StackPanel();
        var head = new DockPanel();
        var recheck = PadUi.Button("Verificar de novo");
        recheck.Click += (_, _) => Check();
        _updateAll.Margin = new Thickness(0);
        _updateAll.Click += async (_, _) => await UpdateAllAsync();
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(recheck);
        right.Children.Add(_updateAll);
        DockPanel.SetDock(right, Dock.Right);
        head.Children.Add(right);
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(PadUi.Head("PASSO 1 · PREPARAR O PC"));
        left.Children.Add(_summary);
        head.Children.Add(left);
        step.Children.Add(head);
        step.Children.Add(_parts);
        step.Children.Add(_installNote);
        Body.Children.Add(PadUi.Card(step));

        // passo 2 (pequeno) ao lado do passo 3
        var connect = new StackPanel();
        connect.Children.Add(PadUi.Head("PASSO 2 · CONECTAR O CONTROLE"));
        connect.Children.Add(Line("\uE88E", "Cabo USB", "Cabo que passa dados (não só carga)."));
        connect.Children.Add(Line("\uE702", "Bluetooth", "Segure PS + Create (PS5) ou PS + Share (PS4) até piscar rápido."));
        var bt = PadUi.Button("Abrir o Bluetooth do Windows");
        bt.HorizontalAlignment = HorizontalAlignment.Left;
        bt.Margin = new Thickness(0, 4, 0, 0);
        bt.Click += (_, _) => PadUi.Open("ms-settings:bluetooth");
        connect.Children.Add(bt);
        connect.Children.Add(_padState);
        var row1 = PadUi.Columns((PadUi.Card(connect), 1), (PadUi.Card(Engine()), 2));
        row1.Margin = new Thickness(0, 0, 0, 0);
        Body.Children.Add(row1);

        // ajustes e remapear lado a lado
        Body.Children.Add(PadUi.Columns((PadUi.Card(Tuning()), 1.25), (PadUi.Card(Remap()), 1)));
        AppServices.Emulation.Changed += () => Dispatcher.BeginInvoke(new Action(ShowEngine));
        Action reopen = () => Dispatcher.BeginInvoke(new Action(() => { if (IsLoaded) MainWindow.Current?.Reopen("mbc-emu", $"Perfil \"{AppServices.Settings.PadActiveProfile}\" aplicado na emulação."); }));
        Loaded += (_, _) => PadProfileHub.Changed += reopen;
        Unloaded += (_, _) => PadProfileHub.Changed -= reopen;
        Check();
    }

    private static UIElement Line(string glyph, string title, string text)
    {
        var g = new Grid { Margin = new Thickness(0, 8, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var badge = MBExtreme.UI.Controls.GlassFx.Badge(glyph, Color.FromRgb(0xFF, 0xC4, 0x00), 30);
        badge.Margin = new Thickness(0, 0, 10, 0);
        g.Children.Add(badge);
        var st = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        st.Children.Add(new TextBlock { Text = title, FontSize = 12.5, FontWeight = FontWeights.SemiBold });
        st.Children.Add(PadUi.Muted(text, 11));
        Grid.SetColumn(st, 1);
        g.Children.Add(st);
        return g;
    }

    protected override void Slow()
    {
        var d = AppServices.Pads.Current;
        _padState.Text = d is null ? "○ Nenhum controle conectado" : $"● {AppServices.Pads.DisplayName(d)} conectado " + (d.Connection == PadConnection.Bluetooth ? "pelo Bluetooth" : d.Connection == PadConnection.Usb ? "pelo cabo" : "");
        _padState.Foreground = PadUi.Res(d is null ? "MutedBrush" : "OkBrush");
    }

    private static string Glyph(string id) => id switch
    {
        "vigem" => "\uE7FC", "viiper" => "\uE7FC", "hidhide" => "\uED1A", "fakerinput" => "\uE765", _ => "\uE74C",
    };

    private readonly List<(DriverPackage Package, string Title, Button Button, TextBlock Progress)> _updatable = new();

    private void Check()
    {
        var list = EmulationDrivers.CheckWindows();
        var target = AppServices.Settings.Emulation.Target;
        bool ps5 = target is VirtualTarget.DualSense or VirtualTarget.DualSenseEdge;
        bool ready = EmulationDrivers.ReadyFor(list, target);
        int ok = list.Count(c => c.Installed);
        _summary.Text = ready ? $"{ok} de {list.Count} instalados · pronto para o controle escolhido" : ps5 ? "Falta instalar o VIIPER para o PS5 virtual" : "Falta instalar o controle virtual (ViGEmBus)";
        _summary.Foreground = PadUi.Res(ready ? "OkBrush" : "WarnBrush");
        _parts.Children.Clear();
        _parts.Columns = Math.Max(1, list.Count);
        _updatable.Clear();
        foreach (var c in list)
        {
            int paren = c.Title.IndexOf(" (", StringComparison.Ordinal);
            string name = paren > 0 ? c.Title[..paren] : c.Title;
            string tech = paren > 0 ? c.Title[(paren + 2)..].TrimEnd(')') + " · " + c.Need : c.Need;
            var st = new StackPanel();
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition());
            var badge = MBExtreme.UI.Controls.GlassFx.Badge(Glyph(c.Id), Color.FromRgb(0xFF, 0xC4, 0x00), 32);
            badge.Margin = new Thickness(0, 0, 10, 0);
            top.Children.Add(badge);
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(new TextBlock { Text = name, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = c.Purpose });
            names.Children.Add(new TextBlock { Text = tech, FontSize = 11, Foreground = PadUi.Res("MutedBrush"), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = c.Title });
            Grid.SetColumn(names, 1);
            top.Children.Add(names);
            st.Children.Add(top);

            var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var progress = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = PadUi.Res("MutedBrush"), Margin = new Thickness(0, 6, 0, 0) };
            string url = c.Url;
            Button action;
            if (DriverDownloads.For(c.Id) is { } package)
            {
                action = Small(c.Installed ? "Atualizar" : "Instalar", !c.Installed && c.Need != "opcional");
                action.Click += async (_, _) => await InstallAsync(package, c.Title, action, progress);
                action.ToolTip = "Baixa a versão mais nova do site oficial e instala. Botão direito: abrir o site oficial.";
                action.MouseRightButtonUp += (_, _) => PadUi.Open(url);
                if (c.Installed) _updatable.Add((package, c.Title, action, progress));
            }
            else
            {
                action = Small(c.Installed ? "Reinstalar" : "Baixar", !c.Installed && c.Need != "opcional");
                action.Click += (_, _) => PadUi.Open(url);
            }
            DockPanel.SetDock(action, Dock.Right);
            bottom.Children.Add(action);
            var state = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            string brush = c.Installed ? "OkBrush" : c.Need == "opcional" ? "MutedBrush" : "WarnBrush";
            state.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = PadUi.Res(brush), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            state.Children.Add(new TextBlock { Text = c.Installed ? "Instalado" : c.Need == "opcional" ? "Não instalado" : "Falta instalar", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = PadUi.Res(brush) });
            bottom.Children.Add(state);
            st.Children.Add(bottom);
            st.Children.Add(progress);
            var tile = new Border { Background = PadUi.Res("Surface2Brush"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 8, 0), Child = st };
            MBExtreme.UI.Controls.GlassFx.Attach(tile, badge, bar: false);
            _parts.Children.Add(tile);
        }
        _updateAll.IsEnabled = _updatable.Count > 0 && !_installing;
    }

    private static Button Small(string text, bool primary)
    {
        var b = PadUi.Button(text, primary);
        b.Margin = new Thickness(0);
        b.Padding = new Thickness(10, 3, 10, 3);
        b.FontSize = 11.5;
        return b;
    }

    /// <summary>Atualiza um por um os componentes já instalados que o programa sabe baixar.</summary>
    private async Task UpdateAllAsync()
    {
        foreach (var (package, title, button, progress) in _updatable.ToList())
        {
            await InstallAsync(package, title, button, progress, recheck: false);
        }
        Check();
    }

    private static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private bool _installing;

    /// <summary>Um clique: baixa o instalador oficial mais novo, confere o SHA-256 e roda (pede administrador ao Windows).</summary>
    private async Task InstallAsync(DriverPackage package, string title, Button button, TextBlock progress, bool recheck = true)
    {
        if (_installing) { progress.Text = "Espere a outra instalação terminar."; return; }
        _installing = true;
        button.IsEnabled = false;
        progress.Foreground = PadUi.Res("MutedBrush");
        try
        {
            var (path, asset) = await DriverDownloads.DownloadAsync(Http, package, new Progress<string>(t => progress.Text = t), CancellationToken.None);
            progress.Text = package.Silent ? $"Instalando {asset.Tag}... (o Windows pode pedir permissão)" : "Siga o instalador que abriu (Avançar até o fim).";
            var info = package.Msi
                ? new System.Diagnostics.ProcessStartInfo("msiexec.exe", $"/i \"{path}\" {package.Arguments}")
                : new System.Diagnostics.ProcessStartInfo(path, package.Arguments);
            info.UseShellExecute = true;
            info.Verb = "runas";
            info.WorkingDirectory = System.IO.Path.GetDirectoryName(path)!;
            using var process = System.Diagnostics.Process.Start(info) ?? throw new InvalidOperationException("O instalador não abriu.");
            await process.WaitForExitAsync();
            var (ok, reboot) = DriverDownloads.Result(process.ExitCode);
            progress.Text = ok ? (reboot ? "Instalado. Reinicie o PC para terminar." : "Instalado.") : process.ExitCode is 1602 or 1223 ? "Instalação cancelada." : $"O instalador terminou com erro ({process.ExitCode}).";
            progress.Foreground = PadUi.Res(ok ? "OkBrush" : "WarnBrush");
            try { System.IO.File.Delete(path); } catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { }
            if (ok && reboot)
                MessageBox.Show($"{title} foi instalado. O Windows precisa reiniciar para o driver começar a funcionar.", "Emulação de controle", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (System.ComponentModel.Win32Exception) { progress.Text = "Instalação cancelada (o Windows não recebeu permissão)."; progress.Foreground = PadUi.Res("WarnBrush"); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Net.Http.HttpRequestException or TaskCanceledException or System.IO.IOException or UnauthorizedAccessException)
        {
            progress.Text = ex.Message;
            progress.Foreground = PadUi.Res("WarnBrush");
        }
        finally
        {
            _installing = false;
            button.IsEnabled = true;
        }
        string keep = progress.Text;
        if (!recheck) return;
        Check();
        _installNote.Text = title + ": " + keep;
        _installNote.Foreground = progress.Foreground;
    }

    // ------------------------------------------------------------ passo 3: ligar o controle virtual

    private readonly EmulationSettings _e = AppServices.Settings.Emulation;
    private readonly TextBlock _engineStatus = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly Button _toggle = PadUi.Button("Ligar emulação", primary: true);
    private readonly System.Windows.Threading.DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };

    private void Save()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private static TextBlock Head(string text) => new() { Text = text, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)), Margin = new Thickness(0, 0, 0, 8) };

    private WrapPanel Chips<T>(IEnumerable<(T Value, string Label)> options, Func<T> get, Action<T> set) where T : struct
    {
        var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var list = new List<(System.Windows.Controls.Primitives.ToggleButton B, T V)>();
        foreach (var (value, label) in options)
        {
            var b = new System.Windows.Controls.Primitives.ToggleButton { Content = label, Style = (Style)FindResource("ChipToggle"), Margin = new Thickness(0, 0, 6, 6) };
            var v = value;
            b.Click += (_, _) => { set(v); foreach (var (o, ov) in list) o.IsChecked = EqualityComparer<T>.Default.Equals(ov, v); Save(); };
            list.Add((b, value));
            wrap.Children.Add(b);
        }
        foreach (var (b, v) in list) b.IsChecked = EqualityComparer<T>.Default.Equals(v, get());
        return wrap;
    }

    private CheckBox Check(string text, Func<bool> get, Action<bool> set)
    {
        var c = new CheckBox { Content = text, IsChecked = get(), FontSize = 13, Margin = new Thickness(0, 0, 18, 8) };
        c.Click += (_, _) => { set(c.IsChecked == true); Save(); };
        return c;
    }

    private UIElement Slider(string label, int min, int max, Func<int> get, Action<int> set, string unit = "%") =>
        PadUi.SliderRow(label, min, max, get(), v => $"{v:0}{unit}", v => { set((int)v); Save(); });

    private UIElement Engine()
    {
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); AppServices.Settings.Save(); };
        var st = new StackPanel();
        st.Children.Add(Head("PASSO 3 · LIGAR O CONTROLE VIRTUAL"));
        st.Children.Add(PadUi.Muted("O jogo vai ver:", 12));
        st.Children.Add(_targets);
        FillTargets();
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var hide = Check("Esconder o controle físico dos jogos (HidHide)", () => _e.HidePhysical, v => _e.HidePhysical = v);
        hide.VerticalAlignment = VerticalAlignment.Center;
        hide.Margin = new Thickness(0);
        _toggle.Click += (_, _) =>
        {
            if (AppServices.Emulation.Running) AppServices.Emulation.Stop();
            else if (AppServices.Pads.Current is null) MessageBox.Show("Conecte o controle primeiro (passo 2).", "Emulação", MessageBoxButton.OK, MessageBoxImage.Information);
            else AppServices.Emulation.Start();
        };
        _toggle.Margin = new Thickness(0);
        _toggle.Padding = new Thickness(22, 9, 22, 9);
        _toggle.FontSize = 13.5;
        DockPanel.SetDock(_toggle, Dock.Right);
        row.Children.Add(_toggle);
        row.Children.Add(hide);
        st.Children.Add(row);
        st.Children.Add(_engineStatus);
        st.Children.Add(PadUi.Muted("Xbox e PS4 usam o ViGEmBus. PS5 e PS5 Edge usam o VIIPER (gratuito, cria um DualSense de verdade por USB virtual, com ícones de PS5 e giroscópio nos jogos). A emulação continua ligada enquanto o MB estiver aberto (pode minimizar).", 11.5));
        ShowEngine();
        return st;
    }

    private readonly System.Windows.Controls.Primitives.UniformGrid _targets = new() { Columns = 4, Margin = new Thickness(0, 6, 0, 4) };

    private void FillTargets()
    {
        _targets.Children.Clear();
        foreach (var (t, name, sub) in new[]
        {
            (VirtualTarget.Xbox360, "Xbox 360", "quase todo jogo"), (VirtualTarget.DualShock4, "PS4", "DualShock 4"),
            (VirtualTarget.DualSense, "PS5", "DualSense"), (VirtualTarget.DualSenseEdge, "PS5 Edge", "DualSense Edge"),
        })
        {
            var target = t;
            _targets.Children.Add(PadUi.SelectTile("\uE7FC", name, sub, _e.Target == t, () =>
            {
                _e.Target = target;
                Save();
                FillTargets();
                Check();
                if (AppServices.Emulation.Running) AppServices.Emulation.Start();
            }, vertical: true));
        }
    }

    private void ShowEngine()
    {
        bool on = AppServices.Emulation.Running;
        _toggle.Content = on ? "Desligar emulação" : "Ligar emulação";
        _engineStatus.Text = AppServices.Emulation.Status;
        _engineStatus.Foreground = PadUi.Res(on ? "OkBrush" : AppServices.Emulation.Status.StartsWith("Desligada") ? "MutedBrush" : "WarnBrush");
    }

    private UIElement Tuning()
    {
        var st = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var pick = new StackPanel { Orientation = Orientation.Horizontal };
        pick.Children.Add(new TextBlock { Text = "Usando o perfil", FontSize = 11.5, Foreground = PadUi.Res("MutedBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        pick.Children.Add(PadProfileHub.Picker(190));
        DockPanel.SetDock(pick, Dock.Right);
        head.Children.Add(pick);
        head.Children.Add(PadUi.Head("AJUSTES (VALEM DENTRO DO JOGO)"));
        st.Children.Add(head);
        var stickCols = new List<UIElement>();
        foreach (var (name, t) in new[] { ("Analógico esquerdo", _e.Left), ("Analógico direito", _e.Right) })
        {
            var col = new StackPanel();
            col.Children.Add(new TextBlock { Text = name, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            col.Children.Add(Slider("Zona morta", 0, 30, () => t.Deadzone, v => t.Deadzone = v));
            col.Children.Add(Slider("Anti-zona morta", 0, 30, () => t.AntiDeadzone, v => t.AntiDeadzone = v));
            col.Children.Add(Slider("Sensibilidade", 50, 150, () => t.Sensitivity, v => t.Sensitivity = v));
            col.Children.Add(Chips(new[] { (StickCurve.Linear, "Linear"), (StickCurve.Suave, "Suave"), (StickCurve.Agressiva, "Agressiva") }, () => t.Curve, c => t.Curve = c));
            col.Children.Add(Check("Zona morta por eixo (axial)", () => t.Axial, v => t.Axial = v));
            col.Children.Add(Check("Inverter o eixo vertical", () => t.InvertY, v => t.InvertY = v));
            stickCols.Add(col);
        }
        st.Children.Add(PadUi.Columns((stickCols[0], 1), (stickCols[1], 1)));
        var opts = new WrapPanel();
        opts.Children.Add(Check("Trocar os analógicos", () => _e.SwapSticks, v => _e.SwapSticks = v));
        opts.Children.Add(Check("Botões estilo Nintendo (troca ✕/○ e □/△)", () => _e.NintendoLayout, v => _e.NintendoLayout = v));
        st.Children.Add(opts);
        st.Children.Add(new TextBlock { Text = "Mira pelo giroscópio (PS4/PS5)", FontSize = 13.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 6) });
        var gyro = new WrapPanel();
        gyro.Children.Add(Check("Ligar", () => _e.GyroAim, v => _e.GyroAim = v));
        gyro.Children.Add(Check("Só enquanto segura L2 (mirando)", () => _e.GyroOnlyWhenAiming, v => _e.GyroOnlyWhenAiming = v));
        st.Children.Add(gyro);
        st.Children.Add(PadUi.Columns(
            (Slider("Zona morta dos gatilhos", 0, 30, () => _e.TriggerDeadzone, v => _e.TriggerDeadzone = v), 1),
            (Slider("Sensibilidade do giroscópio", 1, 100, () => _e.GyroSensitivity, v => _e.GyroSensitivity = v), 1)));
        var fine = PadUi.Button("Ajustes finos com prévia ao vivo →");
        fine.HorizontalAlignment = HorizontalAlignment.Left;
        fine.Margin = new Thickness(0, 6, 0, 0);
        fine.ToolTip = "Zona morta externa, curva personalizada, suavização, anti-retorno, curso dos gatilhos e filtro do giroscópio.";
        fine.Click += (_, _) => MainWindow.Current?.Go("mbc-tune");
        st.Children.Add(fine);
        return st;
    }

    private static readonly (PadButtons Button, string Name)[] Buttons =
    {
        (PadButtons.Cross, "✕ (A)"), (PadButtons.Circle, "○ (B)"), (PadButtons.Square, "□ (X)"), (PadButtons.Triangle, "△ (Y)"),
        (PadButtons.L1, "L1 (LB)"), (PadButtons.R1, "R1 (RB)"), (PadButtons.L3, "L3"), (PadButtons.R3, "R3"),
        (PadButtons.Up, "Cima"), (PadButtons.Down, "Baixo"), (PadButtons.Left, "Esquerda"), (PadButtons.Right, "Direita"),
        (PadButtons.Create, "Create/Share (Back)"), (PadButtons.Options, "Options (Start)"), (PadButtons.PS, "PS (Guia)"), (PadButtons.Touchpad, "Touchpad"),
    };

    private UIElement Remap()
    {
        var st = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var reset = PadUi.Button("Voltar ao padrão");
        reset.Margin = new Thickness(0);
        reset.Click += (_, _) => { _e.Remap.Clear(); AppServices.Settings.Save(); MainWindow.Current?.Reopen("mbc-emu", "Botões voltaram ao padrão."); };
        DockPanel.SetDock(reset, Dock.Right);
        head.Children.Add(reset);
        head.Children.Add(PadUi.Head("REMAPEAR BOTÕES"));
        st.Children.Add(head);
        st.Children.Add(PadUi.Muted("Escolha o que cada botão faz no controle virtual. Os trocados ficam em amarelo.", 11.5));
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var (button, name) in Buttons)
        {
            var box = new ComboBox { Margin = new Thickness(0, 2, 8, 8), FontSize = 12 };
            box.Items.Add(new ComboBoxItem { Content = "Igual (" + name + ")", Tag = "" });
            box.Items.Add(new ComboBoxItem { Content = "Desligado", Tag = "None" });
            foreach (var (b2, n2) in Buttons) if (b2 != button) box.Items.Add(new ComboBoxItem { Content = n2, Tag = b2.ToString() });
            string current = _e.Remap.TryGetValue(button.ToString(), out var c) ? c : "";
            box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == current) ?? box.Items[0];
            var key = button.ToString();
            box.SelectionChanged += (_, _) =>
            {
                string tag = (string)((ComboBoxItem)box.SelectedItem).Tag;
                if (tag.Length == 0) _e.Remap.Remove(key); else _e.Remap[key] = tag;
                Mark(box, tag.Length > 0);
                Save();
            };
            Mark(box, current.Length > 0);
            var cell = new StackPanel();
            cell.Children.Add(new TextBlock { Text = name, FontSize = 11, Foreground = PadUi.Res("MutedBrush") });
            cell.Children.Add(box);
            grid.Children.Add(cell);
        }
        st.Children.Add(grid);
        return st;
    }

    private static void Mark(ComboBox box, bool changed)
    {
        if (changed)
        {
            box.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00));
            box.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00));
        }
        else
        {
            box.ClearValue(Control.BorderBrushProperty);
            box.ClearValue(Control.ForegroundProperty);
        }
    }
}
