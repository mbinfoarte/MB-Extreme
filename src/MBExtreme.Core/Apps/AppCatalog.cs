namespace MBExtreme.Core.Apps;

/// <summary>Um programa do catálogo de instalação (id do winget, categoria e os kits em que entra).</summary>
public sealed record CatalogEntry(string Id, string Title, string Description, string Category, params string[] Kits);

/// <summary>Kit pronto: um grupo de programas para um tipo de uso.</summary>
public sealed record AppKit(string Key, string Title, string Description, string Icon);

/// <summary>
/// Catálogo da tela Programas: os programas mais pedidos depois de formatar, por categoria, e kits prontos
/// (Essencial, Gamer, Escritório, Criador de conteúdo, Programador, Técnico). Tudo pelo winget, direto da fonte oficial de cada fabricante.
/// </summary>
public static class AppCatalog
{
    public static IReadOnlyList<AppKit> Kits { get; } = new AppKit[]
    {
        new("essencial", "Essencial", "Para qualquer PC: navegador, PDF, vídeo, compactador e conversa.", ""),
        new("gamer", "Gamer", "Lojas de jogos, Discord, gravação e as bibliotecas que os jogos pedem.", ""),
        new("escritorio", "Escritório", "Pacote Office grátis, PDF, reuniões e nuvem.", ""),
        new("criador", "Criador de conteúdo", "Gravar, editar vídeo, foto e áudio.", ""),
        new("programador", "Programador", "VS Code, Git, Python, Node.js e o Terminal.", ""),
        new("tecnico", "Técnico", "Diagnóstico, discos, pendrive de boot e acesso remoto.", ""),
    };

    public static IReadOnlyList<string> Categories { get; } = new[]
    {
        "Navegadores", "Comunicação", "Documentos", "Compactadores", "Vídeo e música", "Jogos", "Criação", "Programação", "Utilitários", "Diagnóstico", "Bibliotecas",
    };

    public static IReadOnlyList<CatalogEntry> All { get; } = new CatalogEntry[]
    {
        new("Google.Chrome", "Google Chrome", "Navegador do Google.", "Navegadores", "essencial", "escritorio"),
        new("Mozilla.Firefox", "Firefox", "Navegador focado em privacidade.", "Navegadores"),
        new("Brave.Brave", "Brave", "Navegador com bloqueador de anúncios.", "Navegadores"),
        new("Opera.OperaGX", "Opera GX", "Navegador para quem joga.", "Navegadores"),

        new("Discord.Discord", "Discord", "Voz, vídeo e texto para jogar e conversar.", "Comunicação", "gamer", "criador"),
        new("Telegram.TelegramDesktop", "Telegram", "Mensagens com grupos e canais.", "Comunicação"),
        new("Zoom.Zoom", "Zoom", "Reuniões por vídeo.", "Comunicação", "escritorio"),
        new("Microsoft.Teams", "Microsoft Teams", "Reuniões e chat de trabalho.", "Comunicação"),
        new("SlackTechnologies.Slack", "Slack", "Chat para equipes.", "Comunicação"),
        new("Mozilla.Thunderbird", "Thunderbird", "Programa de e-mail.", "Comunicação"),

        new("TheDocumentFoundation.LibreOffice", "LibreOffice", "Pacote Office grátis: textos, planilhas e apresentações.", "Documentos", "escritorio"),
        new("ONLYOFFICE.DesktopEditors", "ONLYOFFICE", "Office grátis com visual parecido com o Word e o Excel.", "Documentos"),
        new("Adobe.Acrobat.Reader.64-bit", "Adobe Acrobat Reader", "Leitor de PDF oficial da Adobe.", "Documentos", "essencial", "escritorio"),
        new("SumatraPDF.SumatraPDF", "SumatraPDF", "Leitor de PDF e e-book bem leve.", "Documentos"),
        new("geeksoftwareGmbH.PDF24Creator", "PDF24", "Junta, divide, comprime e converte PDF.", "Documentos", "escritorio"),
        new("Notion.Notion", "Notion", "Notas, tarefas e documentos.", "Documentos"),
        new("Google.GoogleDrive", "Google Drive", "Arquivos na nuvem do Google.", "Documentos", "escritorio"),
        new("Dropbox.Dropbox", "Dropbox", "Arquivos na nuvem.", "Documentos"),

        new("7zip.7zip", "7-Zip", "Abre e cria .zip, .rar e .7z. Grátis.", "Compactadores", "essencial", "tecnico"),
        new("RARLab.WinRAR", "WinRAR", "Compactador clássico.", "Compactadores"),

        new("VideoLAN.VLC", "VLC", "Toca qualquer vídeo e música.", "Vídeo e música", "essencial", "criador"),
        new("clsid2.mpc-hc", "MPC-HC", "Player de vídeo leve.", "Vídeo e música"),
        new("CodecGuide.K-LiteCodecPack.Standard", "K-Lite Codec Pack", "Codecs para abrir formatos de vídeo raros.", "Vídeo e música"),
        new("Spotify.Spotify", "Spotify", "Músicas e podcasts.", "Vídeo e música"),

        new("Valve.Steam", "Steam", "Loja e biblioteca de jogos da Valve.", "Jogos", "gamer"),
        new("EpicGames.EpicGamesLauncher", "Epic Games", "Loja de jogos da Epic (jogos grátis toda semana).", "Jogos", "gamer"),
        new("ElectronicArts.EADesktop", "EA app", "Loja de jogos da EA.", "Jogos"),
        new("Ubisoft.Connect", "Ubisoft Connect", "Loja de jogos da Ubisoft.", "Jogos"),
        new("GOG.Galaxy", "GOG Galaxy", "Loja de jogos sem DRM.", "Jogos"),
        new("Blizzard.BattleNet", "Battle.net", "Jogos da Blizzard.", "Jogos"),
        new("Playnite.Playnite", "Playnite", "Junta todas as lojas numa biblioteca só.", "Jogos"),
        new("Guru3D.Afterburner", "MSI Afterburner", "FPS na tela, curva de ventoinha e overclock da placa.", "Jogos", "gamer"),
        new("Ryochan7.DS4Windows", "DS4Windows", "Controle de PlayStation funcionando como de Xbox.", "Jogos"),
        new("Parsec.Parsec", "Parsec", "Jogar e acessar o PC de longe.", "Jogos"),

        new("OBSProject.OBSStudio", "OBS Studio", "Gravação e transmissão ao vivo.", "Criação", "criador", "gamer"),
        new("ShareX.ShareX", "ShareX", "Prints e gravação de tela completos.", "Criação", "criador"),
        new("Skillbrains.Lightshot", "Lightshot", "Print de um pedaço da tela.", "Criação"),
        new("KDE.Kdenlive", "Kdenlive", "Editor de vídeo grátis.", "Criação", "criador"),
        new("HandBrake.HandBrake", "HandBrake", "Converte e comprime vídeos.", "Criação", "criador"),
        new("Audacity.Audacity", "Audacity", "Gravação e edição de áudio.", "Criação", "criador"),
        new("GIMP.GIMP", "GIMP", "Editor de imagens grátis.", "Criação", "criador"),
        new("dotPDN.PaintDotNet", "Paint.NET", "Editor de imagens simples.", "Criação"),
        new("KDE.Krita", "Krita", "Desenho e pintura digital.", "Criação"),
        new("Inkscape.Inkscape", "Inkscape", "Desenho vetorial (logos e artes).", "Criação"),
        new("BlenderFoundation.Blender", "Blender", "Modelagem e animação 3D.", "Criação"),
        new("Canva.Canva", "Canva", "Artes, posts e apresentações.", "Criação", "criador"),

        new("Microsoft.VisualStudioCode", "Visual Studio Code", "Editor de código da Microsoft.", "Programação", "programador"),
        new("Git.Git", "Git", "Controle de versões do código.", "Programação", "programador"),
        new("Python.Python.3.12", "Python 3.12", "Linguagem para automação, dados e IA.", "Programação", "programador"),
        new("OpenJS.NodeJS.LTS", "Node.js LTS", "JavaScript fora do navegador.", "Programação", "programador"),
        new("Microsoft.WindowsTerminal", "Windows Terminal", "Terminal moderno com abas.", "Programação", "programador", "tecnico"),
        new("Docker.DockerDesktop", "Docker Desktop", "Contêineres para desenvolvimento.", "Programação"),
        new("Postman.Postman", "Postman", "Testa APIs.", "Programação"),
        new("Microsoft.VisualStudio.2022.Community", "Visual Studio 2022 Community", "IDE completa da Microsoft.", "Programação"),
        new("WinSCP.WinSCP", "WinSCP", "Copia arquivos para servidores (SFTP).", "Programação"),
        new("PuTTY.PuTTY", "PuTTY", "Acesso SSH a servidores.", "Programação"),

        new("Notepad++.Notepad++", "Notepad++", "Bloco de notas turbinado.", "Utilitários", "programador", "tecnico"),
        new("Microsoft.PowerToys", "PowerToys", "Utilitários extras da Microsoft para o Windows.", "Utilitários"),
        new("voidtools.Everything", "Everything", "Acha qualquer arquivo na hora.", "Utilitários"),
        new("qBittorrent.qBittorrent", "qBittorrent", "Cliente de torrent sem anúncios.", "Utilitários"),
        new("AnyDesk.AnyDesk", "AnyDesk", "Acesso remoto.", "Utilitários", "tecnico"),
        new("rocksdanister.LivelyWallpaper", "Lively Wallpaper", "Papel de parede animado.", "Utilitários"),
        new("CharlesMilette.TranslucentTB", "TranslucentTB", "Barra de tarefas transparente.", "Utilitários"),
        new("Bitwarden.Bitwarden", "Bitwarden", "Cofre de senhas grátis.", "Utilitários"),

        new("CPUID.CPU-Z", "CPU-Z", "Detalhes do processador, memória e placa-mãe.", "Diagnóstico", "tecnico"),
        new("TechPowerUp.GPU-Z", "GPU-Z", "Detalhes da placa de vídeo.", "Diagnóstico", "tecnico"),
        new("REALiX.HWiNFO", "HWiNFO", "Temperaturas e sensores de tudo.", "Diagnóstico", "tecnico"),
        new("CrystalDewWorld.CrystalDiskInfo", "CrystalDiskInfo", "Saúde de SSD e HD.", "Diagnóstico", "tecnico"),
        new("CrystalDewWorld.CrystalDiskMark", "CrystalDiskMark", "Velocidade de SSD e HD.", "Diagnóstico", "tecnico"),
        new("Rufus.Rufus", "Rufus", "Cria pendrive de instalação do Windows.", "Diagnóstico", "tecnico"),
        new("Ventoy.Ventoy", "Ventoy", "Um pendrive com várias ISOs de boot.", "Diagnóstico", "tecnico"),
        new("Microsoft.Sysinternals.ProcessExplorer", "Process Explorer", "Gerenciador de tarefas avançado da Microsoft: mostra quem abriu cada arquivo e o que cada processo carrega.", "Diagnóstico", "tecnico"),
        new("Microsoft.Sysinternals.Autoruns", "Autoruns", "Tudo o que inicia com o Windows (serviços, tarefas, extensões), da Microsoft.", "Diagnóstico", "tecnico"),
        new("Microsoft.Sysinternals.ProcessMonitor", "Process Monitor", "Mostra em tempo real o que os programas leem e gravam no disco e no registro.", "Diagnóstico", "tecnico"),
        new("AntibodySoftware.WizTree", "WizTree", "Mostra o que ocupa o disco, muito rápido.", "Diagnóstico"),
        new("WiresharkFoundation.Wireshark", "Wireshark", "Análise de tráfego de rede.", "Diagnóstico"),

        new("Microsoft.VCRedist.2015+.x64", "Visual C++ 2015-2022 (64 bits)", "Biblioteca que muitos jogos e programas pedem.", "Bibliotecas", "essencial", "gamer"),
        new("Microsoft.VCRedist.2015+.x86", "Visual C++ 2015-2022 (32 bits)", "A mesma, para programas de 32 bits.", "Bibliotecas", "essencial", "gamer"),
        new("Microsoft.DotNet.DesktopRuntime.8", ".NET 8 Desktop Runtime", "Necessário para muitos programas modernos.", "Bibliotecas", "essencial"),
        new("Microsoft.DirectX", "DirectX (bibliotecas antigas)", "Arquivos do DirectX 9 que jogos antigos pedem.", "Bibliotecas", "gamer"),
        new("EclipseAdoptium.Temurin.21.JRE", "Java 21 (Temurin)", "Para programas e jogos em Java (Minecraft Java usa o próprio).", "Bibliotecas"),
    };

    public static IReadOnlyList<CatalogEntry> InKit(string kit) => All.Where(a => a.Kits.Contains(kit)).ToList();

    /// <summary>Quais destes ids aparecem na saída do "winget list".</summary>
    public static ISet<string> IdsIn(string wingetListOutput, IEnumerable<string> ids) =>
        new HashSet<string>(ids.Where(id => wingetListOutput.Contains(id, StringComparison.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);
}
