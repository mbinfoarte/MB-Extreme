using System.Windows;
using System.Windows.Media;

namespace MBExtreme.UI.Controls;

/// <summary>
/// Anel da saúde do PC no estilo do núcleo do benchmark (<see cref="BenchCore"/>): brilho de fundo, anel tracejado girando, arcos em
/// degradê amarelo → laranja, pontos em órbita e o arco da nota enchendo até o valor. Trabalhando (análise/correção) gira mais rápido.
/// Fica parado quando as animações estão desligadas.
/// </summary>
internal sealed class ScoreCore : FrameworkElement
{
    private static readonly Color Yellow = Color.FromRgb(0xFF, 0xC4, 0x00);
    private static readonly Color Orange = Color.FromRgb(0xFF, 0x7A, 0x00);
    private readonly DateTime _start = DateTime.Now;
    private DateTime _last = DateTime.Now;
    private bool _hooked;
    private double _value, _target;
    private bool _running;
    private Color _accent = Yellow;

    public ScoreCore(double size = 132)
    {
        Width = size;
        Height = size;
        IsHitTestVisible = false;
        Loaded += (_, _) => Hook(true);
        Unloaded += (_, _) => Hook(false);
        IsVisibleChanged += (_, _) => Hook(IsVisible);
    }

    /// <summary>Trabalhando: gira mais rápido e o brilho fica mais forte.</summary>
    public bool Running { get => _running; set { _running = value; InvalidateVisual(); } }

    /// <summary>Cor do arco da nota (amarelo bom, laranja médio, vermelho ruim). O resto fica sempre amarelo e laranja.</summary>
    public Color Accent { get => _accent; set { _accent = value; InvalidateVisual(); } }

    /// <summary>Vai até o valor (0 a 100) com animação.</summary>
    public void AnimateTo(double value) { _target = Math.Clamp(value, 0, 100); if (!_hooked) _value = _target; InvalidateVisual(); }

    /// <summary>Pula direto para o valor.</summary>
    public void Jump(double value) { _value = _target = Math.Clamp(value, 0, 100); InvalidateVisual(); }

    private void Hook(bool on)
    {
        bool animate = on && AppServices.Settings.Animations && SystemParameters.ClientAreaAnimation;
        if (animate == _hooked) return;
        _hooked = animate;
        if (animate) { _last = DateTime.Now; CompositionTarget.Rendering += Tick; }
        else { CompositionTarget.Rendering -= Tick; _value = _target; }
        InvalidateVisual();
    }

    private void Tick(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        double dt = Math.Min(0.1, (now - _last).TotalSeconds);
        _last = now;
        if (Math.Abs(_target - _value) > 0.05) _value += (_target - _value) * Math.Min(1, dt * 3.2);   // enche suave, rápido no começo
        else _value = _target;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double t = _hooked ? (DateTime.Now - _start).TotalSeconds : 0;
        double speed = _running ? 1.0 : 0.3;
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        double r = size * 0.40;

        // brilho de fundo
        byte glowA = _running ? (byte)0x48 : (byte)(0x24 + 0x10 * (0.5 + 0.5 * Math.Sin(t * 1.6)));
        dc.DrawEllipse(new RadialGradientBrush(Color.FromArgb(glowA, 0xFF, 0xB0, 0x00), Color.FromArgb(0, 0xFF, 0xB0, 0x00)), null, c, r * 1.25, r * 1.25);

        // trilho do anel e anel tracejado girando
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF)), size * 0.06), c, r, r);
        var dashed = new Pen(new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xC4, 0x00)), 1.2) { DashStyle = new DashStyle(new double[] { 2, 6 }, t * 8 * speed) };
        dc.DrawEllipse(null, dashed, c, r * 0.78, r * 0.78);

        // arco da nota (de cima, sentido horário), em degradê
        var scoreBrush = new LinearGradientBrush(_accent, _accent == Yellow ? Orange : Color.FromArgb(0xFF, (byte)Math.Min(255, _accent.R + 20), (byte)(_accent.G * 0.6), (byte)(_accent.B * 0.6)), 45);
        Arc(dc, c, r, -90, _value * 3.6, new Pen(scoreBrush, size * 0.06) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });

        // arcos girando por fora (como no benchmark)
        var gradient = new LinearGradientBrush(Yellow, Orange, 45);
        Arc(dc, c, r * 1.14, t * 70 * speed, 80, new Pen(gradient, 2.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        Arc(dc, c, r * 1.14, t * 70 * speed + 180, 45, new Pen(gradient, 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        Arc(dc, c, r * 0.66, -t * 50 * speed, 60, new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0x9A, 0x1F)), 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });

        // ponta do arco da nota brilhando
        if (_value > 1)
        {
            double a = (-90 + _value * 3.6) * Math.PI / 180;
            var tip = new Point(c.X + Math.Cos(a) * r, c.Y + Math.Sin(a) * r);
            dc.DrawEllipse(new RadialGradientBrush(Color.FromArgb(0xC0, 0xFF, 0xE0, 0x70), Color.FromArgb(0, 0xFF, 0xE0, 0x70)), null, tip, size * 0.07, size * 0.07);
        }

        // pontos em órbita
        for (int i = 0; i < 3; i++)
        {
            double a = (t * (35 + i * 17) * speed + i * 120) * Math.PI / 180;
            var p = new Point(c.X + Math.Cos(a) * r * 0.78, c.Y + Math.Sin(a) * r * 0.78);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xD8, 0x60)), null, p, size * 0.016, size * 0.016);
        }
    }

    private static void Arc(DrawingContext dc, Point c, double r, double startDeg, double sweepDeg, Pen pen)
    {
        if (sweepDeg <= 0.5) return;
        sweepDeg = Math.Min(sweepDeg, 359.9);
        double a0 = startDeg * Math.PI / 180, a1 = (startDeg + sweepDeg) * Math.PI / 180;
        var p0 = new Point(c.X + Math.Cos(a0) * r, c.Y + Math.Sin(a0) * r);
        var p1 = new Point(c.X + Math.Cos(a1) * r, c.Y + Math.Sin(a1) * r);
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(p0, false, false);
            g.ArcTo(p1, new Size(r, r), 0, sweepDeg > 180, SweepDirection.Clockwise, true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }
}
