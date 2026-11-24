using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MBExtreme.Core.Social;

namespace MBExtreme.UI;

/// <summary>
/// Vídeo novo do canal: confere o feed do YouTube ao abrir (depois de alguns segundos) e a cada 2 horas. Quando sai vídeo novo, mostra um
/// cartão animado no canto da tela (uma vez por vídeo) e guarda o mais recente para a faixa do Painel.
/// </summary>
internal sealed class VideoWatcher
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromHours(2) };
    public YouTubeVideo? Latest { get; private set; }
    public event Action<YouTubeVideo>? Found;

    public void Start()
    {
        _timer.Tick += async (_, _) => await CheckAsync();
        _timer.Start();
        var first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        first.Tick += async (_, _) => { first.Stop(); await CheckAsync(); };
        first.Start();
    }

    public async Task CheckAsync()
    {
        var settings = AppServices.Settings;
        if (!settings.NotifyNewVideos) return;
        var channel = await ChannelConfig.RefreshAsync(AppServices.Web);
        AppServices.Channel = channel;
        string? known = YouTubeFeed.IsChannelId(channel.YouTubeChannelId) ? channel.YouTubeChannelId : settings.YouTubeChannelId;
        var (id, videos) = await YouTubeFeed.LatestAsync(AppServices.Web, channel.Handle, known);
        if (id is not null && id != settings.YouTubeChannelId) { settings.YouTubeChannelId = id; settings.Save(); }
        if (videos.Count == 0) return;
        Latest = videos[0];
        Found?.Invoke(Latest);
        if (YouTubeFeed.ShouldNotify(Latest, settings.LastNotifiedVideoId, DateTimeOffset.Now))
        {
            settings.LastNotifiedVideoId = Latest.Id;
            settings.Save();
            Toast(Latest);
        }
    }

    /// <summary>Cartão no canto de baixo da tela, que sobe e some sozinho em 12 segundos.</summary>
    private static void Toast(YouTubeVideo video)
    {
        var area = SystemParameters.WorkArea;
        var thumb = new System.Windows.Controls.Image { Width = 120, Height = 68, Stretch = Stretch.UniformToFill };
        try { thumb.Source = new BitmapImage(new Uri(video.Thumbnail)); } catch (UriFormatException) { }
        var text = new System.Windows.Controls.StackPanel { Margin = new Thickness(12, 0, 0, 0), Width = 220, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new System.Windows.Controls.TextBlock { Text = "▶ VÍDEO NOVO NO CANAL", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)) });
        text.Children.Add(new System.Windows.Controls.TextBlock { Text = video.Title, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, MaxHeight = 54, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new System.Windows.Controls.TextBlock { Text = "Clique para assistir", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x96)) });
        var row = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        row.Children.Add(new System.Windows.Controls.Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = thumb });
        row.Children.Add(text);
        var card = new System.Windows.Controls.Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x17)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xC4, 0x00)), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14), Padding = new Thickness(12), Child = row, Cursor = System.Windows.Input.Cursors.Hand,
        };
        var window = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false, Topmost = true, ShowActivated = false,
            SizeToContent = SizeToContent.WidthAndHeight, Content = card, Left = area.Right - 400, Top = area.Bottom - 120,
        };
        card.MouseLeftButtonUp += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(video.Url) { UseShellExecute = true }); } catch (System.ComponentModel.Win32Exception) { }
            window.Close();
        };
        window.Loaded += (_, _) =>
        {
            window.Left = area.Right - window.ActualWidth - 16;
            window.Top = area.Bottom - window.ActualHeight - 16;
            Motion.Enter(card, fromY: 30);
        };
        window.Show();
        var close = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        close.Tick += (_, _) => { close.Stop(); window.Close(); };
        close.Start();
    }
}
