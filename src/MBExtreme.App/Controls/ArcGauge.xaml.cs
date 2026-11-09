using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MBExtreme.UI.Controls;

/// <summary>Medidor em arco de 270 graus. Fica laranja e vermelho quando o valor passa dos limites de aviso.</summary>
public partial class ArcGauge : UserControl
{
    private const double Center = 80, Radius = 64, StartDegrees = -135, SweepDegrees = 270;

    public static readonly DependencyProperty ValueProperty = Register(nameof(Value), typeof(double?), null);
    public static readonly DependencyProperty MaximumProperty = Register(nameof(Maximum), typeof(double), 100.0);
    public static readonly DependencyProperty UnitProperty = Register(nameof(Unit), typeof(string), "");
    public static readonly DependencyProperty WarnAtProperty = Register(nameof(WarnAt), typeof(double), 70.0);
    public static readonly DependencyProperty BadAtProperty = Register(nameof(BadAt), typeof(double), 90.0);

    private static DependencyProperty Register(string name, Type type, object? defaultValue) =>
        DependencyProperty.Register(name, type, typeof(ArcGauge),
            new PropertyMetadata(defaultValue, (d, _) => ((ArcGauge)d).Redraw()));

    public ArcGauge()
    {
        InitializeComponent();
        Redraw();
    }

    public double? Value { get => (double?)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public double WarnAt { get => (double)GetValue(WarnAtProperty); set => SetValue(WarnAtProperty, value); }
    public double BadAt { get => (double)GetValue(BadAtProperty); set => SetValue(BadAtProperty, value); }

    private static Point PointAt(double degrees)
    {
        double radians = degrees * Math.PI / 180;
        return new Point(Center + Radius * Math.Sin(radians), Center - Radius * Math.Cos(radians));
    }

    private static PathGeometry ArcGeometry(double sweepDegrees)
    {
        var figure = new PathFigure { StartPoint = PointAt(StartDegrees), IsClosed = false };
        figure.Segments.Add(new ArcSegment(PointAt(StartDegrees + sweepDegrees), new Size(Radius, Radius), 0,
            sweepDegrees > 180, SweepDirection.Clockwise, true));
        return new PathGeometry(new[] { figure });
    }

    private void Redraw()
    {
        if (Track is null || Arc is null) return; // chamado durante a criação das propriedades

        Track.Data = ArcGeometry(SweepDegrees);
        UnitText.Text = Unit;

        if (Value is not double value)
        {
            Arc.Data = null;
            ValueText.Text = "—";
            return;
        }

        double fraction = Maximum <= 0 ? 0 : Math.Clamp(value / Maximum, 0, 1);
        Arc.Data = fraction < 0.005 ? null : ArcGeometry(SweepDegrees * fraction);
        ValueText.Text = Math.Round(value).ToString("0");

        string brush = value >= BadAt ? "BadBrush" : value >= WarnAt ? "WarnBrush" : "GaugeBrush";
        Arc.Stroke = (Brush)FindResource(brush);
    }
}
