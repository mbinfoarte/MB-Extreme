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
            new("undo", "Central Desfazer", "Histórico de tudo que o programa mudou, com volta ao valor original.", "\uE7A7", "Sistema e segurança"),
            new("diskmgr", "Gerenciador de discos", "Crie, formate, divida e organize partições; troque letras e prepare discos novos.", "\uEDA2", "Discos e dispositivos"),
            new("repair", "Reparo do Windows", "SFC, DISM, CHKDSK e correções de Windows Update, loja e rede.", "\uE90F", "Discos e dispositivos"),
            new("devices", "Dispositivos conectados", "Lista de USB e de todo o hardware.", "\uE88E", "Discos e dispositivos"),
            new("fakecheck", "Pendrive original?", "Descobre se um pendrive ou cartão tem a capacidade falsa (escreve e confere o espaço livre).", "\uE88E", "Discos e dispositivos"),
            new("ghosts", "Cache de portas USB", "Remove registros de USB que não existem mais.", "\uE74D", "Discos e dispositivos"),
            new("uninstaller", "Desinstalador", "Programas instalados e aplicativos do Windows: remova vários de uma vez.", "\uE74D", "Drivers e programas"),
            new("software", "Drivers e programas", "Atualize drivers e programas, instale ferramentas e remova o driver de vídeo, tudo num lugar só.", "\uE896", "Drivers e programas"),
            new("processes", "Processos", "Quem usa memória e processador, com encerramento seguro.", "\uE9D9", "Ferramentas"),
            new("diskmap", "Mapa do disco", "Mapa colorido de quais pastas e arquivos estão enchendo o disco.", "\uE8B7", "Ferramentas"),
            new("largefiles", "Arquivos grandes", "Ache o que está ocupando espaço.", "\uE8B7", "Ferramentas"),
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
            new("license", "Licença e conta", "Entre com a conta do site para ativar o plano Pro.", "\uE8D7", ""),
        }),
    };

    /// <summary>
    /// Menu lateral: itens diretos agrupados em seções (como um painel de controle). Os módulos completos continuam
    /// em "Todas as ferramentas".
    /// </summary>
    public static IReadOnlyList<SideSection> Sidebar { get; } = new SideSection[]
    {
        new("", new SideItem[]
        {
            new("home", "Painel", "\uE80F"),
            new("postinstall", "Pós-formatação", "\uE896"),
            new("programs", "Programas", "\uE71D"),
        }),
        new("MEU PC", new SideItem[]
        {
            new("sysinfo", "Meu PC", "\uE7F8"),
            new("storage", "Saúde dos discos", "\uEDA2"),
            new("win11", "Windows 11", "\uE770"),
            new("drivers", "Drivers", "\uE964"),
            new("wallpaper", "Papel de parede", "\uE771"),
            new("speedtest", "Rede", "\uE774"),
        }),
        new("TESTES", new SideItem[]
        {
            new("bench", "Benchmark", "\uE9D9"),
            new("periphtest", "Periféricos", "\uE765"),
            new("padcalib", "Calibrar controle", "\uE7FC"),
        }),
        new("OTIMIZAR", new SideItem[]
        {
            new("cleanup", "Limpeza", "\uE74D"),
            new("diskmap", "Mapa do disco", "\uE8B7"),
            new("duplicates", "Arquivos duplicados", "\uE8C8"),
            new("tweaks-all", "Otimizações", "\uE945"),
            new("uninstaller", "Desinstalador", "\uE8BB"),
            new("startup", "Inicialização", "\uE7E8"),
            new("memory", "Memória", "\uE950"),
        }),
        new("JOGOS E ESTÚDIO", new SideItem[]
        {
            new("gamelibrary", "Biblioteca de jogos", "\uE7FC"),
            new("bottleneck", "Gargalos", "\uE9D9"),
            new("record", "Gravação e live", "\uE714"),
            new("shorts", "Vídeos curtos", "\uE8B2"),
        }),
        new("SEGURANÇA", new SideItem[]
        {
            new("repair", "Reparar o Windows", "\uE90F"),
            new("restorepoints", "Pontos de restauração", "\uE81C"),
            new("undo", "Central Desfazer", "\uE7A7"),
            new("formatbackup", "Backup para formatar", "\uE74E"),
            new("security", "Proteção do Windows", "\uE72E"),
        }),
        new("MB INFOARTE", new SideItem[]
        {
            new("social", "Nossas redes", "\uE716"),
            new("suggest", "Sugestões", "\uE8BD"),
            new("support", "Suporte", "\uE8F2"),
            new("about", "Sobre", "\uE946"),
        }),
    };

    /// <summary>Tela de atalho do menu lateral que representa a página (a própria ou nenhuma).</summary>
    public static string? SideKeyOf(string pageKey) =>
        Sidebar.SelectMany(s => s.Items).Any(i => i.Key == pageKey) ? pageKey : null;

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
