using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MBExtreme.Core.Gamepads;

namespace MBExtreme.UI.Pages;

/// <summary>MB Control · Luz, gatilhos e vibração: barra de luz (cores e modos), luzes do jogador, mudo, gatilhos adaptáveis e teste de vibração.</summary>
internal sealed class MbLightPage : MbControlPageBase
{
    private static readonly string[] Palette =
    {
        "#FFC400", "#FF8A00", "#FF3B30", "#FF2D92", "#B14CFF", "#5856FF", "#0A84FF", "#00C7FF",
        "#00E5A0", "#34C759", "#A8E000", "#FFFFFF", "#7A7A80", "#8B0000", "#003A8C", "#004D33",
    };

    private readonly PadLightSettings _s = AppServices.Settings.PadLight;
    private readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly Rectangle _preview = new() { Height = 10, RadiusX = 5, RadiusY = 5, Margin = new Thickness(0, 2, 0, 14) };
    private readonly TextBox _hex = new() { Width = 90, FontSize = 13, Margin = new Thickness(8, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock _ds4Note = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
    private readonly MBExtreme.UI.Controls.ColorPicker _picker = new();
    private DateTime _t0 = DateTime.Now;

    public MbLightPage() : base("Luz, gatilhos e vibração", "Escolha a cor e o efeito da barra de luz, as luzes do jogador, a luz do mudo e a resistência dos gatilhos adaptáveis. Vale pelo cabo USB e pelo Bluetooth.")
    {
        _save.Tick += (_, _) => { _save.Stop(); AppServices.Settings.Save(); };
        _ds4Note.Foreground = PadUi.Res("WarnBrush");
        Body.Children.Add(_ds4Note);

        var enable = new CheckBox { Content = "Usar a luz e os gatilhos do MB Control", IsChecked = _s.Enabled, FontSize = 14, Margin = new Thickness(0, 0, 0, 12) };
        enable.Click += (_, _) => { _s.Enabled = enable.IsChecked == true; Changed(); };
        Body.Children.Add(enable);

        Body.Children.Add(PadUi.Card(LightBar()));
        var row = new UniformGrid { Columns = 2 };
        row.Children.Add(PadUi.Card(Players()));
        row.Children.Add(PadUi.Card(Rumble()));
        Body.Children.Add(row);
        var triggers = new UniformGrid { Columns = 2 };
        triggers.Children.Add(PadUi.Card(Trigger("Gatilho esquerdo (L2)", _s.Left)));
        triggers.Children.Add(PadUi.Card(Trigger("Gatilho direito (R2)", _s.Right)));
        Body.Children.Add(triggers);
        Body.Children.Add(PadUi.Muted("Os jogos com suporte ao PS5 mandam os próprios efeitos de gatilho; aqui você define o padrão para jogos que não usam e para testar o controle. A personalização volta ao padrão quando o controle é desligado sem o MB aberto.", 11.5));
        ShowColor();
    }

    private void Changed()
    {
        AppServices.Pads.OutputChanged();
        _save.Stop();
        _save.Start();
    }

    private static TextBlock Title(string text) => new() { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };

    private static UIElement Labeled(string label, Slider slider, Func<double, string> format)
    {
        var valueText = new TextBlock { Text = format(slider.Value), FontSize = 12, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)) };
        slider.ValueChanged += (_, _) => valueText.Text = format(slider.Value);
        var pill = new Border { Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x24, 0x10)), CornerRadius = new CornerRadius(999), Padding = new Thickness(9, 2, 9, 2), Child = valueText };
        var head = new DockPanel();
        DockPanel.SetDock(pill, Dock.Right);
        head.Children.Add(pill);
        head.Children.Add(new TextBlock { Text = label, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
        slider.Margin = new Thickness(0, 6, 0, 0);
        System.Windows.Automation.AutomationProperties.SetName(slider, label);
        var st = new StackPanel();
        st.Children.Add(head);
        st.Children.Add(slider);
        return new Border { Background = PadUi.Res("Surface2Brush"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 8), Child = st };
    }

    private static Slider NewSlider(double min, double max, double value) =>
        new() { Minimum = min, Maximum = max, Value = value, IsSnapToTickEnabled = true, TickFrequency = 1, Style = (Style)Application.Current.FindResource("MbSlider") };

    private WrapPanel Chips<T>(IEnumerable<(T Value, string Label)> options, Func<T> get, Action<T> set) where T : struct
    {
        var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        var buttons = new List<(ToggleButton, T)>();
        foreach (var (value, label) in options)
        {
            var b = new ToggleButton { Content = label, Style = (Style)FindResource("ChipToggle"), Margin = new Thickness(0, 0, 6, 6) };
            var v = value;
            b.Click += (_, _) =>
            {
                set(v);
                foreach (var (other, ov) in buttons) other.IsChecked = EqualityComparer<T>.Default.Equals(ov, v);
                Changed();
            };
            buttons.Add((b, value));
            wrap.Children.Add(b);
        }
        foreach (var (b, v) in buttons) b.IsChecked = EqualityComparer<T>.Default.Equals(v, get());
        return wrap;
    }

    private UIElement LightBar()
    {
        var st = new StackPanel();
        st.Children.Add(Title("Barra de luz"));
        st.Children.Add(Chips(new[] { (LightMode.Static, "Cor fixa"), (LightMode.Breathing, "Respiração"), (LightMode.Rainbow, "Multicor"), (LightMode.Strobe, "Estroboscópio"), (LightMode.Battery, "Pela bateria") },
            () => _s.Mode, m => _s.Mode = m));
        st.Children.Add(_preview);

        var cols = new Grid();
        cols.ColumnDefinitions.Add(new ColumnDefinition());
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        cols.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel();
        var bright = NewSlider(0, 100, _s.Brightness);
        bright.ValueChanged += (_, _) => { _s.Brightness = (int)bright.Value; Changed(); };
        left.Children.Add(Labeled("Brilho", bright, v => $"{v:0}%"));
        var speed = NewSlider(1, 100, _s.Speed);
        speed.ValueChanged += (_, _) => { _s.Speed = (int)speed.Value; Changed(); };
        left.Children.Add(Labeled("Velocidade do efeito", speed, v => $"{v:0}"));
        cols.Children.Add(left);

        var right = new StackPanel();
        _picker.SetColor(Color.FromRgb(_s.R, _s.G, _s.B));
        _picker.ColorChanged += c => { _s.R = c.R; _s.G = c.G; _s.B = c.B; ShowColor(); Changed(); };
        right.Children.Add(_picker);
        right.Children.Add(new Border { Height = 12 });
        right.Children.Add(new TextBlock { Text = "Cores prontas", FontSize = 12.5, Margin = new Thickness(0, 0, 0, 6) });
        var swatches = new WrapPanel();
        foreach (string hex in Palette)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var b = new Button { Width = 26, Height = 26, Margin = new Thickness(0, 0, 8, 8), Background = new SolidColorBrush(color), ToolTip = hex, Cursor = System.Windows.Input.Cursors.Hand, Template = RoundSwatch() };
            System.Windows.Automation.AutomationProperties.SetName(b, "Cor " + hex);
            b.Click += (_, _) => SetColor(color);
            swatches.Children.Add(b);
        }
        right.Children.Add(swatches);
        var hexRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        hexRow.Children.Add(new TextBlock { Text = "Código da cor (#RRGGBB)", FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
        hexRow.Children.Add(_hex);
        _hex.LostFocus += (_, _) => ApplyHex();
        _hex.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) ApplyHex(); };
        right.Children.Add(hexRow);
        Grid.SetColumn(right, 2);
        cols.Children.Add(right);
        st.Children.Add(cols);

        // prévia animada do efeito, igual ao que vai para o controle
        var preview = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        preview.Tick += (_, _) => ShowColor();
        Loaded += (_, _) => preview.Start();
        Unloaded += (_, _) => preview.Stop();
        return st;
    }

    private static ControlTemplate RoundSwatch()
    {
        var t = new ControlTemplate(typeof(Button));
        var e = new FrameworkElementFactory(typeof(Ellipse));
        e.SetValue(Shape.FillProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        e.SetValue(Shape.StrokeProperty, new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)));
        e.SetValue(Shape.StrokeThicknessProperty, 1.5);
        t.VisualTree = e;
        return t;
    }

    private void ApplyHex()
    {
        try { SetColor((Color)ColorConverter.ConvertFromString(_hex.Text.Trim().StartsWith('#') ? _hex.Text.Trim() : "#" + _hex.Text.Trim())); }
        catch (FormatException) { ShowColor(); }
    }

    private void SetColor(Color c)
    {
        _s.R = c.R; _s.G = c.G; _s.B = c.B;
        _picker.SetColor(c);
        ShowColor();
        Changed();
    }

    private void ShowColor()
    {
        var (r, g, b) = PadOutput.LightColor(_s, (DateTime.Now - _t0).TotalMilliseconds, AppServices.Pads.State.Battery);
        _preview.Fill = new SolidColorBrush(Color.FromRgb(r, g, b));
        if (!_hex.IsKeyboardFocused) _hex.Text = $"#{_s.R:X2}{_s.G:X2}{_s.B:X2}";
    }

    private UIElement Players()
    {
        var st = new StackPanel();
        st.Children.Add(Title("Luzes do jogador e do mudo"));
        st.Children.Add(new TextBlock { Text = "Número do jogador", FontSize = 12.5, Margin = new Thickness(0, 0, 0, 4) });
        st.Children.Add(Chips(new[] { (0, "Apagadas"), (1, "1"), (2, "2"), (3, "3"), (4, "4"), (5, "Todas") }, () => _s.PlayerLeds, v => _s.PlayerLeds = v));
        st.Children.Add(new TextBlock { Text = "Brilho das luzes", FontSize = 12.5, Margin = new Thickness(0, 0, 0, 4) });
        st.Children.Add(Chips(new[] { (0, "Alto"), (1, "Médio"), (2, "Baixo") }, () => _s.PlayerBrightness, v => _s.PlayerBrightness = v));
        st.Children.Add(new TextBlock { Text = "Luz do botão de mudo", FontSize = 12.5, Margin = new Thickness(0, 0, 0, 4) });
        st.Children.Add(Chips(new[] { (MuteLed.Off, "Apagada"), (MuteLed.On, "Acesa"), (MuteLed.Pulse, "Piscando") }, () => _s.Mute, v => _s.Mute = v));
        return st;
    }

    private UIElement Rumble()
    {
        var st = new StackPanel();
        st.Children.Add(Title("Teste de vibração"));
        st.Children.Add(PadUi.Muted("Cada botão vibra por 1 segundo. Se um lado não vibrar, o motor pode estar com defeito.", 12));
        var wrap = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var (label, strong, weak) in new[] { ("Motor forte (esquerdo)", (byte)255, (byte)0), ("Motor leve (direito)", (byte)0, (byte)255), ("Os dois", (byte)200, (byte)200) })
        {
            var b = PadUi.Button(label);
            b.Margin = new Thickness(0, 0, 8, 8);
            b.Click += (_, _) => AppServices.Pads.Rumble(strong, weak, 1000);
            wrap.Children.Add(b);
        }
        st.Children.Add(wrap);
        return st;
    }

    private UIElement Trigger(string title, TriggerEffect e)
    {
        var st = new StackPanel();
        st.Children.Add(Title(title));
        var details = new StackPanel();
        void Build()
        {
            details.Children.Clear();
            if (e.Kind == TriggerKind.Off) { details.Children.Add(PadUi.Muted("Gatilho livre, sem resistência.", 12)); return; }
            var start = NewSlider(e.Kind == TriggerKind.Weapon ? 2 : 0, e.Kind == TriggerKind.Weapon ? 7 : 9, e.Start);
            start.ValueChanged += (_, _) => { e.Start = (int)start.Value; Changed(); };
            details.Children.Add(Labeled("Onde começa (curso do gatilho)", start, v => $"{v * 10 + 10:0}%"));
            if (e.Kind == TriggerKind.Weapon)
            {
                var end = NewSlider(3, 8, Math.Max(e.End, e.Start + 1));
                end.ValueChanged += (_, _) => { e.End = (int)end.Value; Changed(); };
                details.Children.Add(Labeled("Onde \"dispara\"", end, v => $"{v * 10 + 10:0}%"));
            }
            var strength = NewSlider(1, 8, e.Strength);
            strength.ValueChanged += (_, _) => { e.Strength = (int)strength.Value; Changed(); };
            details.Children.Add(Labeled(e.Kind == TriggerKind.Vibration ? "Intensidade" : "Força", strength, v => $"{v:0}/8"));
            if (e.Kind == TriggerKind.Vibration)
            {
                var freq = NewSlider(1, 120, e.Frequency);
                freq.ValueChanged += (_, _) => { e.Frequency = (int)freq.Value; Changed(); };
                details.Children.Add(Labeled("Frequência", freq, v => $"{v:0} Hz"));
            }
        }
        st.Children.Add(Chips(new[] { (TriggerKind.Off, "Livre"), (TriggerKind.Resistance, "Resistência"), (TriggerKind.Weapon, "Arma (trava e dispara)"), (TriggerKind.Vibration, "Vibração") },
            () => e.Kind, k => { e.Kind = k; Build(); }));
        st.Children.Add(details);
        Build();
        return st;
    }

    protected override void Slow()
    {
        var d = AppServices.Pads.Current;
        _ds4Note.Text = d is null ? "Conecte um controle PS4 ou PS5 para ver as mudanças nele."
            : d.Sony == PadModel.DualShock4 ? "PS4 (DualShock 4): barra de luz e vibração funcionam; gatilhos adaptáveis, luzes do jogador e mudo são só do PS5."
            : d.Sony is null ? "Controles Xbox não têm barra de luz nem gatilhos adaptáveis; use um controle PS4 ou PS5." : "";
        _ds4Note.Visibility = _ds4Note.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
