using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MBExtreme.UI.Controls;

/// <summary>Gráfico de área com o histórico de um valor (mais recente à direita).</summary>
public partial class AreaChart : UserControl
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(AreaChart),
        new PropertyMetadata(null, (d, _) => ((AreaChart)d).Redraw()));

    /// <summary>Teto do eixo. Zero ou menos: ajusta sozinho ao maior valor do histórico.</summary>
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(AreaChart),
        new PropertyMetadata(0.0, (d, _) => ((AreaChart)d).Redraw()));

    public AreaChart()
    {
        InitializeComponent();
    }

    public IReadOnlyList<double>? Values
    {
        get => (IReadOnlyList<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void Redraw()
    {
        if (Line is null || Area is null) return;

        var values = Values;
        double width = ActualWidth, height = ActualHeight;
        if (values is null || values.Count < 2 || width <= 0 || height <= 0)
        {
            Line.Points = new PointCollection();
            Area.Points = new PointCollection();
            return;
        }

        double max = Maximum > 0 ? Maximum : Math.Max(1, values.Max() * 1.15);
        double step = width / (values.Count - 1);
        var points = new PointCollection(values.Count);
        for (int i = 0; i < values.Count; i++)
        {
            double y = height - Math.Clamp(values[i] / max, 0, 1) * (height - 4) - 2;
            points.Add(new Point(i * step, y));
        }

        Line.Points = points;

        var area = new PointCollection(points) { new Point(width, height), new Point(0, height) };
        Area.Points = area;
    }
}
