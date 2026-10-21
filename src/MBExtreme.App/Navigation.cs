namespace MBExtreme.UI;

/// <summary>Um atalho dentro de um módulo (abre uma página).</summary>
internal sealed record Tile(string Key, string Title, string Description, string Icon);

/// <summary>Um módulo do menu lateral. Ao clicar, mostra os atalhos dele.</summary>
internal sealed record Module(string Key, string Title, string Subtitle, string Icon, IReadOnlyList<Tile> Tiles);

internal static class NavigationMap
{
    // Ícones: fonte "Segoe MDL2 Assets", que já vem no Windows 10 e 11.
    public static IReadOnlyList<Module> Modules { get; } = new Module[]
    {
        new("opt", "Otimização", "Deixe o computador mais rápido e leve.", "\uE945", new Tile[]
        {
            new("cleanup", "Limpeza", "Procura lixo, temporários, cache e libera memória.", "\uE74D"),
            new("tweaks-perf", "Desempenho", "Prioridade de CPU, energia e serviços.", "\uE9D9"),
            new("memory", "Memória", "Limpe a RAM e proteja aplicativos.", "\uE950"),
            new("storage", "Armazenamento", "SSD, HD e saúde dos discos.", "\uEDA2"),
        }),
        new("games", "Jogos", "Windows, GPU e energia para jogar melhor.", "\uE7FC", new Tile[]
        {
            new("gamesettings", "Jogos e energia", "Modo de Jogo, agendamento de GPU e plano de energia.", "\uE7FC"),
            new("tweaks-game", "Ajustes para jogos", "Prioridade, gravação em segundo plano e mais.", "\uE713"),
            new("fps", "Monitor de FPS", "Overlay com FPS, temperatura e uso.", "\uE9D9"),
            new("record", "Gravação de tela", "Grave a janela para guardar testes.", "\uE714"),
        }),
        new("test", "Testes", "Informações, benchmark e teste de estresse.", "\uE9D9", new Tile[]
        {
            new("sysinfo", "Informações do sistema", "Estilo CPU-Z, GPU-Z e AIDA.", "\uE770"),
            new("bench", "Benchmark", "Pontuação de CPU, memória e disco.", "\uE9D9"),
            new("stress", "Teste de estresse", "Carga no hardware com limite de temperatura.", "\uE7BA"),
        }),
        new("dev", "Dispositivos", "USB e tudo que está conectado ao PC.", "\uE88E", new Tile[]
        {
            new("devices", "Dispositivos conectados", "Lista de USB e de todo o hardware.", "\uE88E"),
            new("ghosts", "Cache de portas USB", "Remove registros de USB que não existem mais.", "\uE74D"),
        }),
        new("net", "Rede", "Conexão, ping e DNS.", "\uE774", new Tile[]
        {
            new("dns", "DNS", "Teste e escolha o servidor mais rápido.", "\uE774"),
            new("net-tweaks", "Otimizações de rede", "Ajustes do Windows para a rede.", "\uE713"),
            new("netanalyzer", "Analisador de rede", "Ping, TCPing e MTR.", "\uE9D9"),
        }),
        new("win", "Windows", "Privacidade, aplicativos e inicialização.", "\uE713", new Tile[]
        {
            new("privacy", "Privacidade", "Anúncios, histórico, Copilot e SmartScreen.", "\uE72E"),
            new("debloat", "Aplicativos", "Remova apps do Windows que você não usa.", "\uE74D"),
            new("boot", "Inicialização e boot", "Como o Windows liga e desliga.", "\uE7E8"),
            new("periph", "Periféricos", "Mouse e teclado.", "\uE765"),
        }),
        new("drv", "Drivers e programas", "Atualizações por fontes oficiais.", "\uE896", new Tile[]
        {
            new("drivers", "Drivers", "Placa de vídeo (NVIDIA, AMD, Intel) e Windows Update.", "\uE950"),
            new("programs", "Programas", "Atualize e instale ferramentas pelo winget.", "\uE896"),
        }),
    };

    public static Module? ModuleOf(string pageKey) =>
        Modules.FirstOrDefault(m => m.Key == pageKey || m.Tiles.Any(t => t.Key == pageKey));

    public static Tile? TileOf(string pageKey) =>
        Modules.SelectMany(m => m.Tiles).FirstOrDefault(t => t.Key == pageKey);
}
