using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MBExtreme.Core.Memory;
using MBExtreme.Core.Sensors;

namespace MBExtreme.UI.Widget;

/// <summary>
/// Barrinha flutuante na área de trabalho (estilo HUD): foguete do MB, RAM, CPU, disco e rede, atualizando a cada segundo.
/// Sempre no topo, sem moldura, arrastável; guarda a posição. Um botão abre o programa e outro esconde o widget.
/// </summary>
public sealed class FloatingWidget : Window
{
    private readonly WindowsUsage _usage = new();
    private readonly MBExtreme.Core.Network.NetworkMonitor _net = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly TextBlock _ram = Value(), _cpu = Value(), _disk = Value(), _netText = Value();
    private readonly Action _openApp;
    private readonly Action<Rect?>? _openPanel;

    private static TextBlock Value() => new() { FontSize = 15, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)) };

    public FloatingWidget(Action openApp, Action<Rect?>? openPanel = null)
    {
        _openApp = openApp;
        _openPanel = openPanel;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = "MB Extreme";

        var s = AppServices.Settings;
        WindowStartupLocation = s.WidgetX >= 0 && s.WidgetY >= 0 ? WindowStartupLocation.Manual : WindowStartupLocation.CenterScreen;
        if (s.WidgetX >= 0) { Left = s.WidgetX; Top = s.WidgetY; }

        Content = BuildCard();
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        LocationChanged += (_, _) => { AppServices.Settings.WidgetX = Left; AppServices.Settings.WidgetY = Top; };
        Closed += (_, _) => { _timer.Stop(); _usage.Dispose(); AppServices.Settings.Save(); };

        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { Refresh(); _timer.Start(); };
    }

    private Border BuildCard()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        // foguete num círculo
        var badge = new Border
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Margin = new Thickness(2, 0, 10, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x20)), BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)), BorderThickness = new Thickness(1.4),
            Child = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 16, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        row.Children.Add(badge);

        row.Children.Add(Metric("RAM", _ram));
        row.Children.Add(Metric("CPU", _cpu));
        row.Children.Add(Metric("DISCO", _disk));
        row.Children.Add(Metric("REDE", _netText));

        // ⤢ expande a própria barra (modos do PC e atalhos); ⤡ volta a ficar fina
        _expandButton = IconButton("\uE740", "Mostrar os modos e os atalhos", ToggleExpand);
        row.Children.Add(_expandButton);
        row.Children.Add(IconButton("\uE711", "Esconder o widget", HideWidget));

        var stack = new StackPanel();
        stack.Children.Add(row);
        _extra.Visibility = Visibility.Collapsed;
        _extra.Margin = new Thickness(0, 10, 4, 4);
        stack.Children.Add(_extra);
        return new Border
        {
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x10, 0x10, 0x16)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xC4, 0x00)), BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 7, 8, 7), Child = stack, Cursor = Cursors.SizeAll,
        };
    }

    // ------------------------------------------------------------ parte expandida: modos do PC e atalhos

    private readonly StackPanel _extra = new() { Width = 420 };
    private readonly TextBlock _modeStatus = new() { FontSize = 12, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White };
    private Button? _expandButton;
    private bool _expanded;

    private void ToggleExpand()
    {
        _expanded = !_expanded;
        if (_expandButton?.Content is TextBlock t) t.Text = _expanded ? "\uE73F" : "\uE740";
        if (_expandButton is not null) _expandButton.ToolTip = _expanded ? "Recolher" : "Mostrar os modos e os atalhos";
        if (_expanded) BuildExtra();
        _extra.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildExtra()
    {
        _extra.Children.Clear();
        _modeStatus.Text = PcModes.Status();
        _extra.Children.Add(_modeStatus);
        _extra.Children.Add(new TextBlock { Text = "MODO DO PC", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA3)), Margin = new Thickness(0, 10, 0, 6) });
        var modes = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4 };
        foreach (var (mode, glyph, color) in new[]
        {
            (PcModes.Mode.Normal, "\uE7E8", Color.FromRgb(0x6C, 0xB6, 0xFF)), (PcModes.Mode.Gamer, "\uE7FC", Color.FromRgb(0xFF, 0xC4, 0x00)),
            (PcModes.Mode.Turbo, "\uE945", Color.FromRgb(0xFF, 0x8A, 0x00)), (PcModes.Mode.Silencioso, "\uE83F", Color.FromRgb(0x3D, 0xDC, 0x97)),
        })
        {
            var m = mode;
            var (title, sub) = PcModes.Describe(mode);
            modes.Children.Add(ModeTile(glyph, color, title, sub, PcModes.Current == mode, async () =>
            {
                _modeStatus.Text = "Aplicando...";
                await PcModes.ApplyAsync(m);
                BuildExtra();
            }));
        }
        _extra.Children.Add(modes);
        var acts = new System.Windows.Controls.Primitives.UniformGrid { Columns = 5, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var (glyph, text, key, auto) in new[]
        {
            ("\uE945", "Otimizar", "tweaks-all", false), ("\uE7C8", "Gravar", "record", false), ("\uE74D", "Limpar", "cleanup", true),
            ("\uE72E", "Proteção", "security", false), ("\uE8F2", "Suporte", "support", false),
        })
        {
            string k = key; bool a = auto;
            acts.Children.Add(ModeTile(glyph, Color.FromRgb(0xC9, 0xC9, 0xD1), text, "", false, () => { _openApp(); MainWindow.Current?.Go(k, a); return Task.CompletedTask; }));
        }
        _extra.Children.Add(acts);
    }

    private static Border ModeTile(string glyph, Color color, string title, string sub, bool on, Func<Task> click)
    {
        var st = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        st.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 17, Foreground = new SolidColorBrush(color), HorizontalAlignment = HorizontalAlignment.Center });
        st.Children.Add(new TextBlock { Text = title, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
        if (sub.Length > 0) st.Children.Add(new TextBlock { Text = sub, FontSize = 9.5, Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA3)), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap });
        var b = new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(6, 8, 6, 8), Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand,
            Background = new SolidColorBrush(on ? Color.FromArgb(0x40, 0xFF, 0xC4, 0x00) : Color.FromRgb(0x1B, 0x1B, 0x1F)),
            BorderBrush = new SolidColorBrush(on ? Color.FromRgb(0xFF, 0xC4, 0x00) : Color.FromRgb(0x2A, 0x2A, 0x30)), BorderThickness = new Thickness(1), Child = st,
        };
        b.MouseLeftButtonDown += (_, e) => e.Handled = true;   // não começa a arrastar a janela
        b.MouseLeftButtonUp += async (_, e) => { e.Handled = true; await click(); };
        return b;
    }

    private static UIElement Metric(string label, TextBlock value)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 9.5, Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA3)) });
        panel.Children.Add(value);
        return panel;
    }

    private Button IconButton(string glyph, string tip, Action click)
    {
        var b = new Button
        {
            Content = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13 },
            Width = 30, Height = 30, Margin = new Thickness(2, 0, 0, 0), ToolTip = tip, Cursor = Cursors.Hand,
            Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xC9, 0xD1)), Background = Brushes.Transparent, BorderThickness = new Thickness(0),
        };
        var t = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        t.VisualTree = border;
        b.Template = t;
        b.Click += (_, _) => click();
        return b;
    }

    private void HideWidget()
    {
        AppServices.Settings.WidgetEnabled = false;
        AppServices.Settings.Save();
        Close();
    }

    private void Refresh()
    {
        try
        {
            var mem = MemoryService.GetStatus();
            _ram.Text = mem.TotalBytes > 0 ? $"{100 - mem.FreePercent:0}%" : "—";
            _cpu.Text = _usage.Cpu() is double c ? $"{c:0}%" : "…";
            _disk.Text = _usage.Disk() is double d ? $"{d:0}%" : "…";
            var net = _net.Sample();
            _netText.Text = Speed(net.DownloadBytesPerSecond);
            if (_expanded) _modeStatus.Text = PcModes.Status();
            if (_expanded) _modeStatus.Text = PcModes.Status();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { /* uma leitura falhou: tenta no próximo segundo */ }
    }

    private static string Speed(double bytesPerSecond) => bytesPerSecond switch
    {
        >= 1024 * 1024 => $"{bytesPerSecond / (1024 * 1024):0.0} MB/s",
        >= 1024 => $"{bytesPerSecond / 1024:0} KB/s",
        _ => "0 KB/s",
    };
}
