using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace MBExtreme.UI.Overlay;

/// <summary>Aviso pequeno por cima do jogo ("Clipe salvo"), que some sozinho. Não pega o mouse nem o foco. Aparece em jogos em janela ou sem bordas; em tela cheia exclusiva o Windows pode esconder.</summary>
internal sealed class ClipToast : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExToolWindow = 0x80;
    private const int WsExNoActivate = 0x08000000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    private static ClipToast? s_current;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3.5) };

    private ClipToast(string title, string detail)
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
        panel.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)) });
        if (detail.Length > 0) panel.Children.Add(new TextBlock { Text = detail, FontSize = 12, Foreground = Brushes.White, Margin = new Thickness(0, 2, 0, 0), MaxWidth = 380, TextWrapping = TextWrapping.Wrap });
        Content = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(215, 0, 0, 0)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)),
            BorderThickness = new Thickness(1.5),
            Padding = new Thickness(16, 10, 16, 12),
            Child = panel,
        };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLong(handle, GwlExStyle, GetWindowLong(handle, GwlExStyle) | WsExTransparent | WsExToolWindow | WsExNoActivate);
        };
        Loaded += (_, _) =>
        {
            Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
            Top = 28;
            _timer.Start();
        };
        _timer.Tick += (_, _) => { _timer.Stop(); Close(); };
        Closed += (_, _) => { if (ReferenceEquals(s_current, this)) s_current = null; };
    }

    /// <summary>Mostra o aviso (troca o que estiver na tela). Pode ser chamado de qualquer lugar da interface.</summary>
    public static void Show(string title, string detail = "")
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                s_current?.Close();
                var toast = new ClipToast(title, detail);
                s_current = toast;
                toast.Show();
            }
            catch (InvalidOperationException) { }
        }));
    }
}
