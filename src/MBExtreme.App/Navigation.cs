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
            new("profiles", "Perfis", "Jogo, Competitivo e PC fraco em um clique.", "\uE945"),
            new("cleanup", "Limpeza", "Procura lixo, temporários, cache e libera memória.", "\uE74D"),
            new("startup", "Inicialização", "Programas que abrem junto com o Windows.", "\uE7E8"),
            new("tweaks-perf", "Desempenho", "Prioridade de CPU, energia e serviços.", "\uE9D9"),
            new("visual", "Efeitos visuais", "Transparência e animações do Windows.", "\uE713"),
            new("memory", "Memória", "Limpe a RAM e proteja aplicativos.", "\uE950"),
            new("storage", "Armazenamento", "SSD, HD e saúde dos discos.", "\uEDA2"),
        }),
        new("games", "Jogos", "Windows, GPU, energia e latência para jogar melhor.", "\uE7FC", new Tile[]
        {
            new("gameboost", "Modo Jogo", "Prepara o PC quando um jogo abre.", "\uE7FC"),
            new("gamesettings", "Jogos e energia", "Modo de Jogo do Windows, agendamento de GPU e plano de energia.", "\uE713"),
            new("tweaks-game", "Ajustes para jogos", "Prioridade, gravação em segundo plano, energia e mais.", "\uE9D9"),
            new("latency", "Diagnóstico de latência", "Acha o driver que causa micro-travadas.", "\uE9D9"),
            new("gameadvisor", "Assistente de jogo", "Configurações ideais para o seu hardware.", "\uE770"),
            new("fps", "Monitor de FPS", "Overlay com FPS, temperatura e uso.", "\uE9D9"),
            new("record", "Gravação de tela", "Full HD, 2K e 4K pela placa de vídeo.", "\uE714"),
        }),
        new("test", "Testes", "Informações, sensores, benchmark e estresse.", "\uE9D9", new Tile[]
        {
            new("sysinfo", "Informações do sistema", "Estilo CPU-Z, GPU-Z e AIDA.", "\uE770"),
            new("monitor", "Monitor em tempo real", "Clock, uso, tensões e temperaturas.", "\uE9D9"),
            new("bench", "Benchmark", "CPU, memória, disco e GPU em 3D.", "\uE9D9"),
            new("compare", "Antes e depois", "Mede o efeito real de cada mudança.", "\uE9D9"),
            new("stress", "Teste de estresse", "Carga no hardware, por núcleo ou geral.", "\uE7BA"),
        }),
        new("hw", "Hardware e BIOS", "Leitura das peças, BIOS e PBO.", "\uE950", new Tile[]
        {
            new("hardware", "Meu hardware", "O que melhorar: memória, Windows, BIOS e mais.", "\uE950"),
            new("bios", "BIOS e atualizações", "Versão da BIOS e onde achar a mais nova.", "\uE896"),
            new("recipe", "Receita de BIOS", "XMP/EXPO, Resizable BAR e PBO para o seu PC.", "\uE713"),
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
            new("netanalyzer", "Analisador de rede", "Ping, TCPing, MTR e jitter.", "\uE9D9"),
            new("wifi", "Diagnóstico de Wi-Fi", "Sinal, banda e dicas.", "\uE774"),
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
