using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace MBExtreme.UI.Overlay;

/// <summary>Janela transparente, sempre no topo e que não captura o mouse, com FPS, tempo de quadro e sensores.</summary>
internal sealed class FpsOverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExToolWindow = 0x80;
    private const int WsExNoActivate = 0x08000000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    private readonly TextBlock _fps = new() { FontSize = 30, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
    private readonly TextBlock _detail = new() { FontSize = 13, Foreground = Brushes.White, Margin = new Thickness(0, 2, 0, 0) };

    public FpsOverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;

        var panel = new StackPanel();
        panel.Children.Add(_fps);
        panel.Children.Add(_detail);
        Content = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)),
            Padding = new Thickness(14, 8, 14, 10),
            Child = panel,
        };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLong(handle, GwlExStyle, GetWindowLong(handle, GwlExStyle) | WsExTransparent | WsExToolWindow | WsExNoActivate);
        };
    }

    public void Place(int corner)
    {
        const double margin = 16;
        Left = corner is 1 or 3 ? SystemParameters.PrimaryScreenWidth - Math.Max(ActualWidth, 200) - margin : margin;
        Top = corner is 2 or 3 ? SystemParameters.PrimaryScreenHeight - Math.Max(ActualHeight, 100) - margin : margin;
    }

    public void SetText(string fps, string detail)
    {
        _fps.Text = fps;
        _detail.Text = detail;
    }
}
