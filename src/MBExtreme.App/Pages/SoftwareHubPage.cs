using System.Windows;
using System.Windows.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>Drivers e programas numa página só, com abas: atualizar drivers, atualizar programas, instalar novos e remover o driver de vídeo.</summary>
internal sealed class SoftwareHubPage : UserControl
{
    private readonly (string Title, string Key, Func<UserControl> Create)[] _tabs =
    {
        ("Drivers", "drivers", () => new DriversPage()),
        ("Programas instalados", "programs", () => new ProgramsPage()),
        ("Instalar novos", "recommended", () => new RecommendedPage()),
        ("Remover driver de vídeo", "driver-removal", () => new DriverRemovalPage()),
    };

    private readonly Dictionary<int, UserControl> _pages = new();
    private readonly ContentControl _host = new();
    private readonly List<Button> _buttons = new();
    private int _selected;

    public SoftwareHubPage()
    {
        var bar = new WrapPanel { Margin = new Thickness(36, 8, 36, 0) };
        for (int i = 0; i < _tabs.Length; i++)
        {
            int index = i;
            var button = new Button { Content = _tabs[i].Title, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(14, 6, 14, 6), FontSize = 13 };
            button.Click += (_, _) => Select(index);
            _buttons.Add(button);
            bar.Children.Add(button);
        }

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(bar);
        Grid.SetRow(_host, 1);
        grid.Children.Add(_host);
        Content = grid;
        Loaded += (_, _) => Select(_selected);
    }

    private void Select(int index)
    {
        _selected = index;
        for (int i = 0; i < _buttons.Count; i++)
            _buttons[i].Style = (Style)Application.Current.FindResource(i == index ? "PrimaryButton" : "GhostButton");

        string key = _tabs[index].Key;
        // recursos do plano Pro sem licença válida mostram o aviso no lugar da aba
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
