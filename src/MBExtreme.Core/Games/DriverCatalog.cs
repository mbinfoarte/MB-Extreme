namespace MBExtreme.Core.Games;

/// <summary>Uma configuração de driver NVIDIA do catálogo. Advanced = só entra se o usuário ligar os ajustes avançados.</summary>
public sealed record DriverSetting(uint Id, uint Value, string Title, string ValueText, string Why, bool Advanced);

/// <summary>Perfil de driver de um jogo (ou família de jogos). HasReflex = o jogo tem NVIDIA Reflex próprio, então não mexemos nos quadros pré-renderizados.</summary>
public sealed record GameDriverProfile(string Key, string Title, IReadOnlyList<string> Executables, bool HasReflex, IReadOnlyList<DriverSetting> Settings);

/// <summary>
/// Catálogo de ajustes de driver por jogo. Identificadores e valores vêm do arquivo CustomSettingNames.xml do NVIDIA Profile Inspector (MIT,
/// https://github.com/Orbmu2k/nvidiaProfileInspector). Ver docs/PERFIS-DRIVER-NVIDIA.md. Energia e quadros pré-renderizados ficam em <see cref="DriverPlan"/>.
/// </summary>
public static class DriverCatalog
{
    public const uint ThreadedOptimization = 0x20C1221E;
    public const uint TextureQuality = 0x00CE2691;
    public const uint VerticalSync = 0x00A879CF;
    public const uint ShaderCacheSize = 0x00AC8497;
    public const uint DlssOverride = 0x10E41E01;

    private static readonly DriverSetting ShaderCache = new(ShaderCacheSize, 0xFFFFFFFF, "Cache de shaders", "Sem limite",
        "Guarda os shaders compilados para o jogo não recompilar e travar. Ocupa espaço em disco.", false);
    private static readonly DriverSetting Threads = new(ThreadedOptimization, 2, "Otimização com threads", "Desligada",
        "Experimental: um guia do Battlefield 6 sugere testar desligada, sem benchmark. Se piorar, desfaça.", true);
    private static readonly DriverSetting Texture = new(TextureQuality, 0x14, "Qualidade da filtragem de textura", "Alto desempenho",
        "Ganho pequeno de FPS com textura um pouco pior. Comum em guias de jogos competitivos.", true);
    private static readonly DriverSetting VSync = new(VerticalSync, 0x08416747, "Sincronia vertical (driver)", "Forçar desligada",
        "Tira o atraso do V-Sync. Pode causar rasgos de imagem; prefira G-Sync/limite de FPS se tiver monitor compatível.", true);
    private static readonly DriverSetting Dlss = new(DlssOverride, 1, "DLSS: usar a versão mais recente instalada no driver", "Ligado",
        "Troca o DLSS do jogo pelo do driver sem mexer nos arquivos do jogo. Só vale para jogos com DLSS; teste em partidas offline.", true);

    public static readonly GameDriverProfile Battlefield = new("battlefield", "Battlefield", new[] { "bf6.exe", "bf2042.exe", "bfv.exe", "bf4.exe", "bf1.exe" }, true,
        new[] { ShaderCache, Threads, Dlss });
    public static readonly GameDriverProfile Cs2 = new("cs2", "Counter-Strike 2", new[] { "cs2.exe" }, true, new[] { ShaderCache, Texture, VSync });
    public static readonly GameDriverProfile Valorant = new("valorant", "Valorant", new[] { "valorant-win64-shipping.exe" }, true, new[] { ShaderCache, Texture });
    public static readonly GameDriverProfile Apex = new("apex", "Apex Legends", new[] { "r5apex.exe", "r5apex_dx12.exe" }, true, new[] { ShaderCache, Texture, VSync });
    public static readonly GameDriverProfile Fortnite = new("fortnite", "Fortnite", new[] { "fortniteclient-win64-shipping.exe" }, true, new[] { ShaderCache, Texture, Dlss });
    public static readonly GameDriverProfile Warzone = new("warzone", "Call of Duty / Warzone", new[] { "cod.exe", "codbo6.exe", "cod24-cod.exe" }, true, new[] { ShaderCache, Texture, Dlss });

    public static IReadOnlyList<GameDriverProfile> All { get; } = new[] { Battlefield, Cs2, Valorant, Apex, Fortnite, Warzone };

    public static GameDriverProfile? For(string? exeName) =>
        string.IsNullOrWhiteSpace(exeName) ? null : All.FirstOrDefault(p => p.Executables.Contains(exeName, StringComparer.OrdinalIgnoreCase));

    /// <summary>Ajustes genéricos para jogo sem perfil próprio (só o seguro).</summary>
    public static IReadOnlyList<DriverSetting> Generic { get; } = new[] { ShaderCache };

    public static IReadOnlyList<DriverSetting> SettingsFor(string? exeName, bool advanced)
    {
        var list = For(exeName)?.Settings ?? Generic;
        return list.Where(s => advanced || !s.Advanced).ToList();
    }

    public static string TitleOf(uint id) => id switch
    {
        DriverSettingIds.PowerMode => "Modo de gerenciamento de energia",
        DriverSettingIds.PreRenderLimit => "Quadros pré-renderizados",
        _ => All.SelectMany(p => p.Settings).Concat(Generic).FirstOrDefault(s => s.Id == id)?.Title ?? DriverPresets.NameOf(id),
    };
}
