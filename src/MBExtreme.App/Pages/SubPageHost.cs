using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MBExtreme.UI.Pages;

/// <summary>Moldura de uma página dentro de um módulo: botão de voltar e o nome do caminho.</summary>
internal sealed class SubPageHost : UserControl
{
    public SubPageHost(Module module, Tile tile, UserControl page, Action<string> goTo)
    {
        var back = new Button
        {
            Content = "‹  " + module.Title,
            Style = (Style)Application.Current.FindResource("GhostButton"),
            Padding = new Thickness(16, 7, 16, 7),
            FontSize = 13,
        };
        string moduleKey = module.Key;
        back.Click += (_, _) => goTo(moduleKey);

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(36, 22, 36, 0) };
        bar.Children.Add(back);
        bar.Children.Add(new TextBlock
        {
            Text = "›   " + tile.Title,
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.FindResource("MutedBrush"),
        });

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(bar);
        Grid.SetRow(page, 1);
        grid.Children.Add(page);
        Content = grid;
    }
}
