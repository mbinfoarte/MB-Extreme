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

    private readonly MBExtreme.Core.Settings.AppSettings _settings;

    public CoverPickerWindow(HttpClient http, string gameName, MBExtreme.Core.Settings.AppSettings settings, bool hasCustom)
    {
        _http = http;
        _settings = settings;
        Title = "Buscar capa";
        Width = 640;
        Height = 640;
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
        var steam = await CoverSearch.SearchAsync(_http, name);
        var grid = await SteamGridDb.SearchAsync(_http, _settings.SteamGridDbKey, name);
        if (steam.Count == 0 && grid.Count == 0)
        {
            _status.Text = string.IsNullOrWhiteSpace(_settings.SteamGridDbKey)
                ? "Nada encontrado na Steam. Para jogos de outras lojas, coloque a chave gratuita do SteamGridDB acima. Também ajuda tentar só a primeira palavra do nome."
                : "Nada encontrado (ou sem internet). Tente só a primeira palavra do nome.";
            return;
        }
        _status.Text = "";
        if (steam.Count > 0) AddHeading("Loja da Steam");
        foreach (var candidate in steam) AddCandidate(candidate);
        if (grid.Count > 0) AddHeading("SteamGridDB (todas as lojas)");
        foreach (var candidate in grid) AddCandidate(candidate);
    }

    private void AddHeading(string text)
    {
        var heading = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 6), Width = 560 };
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
