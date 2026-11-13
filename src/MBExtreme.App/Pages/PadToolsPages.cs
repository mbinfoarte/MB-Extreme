using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MBExtreme.Core.Gamepads;
using MBExtreme.Core.Operations;

namespace MBExtreme.UI.Pages;

/// <summary>Peças comuns das abas do controle.</summary>
internal static class PadUi
{
    public static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    public static StackPanel Root(string title, string subtitle, out ScrollViewer scroll)
    {
        var root = new StackPanel { Margin = new Thickness(40, 18, 40, 32) };
        root.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock { Text = subtitle, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10), Foreground = Res("MutedBrush") });
        scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
        return root;
    }

    public static Border Card(UIElement child) => new()
    {
        Style = (Style)Application.Current.FindResource("Card"), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 8), Child = child,
    };

    public static Button Button(string text, bool primary = false) => new()
    {
        Content = text, Style = (Style)Application.Current.FindResource(primary ? "PrimaryButton" : "GhostButton"),
        Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0), FontSize = 12.5,
    };

    /// <summary>Barra deslizante do MB Control: nome à esquerda, valor em destaque à direita e a barra minimalista embaixo.</summary>
    public static Border SliderRow(string label, double min, double max, double value, Func<double, string> format, Action<double> changed, double width = double.NaN)
    {
        var valueText = new TextBlock { Text = format(value), FontSize = 12, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)) };
        var pill = new Border { Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x24, 0x10)), CornerRadius = new CornerRadius(999), Padding = new Thickness(9, 2, 9, 2), Child = valueText };
        var slider = new Slider
        {
            Minimum = min, Maximum = max, Value = value, IsSnapToTickEnabled = true, TickFrequency = 1,
            Style = (Style)Application.Current.FindResource("MbSlider"), Margin = new Thickness(0, 6, 0, 0),
        };
        System.Windows.Automation.AutomationProperties.SetName(slider, label);
        slider.ValueChanged += (_, _) => { valueText.Text = format(slider.Value); changed(slider.Value); };
        var head = new DockPanel();
        DockPanel.SetDock(pill, Dock.Right);
        head.Children.Add(pill);
        head.Children.Add(new TextBlock { Text = label, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
        var st = new StackPanel();
        st.Children.Add(head);
        st.Children.Add(slider);
        return new Border
        {
            Background = Res("Surface2Brush"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 8, 8),
            Width = width, Child = st,
        };
    }

    public static TextBlock Muted(string text, double size = 12) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = Res("MutedBrush") };

    public static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { MessageBox.Show("Não foi possível abrir: " + url, "Controle", MessageBoxButton.OK, MessageBoxImage.Information); }
    }
}

/// <summary>XInput (controles Xbox e compatíveis): estado, bateria e contador de pacotes.</summary>
internal static class XInput
{
    [StructLayout(LayoutKind.Sequential)]
    public struct State { public uint PacketNumber; public ushort Buttons; public byte LeftTrigger, RightTrigger; public short LX, LY, RX, RY; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Battery { public byte Type, Level; }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")] private static extern uint GetState(uint index, out State state);
    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetBatteryInformation")] private static extern uint GetBattery(uint index, byte devType, out Battery battery);

    public static bool TryState(uint index, out State state)
    {
        try { return GetState(index, out state) == 0; }
        catch (DllNotFoundException) { state = default; return false; }
        catch (EntryPointNotFoundException) { state = default; return false; }
    }

    public static string BatteryText(uint index)
    {
        try
        {
            if (GetBattery(index, 0, out var b) != 0) return "";
            if (b.Type == 1) return "ligado no cabo";
            if (b.Type is 0 or 0xFF) return "";
            return b.Level switch { 0 => "bateria vazia", 1 => "bateria baixa", 2 => "bateria média", _ => "bateria cheia" };
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return ""; }
    }

    /// <summary>Lê o controle a cada ~1 ms e anota quando chega pacote novo (mexa o analógico).</summary>
    public static IReadOnlyList<double> Capture(uint index, int durationMs, CancellationToken token)
    {
        var times = new List<double>();
        uint last = uint.MaxValue;
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < durationMs && !token.IsCancellationRequested)
        {
            if (TryState(index, out var s) && s.PacketNumber != last)
            {
                if (last != uint.MaxValue) times.Add(watch.Elapsed.TotalMilliseconds);
                last = s.PacketNumber;
            }
            Thread.Sleep(0);
        }
        return times;
    }
}

/// <summary>Aba "Informações e latência": todos os controles ligados, ligação, bateria e a taxa de atualização medida.</summary>
internal sealed class PadInfoPage : UserControl
{
    private readonly StackPanel _list = new();
    private CancellationTokenSource? _cts;

    public PadInfoPage()
    {
        var root = PadUi.Root("Informações e latência",
            "Todos os controles ligados ao PC, como estão conectados e quantas vezes por segundo eles mandam os comandos (taxa de atualização). " +
            "Quanto maior a taxa, menor o atraso entre apertar e o jogo receber.", out var scroll);
        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        var refresh = PadUi.Button("Procurar controles");
        refresh.Click += (_, _) => Load();
        var improve = PadUi.Button("Melhorar a resposta", primary: true);
        improve.ToolTip = "Impede o Windows de pôr as portas USB para dormir (evita atraso no primeiro movimento). Pode ser desfeito na Central Desfazer.";
        improve.Click += async (_, _) => await ImproveAsync();
        buttons.Children.Add(improve);
        buttons.Children.Add(refresh);
        root.Children.Add(buttons);
        root.Children.Add(_list);
        root.Children.Add(PadUi.Muted("Dica: pelo cabo USB a taxa costuma ser maior e mais estável que pelo Bluetooth. No Bluetooth, deixe o controle perto do PC e longe do roteador Wi-Fi.", 11.5));
        Content = scroll;
        Loaded += (_, _) => { if (_list.Children.Count == 0) Load(); };
        Unloaded += (_, _) => _cts?.Cancel();
    }

    private void Load()
    {
        _list.Children.Clear();
        IReadOnlyList<GameController> hid;
        try { hid = WinHid.FindGameControllers(); }
        catch (Exception ex) when (ex is System.IO.IOException or System.ComponentModel.Win32Exception or DllNotFoundException) { hid = Array.Empty<GameController>(); }

        int count = 0;
        for (uint i = 0; i < 4; i++)
        {
            if (!XInput.TryState(i, out _)) continue;
            count++;
            string battery = XInput.BatteryText(i);
            _list.Children.Add(Row($"Controle Xbox / XInput {i + 1}", "Microsoft Xbox (ou compatível)", "Padrão XInput" + (battery.Length > 0 ? " · " + battery : ""),
                token => XInput.Capture(i, 3000, token)));
        }
        foreach (var pad in hid.Where(p => p.Vendor != ControllerCatalog.Microsoft || p.Connection == PadConnection.Bluetooth))
        {
            count++;
            var current = pad;
            _list.Children.Add(Row(pad.Name, pad.Brand, $"{pad.ConnectionText} · VID {pad.Vendor:X4} · PID {pad.Product:X4}",
                token => WinHid.CaptureReports(current.Path!, current.InputLength, 3000, token)));
        }
        if (count == 0)
            _list.Children.Add(PadUi.Card(PadUi.Muted("Nenhum controle encontrado. Ligue o controle no cabo USB ou pareie pelo Bluetooth do Windows e clique em Procurar controles.", 13)));
    }

    private Border Row(string name, string brand, string detail, Func<CancellationToken, IReadOnlyList<double>> capture)
    {
        var result = new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        var measure = PadUi.Button("Medir latência");
        measure.Click += async (_, _) =>
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            measure.IsEnabled = false;
            result.Foreground = PadUi.Res("WarnBrush");
            result.Text = "Mexa o analógico em círculos sem parar por 3 segundos...";
            try
            {
                var times = await Task.Run(() => capture(token));
                var stats = PollingStats.From(times);
                if (stats is null)
                {
                    result.Foreground = PadUi.Res("MutedBrush");
                    result.Text = "Chegaram poucos comandos. Mexa o analógico o tempo todo durante a medição e tente de novo.";
                }
                else
                {
                    result.Foreground = PadUi.Res(stats.RateHz >= 200 ? "OkBrush" : stats.RateHz >= 100 ? "WarnBrush" : "BadBrush");
                    result.Text = $"{stats.RateHz:0} Hz · um comando a cada {stats.AverageMs:0.0} ms · variação {stats.JitterMs:0.0} ms · pior {stats.WorstMs:0.0} ms\n{stats.Verdict}";
                }
            }
            catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                result.Foreground = PadUi.Res("BadBrush");
                result.Text = ex.Message;
            }
            finally
            {
                measure.IsEnabled = true;
            }
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = name, FontSize = 14, FontWeight = FontWeights.SemiBold });
        text.Children.Add(PadUi.Muted(brand + " · " + detail));
        text.Children.Add(result);
        grid.Children.Add(text);
        Grid.SetColumn(measure, 1);
        measure.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(measure);
        return PadUi.Card(grid);
    }

    private static async Task ImproveAsync()
    {
        var tweak = AppServices.Tweaks.FirstOrDefault(t => t.Id == "power-usb-suspend");
        if (tweak is null) return;
        if (tweak.IsApplied() == true)
        {
            MessageBox.Show("Já está ligado: as portas USB não dormem enquanto o PC está na tomada.", "Controle", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            await Task.Run(() => OperationQueue.Shared.Run(new IOperation[] { new TweakOperation(tweak, apply: true) }));
            MessageBox.Show("Pronto: o Windows não põe mais as portas USB para dormir (na tomada). Controles, mouse e teclado respondem sem o atraso de reativação.\nPara desfazer: Central Desfazer.",
                "Controle", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "Controle", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}

/// <summary>Aba "Drivers e firmware": o que está ligado e onde atualizar, sempre pelos sites e programas oficiais.</summary>
internal sealed class PadDriversPage : UserControl
{
    private readonly StackPanel _list = new();

    public PadDriversPage()
    {
        var root = PadUi.Root("Drivers e firmware",
            "Atualizar o firmware corrige drift, falhas de conexão e melhora a resposta. Por segurança, o firmware é gravado pelo programa oficial de cada marca; " +
            "aqui você vê o que está ligado e abre o lugar certo para atualizar.", out var scroll);
        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        var drivers = PadUi.Button("Procurar drivers que faltam", primary: true);
        drivers.Click += (_, _) => MainWindow.Current?.Go("drivers");
        var devices = PadUi.Button("Controles do Windows (joy.cpl)");
        devices.Click += (_, _) => PadUi.Open("joy.cpl");
        var refresh = PadUi.Button("Procurar controles");
        refresh.Click += (_, _) => Load();
        buttons.Children.Add(drivers);
        buttons.Children.Add(devices);
        buttons.Children.Add(refresh);
        root.Children.Add(buttons);
        root.Children.Add(_list);
        Content = scroll;
        Loaded += (_, _) => { if (_list.Children.Count == 0) Load(); };
    }

    private void Load()
    {
        _list.Children.Clear();
        var vendors = new List<ushort>();
        try { vendors.AddRange(WinHid.FindGameControllers().Select(p => p.Vendor)); }
        catch (Exception ex) when (ex is System.IO.IOException or System.ComponentModel.Win32Exception or DllNotFoundException) { }
        for (uint i = 0; i < 4; i++) if (XInput.TryState(i, out _)) vendors.Add(ControllerCatalog.Microsoft);

        var brands = vendors.Distinct().ToList();
        _list.Children.Add(new TextBlock { Text = brands.Count == 0 ? "Nenhum controle ligado agora. Veja os programas oficiais por marca:" : "Seus controles", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 8) });
        var show = brands.Count == 0
            ? new ushort[] { ControllerCatalog.Sony, ControllerCatalog.Microsoft, ControllerCatalog.EightBitDo, ControllerCatalog.Nintendo, ControllerCatalog.Logitech }
            : brands.ToArray();
        foreach (ushort vendor in show)
        {
            var resources = ControllerCatalog.Resources(vendor);
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = ControllerCatalog.Brand(vendor), FontSize = 14, FontWeight = FontWeights.SemiBold });
            if (resources.Count == 0) stack.Children.Add(PadUi.Muted("Este controle usa o driver padrão do Windows. Se algum botão não responder, use Procurar drivers que faltam ou o Steam Input."));
            foreach (var r in resources) stack.Children.Add(ResourceRow(r));
            _list.Children.Add(PadUi.Card(stack));
        }
        var general = new StackPanel();
        general.Children.Add(new TextBlock { Text = "Para qualquer controle", FontSize = 14, FontWeight = FontWeights.SemiBold });
        foreach (var r in ControllerCatalog.General) general.Children.Add(ResourceRow(r));
        _list.Children.Add(PadUi.Card(general));
    }

    private static UIElement ResourceRow(ControllerResource r)
    {
        var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = r.Title, FontSize = 13, FontWeight = FontWeights.SemiBold });
        text.Children.Add(PadUi.Muted(r.Description));
        grid.Children.Add(text);
        var open = PadUi.Button("Abrir");
        open.Margin = new Thickness(10, 0, 0, 0);
        open.VerticalAlignment = VerticalAlignment.Center;
        open.Click += (_, _) => PadUi.Open(r.Url);
        Grid.SetColumn(open, 1);
        grid.Children.Add(open);
        return grid;
    }
}

/// <summary>Aba "Perfis": zona morta, curva, sensibilidade, gatilhos e vibração por tipo de jogo, com prévia ao vivo no analógico.</summary>
internal sealed class PadProfilesPage : UserControl
{
    private readonly ComboBox _profiles = new() { Width = 260, Margin = new Thickness(0, 0, 8, 0) };
    private readonly StackPanel _editor = new();
    private readonly TextBlock _notes = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Canvas _preview = new() { Width = 180, Height = 180 };
    private readonly Ellipse _raw = new() { Width = 10, Height = 10 };
    private readonly Ellipse _out = new() { Width = 14, Height = 14 };
    private readonly Ellipse _dead = new();
    private readonly System.Windows.Threading.DispatcherTimer _timer = new(System.Windows.Threading.DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    private PadProfile _current = PadProfiles.Defaults[0].Clone();

    public PadProfilesPage()
    {
        var root = PadUi.Root("Perfis do controle",
            "Monte e salve um perfil por tipo de jogo. A prévia mostra ao vivo como o analógico esquerdo fica com o perfil (ponto amarelo) e sem ele (ponto cinza). " +
            "Use os mesmos valores nas configurações de controle do jogo ou no Steam Input.", out var scroll);

        var top = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        _profiles.SelectionChanged += (_, _) =>
        {
            if (_profiles.SelectedItem is ComboBoxItem { Tag: PadProfile p }) { _current = p.Clone(); BuildEditor(); }
        };
        var save = PadUi.Button("Salvar perfil", primary: true);
        save.Click += (_, _) => Save();
        var remove = PadUi.Button("Apagar");
        remove.Click += (_, _) => Remove();
        top.Children.Add(_profiles);
        top.Children.Add(save);
        top.Children.Add(remove);
        root.Children.Add(top);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition());
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.Children.Add(PadUi.Card(_editor));
        var previewBox = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        previewBox.Children.Add(new TextBlock { Text = "Prévia ao vivo", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        var ring = new Ellipse { Width = 180, Height = 180, Stroke = PadUi.Res("BorderBrush"), StrokeThickness = 2 };
        _dead.Fill = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0x5A, 0x5F));
        _raw.Fill = PadUi.Res("MutedBrush");
        _out.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00));
        _preview.Children.Add(ring);
        _preview.Children.Add(_dead);
        _preview.Children.Add(_raw);
        _preview.Children.Add(_out);
        previewBox.Children.Add(PadUi.Card(_preview));
        previewBox.Children.Add(PadUi.Muted("Funciona com controles Xbox/XInput.\nPlayStation: use a aba Calibrar.", 11));
        Grid.SetColumn(previewBox, 1);
        body.Children.Add(previewBox);
        root.Children.Add(body);
        root.Children.Add(_notes);
        Content = scroll;

        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) => { FillList(AppServices.Settings.PadActiveProfile); _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
    }

    private static List<PadProfile> All() =>
        AppServices.Settings.PadProfiles.Concat(PadProfiles.Defaults.Where(d => AppServices.Settings.PadProfiles.All(s => s.Name != d.Name))).ToList();

    private void FillList(string select)
    {
        _profiles.Items.Clear();
        foreach (var p in All())
        {
            bool mine = AppServices.Settings.PadProfiles.Any(s => s.Name == p.Name);
            var item = new ComboBoxItem { Content = p.Name + (mine ? " (meu)" : ""), Tag = p };
            _profiles.Items.Add(item);
            if (p.Name == select) _profiles.SelectedItem = item;
        }
        if (_profiles.SelectedItem is null && _profiles.Items.Count > 0) _profiles.SelectedIndex = 0;
    }

    private void BuildEditor()
    {
        _editor.Children.Clear();
        var name = new TextBox { Text = _current.Name, Margin = new Thickness(0, 2, 0, 10), FontSize = 13 };
        name.TextChanged += (_, _) => _current.Name = name.Text.Trim();
        _editor.Children.Add(PadUi.Muted("Nome do perfil (ex.: Warzone, F1, Street Fighter)"));
        _editor.Children.Add(name);
        _editor.Children.Add(Slider("Zona morta do analógico", 0, 30, _current.Deadzone, v => _current.Deadzone = v, "%"));
        _editor.Children.Add(Slider("Anti-zona morta", 0, 30, _current.AntiDeadzone, v => _current.AntiDeadzone = v, "%"));
        _editor.Children.Add(Slider("Sensibilidade", 50, 150, _current.Sensitivity, v => _current.Sensitivity = v, "%"));
        _editor.Children.Add(Slider("Zona morta dos gatilhos", 0, 30, _current.TriggerDeadzone, v => _current.TriggerDeadzone = v, "%"));
        _editor.Children.Add(Slider("Vibração", 0, 100, _current.Vibration, v => _current.Vibration = v, "%"));

        _editor.Children.Add(PadUi.Muted("Curva do analógico"));
        var curves = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var curve in Enum.GetValues<StickCurve>().Where(c => c != StickCurve.Personalizada))
        {
            var c = curve;
            var b = new RadioButton { Content = curve switch { StickCurve.Suave => "Suave (mira fina)", StickCurve.Agressiva => "Agressiva (resposta rápida)", _ => "Linear" }, IsChecked = _current.Curve == curve, Margin = new Thickness(0, 0, 14, 0), GroupName = "curve" };
            b.Checked += (_, _) => _current.Curve = c;
            curves.Children.Add(b);
        }
        _editor.Children.Add(curves);
        _notes.Foreground = PadUi.Res("MutedBrush");
        _notes.Text = _current.Notes;
    }

    private static UIElement Slider(string title, int min, int max, int value, Action<int> set, string unit) =>
        PadUi.SliderRow(title, min, max, value, v => $"{v:0}{unit}", v => set((int)v));

    private void Tick()
    {
        double x = 0, y = 0;
        for (uint i = 0; i < 4; i++)
            if (XInput.TryState(i, out var s)) { x = s.LX / 32767.0; y = -s.LY / 32767.0; break; }
        var (ox, oy) = PadProfiles.Apply(_current, x, y);
        const double c = 90, r = 86;
        double dz = Math.Clamp(_current.Deadzone, 0, 50) / 100.0 * r * 2;
        _dead.Width = _dead.Height = dz;
        Canvas.SetLeft(_dead, c - dz / 2);
        Canvas.SetTop(_dead, c - dz / 2);
        Canvas.SetLeft(_raw, c + x * r - 5);
        Canvas.SetTop(_raw, c + y * r - 5);
        Canvas.SetLeft(_out, c + ox * r - 7);
        Canvas.SetTop(_out, c + oy * r - 7);
    }

    private void Save()
    {
        if (_current.Name.Length == 0) { MessageBox.Show("Dê um nome ao perfil.", "Perfis", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var list = AppServices.Settings.PadProfiles;
        list.RemoveAll(p => p.Name == _current.Name);
        list.Add(_current.Clone());
        AppServices.Settings.PadActiveProfile = _current.Name;
        AppServices.Settings.Save();
        FillList(_current.Name);
        _notes.Foreground = PadUi.Res("OkBrush");
        _notes.Text = $"Perfil \"{_current.Name}\" salvo. Use estes valores nas configurações de controle do jogo ou no Steam Input.";
    }

    private void Remove()
    {
        if (AppServices.Settings.PadProfiles.RemoveAll(p => p.Name == _current.Name) == 0)
        {
            MessageBox.Show("Os perfis prontos não podem ser apagados; só os que você salvou.", "Perfis", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        AppServices.Settings.Save();
        FillList("");
    }
}
