using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MBExtreme.Core.Gamepads;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>
/// MB Control · Ajustes finos: zona morta interna e externa, curva personalizada, força vertical, suavização e anti-retorno por analógico;
/// zona morta, curso e gatilho sensível por gatilho; zona morta, filtro e inversão do giroscópio. Tudo com prévia ao vivo
/// (o ponto cinza é o controle de verdade, o amarelo é o que o jogo recebe). Vale na emulação (controle virtual).
/// </summary>
internal sealed class MbTunePage : MbControlPageBase
{
    private readonly EmulationSettings _e = AppServices.Settings.Emulation;
    private readonly PadFilter _filter = new(), _gyroFilter = new();
    private readonly StickTunePreview _stickPreview = new() { Margin = new Thickness(0, 0, 0, 10), HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TriggerTunePreview _l2 = new(), _r2 = new();
    private readonly TextBlock _l2Text = Small(), _r2Text = Small(), _gyroText = Small();
    private readonly StackPanel _stickBody = new();
    private readonly System.Windows.Threading.DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private bool _right;
    private long _last;

    public MbTunePage() : base("Ajustes finos", "Deixe cada analógico, gatilho e o giroscópio do jeito da sua mão. O ponto cinza é o seu controle; o amarelo é o que o jogo recebe. Vale com a emulação ligada (controle virtual).")
    {
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); AppServices.Settings.Save(); };
        _e.L2 ??= new TriggerTuning();
        _e.R2 ??= new TriggerTuning();

        Body.Children.Add(PadUi.Card(StickCard()));
        Body.Children.Add(PadUi.Card(TriggerCard()));
        Body.Children.Add(PadUi.Card(GyroCard()));

        var foot = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        var emu = PadUi.Button("Abrir a emulação", primary: true);
        emu.Click += (_, _) => MainWindow.Current?.Go("mbc-emu");
        foot.Children.Add(emu);
        var reset = PadUi.Button("Voltar tudo ao padrão");
        reset.Click += (_, _) =>
        {
            if (MessageBox.Show("Voltar os ajustes finos dos analógicos, gatilhos e giroscópio ao padrão? (botões remapeados e o tipo de controle virtual continuam)",
                "Ajustes finos", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var fresh = new EmulationSettings();
            _e.Left = fresh.Left; _e.Right = fresh.Right; _e.L2 = fresh.L2; _e.R2 = fresh.R2; _e.TriggerDeadzone = fresh.TriggerDeadzone;
            _e.GyroDeadzone = fresh.GyroDeadzone; _e.GyroSmoothing = fresh.GyroSmoothing; _e.GyroInvertX = false; _e.GyroInvertY = false;
            _e.GyroVertical = fresh.GyroVertical; _e.GyroSensitivity = fresh.GyroSensitivity;
            AppServices.Settings.Save();
            MainWindow.Current?.Reopen("mbc-tune", "Ajustes finos voltaram ao padrão.");
        };
        foot.Children.Add(reset);
        Body.Children.Add(foot);
        Body.Children.Add(PadUi.Muted(AppServices.Emulation.Running
            ? "A emulação está ligada: as mudanças já valem no jogo."
            : "A emulação está desligada: as mudanças ficam salvas e passam a valer quando você ligar o controle virtual.", 11.5));
    }

    private static TextBlock Small() => new() { FontSize = 11.5, Foreground = PadUi.Res("MutedBrush"), Margin = new Thickness(0, 4, 0, 0) };
    private static TextBlock Head(string text) => new() { Text = text, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)), Margin = new Thickness(0, 0, 0, 8) };

    private void Save()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
        _stickPreview.Refresh();
    }

    private UIElement Slider(string label, int min, int max, int value, Action<int> set, Func<int, string>? format = null) =>
        PadUi.SliderRow(label, min, max, value, v => format?.Invoke((int)v) ?? $"{v:0}%", v => { set((int)v); Save(); }, 250);

    private CheckBox Check(string text, bool value, Action<bool> set)
    {
        var c = new CheckBox { Content = text, IsChecked = value, FontSize = 12.5, Margin = new Thickness(0, 0, 18, 8) };
        c.Click += (_, _) => { set(c.IsChecked == true); Save(); };
        return c;
    }

    private WrapPanel Chips<T>(IEnumerable<(T Value, string Label)> options, T current, Action<T> set) where T : struct
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
        foreach (var (b, v) in list) b.IsChecked = EqualityComparer<T>.Default.Equals(v, current);
        return wrap;
    }

    // ------------------------------------------------------------ analógicos

    private UIElement StickCard()
    {
        var st = new StackPanel();
        st.Children.Add(Head("ANALÓGICOS"));
        st.Children.Add(Chips(new[] { (false, "Esquerdo (andar)"), (true, "Direito (mira)") }, _right, v => { _right = v; BuildStick(); }));
        st.Children.Add(_stickPreview);
        st.Children.Add(_stickBody);
        BuildStick();
        return st;
    }

    private void BuildStick()
    {
        var t = _right ? _e.Right : _e.Left;
        _stickPreview.Tuning = t;
        _stickPreview.Refresh();
        _filter.Reset();
        _stickBody.Children.Clear();

        var sliders = new WrapPanel();
        sliders.Children.Add(Slider("Zona morta interna", 0, 30, t.Deadzone, v => t.Deadzone = v));
        sliders.Children.Add(Slider("Zona morta externa", 0, 30, t.OuterDeadzone, v => t.OuterDeadzone = v));
        sliders.Children.Add(Slider("Anti-zona morta", 0, 30, t.AntiDeadzone, v => t.AntiDeadzone = v));
        sliders.Children.Add(Slider("Sensibilidade", 50, 150, t.Sensitivity, v => t.Sensitivity = v));
        sliders.Children.Add(Slider("Força vertical", 50, 150, t.VerticalScale, v => t.VerticalScale = v));
        sliders.Children.Add(Slider("Suavização", 0, 100, t.Smoothing, v => t.Smoothing = v));
        _stickBody.Children.Add(sliders);

        _stickBody.Children.Add(new TextBlock { Text = "Curva de resposta", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 6) });
        var power = Slider("Curva personalizada (expoente)", 25, 300, t.CurvePower, v => t.CurvePower = v, v => (v / 100.0).ToString("0.00"));
        power.Visibility = t.Curve == StickCurve.Personalizada ? Visibility.Visible : Visibility.Collapsed;
        _stickBody.Children.Add(Chips(new[] { (StickCurve.Linear, "Linear"), (StickCurve.Suave, "Suave (mira fina)"), (StickCurve.Agressiva, "Agressiva (giro rápido)"), (StickCurve.Personalizada, "Personalizada") },
            t.Curve, c => { t.Curve = c; power.Visibility = c == StickCurve.Personalizada ? Visibility.Visible : Visibility.Collapsed; }));
        _stickBody.Children.Add(power);

        var checks = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        checks.Children.Add(Check("Zona morta por eixo (axial)", t.Axial, v => t.Axial = v));
        checks.Children.Add(Check("Anti-retorno ao soltar", t.AntiSnapback, v => { t.AntiSnapback = v; _filter.Reset(); }));
        checks.Children.Add(Check("Inverter horizontal", t.InvertX, v => t.InvertX = v));
        checks.Children.Add(Check("Inverter vertical", t.InvertY, v => t.InvertY = v));
        _stickBody.Children.Add(checks);

        _stickBody.Children.Add(PadUi.Muted(
            "Interna: ignora o começo do curso (drift). Externa: chega no máximo antes da borda (controle gasto que não alcança 100%). " +
            "Anti-zona morta: compensa a zona morta que o próprio jogo já tem. Suavização tira a tremida da mão, mas atrasa um pouco. " +
            "Anti-retorno: ao soltar de uma vez, a mola passa do centro e o personagem vira para trás por um instante; isso segura (bom em analógicos Hall Effect e TMR).", 11.5));
    }

    // ------------------------------------------------------------ gatilhos

    private UIElement TriggerCard()
    {
        var st = new StackPanel();
        st.Children.Add(Head("GATILHOS (L2 / R2)"));
        var cols = new WrapPanel();
        foreach (var (name, t, bar, text) in new[] { ("L2 (LT)", _e.L2!, _l2, _l2Text), ("R2 (RT)", _e.R2!, _r2, _r2Text) })
        {
            var col = new StackPanel { Width = 320, Margin = new Thickness(0, 0, 24, 0) };
            col.Children.Add(new TextBlock { Text = name, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            col.Children.Add(bar);
            col.Children.Add(text);
            var tt = t;
            col.Children.Add(Slider("Zona morta", 0, 40, tt.Deadzone ?? _e.TriggerDeadzone, v => tt.Deadzone = v));
            col.Children.Add(Slider("Curso até 100%", 50, 100, tt.Max, v => tt.Max = v));
            col.Children.Add(Chips(new[] { (StickCurve.Linear, "Linear"), (StickCurve.Suave, "Suave"), (StickCurve.Agressiva, "Agressiva") }, tt.Curve, c => tt.Curve = c));
            col.Children.Add(Check("Gatilho sensível (qualquer toque = 100%)", tt.HairTrigger, v => tt.HairTrigger = v));
            cols.Children.Add(col);
        }
        st.Children.Add(cols);
        st.Children.Add(PadUi.Muted("Curso até 100%: o tiro sai inteiro sem apertar até o fundo. Gatilho sensível é bom para atirar em jogo de tiro; deixe desligado em corrida, onde o acelerador precisa ser dosado.", 11.5));
        return st;
    }

    // ------------------------------------------------------------ giroscópio

    private UIElement GyroCard()
    {
        var st = new StackPanel();
        st.Children.Add(Head("GIROSCÓPIO (MIRA PELO MOVIMENTO, PS4/PS5)"));
        var top = new WrapPanel();
        top.Children.Add(Check("Ligar a mira pelo giroscópio", _e.GyroAim, v => _e.GyroAim = v));
        top.Children.Add(Check("Só enquanto segura L2", _e.GyroOnlyWhenAiming, v => _e.GyroOnlyWhenAiming = v));
        top.Children.Add(Check("Inverter horizontal", _e.GyroInvertX, v => _e.GyroInvertX = v));
        top.Children.Add(Check("Inverter vertical", _e.GyroInvertY, v => _e.GyroInvertY = v));
        st.Children.Add(top);
        var sliders = new WrapPanel();
        sliders.Children.Add(Slider("Sensibilidade", 1, 100, _e.GyroSensitivity, v => _e.GyroSensitivity = v));
        sliders.Children.Add(Slider("Zona morta (mão parada)", 0, 10, _e.GyroDeadzone, v => _e.GyroDeadzone = v, v => v + " °/s"));
        sliders.Children.Add(Slider("Suavização", 0, 100, _e.GyroSmoothing, v => { _e.GyroSmoothing = v; _gyroFilter.Reset(); }));
        sliders.Children.Add(Slider("Força vertical", 50, 150, _e.GyroVertical, v => _e.GyroVertical = v));
        st.Children.Add(sliders);
        st.Children.Add(_gyroText);
        st.Children.Add(PadUi.Muted("A suavização do giroscópio só age em movimentos lentos (mira fina); virar rápido continua sem atraso.", 11.5));
        return st;
    }

    // ------------------------------------------------------------ ao vivo

    protected override void Live(PadState s)
    {
        long now = Stopwatch.GetTimestamp();
        double dt = _last == 0 ? 0.016 : Math.Min(0.1, (now - _last) / (double)Stopwatch.Frequency);
        _last = now;

        // a prévia não soma o giroscópio no analógico (ele aparece separado)
        var o = PadMapper.Map(s, _e.WithoutGyro(), _filter, dt);

        bool swap = _e.SwapSticks;
        double rawX = _right ? s.RX : s.LX, rawY = _right ? s.RY : s.LY;
        double outX = _right ^ swap ? o.RX : o.LX, outY = _right ^ swap ? o.RY : o.LY;
        _stickPreview.Show(rawX, rawY, outX, outY);

        double dz2 = (_e.L2!.Deadzone ?? _e.TriggerDeadzone) / 100.0, dzR = (_e.R2!.Deadzone ?? _e.TriggerDeadzone) / 100.0;
        _l2.Show(s.L2, o.L2, dz2, _e.L2.Max / 100.0);
        _r2.Show(s.R2, o.R2, dzR, _e.R2.Max / 100.0);
        _l2Text.Text = $"apertado {s.L2 * 100:0}% → jogo recebe {o.L2 * 100:0}%";
        _r2Text.Text = $"apertado {s.R2 * 100:0}% → jogo recebe {o.R2 * 100:0}%";

        var (gx, gy) = PadMapper.Gyro(s.GyroYaw, s.GyroPitch, _e, _gyroFilter, dt);
        _gyroText.Text = $"Movimento agora: {s.GyroYaw:0.0} / {s.GyroPitch:0.0} °/s → depois do filtro: {gx:0.0} / {gy:0.0} °/s";
    }
}
