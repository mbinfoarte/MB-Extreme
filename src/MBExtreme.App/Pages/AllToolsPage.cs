using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace MBExtreme.UI.Pages;

/// <summary>Todas as ferramentas do programa, por módulo, com busca (o menu lateral mostra só as principais).</summary>
internal sealed class AllToolsPage : UserControl
{
    private readonly StackPanel _list = new();
    private readonly TextBox _search = new() { Padding = new Thickness(10, 6, 10, 6), FontSize = 13, Margin = new Thickness(0, 14, 0, 4) };
    private readonly Action<string> _open;

    public AllToolsPage(Action<string> open)
    {
        _open = open;
        var root = new StackPanel { Margin = new Thickness(40, 32, 40, 32) };
        root.Children.Add(new TextBlock { Text = "Todas as ferramentas", FontSize = 28, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock
        {
            Text = "Tudo o que o MB Extreme faz, separado por assunto. Digite para procurar.",
            Foreground = (Brush)Application.Current.FindResource("MutedBrush"), Margin = new Thickness(0, 4, 0, 0),
        });
        System.Windows.Automation.AutomationProperties.SetName(_search, "Procurar ferramenta");
        _search.TextChanged += (_, _) => Fill();
        root.Children.Add(_search);
        root.Children.Add(_list);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
        Fill();
    }

    private void Fill()
    {
        _list.Children.Clear();
        string q = _search.Text.Trim();
        foreach (var module in NavigationMap.Modules)
        {
            var tiles = module.Tiles.Where(t => q.Length == 0 || t.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                                                || t.Description.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
            if (tiles.Count == 0) continue;
            _list.Children.Add(new TextBlock { Text = module.Title.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.FindResource("MutedBrush"), Margin = new Thickness(2, 18, 0, 8) });
            var grid = new UniformGrid { Columns = 3 };
            foreach (var tile in tiles) grid.Children.Add(ToolCard.Build(tile.Title, tile.Description, tile.Icon, () => _open(tile.Key)));
            _list.Children.Add(grid);
        }
    }
}

/// <summary>Cartão de ferramenta: ícone em quadrado amarelo, nome e uma linha de descrição.</summary>
internal static class ToolCard
{
    public static Button Build(string title, string description, string glyph, Action click)
    {
        var icon = new Border
        {
            Style = (Style)Application.Current.FindResource("ToolIconBox"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0B)),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        var content = new StackPanel();
        content.Children.Add(icon);
        content.Children.Add(new TextBlock { Text = title, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        content.Children.Add(new TextBlock
        {
            Text = description, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0),
            Foreground = (Brush)Application.Current.FindResource("MutedBrush"), ToolTip = description,
        });
        var button = new Button { Style = (Style)Application.Current.FindResource("ToolTileButton"), Margin = new Thickness(0, 0, 10, 10), Content = content };
        System.Windows.Automation.AutomationProperties.SetName(button, title);
        button.Click += (_, _) => click();
        return button;
    }
}
