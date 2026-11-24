using System.Windows;
using System.Windows.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>Várias páginas numa tela só, com abas no alto (ex.: Programas: instalar e atualizar; Desinstalador: programas e aplicativos do Windows).</summary>
internal sealed class TabsPage : UserControl
{
    private readonly (string Title, string Key, Func<UserControl> Create)[] _tabs;
    private readonly Dictionary<int, UserControl> _pages = new();
    private readonly ContentControl _host = new();
    private readonly List<RadioButton> _buttons = new();
    private int _selected;

    public TabsPage(params (string Title, string Key, Func<UserControl> Create)[] tabs) : this(null, tabs) { }

    /// <summary>Com <paramref name="header"/>: uma faixa fixa acima das abas (ex.: o Turbo da Limpeza).</summary>
    public TabsPage(UIElement? header, params (string Title, string Key, Func<UserControl> Create)[] tabs) : this(header, false, tabs) { }

    /// <summary>Com <paramref name="inner"/>: abas menores, para uma tela de abas dentro de outra (ex.: Otimizar › Limpeza).</summary>
    public TabsPage(UIElement? header, bool inner, params (string Title, string Key, Func<UserControl> Create)[] tabs) : this(header, inner, null, tabs) { }

    /// <summary>
    /// <paramref name="hubKey"/> dá o título e o ícone do módulo. No 1º nível: "barra de ferramentas do módulo" (ícone que acende dourado,
    /// nome e uma linha do que faz). No 2º nível: abas internas com ícone e sublinhado dourado, numa faixa com o nome da ferramenta.
    /// </summary>
    public TabsPage(UIElement? header, bool inner, string? hubKey, params (string Title, string Key, Func<UserControl> Create)[] tabs)
    {
        _tabs = tabs;
        string group = "tabs-" + Guid.NewGuid().ToString("N");
        var bar = new WrapPanel();
        for (int i = 0; i < _tabs.Length; i++)
        {
            int index = i;
            var info = NavigationMap.TabInfo.TryGetValue(_tabs[i].Key, out var x) ? x : ("\uE8A9", "");
            object content;
            if (inner) content = _tabs[i].Title;
            else
            {
                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = _tabs[i].Title, FontWeight = FontWeights.SemiBold });
                if (info.Item2.Length > 0) text.Children.Add(new TextBlock { Text = info.Item2, FontSize = 10.5, Opacity = 0.62, Margin = new Thickness(0, 1, 0, 0) });
                content = text;
            }
            var button = new RadioButton
            {
                Content = content, Tag = info.Item1, GroupName = group,
                Style = (Style)Application.Current.FindResource(inner ? "InnerTab" : "ToolTab"),
                ToolTip = info.Item2.Length > 0 ? $"{_tabs[i].Title}: {info.Item2}" : null,
            };
            System.Windows.Automation.AutomationProperties.SetName(button, _tabs[i].Title);
            button.Checked += (_, _) => { if (_selected != index || _host.Content is null) Select(index); };
            _buttons.Add(button);
            bar.Children.Add(button);
        }

        var (title, icon) = hubKey is null ? ("", "") : NavigationMap.HubTitle(hubKey);
        UIElement tabsBlock;
        if (inner)
        {
            var row = new DockPanel();
            if (title.Length > 0)
            {
                var label = new TextBlock
                {
                    Text = title.ToUpperInvariant() + "  ›", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC9, 0x9A, 0x00)),
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 10, 0),
                };
                DockPanel.SetDock(label, Dock.Left);
                row.Children.Add(label);
            }
            row.Children.Add(bar);
            tabsBlock = new Border
            {
                Child = row, Margin = new Thickness(40, 6, 40, 0), Padding = new Thickness(6, 4, 6, 4), CornerRadius = new CornerRadius(12),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x13, 0x13, 0x17)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x24, 0x24, 0x2A)), BorderThickness = new Thickness(1),
            };
        }
        else
        {
            var box = new StackPanel();
            if (title.Length > 0)
            {
                var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 10) };
                head.Children.Add(new Border
                {
                    Width = 30, Height = 30, CornerRadius = new CornerRadius(9), Background = (System.Windows.Media.Brush)Application.Current.FindResource("AccentGradientBrush"),
                    Child = new TextBlock { Text = icon, FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"), FontSize = 15, Foreground = System.Windows.Media.Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                });
                head.Children.Add(new TextBlock { Text = title, FontSize = 15.5, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
                head.Children.Add(new TextBlock
                {
                    Text = $"  ·  {_tabs.Length} ferramentas  ·  escolha uma", FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("MutedBrush"),
                });
                box.Children.Add(head);
            }
            box.Children.Add(bar);
            tabsBlock = new Border
            {
                Child = box, Margin = new Thickness(40, 14, 40, 0), Padding = new Thickness(12, 12, 4, 4), CornerRadius = new CornerRadius(16),
                Background = new System.Windows.Media.LinearGradientBrush(System.Windows.Media.Color.FromRgb(0x15, 0x15, 0x19), System.Windows.Media.Color.FromRgb(0x10, 0x10, 0x13), 90),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x24, 0x24, 0x2B)), BorderThickness = new Thickness(1),
            };
        }

        var top = new StackPanel();
        if (header is not null) top.Children.Add(header);
        top.Children.Add(tabsBlock);
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(top);
        Grid.SetRow(_host, 1);
        grid.Children.Add(_host);
        Content = grid;
        Loaded += (_, _) => Select(_selected);
    }

    /// <summary>Mostra a aba da ferramenta <paramref name="key"/> e devolve a página dela (null se a chave não for desta tela).</summary>
    public UserControl? Select(string key)
    {
        int index = Array.FindIndex(_tabs, t => t.Key == key);
        if (index < 0) return null;
        Select(index);
        return _pages.TryGetValue(index, out var page) ? page : null;
    }

    private void Select(int index)
    {
        _selected = index;
        for (int i = 0; i < _buttons.Count; i++)
            if ((_buttons[i].IsChecked == true) != (i == index)) _buttons[i].IsChecked = i == index;

        string key = _tabs[index].Key;
        if (!AppServices.License.IsAllowed(key))
        {
            _host.Content = new UpgradePage(_tabs[index].Title, key);
            return;
        }
        if (!_pages.TryGetValue(index, out var page))
            _pages[index] = page = _tabs[index].Create();
        _host.Content = page;
    }
}
