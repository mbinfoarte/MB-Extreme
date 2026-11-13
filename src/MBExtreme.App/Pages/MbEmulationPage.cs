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
    private readonly StackPanel _parts = new();
    private readonly TextBlock _summary = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) };

    public MbEmulationPage() : base("Emulação de controle", "Faz os jogos enxergarem o seu controle PS4, PS5 ou Xbox como um controle nativo do Windows. Primeiro preparamos o PC; só instalamos o que for preciso, pelos sites oficiais.")
    {
        var step = new StackPanel();
        step.Children.Add(new TextBlock { Text = "PASSO 1 · PREPARAR O PC", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)) });
        step.Children.Add(_summary);
        step.Children.Add(_parts);
        var recheck = PadUi.Button("Verificar de novo");
        recheck.HorizontalAlignment = HorizontalAlignment.Left;
        recheck.Margin = new Thickness(0, 6, 0, 0);
        recheck.Click += (_, _) => Check();
        step.Children.Add(recheck);
        Body.Children.Add(PadUi.Card(step));

        var connect = new StackPanel();
        connect.Children.Add(new TextBlock { Text = "PASSO 2 · CONECTAR O CONTROLE", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)), Margin = new Thickness(0, 0, 0, 8) });
        connect.Children.Add(Line("Pelo cabo USB", "Use um cabo que passa dados (não só carga): micro-USB no PS4, USB-C no PS5 e no Xbox Series."));
        connect.Children.Add(Line("Pelo Bluetooth", "Com o controle desligado, segure PS + Share (PS4) ou PS + Create (PS5) até a luz piscar rápido. No Xbox, segure o botão de parear. Depois adicione o controle no Bluetooth do Windows."));
        var bt = PadUi.Button("Abrir o Bluetooth do Windows");
        bt.HorizontalAlignment = HorizontalAlignment.Left;
        bt.Margin = new Thickness(0, 8, 0, 0);
        bt.Click += (_, _) => PadUi.Open("ms-settings:bluetooth");
        connect.Children.Add(bt);
        Body.Children.Add(PadUi.Card(connect));

        Body.Children.Add(PadUi.Card(Engine()));
        Body.Children.Add(PadUi.Card(Tuning()));
        Body.Children.Add(PadUi.Card(Remap()));
        AppServices.Emulation.Changed += () => Dispatcher.BeginInvoke(new Action(ShowEngine));
        Check();
    }

    private static UIElement Line(string title, string text)
    {
        var st = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        st.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold });
        st.Children.Add(PadUi.Muted(text, 12.5));
        return st;
    }

    private void Check()
    {
        var list = EmulationDrivers.CheckWindows();
        _summary.Text = EmulationDrivers.Ready(list) ? "Tudo pronto para o controle virtual." : "Falta instalar o controle virtual para emular.";
        _summary.Foreground = PadUi.Res(EmulationDrivers.Ready(list) ? "OkBrush" : "WarnBrush");
        _parts.Children.Clear();
        foreach (var c in list)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var dot = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 5, 12, 0), VerticalAlignment = VerticalAlignment.Top,
                Background = PadUi.Res(c.Installed ? "OkBrush" : c.Need == "opcional" ? "BorderBrush" : "WarnBrush") };
            grid.Children.Add(dot);
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = $"{c.Title} · {c.Need}", FontSize = 14, FontWeight = FontWeights.SemiBold });
            text.Children.Add(PadUi.Muted(c.Purpose, 12));
            text.Children.Add(new TextBlock { Text = c.Installed ? "Instalado" : c.Need == "opcional" ? "Não instalado · não é necessário para o controle" : "Falta instalar", FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = PadUi.Res(c.Installed ? "OkBrush" : c.Need == "opcional" ? "MutedBrush" : "WarnBrush") });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            var b = PadUi.Button(c.Installed ? "Reinstalar / atualizar" : "Baixar do site oficial", primary: !c.Installed && c.Need != "opcional");
            b.VerticalAlignment = VerticalAlignment.Center;
            b.Margin = new Thickness(10, 0, 0, 0);
            string url = c.Url;
            b.Click += (_, _) => PadUi.Open(url);
            Grid.SetColumn(b, 2);
            grid.Children.Add(b);
            _parts.Children.Add(new Border { Background = PadUi.Res("Surface2Brush"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, 8), Child = grid });
        }
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

    private UIElement Slider(string label, int min, int max, Func<int> get, Action<int> set, string unit = "%")
    {
        var value = new TextBlock { FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = PadUi.Res("WarnBrush") };
        var sl = new Slider { Minimum = min, Maximum = max, Value = get(), IsSnapToTickEnabled = true, TickFrequency = 1, Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
        value.Text = $"{get()}{unit}";
        sl.ValueChanged += (_, _) => { set((int)sl.Value); value.Text = $"{(int)sl.Value}{unit}"; Save(); };
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new TextBlock { Text = label + "  ", FontSize = 12.5 });
        head.Children.Add(value);
        var st = new StackPanel { Margin = new Thickness(0, 0, 24, 8) };
        st.Children.Add(head);
        st.Children.Add(sl);
        return st;
    }

    private UIElement Engine()
    {
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); AppServices.Settings.Save(); };
        var st = new StackPanel();
        st.Children.Add(Head("PASSO 3 · LIGAR O CONTROLE VIRTUAL"));
        st.Children.Add(new TextBlock { Text = "O jogo vai ver:", FontSize = 13, Margin = new Thickness(0, 0, 0, 6) });
        st.Children.Add(Chips(new[] { (VirtualTarget.Xbox360, "Xbox 360 (funciona em quase todo jogo)"), (VirtualTarget.DualShock4, "PS4 (ícones de PlayStation)") },
            () => _e.Target, t => { _e.Target = t; if (AppServices.Emulation.Running) AppServices.Emulation.Start(); }));
        st.Children.Add(Check("Esconder o controle físico dos jogos (HidHide)", () => _e.HidePhysical, v => _e.HidePhysical = v));
        var row = new WrapPanel();
        _toggle.Click += (_, _) =>
        {
            if (AppServices.Emulation.Running) AppServices.Emulation.Stop();
            else if (AppServices.Pads.Current is null) MessageBox.Show("Conecte o controle primeiro (passo 2).", "Emulação", MessageBoxButton.OK, MessageBoxImage.Information);
            else AppServices.Emulation.Start();
        };
        row.Children.Add(_toggle);
        st.Children.Add(row);
        st.Children.Add(_engineStatus);
        st.Children.Add(PadUi.Muted("PS5 como controle virtual depende de uma licença do driver e fica para depois. A emulação continua ligada enquanto o MB estiver aberto (pode minimizar).", 11.5));
        ShowEngine();
        return st;
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
        st.Children.Add(Head("AJUSTES (VALEM DENTRO DO JOGO)"));
        var copy = new ComboBox { Width = 240, Margin = new Thickness(0, 0, 8, 10), HorizontalAlignment = HorizontalAlignment.Left };
        copy.Items.Add(new ComboBoxItem { Content = "Copiar de um perfil por jogo...", IsEnabled = false });
        foreach (var p in AppServices.Settings.PadProfiles.Concat(PadProfiles.Defaults)) copy.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p });
        copy.SelectedIndex = 0;
        copy.SelectionChanged += (_, _) =>
        {
            if (copy.SelectedItem is not ComboBoxItem { Tag: PadProfile p }) return;
            foreach (var t in new[] { _e.Left, _e.Right }) { t.Deadzone = p.Deadzone; t.AntiDeadzone = p.AntiDeadzone; t.Sensitivity = p.Sensitivity; t.Curve = p.Curve; }
            _e.TriggerDeadzone = p.TriggerDeadzone;
            AppServices.Settings.Save();
            MainWindow.Current?.Reopen("mbc-emu", $"Ajustes copiados do perfil \"{p.Name}\".");
        };
        st.Children.Add(copy);
        var sticks = new WrapPanel();
        foreach (var (name, t) in new[] { ("Analógico esquerdo", _e.Left), ("Analógico direito", _e.Right) })
        {
            var col = new StackPanel { Margin = new Thickness(0, 0, 32, 0) };
            col.Children.Add(new TextBlock { Text = name, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            col.Children.Add(Slider("Zona morta", 0, 30, () => t.Deadzone, v => t.Deadzone = v));
            col.Children.Add(Slider("Anti-zona morta", 0, 30, () => t.AntiDeadzone, v => t.AntiDeadzone = v));
            col.Children.Add(Slider("Sensibilidade", 50, 150, () => t.Sensitivity, v => t.Sensitivity = v));
            col.Children.Add(Chips(new[] { (StickCurve.Linear, "Linear"), (StickCurve.Suave, "Suave"), (StickCurve.Agressiva, "Agressiva") }, () => t.Curve, c => t.Curve = c));
            col.Children.Add(Check("Zona morta por eixo (axial)", () => t.Axial, v => t.Axial = v));
            col.Children.Add(Check("Inverter o eixo vertical", () => t.InvertY, v => t.InvertY = v));
            sticks.Children.Add(col);
        }
        st.Children.Add(sticks);
        st.Children.Add(Slider("Zona morta dos gatilhos", 0, 30, () => _e.TriggerDeadzone, v => _e.TriggerDeadzone = v));
        var opts = new WrapPanel();
        opts.Children.Add(Check("Trocar os analógicos", () => _e.SwapSticks, v => _e.SwapSticks = v));
        opts.Children.Add(Check("Botões estilo Nintendo (troca ✕/○ e □/△)", () => _e.NintendoLayout, v => _e.NintendoLayout = v));
        st.Children.Add(opts);
        st.Children.Add(new TextBlock { Text = "Mira pelo giroscópio (PS4/PS5)", FontSize = 13.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 6) });
        var gyro = new WrapPanel();
        gyro.Children.Add(Check("Ligar", () => _e.GyroAim, v => _e.GyroAim = v));
        gyro.Children.Add(Check("Só enquanto segura L2 (mirando)", () => _e.GyroOnlyWhenAiming, v => _e.GyroOnlyWhenAiming = v));
        st.Children.Add(gyro);
        st.Children.Add(Slider("Sensibilidade do giroscópio", 1, 100, () => _e.GyroSensitivity, v => _e.GyroSensitivity = v));
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
        st.Children.Add(Head("REMAPEAR BOTÕES"));
        st.Children.Add(PadUi.Muted("Escolha o que cada botão faz no controle virtual. \"Desligado\" faz o botão não fazer nada.", 12));
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var (button, name) in Buttons)
        {
            var box = new ComboBox { Margin = new Thickness(0, 2, 12, 8) };
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
                Save();
            };
            var cell = new StackPanel();
            cell.Children.Add(new TextBlock { Text = name, FontSize = 12 });
            cell.Children.Add(box);
            grid.Children.Add(cell);
        }
        st.Children.Add(grid);
        var reset = PadUi.Button("Voltar tudo ao padrão");
        reset.HorizontalAlignment = HorizontalAlignment.Left;
        reset.Click += (_, _) => { _e.Remap.Clear(); AppServices.Settings.Save(); MainWindow.Current?.Reopen("mbc-emu", "Botões voltaram ao padrão."); };
        st.Children.Add(reset);
        return st;
    }
}
