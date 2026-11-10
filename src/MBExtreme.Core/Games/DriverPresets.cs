using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace MBExtreme.Core.Games;

/// <summary>Um perfil pronto de driver (configurações que a comunidade costuma usar), aplicado só no perfil do jogo escolhido.</summary>
public sealed record DriverPreset(string Key, string Title, string Description, IReadOnlyList<DriverSetting> Settings);

/// <summary>Configuração lida de um arquivo .nip do NVIDIA Profile Inspector.</summary>
public sealed record NipSetting(uint Id, uint Value, string Name);

/// <summary>Perfil lido de um .nip: nome, executáveis que ele cita e as configurações DWORD (as outras são ignoradas).</summary>
public sealed record NipProfile(string Name, IReadOnlyList<string> Executables, IReadOnlyList<NipSetting> Settings, int Skipped);

/// <summary>
/// Perfis prontos de driver NVIDIA no estilo dos que a comunidade monta no NVIDIA Profile Inspector, e leitura de arquivos .nip.
/// Identificadores e valores do cabeçalho público NvApiDriverSettings.h (NVAPI R620).
/// </summary>
public static class DriverPresets
{
    public const uint VSyncMode = 0x00A879CF, VSyncOff = 0x08416747, VSyncOn = 0x47814940, VSyncApp = 0x60925292;
    public const uint TextureQuality = 0x00CE2691, TexHighQuality = 0xFFFFFFF6, TexQuality = 0, TexPerformance = 0x0A, TexHighPerformance = 0x14;
    public const uint ThreadedOptimization = 0x20C1221E;
    public const uint AnisoSelector = 0x10D2BB16, AnisoLevel = 0x101E61A9;
    public const uint FrameRateLimit = 0x10835002;
    public const uint GSync = 0x10A879CF;

    private static DriverSetting S(uint id, uint value, string title, string text, string why) => new(id, value, title, text, why, false);

    public static IReadOnlyList<DriverPreset> All { get; } = new[]
    {
        new DriverPreset("competitive", "Competitivo (máximo de FPS e menor atraso)",
            "Para jogos de tiro e e-sports: tira o V-Sync do driver, deixa a placa no desempenho máximo e a filtragem de textura mais leve.",
            new[]
            {
                S(DriverSettingIds.PowerMode, 1, "Modo de gerenciamento de energia", "Preferir desempenho máximo", "A placa não abaixa o clock no meio da partida."),
                S(DriverSettingIds.PreRenderLimit, 1, "Quadros pré-renderizados", "1 (baixa latência)", "Menos atraso entre o mouse e a tela (jogos com Reflex já fazem isso sozinhos)."),
                S(VSyncMode, VSyncOff, "Sincronia vertical (driver)", "Forçar desligada", "Sem o atraso do V-Sync. Com monitor G-Sync, prefira limitar o FPS."),
                S(TextureQuality, TexHighPerformance, "Qualidade da filtragem de textura", "Alto desempenho", "Um pouco mais de FPS, textura levemente pior."),
                S(ThreadedOptimization, 1, "Otimização com threads", "Ligada", "Divide o trabalho do driver entre os núcleos do processador."),
            }),
        new DriverPreset("balanced", "Equilíbrio",
            "Desempenho máximo de energia e baixa latência, sem perder qualidade de imagem. Bom para a maioria dos jogos.",
            new[]
            {
                S(DriverSettingIds.PowerMode, 1, "Modo de gerenciamento de energia", "Preferir desempenho máximo", "A placa não abaixa o clock no meio do jogo."),
                S(DriverSettingIds.PreRenderLimit, 1, "Quadros pré-renderizados", "1 (baixa latência)", "Resposta mais rápida aos comandos."),
                S(TextureQuality, TexQuality, "Qualidade da filtragem de textura", "Qualidade", "O padrão do driver: bom equilíbrio."),
                S(ThreadedOptimization, 1, "Otimização com threads", "Ligada", "Divide o trabalho do driver entre os núcleos do processador."),
            }),
        new DriverPreset("quality", "Qualidade visual",
            "Para jogos de mundo aberto e história: filtragem de textura em alta qualidade e anisotrópica 16x.",
            new[]
            {
                S(DriverSettingIds.PowerMode, 1, "Modo de gerenciamento de energia", "Preferir desempenho máximo", "Clock estável."),
                S(TextureQuality, TexHighQuality, "Qualidade da filtragem de textura", "Alta qualidade", "Texturas mais nítidas de longe."),
                S(AnisoSelector, 1, "Filtragem anisotrópica: controle", "Definido pelo driver", "O driver força a anisotrópica."),
                S(AnisoLevel, 16, "Filtragem anisotrópica", "16x", "Chão e paredes nítidos em ângulo, com custo baixo."),
            }),
    };

    /// <summary>Limite de FPS do driver (0 = sem limite). Útil para G-Sync: 3 abaixo da taxa do monitor.</summary>
    public static DriverSetting FpsLimit(int fps) =>
        S(FrameRateLimit, (uint)Math.Clamp(fps, 0, 1023), "Limite de FPS (driver)", fps <= 0 ? "Sem limite" : fps + " FPS", "Segura o FPS num valor estável.");

    /// <summary>Nome em português de configurações conhecidas (para mostrar o que um .nip muda).</summary>
    public static string NameOf(uint id, string? fallback = null) => id switch
    {
        DriverSettingIds.PowerMode => "Modo de gerenciamento de energia",
        DriverSettingIds.PreRenderLimit => "Quadros pré-renderizados (baixa latência)",
        VSyncMode => "Sincronia vertical",
        TextureQuality => "Qualidade da filtragem de textura",
        ThreadedOptimization => "Otimização com threads",
        AnisoSelector => "Filtragem anisotrópica: controle",
        AnisoLevel => "Filtragem anisotrópica",
        FrameRateLimit => "Limite de FPS",
        GSync => "G-SYNC / VRR",
        0x00AC8497 => "Tamanho do cache de shaders",
        DlssIds.SrOverride => "DLSS: substituição",
        DlssIds.SrPreset => "DLSS: modelo (preset)",
        DlssIds.SrMode => "DLSS: qualidade",
        DlssIds.FgOverride => "Geração de quadros: substituição",
        DlssIds.FgPreset => "Geração de quadros: modelo",
        DlssIds.MultiFrameCount => "Geração de quadros: multiplicador",
        DlssIds.RrOverride => "Ray Reconstruction: substituição",
        DlssIds.RrPreset => "Ray Reconstruction: modelo",
        _ => string.IsNullOrWhiteSpace(fallback) ? $"Configuração 0x{id:X8}" : fallback!.Trim(),
    };

    /// <summary>Configurações que nunca vêm de um .nip: identificador vazio.</summary>
    private static readonly HashSet<uint> Blocked = new() { 0 };

    /// <summary>
    /// Lê um .nip (XML do NVIDIA Profile Inspector). Aceita só DWORD com valor decimal válido; ignora o resto. Sem DTD nem entidades externas.
    /// </summary>
    public static IReadOnlyList<NipProfile> ParseNip(string xml)
    {
        var result = new List<NipProfile>();
        XDocument doc;
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 20_000_000 });
            doc = XDocument.Load(reader);
        }
        catch (XmlException) { return result; }
        foreach (var p in doc.Descendants("Profile").Take(200))
        {
            string name = (string?)p.Element("ProfileName") ?? "";
            var exes = p.Element("Executeables")?.Elements("string").Select(e => e.Value.Trim()).Where(e => e.Length > 0).ToList() ?? new List<string>();
            var settings = new List<NipSetting>();
            int skipped = 0;
            foreach (var s in p.Element("Settings")?.Elements("ProfileSetting") ?? Enumerable.Empty<XElement>())
            {
                string type = ((string?)s.Element("ValueType") ?? "Dword").Trim();
                bool okId = uint.TryParse(((string?)s.Element("SettingID"))?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out uint id);
                bool okValue = uint.TryParse(((string?)s.Element("SettingValue"))?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out uint value);
                if (!type.Equals("Dword", StringComparison.OrdinalIgnoreCase) || !okId || !okValue || Blocked.Contains(id)) { skipped++; continue; }
                settings.Add(new NipSetting(id, value, NameOf(id, (string?)s.Element("SettingNameInfo"))));
            }
            result.Add(new NipProfile(name, exes, settings, skipped));
        }
        return result;
    }

    /// <summary>O perfil do .nip que serve para o jogo: o que cita o executável, senão o único do arquivo.</summary>
    public static NipProfile? PickFor(IReadOnlyList<NipProfile> profiles, string? exeName) =>
        profiles.FirstOrDefault(p => exeName is not null && p.Executables.Contains(exeName, StringComparer.OrdinalIgnoreCase))
        ?? (profiles.Count == 1 ? profiles[0] : null);
}
