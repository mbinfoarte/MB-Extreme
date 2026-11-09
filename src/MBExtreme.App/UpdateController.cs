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
/// Atualização dentro do programa: pergunta ao servidor e ao GitHub pela compilação mais nova (ao abrir, a cada 15 minutos e sempre que você volta para a janela), mostra um aviso no alto,
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
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(15) };
    private UpdateManifest? _manifest;
    private bool _busy;
    private DateTime _lastCheck = DateTime.MinValue;

    public UpdateController(Grid root, FrameworkElement content)
    {
        _host = root;
        _content = content;
        _contentMargin = content.Margin;
        Build();
        _timer.Tick += async (_, _) => await CheckAsync();
        _timer.Start();
        HookWindowActivation();
    }

    /// <summary>Checa também sempre que a pessoa volta para a janela do MB Extreme (no máximo uma vez a cada 5 minutos), para avisar quase na hora.</summary>
    private void HookWindowActivation()
    {
        void Hook()
        {
            if (Window.GetWindow(_host) is { } window) window.Activated += async (_, _) => await OnActivatedAsync();
        }
        if (Window.GetWindow(_host) is not null) Hook();
        else _host.Loaded += (_, _) => Hook();
    }

    private async Task OnActivatedAsync()
    {
        if (_busy || _ready is not null || _lock.Visibility == Visibility.Visible) return;
        if (DateTime.Now - _lastCheck < TimeSpan.FromMinutes(5)) return;
        await CheckAsync();
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
        _later.Click += (_, _) => { _banner.Visibility = Visibility.Collapsed; _content.Margin = _contentMargin; };   // baixada: continua instalando ao fechar

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

    private string? _ready;          // instalador já baixado e conferido, esperando para instalar
    private bool _installOnExit;

    /// <summary>
    /// Procura a compilação mais nova: no servidor (se houver) e nas Releases públicas do GitHub; fica com a maior.
    /// </summary>
    private static async Task<UpdateManifest?> FindAsync()
    {
        UpdateManifest? best = null;
        string url = AppServices.License.Config.ServerUrl;
        if (url.Length > 0) best = await AppUpdates.CheckAsync(Http, url);
        bool beta = AppServices.Settings.BetaUpdates || AppInfo.Channel == "Beta";
        var github = await GitHubReleases.CheckAsync(Http, AppInfo.UpdateRepository, beta);
        if (github is not null && (best is null || github.Build > best.Build))
            best = best is null ? github : github with { MinimumBuild = Math.Max(github.MinimumBuild, best.MinimumBuild) };
        return best;
    }

    /// <summary>Ao abrir, a cada 15 minutos e ao voltar para a janela: atualização obrigatória cobre a tela; as outras são baixadas sozinhas (ou só avisadas).</summary>
    public async Task CheckAsync()
    {
        if (_busy) return;
        _lastCheck = DateTime.Now;
        var manifest = await FindAsync();
        if (manifest is null) return;
        _manifest = manifest;
        var state = AppUpdates.Evaluate(AppUpdates.CurrentBuild(), manifest);
        string news = string.IsNullOrWhiteSpace(manifest.Notes) ? "" : "\n" + Shorten(manifest.Notes);
        switch (state)
        {
            case UpdateState.Required:
                _lockText.Text = $"Esta versão ({AppInfo.DisplayVersion}, compilação {AppUpdates.CurrentBuild()}) não é mais aceita. Atualize para a compilação {manifest.Build} para continuar usando o MB Extreme." + news;
                if (_lock.Visibility != Visibility.Visible) { _lock.Visibility = Visibility.Visible; Motion.Enter(_lock, fromY: 0); }
                break;
            case UpdateState.Available when AppServices.Settings.AutoUpdate:
                await DownloadQuietlyAsync(manifest);
                break;
            case UpdateState.Available:
                ShowBanner($"Nova versão disponível: compilação {manifest.Build}." + news, "Atualizar agora");
                break;
        }
    }

    private static string Shorten(string notes)
    {
        var lines = notes.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(3).ToList();
        return string.Join("\n", lines) + (notes.Count(c => c == '\n') > 3 ? "\n..." : "");
    }

    private void ShowBanner(string text, string button)
    {
        _bannerText.Text = text;
        _now.Content = button;
        if (_banner.Visibility != Visibility.Visible) { _banner.Visibility = Visibility.Visible; Motion.Enter(_banner, fromY: -24); }
        _content.Margin = new Thickness(_contentMargin.Left, _contentMargin.Top + 56, _contentMargin.Right, _contentMargin.Bottom);
    }

    /// <summary>"Atualizar sozinho": baixa em segundo plano (com conferência do SHA-256) e oferece reiniciar agora ou instalar ao fechar.</summary>
    private async Task DownloadQuietlyAsync(UpdateManifest manifest)
    {
        if (_ready is not null) { ShowReady(manifest); return; }
        _busy = true;
        try
        {
            string folder = Path.Combine(Path.GetTempPath(), "MBExtreme-update");
            string cached = Path.Combine(folder, $"MBExtreme-Beta-Setup-{manifest.Build}.exe");
            _ready = File.Exists(cached) && AppUpdates.Sha256Of(cached) == manifest.Sha256
                ? cached
                : await AppUpdates.DownloadAsync(Http, manifest, folder, null, CancellationToken.None);
            ShowReady(manifest);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException or UnauthorizedAccessException)
        {
            // falhou em silêncio: na próxima verificação tenta de novo; o aviso manual continua disponível
            ShowBanner($"Nova versão disponível: compilação {manifest.Build}.", "Atualizar agora");
        }
        finally { _busy = false; }
    }

    private void ShowReady(UpdateManifest manifest)
    {
        ShowBanner($"Atualização pronta: compilação {manifest.Build}. Clique em \"Reiniciar e atualizar\" ou ela instala sozinha quando você fechar o programa.", "Reiniciar e atualizar");
        _installOnExit = true;
        _later.Content = "Ao fechar";
    }

    /// <summary>Chamado ao fechar o programa: com atualização baixada, abre o instalador em modo de atualização.</summary>
    public void InstallPendingOnExit()
    {
        if (!_installOnExit || _ready is null || !File.Exists(_ready)) return;
        StartInstaller(_ready);
    }

    private void StartInstaller(string file)
    {
        if (_manifest is { Notes.Length: > 0 } m)
        {
            AppServices.Settings.LastUpdateNotes = m.Notes;   // "O que há de novo" mostra o texto da Release depois de atualizar
            AppServices.Settings.Save();
        }
        string folder = AppContext.BaseDirectory;
        Process.Start(new ProcessStartInfo(file, MBExtreme.Core.Setup.UpdateArgs.Build(folder)) { UseShellExecute = true });
    }

    internal enum CheckOutcome { UpToDate, Available, Offline }

    internal sealed record UpdateCheckResult(CheckOutcome Outcome, int NewBuild, string Notes);

    /// <summary>Verificação pedida pelo usuário (rodapé do menu): sempre responde, mesmo quando não há nada novo.</summary>
    public async Task<UpdateCheckResult> CheckNowAsync()
    {
        var manifest = await FindAsync();
        if (manifest is null) return new UpdateCheckResult(CheckOutcome.Offline, 0, "");
        var state = AppUpdates.Evaluate(AppUpdates.CurrentBuild(), manifest);
        if (state == UpdateState.UpToDate) return new UpdateCheckResult(CheckOutcome.UpToDate, 0, "");
        await CheckAsync();   // mostra o aviso no alto da tela
        return new UpdateCheckResult(CheckOutcome.Available, manifest.Build, manifest.Notes);
    }

    /// <summary>Janela de resultado da verificação, no tema do programa (igual ao "O que há de novo"), no lugar da caixa cinza do Windows.</summary>
    public void ShowCheckResult(Window owner, UpdateCheckResult result)
    {
        string current = $"Você está na versão {AppInfo.Version} ({AppInfo.DisplayVersion}), compilação {AppUpdates.CurrentBuild()}.";
        var (glyph, title, message) = result.Outcome switch
        {
            CheckOutcome.Available => ("⬆", "Atualização disponível",
                $"Há uma versão nova: compilação {result.NewBuild}." + (AppServices.Settings.AutoUpdate
                    ? " Ela já está sendo baixada; o aviso para reiniciar e atualizar apareceu no alto da tela."
                    : " O aviso para instalar apareceu no alto da tela.")),
            CheckOutcome.Offline => ("⚠", "Não deu para verificar",
                "Não foi possível ver se há versão nova agora (sem internet, ou as versões ainda não estão públicas no GitHub). Tente de novo mais tarde."),
            _ => ("✓", "Tudo em dia", "Você já está com a versão mais nova."),
        };
        Brush accent = result.Outcome == CheckOutcome.Offline ? Res("WarnBrush") : Res("AccentBrush");

        var head = new Border
        {
            Background = accent, Padding = new Thickness(22, 16, 22, 14),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new TextBlock { Text = glyph, FontSize = 26, Foreground = Brushes.Black, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) },
                    new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Brushes.Black, VerticalAlignment = VerticalAlignment.Center },
                },
            },
        };

        var body = new StackPanel { Margin = new Thickness(22, 18, 22, 8) };
        body.Children.Add(new TextBlock { Text = message, FontSize = 14, TextWrapping = TextWrapping.Wrap, Foreground = Res("TextBrush") });
        body.Children.Add(new TextBlock { Text = current, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Res("MutedBrush"), Margin = new Thickness(0, 12, 0, 0) });
        if (result.Outcome == CheckOutcome.Available && !string.IsNullOrWhiteSpace(result.Notes))
        {
            body.Children.Add(new TextBlock { Text = "O que vem nela:", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Res("TextBrush"), Margin = new Thickness(0, 14, 0, 6) });
            foreach (var line in result.Notes.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries)
                         .Where(l => !l.TrimStart().StartsWith('#')).Select(l => l.TrimStart('-', '*', '•', ' ')).Where(l => l.Length > 0).Take(8))
                body.Children.Add(new TextBlock { Text = "•  " + line, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Foreground = Res("TextBrush"), Margin = new Thickness(0, 0, 0, 5) });
        }

        var window = new Window
        {
            Title = "Atualização", Width = 520, SizeToContent = SizeToContent.Height, Owner = owner,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Background = Res("SurfaceBrush"), ResizeMode = ResizeMode.NoResize,
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(22, 8, 22, 16) };
        if (result.Outcome == CheckOutcome.Available)
        {
            var update = new Button { Content = "Atualizar agora", Style = (Style)Application.Current.FindResource("PrimaryButton"), Padding = new Thickness(20, 7, 20, 7) };
            update.Click += async (_, _) => { window.Close(); await InstallAsync(_bannerProgress); };
            buttons.Children.Add(update);
            var later = new Button { Content = "Depois", Style = (Style)Application.Current.FindResource("GhostButton"), Padding = new Thickness(18, 7, 18, 7), Margin = new Thickness(8, 0, 0, 0) };
            later.Click += (_, _) => window.Close();
            buttons.Children.Add(later);
        }
        else
        {
            var ok = new Button { Content = "OK", Style = (Style)Application.Current.FindResource("PrimaryButton"), Padding = new Thickness(26, 7, 26, 7) };
            ok.Click += (_, _) => window.Close();
            buttons.Children.Add(ok);
        }

        var root = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(head);
        root.Children.Add(buttons);
        root.Children.Add(body);
        window.Content = root;
        window.ShowDialog();
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
            string file = _ready ?? await AppUpdates.DownloadAsync(Http, _manifest, Path.Combine(Path.GetTempPath(), "MBExtreme-update"),
                new Progress<double>(p => Motion.To(progress, System.Windows.Controls.Primitives.RangeBase.ValueProperty, p, Motion.Fast)), CancellationToken.None);
            if (MessageBox.Show("O MB Extreme vai fechar, instalar a versão nova e abrir de novo sozinho. Continuar?", AppInfo.Name, MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            _installOnExit = false;
            StartInstaller(file);
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
