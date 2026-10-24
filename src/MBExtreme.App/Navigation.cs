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
            new("weekly", "Limpeza semanal", "Limpeza automática todo domingo, sem abrir janela.", "\uE74D"),
            new("startup", "Inicialização", "Programas que abrem junto com o Windows.", "\uE7E8"),
            new("tweaks-perf", "Desempenho", "Prioridade de CPU, energia e serviços.", "\uE9D9"),
            new("visual", "Efeitos visuais", "Transparência e animações do Windows.", "\uE713"),
            new("memory", "Memória", "Limpe a RAM e proteja aplicativos.", "\uE950"),
            new("storage", "Armazenamento", "SSD, HD e saúde dos discos.", "\uEDA2"),
            new("tweaks-storage", "Ajustes de armazenamento", "TRIM, hibernação, último acesso e nomes 8.3.", "\uEDA2"),
        }),
        new("games", "Jogos", "Windows, GPU, energia e latência para jogar melhor.", "\uE7FC", new Tile[]
        {
            new("gamelibrary", "Biblioteca de jogos", "Seus jogos instalados, com Modo Jogo e prioridade por jogo.", "\uE7FC"),
            new("gameboost", "Modo Jogo", "Prepara o PC quando um jogo abre.", "\uE7FC"),
            new("gamesettings", "Jogos e energia", "Modo de Jogo do Windows, agendamento de GPU e plano de energia.", "\uE713"),
            new("tweaks-game", "Ajustes para jogos", "Prioridade, gravação em segundo plano, energia e mais.", "\uE9D9"),
            new("latency", "Diagnóstico de latência", "Acha o driver que causa micro-travadas.", "\uE9D9"),
            new("gameadvisor", "Assistente de jogo", "Configurações ideais para o seu hardware.", "\uE770"),
            new("bottleneck", "Monitor de gargalos", "O que limita o seu FPS: placa, processador, memória, disco ou calor.", "\uE9D9"),
            new("fps", "Monitor de FPS", "Overlay com FPS, temperatura e uso.", "\uE9D9"),
        }),
        new("record", "Gravação e transmissão", "Grave a tela, transmita ao vivo e salve replays.", "\uE714", new Tile[]
        {
            new("record", "Gravação e transmissão", "Grave, transmita ao vivo e salve replays, com áudio, câmera e escolha de janela.", "\uE714"),
        }),
        new("test", "Testes", "Informações, sensores, benchmark e estresse.", "\uE9D9", new Tile[]
        {
            new("sysinfo", "Informações do sistema", "Estilo CPU-Z, GPU-Z e AIDA.", "\uE770"),
            new("cpu-detail", "Processador", "Uso, temperatura, velocidade e energia, por núcleo.", "\uE950"),
            new("gpu-detail", "Placa de vídeo", "Uso, temperatura, velocidade, energia e memória de vídeo.", "\uE7F4"),
            new("ram-detail", "Memória", "Pentes, uso em tempo real e limpeza automática.", "\uE950"),
            new("disks", "Discos", "Modelo, velocidade e espaço livre e usado.", "\uEDA2"),
            new("removable", "Pendrives e HDs externos", "Espaço e remoção segura.", "\uE88E"),
            new("monitor", "Monitor em tempo real", "Clock, uso, tensões e temperaturas.", "\uE9D9"),
            new("bench", "Benchmark", "CPU, memória, disco e GPU em 3D.", "\uE9D9"),
            new("compare", "Antes e depois", "Mede o efeito real de cada mudança.", "\uE9D9"),
            new("stress", "Teste de estresse", "Carga no hardware, por núcleo ou geral.", "\uE7BA"),
            new("fans", "Ventoinhas e refrigeração", "Velocidade das ventoinhas, bomba e cooler, com curva por temperatura.", "\uE9CA"),
            new("rgb", "Iluminação RGB", "Cores dos LEDs RGB e ARGB do PC (via OpenRGB).", "\uE790"),
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
            new("security", "Segurança", "Antivírus, firewall e controle de contas.", "\uE72E"),
            new("diskmgr", "Gerenciador de discos", "Crie, formate, divida e organize partições; troque letras e prepare discos novos.", "\uEDA2"),
            new("repair", "Reparo do Windows", "SFC, DISM, CHKDSK e correções de Windows Update, loja e rede.", "\uE90F"),
        }),
        new("sup", "Suporte", "Ajuda do suporte da MB InfoArt por acesso remoto.", "\uE8F2", new Tile[]
        {
            new("remote", "Acesso remoto", "Seu ID, conexão com outro PC (tela, mouse, teclado e som).", "\uE8AF"),
            new("support", "Pedir suporte", "Descreva o problema e envie o relatório do PC; AnyDesk como alternativa.", "\uE8F2"),
        }),
        new("tools", "Ferramentas", "Processos, arquivos grandes e duplicados, e mais.", "\uE90F", new Tile[]
        {
            new("processes", "Processos", "Quem usa memória e processador, com encerramento seguro.", "\uE9D9"),
            new("largefiles", "Arquivos grandes", "Ache o que está ocupando espaço.", "\uE8B7"),
            new("duplicates", "Arquivos duplicados", "Cópias idênticas e espaço recuperável.", "\uE8C8"),
            new("cleanextras", "Pastas vazias e atalhos inválidos", "Limpeza de lixo que sobra no perfil.", "\uE74D"),
            new("shredder", "Destruir arquivos", "Apaga de forma definitiva, sem recuperação.", "\uE74D"),
            new("autoshutdown", "Desligamento automático", "Desliga ou reinicia o PC no horário.", "\uE7E8"),
        }),
        new("lic", "Licença", "Plano, ativação e conta.", "\uE8D7", new Tile[]
        {
            new("license", "Licença e conta", "Entre com a conta do site para ativar o plano Pro.", "\uE8D7"),
        }),
        new("drv", "Drivers e programas", "Atualizações por fontes oficiais.", "\uE896", new Tile[]
        {
            new("driver-removal", "Remover driver de vídeo", "Remoção completa do driver NVIDIA, AMD ou Intel para reinstalar limpo.", "\uE74D"),
            new("drivers", "Drivers", "Placa de vídeo (NVIDIA, AMD, Intel) e Windows Update.", "\uE950"),
            new("recommended", "Programas recomendados", "Ferramentas gratuitas que combinam com o MB Extreme.", "\uE734"),
            new("programs", "Programas", "Atualize e instale ferramentas pelo winget.", "\uE896"),
        }),
    };

    public static Module? ModuleOf(string pageKey) =>
        Modules.FirstOrDefault(m => m.Key == pageKey || m.Tiles.Any(t => t.Key == pageKey));

    public static Tile? TileOf(string pageKey) =>
        Modules.SelectMany(m => m.Tiles).FirstOrDefault(t => t.Key == pageKey);
}
