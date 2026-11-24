using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace MBExtreme.UI.Controls;

/// <summary>
/// Peças em comum das telas do Otimizar (Inicialização, Processos, Serviços...): números grandes no topo, barra de ferramentas
/// com busca, filtros em pílula e o botão de "um clique".
/// </summary>
internal static class OptimizeKit
{
    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    /// <summary>Cartão de número: rótulo pequeno, valor grande e uma linha de detalhe colorida.</summary>
    public static (Border Card, TextBlock Value, TextBlock Detail) Stat(string label, string value = "…", string detail = "", string detailBrush = "MutedBrush")
    {
        var valueText = new TextBlock { Text = value, FontSize = 24, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 2, 0, 0) };
        var detailText = new TextBlock { Text = detail, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Res(detailBrush), TextWrapping = TextWrapping.Wrap };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = label, FontSize = 11.5, Foreground = Res("MutedBrush") });
        stack.Children.Add(valueText);
        stack.Children.Add(detailText);
        var card = new Border
        {
            CornerRadius = new CornerRadius(14), Background = Res("SurfaceBrush"), BorderBrush = Res("BorderBrush"), BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 10, 10), Child = stack,
        };
        return (card, valueText, detailText);
    }

    /// <summary>Linha de cartões de número que divide a largura igualmente.</summary>
    public static UniformGrid StatRow(params Border[] cards)
    {
        var grid = new UniformGrid { Columns = cards.Length, Margin = new Thickness(0, 0, -10, 2) };
        foreach (var c in cards) grid.Children.Add(c);
        return grid;
    }

    /// <summary>Filtros em pílula (um marcado por vez), com ícone pelo nome.</summary>
    public static WrapPanel Filters(IReadOnlyList<string> names, int selected, Action<int> changed)
    {
        var panel = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        var chips = new List<ToggleButton>();
        for (int i = 0; i < names.Count; i++)
        {
            int index = i;
            var chip = new ToggleButton
            {
                Content = names[i], Style = (Style)Application.Current.FindResource("ChipToggle"), IsChecked = i == selected,
                Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(11, 4, 11, 4), FontSize = 12,
            };
            chip.Click += (_, _) =>
            {
                foreach (var c in chips) c.IsChecked = ReferenceEquals(c, chip);
                changed(index);
            };
            chips.Add(chip);
            panel.Children.Add(chip);
        }
        return panel;
    }

    /// <summary>Barra: à esquerda busca e filtros; à direita o texto de seleção e os botões.</summary>
    public static DockPanel Toolbar(IEnumerable<UIElement> left, IEnumerable<UIElement> right)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 6, 0, 8), LastChildFill = true };
        var r = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var e in right) r.Children.Add(e);
        DockPanel.SetDock(r, Dock.Right);
        dock.Children.Add(r);
        var l = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var e in left)
        {
            if (e is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 10, 0);
            l.Children.Add(e);
        }
        dock.Children.Add(l);
        return dock;
    }

    /// <summary>Faixa de garantia no rodapé: o que o "um clique" nunca toca.</summary>
    public static Border SafetyNote(string text)
    {
        var row = new DockPanel();
        var icon = new TextBlock
        {
            Text = "\uE83D", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 15, Foreground = Res("OkBrush"),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0),
        };
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(new TextBlock { Text = text, FontSize = 12, Foreground = Res("MutedBrush"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        return new Border
        {
            CornerRadius = new CornerRadius(12), Background = Res("SurfaceBrush"), BorderBrush = Res("BorderBrush"), BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 10, 0, 0), Child = row,
        };
    }

    /// <summary>Caminho do .exe numa linha de comando ("C:\x\a.exe" -b → C:\x\a.exe), para mostrar o ícone do programa.</summary>
    public static string? ExeOf(string command)
    {
        string c = Environment.ExpandEnvironmentVariables(command ?? "").Trim();
        if (c.StartsWith('"'))
        {
            int end = c.IndexOf('"', 1);
            return end > 1 ? c[1..end] : null;
        }
        int exe = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? c[..(exe + 4)] : null;
    }
}
