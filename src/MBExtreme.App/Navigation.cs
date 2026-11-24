namespace MBExtreme.UI;

/// <summary>Um atalho dentro de um módulo (abre uma página).</summary>
/// <summary>Section: título do subgrupo dentro do módulo (vazio = sem subgrupo).</summary>
internal sealed record Tile(string Key, string Title, string Description, string Icon, string Section = "");

/// <summary>Um módulo do menu lateral. Ao clicar, mostra os atalhos dele.</summary>
internal sealed record Module(string Key, string Title, string Subtitle, string Icon, IReadOnlyList<Tile> Tiles);

/// <summary>Um item do menu lateral.</summary>
internal sealed record SideItem(string Key, string Title, string Icon);

/// <summary>Uma seção do menu lateral (título em maiúsculas; vazio = sem título).</summary>
internal sealed record SideSection(string Header, IReadOnlyList<SideItem> Items);

internal static class NavigationMap
{
    // Ícones: fonte "Segoe MDL2 Assets", que já vem no Windows 10 e 11.
    public static IReadOnlyList<Module> Modules { get; } = new Module[]
    {
        new("opt", "Otimização", "Deixe o computador mais rápido e leve: limpeza, desempenho, jogos e rede.", "\uE945", new Tile[]
        {
            new("diagnose", "Painel inteligente", "Analisa o PC sem mudar nada e corrige só o que você escolher.", "\uE9D9", "Diagnóstico"),
            new("postinstall", "Pós-formatação", "Prepara um Windows recém-instalado: perfis Leve, Equilibrado e Extremo.", "\uE7E8", "Diagnóstico"),
            new("cleanup", "Limpeza", "Procura lixo, temporários, cache e libera memória.", "\uE74D", "Limpeza e espaço"),
            new("weekly", "Limpeza semanal", "Limpeza automática todo domingo, sem abrir janela.", "\uE74D", "Limpeza e espaço"),
            new("memory", "Memória", "Limpe a RAM e proteja aplicativos.", "\uE950", "Limpeza e espaço"),
            new("storage", "Armazenamento", "SSD, HD e saúde dos discos.", "\uEDA2", "Limpeza e espaço"),
            new("tweaks-storage", "Ajustes de armazenamento", "TRIM, hibernação, último acesso e nomes 8.3.", "\uEDA2", "Limpeza e espaço"),
            new("profiles", "Perfis", "Jogo, Competitivo e PC fraco em um clique.", "\uE945", "Desempenho"),
            new("startup", "Inicialização", "Programas que abrem junto com o Windows.", "\uE7E8", "Desempenho"),
            new("tweaks-all", "Otimizações", "Todos os ajustes do Windows num lugar só, com selos de risco e volta pela Central Desfazer.", "\uE945", "Desempenho"),
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
            new("speedtest", "Teste de internet", "Download, upload, latência e variação com várias conexões, e o histórico.", "\uE774", "Rede"),
            new("netanalyzer", "Analisador de rede", "Ping, TCPing, MTR e jitter.", "\uE9D9", "Rede"),
            new("periphtest", "Teste de periféricos", "Teclado, mouse (taxa e clique duplo), tela, som, microfone e webcam.", "\uE765", "Periféricos"),
            new("padcalib", "Calibrar controle", "Corrige o drift do DualSense e do DualShock 4 gravando a calibração no controle, com laudo para o cliente.", "\uE7FC", "Periféricos"),
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
            new("win11", "Windows 11", "Confere se o PC atende aos requisitos do Windows 11 e o que falta.", "\uE770", "Hardware e BIOS"),
            new("bios", "BIOS e atualizações", "Versão da BIOS e onde achar a mais nova.", "\uE896", "Hardware e BIOS"),
            new("recipe", "Receita de BIOS", "XMP/EXPO, Resizable BAR e PBO para o seu PC.", "\uE713", "Hardware e BIOS"),
            new("bios-tune", "Otimização de BIOS", "Plano de BIOS pelo seu hardware: EXPO/XMP, Resizable BAR, PBO/Curve Optimizer, undervolt e temperatura.", "\uE950", "Hardware e BIOS"),
        }),
        new("win", "Windows", "Sistema, discos, dispositivos, ferramentas, drivers e programas.", "\uE713", new Tile[]
        {
            new("privacy", "Privacidade", "Anúncios, histórico, Copilot e SmartScreen.", "\uE72E", "Sistema e segurança"),
            new("debloat", "Aplicativos", "Remova apps do Windows que você não usa.", "\uE74D", "Sistema e segurança"),
            new("boot", "Inicialização e boot", "Como o Windows liga e desliga.", "\uE7E8", "Sistema e segurança"),
            new("periph", "Periféricos", "Mouse e teclado.", "\uE765", "Sistema e segurança"),
            new("security", "Segurança", "Antivírus, firewall e controle de contas.", "\uE72E", "Sistema e segurança"),
            new("restorepoints", "Pontos de restauração", "Veja, crie, apague e volte o Windows para um ponto de restauração.", "\uE81C", "Sistema e segurança"),
            new("formatbackup", "Backup para formatar", "Salva drivers e redes Wi-Fi antes de formatar e devolve tudo depois.", "\uE74E", "Sistema e segurança"),
            new("iso-create", "Criar ISO personalizada", "Gera a sua ISO do Windows a partir da oficial: edição, instalação automática, sem apps inúteis, drivers e kit de programas.", "\uE958", "Sistema e segurança"),
            new("reinstall", "Reinstalar sem pendrive", "Abre o instalador do Windows no próximo reinício usando uma partição auxiliar no próprio disco.", "\uE777", "Sistema e segurança"),
            new("undo", "Central Desfazer", "Histórico de tudo que o programa mudou, com volta ao valor original.", "\uE7A7", "Sistema e segurança"),
            new("diskmgr", "Gerenciador de discos", "Crie, formate, divida e organize partições; troque letras e prepare discos novos.", "\uEDA2", "Discos e dispositivos"),
            new("repair", "Reparo do Windows", "SFC, DISM, CHKDSK e correções de Windows Update, loja e rede.", "\uE90F", "Discos e dispositivos"),
            new("devices", "Dispositivos conectados", "Lista de USB e de todo o hardware.", "\uE88E", "Discos e dispositivos"),
            new("fakecheck", "Pendrive original?", "Descobre se um pendrive ou cartão tem a capacidade falsa (escreve e confere o espaço livre).", "\uE88E", "Discos e dispositivos"),
            new("ghosts", "Cache de portas USB", "Remove registros de USB que não existem mais.", "\uE74D", "Discos e dispositivos"),
            new("uninstaller", "Desinstalador", "Programas instalados e aplicativos do Windows: remova vários de uma vez.", "\uE74D", "Drivers e programas"),
            new("software", "Drivers e programas", "Atualize drivers e programas, instale ferramentas e remova o driver de vídeo, tudo num lugar só.", "\uE896", "Drivers e programas"),
            new("processes", "Processos", "Quem usa memória e processador, com encerramento seguro.", "\uE9D9", "Ferramentas"),
            new("overlay-setup", "Contador na tela do jogo", "FPS, 1% e 0,1% pior, gráfico e sensores por cima do jogo (estilo RivaTuner).", "\uE7F4", "Ferramentas"),
            new("proc-profiles", "Processos e inicialização", "Exame automático e perfis de um clique: Gamer, Turbo, Trabalho, Notebook e Streamer.", "\uE7E8", "Ferramentas"),
            new("diskmap", "Mapa do disco", "Mapa colorido de quais pastas e arquivos estão enchendo o disco.", "\uE8B7", "Ferramentas"),
            new("largefiles", "Arquivos grandes", "Ache o que está ocupando espaço.", "\uE8B7", "Ferramentas"),
            new("registry", "Limpeza de registro", "Programas fantasma, inicialização quebrada e chaves vazias, com cópia antes e Desfazer.", "\uE74C", "Ferramentas"),
            new("duplicates", "Arquivos duplicados", "Cópias idênticas e espaço recuperável.", "\uE8C8", "Ferramentas"),
            new("cleanextras", "Pastas vazias e atalhos inválidos", "Limpeza de lixo que sobra no perfil.", "\uE74D", "Ferramentas"),
            new("shredder", "Destruir arquivos", "Apaga de forma definitiva, sem recuperação.", "\uE74D", "Ferramentas"),
            new("autoshutdown", "Desligamento automático", "Desliga ou reinicia o PC no horário.", "\uE7E8", "Ferramentas"),
        }),
        new("sup", "Suporte", "Ajuda do suporte da MB InfoArt por acesso remoto.", "\uE8F2", new Tile[]
        {
            new("a11y", "Acessibilidade e tamanho", "Interface maior ou menor, teclado e Narrador.", "\uE776", ""),
            new("social", "Nossas redes", "Canal, redes sociais, ofertas e parceiros com cupom.", "\uE716", ""),
            new("suggest", "Sugestões", "Mande uma ideia, avise de um problema ou elogie.", "\uE8BD", ""),
            new("about", "Sobre", "Versão, novidades, como o programa protege o seu PC e créditos.", "\uE946", ""),
            new("support", "Suporte e acesso remoto", "Passe o seu ID ao técnico ou digite o ID de outro PC para ajudar (tela, mouse, teclado e som), fale com o suporte e envie o relatório.", "\uE8F2", ""),
        }),
        new("lic", "Licença", "Plano, ativação e conta.", "\uE8D7", new Tile[]
        {
            new("license", "Licença e conta", "Entre com a conta do site para ativar o seu plano.", "\uE8D7", ""),
        }),
    };

    /// <summary>
    /// Menu lateral: Painel no início, os módulos em ordem alfabética e, separados no fim, os de informação (Nossas Redes e Sobre).
    /// Módulos com várias ferramentas abrem uma tela de abas (veja <see cref="Hubs"/>).
    /// </summary>
    public static IReadOnlyList<SideSection> Sidebar { get; } = new SideSection[]
    {
        new("", new SideItem[]
        {
            new("home", "Painel", "\uE80F"),
            new("bench", "Benchmark", "\uE9D9"),
            new("gamelibrary", "Biblioteca de Jogos", "\uE7FC"),
            new("hub-control", "Controle", "\uE7FC"),
            new("shorts", "Editor de Vídeos", "\uE8B2"),
            new("record", "Gravação e Lives", "\uE714"),
            new("hub-meupc", "Meu PC", "\uE7F8"),
            new("hub-otimizar", "Otimizar", "\uE945"),
            new("hub-perif", "Periféricos", "\uE765"),
            new("hub-personal", "Personalização", "\uE771"),
            new("hub-format", "Pós-Formatação", "\uE896"),
            new("programs", "Programas", "\uE71D"),
            new("hub-seguranca", "Segurança", "\uE72E"),
        }),
        // informação (não são módulos do sistema): sempre no fim, separados por uma linha
        new("", new SideItem[]
        {
            new("admin", "Administrador", "\uE7EF"),   // só aparece para a conta de administrador (AdminGate)
            new("social", "Nossas Redes", "\uE716"),
            new("support", "Suporte", "\uE8F2"),
            new("hub-sobre", "Sobre", "\uE946"),
        }),
    };

    /// <summary>
    /// Telas que juntam várias ferramentas em abas (chave da tela → abas em ordem). Abrir uma ferramenta que está numa delas
    /// (pelo Painel, pela busca, por um atalho) abre a tela com a aba certa e destaca o item no menu. Uma aba pode ser outra
    /// tela de abas (ex.: Otimizar › Limpeza › Memória).
    /// </summary>
    public static IReadOnlyDictionary<string, (string Title, string Key)[]> Hubs { get; } = new Dictionary<string, (string, string)[]>
    {
        // Administrador: só a conta de administrador vê no menu (o servidor confere a conta em cada chamada)
        ["admin"] = new[] { ("Atendimento técnico", "service"), ("Site", "admin-site"), ("Suporte ao vivo", "admin-live") },
        ["programs"] = new[] { ("Instalar", "programs-install"), ("Atualizar", "programs-installed"), ("Desinstalador", "uninstaller"), ("Aplicativos do Windows", "debloat") },
        ["hub-control"] = new[]
        {
            ("Meu controle", "mbc-overview"), ("Calibrador", "padcalib"), ("Emulação", "mbc-emu"), ("Teclado e mouse", "mbc-kbm"),
            ("Mira do mouse", "mouse-aim"), ("Luz, gatilhos e vibração", "mbc-light"), ("Ajustes finos", "mbc-tune"),
            ("Desempenho e latência", "mbc-perf"),
        },
        ["mbc-perf"] = new[]
        {
            ("Analisador", "pad-analyzer"), ("Portas USB", "usb-ports"), ("Controle competitivo", "pad-competitive"), ("Overclock 1000 Hz", "pad-overclock"),
        },
        ["hub-meupc"] = new[] { ("Resumo do PC", "sysinfo"), ("Componentes", "hardware"), ("Monitor e Hz", "monitor-hz"), ("Windows", "win11") },
        ["hub-perif"] = new[]
        {
            ("Teste de periféricos", "periphtest"), ("Limpar cache de USB", "ghosts"), ("Dispositivos conectados", "devices"),
            ("Pendrives e HDs externos", "removable"), ("Pendrive original?", "fakecheck"),
        },
        ["hub-personal"] = new[] { ("Papel de parede", "wallpaper"), ("Iluminação RGB", "rgb"), ("Coolers, fans e controladora", "fans") },
        ["hub-otimizar"] = new[]
        {
            ("Processos e inicialização", "hub-startup"), ("Limpeza", "hub-clean"), ("Otimizações", "tweaks-all"), ("Discos", "hub-disks"),
            ("Drivers", "drivers"), ("BIOS", "bios-tune"), ("Rede", "speedtest"), ("Reparo do Windows", "repair"),
        },
        ["hub-startup"] = new[] { ("Otimizar", "proc-profiles"), ("Programas da inicialização", "startup"), ("Processos abertos", "processes"), ("Serviços", "services") },
        ["hub-clean"] = new[] { ("Limpeza", "cleanup"), ("Memória", "memory"), ("Registro", "registry"), ("Arquivos duplicados", "duplicates") },
        ["hub-disks"] = new[] { ("Saúde, desfragmentar e TRIM", "storage"), ("Mapa do disco", "diskmap") },
        ["hub-seguranca"] = new[] { ("Proteção do Windows", "security"), ("Pontos de restauração", "restorepoints"), ("Central Desfazer", "undo") },
        ["hub-format"] = new[] { ("Pós-Formatação", "postinstall"), ("Criar ISO", "iso-create"), ("Reinstalar sem pendrive", "reinstall"), ("Backup para formatar", "formatbackup") },
        ["hub-sobre"] = new[] { ("Sobre", "about"), ("Sugestões", "suggest") },
        ["hub-fps"] = new[] { ("Gargalos", "bottleneck"), ("Medidor de FPS", "gamefps") },
    };

    /// <summary>
    /// Ícone (Segoe MDL2 Assets) e uma linha curta do que cada ferramenta das telas de abas faz, para a barra de ferramentas do módulo
    /// e as abas internas. Toda aba de <see cref="Hubs"/> tem que estar aqui (o teste confere).
    /// </summary>
    public static IReadOnlyDictionary<string, (string Icon, string Hint)> TabInfo { get; } = new Dictionary<string, (string, string)>
    {
        ["programs-install"] = ("\uE896", "kits e programas"), ["programs-installed"] = ("\uE777", "versões novas"), ["uninstaller"] = ("\uE74D", "remover e limpar restos"), ["debloat"] = ("\uE71D", "apps que vêm no Windows"),
        ["mbc-overview"] = ("\uE7FC", "bateria, firmware e perfil"), ["padcalib"] = ("\uE9D9", "analógicos e gatilhos"), ["mbc-emu"] = ("\uE7FC", "Xbox, PS4 e PS5 virtuais"), ["mbc-kbm"] = ("\uE765", "jogar no teclado e mouse"),
        ["mouse-aim"] = ("\uE765", "sensibilidade e curva"), ["mbc-light"] = ("\uE945", "LED, gatilhos e rumble"), ["mbc-tune"] = ("\uE713", "zona morta e resposta"),
        ["mbc-perf"] = ("\uE9D9", "latência, USB e 1000 Hz"), ["pad-analyzer"] = ("\uE9D9", "cabo × Bluetooth"), ["usb-ports"] = ("\uE88E", "a melhor porta"),
        ["pad-competitive"] = ("\uE945", "futebol e corrida"), ["pad-overclock"] = ("\uE7FC", "DualSense e DS4"),
        ["sysinfo"] = ("\uE7F8", "peças e Windows"), ["hardware"] = ("\uE950", "CPU, GPU, RAM e discos"), ["monitor-hz"] = ("\uE7F4", "taxa de atualização"), ["win11"] = ("\uE7F8", "versão e requisitos"),
        ["periphtest"] = ("\uE765", "teclado, mouse, som e câmera"), ["ghosts"] = ("\uE88E", "registros de USB antigos"), ["devices"] = ("\uE88E", "tudo que está ligado"), ["removable"] = ("\uEDA2", "pendrives e HDs"), ["fakecheck"] = ("\uE9D9", "capacidade falsa?"),
        ["wallpaper"] = ("\uE771", "temas em alta resolução"), ["rgb"] = ("\uE771", "cores e efeitos"), ["fans"] = ("\uE945", "curvas e rotação"),
        ["hub-startup"] = ("\uE7E8", "inicialização e serviços"), ["hub-clean"] = ("\uE74D", "lixo, memória e registro"), ["tweaks-all"] = ("\uE713", "ajustes do Windows"), ["hub-disks"] = ("\uEDA2", "saúde e TRIM"),
        ["drivers"] = ("\uE896", "atualizar e limpar"), ["bios-tune"] = ("\uE950", "ajustes e guia"), ["speedtest"] = ("\uE9D9", "velocidade e ping"), ["repair"] = ("\uE90F", "SFC, DISM e CHKDSK"),
        ["proc-profiles"] = ("\uE945", "perfis de um clique"), ["startup"] = ("\uE7E8", "o que liga com o PC"), ["processes"] = ("\uE9D9", "o que está aberto"), ["services"] = ("\uE713", "serviços do Windows"),
        ["cleanup"] = ("\uE74D", "lixo e temporários"), ["memory"] = ("\uE950", "RAM livre"), ["registry"] = ("\uE713", "chaves quebradas"), ["duplicates"] = ("\uE8B7", "arquivos repetidos"),
        ["storage"] = ("\uEDA2", "saúde, desfragmentar e TRIM"), ["diskmap"] = ("\uE8B7", "o que ocupa espaço"),
        ["security"] = ("\uE72E", "antivírus e firewall"), ["restorepoints"] = ("\uE81C", "voltar o Windows"), ["undo"] = ("\uE7A7", "desfazer ajustes"),
        ["postinstall"] = ("\uE896", "Windows novo pronto"), ["iso-create"] = ("\uE958", "a sua ISO do Windows"), ["reinstall"] = ("\uE777", "instalar pelo próprio disco"), ["formatbackup"] = ("\uE74E", "drivers e Wi-Fi"),
        ["support"] = ("\uE716", "acesso remoto"), ["service"] = ("\uE90F", "empresas e visitas"), ["admin-site"] = ("\uE774", "painel do site"), ["admin-live"] = ("\uE8F2", "quem pediu ajuda"), ["suggest"] = ("\uE946", "ideias e pedidos"), ["about"] = ("\uE946", "versão e créditos"),
        ["bottleneck"] = ("\uE9D9", "o que segura o FPS"), ["gamefps"] = ("\uE7FC", "FPS e 1% low"),
    };

    /// <summary>Título e ícone do módulo de uma tela de abas: o item do menu lateral ou, se for aba de outra tela, o nome dessa aba.</summary>
    public static (string Title, string Icon) HubTitle(string hubKey)
    {
        var side = Sidebar.SelectMany(x => x.Items).FirstOrDefault(i => i.Key == hubKey);
        if (side is not null) return (side.Title, side.Icon);
        var parent = Hubs.Values.SelectMany(t => t).FirstOrDefault(t => t.Key == hubKey);
        string title = parent.Title ?? hubKey;
        return (title, TabInfo.TryGetValue(hubKey, out var info) ? info.Icon : "\uE8A9");
    }

    /// <summary>Caminho de telas de abas até a ferramenta, da mais de fora para a mais de dentro (vazio se não estiver em nenhuma).</summary>
    public static IReadOnlyList<string> HubPath(string pageKey)
    {
        var path = new List<string>();
        string? current = pageKey;
        while (HubOf(current) is { } hub && !path.Contains(hub))
        {
            path.Insert(0, hub);
            current = hub;
        }
        return path;
    }

    /// <summary>Tela de abas que contém a ferramenta (ou null).</summary>
    public static string? HubOf(string pageKey) =>
        Hubs.FirstOrDefault(h => h.Value.Any(t => t.Key == pageKey)).Key;

    /// <summary>Tela de atalho do menu lateral que representa a página (a própria ou nenhuma).</summary>
    public static string? SideKeyOf(string pageKey)
    {
        var items = Sidebar.SelectMany(s => s.Items).Select(i => i.Key).ToHashSet();
        if (items.Contains(pageKey)) return pageKey;
        return HubPath(pageKey).FirstOrDefault(items.Contains);
    }

    /// <summary>
    /// Busca do menu: acha qualquer ferramenta pelo nome, pela descrição ou pelo grupo, sem ligar para acentos e maiúsculas.
    /// Nome que começa com o texto vem primeiro, depois nome que contém, depois descrição/grupo. Substitui a antiga "Todas as ferramentas".
    /// </summary>
    public static IReadOnlyList<(Tile Tile, Module Module)> Search(string? query, int max = 12)
    {
        string q = Fold(query);
        if (q.Length < 2) return Array.Empty<(Tile, Module)>();
        var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return Modules
            .SelectMany(m => m.Tiles.Select(t => (Tile: t, Module: m)))
            .Select(x =>
            {
                string title = Fold(x.Tile.Title), rest = Fold(x.Tile.Description + " " + x.Tile.Section + " " + x.Module.Title);
                if (!words.All(w => title.Contains(w, StringComparison.Ordinal) || rest.Contains(w, StringComparison.Ordinal))) return (x, Score: -1);
                int score = title.StartsWith(q, StringComparison.Ordinal) ? 0 : title.Contains(q, StringComparison.Ordinal) ? 1 : words.All(w => title.Contains(w, StringComparison.Ordinal)) ? 2 : 3;
                return (x, Score: score);
            })
            .Where(r => r.Score >= 0)
            .OrderBy(r => r.Score).ThenBy(r => r.x.Tile.Title, StringComparer.CurrentCulture)
            .Select(r => r.x)
            .GroupBy(x => x.Tile.Key).Select(g => g.First())
            .Take(max)
            .ToList();
    }

    private static string Fold(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var normalized = text.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(normalized.Length);
        foreach (char c in normalized)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString();
    }

    public static Module? ModuleOf(string pageKey) =>
        Modules.FirstOrDefault(m => m.Key == pageKey || m.Tiles.Any(t => t.Key == pageKey));

    public static Tile? TileOf(string pageKey) =>
        Modules.SelectMany(m => m.Tiles).FirstOrDefault(t => t.Key == pageKey);
}
