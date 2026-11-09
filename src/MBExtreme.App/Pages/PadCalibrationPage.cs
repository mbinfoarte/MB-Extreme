using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using MBExtreme.Core.Gamepads;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Calibrar controle: corrige drift de DualSense e DualShock 4 gravando a calibração na memória do controle (pelo cabo USB).
/// Modo simples (guiado: diagnóstico, centro, alcance, conferência e gravação) e modo avançado (entrada ao vivo,
/// botões, sensores e cada ferramenta separada). Faz cópia da calibração original e gera o laudo para o cliente.
/// Protocolo baseado no projeto aberto dualshock-tools (MIT).
/// </summary>
public sealed class PadCalibrationPage : ToolPageBase
{
    private const string IconFont = "Segoe MDL2 Assets";
    private static readonly Color Yellow = Color.FromRgb(0xFF, 0xC4, 0x00);
    private static int _mode; // 0 simples, 1 avançado

    // ---- conexão
    private IHidChannel? _hid;
    private SonyPad? _pad;
    private PadInfo? _info;
    private HidPadInfo? _device;
    private PadState _state = PadState.Empty;
    private string _connection = "Procurando controle...";
    private CancellationTokenSource? _loop;
    private readonly object _gate = new();
    private readonly PadRepairLog _log = new();

    // ---- medições
    private readonly CircularityMap _mapL = new(), _mapR = new();
    private readonly DriftMeter _driftL = new(), _driftR = new();
    private bool _collectMap, _collectDrift;
    private bool _busy;
    private StickMeasure? _lb, _rb, _la, _ra;
    private bool _saved;
    private TaskCompletionSource<bool>? _next;

    // ---- interface
    private readonly StickView _left = new() { Width = 230, Height = 230, Caption = "Analógico esquerdo" };
    private readonly StickView _right = new() { Width = 230, Height = 230, Caption = "Analógico direito" };
    private readonly TextBlock _leftValues = new() { FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _rightValues = new() { FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _statusLine = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center };
    private readonly ProgressBar _stepProgress = new() { Height = 6, Maximum = 100, Margin = new Thickness(0, 8, 0, 0) };
    private readonly UniformGrid _strip = new() { Columns = 6 };
    private readonly Grid _content = new();
    private readonly StackPanel _leftPanel = new();
    private readonly StackPanel _centerPanel = new();
    private readonly StackPanel _rightPanel = new();
    private readonly Button _primary = new() { Content = "Começar", MinWidth = 140 };
    private readonly Button _save = new() { Content = "Gravar no controle", MinWidth = 140, Margin = new Thickness(8, 0, 0, 0) };
    private readonly TextBox _client = new() { MinWidth = 200 };
    private readonly TextBox _contact = new() { MinWidth = 160 };
    private readonly TextBox _notes = new() { MinWidth = 260 };
    private readonly WrapPanel _buttonsGrid = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
    private readonly Dictionary<PadButtons, Border> _buttonCells = new();
    private readonly ProgressBar _l2 = new() { Height = 6, Maximum = 1, Width = 120 };
    private readonly ProgressBar _r2 = new() { Height = 6, Maximum = 1, Width = 120 };
    private readonly TextBlock _imu = new() { FontSize = 11.5, FontFamily = new FontFamily("Consolas"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
    private readonly StackPanel _steps = new();
    private readonly TextBlock _instructionTitle = new() { FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
    private readonly TextBlock _instruction = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
    private readonly StackPanel _compare = new() { Margin = new Thickness(0, 12, 0, 0) };
    private readonly DispatcherTimer _ui = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private int _step = -1;

    private static readonly (string Title, string Detail)[] StepTexts =
    {
        ("Conectar o controle", "Ligue o controle no PC pelo cabo USB."),
        ("Diagnóstico (antes)", "Mede o drift com os analógicos soltos e a circularidade girando."),
        ("Centro", "Analógicos soltos enquanto o controle aprende o centro."),
        ("Alcance", "Gire os dois analógicos até a borda, várias voltas."),
        ("Conferir (depois)", "Mede de novo para comparar com o antes."),
        ("Gravar", "Grava a calibração na memória do controle."),
    };

    public PadCalibrationPage() : base("Calibrar controle",
        "Corrige drift e analógicos fora de centro do DualSense e do DualShock 4, gravando a calibração na memória do próprio controle. Use o cabo USB.")
    {
        _primary.Style = (Style)FindResource("PrimaryButton");
        _save.Style = (Style)FindResource("GhostButton");
        _primary.Click += (_, _) => OnPrimary();
        _save.Click += async (_, _) => await SaveAsync();
        _stepProgress.Style = (Style)FindResource("GradientProgress");
        _stepProgress.Height = 6;

        // marcas (só PlayStation calibra por enquanto) e modo
        var top = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var brands = new WrapPanel();
        var brandChips = new List<ToggleButton>();
        foreach (var (name, glyph, index) in new[] { ("PlayStation", "", 0), ("Xbox", "", 1), ("Nintendo", "", 2), ("Outro", "", 3) })
        {
            var chip = new ToggleButton { Style = (Style)FindResource("ChipToggle"), IsChecked = index == 0,
                Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { new TextBlock { Text = glyph, FontFamily = new FontFamily(IconFont), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, FontSize = 13.5 } } } };
            chip.Click += (_, _) =>
            {
                foreach (var c in brandChips) c.IsChecked = ReferenceEquals(c, chip);
                if (index == 0) BuildLayout(); else ShowOtherBrand(name);
            };
            brandChips.Add(chip);
            brands.Children.Add(chip);
        }
        top.Children.Add(brands);
        var modes = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (name, index) in new[] { ("Simples", 0), ("Avançado", 1) })
        {
            var chip = new ToggleButton { Style = (Style)FindResource("ChipToggle"), IsChecked = _mode == index, Content = new TextBlock { Text = "Modo " + name, FontWeight = FontWeights.SemiBold } };
            chip.Click += (_, _) =>
            {
                _mode = index;
                foreach (var c in modes.Children.OfType<ToggleButton>()) c.IsChecked = ReferenceEquals(c, chip);
                BuildLayout();
            };
            modes.Children.Add(chip);
        }
        Grid.SetColumn(modes, 1);
        top.Children.Add(modes);
        Body.Children.Add(top);

        var stripCard = CardOf(_strip);
        stripCard.Padding = new Thickness(14, 8, 14, 8);
        Body.Children.Add(stripCard);

        _content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        _content.ColumnDefinitions.Add(new ColumnDefinition());
        _content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        Body.Children.Add(_content);
        Body.Children.Add(ClientCard());
        Body.Children.Add(Muted("Calibração baseada no projeto aberto dualshock-tools (licença MIT, © 2024 the_al). Gravar na memória do controle é como os técnicos fazem: use o cabo USB e não desconecte durante a gravação.", 11));

        _ui.Tick += (_, _) => RefreshUi();
        Loaded += (_, _) => { BuildLayout(); StartLoop(); _ui.Start(); };
        Unloaded += (_, _) => { _ui.Stop(); StopLoop(); };
    }

    // ================================================================== conexão e leitura

    private void StartLoop()
    {
        if (_loop is not null) return;
        _loop = new CancellationTokenSource();
        var token = _loop.Token;
        new Thread(() => ReadLoop(token)) { IsBackground = true, Name = "Leitura do controle" }.Start();
    }

    private void StopLoop()
    {
        _loop?.Cancel();
        _loop = null;
        Disconnect();
    }

    private void Disconnect()
    {
        lock (_gate)
        {
            _hid?.Dispose();
            _hid = null;
            _pad = null;
            _info = null;
            _device = null;
            _state = PadState.Empty;
        }
    }

    private void ReadLoop(CancellationToken token)
    {
        if (!OperatingSystem.IsWindows()) { _connection = "Disponível só no Windows."; return; }
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_hid is null)
                {
                    var pads = WinHid.FindPads();
                    var usb = pads.FirstOrDefault(p => p.IsUsb);
                    if (usb is null)
                    {
                        _connection = pads.Count > 0 ? "Controle no Bluetooth: ligue pelo cabo USB para calibrar." : "Procurando controle... ligue o DualSense ou o DualShock 4 pelo cabo USB.";
                        Thread.Sleep(1000);
                        continue;
                    }
                    var hid = WinHid.OpenPad(usb);
                    var pad = new SonyPad(hid, usb.Model);
                    lock (_gate) { _hid = hid; _pad = pad; _device = usb; }
                    _connection = $"Conectado (USB) · lendo informações...";
                    var info = pad.ReadInfo();
                    lock (_gate) _info = info;
                    if (!info.LooksClone && info.Model != PadModel.DualShock4 && pad.ReadFinetune() is { } tune && _log.SaveBackup(info.Serial, tune))
                        Dispatcher.BeginInvoke(() => SetStatus("Cópia da calibração original guardada (dá para voltar a ela no modo avançado).", "OkBrush"));
                    _connection = "Conectado (USB)";
                    Dispatcher.BeginInvoke(new Action(() => { FillStrip(); if (_step < 0 || _step == 0) GoStep(0); }));
                    continue;
                }

                var report = _hid.ReadInput(150);
                if (report is null || _device is null) continue;
                if (PadInput.Parse(_device.Model, report) is not { } state) continue;
                lock (_gate)
                {
                    _state = state;
                    if (_collectMap) { _mapL.Add(state.LX, state.LY); _mapR.Add(state.RX, state.RY); }
                    if (_collectDrift) { _driftL.Add(state.LX, state.LY); _driftR.Add(state.RX, state.RY); }
                }
            }
            catch (Exception ex) when (ex is IOException or PadCommandException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
            {
                _connection = "Controle desconectado: " + ex.Message;
                Disconnect();
                Dispatcher.BeginInvoke(new Action(() => { FillStrip(); if (!_busy) GoStep(0); }));
                Thread.Sleep(1000);
            }
        }
    }

    private bool CanCalibrate(out string why)
    {
        why = "";
        if (_pad is null || _info is null) { why = "Conecte o controle pelo cabo USB."; return false; }
        if (_info.LooksClone) { why = "Este controle parece não ser original (" + _info.CloneReason + "). A calibração ficou desligada para não estragá-lo."; return false; }
        if (_info.Model == PadModel.DualSenseEdge) { why = "DualSense Edge: nesta versão só os testes (a gravação dele é diferente)."; return false; }
        if (_info.Memory == NvStatus.PendingReboot) { why = "O controle pediu para reiniciar: desconecte e conecte o cabo de novo."; return false; }
        return true;
    }

    // ================================================================== layout

    private void BuildLayout()
    {
        _content.Children.Clear();
        _leftPanel.Children.Clear();
        _centerPanel.Children.Clear();
        _rightPanel.Children.Clear();
        _content.ColumnDefinitions[0].Width = new GridLength(280);
        _content.ColumnDefinitions[2].Width = new GridLength(300);

        // centro: os dois analógicos
        var sticks = new UniformGrid { Columns = 2 };
        sticks.Children.Add(new StackPanel { Children = { UiTree.Free(_left), UiTree.Free(_leftValues) } });
        sticks.Children.Add(new StackPanel { Children = { UiTree.Free(_right), UiTree.Free(_rightValues) } });
        _centerPanel.Children.Add(Title(_mode == 0 ? "CALIBRAÇÃO" : "ENTRADA AO VIVO"));
        _centerPanel.Children.Add(UiTree.Free(_statusLine));
        _centerPanel.Children.Add(sticks);
        _centerPanel.Children.Add(UiTree.Free(_stepProgress));
        if (_mode == 1)
        {
            BuildButtonsGrid();
            _centerPanel.Children.Add(UiTree.Free(_buttonsGrid));
            var triggers = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
            triggers.Children.Add(new TextBlock { Text = "L2 ", FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center });
            triggers.Children.Add(UiTree.Free(_l2));
            triggers.Children.Add(new TextBlock { Text = "   R2 ", FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center });
            triggers.Children.Add(UiTree.Free(_r2));
            _centerPanel.Children.Add(triggers);
            _centerPanel.Children.Add(UiTree.Free(_imu));
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
        if (_mode == 0) buttons.Children.Add(UiTree.Free(_primary));
        buttons.Children.Add(UiTree.Free(_save));
        _centerPanel.Children.Add(buttons);
        _centerPanel.Children.Add(UiTree.Free(_compare));

        if (_mode == 0)
        {
            _leftPanel.Children.Add(Title("ETAPAS"));
            _leftPanel.Children.Add(UiTree.Free(_steps));
            BuildSteps();
            _rightPanel.Children.Add(Title("INSTRUÇÕES"));
            var box = new StackPanel { Margin = new Thickness(6, 30, 6, 10) };
            box.Children.Add(UiTree.Free(_instructionTitle));
            box.Children.Add(UiTree.Free(_instruction));
            _rightPanel.Children.Add(box);
        }
        else
        {
            _leftPanel.Children.Add(Title("STATUS"));
            FillStatusList(_leftPanel);
            _rightPanel.Children.Add(Title("FERRAMENTAS"));
            BuildTools(_rightPanel);
        }

        AddColumn(_leftPanel, 0);
        AddColumn(_centerPanel, 1);
        AddColumn(_rightPanel, 2);
        FillStrip();
        if (_mode == 0 && _step < 0) GoStep(_pad is null ? 0 : 0);
    }

    private void AddColumn(StackPanel panel, int column)
    {
        var card = CardOf(UiTree.Free(panel));
        card.Margin = new Thickness(column == 0 ? 0 : 5, 0, column == 2 ? 0 : 5, 8);
        card.MinHeight = 480;
        Grid.SetColumn(card, column);
        _content.Children.Add(card);
    }

    private TextBlock Title(string text) => new() { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brush("MutedBrush"), Margin = new Thickness(0, 0, 0, 8) };

    private void ShowOtherBrand(string brand)
    {
        _content.Children.Clear();
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 30, 0, 30) };
        stack.Children.Add(new TextBlock { Text = $"{brand}: calibração em breve", FontSize = 18, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(Muted("A gravação da calibração na memória só existe para controles PlayStation nesta versão (os de Xbox e Nintendo usam protocolos fechados). " +
                                 "O teste de drift, circularidade, botões e vibração desses controles está em Periféricos.", 12.5));
        var go = new Button { Content = "Abrir Periféricos", Style = (Style)FindResource("PrimaryButton"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
        go.Click += (_, _) => MainWindow.Current?.Go("periphtest");
        stack.Children.Add(go);
        var card = CardOf(stack);
        Grid.SetColumnSpan(card, 3);
        _content.Children.Add(card);
    }

    private void FillStrip()
    {
        _strip.Children.Clear();
        PadInfo? info; PadState s; HidPadInfo? device;
        lock (_gate) { info = _info; s = _state; device = _device; }
        void Cell(string glyph, string title, string value, string brush = "TextBrush")
        {
            var cell = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 8, 2) };
            cell.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily(IconFont), FontSize = 14, Foreground = Brush("MutedBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            cell.Children.Add(new StackPanel { Children = { new TextBlock { Text = title, FontSize = 10.5, Foreground = Brush("MutedBrush") }, new TextBlock { Text = value, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brush(brush), TextTrimming = TextTrimming.CharacterEllipsis } } });
            _strip.Children.Add(cell);
        }
        Cell("", "Conexão", device is null ? _connection : $"{PadIds.Name(device.Model)} (USB)", device is null ? "WarnBrush" : "OkBrush");
        Cell("", "Bateria", device is null ? "—" : $"{s.Battery}%" + (s.Charging ? " (carregando)" : s.Cable ? " (no cabo)" : ""));
        Cell("", "Série", info?.Serial ?? "—");
        Cell("", "Placa", info?.Board ?? "—");
        Cell("", "Cor", info?.Color ?? "—");
        Cell("", "Memória", info?.Memory switch
        {
            NvStatus.Temporary => "Temporária (protegida)",
            NvStatus.Permanent => "Permanente (destravada)",
            NvStatus.PendingReboot => "Reinicie o controle",
            null => "—",
            _ => "Desconhecida",
        }, info?.LooksClone == true ? "BadBrush" : "TextBrush");
    }

    private void FillStatusList(Panel host)
    {
        PadInfo? info;
        lock (_gate) info = _info;
        void Line(string name, string? value, string brush = "TextBrush")
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(new TextBlock { Text = name, FontSize = 11.5, Foreground = Brush("MutedBrush") });
            var v = new TextBlock { Text = value ?? "—", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Brush(brush), TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(v, 1);
            g.Children.Add(v);
            host.Children.Add(new Border { Child = g, Background = Brush("Surface2Brush"), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 0, 4) });
        }
        if (info is null) { host.Children.Add(Muted(_connection, 12)); return; }
        Line("Modelo", PadIds.Name(info.Model));
        Line("Série", info.Serial);
        Line("Placa", info.Board);
        Line("Cor", info.Color);
        Line("Firmware", info.Firmware);
        Line("Data do FW", info.BuildDate);
        Line("Bluetooth", info.BluetoothAddress);
        Line("Original?", info.LooksClone ? "Parece clone: " + info.CloneReason : "Sim", info.LooksClone ? "BadBrush" : "OkBrush");
        Line("Memória", info.Memory.ToString());
        var history = _log.For(info.Serial);
        if (history.Count > 0)
        {
            host.Children.Add(new TextBlock { Text = "Atendimentos deste controle", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) });
            foreach (var h in history.Take(5))
                host.Children.Add(Muted($"{h.When:dd/MM/yyyy} · {h.Client ?? "sem nome"} · {(h.Saved ? "gravado" : "não gravado")}", 11));
        }
    }

    private void BuildSteps()
    {
        _steps.Children.Clear();
        for (int i = 0; i < StepTexts.Length; i++)
        {
            bool done = i < _step, current = i == _step;
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var circle = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), BorderThickness = new Thickness(1.5), VerticalAlignment = VerticalAlignment.Top,
                BorderBrush = current ? (Brush)FindResource("AccentGradientBrush") : done ? Brush("OkBrush") : Brush("BorderBrush"),
                Background = current ? (Brush)FindResource("GlassSelectedBrush") : Brushes.Transparent,
                Child = new TextBlock { Text = done ? "✓" : (i + 1).ToString(), FontSize = 11.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = done ? Brush("OkBrush") : Brush("TextBrush") } };
            row.Children.Add(circle);
            var text = new StackPanel { Children =
            {
                new TextBlock { Text = StepTexts[i].Title, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = current ? new SolidColorBrush(Yellow) : Brush("TextBrush") },
                new TextBlock { Text = StepTexts[i].Detail, FontSize = 11, Foreground = Brush("MutedBrush"), TextWrapping = TextWrapping.Wrap },
            } };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            _steps.Children.Add(row);
        }
    }

    private void BuildButtonsGrid()
    {
        _buttonsGrid.Children.Clear();
        _buttonCells.Clear();
        foreach (var (b, label) in new[]
        {
            (PadButtons.Square, "□"), (PadButtons.Cross, "✕"), (PadButtons.Circle, "○"), (PadButtons.Triangle, "△"),
            (PadButtons.L1, "L1"), (PadButtons.R1, "R1"), (PadButtons.L2, "L2"), (PadButtons.R2, "R2"),
            (PadButtons.Up, "↑"), (PadButtons.Down, "↓"), (PadButtons.Left, "←"), (PadButtons.Right, "→"),
            (PadButtons.L3, "L3"), (PadButtons.R3, "R3"), (PadButtons.Create, "Create"), (PadButtons.Options, "Options"),
            (PadButtons.PS, "PS"), (PadButtons.Touchpad, "Touch"), (PadButtons.Mute, "Mudo"),
        })
        {
            var cell = new Border { Width = 50, Height = 34, CornerRadius = new CornerRadius(8), Margin = new Thickness(3), BorderThickness = new Thickness(1), BorderBrush = Brush("BorderBrush"), Background = Brush("Surface2Brush"),
                Child = new TextBlock { Text = label, FontSize = label.Length > 2 ? 10.5 : 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            _buttonCells[b] = cell;
            _buttonsGrid.Children.Add(cell);
        }
    }

    // ================================================================== atualização da tela (30 por segundo)

    private PadButtons _shownButtons = (PadButtons)uint.MaxValue;
    private int _stripTick;

    private void RefreshUi()
    {
        PadState s;
        double[] mapL, mapR;
        double errL, errR, covL, covR;
        lock (_gate)
        {
            s = _state;
            mapL = _mapL.Values.ToArray(); mapR = _mapR.Values.ToArray();
            errL = _mapL.Error; errR = _mapR.Error; covL = _mapL.Coverage; covR = _mapR.Coverage;
        }
        _left.Map = mapL;
        _right.Map = mapR;
        _left.SetPosition(s.LX, s.LY);
        _right.SetPosition(s.RX, s.RY);
        _leftValues.Text = $"X {s.LX:+0.000;-0.000}   Y {s.LY:+0.000;-0.000}" + (errL > 0 ? $"   ·   erro {errL:0.0}%" : "");
        _rightValues.Text = $"X {s.RX:+0.000;-0.000}   Y {s.RY:+0.000;-0.000}" + (errR > 0 ? $"   ·   erro {errR:0.0}%" : "");
        if (_collectMap && _step is 1 or 3 or 4 || _mode == 1 && _collectMap) _stepProgress.Value = Math.Min(covL, covR) * 100;
        if (_mode == 1)
        {
            _l2.Value = s.L2;
            _r2.Value = s.R2;
            _imu.Text = $"Giroscópio  {s.GyroPitch,7:0.0} {s.GyroYaw,7:0.0} {s.GyroRoll,7:0.0} °/s\nAcelerômetro {s.AccelX,6:0.00} {s.AccelY,6:0.00} {s.AccelZ,6:0.00} g";
            if (s.Buttons != _shownButtons)
            {
                _shownButtons = s.Buttons;
                foreach (var (b, cell) in _buttonCells)
                {
                    bool on = s.Buttons.HasFlag(b);
                    cell.Background = on ? (Brush)FindResource("AccentGradientBrush") : Brush("Surface2Brush");
                    ((TextBlock)cell.Child).Foreground = on ? Brushes.Black : Brush("TextBrush");
                }
            }
        }
        if (++_stripTick % 30 == 0) FillStrip();
        if (_pad is null && !_busy) _statusLine.Text = _connection;
    }

    // ================================================================== modo simples (guiado)

    private void GoStep(int step)
    {
        _step = step;
        if (_mode != 0) return;
        BuildSteps();
        _instructionTitle.Text = StepTexts[Math.Clamp(step, 0, StepTexts.Length - 1)].Title;
        _save.IsEnabled = step == 5 && !_busy;
        _primary.IsEnabled = !_busy;
        switch (step)
        {
            case 0:
                _instruction.Text = _pad is null ? "Ligue o controle no PC pelo cabo USB (não use o Bluetooth). Feche o Steam e o DS4Windows se eles estiverem usando o controle." : "Controle conectado. Clique em Começar para o diagnóstico.";
                _primary.Content = "Começar";
                _primary.IsEnabled = _pad is not null && !_busy;
                _statusLine.Text = _pad is null ? _connection : "Controle conectado pelo cabo. Bateria " + _state.Battery + "%";
                break;
            case 5:
                _instruction.Text = "Se ficou bom, clique em Gravar no controle. Se não, reinicie o controle para desfazer (o que não foi gravado volta ao que era).";
                _primary.Content = "Refazer";
                break;
            default:
                _primary.Content = "Próximo";
                break;
        }
    }

    private void OnPrimary()
    {
        if (_next is { Task.IsCompleted: false }) { _next.TrySetResult(true); return; }
        if (_busy) return;
        if (_step == 5) { GoStep(0); _ = RunGuidedAsync(); return; }
        _ = RunGuidedAsync();
    }

    private async Task RunGuidedAsync()
    {
        if (!CanCalibrate(out string why)) { SetStatus(why, "WarnBrush"); _statusLine.Text = why; return; }
        _busy = true;
        _saved = false;
        _compare.Children.Clear();
        try
        {
            // 2. diagnóstico
            GoStep(1);
            (_lb, _rb) = await MeasureAsync("Solte os dois analógicos e não encoste no controle.", "Agora gire os dois analógicos até a borda, devagar, umas 3 voltas. Clique em Próximo quando o desenho fechar.");

            // 3. centro
            GoStep(2);
            _instruction.Text = "Solte os dois analógicos e deixe o controle parado na mesa.";
            await CountdownAsync(2, "Calibrando o centro em");
            _statusLine.Text = "Calibrando o centro...";
            var pad = _pad ?? throw new IOException("O controle desconectou.");
            await Task.Run(() => pad.CalibrateCenter(p => Dispatcher.BeginInvoke(() => _stepProgress.Value = p * 100)));

            // 4. alcance
            GoStep(3);
            _instruction.Text = "Gire os dois analógicos até a borda, em círculos completos, devagar, umas 3 voltas cada. Clique em Próximo quando os dois desenhos fecharem.";
            await Task.Run(pad.RangeBegin);
            lock (_gate) { _mapL.Clear(); _mapR.Clear(); _collectMap = true; }
            _statusLine.Text = "Calibrando o alcance: gire os analógicos";
            await WaitCoverageAsync(0.95);
            lock (_gate) _collectMap = false;
            await Task.Run(pad.RangeEnd);

            // 5. conferência
            GoStep(4);
            (_la, _ra) = await MeasureAsync("Solte os analógicos de novo para medir o drift depois da calibração.", "Gire os dois analógicos até a borda de novo, para conferir a circularidade.");
            ShowComparison();

            // 6. gravar
            GoStep(5);
            _statusLine.Text = "Calibração feita (ainda não gravada). Confira os números e grave.";
        }
        catch (Exception ex) when (ex is IOException or PadCommandException or InvalidOperationException or TaskCanceledException)
        {
            _statusLine.Text = "Não deu certo: " + ex.Message;
            SetStatus("A calibração parou: " + ex.Message + " Reinicie o controle (desconecte e conecte) para voltar ao que era.", "BadBrush");
            GoStep(0);
        }
        finally
        {
            lock (_gate) { _collectMap = false; _collectDrift = false; }
            _busy = false;
            _primary.IsEnabled = true;
            _save.IsEnabled = _step == 5 || _mode == 1;
        }
    }

    private async Task CountdownAsync(int seconds, string label)
    {
        for (int i = seconds; i > 0; i--)
        {
            _statusLine.Text = $"{label} {i}...";
            await Task.Delay(1000);
        }
    }

    /// <summary>Drift (3 s solto) e circularidade (girando até cobrir as direções ou a pessoa clicar em Próximo).</summary>
    private async Task<(StickMeasure Left, StickMeasure Right)> MeasureAsync(string releaseText, string rotateText)
    {
        _instruction.Text = releaseText;
        await CountdownAsync(2, "Medindo o drift em");
        lock (_gate) { _driftL.Clear(); _driftR.Clear(); _collectDrift = true; }
        _statusLine.Text = "Medindo o drift: não encoste nos analógicos";
        for (int i = 0; i < 30; i++) { _stepProgress.Value = i * 100 / 30.0; await Task.Delay(100); }
        lock (_gate) _collectDrift = false;
        _instruction.Text = rotateText;
        lock (_gate) { _mapL.Clear(); _mapR.Clear(); _collectMap = true; }
        _statusLine.Text = "Gire os dois analógicos até a borda";
        await WaitCoverageAsync(0.9);
        lock (_gate)
        {
            _collectMap = false;
            return (new StickMeasure(_driftL.Peak, _driftL.Offset, _mapL.Error), new StickMeasure(_driftR.Peak, _driftR.Offset, _mapR.Error));
        }
    }

    /// <summary>Espera os dois analógicos cobrirem as direções (ou o clique em Próximo).</summary>
    private async Task WaitCoverageAsync(double target)
    {
        _next = new TaskCompletionSource<bool>();
        _primary.Content = "Próximo";
        _primary.IsEnabled = true;
        var watch = Stopwatch.StartNew();
        double full = -1;
        while (!_next.Task.IsCompleted)
        {
            double cov;
            lock (_gate) cov = Math.Min(_mapL.Coverage, _mapR.Coverage);
            if (cov >= target) { if (full < 0) full = watch.Elapsed.TotalSeconds; else if (watch.Elapsed.TotalSeconds - full > 1.5) break; }
            if (_pad is null) throw new IOException("O controle desconectou.");
            await Task.Delay(100);
        }
        _next = null;
    }

    private void ShowComparison()
    {
        _compare.Children.Clear();
        if (_lb is null || _la is null || _rb is null || _ra is null) return;
        var grid = new Grid();
        for (int i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        void Cell(int row, int col, string text, string brush = "TextBrush", bool bold = false)
        {
            while (grid.RowDefinitions.Count <= row) grid.RowDefinitions.Add(new RowDefinition());
            var t = new TextBlock { Text = text, FontSize = 12, Foreground = Brush(brush), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(0, 2, 8, 2) };
            Grid.SetRow(t, row); Grid.SetColumn(t, col);
            grid.Children.Add(t);
        }
        Cell(0, 0, "", bold: true); Cell(0, 1, "Antes", bold: true); Cell(0, 2, "Depois", bold: true);
        string D(StickMeasure m) => $"drift {m.DriftPeak * 100:0.0}% · círculo {m.CircularityError:0.0}%";
        Cell(1, 0, "Esquerdo"); Cell(1, 1, D(_lb), _lb.DriftPeak > 0.025 ? "WarnBrush" : "MutedBrush"); Cell(1, 2, D(_la), _la.DriftPeak > 0.025 ? "WarnBrush" : "OkBrush");
        Cell(2, 0, "Direito"); Cell(2, 1, D(_rb), _rb.DriftPeak > 0.025 ? "WarnBrush" : "MutedBrush"); Cell(2, 2, D(_ra), _ra.DriftPeak > 0.025 ? "WarnBrush" : "OkBrush");
        var card = new Border { Child = grid, Background = Brush("Surface2Brush"), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8, 12, 8) };
        _compare.Children.Add(card);
        if (_la.DriftPeak > 0.07 || _ra.DriftPeak > 0.07)
            _compare.Children.Add(Muted("O drift continuou alto depois da calibração: o potenciômetro (módulo do analógico) pode estar gasto e precisar de troca.", 11.5));
    }

    // ================================================================== gravar, desfazer e laudo

    private async Task SaveAsync()
    {
        if (_pad is null || _info is null) return;
        if (!CanCalibrate(out string why)) { SetStatus(why, "WarnBrush"); return; }
        if (MessageBox.Show("Gravar a calibração na memória do controle?\n\nNão desconecte o cabo durante a gravação. Uma cópia da calibração original já está guardada neste PC.",
                "Gravar no controle", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _busy = true;
        try
        {
            var pad = _pad;
            await Task.Run(pad.SaveToController);
            await Task.Delay(300);
            var nv = await Task.Run(pad.QueryNv);
            _saved = true;
            _statusLine.Text = "Gravado na memória do controle.";
            SetStatus("Calibração gravada no controle. Ela continua mesmo desligando ou usando no console.", "OkBrush");
            RecordRepair();
            lock (_gate) if (_info is not null) _info = _info with { Memory = nv };
            FillStrip();
        }
        catch (Exception ex) when (ex is IOException or PadCommandException)
        {
            SetStatus("Não foi possível gravar: " + ex.Message, "BadBrush");
        }
        finally
        {
            _busy = false;
        }
    }

    private void RecordRepair()
    {
        if (_info is null) return;
        _log.Add(CurrentRepair());
    }

    private PadRepair CurrentRepair() => new(DateTime.Now, PadIds.Name(_info?.Model ?? PadModel.DualSense), _info?.Serial, _info?.Board, _info?.Color,
        string.IsNullOrWhiteSpace(_client.Text) ? null : _client.Text.Trim(), string.IsNullOrWhiteSpace(_contact.Text) ? null : _contact.Text.Trim(),
        _lb, _rb, _la, _ra, _saved, string.IsNullOrWhiteSpace(_notes.Text) ? null : _notes.Text.Trim());

    private Border ClientCard()
    {
        var grid = new WrapPanel();
        void Field(string label, TextBox box)
        {
            grid.Children.Add(new StackPanel { Margin = new Thickness(0, 0, 12, 6), Children = { new TextBlock { Text = label, FontSize = 11, Foreground = Brush("MutedBrush") }, box } });
        }
        Field("Cliente", _client);
        Field("Contato (WhatsApp)", _contact);
        Field("Observações", _notes);
        var report = new Button { Content = "Gerar laudo", Style = (Style)FindResource("GhostButton"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6) };
        report.Click += (_, _) =>
        {
            try
            {
                string file = PadRepairLog.WriteReport(CurrentRepair());
                Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
                SetStatus("Laudo salvo em " + file, "OkBrush");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                SetStatus("Não foi possível gerar o laudo: " + ex.Message, "BadBrush");
            }
        };
        grid.Children.Add(report);
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "ATENDIMENTO", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brush("MutedBrush"), Margin = new Thickness(0, 0, 0, 6) });
        stack.Children.Add(grid);
        stack.Children.Add(Muted("O laudo mostra o drift e a circularidade antes e depois, e se a calibração foi gravada. Fica em Documentos\\MB Extreme\\Controles.", 11));
        return CardOf(stack);
    }

    // ================================================================== modo avançado: ferramentas separadas

    private void BuildTools(Panel host)
    {
        Border Tool(string title, string detail, params Button[] buttons)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = title, FontSize = 12.5, FontWeight = FontWeights.SemiBold });
            stack.Children.Add(Muted(detail, 11));
            var row = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            foreach (var b in buttons) { b.Margin = new Thickness(0, 0, 6, 0); row.Children.Add(b); }
            stack.Children.Add(row);
            var card = new Border { Child = stack, Background = Brush("Surface2Brush"), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8, 12, 10), Margin = new Thickness(0, 0, 0, 8) };
            host.Children.Add(card);
            return card;
        }
        Button B(string text, Func<Task> action, bool primary = false)
        {
            var b = new Button { Content = text, Style = (Style)FindResource(primary ? "PrimaryButton" : "GhostButton") };
            b.Click += async (_, _) =>
            {
                if (_busy) return;
                _busy = true;
                try { await action(); }
                catch (Exception ex) when (ex is IOException or PadCommandException or InvalidOperationException)
                {
                    SetStatus("Erro: " + ex.Message, "BadBrush");
                }
                finally { _busy = false; }
            };
            return b;
        }

        Tool("Centro", "Solte os dois analógicos antes de começar.", B("Calibrar centro", async () =>
        {
            if (!CanCalibrate(out var why)) { SetStatus(why, "WarnBrush"); return; }
            _statusLine.Text = "Solte os analógicos...";
            await Task.Delay(1500);
            var pad = _pad!;
            await Task.Run(() => pad.CalibrateCenter(p => Dispatcher.BeginInvoke(() => _stepProgress.Value = p * 100)));
            _statusLine.Text = "Centro calibrado (ainda não gravado).";
        }, true));

        var rangeButton = new Button { Content = "Começar alcance", Style = (Style)FindResource("GhostButton") };
        bool inRange = false;
        rangeButton.Click += async (_, _) =>
        {
            try
            {
                if (!CanCalibrate(out var why)) { SetStatus(why, "WarnBrush"); return; }
                var pad = _pad!;
                if (!inRange)
                {
                    await Task.Run(pad.RangeBegin);
                    lock (_gate) { _mapL.Clear(); _mapR.Clear(); _collectMap = true; }
                    inRange = true;
                    rangeButton.Content = "Concluir alcance";
                    _statusLine.Text = "Gire os dois analógicos até a borda, várias voltas, e clique em Concluir.";
                }
                else
                {
                    lock (_gate) _collectMap = false;
                    await Task.Run(pad.RangeEnd);
                    inRange = false;
                    rangeButton.Content = "Começar alcance";
                    _statusLine.Text = "Alcance calibrado (ainda não gravado).";
                }
            }
            catch (Exception ex) when (ex is IOException or PadCommandException)
            {
                inRange = false;
                rangeButton.Content = "Começar alcance";
                SetStatus("Erro no alcance: " + ex.Message, "BadBrush");
            }
        };
        Tool("Alcance", "Gire até a borda em círculos completos; a borda colorida mostra onde já passou.", rangeButton);

        Tool("Drift (5 segundos)", "Mede quanto os analógicos saem do centro estando soltos.", B("Medir drift", async () =>
        {
            lock (_gate) { _driftL.Clear(); _driftR.Clear(); _collectDrift = true; }
            _statusLine.Text = "Medindo: não encoste nos analógicos";
            for (int i = 0; i < 50; i++) { _stepProgress.Value = i * 2; await Task.Delay(100); }
            lock (_gate)
            {
                _collectDrift = false;
                _statusLine.Text = $"Esquerdo: {_driftL.Peak * 100:0.0}% ({DriftMeter.Verdict(_driftL.Peak)})   ·   Direito: {_driftR.Peak * 100:0.0}% ({DriftMeter.Verdict(_driftR.Peak)})";
            }
        }));

        var circ = new Button { Content = "Ligar circularidade", Style = (Style)FindResource("GhostButton") };
        circ.Click += (_, _) =>
        {
            lock (_gate)
            {
                _collectMap = !_collectMap;
                if (_collectMap) { _mapL.Clear(); _mapR.Clear(); }
            }
            circ.Content = _collectMap ? "Parar circularidade" : "Ligar circularidade";
        };
        Tool("Circularidade", "Gire os analógicos até a borda: o erro médio aparece embaixo de cada um.", circ);

        Tool("Gravar ou desfazer", "Gravar deixa a calibração permanente. Reiniciar desfaz o que ainda não foi gravado.",
            B("Gravar", SaveAsync, true),
            B("Reiniciar controle", async () =>
            {
                var pad = _pad;
                if (pad is null) return;
                await Task.Run(pad.Reboot);
                SetStatus("Controle reiniciado: o que não foi gravado voltou ao que era.", "OkBrush");
            }));

        Tool("Calibração original", "Volta à cópia guardada antes da primeira mudança neste PC (só DualSense).", B("Restaurar cópia", async () =>
        {
            if (_pad is null || _info is null || _info.Model != PadModel.DualSense) { SetStatus("Disponível só para o DualSense.", "WarnBrush"); return; }
            var backup = _log.LoadBackup(_info.Serial);
            if (backup is null) { SetStatus("Não há cópia guardada para este controle.", "WarnBrush"); return; }
            if (MessageBox.Show("Voltar o controle à calibração guardada e gravar?", "Restaurar cópia", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            var pad = _pad;
            await Task.Run(() => { pad.WriteFinetune(backup); pad.SaveToController(); });
            SetStatus("Calibração original restaurada e gravada.", "OkBrush");
        }));
    }
}
