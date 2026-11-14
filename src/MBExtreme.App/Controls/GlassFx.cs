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
    public static Border Badge(string glyph, Color color, double size = 30, bool breathe = false)
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
        };
        return badge;
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
