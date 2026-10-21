using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Tweaks;
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
        "debloat" => new DebloatPage(),
        "tweaks-perf" => new TweaksPage("Desempenho",
            "Prioridade de CPU, plano de energia e serviços do Windows. Cada ajuste mostra o risco e pode ser restaurado.",
            TweakCategory.Performance),
        "tweaks-game" => new TweaksPage("Jogos e GPU",
            "Ajustes para jogar melhor: prioridade, gravação em segundo plano, agendamento de GPU e drivers.",
            TweakCategory.Gaming),
        "net-tweaks" => new TweaksPage("Otimizações de rede",
            "Ajustes do Windows que afetam a rede. O ganho varia; use o analisador de rede para medir antes e depois.",
            TweakCategory.Network),
        "privacy" => new TweaksPage("Privacidade",
            "Anúncios, histórico, Copilot, Cortana e SmartScreen.",
            TweakCategory.Privacy),
        "boot" => new TweaksPage("Inicialização e boot",
            "Como o Windows liga e desliga.",
            TweakCategory.Boot),
        "periph" => new TweaksPage("Periféricos",
            "Mouse e teclado.",
            TweakCategory.Peripherals),
        "storage" => new PlaceholderPage("Armazenamento", "Ajustes de SSD e HD, limpeza e saúde dos discos. Veja docs/CATALOGO-AJUSTES.md."),
        "netanalyzer" => new PlaceholderPage("Analisador de rede", "MTR, Ping, TCPing, monitoramento e ping para jogos. Veja docs/ESPECIFICACAO-REDE.md."),
        "perf" => new PlaceholderPage("Hardware e benchmarks", "Informações do sistema, sensores, stress test e benchmarks próprios."),
        "tools" => new PlaceholderPage("Drivers e programas", "Atualização de drivers por fontes oficiais e instalação de programas pelo winget."),
        "actions" => new PlaceholderPage("Central de ações", "Histórico do que foi alterado e botão de desfazer."),
        _ => new PlaceholderPage(key, ""),
    };
}
