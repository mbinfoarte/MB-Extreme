using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MBExtreme.Core.Diagnostics;
using MBExtreme.Core.Memory;

namespace MBExtreme.UI.Widget;

/// <summary>
/// Painel MB que abre ao clicar no ícone da bandeja (ao lado do relógio): nota de saúde, temperaturas, RAM e ping, e atalhos que ligam e
/// desligam (Modo jogo) ou abrem a ferramenta no programa. Fecha ao clicar fora.
/// </summary>
public sealed class TrayPanel : Window
{
    private static readonly Color Yellow = Color.FromRgb(0xFF, 0xC4, 0x00);
    private static readonly Color Green = Color.FromRgb(0x8F, 0xD6, 0x94);
    private readonly TextBlock _cpu = Big(), _gpu = Big(), _ram = Big(), _ping = Big(), _score = new() { FontSize = 26, FontWeight = FontWeights.Bold };
    private readonly TextBlock _status = new() { FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xA8)) };
    private readonly Border _alert = new() { Visibility = Visibility.Collapsed };
    private readonly WrapPanel _toggles = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Action<string, bool> _go;
    private readonly Action _openApp;
    private bool _closing;

    private static TextBlock Big() => new() { Text = "…", FontSize = 17, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center };

    /// <param name="go">Abre uma página do programa (chave, iniciar sozinho).</param>
    public TrayPanel(Action<string, bool> go, Action openApp)
    {
        _go = go;
        _openApp = openApp;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        Title = "Painel MB";
        Content = Build();
        Deactivated += (_, _) => { if (!_closing) { _closing = true; Close(); } };
        Closed += (_, _) => { _timer.Stop(); AppServices.Boost.Changed -= OnBoostChanged; };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - 12;
            Top = area.Bottom - ActualHeight - 12;
            _ = RefreshAsync();
            _timer.Start();
        };
        _timer.Tick += async (_, _) => await RefreshAsync();
        AppServices.Boost.Changed += OnBoostChanged;
    }

    private void OnBoostChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(BuildToggles));

    private UIElement Build()
    {
        var stack = new StackPanel();
        // topo: logo, nome, situação e nota
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var logo = new Border
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Margin = new Thickness(0, 0, 10, 0),
            Background = new LinearGradientBrush(Color.FromRgb(0xFF, 0xD8, 0x4D), Color.FromRgb(0xFF, 0x8A, 0x00), 45),
            Child = new TextBlock { Text = "MB", FontWeight = FontWeights.ExtraBold, FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(0x0B, 0x0C, 0x0E)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        head.Children.Add(logo);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = "MB Extreme", FontSize = 14, FontWeight = FontWeights.Bold });
        names.Children.Add(_status);
        Grid.SetColumn(names, 1);
        head.Children.Add(names);
        _score.VerticalAlignment = VerticalAlignment.Center;
        _score.ToolTip = "Nota de saúde da última análise do PC";
        Grid.SetColumn(_score, 2);
        head.Children.Add(_score);
        stack.Children.Add(head);

        // leituras
        var tiles = new UniformGrid4();
        tiles.Add(Tile(_cpu, "CPU"));
        tiles.Add(Tile(_gpu, "GPU"));
        tiles.Add(Tile(_ram, "RAM"));
        tiles.Add(Tile(_ping, "Ping"));
        stack.Children.Add(tiles.Panel);

        BuildToggles();
        stack.Children.Add(_toggles);
        stack.Children.Add(_alert);

        var foot = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var open = LinkButton("Abrir o MB Extreme", () => { Close(); _openApp(); });
        DockPanel.SetDock(open, Dock.Right);
        foot.Children.Add(open);
        foot.Children.Add(new TextBlock { Text = "Esc ou clique fora para fechar", FontSize = 10.5, Foreground = new SolidColorBrush(Color.FromRgb(0x7E, 0x84, 0x8D)), VerticalAlignment = VerticalAlignment.Center });
        stack.Children.Add(foot);

        var glass = new Border
        {
            CornerRadius = new CornerRadius(14), Padding = new Thickness(18), Child = stack,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
            Background = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(0xF0, 0x26, 0x29, 0x30), 0),
                new GradientStop(Color.FromArgb(0xF6, 0x14, 0x15, 0x19), 0.45),
                new GradientStop(Color.FromArgb(0xFA, 0x08, 0x08, 0x0A), 1),
            }, 90),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.6, Color = Colors.Black },
        };
        TextElement.SetForeground(glass, new SolidColorBrush(Color.FromRgb(0xF2, 0xF3, 0xF5)));
        TextElement.SetFontFamily(glass, new FontFamily("Segoe UI"));
        return new Border { Padding = new Thickness(10), Child = glass };
    }

    private sealed class UniformGrid4
    {
        public readonly System.Windows.Controls.Primitives.UniformGrid Panel = new() { Columns = 4, Margin = new Thickness(0, 14, 0, 4) };
        public void Add(UIElement e) => Panel.Children.Add(e);
    }

    private static Border Tile(TextBlock value, string label)
    {
        var s = new StackPanel();
        s.Children.Add(value);
        s.Children.Add(new TextBlock { Text = label, FontSize = 10.5, Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xA8)), HorizontalAlignment = HorizontalAlignment.Center });
        return new Border
        {
            Child = s, Margin = new Thickness(3), Padding = new Thickness(4, 9, 4, 8), CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
        };
    }

    private void BuildToggles()
    {
        _toggles.Children.Clear();
        _toggles.Margin = new Thickness(0, 6, 0, 0);
        bool game = AppServices.Boost.IsActive;
        _toggles.Children.Add(Toggle("", game ? "Modo jogo: ligado" : "Modo jogo", game, async () =>
        {
            if (AppServices.Boost.IsActive) await AppServices.Boost.DeactivateAsync();
            else await AppServices.Boost.ActivateNowAsync();
            _status.Text = AppServices.Boost.IsActive ? AppServices.Boost.LastReport?.Summary ?? "Modo jogo ligado." : "Modo jogo desligado: tudo voltou ao normal.";
        }));
        _toggles.Children.Add(Toggle("", "Otimizar", false, () => { Close(); _go("tweaks-all", false); return Task.CompletedTask; }));
        _toggles.Children.Add(Toggle("", "Gravar tela", false, () => { Close(); _go("record", false); return Task.CompletedTask; }));
        _toggles.Children.Add(Toggle("", "Limpeza", false, () => { Close(); _go("cleanup", true); return Task.CompletedTask; }));
        _toggles.Children.Add(Toggle("", "Proteção", false, () => { Close(); _go("security", false); return Task.CompletedTask; }));
        _toggles.Children.Add(Toggle("", "Suporte", false, () => { Close(); _go("support", false); return Task.CompletedTask; }));
    }

    private static Button Toggle(string glyph, string text, bool on, Func<Task> click)
    {
        var s = new StackPanel();
        s.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 16, Margin = new Thickness(0, 0, 0, 8) });
        s.Children.Add(new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var border = new Border
        {
            Width = 116, Height = 72, Padding = new Thickness(11, 10, 8, 8), CornerRadius = new CornerRadius(10), Child = s,
            Background = on ? new LinearGradientBrush(Color.FromArgb(0x3C, 0xFF, 0xE2, 0x7A), Color.FromArgb(0x1F, 0xFF, 0xC4, 0x00), 90) : new SolidColorBrush(Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF)),
            BorderBrush = on ? new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xC4, 0x00)) : new SolidColorBrush(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
        };
        var button = new Button { Content = border, Margin = new Thickness(3), Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Foreground = on ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xC9, 0xCD, 0xD3)) };
        button.Template = Plain();
        button.Click += async (_, _) => await click();
        return button;
    }

    private static ControlTemplate Plain()
    {
        var t = new ControlTemplate(typeof(Button));
        t.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter));
        return t;
    }

    private static Button LinkButton(string text, Action click)
    {
        var b = new Button { Content = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Yellow) }, Cursor = Cursors.Hand, Template = Plain() };
        b.Click += (_, _) => click();
        return b;
    }

    // ------------------------------------------------------------ leituras

    private int _pingTick;

    private async Task RefreshAsync()
    {
        try
        {
            var mem = MemoryService.GetStatus();
            _ram.Text = mem.TotalBytes > 0 ? $"{100 - mem.FreePercent:0}%" : "—";
            _ram.Foreground = new SolidColorBrush(Yellow);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }

        try
        {
            var sensors = await AppServices.Sensors;
            var snap = await Task.Run(sensors.Read);
            Temp(_cpu, snap.CpuTemperature);
            Temp(_gpu, snap.GpuTemperature);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or AggregateException) { }

        if (_pingTick++ % 3 == 0)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync("1.1.1.1", 1500);
                _ping.Text = reply.Status == IPStatus.Success ? $"{reply.RoundtripTime} ms" : "—";
                _ping.Foreground = new SolidColorBrush(reply.Status == IPStatus.Success && reply.RoundtripTime < 60 ? Green : Yellow);
            }
            catch (Exception ex) when (ex is PingException or InvalidOperationException or System.Net.Sockets.SocketException) { _ping.Text = "—"; }
        }

        if (_score.Text.Length == 0) ShowHealth();
    }

    private static void Temp(TextBlock text, double? celsius)
    {
        text.Text = celsius is { } c ? $"{c:0}°" : "—";
        text.Foreground = new SolidColorBrush(celsius is >= 85 ? Color.FromRgb(0xFF, 0x8A, 0x8A) : celsius is >= 75 ? Yellow : Green);
        text.ToolTip = celsius is null ? "Temperatura indisponível (abra o MB Extreme como administrador)" : null;
    }

    private void ShowHealth()
    {
        var last = new DiagnosticHistory().Load().OrderByDescending(s => s.Started).FirstOrDefault();
        if (last is null)
        {
            _score.Text = "—";
            _status.Text = AppServices.Boost.IsActive ? "Modo jogo ligado" : "Faça uma análise no Painel para ver a nota";
            return;
        }
        int score = Math.Clamp(100 - last.Critical * 25 - last.Problems * 12 - last.Warnings * 5, 0, 100);
        _score.Text = score.ToString();
        _score.Foreground = new SolidColorBrush(HealthScore.Level(score) switch { 2 => Green, 1 => Yellow, _ => Color.FromRgb(0xFF, 0x8A, 0x8A) });
        _status.Text = AppServices.Boost.IsActive ? "Modo jogo ligado · " + HealthScore.Headline(score) : HealthScore.Headline(score);
        if (last.Titles.Count > 0)
        {
            _alert.Visibility = Visibility.Visible;
            _alert.Margin = new Thickness(3, 8, 3, 0);
            _alert.Padding = new Thickness(12, 9, 12, 9);
            _alert.CornerRadius = new CornerRadius(10);
            _alert.Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0x8A, 0x1E));
            _alert.BorderBrush = new SolidColorBrush(Color.FromArgb(0x59, 0xFF, 0x8A, 0x1E));
            _alert.BorderThickness = new Thickness(1);
            var row = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
            row.Inlines.Add(new System.Windows.Documents.Run("Aviso  ") { Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x1E)), FontWeight = FontWeights.Bold });
            row.Inlines.Add(new System.Windows.Documents.Run(last.Titles[0] + (last.Titles.Count > 1 ? $" (+{last.Titles.Count - 1})" : "")));
            var button = new Button { Content = row, Template = Plain(), Cursor = Cursors.Hand, ToolTip = "Abrir o Painel" };
            button.Click += (_, _) => { Close(); _go("home", false); };
            _alert.Child = button;
        }
    }
}
