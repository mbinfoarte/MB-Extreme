using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MBExtreme.Core.Gamepads;

namespace MBExtreme.UI.Pages;

/// <summary>MB Control · Teclado e mouse → controle: liga a conversão, ajusta a mira do mouse e troca as teclas de cada botão.</summary>
internal sealed class MbKbmPage : MbControlPageBase
{
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);

    private readonly KbmSettings _s = AppServices.Settings.Kbm;
    private readonly Button _toggle = PadUi.Button("Ligar", primary: true);
    private readonly TextBlock _status = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Canvas _preview = new() { Width = 120, Height = 120 };
    private readonly Ellipse _dot = new() { Width = 12, Height = 12, Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)) };
    private readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly UniformGrid _keys = new() { Columns = 3 };
    private readonly List<Action> _refresh = new();
    private readonly TextBlock _presetState = new() { FontSize = 12, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

    public MbKbmPage() : base("Teclado e mouse → controle", "Jogue com mouse e teclado em jogos que só aceitam controle. O MB cria um controle virtual: as teclas viram botões e o mouse vira o analógico direito (a mira).")
    {
        _save.Tick += (_, _) => { _save.Stop(); AppServices.Settings.Save(); };

        var warn = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x1F, 0x0E)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x5A, 0x3A, 0x14)), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 12),
            Child = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD1, 0xA1)),
                Text = "Em jogos online com mira assistida (Call of Duty, Fortnite e outros), usar mouse fingindo ser controle é tratado como trapaça e pode banir a conta. Use em jogos offline ou sem mira assistida.",
            },
        };
        Body.Children.Add(warn);

        // ligar, saída e prévia
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();
        left.Children.Add(Head("CONTROLE VIRTUAL"));
        left.Children.Add(Chips(new[] { (VirtualTarget.Xbox360, "Xbox 360"), (VirtualTarget.DualShock4, "PS4") }, () => _s.Target, t => { _s.Target = t; if (AppServices.Kbm.Running) AppServices.Kbm.Start(); }));
        var row = new WrapPanel();
        _toggle.Click += (_, _) => { if (AppServices.Kbm.Running) AppServices.Kbm.Stop(); else AppServices.Kbm.Start(); Show(); };
        row.Children.Add(_toggle);
        row.Children.Add(new TextBlock { Text = $"Atalho para pausar e voltar: {_s.ToggleKey}", FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), Foreground = PadUi.Res("MutedBrush") });
        left.Children.Add(row);
        left.Children.Add(_status);
        left.Children.Add(PadUi.Muted("Precisa do driver do controle virtual (ViGEmBus): instale em MB Control › Emulação de controle, passo 1. Em jogos que travam o mouse no centro, tudo funciona igual.", 11.5));
        top.Children.Add(left);
        var prev = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
        prev.Children.Add(new TextBlock { Text = "Mira ao vivo", FontSize = 12, Foreground = PadUi.Res("MutedBrush"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) });
        _preview.Children.Add(new Ellipse { Width = 120, Height = 120, Stroke = PadUi.Res("BorderBrush"), StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0C)) });
        _preview.Children.Add(_dot);
        prev.Children.Add(_preview);
        Grid.SetColumn(prev, 1);
        top.Children.Add(prev);
        Body.Children.Add(PadUi.Card(top));

        // perfis prontos por jogo
        var presets = new StackPanel();
        presets.Children.Add(Head("PERFIL PRONTO DO JOGO"));
        var cod = KbmPresets.CallOfDuty;
        var presetRow = new WrapPanel();
        var apply = PadUi.Button(cod.Name, primary: true);
        apply.Click += (_, _) => ApplyPreset(cod);
        presetRow.Children.Add(apply);
        presetRow.Children.Add(_presetState);
        presets.Children.Add(presetRow);
        presets.Children.Add(PadUi.Muted(cod.Description + " Entra sozinho quando o jogo abre (com a troca automática de perfil ligada).", 11.5));
        presets.Children.Add(new TextBlock { Text = "Ajuste também dentro do jogo:", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) });
        foreach (var tip in cod.InGameTips) presets.Children.Add(PadUi.Muted("• " + tip, 11.5));
        presets.Children.Add(PadUi.Muted("Só para COD com campanha offline (COD 4, World at War, MW2 e MW3 antigos, Black Ops 1 a 3, Ghosts, Advanced Warfare, MW Remastered, Infinite Warfare e WWII). Nos novos (MW 2019 em diante, Black Ops 6 e 7) e no Battlefield 6 o anti-cheat roda até na campanha: o MB desliga o controle virtual sozinho quando eles abrem. Para o mouse nesses jogos, use MB Control › Mira do mouse.", 11.5));
        Body.Children.Add(PadUi.Card(presets));

        // ajustes da mira
        var aim = new StackPanel();
        aim.Children.Add(Head("MOUSE → ANALÓGICO DIREITO (MIRA)"));
        var sliders = new WrapPanel();
        sliders.Children.Add(Live(PadUi.SliderRow("Sensibilidade", 1, 100, _s.Sensitivity, v => $"{v:0}%", v => { _s.Sensitivity = (int)v; Save(); }, 300), () => _s.Sensitivity));
        sliders.Children.Add(Live(PadUi.SliderRow("Aceleração", 0, 100, _s.Acceleration, v => $"{v:0}%", v => { _s.Acceleration = (int)v; Save(); }, 300), () => _s.Acceleration));
        sliders.Children.Add(Live(PadUi.SliderRow("Anti-zona morta do jogo", 0, 40, _s.AntiDeadzone, v => $"{v:0}%", v => { _s.AntiDeadzone = (int)v; Save(); }, 300), () => _s.AntiDeadzone));
        sliders.Children.Add(Live(PadUi.SliderRow("Suavização", 0, 90, _s.Smoothing, v => $"{v:0}%", v => { _s.Smoothing = (int)v; Save(); }, 300), () => _s.Smoothing));
        aim.Children.Add(sliders);
        aim.Children.Add(new TextBlock { Text = "Curva", FontSize = 12.5, Margin = new Thickness(0, 4, 0, 6) });
        aim.Children.Add(Chips(new[] { (StickCurve.Linear, "Linear"), (StickCurve.Suave, "Suave (mira fina)"), (StickCurve.Agressiva, "Agressiva (giro rápido)") }, () => _s.Curve, c => _s.Curve = c));
        aim.Children.Add(PadUi.Muted("Dica: se a mira \"anda sozinha\" devagar, diminua a anti-zona morta. Se demora para começar a mexer, aumente.", 11.5));
        Body.Children.Add(PadUi.Card(aim));

        // teclas
        var keys = new StackPanel();
        keys.Children.Add(Head("TECLAS → BOTÕES"));
        keys.Children.Add(PadUi.Muted("Clique em \"Trocar\" e aperte a tecla ou o botão do mouse que quer usar.", 12));
        keys.Children.Add(new Border { Height = 8 });
        keys.Children.Add(_keys);
        var reset = PadUi.Button("Voltar às teclas padrão");
        reset.HorizontalAlignment = HorizontalAlignment.Left;
        reset.Margin = new Thickness(0, 6, 0, 0);
        reset.Click += (_, _) => { _s.Bindings = KbmMapper.DefaultBindings(); Save(); BuildKeys(); };
        keys.Children.Add(reset);
        Body.Children.Add(PadUi.Card(keys));
        BuildKeys();

        AppServices.Kbm.Changed += () => Dispatcher.BeginInvoke(new Action(() => { if (!IsLoaded) return; foreach (var r in _refresh) r(); BuildKeys(); Show(); ShowPreset(); }));
        var live = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        live.Tick += (_, _) =>
        {
            var st = AppServices.Kbm.Last;
            Canvas.SetLeft(_dot, 60 + st.RX * 52 - 6);
            Canvas.SetTop(_dot, 60 + st.RY * 52 - 6);
        };
        Loaded += (_, _) => { live.Start(); foreach (var r in _refresh) r(); BuildKeys(); Show(); ShowPreset(); };
        Unloaded += (_, _) => live.Stop();
    }

    private void Save() { _save.Stop(); _save.Start(); ShowPreset(); }

    private Border Live(Border row, Func<int> get)
    {
        var slider = FindSlider(row);
        if (slider is not null) _refresh.Add(() => slider.Value = get());
        return row;
    }

    private static Slider? FindSlider(DependencyObject root)
    {
        if (root is Slider s) return s;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            if (FindSlider(child) is { } found) return found;
        return null;
    }

    private void ApplyPreset(KbmPreset preset)
    {
        var oldTarget = _s.Target;
        preset.ApplyTo(_s);
        foreach (var r in _refresh) r();
        BuildKeys();
        AppServices.Settings.Save();
        if (AppServices.Kbm.Running && oldTarget != _s.Target) AppServices.Kbm.Start();
        ShowPreset();
        Show();
    }

    private void ShowPreset()
    {
        bool on = KbmPresets.CallOfDuty.Matches(_s);
        _presetState.Text = on ? "✓ em uso" : "clique para aplicar (troca a mira e as teclas)";
        _presetState.Foreground = PadUi.Res(on ? "OkBrush" : "MutedBrush");
    }

    private void Show()
    {
        bool on = AppServices.Kbm.Running;
        _toggle.Content = on ? "Desligar" : "Ligar";
        _status.Text = AppServices.Kbm.Status;
        _status.Foreground = PadUi.Res(on && !AppServices.Kbm.Paused ? "OkBrush" : on ? "WarnBrush" : AppServices.Kbm.Status.StartsWith("Desligado") ? "MutedBrush" : "BadBrush");
    }

    private static TextBlock Head(string text) => new() { Text = text, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)), Margin = new Thickness(0, 0, 0, 8) };

    private WrapPanel Chips<T>(IEnumerable<(T Value, string Label)> options, Func<T> get, Action<T> set) where T : struct
    {
        var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var list = new List<(ToggleButton B, T V)>();
        foreach (var (value, label) in options)
        {
            var b = new ToggleButton { Content = label, Style = (Style)FindResource("ChipToggle"), Margin = new Thickness(0, 0, 6, 6) };
            var v = value;
            b.Click += (_, _) => { set(v); foreach (var (o, ov) in list) o.IsChecked = EqualityComparer<T>.Default.Equals(ov, v); Save(); };
            list.Add((b, value));
            wrap.Children.Add(b);
        }
        void Sync() { foreach (var (b, v) in list) b.IsChecked = EqualityComparer<T>.Default.Equals(v, get()); }
        Sync();
        _refresh.Add(Sync);
        return wrap;
    }

    private void BuildKeys()
    {
        _keys.Children.Clear();
        foreach (var (id, name) in KbmMapper.Actions)
        {
            string key = _s.Bindings.FirstOrDefault(b => b.Value == id).Key ?? "";
            var keyText = new TextBlock { Text = key.Length > 0 ? KbmMapper.KeyLabel(key) : "—", FontSize = 12.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)), VerticalAlignment = VerticalAlignment.Center };
            var cap = new Border { Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x2B)), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3, 8, 3), MinWidth = 44, Child = keyText, Margin = new Thickness(0, 0, 8, 0) };
            var change = PadUi.Button("Trocar");
            change.Padding = new Thickness(10, 3, 10, 3);
            string action = id;
            change.Click += async (_, _) =>
            {
                keyText.Text = "aperte...";
                string? pressed = await CaptureKeyAsync();
                if (pressed is null) { keyText.Text = key.Length > 0 ? KbmMapper.KeyLabel(key) : "—"; return; }
                foreach (var k in _s.Bindings.Where(b => b.Value == action || b.Key.Equals(pressed, StringComparison.OrdinalIgnoreCase)).Select(b => b.Key).ToList()) _s.Bindings.Remove(k);
                _s.Bindings[pressed] = action;
                Save();
                BuildKeys();
            };
            var line = new DockPanel();
            DockPanel.SetDock(change, Dock.Right);
            line.Children.Add(change);
            line.Children.Add(cap);
            line.Children.Add(new TextBlock { Text = name, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            _keys.Children.Add(new Border { Background = PadUi.Res("Surface2Brush"), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 7, 8, 7), Margin = new Thickness(0, 0, 8, 8), Child = line });
        }
    }

    /// <summary>Espera a próxima tecla ou botão do mouse (até 6 s). O clique que abriu a captura é ignorado.</summary>
    private static async Task<string?> CaptureKeyAsync()
    {
        await Task.Delay(250);
        while ((GetAsyncKeyState(0x01) & 0x8000) != 0) await Task.Delay(15);
        var until = DateTime.Now.AddSeconds(6);
        while (DateTime.Now < until)
        {
            foreach (var (name, vk) in KbmMapper.Keys)
                if ((GetAsyncKeyState(vk) & 0x8000) != 0) return name;
            await Task.Delay(15);
        }
        return null;
    }
}
