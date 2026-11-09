using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MBExtreme.UI.Pages;

/// <summary>Moldura de uma página dentro de um módulo: botão de voltar e o nome do caminho.</summary>
internal sealed class SubPageHost : UserControl
{
    private readonly Button _back;
    private Action _goBack;

    /// <summary>Faz o botão voltar para a tela de onde o usuário veio (ex.: o Início), com o nome dela.</summary>
    public void SetBack(string label, Action goBack)
    {
        _goBack = goBack;
        _back.Content = "‹  " + label;
    }

    public SubPageHost(Module module, Tile tile, UserControl page, Action<string> goTo)
    {
        var back = new Button
        {
            Content = "‹  Voltar",
            Style = (Style)Application.Current.FindResource("GhostButton"),
            Padding = new Thickness(16, 7, 16, 7),
            FontSize = 13,
        };
        string moduleKey = module.Key;
        _goBack = () => goTo(moduleKey);
        _back = back;
        back.Click += (_, _) => _goBack();
        back.Content = "‹  " + module.Title;

        var home = new Button
        {
            Content = "Início",
            Style = (Style)Application.Current.FindResource("GhostButton"),
            Padding = new Thickness(14, 7, 14, 7),
            FontSize = 13,
            Margin = new Thickness(8, 0, 0, 0),
        };
        home.Click += (_, _) => goTo("home");

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(36, 22, 36, 0) };
        bar.Children.Add(back);
        bar.Children.Add(home);
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
