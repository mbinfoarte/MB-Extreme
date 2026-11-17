using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MBExtreme.UI.Overlay;

/// <summary>Uma linha do contador: rótulo colorido e valor.</summary>
internal sealed record OverlayLine(string Label, string Value, Color LabelColor);

/// <summary>
/// Contador na tela do jogo (estilo RivaTuner, mas por cima do jogo, sem injetar nada): FPS, 1%/0,1% pior, tempo de quadro com
/// gráfico e sensores. Transparente, sempre no topo e sem pegar o mouse. Posição, itens e aparência vêm das configurações.
/// </summary>
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

    private readonly Border _card = new() { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 7, 12, 8) };
    private readonly Panel _host = new StackPanel();
    private readonly Polyline _graph = new() { StrokeThickness = 1.4, Stroke = new SolidColorBrush(Color.FromRgb(0x3D, 0xDC, 0x97)) };
    private readonly Queue<double> _frameTimes = new();

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
        Content = _card;
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLong(handle, GwlExStyle, GetWindowLong(handle, GwlExStyle) | WsExTransparent | WsExToolWindow | WsExNoActivate);
        };
        SizeChanged += (_, _) => Place(AppServices.Settings.OverlayPosition);
    }

    /// <summary>Posição 0 a 8: colunas esquerda/meio/direita, linhas cima/meio/baixo (16 px da borda).</summary>
    public void Place(int position)
    {
        const double margin = 16;
        var area = SystemParameters.WorkArea;
        double w = Math.Max(ActualWidth, 120), h = Math.Max(ActualHeight, 40);
        int col = Math.Clamp(position, 0, 8) % 3, row = Math.Clamp(position, 0, 8) / 3;
        Left = col switch { 0 => area.Left + margin, 1 => area.Left + (area.Width - w) / 2, _ => area.Right - w - margin };
        Top = row switch { 0 => area.Top + margin, 1 => area.Top + (area.Height - h) / 2, _ => area.Bottom - h - margin };
    }

    /// <summary>Desenha o contador com as linhas dadas (a primeira é o FPS, em destaque).</summary>
    public void Show(double? fps, double? frameMs, IReadOnlyList<OverlayLine> lines, bool graph)
    {
        var s = AppServices.Settings;
        var text = ParseColor(s.OverlayColor, Colors.White);
        _card.Background = new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(s.OverlayOpacity * 255 / 100, 0, 255), 0, 0, 0));
        _card.LayoutTransform = new ScaleTransform(s.OverlayScale / 100.0, s.OverlayScale / 100.0);
        if (frameMs is { } ft && ft > 0) { _frameTimes.Enqueue(ft); while (_frameTimes.Count > 80) _frameTimes.Dequeue(); }

        var root = new StackPanel();
        Panel body = s.OverlayHorizontal ? new WrapPanel() : new StackPanel();
        if (fps is { } f)
        {
            var head = new TextBlock { FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0, 0, s.OverlayHorizontal ? 14 : 0, 0), VerticalAlignment = VerticalAlignment.Center };
            head.Inlines.Add(new System.Windows.Documents.Run("FPS ") { Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)), FontWeight = FontWeights.Bold, FontSize = 13 });
            var fpsColor = s.OverlayFpsColors ? f >= 60 ? Color.FromRgb(0x3D, 0xDC, 0x97) : f >= 30 ? Color.FromRgb(0xFF, 0xC4, 0x00) : Color.FromRgb(0xFF, 0x5A, 0x5F) : text;
            head.Inlines.Add(new System.Windows.Documents.Run($"{f:0}") { Foreground = new SolidColorBrush(fpsColor), FontWeight = FontWeights.Bold, FontSize = 22 });
            body.Children.Add(head);
        }
        foreach (var line in lines)
        {
            var t = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 13, Margin = new Thickness(0, 1, s.OverlayHorizontal ? 14 : 0, 0), VerticalAlignment = VerticalAlignment.Center };
            t.Inlines.Add(new System.Windows.Documents.Run(line.Label + " ") { Foreground = new SolidColorBrush(line.LabelColor), FontWeight = FontWeights.SemiBold });
            t.Inlines.Add(new System.Windows.Documents.Run(line.Value) { Foreground = new SolidColorBrush(text) });
            body.Children.Add(t);
        }
        root.Children.Add(body);
        if (graph && _frameTimes.Count > 2)
        {
            const double width = 160, height = 26;
            double max = Math.Max(20, _frameTimes.Max() * 1.1);
            _graph.Points.Clear();
            int i = 0, n = _frameTimes.Count;
            foreach (double v in _frameTimes) _graph.Points.Add(new Point(i++ * width / (n - 1), height - v / max * height));
            if (_graph.Parent is Panel old) old.Children.Remove(_graph);
            var canvas = new Canvas { Width = width, Height = height, Margin = new Thickness(0, 3, 0, 0), ClipToBounds = true };
            canvas.Children.Add(_graph);
            root.Children.Add(canvas);
        }
        _card.Child = root;
    }

    public static Color ParseColor(string hex, Color fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch (FormatException) { return fallback; }
    }
}
