namespace MBExtreme.Core.Startup;

/// <summary>Jeito de usar o PC: cada perfil escolhe o que desligar, o que só pausar no jogo e o que deixar.</summary>
public enum PcProfile { Gamer, Turbo, Trabalho, Notebook, Streamer }

/// <summary>O que fazer com um programa que abre junto com o Windows.</summary>
public enum StartupVerdict { CanWait, Keep, Protected, Unknown }

public sealed record ProfileStartupAdvice(StartupEntry Entry, StartupVerdict Verdict, string Group, string Why);

public sealed record ProfileInfo(PcProfile Id, string Name, string Glyph, string Color, string Description);

/// <summary>Perfis de um clique da tela Processos e inicialização.</summary>
public static class ProcessProfiles
{
    public static IReadOnlyList<ProfileInfo> All { get; } = new[]
    {
        new ProfileInfo(PcProfile.Gamer, "Gamer", "", "#FFC400", "Tira da inicialização o que não é do jogo, desliga atualizadores escondidos e pausa o resto quando o jogo abre."),
        new ProfileInfo(PcProfile.Turbo, "Turbo (PC fraco)", "", "#FF8A00", "Máximo de memória livre: tudo do Gamer, menos processos do Windows, menos efeitos e sem indexação."),
        new ProfileInfo(PcProfile.Trabalho, "Trabalho", "", "#6CB6FF", "Mantém OneDrive, Teams, impressora e busca; tira só o que não serve para nada."),
        new ProfileInfo(PcProfile.Notebook, "Notebook / economia", "", "#3DDC97", "Menos coisas em segundo plano para a bateria durar mais e esquentar menos."),
        new ProfileInfo(PcProfile.Streamer, "Streamer", "", "#C084FC", "Igual ao Gamer, mas mantém OBS, Discord, câmera e os apps de live."),
    };

    private static readonly string[] Common =
    {
        "perf-svc-diagtrack", "perf-svc-maps", "perf-background-apps", "proc-svc-wersvc", "proc-svc-location", "proc-telemetry",
        "proc-activity", "proc-consumer-features", "proc-device-companion", "proc-wpbt", "proc-updater-tasks", "proc-endtask-taskbar",
    };

    /// <summary>Ajustes (ids do catálogo) que o perfil aplica. <paramref name="ramGb"/> decide o ajuste dos processos svchost no Turbo.</summary>
    public static IReadOnlyList<string> Tweaks(PcProfile p, double ramGb) => p switch
    {
        PcProfile.Gamer or PcProfile.Streamer => Common.Concat(new[] { "proc-delivery-optimization", "proc-svc-csc", "proc-svc-sharedaccess", "game-dvr" }).ToList(),
        PcProfile.Turbo => Common.Concat(new[] { "proc-delivery-optimization", "proc-svc-csc", "proc-svc-sharedaccess", "game-dvr", "perf-svc-search", "visual-animations", "visual-transparency" })
            .Concat(ramGb is > 0 and <= 12.5 ? new[] { "proc-svchost-split" } : Array.Empty<string>()).ToList(),
        PcProfile.Notebook => Common.Concat(new[] { "proc-delivery-optimization", "proc-svc-sharedaccess" }).ToList(),
        _ => Common.ToList(),
    };

    /// <summary>Modo Jogo automático (fecha/pausa atualizadores e sincronização quando o jogo abre, devolve quando fecha).</summary>
    public static bool UsesGameMode(PcProfile p) => p is PcProfile.Gamer or PcProfile.Turbo or PcProfile.Streamer;

    /// <summary>Perfil sugerido para o PC.</summary>
    public static PcProfile Recommend(bool laptop, double ramGb) => laptop ? PcProfile.Notebook : ramGb is > 0 and <= 8.5 ? PcProfile.Turbo : PcProfile.Gamer;

    // ------------------------------------------------------------ programas que abrem com o Windows

    private static readonly (string[] Keys, string Group, string Why)[] Protected =
    {
        (new[] { "SecurityHealth", "Windows Defender", "avast", "avg", "avp", "kaspersky", "norton", "mcafee", "egui", "eset", "bdagent", "bitdefender", "malwarebytes", "mbam" }, "Antivírus", "proteção do PC · não mexo"),
        (new[] { "RtkAud", "Realtek", "RAVCpl", "Waves", "MaxxAudio", "Nahimic", "Dolby", "SonicStudio" }, "Áudio", "driver de áudio · não mexo"),
        (new[] { "NVIDIA", "nvcpl", "RadeonSoftware", "AMD", "igfx", "IntelGraphics", "HDAudio" }, "Vídeo", "driver da placa de vídeo · não mexo"),
        (new[] { "SynTP", "ETDCtrl", "Elan", "touchpad", "SmartAudio", "Lenovo", "HPHotkey", "Fn", "ASUS", "Armoury", "MSI Center", "Dragon Center" }, "Notebook e placa", "tecla de função, touchpad ou controle da placa"),
        (new[] { "MBExtreme", "MB Extreme" }, "MB", "o próprio MB"),
    };

    private static readonly (string[] Keys, string Group, string Why, PcProfile[] KeepIn)[] Known =
    {
        (new[] { "Teams", "ms-teams", "msteams" }, "Comunicação", "abre com o Windows e fica gastando memória", new[] { PcProfile.Trabalho }),
        (new[] { "OneDrive" }, "Nuvem", "sincroniza em segundo plano (abre quando você precisar)", new[] { PcProfile.Trabalho }),
        (new[] { "GoogleDriveFS", "Dropbox", "iCloud" }, "Nuvem", "sincroniza em segundo plano", new[] { PcProfile.Trabalho }),
        (new[] { "PhoneExperienceHost", "YourPhone", "Phone Link" }, "Celular", "fica aberto sem uso", Array.Empty<PcProfile>()),
        (new[] { "Spotify" }, "Música", "abre sozinho sem você pedir", Array.Empty<PcProfile>()),
        (new[] { "Skype" }, "Comunicação", "abre sozinho sem você pedir", Array.Empty<PcProfile>()),
        (new[] { "MicrosoftEdgeAutoLaunch", "EdgeAutoLaunch", "GoogleChromeAutoLaunch", "Opera", "Brave" }, "Navegador", "o navegador já fica carregado sem você abrir", Array.Empty<PcProfile>()),
        (new[] { "CCXProcess", "Creative Cloud", "Adobe", "AcroTray", "AdobeGCInvoker" }, "Adobe", "vários processos em segundo plano", Array.Empty<PcProfile>()),
        (new[] { "iTunesHelper", "QuickTime", "Java Update", "jusched" }, "Atualizador", "atualizador antigo que fica rodando", Array.Empty<PcProfile>()),
        (new[] { "uTorrent", "BitTorrent", "qBittorrent" }, "Download", "usa a internet em segundo plano", Array.Empty<PcProfile>()),
        (new[] { "Steam", "EpicGamesLauncher", "EADesktop", "Origin", "Ubisoft", "UbisoftConnect", "Battle.net", "GalaxyClient", "RiotClient" }, "Loja de jogos", "abre quando você abre o jogo", new[] { PcProfile.Gamer, PcProfile.Streamer }),
        (new[] { "Discord" }, "Voz", "abre sozinho com o Windows", new[] { PcProfile.Gamer, PcProfile.Streamer }),
        (new[] { "obs64", "OBS", "Streamlabs", "StreamDeck", "Elgato" }, "Live", "apps de transmissão", new[] { PcProfile.Streamer, PcProfile.Gamer }),
        (new[] { "lghub", "LGHUB", "Razer", "Synapse", "iCUE", "Corsair", "SteelSeries", "HyperX", "NGENUITY", "Redragon" }, "Periféricos", "configura o seu mouse, teclado ou fone", new[] { PcProfile.Gamer, PcProfile.Turbo, PcProfile.Trabalho, PcProfile.Notebook, PcProfile.Streamer }),
        (new[] { "Cortana", "Copilot" }, "Assistente", "assistente que fica aberto", Array.Empty<PcProfile>()),
    };

    private static bool Matches(StartupEntry e, string key) =>
        e.Name.Contains(key, StringComparison.OrdinalIgnoreCase) || e.Command.Contains(key, StringComparison.OrdinalIgnoreCase);

    /// <summary>Classifica um item da inicialização para o perfil.</summary>
    public static ProfileStartupAdvice Classify(StartupEntry e, PcProfile profile)
    {
        foreach (var (keys, group, why) in Protected)
            if (keys.Any(k => Matches(e, k))) return new ProfileStartupAdvice(e, StartupVerdict.Protected, group, why);
        foreach (var (keys, group, why, keep) in Known)
            if (keys.Any(k => Matches(e, k)))
                return keep.Contains(profile) ? new ProfileStartupAdvice(e, StartupVerdict.Keep, group, "você usa neste perfil") : new ProfileStartupAdvice(e, StartupVerdict.CanWait, group, why);
        return new ProfileStartupAdvice(e, StartupVerdict.Unknown, "Outros", "não conheço: fica como está (dá para desligar em Programas)");
    }
}
