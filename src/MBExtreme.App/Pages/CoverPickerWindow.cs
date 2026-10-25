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

    public CoverPickerWindow(HttpClient http, string gameName)
    {
        _http = http;
        Title = "Buscar capa";
        Width = 640;
        Height = 560;
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
        var hint = new TextBlock { Text = "Clique na capa que você quer usar na biblioteca. Se o jogo não aparece, tente só uma parte do nome.", Margin = new Thickness(16, 8, 16, 0), TextWrapping = TextWrapping.Wrap, Foreground = _status.Foreground };
        Grid.SetRow(hint, 1);
        grid.Children.Add(hint);
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
        var found = await CoverSearch.SearchAsync(_http, _query.Text);
        if (found.Count == 0)
        {
            _status.Text = "Nada encontrado (ou sem internet). Tente outro nome, por exemplo só a primeira palavra.";
            return;
        }
        _status.Text = "";
        foreach (var candidate in found)
        {
            var image = new Border { Width = 120, Height = 180, CornerRadius = new CornerRadius(8), Background = (Brush)Application.Current.FindResource("Surface2Brush") };
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(candidate.ImageUrl);
                bmp.DecodePixelWidth = 240;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                image.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
            }
            catch (Exception ex) when (ex is UriFormatException or NotSupportedException or InvalidOperationException) { }

            var stack = new StackPanel { Width = 120 };
            stack.Children.Add(image);
            stack.Children.Add(new TextBlock { Text = candidate.Name, FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxHeight = 32, Margin = new Thickness(0, 4, 0, 0) });
            var button = new Button { Content = stack, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(4), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand };
            var chosen = candidate;
            button.Click += (_, _) => { Chosen = chosen; DialogResult = true; };
            _results.Children.Add(button);
        }
    }
}
