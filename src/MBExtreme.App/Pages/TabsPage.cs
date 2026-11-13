using System.Windows;
using System.Windows.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>Várias páginas numa tela só, com abas no alto (ex.: Programas: instalar e atualizar; Desinstalador: programas e aplicativos do Windows).</summary>
internal sealed class TabsPage : UserControl
{
    private readonly (string Title, string Key, Func<UserControl> Create)[] _tabs;
    private readonly Dictionary<int, UserControl> _pages = new();
    private readonly ContentControl _host = new();
    private readonly List<Button> _buttons = new();
    private int _selected;

    public TabsPage(params (string Title, string Key, Func<UserControl> Create)[] tabs) : this(null, tabs) { }

    /// <summary>Com <paramref name="header"/>: uma faixa fixa acima das abas (ex.: o Turbo da Limpeza).</summary>
    public TabsPage(UIElement? header, params (string Title, string Key, Func<UserControl> Create)[] tabs)
    {
        _tabs = tabs;
        var bar = new WrapPanel { Margin = new Thickness(40, 14, 40, 0) };
        for (int i = 0; i < _tabs.Length; i++)
        {
            int index = i;
            var button = new Button { Content = _tabs[i].Title, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(14, 6, 14, 6), FontSize = 13 };
            button.Click += (_, _) => Select(index);
            _buttons.Add(button);
            bar.Children.Add(button);
        }

        var top = new StackPanel();
        if (header is not null) top.Children.Add(header);
        top.Children.Add(bar);
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
            _buttons[i].Style = (Style)Application.Current.FindResource(i == index ? "PrimaryButton" : "GhostButton");

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
