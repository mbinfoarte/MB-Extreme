using System.Windows;
using System.Windows.Controls;
using MBExtreme.UI.Pages;

namespace MBExtreme.UI;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, UserControl> _pages = new();

    public MainWindow()
    {
        InitializeComponent();
        Navigate("home");
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        // O evento dispara durante o carregamento do XAML, antes de ContentHost existir.
        if (ContentHost is null) return;
        if (sender is RadioButton { Tag: string key }) Navigate(key);
    }

    private void Navigate(string key)
    {
        if (!_pages.TryGetValue(key, out var page))
        {
            page = Create(key);
            _pages[key] = page;
        }

        ContentHost.Content = page;
    }

    private static UserControl Create(string key) => key switch
    {
        "home" => new HomePage(),
        "memory" => new MemoryPage(),
        "dns" => new DnsPage(),
        "tweaks" => new PlaceholderPage("Ajustes de sistema", "Ajustes reversíveis de desempenho do Windows. Veja docs/ESPECIFICACAO-FUNCIONAL.md."),
        "netanalyzer" => new PlaceholderPage("Analisador de rede", "MTR, Ping, TCPing, monitoramento e ping para jogos. Veja docs/ESPECIFICACAO-REDE.md."),
        "perf" => new PlaceholderPage("Hardware e benchmarks", "Informações do sistema, sensores, stress test e benchmarks próprios."),
        "tools" => new PlaceholderPage("Drivers e programas", "Atualização de drivers por fontes oficiais e instalação de programas pelo winget."),
        "actions" => new PlaceholderPage("Central de ações", "Histórico do que foi alterado e botão de desfazer."),
        _ => new PlaceholderPage(key, ""),
    };
}
