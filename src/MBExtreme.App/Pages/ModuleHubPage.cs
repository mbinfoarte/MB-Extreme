using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MBExtreme.UI.Pages;

/// <summary>Página de um módulo: título e atalhos em cartões arredondados com ícone.</summary>
internal sealed class ModuleHubPage : UserControl
{
    public ModuleHubPage(Module module, Action<string> open)
    {
        var tiles = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 24, 0, 0) };
        foreach (var tile in module.Tiles)
            tiles.Children.Add(BuildTile(tile, open));

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(Icon(module.Icon, 30, 56));
        var titles = new StackPanel { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = module.Title, FontSize = 30, FontWeight = FontWeights.SemiBold });
        titles.Children.Add(new TextBlock
        {
            Text = module.Subtitle,
            Foreground = (Brush)Application.Current.FindResource("MutedBrush"),
            Margin = new Thickness(0, 2, 0, 0),
        });
        header.Children.Add(titles);

        var root = new StackPanel { Margin = new Thickness(36, 32, 36, 32) };
        root.Children.Add(header);
        root.Children.Add(tiles);

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
    }

    private static Border Icon(string glyph, double size, double box) => new()
    {
        Width = box,
        Height = box,
        CornerRadius = new CornerRadius(box / 2),
        Background = (Brush)Application.Current.FindResource("Surface2Brush"),
        Child = new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        },
    };

    private static Button BuildTile(Tile tile, Action<string> open)
    {
        var content = new StackPanel { Width = 220 };
        content.Children.Add(Icon(tile.Icon, 22, 48));
        content.Children.Add(new TextBlock
        {
            Text = tile.Title,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 16, 0, 0),
        });
        content.Children.Add(new TextBlock
        {
            Text = tile.Description,
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = (Brush)Application.Current.FindResource("MutedBrush"),
        });

        var button = new Button
        {
            Style = (Style)Application.Current.FindResource("TileButton"),
            Margin = new Thickness(0, 0, 18, 18),
            Content = content,
        };
        string key = tile.Key;
        button.Click += (_, _) => open(key);
        return button;
    }
}
