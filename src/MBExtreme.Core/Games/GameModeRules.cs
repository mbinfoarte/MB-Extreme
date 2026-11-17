namespace MBExtreme.Core.Games;

/// <summary>
/// Regras do Modo Jogo 2.0: o que dá para fechar com segurança, o que nunca é tocado e quais tarefas do Windows ficam pausadas.
/// A regra de ouro: na dúvida, não fecha; só abaixa a prioridade (e tudo volta ao sair do jogo).
/// </summary>
public static class GameModeRules
{
    /// <summary>Ajudantes de fundo que não fazem falta durante o jogo (atualizadores, widgets, sincronização). Fecham sozinhos no Modo Jogo.</summary>
    public static IReadOnlyList<string> AutoClose { get; } = new[]
    {
        // atualizadores
        "jusched", "AdobeARM", "AdobeCollabSync", "CCXProcess", "GoogleUpdate", "MicrosoftEdgeUpdate",
        // widgets e celular
        "Widgets", "WidgetService", "PhoneExperienceHost", "YourPhone",
        // sincronização na nuvem (reabrem ao sair do jogo)
        "OneDrive", "GoogleDriveFS", "Dropbox", "iCloudDrive", "iCloudServices", "iCloudPhotos",
        // Adobe e Creative Cloud em segundo plano
        "Creative Cloud", "CCLibrary", "CoreSync", "AdobeNotificationClient", "AdobeIPCBroker", "AdobeUpdateService",
        // relatórios de erro e atualizadores extras
        "GoogleCrashHandler", "GoogleCrashHandler64", "OneDriveStandaloneUpdater", "OfficeC2RClient", "SoftwareUpdate",
        // assistentes e apps da loja que ficam abertos sem uso
        "Copilot", "Microsoft.Photos", "PhotosApp", "Cortana", "SkypeApp", "Skype", "ms-teams", "msteams", "Teams",
    };

    /// <summary>Programas que reabrimos ao sair do jogo (eram usados antes e só foram pausados).</summary>
    public static IReadOnlyList<string> ReopenAfter { get; } = new[] { "OneDrive", "GoogleDriveFS", "Dropbox" };

    /// <summary>
    /// Nunca fecha nem abaixa a prioridade: sistema, segurança, antitrapaça, lojas de jogos (o jogo depende delas), voz, captura,
    /// drivers de vídeo e áudio, e o próprio MB Extreme.
    /// </summary>
    public static IReadOnlySet<string> NeverTouch { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Windows
        "System", "Idle", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso", "svchost", "dwm", "explorer", "fontdrvhost",
        "sihost", "ctfmon", "taskhostw", "RuntimeBroker", "StartMenuExperienceHost", "ShellExperienceHost", "SearchHost", "TextInputHost",
        "audiodg", "conhost", "dllhost", "spoolsv", "Registry", "MemCompression", "WmiPrvSE", "SecurityHealthSystray", "SecurityHealthService",
        "MsMpEng", "NisSrv", "MpDefenderCoreService", "smartscreen", "LockApp", "LogonUI", "WUDFHost", "dasHost", "TabTip",
        // antitrapaça
        "EasyAntiCheat", "EasyAntiCheat_EOS", "EasyAntiCheat_EOS_Setup", "BEService", "BEService_x64", "vgc", "vgtray", "vgk", "FACEIT", "FACEITService",
        "faceit-ac", "EAAntiCheat.GameServiceLauncher", "EAAntiCheat.GameService", "Vanguard", "nProtect", "GameGuard", "xigncode", "mhyprot", "ACE-Tray", "SGuard64",
        // lojas e serviços de jogo
        "steam", "steamwebhelper", "steamservice", "EpicGamesLauncher", "EpicWebHelper", "Battle.net", "Agent", "EADesktop", "EABackgroundService",
        "upc", "UbisoftConnect", "GalaxyClient", "GalaxyClientService", "RiotClientServices", "RiotClientUx", "XboxPcApp", "GamingServices", "GameBar",
        "XboxGameBarWidgets", "RockstarService", "Launcher", "SocialClubHelper",
        // voz, música e captura
        "Discord", "Teamspeak", "ts3client_win64", "Mumble", "Spotify", "obs64", "obs32", "obs", "Streamlabs OBS", "XSplit.Core", "ShareX",
        "NVIDIA Share", "NVIDIA Overlay", "nvsphelper64", "RadeonSoftware", "AMDRSServ", "AMDRSSrcExt",
        // drivers, áudio, periféricos
        "nvcontainer", "NVDisplay.Container", "nvidia-smi", "atiesrxx", "atieclxx", "igfxEM", "RtkAudUService64", "RAVBg64", "Nahimic", "NahimicSvc64",
        "voicemeeter", "voicemeeter8x64", "voicemeeterpro", "audiorelay", "iCUE", "LGHUB", "lghub_agent", "lghub_updater", "Razer Synapse 3", "RazerCentralService",
        "SteelSeriesGG", "ArmouryCrate", "ArmourySocketServer", "MSI.CentralServer", "ROGLiveService",
        // MB Extreme
        "MBExtreme", "MBExtreme-Instalar",
    };

    /// <summary>Tarefas do Windows pausadas durante o jogo (só as que estavam rodando; voltam ao sair): indexação de pesquisa e otimização de entrega.</summary>
    public static IReadOnlyList<(string Service, string Title)> PausableServices { get; } = new[]
    {
        ("WSearch", "Indexação da pesquisa do Windows"),
        ("DoSvc", "Otimização de entrega (downloads do Windows Update)"),
        ("DiagTrack", "Telemetria do Windows"),
        ("WerSvc", "Relatório de erros do Windows"),
        ("SysMain", "SysMain (pré-carregamento de programas)"),
        ("MapsBroker", "Download de mapas offline"),
        ("PcaSvc", "Assistente de compatibilidade de programas"),
        ("WMPNetworkSvc", "Compartilhamento do Windows Media Player"),
        ("Spooler", "Impressão (volta ao sair do jogo)"),
    };

    /// <summary>Pode abaixar a prioridade deste processo? Só da sessão do usuário, nunca protegidos, nunca o jogo, nunca o próprio programa.</summary>
    public static bool CanDeprioritize(string name, int sessionId, int mySession, bool isGameOrSelf, IReadOnlySet<string>? alsoKeep = null) =>
        !isGameOrSelf && sessionId == mySession && sessionId != 0 && name.Length > 0 && !NeverTouch.Contains(name) && !(alsoKeep?.Contains(name) ?? false);

    /// <summary>
    /// Modo Turbo: pode pedir para fechar esta janela aberta? Só programas com janela, da sessão do usuário, que não sejam protegidos,
    /// o jogo, o próprio MB ou da lista "nunca fechar". O pedido é o mesmo do X: o programa pode perguntar se quer salvar.
    /// </summary>
    public static bool CanCloseWindow(string name, int sessionId, int mySession, bool isGameOrSelf, bool hasWindow, IReadOnlySet<string>? keep = null) =>
        hasWindow && CanDeprioritize(name, sessionId, mySession, isGameOrSelf, keep);

    /// <summary>Pode fechar este processo da lista automática? Respeita o "nunca fechar" do usuário.</summary>
    public static bool CanAutoClose(string name, IReadOnlySet<string>? keep = null) =>
        AutoClose.Contains(name, StringComparer.OrdinalIgnoreCase) && !NeverTouch.Contains(name) && !(keep?.Contains(name) ?? false);

    /// <summary>Resumo para mostrar ao usuário.</summary>
    public static string Summary(int closed, int lowered, int services, long freedBytes)
    {
        var parts = new List<string>();
        if (closed > 0) parts.Add($"fechou {closed} programa(s) de fundo");
        if (lowered > 0) parts.Add($"deixou {lowered} em segundo plano");
        if (services > 0) parts.Add($"pausou {services} tarefa(s) do Windows");
        if (freedBytes > 0) parts.Add($"liberou {freedBytes / 1048576.0 / 1024:0.0} GB de RAM");
        return parts.Count == 0 ? "Nada para aliviar: o PC já estava livre." : "Modo Jogo: " + string.Join(", ", parts) + ".";
    }
}
