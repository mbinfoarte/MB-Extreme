using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MBExtreme.UI.Controls;

/// <summary>
/// Anel redondo do Painel ao vivo (igual ao esboço aprovado): trilho escuro, arco colorido até o valor, número grande e a unidade
/// por baixo, dentro do anel. A cor segue o valor (normal, atenção, alerta) e o arco anda suave entre as leituras.
/// </summary>
public sealed class LiveRing : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double?), typeof(LiveRing),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, e) => ((LiveRing)d).Animate()));
    private static readonly DependencyProperty SweepProperty = DependencyProperty.Register("Sweep", typeof(double), typeof(LiveRing),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double? Value { get => (double?)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Maximum { get; set; } = 100;
    public double WarnAt { get; set; } = 70;
    public double BadAt { get; set; } = 90;
    public string Unit { get; set; } = "";
    /// <summary>Cor normal do arco (#RRGGBB).</summary>
    public string Accent { get; set; } = "#FFC400";
    public double Thickness { get; set; } = 9;

    private static readonly Brush Track = Frozen(Color.FromRgb(0x26, 0x26, 0x2B));
    private static readonly Brush Muted = Frozen(Color.FromRgb(0x8E, 0x8E, 0x96));
    private static readonly Brush Text = Frozen(Color.FromRgb(0xF4, 0xF4, 0xF5));

    public LiveRing()
    {
        Width = 112;
        Height = 112;
        System.Windows.Automation.AutomationProperties.SetName(this, "Medidor");
    }

    private void Animate()
    {
        double target = Value is double v ? Math.Clamp(v / Math.Max(1, Maximum), 0, 1) * 359.9 : 0;
        if (Motion.Enabled) BeginAnimation(SweepProperty, new DoubleAnimation(target, new Duration(TimeSpan.FromMilliseconds(500))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        else { BeginAnimation(SweepProperty, null); SetValue(SweepProperty, target); }
        System.Windows.Automation.AutomationProperties.SetName(this, Value is double x ? $"{x:0} {Unit}" : "sem leitura");
    }

    private Brush ArcBrush()
    {
        double v = Value ?? 0;
        if (v >= BadAt) return Frozen(Color.FromRgb(0xFF, 0x5A, 0x5F));
        if (v >= WarnAt) return Frozen(Color.FromRgb(0xFF, 0xA9, 0x4D));
        try { return Frozen((Color)ColorConverter.ConvertFromString(Accent)); }
        catch (FormatException) { return Frozen(Color.FromRgb(0xFF, 0xC4, 0x00)); }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= Thickness * 2) return;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        double r = size / 2 - Thickness / 2;
        dc.DrawEllipse(null, new Pen(Track, Thickness), c, r, r);

        double sweep = (double)GetValue(SweepProperty);
        if (sweep > 0.5)
        {
            var pen = new Pen(ArcBrush(), Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            Point P(double deg) => new(c.X + r * Math.Cos(deg * Math.PI / 180), c.Y + r * Math.Sin(deg * Math.PI / 180));
            var fig = new PathFigure { StartPoint = P(-90), IsClosed = false };
            fig.Segments.Add(new ArcSegment(P(-90 + sweep), new Size(r, r), 0, sweep > 180, SweepDirection.Clockwise, true));
            var geo = new PathGeometry(new[] { fig });
            geo.Freeze();
            dc.DrawGeometry(null, pen, geo);
        }

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var bold = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var normal = new Typeface("Segoe UI");
        var number = new FormattedText(Value is double v ? v.ToString("0", CultureInfo.CurrentCulture) : "—", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, bold, size * 0.24, Text, dpi);
        var unit = new FormattedText(Unit, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, normal, size * 0.1, Muted, dpi);
        double total = number.Height + unit.Height * 0.85;
        dc.DrawText(number, new Point(c.X - number.Width / 2, c.Y - total / 2));
        dc.DrawText(unit, new Point(c.X - unit.Width / 2, c.Y - total / 2 + number.Height * 0.92));
    }

    private static Brush Frozen(Color color)
    {
        var b = new SolidColorBrush(color);
        b.Freeze();
        return b;
    }
}
