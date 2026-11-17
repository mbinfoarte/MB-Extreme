using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MBExtreme.UI.Controls;

/// <summary>
/// Seletor de cor: quadrado de saturação/brilho, barra de matiz (arco-íris) e prévia. Arrastar com o mouse muda a cor na hora.
/// </summary>
internal sealed class ColorPicker : UserControl
{
    private readonly Grid _sv = new() { Width = 220, Height = 160, ClipToBounds = true, Cursor = Cursors.Cross };
    private readonly Rectangle _hueBase = new();
    private readonly Ellipse _svDot = new() { Width = 14, Height = 14, Stroke = Brushes.White, StrokeThickness = 2.5, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly Grid _hue = new() { Width = 220, Height = 16, Margin = new Thickness(0, 10, 0, 0), Cursor = Cursors.Hand };
    private readonly Border _hueKnob = new() { Width = 8, Height = 20, CornerRadius = new CornerRadius(4), BorderBrush = Brushes.White, BorderThickness = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false, Margin = new Thickness(0, -2, 0, -2) };
    private double _h, _s = 1, _v = 1;

    public event Action<Color>? ColorChanged;

    public ColorPicker()
    {
        _sv.Children.Add(new Border { CornerRadius = new CornerRadius(10), Child = _hueBase });
        _sv.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = new LinearGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255), 0) });
        _sv.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, 90) });
        _sv.Children.Add(_svDot);
        _sv.MouseLeftButtonDown += (_, e) => { _sv.CaptureMouse(); PickSv(e.GetPosition(_sv)); };
        _sv.MouseMove += (_, e) => { if (_sv.IsMouseCaptured) PickSv(e.GetPosition(_sv)); };
        _sv.MouseLeftButtonUp += (_, _) => _sv.ReleaseMouseCapture();

        var rainbow = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        for (int i = 0; i <= 6; i++) rainbow.GradientStops.Add(new GradientStop(FromHsv(i * 60, 1, 1), i / 6.0));
        _hue.Children.Add(new Border { CornerRadius = new CornerRadius(8), Background = rainbow });
        _hue.Children.Add(_hueKnob);
        _hue.MouseLeftButtonDown += (_, e) => { _hue.CaptureMouse(); PickHue(e.GetPosition(_hue).X); };
        _hue.MouseMove += (_, e) => { if (_hue.IsMouseCaptured) PickHue(e.GetPosition(_hue).X); };
        _hue.MouseLeftButtonUp += (_, _) => _hue.ReleaseMouseCapture();

        var st = new StackPanel();
        st.Children.Add(_sv);
        st.Children.Add(_hue);
        Content = st;
        Draw();
    }

    public void SetColor(Color c)
    {
        (_h, _s, _v) = ToHsv(c);
        Draw();
    }

    private void PickSv(Point p)
    {
        _s = Math.Clamp(p.X / _sv.Width, 0, 1);
        _v = 1 - Math.Clamp(p.Y / _sv.Height, 0, 1);
        Draw();
        ColorChanged?.Invoke(FromHsv(_h, _s, _v));
    }

    private void PickHue(double x)
    {
        _h = Math.Clamp(x / _hue.Width, 0, 1) * 359.9;
        Draw();
        ColorChanged?.Invoke(FromHsv(_h, _s, _v));
    }

    private void Draw()
    {
        _hueBase.Fill = new SolidColorBrush(FromHsv(_h, 1, 1));
        _svDot.Margin = new Thickness(_s * _sv.Width - 7, (1 - _v) * _sv.Height - 7, 0, 0);
        _hueKnob.Margin = new Thickness(_h / 360 * _hue.Width - 4, -2, 0, -2);
    }

    public static Color FromHsv(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        (double r, double g, double b) = (h % 360 / 60) switch
        {
            < 1 => (c, x, 0.0), < 2 => (x, c, 0.0), < 3 => (0.0, c, x), < 4 => (0.0, x, c), < 5 => (x, 0.0, c), _ => (c, 0.0, x),
        };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    public static (double H, double S, double V) ToHsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = d == 0 ? 0 : max == r ? 60 * (((g - b) / d) % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        if (h < 0) h += 360;
        return (h, max == 0 ? 0 : d / max, max);
    }
}
