using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;
using System.Windows.Media;
using System.Windows.Threading;
using MBExtreme.Core.Gamepads;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Faixa fixa no alto das telas do MB Control, no estilo da seleção do menu lateral: o controle ativo e, com ícones,
/// a ligação (USB ou Bluetooth), a bateria e a latência medida.
/// </summary>
internal sealed class PadStatusStrip : UserControl
{
    private const string Icons = "Segoe MDL2 Assets";
    private static readonly Brush Yellow = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00));
    private readonly TextBlock _name = new() { FontSize = 15, FontWeight = FontWeights.Bold };
    private readonly TextBlock _sub = new() { FontSize = 12 };
    private readonly Border _ring = new() { Width = 46, Height = 46, CornerRadius = new CornerRadius(23), BorderThickness = new Thickness(2.5) };
    private readonly TextBlock _ringIcon = new() { Text = "\uE7FC", FontFamily = new FontFamily(Icons), FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly (TextBlock Icon, TextBlock Value, TextBlock Caption, Border Box) _conn = Stat(), _battery = Stat(), _latency = Stat();
    private readonly StackPanel _stats = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _off = PadUi.Button("Desligar");

    private static (TextBlock, TextBlock, TextBlock, Border) Stat()
    {
        var icon = new TextBlock { FontFamily = new FontFamily(Icons), FontSize = 18, Foreground = Yellow, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        var value = new TextBlock { FontSize = 13.5, FontWeight = FontWeights.Bold };
        var caption = new TextBlock { FontSize = 11, Foreground = PadUi.Res("MutedBrush") };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(value);
        text.Children.Add(caption);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(icon);
        row.Children.Add(text);
        var box = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x55, 0x00, 0x00, 0x00)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0), Child = row,
        };
        return (icon, value, caption, box);
    }

    public PadStatusStrip()
    {
        _sub.Foreground = PadUi.Res("MutedBrush");
        _ring.Child = _ringIcon;
        _off.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x8E));
        _off.VerticalAlignment = VerticalAlignment.Center;
        _off.ToolTip = "Desliga o controle (só no Bluetooth). Para religar, aperte o botão PS.";
        _off.Click += (_, _) => TurnOff();
        _stats.Children.Add(_conn.Box);
        _stats.Children.Add(_battery.Box);
        _stats.Children.Add(_latency.Box);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(_ring);
        var text = new StackPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(_name);
        text.Children.Add(_sub);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        Grid.SetColumn(_stats, 2);
        grid.Children.Add(_stats);
        Grid.SetColumn(_off, 3);
        grid.Children.Add(_off);

        // mesmo estilo da seleção do menu lateral: vidro escuro com brilho amarelo à esquerda
        var glass = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        glass.GradientStops.Add(new GradientStop(Color.FromArgb(0x3A, 0xFF, 0xC4, 0x00), 0));
        glass.GradientStops.Add(new GradientStop(Color.FromArgb(0x10, 0xFF, 0xC4, 0x00), 0.35));
        glass.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xC4, 0x00), 1));
        var inner = new Border { Background = glass, CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 10, 14, 10), Child = grid };
        Content = new Border
        {
            Background = PadUi.Res("SurfaceBrush"), BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xC4, 0x00)), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16), Margin = new Thickness(0, 0, 0, 16), Child = inner,
        };
    }

    private static string BatteryGlyph(int level) => level >= 95 ? "\uE83F" : ((char)(0xE850 + Math.Clamp(level / 10, 0, 9))).ToString();

    public void Refresh()
    {
        var pads = AppServices.Pads;
        var d = pads.Current;
        if (d is null)
        {
            _ring.BorderBrush = PadUi.Res("MutedBrush");
            _ringIcon.Foreground = PadUi.Res("MutedBrush");
            _name.Text = "Nenhum controle conectado";
            _sub.Text = pads.Status.Length > 0 ? pads.Status : "Ligue pelo cabo USB ou pareie pelo Bluetooth do Windows.";
            _stats.Visibility = Visibility.Collapsed;
            _off.Visibility = Visibility.Collapsed;
            return;
        }
        var s = pads.State;
        _stats.Visibility = Visibility.Visible;
        _ring.BorderBrush = PadUi.Res("OkBrush");
        _ringIcon.Foreground = Yellow;
        _name.Text = pads.DisplayName(d);
        string profile = AppServices.Settings.PadActiveProfile.Length > 0 ? AppServices.Settings.PadActiveProfile : "Padrão";
        _sub.Text = $"Perfil: {profile}" + (pads.Info?.BluetoothAddress is { } mac ? " · " + mac : "") + (AppServices.Emulation.Running ? " · emulação ligada" : "");

        (_conn.Icon.Text, _conn.Value.Text, _conn.Caption.Text) = d.Connection switch
        {
            PadConnection.Bluetooth => ("\uE702", "Bluetooth", "Sem fio · conectado"),
            PadConnection.Usb => ("\uE88E", "Cabo USB", "Com fio · conectado"),
            _ => ("\uE7FC", "XInput", "Controle Xbox"),
        };
        if (s.Battery >= 0)
        {
            _battery.Box.Visibility = Visibility.Visible;
            _battery.Icon.Text = BatteryGlyph(s.Battery);
            _battery.Icon.Foreground = s.Battery <= 20 && !s.Charging ? PadUi.Res("BadBrush") : PadUi.Res("OkBrush");
            _battery.Value.Text = $"{s.Battery}%";
            _battery.Caption.Text = s.Charging ? "Carregando" : s.Cable ? "No cabo" : "Bateria";
        }
        else _battery.Box.Visibility = Visibility.Collapsed;
        double hz = pads.RateHz;
        _latency.Icon.Text = "\uEC4A";
        _latency.Value.Text = hz > 0 ? $"{1000 / hz:0.0} ms" : "—";
        _latency.Caption.Text = hz > 0 ? $"Latência · {hz:0} Hz" : "Latência · mexa o controle";
        _off.Visibility = d.Connection == PadConnection.Bluetooth ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void TurnOff()
    {
        string? mac = AppServices.Pads.Info?.BluetoothAddress;
        if (mac is null || !MBExtreme.Core.Gamepads.WinBluetooth.Disconnect(mac))
            MessageBox.Show("Não foi possível desligar pelo PC. Segure o botão PS por 10 segundos para desligar o controle.", "MB Control", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}

/// <summary>Base das telas do MB Control: faixa de status, título e a leitura do controle enquanto a tela está aberta.</summary>
internal abstract class MbControlPageBase : UserControl
{
    protected readonly PadStatusStrip Strip = new();
    protected readonly StackPanel Body = new();
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    private int _tick;

    protected MbControlPageBase(string title, string subtitle)
    {
        var root = new StackPanel { Margin = new Thickness(28, 18, 28, 28) };
        root.Children.Add(Strip);
        root.Children.Add(new TextBlock { Text = title, FontSize = 26, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock { Text = subtitle, FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = PadUi.Res("MutedBrush"), Margin = new Thickness(0, 4, 0, 14) });
        root.Children.Add(Body);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
        _timer.Tick += (_, _) =>
        {
            Live(AppServices.Pads.State);
            if (_tick % 15 == 0) Strip.Refresh();
            if (_tick++ % 30 == 0) Slow();
        };
        Loaded += (_, _) => { AppServices.Pads.Attach(); _timer.Start(); Strip.Refresh(); Slow(); };
        Unloaded += (_, _) => { _timer.Stop(); AppServices.Pads.Detach(); };
    }

    /// <summary>Chamado ~30 vezes por segundo com o estado atual.</summary>
    protected virtual void Live(PadState s) { }

    /// <summary>Chamado 2 vezes por segundo (informações que mudam devagar).</summary>
    protected virtual void Slow() { }
}

/// <summary>MB Control · Meu controle: desenho ao vivo, informações do aparelho, recursos, renomear e copiar.</summary>
internal sealed class MbOverviewPage : MbControlPageBase
{
    private readonly PadView _pad = new() { Margin = new Thickness(0, 6, 0, 6), HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _left = Tile(), _right = Tile(), _triggers = Tile(), _gyro = Tile();
    private readonly UniformGrid _info = new() { Columns = 2 };
    private readonly UniformGrid _features = new() { Columns = 3 };
    private readonly ComboBox _picker = new() { Width = 260, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    private string _pickerKey = "";
    private List<(string, string)> _rows = new();
    private IReadOnlyList<PadFeature> _feat = Array.Empty<PadFeature>();

    public MbOverviewPage() : base("Meu controle", "Tudo sobre o controle conectado, ao vivo. Funciona pelo cabo USB e pelo Bluetooth (PS4, PS5, PS5 Edge e controles Xbox).")
    {
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        _picker.SelectionChanged += (_, _) =>
        {
            if (_picker.SelectedItem is ComboBoxItem { Tag: string key } && key != AppServices.Pads.Current?.Key) AppServices.Pads.Select(key);
        };
        var rename = PadUi.Button("Renomear");
        rename.Click += (_, _) => Rename();
        var copy = PadUi.Button("Copiar informações");
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(PadDetails.ToText("MB Control · " + (AppServices.Pads.Current?.Name ?? "controle"), _rows, _feat)); }
            catch (System.Runtime.InteropServices.COMException) { }
        };
        var calib = PadUi.Button("Calibrar", primary: true);
        calib.Click += (_, _) => MainWindow.Current?.Go("padcalib");
        bar.Children.Add(_picker);
        bar.Children.Add(rename);
        bar.Children.Add(copy);
        bar.Children.Add(calib);
        Body.Children.Add(bar);

        var cols = new Grid();
        cols.ColumnDefinitions.Add(new ColumnDefinition());
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        cols.ColumnDefinitions.Add(new ColumnDefinition());
        var live = new StackPanel();
        live.Children.Add(Heading("Ao vivo"));
        live.Children.Add(_pad);
        var tiles = new UniformGrid { Columns = 2 };
        foreach (var (label, value) in new[] { ("Analógico esquerdo", _left), ("Analógico direito", _right), ("Gatilhos", _triggers), ("Giroscópio (°/s)", _gyro) })
            tiles.Children.Add(Box(label, value));
        live.Children.Add(tiles);
        cols.Children.Add(PadUi.Card(live));
        var info = new StackPanel();
        info.Children.Add(Heading("Informações do aparelho"));
        info.Children.Add(_info);
        var infoCard = PadUi.Card(info);
        Grid.SetColumn(infoCard, 2);
        cols.Children.Add(infoCard);
        Body.Children.Add(cols);

        var feats = new StackPanel();
        feats.Children.Add(Heading("Recursos deste controle"));
        feats.Children.Add(_features);
        Body.Children.Add(PadUi.Card(feats));
        Body.Children.Add(PadUi.Muted("Dica: pelo cabo USB a taxa costuma chegar perto de 1000 Hz no PS5; no Bluetooth fica entre 250 e 800 Hz, conforme o adaptador. A vibração háptica e o som no controle só funcionam pelo cabo.", 11.5));
    }

    private static TextBlock Tile() => new() { FontSize = 13, FontWeight = FontWeights.SemiBold };

    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };

    private static Border Box(string label, UIElement value)
    {
        var st = new StackPanel();
        st.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = PadUi.Res("MutedBrush") });
        st.Children.Add(value);
        return new Border { Background = PadUi.Res("Surface2Brush"), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 8, 8), Child = st };
    }

    protected override void Live(PadState s)
    {
        var cur = AppServices.Pads.Current;
        _pad.Look = cur?.XInputSlot is not null ? PadLook.Xbox : cur?.Sony == PadModel.DualShock4 ? PadLook.DualShock4 : PadLook.DualSense;
        var light = AppServices.Settings.PadLight;
        _pad.LightColor = light.Enabled ? Color.FromRgb(light.R, light.G, light.B) : Color.FromRgb(0x2D, 0x6C, 0xFF);
        _pad.Show(s);
        _left.Text = $"X {s.LX:0.00} · Y {-s.LY:0.00}";
        _right.Text = $"X {s.RX:0.00} · Y {-s.RY:0.00}";
        _triggers.Text = $"L2 {s.L2 * 100:0}% · R2 {s.R2 * 100:0}%";
        _gyro.Text = AppServices.Pads.Current?.IsSony == true ? $"{s.GyroPitch:0} · {s.GyroYaw:0} · {s.GyroRoll:0}" : "—";
    }

    protected override void Slow()
    {
        var pads = AppServices.Pads;
        string key = string.Join("|", pads.Devices.Select(d => d.Key)) + "#" + pads.Current?.Key;
        if (key != _pickerKey)
        {
            _pickerKey = key;
            _picker.Items.Clear();
            foreach (var d in pads.Devices)
            {
                var item = new ComboBoxItem { Content = $"{pads.DisplayName(d)} · {(d.Connection == PadConnection.Bluetooth ? "Bluetooth" : d.Connection == PadConnection.Usb ? "USB" : "XInput")}", Tag = d.Key };
                _picker.Items.Add(item);
                if (d.Key == pads.Current?.Key) _picker.SelectedItem = item;
            }
        }

        var cur = pads.Current;
        _rows = new List<(string, string)>();
        if (cur is not null)
        {
            var info = pads.Info;
            var s = pads.State;
            double hz = pads.RateHz;
            _rows.Add(("Nome", pads.DisplayName(cur)));
            _rows.Add(("Tipo", cur.Name));
            _rows.Add(("Ligação", cur.Connection switch { PadConnection.Bluetooth => "Bluetooth", PadConnection.Usb => "Cabo USB", _ => "XInput (Windows)" }));
            if (cur.Hid is { } hid) _rows.Add(("VID:PID", $"054C:{hid.Product:X4}"));
            if (info?.BluetoothAddress is { } mac) _rows.Add(("Endereço (MAC)", mac));
            if (info?.Serial is { } serial && serial != info.BluetoothAddress) _rows.Add(("Número de série", serial));
            if (info?.Firmware is { } fw) _rows.Add(("Firmware", fw));
            if (info?.BuildDate is { Length: > 0 } date) _rows.Add(("Data do firmware", date));
            if (info?.Board is { } board) _rows.Add(("Placa", board));
            if (info?.Color is { } color) _rows.Add(("Cor", color));
            if (info?.LooksClone == true) _rows.Add(("Atenção", "Parece paralelo: " + info.CloneReason));
            if (s.Battery >= 0) _rows.Add(("Bateria", s.Charging ? $"{s.Battery}% · carregando" : s.Cable ? $"{s.Battery}% · no cabo" : $"{s.Battery}%"));
            _rows.Add(("Taxa medida", hz > 0 ? $"{hz:0} Hz ({1000 / hz:0.0} ms)" : "parado (mexa um analógico)"));
            if (pads.ConnectedAt is { } at) _rows.Add(("Tempo conectado", PadDetails.Duration(DateTime.Now - at)));
            _rows.Add(("Tempo parado", PadDetails.Duration(DateTime.Now - pads.LastChange)));
            _feat = cur.Sony is { } model ? PadDetails.Features(model, cur.Connection)
                : new[] { new PadFeature("Vibração", true), new PadFeature("Gatilhos com vibração (Xbox One/Series)", true, "Pelo app Acessórios Xbox") };
        }
        else _feat = Array.Empty<PadFeature>();

        _info.Children.Clear();
        foreach (var (label, value) in _rows) _info.Children.Add(Box(label, new TextBlock { Text = value, FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap }));
        if (_rows.Count == 0) _info.Children.Add(PadUi.Muted(pads.Status, 13));
        _features.Children.Clear();
        foreach (var f in _feat)
        {
            var g = new DockPanel();
            var state = new TextBlock { Text = f.Supported ? (f.Note.Length > 0 ? f.Note : "Sim") : (f.Note.Length > 0 ? f.Note : "Não"), FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = PadUi.Res(f.Supported ? "OkBrush" : "WarnBrush") };
            DockPanel.SetDock(state, Dock.Right);
            g.Children.Add(state);
            g.Children.Add(new TextBlock { Text = f.Name, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
            _features.Children.Add(new Border { Background = PadUi.Res("Surface2Brush"), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 8, 8), Child = g });
        }
    }

    private void Rename()
    {
        var pads = AppServices.Pads;
        string? key = pads.Info?.BluetoothAddress ?? pads.Info?.Serial;
        if (pads.Current is null || key is null)
        {
            MessageBox.Show("Conecte um controle PlayStation para dar um nome a ele.", "MB Control", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new FormDialog(Window.GetWindow(this), "Renomear controle", "O nome aparece no MB Control e no topo das telas.", "Salvar");
        var name = dialog.Text("Nome", pads.DisplayName(pads.Current), 40);
        if (dialog.ShowDialog() != true) return;
        AppServices.Settings.PadNames[key] = name().Trim();
        AppServices.Settings.Save();
        _pickerKey = "";
        Strip.Refresh();
        Slow();
    }
}

/// <summary>Telas do MB Control que ainda vão chegar: mostram o que vem e em que etapa.</summary>
internal sealed class MbSoonPage : MbControlPageBase
{
    public MbSoonPage(string title, string subtitle, params string[] items) : base(title, subtitle)
    {
        var list = new StackPanel();
        list.Children.Add(new TextBlock { Text = "Em construção", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = PadUi.Res("WarnBrush"), Margin = new Thickness(0, 0, 0, 8) });
        foreach (string item in items) list.Children.Add(new TextBlock { Text = "•  " + item, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
        Body.Children.Add(PadUi.Card(list));
    }
}
