using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MBExtreme.Core;
using MBExtreme.Core.Update;

namespace MBExtreme.UI;

/// <summary>
/// Atualização dentro do programa: pergunta ao servidor pela compilação mais nova (ao abrir e a cada 6 horas), mostra um aviso no alto,
/// e, quando a versão instalada é mais antiga que a mínima aceita, cobre a tela e só deixa atualizar. Baixa, confere o SHA-256 e abre o instalador.
/// </summary>
internal sealed class UpdateController
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly Grid _host;
    private readonly FrameworkElement _content;
    private readonly Thickness _contentMargin;
    private readonly Border _banner = new() { Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 34, 10, 0), Padding = new Thickness(16, 10, 16, 10), CornerRadius = new CornerRadius(12), HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _bannerText = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
    private readonly ProgressBar _bannerProgress = new() { Height = 4, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
    private readonly Button _now = new() { Content = "Atualizar agora", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(12, 0, 0, 0) };
    private readonly Button _later = new() { Content = "Depois", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(8, 0, 0, 0) };
    private readonly Border _lock = new() { Visibility = Visibility.Collapsed };
    private readonly TextBlock _lockText = new() { TextWrapping = TextWrapping.Wrap, FontSize = 14, TextAlignment = TextAlignment.Center, MaxWidth = 460, Margin = new Thickness(0, 10, 0, 16) };
    private readonly ProgressBar _lockProgress = new() { Height = 6, Width = 320, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 12) };
    private readonly Button _lockButton = new() { Content = "Atualizar agora", Padding = new Thickness(26, 9, 26, 9) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromHours(6) };
    private UpdateManifest? _manifest;
    private bool _busy;

    public UpdateController(Grid root, FrameworkElement content)
    {
        _host = root;
        _content = content;
        _contentMargin = content.Margin;
        Build();
        _timer.Tick += async (_, _) => await CheckAsync();
        _timer.Start();
    }

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    private void Build()
    {
        _banner.Background = Res("Surface2Brush");
        _banner.BorderBrush = Res("AccentBrush");
        _banner.BorderThickness = new Thickness(1);
        _now.Style = (Style)Application.Current.FindResource("PrimaryButton");
        _later.Style = (Style)Application.Current.FindResource("GhostButton");
        _lockButton.Style = (Style)Application.Current.FindResource("PrimaryButton");
        var row = new DockPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(_now);
        buttons.Children.Add(_later);
        DockPanel.SetDock(buttons, Dock.Right);
        row.Children.Add(buttons);
        var text = new StackPanel();
        text.Children.Add(_bannerText);
        text.Children.Add(_bannerProgress);
        row.Children.Add(text);
        _banner.Child = row;
        Grid.SetColumn(_banner, 1);
        Panel.SetZIndex(_banner, 50);
        _host.Children.Add(_banner);

        _now.Click += async (_, _) => await InstallAsync(_bannerProgress);
        _later.Click += (_, _) => { _banner.Visibility = Visibility.Collapsed; _content.Margin = _contentMargin; };

        // tela de atualização obrigatória
        _lock.Background = new SolidColorBrush(Color.FromArgb(245, 8, 8, 10));
        var box = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        box.Children.Add(new TextBlock { Text = "Atualização necessária", FontSize = 24, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
        box.Children.Add(_lockText);
        box.Children.Add(_lockProgress);
        box.Children.Add(_lockButton);
        _lock.Child = box;
        Grid.SetColumn(_lock, 1);
        Panel.SetZIndex(_lock, 60);
        _host.Children.Add(_lock);
        _lockButton.Click += async (_, _) => await InstallAsync(_lockProgress);
    }

    /// <summary>Pergunta ao servidor e mostra o aviso (ou a tela obrigatória) se houver compilação nova.</summary>
    public async Task CheckAsync()
    {
        if (_busy) return;
        string url = AppServices.License.Config.ServerUrl;
        if (url.Length == 0) return;
        var manifest = await AppUpdates.CheckAsync(Http, url);
        if (manifest is null) return;
        _manifest = manifest;
        var state = AppUpdates.Evaluate(AppUpdates.CurrentBuild(), manifest);
        string news = string.IsNullOrWhiteSpace(manifest.Notes) ? "" : "\n" + manifest.Notes.Trim();
        switch (state)
        {
            case UpdateState.Required:
                _lockText.Text = $"Esta versão ({AppInfo.DisplayVersion}, compilação {AppUpdates.CurrentBuild()}) não é mais aceita. Atualize para a compilação {manifest.Build} para continuar usando o MB Extreme." + news;
                _lock.Visibility = Visibility.Visible;
                break;
            case UpdateState.Available:
                _bannerText.Text = $"Nova versão disponível: compilação {manifest.Build}." + news;
                _banner.Visibility = Visibility.Visible;
                _content.Margin = new Thickness(_contentMargin.Left, _contentMargin.Top + 56, _contentMargin.Right, _contentMargin.Bottom);
                break;
        }
    }

    /// <summary>Verificação pedida pelo usuário (rodapé do menu): sempre responde, mesmo quando não há nada novo.</summary>
    public async Task<string> CheckNowAsync()
    {
        string current = $"Você está na versão {AppInfo.Version} ({AppInfo.DisplayVersion}), compilação {AppUpdates.CurrentBuild()}.";
        string url = AppServices.License.Config.ServerUrl;
        if (url.Length == 0) return current + "\n\nEsta cópia não tem servidor configurado para procurar atualizações. As novas compilações saem na página de downloads.";
        var manifest = await AppUpdates.CheckAsync(Http, url);
        if (manifest is null) return current + "\n\nNão foi possível falar com o servidor agora. Confira a internet e tente de novo.";
        var state = AppUpdates.Evaluate(AppUpdates.CurrentBuild(), manifest);
        if (state == UpdateState.UpToDate) return current + "\n\nVocê já está com a versão mais nova.";
        await CheckAsync();   // mostra o aviso (ou a tela obrigatória) com o botão de instalar
        return current + $"\n\nHá uma compilação nova ({manifest.Build}). O aviso para instalar apareceu no alto da tela.";
    }

    private async Task InstallAsync(ProgressBar progress)
    {
        if (_busy || _manifest is null) return;
        _busy = true;
        _now.IsEnabled = _lockButton.IsEnabled = false;
        progress.Visibility = Visibility.Visible;
        progress.Maximum = 1;
        try
        {
            string folder = Path.Combine(Path.GetTempPath(), "MBExtreme-update");
            string file = await AppUpdates.DownloadAsync(Http, _manifest, folder, new Progress<double>(p => progress.Value = p), CancellationToken.None);
            if (MessageBox.Show("O MB Extreme vai fechar e abrir o instalador da nova versão. Continuar?", AppInfo.Name, MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
            Application.Current.Shutdown();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            MessageBox.Show("Não foi possível atualizar agora:\n\n" + ex.Message + "\n\nTente de novo em instantes.", AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _busy = false;
            _now.IsEnabled = _lockButton.IsEnabled = true;
            progress.Visibility = Visibility.Collapsed;
        }
    }
}
