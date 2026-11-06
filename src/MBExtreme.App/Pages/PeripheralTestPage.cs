using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MBExtreme.Core.Devices;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Testes de periféricos: teclado (cada tecla acende ao apertar), mouse (botões, roda, clique duplo "fantasma" e taxa de atualização
/// pela entrada bruta do Windows) e controle XInput (botões, gatilhos, analógicos com deriva, circularidade, zona morta e vibração).
/// </summary>
public sealed partial class PeripheralTestPage : ToolPageBase
{
    /// <summary>Enquanto esta página está aberta, Alt+← e o botão lateral do mouse não voltam de página (eles estão sendo testados).</summary>
    public static bool Active { get; private set; }

    private int _tab;
    private readonly StackPanel _host = new();

    public PeripheralTestPage() : base("Teste de periféricos",
        "Teste o teclado, o mouse, o controle, a tela, o som, o microfone e a webcam sem instalar nada. Clique na área do teste antes de começar, para o programa receber as teclas e os cliques.")
    {
        Focusable = true;
        Body.Children.Add(Choice(new[] { "Teclado", "Mouse", "Controle", "Tela", "Alto-falantes", "Microfone", "Webcam" }, 0, i => { _tab = i; Show(); }));
        Body.Children.Add(_host);
        Loaded += (_, _) => { Active = true; Show(); };
        Unloaded += (_, _) => { Active = false; StopAll(); };
    }

    private void StopAll()
    {
        _padTimer.Stop();
        Vibrate(0, 0);
        UnregisterRaw();
        StopMedia();
    }

    private void Show()
    {
        StopAll();
        _host.Children.Clear();
        switch (_tab)
        {
            case 0: BuildKeyboard(); break;
            case 1: BuildMouse(); break;
            case 2: BuildPad(); break;
            case 3: BuildScreen(); break;
            case 4: BuildSpeakers(); break;
            case 5: BuildMic(); break;
            default: BuildWebcam(); break;
        }
    }

    // ---------------------------------------------------------------- teclado

    private readonly Dictionary<Key, Border> _keys = new();
    private readonly HashSet<Key> _down = new();
    private int _maxTogether;
    private readonly TextBlock _keyInfo = new() { FontSize = 13, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };

    private static readonly (string Label, Key Key, double Width)[][] Layout =
    {
        new[] { ("Esc", Key.Escape, 1.0), ("F1", Key.F1, 1), ("F2", Key.F2, 1), ("F3", Key.F3, 1), ("F4", Key.F4, 1), ("F5", Key.F5, 1), ("F6", Key.F6, 1), ("F7", Key.F7, 1), ("F8", Key.F8, 1), ("F9", Key.F9, 1), ("F10", Key.F10, 1), ("F11", Key.F11, 1), ("F12", Key.F12, 1) },
        new[] { ("'", Key.Oem3, 1.0), ("1", Key.D1, 1), ("2", Key.D2, 1), ("3", Key.D3, 1), ("4", Key.D4, 1), ("5", Key.D5, 1), ("6", Key.D6, 1), ("7", Key.D7, 1), ("8", Key.D8, 1), ("9", Key.D9, 1), ("0", Key.D0, 1), ("-", Key.OemMinus, 1), ("=", Key.OemPlus, 1), ("⌫", Key.Back, 2) },
        new[] { ("Tab", Key.Tab, 1.5), ("Q", Key.Q, 1), ("W", Key.W, 1), ("E", Key.E, 1), ("R", Key.R, 1), ("T", Key.T, 1), ("Y", Key.Y, 1), ("U", Key.U, 1), ("I", Key.I, 1), ("O", Key.O, 1), ("P", Key.P, 1), ("´", Key.Oem4, 1), ("[", Key.Oem6, 1), ("Enter", Key.Enter, 1.5) },
        new[] { ("Caps", Key.CapsLock, 1.75), ("A", Key.A, 1), ("S", Key.S, 1), ("D", Key.D, 1), ("F", Key.F, 1), ("G", Key.G, 1), ("H", Key.H, 1), ("J", Key.J, 1), ("K", Key.K, 1), ("L", Key.L, 1), ("Ç", Key.Oem1, 1), ("~", Key.Oem7, 1), ("]", Key.Oem5, 1) },
        new[] { ("Shift", Key.LeftShift, 1.25), ("\\", Key.OemBackslash, 1), ("Z", Key.Z, 1), ("X", Key.X, 1), ("C", Key.C, 1), ("V", Key.V, 1), ("B", Key.B, 1), ("N", Key.N, 1), ("M", Key.M, 1), (",", Key.OemComma, 1), (".", Key.OemPeriod, 1), (";", Key.OemQuestion, 1), ("/", Key.AbntC1, 1), ("Shift", Key.RightShift, 1.75) },
        new[] { ("Ctrl", Key.LeftCtrl, 1.5), ("Win", Key.LWin, 1.25), ("Alt", Key.LeftAlt, 1.25), ("Espaço", Key.Space, 6), ("AltGr", Key.RightAlt, 1.25), ("Menu", Key.Apps, 1.25), ("Ctrl", Key.RightCtrl, 1.5), ("←", Key.Left, 1), ("↑", Key.Up, 1), ("↓", Key.Down, 1), ("→", Key.Right, 1) },
    };

    private void BuildKeyboard()
    {
        _keys.Clear();
        _down.Clear();
        _maxTogether = 0;
        var board = new StackPanel { Focusable = true, Background = Brushes.Transparent };
        foreach (var row in Layout)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            foreach (var (label, key, width) in row)
            {
                var cell = new Border
                {
                    Width = 42 * width + 4 * (width - 1), Height = 40, Margin = new Thickness(0, 0, 4, 0), CornerRadius = new CornerRadius(6),
                    BorderThickness = new Thickness(1), BorderBrush = Brush("BorderBrush"), Background = Brush("SurfaceBrush"),
                    Child = new TextBlock { Text = label, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                };
                _keys.TryAdd(key, cell);
                line.Children.Add(cell);
            }
            board.Children.Add(line);
        }
        board.MouseDown += (_, _) => board.Focus();
        board.PreviewKeyDown += OnKeyDown;
        board.PreviewKeyUp += OnKeyUp;
        _host.Children.Add(Muted("Clique no teclado abaixo e aperte as teclas. Amarelo = funcionou; claro = apertada agora. Teste também várias juntas (jogos usam W, A, Shift e Espaço ao mesmo tempo).", 12));
        _host.Children.Add(CardOf(board));
        Detach(_keyInfo);
        _host.Children.Add(_keyInfo);
        _keyInfo.Text = "Nenhuma tecla apertada ainda.";
        Dispatcher.BeginInvoke(new Action(() => board.Focus()), DispatcherPriority.Input);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        e.Handled = true;   // nada de navegar com Tab ou setas durante o teste
        _down.Add(key);
        _maxTogether = Math.Max(_maxTogether, _down.Count);
        if (_keys.TryGetValue(key, out var cell)) { cell.Background = Brush("AccentBrush"); ((TextBlock)cell.Child).Foreground = Brushes.Black; cell.Opacity = 0.7; }
        int tested = _keys.Values.Count(c => c.Background == Brush("AccentBrush"));
        _keyInfo.Text = $"Última: {key} · {tested} de {_keys.Count} teclas testadas · até {_maxTogether} apertadas juntas" +
                        (_maxTogether >= 6 ? " (sem limite aparente)" : "");
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        e.Handled = true;
        _down.Remove(key);
        if (_keys.TryGetValue(key, out var cell)) cell.Opacity = 1;
    }

    // ---------------------------------------------------------------- mouse

    private readonly Dictionary<MouseButton, Border> _buttons = new();
    private readonly TextBlock _mouseInfo = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock _rateInfo = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private int _wheelUp, _wheelDown;
    private readonly Dictionary<MouseButton, long> _lastClick = new();
    private int _chatter;
    private readonly List<double> _raw = new();
    private readonly Stopwatch _rawClock = Stopwatch.StartNew();
    private HwndSource? _hwnd;

    private void BuildMouse()
    {
        _buttons.Clear();
        _wheelUp = _wheelDown = _chatter = 0;
        _lastClick.Clear();
        var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var (button, label) in new[] { (MouseButton.Left, "Esquerdo"), (MouseButton.Middle, "Meio (roda)"), (MouseButton.Right, "Direito"), (MouseButton.XButton1, "Lateral 1 (voltar)"), (MouseButton.XButton2, "Lateral 2 (avançar)") })
        {
            var cell = new Border
            {
                Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1), BorderBrush = Brush("BorderBrush"), Background = Brush("SurfaceBrush"),
                Child = new TextBlock { Text = label, FontSize = 13 },
            };
            _buttons[button] = cell;
            row.Children.Add(cell);
        }
        var area = new Border
        {
            Height = 260, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(2), BorderBrush = Brush("AccentBrush"), Background = Brush("SurfaceBrush"),
            Child = new TextBlock { Text = "Clique, gire a roda e mexa o mouse aqui dentro", Foreground = Brush("MutedBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        area.PreviewMouseDown += (_, e) => { e.Handled = true; OnMouseButton(e.ChangedButton, true); };
        area.PreviewMouseUp += (_, e) => { e.Handled = true; OnMouseButton(e.ChangedButton, false); };
        area.PreviewMouseWheel += (_, e) => { e.Handled = true; if (e.Delta > 0) _wheelUp++; else _wheelDown++; UpdateMouseInfo(); };
        area.ContextMenuOpening += (_, e) => e.Handled = true;
        _host.Children.Add(row);
        _host.Children.Add(area);
        Detach(_mouseInfo);
        Detach(_rateInfo);
        _host.Children.Add(_mouseInfo);
        _host.Children.Add(_rateInfo);
        _rateInfo.Text = "Taxa de atualização: mexa o mouse em círculos rápidos por uns segundos.";
        UpdateMouseInfo();
        RegisterRaw();
    }

    private void OnMouseButton(MouseButton button, bool down)
    {
        if (_buttons.TryGetValue(button, out var cell))
        {
            cell.Background = down ? Brush("AccentBrush") : Brush("SurfaceBrush");
            ((TextBlock)cell.Child).Foreground = down ? Brushes.Black : Brush("TextBrush");
            if (!down) cell.BorderBrush = Brush("OkBrush");   // borda verde = já funcionou
        }
        if (down)
        {
            long now = _rawClock.ElapsedMilliseconds;
            if (_lastClick.TryGetValue(button, out long last) && MouseMath.IsChatter(now - last)) _chatter++;
            _lastClick[button] = now;
        }
        UpdateMouseInfo();
    }

    private void UpdateMouseInfo() =>
        _mouseInfo.Text = $"Roda: {_wheelUp} para cima, {_wheelDown} para baixo." +
                          (_chatter > 0 ? $"  Atenção: {_chatter} clique(s) duplicado(s) em menos de 80 ms sem você querer — sinal de botão gasto." : "  Cliques duplicados sem querer: nenhum.");

    private const int WmInput = 0x00FF;

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice { public ushort UsagePage; public ushort Usage; public uint Flags; public IntPtr Target; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RawInputDevice[] devices, uint count, uint size);

    private void RegisterRaw()
    {
        if (PresentationSource.FromVisual(this) is not HwndSource source) return;
        _hwnd = source;
        _raw.Clear();
        var device = new RawInputDevice { UsagePage = 1, Usage = 2, Flags = 0, Target = source.Handle };
        if (RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf<RawInputDevice>())) source.AddHook(Hook);
    }

    private void UnregisterRaw()
    {
        if (_hwnd is null) return;
        _hwnd.RemoveHook(Hook);
        var device = new RawInputDevice { UsagePage = 1, Usage = 2, Flags = 0x1, Target = IntPtr.Zero };   // RIDEV_REMOVE
        RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf<RawInputDevice>());
        _hwnd = null;
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmInput) return IntPtr.Zero;
        _raw.Add(_rawClock.Elapsed.TotalMilliseconds);
        if (_raw.Count > 2000) _raw.RemoveRange(0, 1000);
        if (_raw.Count % 100 == 0 && MouseMath.PollingRate(_raw.TakeLast(600).ToList()) is { } hz)
            _rateInfo.Text = $"Taxa de atualização: cerca de {hz:0} Hz (provavelmente {MouseMath.NearestStandard(hz)} Hz de fábrica). Mexa mais rápido se parecer baixa.";
        return IntPtr.Zero;
    }

    // ---------------------------------------------------------------- controle (XInput)

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad { public ushort Buttons; public byte LeftTrigger; public byte RightTrigger; public short LX, LY, RX, RY; }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState { public uint Packet; public XInputGamepad Pad; }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputVibration { public ushort Left; public ushort Right; }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState(uint index, out XInputState state);

    [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")]
    private static extern uint XInputSetState(uint index, ref XInputVibration vibration);

    private readonly DispatcherTimer _padTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private int _pad = -1;
    private readonly TextBlock _padInfo = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap };
    private readonly WrapPanel _padButtons = new();
    private readonly Dictionary<ushort, Border> _padCells = new();
    private readonly ProgressBar _lt = new() { Maximum = 255, Height = 12, Width = 180 }, _rt = new() { Maximum = 255, Height = 12, Width = 180 };
    private readonly (Canvas Canvas, Ellipse Dot) _left = StickView(), _right = StickView();
    private readonly TextBlock _stickResult = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private List<StickSample>? _collectL, _collectR;

    private static readonly (ushort Mask, string Label)[] PadButtons =
    {
        (0x1000, "A"), (0x2000, "B"), (0x4000, "X"), (0x8000, "Y"), (0x0100, "LB"), (0x0200, "RB"), (0x0010, "Start"), (0x0020, "Back"),
        (0x0040, "L3"), (0x0080, "R3"), (0x0001, "↑"), (0x0002, "↓"), (0x0004, "←"), (0x0008, "→"),
    };

    private static (Canvas, Ellipse) StickView()
    {
        var canvas = new Canvas { Width = 150, Height = 150, Margin = new Thickness(0, 0, 24, 0) };
        canvas.Children.Add(new Ellipse { Width = 150, Height = 150, Stroke = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x5E)), StrokeThickness = 2 });
        var dot = new Ellipse { Width = 14, Height = 14, Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)) };
        Canvas.SetLeft(dot, 68);
        Canvas.SetTop(dot, 68);
        canvas.Children.Add(dot);
        return (canvas, dot);
    }

    private void BuildPad()
    {
        _padCells.Clear();
        _padButtons.Children.Clear();
        foreach (var (mask, label) in PadButtons)
        {
            var cell = new Border
            {
                Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(8), MinWidth = 44,
                BorderThickness = new Thickness(1), BorderBrush = Brush("BorderBrush"), Background = Brush("SurfaceBrush"),
                Child = new TextBlock { Text = label, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center },
            };
            _padCells[mask] = cell;
            _padButtons.Children.Add(cell);
        }
        Detach(_padInfo);
        Detach(_padButtons);
        Detach(_stickResult);
        _host.Children.Add(_padInfo);
        _host.Children.Add(_padButtons);
        Detach(_lt);
        Detach(_rt);
        var triggers = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
        triggers.Children.Add(new TextBlock { Text = "LT ", VerticalAlignment = VerticalAlignment.Center });
        triggers.Children.Add(_lt);
        triggers.Children.Add(new TextBlock { Text = "   RT ", VerticalAlignment = VerticalAlignment.Center });
        triggers.Children.Add(_rt);
        _host.Children.Add(triggers);
        var sticks = new StackPanel { Orientation = Orientation.Horizontal };
        Detach(_left.Canvas);
        Detach(_right.Canvas);
        sticks.Children.Add(_left.Canvas);
        sticks.Children.Add(_right.Canvas);
        _host.Children.Add(sticks);
        var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        actions.Children.Add(SmallButton("Medir deriva (solte os analógicos por 3 s)", async (_, _) => await MeasureAsync(drift: true), true));
        actions.Children.Add(SmallButton("Testar circularidade (gire os dois por 5 s)", async (_, _) => await MeasureAsync(drift: false)));
        actions.Children.Add(SmallButton("Vibrar esquerdo", async (_, _) => await PulseAsync(left: true)));
        actions.Children.Add(SmallButton("Vibrar direito", async (_, _) => await PulseAsync(left: false)));
        _host.Children.Add(actions);
        _host.Children.Add(_stickResult);
        _host.Children.Add(Muted("Funciona com controles XInput (Xbox e compatíveis). Controles de PlayStation e Switch aparecem aqui só com um programa que os traduz (ex.: Steam ou DS4Windows).", 11.5));
        _padTimer.Tick -= PadTick;
        _padTimer.Tick += PadTick;
        _padTimer.Start();
    }

    private static void Detach(FrameworkElement element)
    {
        if (element.Parent is Panel panel) panel.Children.Remove(element);
    }

    private void PadTick(object? sender, EventArgs e)
    {
        XInputState state = default;
        int found = -1;
        try
        {
            for (uint i = 0; i < 4; i++)
                if (XInputGetState(i, out state) == 0) { found = (int)i; break; }
        }
        catch (DllNotFoundException) { _padInfo.Text = "O Windows deste PC não tem o XInput (xinput1_4.dll)."; _padTimer.Stop(); return; }
        _pad = found;
        if (found < 0) { _padInfo.Text = "Nenhum controle conectado. Ligue o controle (cabo ou sem fio) e espere uns segundos."; return; }
        _padInfo.Text = $"Controle {found + 1} conectado.";
        var pad = state.Pad;
        foreach (var (mask, cell) in _padCells)
        {
            bool on = (pad.Buttons & mask) != 0;
            cell.Background = on ? Brush("AccentBrush") : Brush("SurfaceBrush");
            ((TextBlock)cell.Child).Foreground = on ? Brushes.Black : Brush("TextBrush");
            if (on) cell.BorderBrush = Brush("OkBrush");
        }
        _lt.Value = pad.LeftTrigger;
        _rt.Value = pad.RightTrigger;
        var l = StickSample.FromXInput(pad.LX, pad.LY);
        var r = StickSample.FromXInput(pad.RX, pad.RY);
        Place(_left.Dot, l);
        Place(_right.Dot, r);
        _collectL?.Add(l);
        _collectR?.Add(r);
    }

    private static void Place(Ellipse dot, StickSample s)
    {
        Canvas.SetLeft(dot, 68 + s.X * 68);
        Canvas.SetTop(dot, 68 - s.Y * 68);
    }

    private async Task MeasureAsync(bool drift)
    {
        if (_pad < 0) { _stickResult.Text = "Conecte um controle primeiro."; return; }
        _collectL = new();
        _collectR = new();
        _stickResult.Text = drift ? "Medindo... não encoste nos analógicos." : "Gire os dois analógicos encostados na borda, várias voltas...";
        await Task.Delay(drift ? 3000 : 5000);
        var l = _collectL;
        var r = _collectR;
        _collectL = _collectR = null;
        if (drift)
        {
            double dl = StickMath.Drift(l), dr = StickMath.Drift(r);
            string Text(string side, double d) => $"{side}: deriva {d * 100:0.0}%{(StickMath.HasDrift(d) ? " (alta — o jogo vai sentir)" : "")}, zona morta sugerida {StickMath.SuggestedDeadzone(d) * 100:0}%";
            _stickResult.Text = Text("Esquerdo", dl) + "\n" + Text("Direito", dr);
        }
        else
        {
            var (el, cl) = StickMath.Circularity(l);
            var (er, cr) = StickMath.Circularity(r);
            string Text(string side, double error, int coverage) => coverage < 80
                ? $"{side}: gire mais (cobriu {coverage}% do círculo)."
                : $"{side}: erro de circularidade {error:0.0}% {(error > 10 ? "(alto: a diagonal fica desigual)" : "(bom)")}";
            _stickResult.Text = Text("Esquerdo", el, cl) + "\n" + Text("Direito", er, cr);
        }
    }

    private async Task PulseAsync(bool left)
    {
        if (_pad < 0) { _stickResult.Text = "Conecte um controle primeiro."; return; }
        Vibrate(left ? (ushort)40000 : (ushort)0, left ? (ushort)0 : (ushort)40000);
        await Task.Delay(600);
        Vibrate(0, 0);
    }

    private void Vibrate(ushort leftMotor, ushort rightMotor)
    {
        if (_pad < 0) return;
        var v = new XInputVibration { Left = leftMotor, Right = rightMotor };
        try { XInputSetState((uint)_pad, ref v); } catch (DllNotFoundException) { }
    }
}
