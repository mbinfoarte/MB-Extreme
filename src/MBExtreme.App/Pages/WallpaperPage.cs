using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MBExtreme.Core.Wallpaper;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Papel de parede: modelos do MB Extreme, galeria online com licença de uso (Pexels) e imagens do seu PC.
/// Aplica no Windows por monitor, com modo de ajuste, na resolução de cada monitor.
/// </summary>
public sealed class WallpaperPage : ToolPageBase
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "wallpapers");

    private IReadOnlyList<WallpaperMonitor> _monitors = Array.Empty<WallpaperMonitor>();
    private string? _targetMonitorId;   // nulo = todos os monitores
    private WallpaperFit _fit = WallpaperFit.Fill;
    private string _tab = "mb";

    private Func<int, int, Task<string?>>? _resolve;   // devolve o caminho do arquivo para aplicar, na resolução pedida
    private string _selectionName = "";
    private readonly Border _preview = new() { Height = 150, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x18)), BorderThickness = new Thickness(1) };
    private readonly Image _previewImage = new() { Stretch = Stretch.UniformToFill };
    private readonly StackPanel _monitorChips = new() { Orientation = Orientation.Horizontal };
    private readonly WrapPanel _fitChips = new();
    private readonly WrapPanel _tabs = new();
    private readonly WrapPanel _grid = new();
    private readonly TextBox _search = new() { Width = 260, Padding = new Thickness(8, 5, 8, 5) };
    private Button? _apply;

    public WallpaperPage() : base("Papel de parede",
        "Deixe a área de trabalho com a cara do MB Extreme, ou escolha uma imagem licenciada ou do seu PC. Aplica no Windows em cada monitor, do jeito que você quiser e na resolução da sua tela.")
    {
        _fit = (WallpaperFit)Math.Clamp(AppServices.Settings.WallpaperFit, 0, 5);
        _preview.Child = _previewImage;
        _preview.BorderBrush = Brush("Surface2Brush");
        Loaded += (_, _) => Build();
    }

    private void Build()
    {
        Body.Children.Clear();
        _monitors = SafeMonitors();

        Body.Children.Add(Heading("Onde aplicar"));
        BuildMonitorChips();
        Body.Children.Add(_monitorChips);
        if (_monitors.Count == 0)
            Body.Children.Add(Muted("Não consegui ler os monitores agora; o papel vai para todos. Em Windows mais antigo, o modo por monitor pode não existir.", 11.5));

        Body.Children.Add(Muted("Ajuste da imagem:", 12));
        BuildFitChips();
        Body.Children.Add(_fitChips);

        BuildTabs();
        Body.Children.Add(_tabs);

        // prévia + aplicar
        var bottom = new Grid { Margin = new Thickness(0, 12, 0, 12) };
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        bottom.ColumnDefinitions.Add(new ColumnDefinition());
        _preview.Width = 230;
        if (_preview.Parent is Panel pp) pp.Children.Remove(_preview);
        bottom.Children.Add(_preview);
        var right = new StackPanel { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(new TextBlock { Text = "Escolha um modelo ou imagem abaixo e clique em Aplicar.", FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = Brush("MutedBrush") });
        _apply = new Button { Content = "Aplicar no Windows", Style = (Style)FindResource("PrimaryButton"), Padding = new Thickness(20, 8, 20, 8), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = false };
        _apply.Click += async (_, _) => await ApplyAsync();
        right.Children.Add(_apply);
        Grid.SetColumn(right, 1);
        bottom.Children.Add(right);
        Body.Children.Add(bottom);

        if (_grid.Parent is Panel gp) gp.Children.Remove(_grid);
        Body.Children.Add(_grid);
        ShowTab(_tab);
    }

    private static IReadOnlyList<WallpaperMonitor> SafeMonitors()
    {
        try { return DesktopWallpaper.Monitors(); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { return Array.Empty<WallpaperMonitor>(); }
    }

    private void BuildMonitorChips()
    {
        _monitorChips.Children.Clear();
        AddChip(_monitorChips, _monitors.Count > 1 ? "Todos os monitores" : "Meu monitor", _targetMonitorId is null, () => { _targetMonitorId = null; BuildMonitorChips(); });
        foreach (var m in _monitors)
        {
            var mon = m;
            AddChip(_monitorChips, $"{m.Name} · {m.Resolution}" + (m.Primary ? " (principal)" : ""), _targetMonitorId == m.Id, () => { _targetMonitorId = mon.Id; BuildMonitorChips(); });
        }
    }

    private void BuildFitChips()
    {
        _fitChips.Children.Clear();
        foreach (WallpaperFit fit in Enum.GetValues(typeof(WallpaperFit)))
        {
            var f = fit;
            if (fit == WallpaperFit.Span && _monitors.Count < 2) continue;   // "esticar nos monitores" só com 2+
            AddChip(_fitChips, WallpaperFitText.Label(fit), _fit == fit, () =>
            {
                _fit = f;
                AppServices.Settings.WallpaperFit = (int)f;
                AppServices.Settings.Save();
                BuildFitChips();
            });
        }
    }

    private void BuildTabs()
    {
        _tabs.Margin = new Thickness(0, 12, 0, 0);
        _tabs.Children.Clear();
        foreach (var (key, label) in new[] { ("mb", "MB Extreme"), ("online", "Galeria online"), ("pc", "Do meu PC") })
        {
            var k = key;
            var tab = new ToggleButton { Style = (Style)FindResource("ChipToggle"), Content = label, IsChecked = key == _tab, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(14, 6, 14, 6) };
            tab.Click += (_, _) => ShowTab(k);
            _tabs.Children.Add(tab);
        }
    }

    private void AddChip(Panel host, string text, bool on, Action click)
    {
        var chip = new ToggleButton { Style = (Style)FindResource("ChipToggle"), Content = text, IsChecked = on, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(12, 4, 12, 4), FontSize = 12 };
        chip.Click += (_, _) => click();
        host.Children.Add(chip);
    }

    private void ShowTab(string key)
    {
        _tab = key;
        foreach (var t in _tabs.Children.OfType<ToggleButton>()) t.IsChecked = (t.Content as string) == (key == "mb" ? "MB Extreme" : key == "online" ? "Galeria online" : "Do meu PC");
        _grid.Children.Clear();
        switch (key)
        {
            case "online": ShowOnline(); break;
            case "pc": ShowPc(); break;
            default: ShowMb(); break;
        }
    }

    // ---------------------------------------------------------------- MB Extreme
    private void ShowMb()
    {
        _grid.Children.Add(FullWidth(Muted("Modelos do MB Extreme, desenhados na resolução da sua tela (sem perder qualidade).", 11.5)));
        double aspect = TargetAspect();
        foreach (var model in MbWallpaperArt.All)
        {
            var m = model;
            BitmapSource preview;
            try { preview = MbWallpaperArt.Preview(model, aspect); }
            catch (Exception ex) when (ex is InvalidOperationException) { continue; }
            _grid.Children.Add(Thumb(preview, model.Name, () =>
            {
                _selectionName = "MB Extreme · " + m.Name;
                _resolve = (w, h) => Task.FromResult<string?>(MbWallpaperArt.Save(m, w, h, Folder));
                Select(preview, _selectionName);
            }));
        }
    }

    // ---------------------------------------------------------------- Galeria online (Pexels)
    private void ShowOnline()
    {
        string key = AppServices.Settings.PexelsApiKey;
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        System.Windows.Automation.AutomationProperties.SetName(_search, "Procurar papel de parede");
        bar.Children.Add(_search);
        bar.Children.Add(SmallButton("Procurar", async (_, _) => await SearchOnlineAsync(), true));
        _grid.Children.Add(FullWidth(bar));

        if (!Pexels.HasKey(key))
        {
            var box = new StackPanel();
            box.Children.Add(new TextBlock { Text = "A galeria online usa o Pexels, um banco gratuito com licença de uso. Para ligar, cole uma chave da API (grátis) do Pexels:", FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Foreground = Brush("TextBrush") });
            var keyBox = new TextBox { Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 6, 0, 6), Width = 420, HorizontalAlignment = HorizontalAlignment.Left };
            box.Children.Add(keyBox);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(SmallButton("Salvar chave", (_, _) =>
            {
                if (!Pexels.HasKey(keyBox.Text)) { SetStatus("Cole uma chave válida do Pexels.", "WarnBrush"); return; }
                AppServices.Settings.PexelsApiKey = keyBox.Text.Trim();
                AppServices.Settings.Save();
                ShowTab("online");
                SetStatus("Chave salva. Procure uma imagem.", "OkBrush");
            }, true));
            row.Children.Add(SmallButton("Pegar chave grátis (pexels.com/api)", (_, _) => Open(Pexels.GetKeyUrl)));
            box.Children.Add(row);
            box.Children.Add(Muted("As imagens do Pexels são gratuitas para uso, com crédito ao autor (o programa mostra o nome). Nada é cobrado.", 11));
            _grid.Children.Add(FullWidth(CardOf(box)));
        }
    }

    private async Task SearchOnlineAsync()
    {
        string key = AppServices.Settings.PexelsApiKey;
        if (!Pexels.HasKey(key)) { SetStatus("Configure a chave do Pexels primeiro.", "WarnBrush"); return; }
        SetStatus("Procurando no Pexels...");
        IReadOnlyList<OnlinePhoto> photos;
        try { photos = await Pexels.SearchAsync(Http, key, _search.Text, 1, CancellationToken.None); }
        catch (Exception ex) { SetStatus("Não foi possível buscar: " + ex.Message, "BadBrush"); return; }
        // limpa só os cartões de imagem (mantém a barra de busca)
        for (int i = _grid.Children.Count - 1; i >= 1; i--) _grid.Children.RemoveAt(i);
        if (photos.Count == 0) { _grid.Children.Add(FullWidth(Muted("Nada encontrado (ou a chave é inválida). Tente outra palavra.", 12))); return; }
        SetStatus($"{photos.Count} imagens.", "OkBrush");
        foreach (var photo in photos)
        {
            var p = photo;
            var img = new BitmapImage();
            try { img.BeginInit(); img.UriSource = new Uri(photo.ThumbUrl); img.CacheOption = BitmapCacheOption.OnLoad; img.EndInit(); }
            catch (Exception ex) when (ex is UriFormatException or NotSupportedException) { continue; }
            _grid.Children.Add(Thumb(img, "por " + photo.Author, () =>
            {
                _selectionName = "Pexels · por " + p.Author;
                _resolve = async (_, _) => await Pexels.DownloadAsync(Http, p, Folder, CancellationToken.None);
                Select(img, _selectionName);
            }));
        }
    }

    // ---------------------------------------------------------------- Do meu PC
    private void ShowPc()
    {
        var box = new StackPanel();
        box.Children.Add(new TextBlock { Text = "Use uma imagem sua (JPG ou PNG). Para ficar nítida, escolha uma com resolução parecida com a da sua tela.", FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Foreground = Brush("TextBrush") });
        box.Children.Add(SmallButton("Escolher imagem do PC...", (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Imagens (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp", Title = "Escolha a imagem do papel de parede" };
            if (dialog.ShowDialog() != true) return;
            string file = dialog.FileName;
            _selectionName = Path.GetFileName(file);
            _resolve = (_, _) => Task.FromResult<string?>(file);
            try
            {
                var img = new BitmapImage();
                img.BeginInit(); img.UriSource = new Uri(file); img.CacheOption = BitmapCacheOption.OnLoad; img.DecodePixelWidth = 480; img.EndInit();
                Select(img, _selectionName);
            }
            catch (Exception ex) when (ex is NotSupportedException or IOException or UriFormatException) { SetStatus("Não consegui abrir essa imagem.", "BadBrush"); }
        }, true));
        _grid.Children.Add(FullWidth(CardOf(box)));
    }

    // ---------------------------------------------------------------- comum
    private FrameworkElement Thumb(ImageSource source, string caption, Action click)
    {
        var img = new Image { Source = source, Stretch = Stretch.UniformToFill, Height = 104 };
        var clip = new Border { CornerRadius = new CornerRadius(8, 8, 0, 0), ClipToBounds = true, Child = img, Height = 104 };
        var text = new TextBlock { Text = caption, FontSize = 11, Margin = new Thickness(8, 5, 8, 6), TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Brush("TextBrush") };
        var panel = new StackPanel { Children = { clip, text } };
        var card = new Border
        {
            Width = 186, Margin = new Thickness(0, 0, 10, 10), CornerRadius = new CornerRadius(8), Background = Brush("SurfaceBrush"),
            BorderBrush = Brush("Surface2Brush"), BorderThickness = new Thickness(1), Child = panel, Cursor = System.Windows.Input.Cursors.Hand,
        };
        card.MouseLeftButtonUp += (_, _) => click();
        return card;
    }

    private FrameworkElement FullWidth(FrameworkElement e)
    {
        e.Width = double.NaN;
        var holder = new Border { Child = e, Margin = new Thickness(0, 0, 0, 8) };
        holder.SetValue(FrameworkElement.MinWidthProperty, 560.0);
        return holder;
    }

    private void Select(ImageSource preview, string name)
    {
        _previewImage.Source = preview;
        _apply!.IsEnabled = true;
        SetStatus("Selecionado: " + name + ". Clique em Aplicar no Windows.", "OkBrush");
    }

    private double TargetAspect()
    {
        var (w, h) = TargetResolution();
        return h > 0 ? (double)w / h : 16.0 / 9.0;
    }

    /// <summary>Resolução de referência: o monitor escolhido, ou o maior monitor quando é "todos".</summary>
    private (int W, int H) TargetResolution()
    {
        if (_targetMonitorId is not null && _monitors.FirstOrDefault(m => m.Id == _targetMonitorId) is { Width: > 0 } m) return (m.Width, m.Height);
        var best = _monitors.Where(m => m.Width > 0).OrderByDescending(m => (long)m.Width * m.Height).FirstOrDefault();
        return best is null ? (1920, 1080) : (best.Width, best.Height);
    }

    private async Task ApplyAsync()
    {
        if (_resolve is null) { SetStatus("Escolha um modelo ou imagem primeiro.", "WarnBrush"); return; }
        _apply!.IsEnabled = false;
        SetStatus("Aplicando...");
        try
        {
            var (w, h) = TargetResolution();
            string? file = await _resolve(w, h);
            if (file is null || !File.Exists(file)) { SetStatus("Não foi possível preparar a imagem.", "BadBrush"); return; }
            string? monitor = _fit == WallpaperFit.Span ? null : _targetMonitorId;   // span é sempre em todos
            bool ok = await Task.Run(() => DesktopWallpaper.Apply(file, _fit, monitor));
            SetStatus(ok
                ? (monitor is null ? "Papel de parede aplicado em todos os monitores." : "Papel de parede aplicado no monitor escolhido.")
                : "O Windows não aceitou aplicar o papel de parede.", ok ? "OkBrush" : "BadBrush");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            SetStatus("Não foi possível aplicar: " + ex.Message, "BadBrush");
        }
        finally { _apply!.IsEnabled = true; }
    }
}
