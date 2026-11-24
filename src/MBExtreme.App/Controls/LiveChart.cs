using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace MBExtreme.UI.Controls;

/// <summary>
/// Gráfico animado com uma ou mais séries (a mais recente à direita). A cada nova medição a linha desliza suavemente para a esquerda.
/// </summary>
public sealed class LiveChart : Grid
{
    private sealed class Series
    {
        public required Polyline Line { get; init; }
        public required Polygon Fill { get; init; }
        public required TranslateTransform Shift { get; init; }
    }

    private readonly List<Series> _series = new();
    private readonly Canvas _canvas = new() { ClipToBounds = true };
    private readonly TextBlock _maxLabel = new() { FontSize = 11, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
    private IReadOnlyList<IReadOnlyList<double>> _last = Array.Empty<IReadOnlyList<double>>();
    private readonly int _length;

    /// <param name="length">Quantidade de pontos visíveis.</param>
    /// <param name="colors">Uma cor por série.</param>
    public LiveChart(int length, params Color[] colors)
    {
        _length = Math.Max(10, length);
        Children.Add(_canvas);
        Children.Add(_maxLabel);
        _maxLabel.Foreground = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x96));

        foreach (var color in colors)
        {
            var shift = new TranslateTransform();
            var fill = new Polygon
            {
                Fill = new LinearGradientBrush(Color.FromArgb(0x55, color.R, color.G, color.B), Color.FromArgb(0x00, color.R, color.G, color.B), 90),
                RenderTransform = shift,
            };
            var line = new Polyline
            {
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round,
                RenderTransform = shift,
            };
            _canvas.Children.Add(fill);
            _canvas.Children.Add(line);
            _series.Add(new Series { Line = line, Fill = fill, Shift = shift });
        }

        SizeChanged += (_, _) => Draw(false);
    }

    /// <summary>Linha tracejada em uma série: ajuda quem não distingue as cores (além da legenda).</summary>
    public void SetDashed(int series, double dash = 4, double gap = 3)
    {
        if (series < 0 || series >= _series.Count) return;
        _series[series].Line.StrokeDashArray = new DoubleCollection { dash, gap };
    }

    /// <summary>Teto do eixo. Zero ou menos: ajusta ao maior valor visível.</summary>
    public double Maximum { get; set; }

    /// <summary>Formata o rótulo do teto (ex.: "12 Mb/s").</summary>
    public Func<double, string> FormatMax { get; set; } = v => v.ToString("0");

    /// <summary>Atualiza as séries (na mesma ordem das cores) e anima a rolagem.</summary>
    public void Update(params IReadOnlyList<double>[] values)
    {
        _last = values;
        Draw(true);
    }

    private void Draw(bool animate)
    {
        double width = ActualWidth, height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        double visibleMax = _last.Count == 0 ? 0 : _last.Max(s => s.Count == 0 ? 0 : s.Max());
        double max = Maximum > 0 ? Maximum : Math.Max(1, visibleMax * 1.2);
        _maxLabel.Text = FormatMax(max);

        double step = width / (_length - 1);
        for (int s = 0; s < _series.Count; s++)
        {
            var values = s < _last.Count ? _last[s] : Array.Empty<double>();
            var points = new PointCollection(_length);
            int pad = Math.Max(0, _length - values.Count);
            for (int i = 0; i < _length; i++)
            {
                int start = Math.Max(0, values.Count - _length);
                double v = i < pad ? 0 : values[start + (i - pad)];
                double y = height - Math.Clamp(v / max, 0, 1) * (height - 6) - 3;
                points.Add(new Point(i * step, y));
            }

            _series[s].Line.Points = points;
            _series[s].Fill.Points = new PointCollection(points) { new Point(width, height), new Point(0, height) };

            if (animate)
            {
                _series[s].Shift.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(step, 0, TimeSpan.FromMilliseconds(950)));
            }
        }
    }
}
