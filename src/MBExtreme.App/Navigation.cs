namespace MBExtreme.UI;

/// <summary>Um atalho dentro de um módulo (abre uma página).</summary>
/// <summary>Section: título do subgrupo dentro do módulo (vazio = sem subgrupo).</summary>
internal sealed record Tile(string Key, string Title, string Description, string Icon, string Section = "");

/// <summary>Um módulo do menu lateral. Ao clicar, mostra os atalhos dele.</summary>
internal sealed record Module(string Key, string Title, string Subtitle, string Icon, IReadOnlyList<Tile> Tiles);

internal static class NavigationMap
{
    // Ícones: fonte "Segoe MDL2 Assets", que já vem no Windows 10 e 11.
    public static IReadOnlyList<Module> Modules { get; } = new Module[]
    {
        new("opt", "Otimização", "Deixe o computador mais rápido e leve: limpeza, desempenho, jogos e rede.", "\uE945", new Tile[]
        {
            new("cleanup", "Limpeza", "Procura lixo, temporários, cache e libera memória.", "\uE74D", "Limpeza e espaço"),
            new("weekly", "Limpeza semanal", "Limpeza automática todo domingo, sem abrir janela.", "\uE74D", "Limpeza e espaço"),
            new("memory", "Memória", "Limpe a RAM e proteja aplicativos.", "\uE950", "Limpeza e espaço"),
            new("storage", "Armazenamento", "SSD, HD e saúde dos discos.", "\uEDA2", "Limpeza e espaço"),
            new("tweaks-storage", "Ajustes de armazenamento", "TRIM, hibernação, último acesso e nomes 8.3.", "\uEDA2", "Limpeza e espaço"),
            new("profiles", "Perfis", "Jogo, Competitivo e PC fraco em um clique.", "\uE945", "Desempenho"),
            new("startup", "Inicialização", "Programas que abrem junto com o Windows.", "\uE7E8", "Desempenho"),
            new("tweaks-perf", "Desempenho", "Prioridade de CPU, energia e serviços.", "\uE9D9", "Desempenho"),
            new("visual", "Efeitos visuais", "Transparência e animações do Windows.", "\uE713", "Desempenho"),
            new("gamesettings", "Jogos e energia", "Modo de Jogo do Windows, agendamento de GPU e plano de energia.", "\uE713", "Jogos"),
            new("tweaks-game", "Ajustes para jogos", "Prioridade, gravação em segundo plano, energia e mais.", "\uE9D9", "Jogos"),
            new("latency", "Diagnóstico de latência", "Acha o driver que causa micro-travadas.", "\uE9D9", "Jogos"),
            new("dns", "DNS", "Teste e escolha o servidor mais rápido.", "\uE774", "Rede"),
            new("net-tweaks", "Otimizações de rede", "Ajustes do Windows para a rede.", "\uE713", "Rede"),
        }),
        new("games", "Jogos", "Sua biblioteca, o Modo Jogo e o que limita o seu FPS.", "\uE7FC", new Tile[]
        {
            new("gamelibrary", "Biblioteca de jogos", "Seus jogos instalados, com Modo Jogo e prioridade por jogo.", "\uE7FC", ""),
            new("gameboost", "Modo Jogo", "Prepara o PC quando um jogo abre.", "\uE7FC", ""),
            new("gameadvisor", "Assistente de jogo", "Configurações ideais para o seu hardware.", "\uE770", ""),
            new("bottleneck", "Monitor de gargalos", "O que limita o seu FPS: placa, processador, memória, disco ou calor.", "\uE9D9", ""),
        }),
        new("studio", "Gravação e transmissão", "Grave a tela, transmita ao vivo, salve replays e crie vídeos curtos.", "\uE714", new Tile[]
        {
            new("record", "Gravação e transmissão", "Grave, transmita ao vivo e salve replays, com áudio, câmera e escolha de janela.", "\uE714", ""),
            new("shorts", "Vídeos curtos (Shorts e Reels)", "Corte um vídeo longo em vários vídeos verticais, na mão ou pelos momentos mais fortes.", "\uE8B2", ""),
        }),
        new("test", "Testes e hardware", "Meça o PC, veja sensores e as peças, a BIOS e a rede.", "\uE9D9", new Tile[]
        {
            new("gamefps", "Jogo (FPS)", "Mede um jogo: FPS, 1% pior, gráfico de tempo de quadro, engasgos e sensores.", "\uE7FC", "Medir desempenho"),
            new("bench", "Benchmark do PC", "CPU, memória, disco e GPU em 3D.", "\uE9D9", "Medir desempenho"),
            new("stress", "Teste de estresse", "CPU, memória e GPU 3D com monitoramento ao vivo, erros e relatório.", "\uE7BA", "Medir desempenho"),
            new("compare", "Antes e depois", "Mede o efeito real de cada mudança.", "\uE9D9", "Medir desempenho"),
            new("fps", "Painel de FPS sobre o jogo", "Overlay com FPS, temperatura e uso.", "\uE9D9", "Medir desempenho"),
            new("netanalyzer", "Analisador de rede", "Ping, TCPing, MTR e jitter.", "\uE9D9", "Rede"),
            new("wifi", "Diagnóstico de Wi-Fi", "Sinal, banda e dicas.", "\uE774", "Rede"),
            new("sysinfo", "Informações do sistema", "Estilo CPU-Z, GPU-Z e AIDA.", "\uE770", "Informações e sensores"),
            new("cpu-detail", "Processador", "Uso, temperatura, velocidade e energia, por núcleo.", "\uE950", "Informações e sensores"),
            new("gpu-detail", "Placa de vídeo", "Uso, temperatura, velocidade, energia e memória de vídeo.", "\uE7F4", "Informações e sensores"),
            new("ram-detail", "Memória", "Pentes, uso em tempo real e limpeza automática.", "\uE950", "Informações e sensores"),
            new("disks", "Discos", "Modelo, velocidade e espaço livre e usado.", "\uEDA2", "Informações e sensores"),
            new("removable", "Pendrives e HDs externos", "Espaço e remoção segura.", "\uE88E", "Informações e sensores"),
            new("monitor", "Monitor em tempo real", "Clock, uso, tensões e temperaturas.", "\uE9D9", "Informações e sensores"),
            new("fans", "Ventoinhas e refrigeração", "Velocidade das ventoinhas, bomba e cooler, com curva por temperatura.", "\uE9CA", "Informações e sensores"),
            new("rgb", "Iluminação RGB", "Cores dos LEDs RGB e ARGB do PC (via OpenRGB).", "\uE790", "Informações e sensores"),
            new("hardware", "Meu hardware", "O que melhorar: memória, Windows, BIOS e mais.", "\uE950", "Hardware e BIOS"),
            new("bios", "BIOS e atualizações", "Versão da BIOS e onde achar a mais nova.", "\uE896", "Hardware e BIOS"),
            new("recipe", "Receita de BIOS", "XMP/EXPO, Resizable BAR e PBO para o seu PC.", "\uE713", "Hardware e BIOS"),
        }),
        new("win", "Windows", "Sistema, discos, dispositivos, ferramentas, drivers e programas.", "\uE713", new Tile[]
        {
            new("privacy", "Privacidade", "Anúncios, histórico, Copilot e SmartScreen.", "\uE72E", "Sistema e segurança"),
            new("debloat", "Aplicativos", "Remova apps do Windows que você não usa.", "\uE74D", "Sistema e segurança"),
            new("boot", "Inicialização e boot", "Como o Windows liga e desliga.", "\uE7E8", "Sistema e segurança"),
            new("periph", "Periféricos", "Mouse e teclado.", "\uE765", "Sistema e segurança"),
            new("security", "Segurança", "Antivírus, firewall e controle de contas.", "\uE72E", "Sistema e segurança"),
            new("undo", "Central Desfazer", "Histórico de tudo que o programa mudou, com volta ao valor original.", "\uE7A7", "Sistema e segurança"),
            new("diskmgr", "Gerenciador de discos", "Crie, formate, divida e organize partições; troque letras e prepare discos novos.", "\uEDA2", "Discos e dispositivos"),
            new("repair", "Reparo do Windows", "SFC, DISM, CHKDSK e correções de Windows Update, loja e rede.", "\uE90F", "Discos e dispositivos"),
            new("devices", "Dispositivos conectados", "Lista de USB e de todo o hardware.", "\uE88E", "Discos e dispositivos"),
            new("ghosts", "Cache de portas USB", "Remove registros de USB que não existem mais.", "\uE74D", "Discos e dispositivos"),
            new("software", "Drivers e programas", "Atualize drivers e programas, instale ferramentas e remova o driver de vídeo, tudo num lugar só.", "\uE896", "Drivers e programas"),
            new("processes", "Processos", "Quem usa memória e processador, com encerramento seguro.", "\uE9D9", "Ferramentas"),
            new("largefiles", "Arquivos grandes", "Ache o que está ocupando espaço.", "\uE8B7", "Ferramentas"),
            new("duplicates", "Arquivos duplicados", "Cópias idênticas e espaço recuperável.", "\uE8C8", "Ferramentas"),
            new("cleanextras", "Pastas vazias e atalhos inválidos", "Limpeza de lixo que sobra no perfil.", "\uE74D", "Ferramentas"),
            new("shredder", "Destruir arquivos", "Apaga de forma definitiva, sem recuperação.", "\uE74D", "Ferramentas"),
            new("autoshutdown", "Desligamento automático", "Desliga ou reinicia o PC no horário.", "\uE7E8", "Ferramentas"),
        }),
        new("sup", "Suporte", "Ajuda do suporte da MB InfoArt por acesso remoto.", "\uE8F2", new Tile[]
        {
            new("remote", "Acesso remoto", "Seu ID, conexão com outro PC (tela, mouse, teclado e som).", "\uE8AF", ""),
            new("support", "Pedir suporte", "Descreva o problema e envie o relatório do PC; AnyDesk como alternativa.", "\uE8F2", ""),
        }),
        new("lic", "Licença", "Plano, ativação e conta.", "\uE8D7", new Tile[]
        {
            new("license", "Licença e conta", "Entre com a conta do site para ativar o plano Pro.", "\uE8D7", ""),
        }),
    };

    public static Module? ModuleOf(string pageKey) =>
        Modules.FirstOrDefault(m => m.Key == pageKey || m.Tiles.Any(t => t.Key == pageKey));

    public static Tile? TileOf(string pageKey) =>
        Modules.SelectMany(m => m.Tiles).FirstOrDefault(t => t.Key == pageKey);
}
