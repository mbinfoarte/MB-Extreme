using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MBExtreme.UI.Controls;

/// <summary>
/// Brilho que acompanha o mouse: o modelo do controle tem uma borda "Glow" e aqui ela ganha um degradê redondo
/// cujo centro anda junto com o ponteiro (o acender e apagar fica nos gatilhos do tema).
/// </summary>
internal static class MouseGlow
{
    public static void Attach(Control control)
    {
        control.MouseEnter += Move;
        control.MouseMove += Move;
    }

    private static void Move(object sender, MouseEventArgs e)
    {
        if (sender is not Control control || control.Template?.FindName("Glow", control) is not Border glow) return;
        if (glow.ActualWidth <= 0 || glow.ActualHeight <= 0) return;
        if (glow.Background is not RadialGradientBrush brush || brush.IsFrozen)
        {
            brush = new RadialGradientBrush
            {
                RadiusX = 0.42,
                RadiusY = 1.5,
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x46, 0xFF, 0xC8, 0x1A), 0),
                    new GradientStop(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF), 0.5),
                    new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1),
                },
            };
            glow.Background = brush;
        }

        var p = e.GetPosition(glow);
        var center = new Point(Math.Clamp(p.X / glow.ActualWidth, 0, 1), Math.Clamp(p.Y / glow.ActualHeight, 0, 1));
        brush.Center = center;
        brush.GradientOrigin = center;
    }
}
