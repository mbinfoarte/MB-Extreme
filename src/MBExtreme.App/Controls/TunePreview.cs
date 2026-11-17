using System.Windows;
using System.Windows.Media;
using MBExtreme.Core.Gamepads;

namespace MBExtreme.UI.Controls;

/// <summary>
/// Prévia de um analógico nos ajustes finos: círculo com a zona morta (vermelha), a zona externa, o ponto do controle de verdade (cinza)
/// e o que o jogo recebe (amarelo). Ao lado, o gráfico da curva de resposta com o ponto atual.
/// </summary>
internal sealed class StickTunePreview : FrameworkElement
{
    private static readonly Brush Hot = Frozen(Color.FromRgb(0xFF, 0xC4, 0x00));
    private static readonly Brush Raw = Frozen(Color.FromRgb(0x8E, 0x8E, 0x96));
    private static readonly Brush Back = Frozen(Color.FromRgb(0x0B, 0x0B, 0x0C));
    private static readonly Brush DeadFill = Frozen(Color.FromArgb(0x2E, 0xFF, 0x5A, 0x5F));
    private static readonly Pen Ring = FrozenPen(Color.FromRgb(0x3A, 0x3A, 0x42), 2);
    private static readonly Pen Grid = FrozenPen(Color.FromRgb(0x26, 0x26, 0x2B), 1);
    private static readonly Pen Dead = FrozenPen(Color.FromRgb(0xFF, 0x5A, 0x5F), 1.2, dashed: true);
    private static readonly Pen Outer = FrozenPen(Color.FromRgb(0x6C, 0xB6, 0xFF), 1.2, dashed: true);
    private static readonly Pen CurvePen = FrozenPen(Color.FromRgb(0xFF, 0xC4, 0x00), 2);
    private static readonly Pen LinePen = FrozenPen(Color.FromRgb(0x3A, 0x3A, 0x42), 1, dashed: true);

    private double _rx, _ry, _ox, _oy;
    public StickTuning Tuning { get; set; } = new();

    public StickTunePreview()
    {
        Width = 360;
        Height = 170;
    }

    public void Show(double rawX, double rawY, double outX, double outY)
    {
        if (Math.Abs(rawX - _rx) + Math.Abs(rawY - _ry) + Math.Abs(outX - _ox) + Math.Abs(outY - _oy) < 0.002) return;
        (_rx, _ry, _ox, _oy) = (rawX, rawY, outX, outY);
        InvalidateVisual();
    }

    public void Refresh() => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualHeight, 170) - 10;
        double r = size / 2;
        var c = new Point(r + 5, r + 5);

        // círculo do analógico
        dc.DrawEllipse(Back, Ring, c, r, r);
        dc.DrawLine(Grid, new Point(c.X - r, c.Y), new Point(c.X + r, c.Y));
        dc.DrawLine(Grid, new Point(c.X, c.Y - r), new Point(c.X, c.Y + r));
        double dz = Math.Clamp(Tuning.Deadzone, 0, 50) / 100.0 * r;
        if (Tuning.Axial)
            dc.DrawRectangle(DeadFill, Dead, new Rect(c.X - dz, c.Y - r, dz * 2, r * 2));
        if (Tuning.Axial)
            dc.DrawRectangle(DeadFill, Dead, new Rect(c.X - r, c.Y - dz, r * 2, dz * 2));
        else if (dz > 0.5)
            dc.DrawEllipse(DeadFill, Dead, c, dz, dz);
        double outer = (1 - Math.Clamp(Tuning.OuterDeadzone, 0, 40) / 100.0) * r;
        if (Tuning.OuterDeadzone > 0) dc.DrawEllipse(null, Outer, c, outer, outer);

        dc.DrawEllipse(Raw, null, new Point(c.X + _rx * r, c.Y + _ry * r), 5, 5);
        dc.DrawLine(FrozenPen(Color.FromArgb(0x80, 0xFF, 0xC4, 0x00), 1.5), c, new Point(c.X + _ox * r, c.Y + _oy * r));
        dc.DrawEllipse(Hot, null, new Point(c.X + _ox * r, c.Y + _oy * r), 6, 6);

        // gráfico da curva: entrada (horizontal) × saída (vertical)
        double gx = size + 30, gw = Math.Max(60, ActualWidth - gx - 6), gh = size;
        var box = new Rect(gx, 5, gw, gh);
        dc.DrawRectangle(Back, Ring, box);
        dc.DrawLine(LinePen, box.BottomLeft, box.TopRight);
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            for (int i = 0; i <= 60; i++)
            {
                double input = i / 60.0;
                double output = PadMapper.Response(input, Tuning);
                var p = new Point(box.Left + input * gw, box.Bottom - output * gh);
                if (i == 0) g.BeginFigure(p, false, false); else g.LineTo(p, true, false);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(null, CurvePen, geo);
        double rin = Math.Min(1, Math.Sqrt(_rx * _rx + _ry * _ry));
        double rout = Math.Min(1, Math.Sqrt(_ox * _ox + _oy * _oy));
        dc.DrawEllipse(Hot, null, new Point(box.Left + rin * gw, box.Bottom - rout * gh), 4.5, 4.5);
        var tf = new Typeface("Segoe UI");
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        dc.DrawText(new FormattedText($"entrada {rin * 100:0}% → saída {rout * 100:0}%", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, tf, 11, Raw, dpi),
            new Point(box.Left + 6, box.Top + 4));
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Pen FrozenPen(Color c, double w, bool dashed = false)
    {
        var p = new Pen(Frozen(c), w);
        if (dashed) p.DashStyle = new DashStyle(new double[] { 3, 3 }, 0);
        p.Freeze();
        return p;
    }
}

/// <summary>Barra de um gatilho: quanto o dedo apertou (cinza) e o que o jogo recebe (amarelo), com a zona morta marcada.</summary>
internal sealed class TriggerTunePreview : FrameworkElement
{
    private static readonly Brush Track = Frozen(Color.FromRgb(0x1B, 0x1B, 0x1F));
    private static readonly Brush Raw = Frozen(Color.FromRgb(0x5A, 0x5A, 0x63));
    private static readonly Brush Hot = Frozen(Color.FromRgb(0xFF, 0xC4, 0x00));
    private static readonly Brush DeadFill = Frozen(Color.FromArgb(0x50, 0xFF, 0x5A, 0x5F));
    private double _raw, _out, _dz, _max = 1;

    public TriggerTunePreview()
    {
        Height = 16;
        MinWidth = 120;
    }

    public void Show(double raw, double output, double deadzone, double max)
    {
        if (Math.Abs(raw - _raw) + Math.Abs(output - _out) + Math.Abs(deadzone - _dz) + Math.Abs(max - _max) < 0.002) return;
        (_raw, _out, _dz, _max) = (raw, output, deadzone, max);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRoundedRectangle(Track, null, new Rect(0, 0, w, h), h / 2, h / 2);
        if (_dz > 0) dc.DrawRectangle(DeadFill, null, new Rect(0, 0, w * _dz, h));
        if (_max < 1) dc.DrawRectangle(DeadFill, null, new Rect(w * _max, 0, w * (1 - _max), h));
        dc.DrawRoundedRectangle(Raw, null, new Rect(0, h * 0.55, w * Math.Clamp(_raw, 0, 1), h * 0.45), 3, 3);
        dc.DrawRoundedRectangle(Hot, null, new Rect(0, 0, w * Math.Clamp(_out, 0, 1), h * 0.5), 3, 3);
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
