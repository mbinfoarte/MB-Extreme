using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace MBExtreme.UI.Controls;

/// <summary>
/// O efeito do item escolhido do menu lateral em qualquer cartão: ao passar o mouse, um brilho de vidro amarelado que segue o cursor,
/// a borda acende e a barrinha em degradê com brilho cresce na esquerda; o ícone do cartão balança. Sai suave ao tirar o mouse.
/// </summary>
internal static class GlassFx
{
    public static void Attach(Border card, FrameworkElement? icon = null, bool bar = true)
    {
        if (card.Child is null || card.Tag is "glassfx") return;
        card.Tag = "glassfx";
        var pad = card.Padding;
        var radius = card.CornerRadius;
        var child = card.Child;
        card.Child = null;

        var glow = new Border { CornerRadius = radius, Opacity = 0, IsHitTestVisible = false, Margin = new Thickness(-pad.Left, -pad.Top, -pad.Right, -pad.Bottom) };
        var brush = new RadialGradientBrush
        {
            RadiusX = 0.55, RadiusY = 1.2,
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x30, 0xFF, 0xC8, 0x1A), 0),
                new GradientStop(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), 0.55),
                new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1),
            },
        };
        glow.Background = brush;

        var grid = new Grid();
        grid.Children.Add(glow);
        grid.Children.Add(child);
        Border? accent = null;
        if (bar)
        {
            accent = new Border
            {
                Width = 3.5, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false,
                Margin = new Thickness(-pad.Left + 1, 4, 0, 4), Opacity = 0,
                Background = (Brush)Application.Current.FindResource("AccentGradientVertical"),
                Effect = new DropShadowEffect { Color = Color.FromRgb(0xFF, 0xC4, 0x00), BlurRadius = 10, ShadowDepth = 0, Opacity = 0.85 },
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 0.3),
            };
            grid.Children.Add(accent);
        }
        card.Child = grid;

        Brush? normalEdge = null;
        card.MouseEnter += (_, e) =>
        {
            normalEdge ??= card.BorderBrush;
            card.BorderBrush = (Brush)Application.Current.FindResource("SideSelectedEdgeBrush");
            Fade(glow, 1);
            if (accent is not null)
            {
                Fade(accent, 1);
                if (Motion.Enabled) ((ScaleTransform)accent.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, Motion.Normal) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                else ((ScaleTransform)accent.RenderTransform).ScaleY = 1;
            }
            if (icon is not null) Motion.Wiggle(icon);
            Follow(glow, brush, e);
        };
        card.MouseMove += (_, e) => Follow(glow, brush, e);
        card.MouseLeave += (_, _) =>
        {
            if (normalEdge is not null) card.BorderBrush = normalEdge;
            Fade(glow, 0);
            if (accent is not null)
            {
                Fade(accent, 0);
                if (Motion.Enabled) ((ScaleTransform)accent.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.3, Motion.Normal));
            }
        };
    }

    private static void Fade(UIElement element, double to)
    {
        if (Motion.Enabled) element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(to, Motion.Fast));
        else { element.BeginAnimation(UIElement.OpacityProperty, null); element.Opacity = to; }
    }

    private static void Follow(Border glow, RadialGradientBrush brush, MouseEventArgs e)
    {
        if (glow.ActualWidth <= 0 || glow.ActualHeight <= 0) return;
        var p = e.GetPosition(glow);
        var center = new Point(Math.Clamp(p.X / glow.ActualWidth, 0, 1), Math.Clamp(p.Y / glow.ActualHeight, 0, 1));
        brush.Center = center;
        brush.GradientOrigin = center;
    }

    /// <summary>Ícone da função num quadradinho colorido (Segoe MDL2). breathe: pulsa devagar (cartões ao vivo).</summary>
    private static readonly Random Phase = new();

    public static Border Badge(string glyph, Color color, double size = 30, bool breathe = false) => Badge(glyph, color, size, breathe, alive: true);

    /// <summary>alive: o ícone fica vivo o tempo todo (pulso leve com brilho, cada um no seu ritmo para não piscarem juntos).</summary>
    public static Border Badge(string glyph, Color color, double size, bool breathe, bool alive)
    {
        var text = new TextBlock
        {
            Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = size * 0.5,
            Foreground = new SolidColorBrush(color), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var badge = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.3), VerticalAlignment = VerticalAlignment.Center,
            Background = new LinearGradientBrush(Color.FromArgb(0x38, color.R, color.G, color.B), Color.FromArgb(0x14, color.R, color.G, color.B), 45),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, color.R, color.G, color.B)), BorderThickness = new Thickness(1),
            Child = text,
        };
        badge.Loaded += (_, _) =>
        {
            Motion.Pop(badge);
            if (breathe) badge.Dispatcher.BeginInvoke(new Action(() => Motion.Breathe(text, 1.12, 2.2)), System.Windows.Threading.DispatcherPriority.Background);
            else if (alive && Motion.Enabled) Alive(text, badge, color);
        };
        return badge;
    }

    /// <summary>Pulso leve do ícone e brilho da moldura, com início sorteado (os ícones não pulsam todos juntos).</summary>
    private static void Alive(TextBlock text, Border badge, Color color)
    {
        var begin = TimeSpan.FromMilliseconds(Phase.Next(0, 2400));
        var scale = new ScaleTransform(1, 1);
        text.RenderTransformOrigin = new Point(0.5, 0.5);
        text.RenderTransform = scale;
        var pulse = new DoubleAnimation(1, 1.14, new Duration(TimeSpan.FromSeconds(1.3)))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        var glow = new DropShadowEffect { Color = color, BlurRadius = 10, ShadowDepth = 0, Opacity = 0 };
        badge.Effect = glow;
        glow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0, 0.55, new Duration(TimeSpan.FromSeconds(1.3)))
        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin });
    }

    /// <summary>Ventoinha desenhada (4 pás) para girar na velocidade real.</summary>
    public static FrameworkElement Fan(Color color, double size = 20)
    {
        var brush = new SolidColorBrush(color);
        var canvas = new Grid { Width = size, Height = size, RenderTransformOrigin = new Point(0.5, 0.5) };
        var blades = System.Windows.Media.Geometry.Parse(
            "M10,8.6 C10,3.6 12,0.6 15,0.6 C18,0.6 17.5,5.6 12.5,8.1 Z " +
            "M11.4,10 C16.4,10 19.4,12 19.4,15 C19.4,18 14.4,17.5 11.9,12.5 Z " +
            "M10,11.4 C10,16.4 8,19.4 5,19.4 C2,19.4 2.5,14.4 7.5,11.9 Z " +
            "M8.6,10 C3.6,10 0.6,8 0.6,5 C0.6,2 5.6,2.5 8.1,7.5 Z");
        canvas.Children.Add(new System.Windows.Shapes.Path { Data = blades, Fill = brush, Stretch = Stretch.Uniform, Opacity = 0.9 });
        canvas.Children.Add(new System.Windows.Shapes.Ellipse { Width = size * 0.26, Height = size * 0.26, Fill = brush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        return canvas;
    }

    /// <summary>Vassourinha desenhada que varre (balança) — indica que há limpeza a fazer.</summary>
    public static FrameworkElement Broom(Color color, double size = 22)
    {
        var brush = new SolidColorBrush(color);
        var grid = new Grid { Width = size, Height = size, RenderTransformOrigin = new Point(0.5, 0.1) };
        var shape = System.Windows.Media.Geometry.Parse("M10,0 L11.4,0 L11.4,11 L10,11 Z M5,11 L16.4,11 L19,20 L2.4,20 Z");
        grid.Children.Add(new System.Windows.Shapes.Path { Data = shape, Fill = brush, Stretch = Stretch.Uniform });
        grid.Children.Add(new System.Windows.Shapes.Path
        {
            Data = System.Windows.Media.Geometry.Parse("M6,13 L4.6,19.4 M9,13 L8.4,19.4 M12.4,13 L13,19.4 M15.4,13 L16.8,19.4"),
            Stroke = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x14)), StrokeThickness = 0.9, Stretch = Stretch.Uniform, Margin = new Thickness(size * 0.12, size * 0.55, size * 0.12, 0),
        });
        if (Motion.Enabled)
        {
            var rotate = new RotateTransform(-14);
            grid.RenderTransform = rotate;
            rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-14, 14, new Duration(TimeSpan.FromSeconds(0.55)))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        }
        return grid;
    }

    /// <summary>Botão com ícone e texto.</summary>
    public static object IconContent(string glyph, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) });
        row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }
}
