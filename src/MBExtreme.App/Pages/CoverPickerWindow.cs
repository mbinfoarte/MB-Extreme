using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MBExtreme.Core.Games;

namespace MBExtreme.UI.Pages;

/// <summary>Janela para procurar a capa de um jogo na internet e escolher uma.</summary>
internal sealed class CoverPickerWindow : Window
{
    private readonly HttpClient _http;
    private readonly TextBox _query = new() { Margin = new Thickness(0, 0, 8, 0), MinWidth = 280, Padding = new Thickness(6, 4, 6, 4) };
    private readonly WrapPanel _results = new();
    private readonly TextBlock _status = new() { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };

    public CoverCandidate? Chosen { get; private set; }

    /// <summary>O usuário pediu para voltar à capa automática.</summary>
    public bool RemoveCustom { get; private set; }

    /// <summary>Link de imagem colado pelo usuário.</summary>
    public string? ChosenUrl { get; private set; }

    /// <summary>Imagem escolhida do próprio PC.</summary>
    public string? ChosenFile { get; private set; }

    private readonly string _gameName;

    private readonly MBExtreme.Core.Settings.AppSettings _settings;

    public CoverPickerWindow(HttpClient http, string gameName, MBExtreme.Core.Settings.AppSettings settings, bool hasCustom)
    {
        _http = http;
        _settings = settings;
        _gameName = gameName;
        Title = "Buscar capa";
        Width = 700;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.FindResource("BgBrush");
        Foreground = (Brush)Application.Current.FindResource("TextBrush");
        _status.Foreground = (Brush)Application.Current.FindResource("MutedBrush");
        _query.Text = gameName;

        var bar = new DockPanel { Margin = new Thickness(16, 16, 16, 0) };
        var search = new Button { Content = "Buscar", Style = (Style)Application.Current.FindResource("PrimaryButton"), Padding = new Thickness(16, 6, 16, 6), IsDefault = true };
        search.Click += async (_, _) => await SearchAsync();
        DockPanel.SetDock(search, Dock.Right);
        bar.Children.Add(search);
        bar.Children.Add(_query);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(bar);
        var info = new StackPanel { Margin = new Thickness(16, 8, 16, 0) };
        info.Children.Add(new TextBlock { Text = "Clique na capa que você quer usar. Aparecem as da loja da Steam e, com a chave gratuita do SteamGridDB, também as de jogos de outras lojas (EA, Epic, Ubisoft, Battle.net...).", TextWrapping = TextWrapping.Wrap, Foreground = _status.Foreground });
        var keyRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var saveKey = new Button { Content = "Salvar chave", Style = (Style)Application.Current.FindResource("GhostButton"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(saveKey, Dock.Right);
        var key = new TextBox { Text = _settings.SteamGridDbKey, Padding = new Thickness(6, 3, 6, 3), ToolTip = "Chave gratuita: steamgriddb.com > seu perfil > Preferências > API" };
        saveKey.Click += async (_, _) => { _settings.SteamGridDbKey = key.Text.Trim(); _settings.Save(); await SearchAsync(); };
        keyRow.Children.Add(saveKey);
        keyRow.Children.Add(new TextBlock { Text = "Chave do SteamGridDB:", Foreground = _status.Foreground, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        keyRow.Children.Add(key);
        info.Children.Add(keyRow);
        info.Children.Add(new TextBlock { Text = "Para pegar a chave: entre em steamgriddb.com, abra seu perfil, Preferências, API.", FontSize = 11, Foreground = _status.Foreground, Margin = new Thickness(0, 3, 0, 0) });

        // outras formas: link de qualquer site, arquivo do PC ou procurar no Google Imagens
        var linkRow = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var useLink = new Button { Content = "Usar link", Style = (Style)Application.Current.FindResource("GhostButton"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(useLink, Dock.Right);
        var link = new TextBox { Padding = new Thickness(6, 3, 6, 3), ToolTip = "No navegador: clique com o botão direito na imagem e escolha \"Copiar endereço da imagem\"" };
        useLink.Click += (_, _) =>
        {
            string url = link.Text.Trim();
            if (!WebCoverSearch.IsSafeUrl(url)) { _status.Text = "O link precisa começar com https:// e ser de um site da internet (copie o endereço da imagem, não da página)."; return; }
            ChosenUrl = url;
            DialogResult = true;
        };
        linkRow.Children.Add(useLink);
        linkRow.Children.Add(new TextBlock { Text = "Link da imagem:", Foreground = _status.Foreground, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        linkRow.Children.Add(link);
        info.Children.Add(linkRow);
        var more = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var google = new Button { Content = "Procurar no Google Imagens", Style = (Style)Application.Current.FindResource("GhostButton"), Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
        google.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(WebCoverSearch.GoogleImagesUrl(_query.Text.Length > 0 ? _query.Text : _gameName)) { UseShellExecute = true }); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { _status.Text = "Não deu para abrir o navegador: " + ex.Message; }
            _status.Text = "No Google, clique com o botão direito na imagem, \"Copiar endereço da imagem\" e cole no campo acima.";
        };
        var file = new Button { Content = "Escolher imagem do PC...", Style = (Style)Application.Current.FindResource("GhostButton"), Padding = new Thickness(12, 4, 12, 4) };
        file.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Imagens (*.jpg;*.jpeg;*.png;*.webp)|*.jpg;*.jpeg;*.png;*.webp", Title = "Escolher capa" };
            if (dialog.ShowDialog(this) == true) { ChosenFile = dialog.FileName; DialogResult = true; }
        };
        more.Children.Add(google);
        more.Children.Add(file);
        info.Children.Add(more);
        if (hasCustom)
        {
            var auto = new Button { Content = "Voltar à capa automática", Style = (Style)Application.Current.FindResource("GhostButton"), Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            auto.Click += (_, _) => { RemoveCustom = true; DialogResult = true; };
            info.Children.Add(auto);
        }
        Grid.SetRow(info, 1);
        grid.Children.Add(info);
        var body = new StackPanel { Margin = new Thickness(16) };
        body.Children.Add(_results);
        body.Children.Add(_status);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body };
        Grid.SetRow(scroll, 2);
        grid.Children.Add(scroll);
        Content = grid;

        Loaded += async (_, _) => await SearchAsync();
    }

    private async Task SearchAsync()
    {
        _results.Children.Clear();
        _status.Text = "Procurando...";
        string name = _query.Text.Trim();
        var steamTask = CoverSearch.SearchAsync(_http, name);
        var gridTask = SteamGridDb.SearchAsync(_http, _settings.SteamGridDbKey, name);
        var webTask = WebCoverSearch.SearchAsync(_http, name);
        var steam = await steamTask;
        var grid = await gridTask;
        var web = await webTask;
        if (steam.Count == 0 && grid.Count == 0 && web.Count == 0)
        {
            _status.Text = "Nada encontrado (ou sem internet). Tente só a primeira palavra do nome, cole o link de uma imagem ou escolha um arquivo do PC.";
            return;
        }
        _status.Text = "";
        if (steam.Count > 0) AddHeading("Loja da Steam");
        foreach (var candidate in steam) AddCandidate(candidate);
        if (grid.Count > 0) AddHeading("SteamGridDB (todas as lojas)");
        foreach (var candidate in grid) AddCandidate(candidate);
        if (web.Count > 0) AddHeading("Internet (imagens de qualquer site)");
        foreach (var candidate in web) AddCandidate(candidate);
    }

    private void AddHeading(string text)
    {
        var heading = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 6), Width = 620 };
        _results.Children.Add(heading);
    }

    private void AddCandidate(CoverCandidate candidate)
    {
        var image = new Border { Width = 120, Height = 180, CornerRadius = new CornerRadius(8), Background = (Brush)Application.Current.FindResource("Surface2Brush") };
        void Load(string url, bool allowFallback)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(url);
                bmp.DecodePixelWidth = 240;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                image.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
                // jogos antigos da Steam não têm a capa vertical: mostra a imagem larga da loja no lugar
                if (allowFallback && candidate.DownloadUrl is null)
                    bmp.DownloadFailed += (_, _) => Load($"https://cdn.cloudflare.steamstatic.com/steam/apps/{candidate.AppId}/header.jpg", false);
            }
            catch (Exception ex) when (ex is UriFormatException or NotSupportedException or InvalidOperationException) { }
        }
        Load(candidate.ImageUrl, true);

        var stack = new StackPanel { Width = 120 };
        stack.Children.Add(image);
        stack.Children.Add(new TextBlock { Text = candidate.Name, FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxHeight = 32, Margin = new Thickness(0, 4, 0, 0) });
        var button = new Button { Content = stack, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(4), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand };
        button.Click += (_, _) => { Chosen = candidate; DialogResult = true; };
        _results.Children.Add(button);
    }
}
