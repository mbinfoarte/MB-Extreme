using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MBExtreme.UI.Controls;

/// <summary>Anel de progresso circular com a porcentagem no centro.</summary>
public partial class ProgressRing : UserControl
{
    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(
        nameof(Percent), typeof(double), typeof(ProgressRing),
        new PropertyMetadata(0.0, (d, e) => ((ProgressRing)d).Update((double)e.NewValue)));

    public ProgressRing()
    {
        InitializeComponent();
        Update(0);
    }

    public double Percent
    {
        get => (double)GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    private void Update(double percent)
    {
        const double radius = 100, center = 120;
        double clamped = Math.Clamp(percent, 0, 100);

        // Evita o arco degenerado de 360 graus (ponto final igual ao inicial).
        double angle = Math.Min(clamped, 99.999) / 100 * 360;
        double radians = (angle - 90) * Math.PI / 180;
        var end = new Point(center + radius * Math.Cos(radians), center + radius * Math.Sin(radians));

        var figure = new PathFigure { StartPoint = new Point(center, center - radius), IsClosed = false };
        figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, angle > 180, SweepDirection.Clockwise, true));
        Arc.Data = new PathGeometry(new[] { figure });

        Label.Text = $"{Math.Round(clamped)}%";
    }
}
