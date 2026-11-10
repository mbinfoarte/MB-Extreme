using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MBExtreme.Core.Games;

/// <summary>Identificadores das substituições de DLSS no driver (NvApiDriverSettings.h, NVAPI R620, github.com/NVIDIA/nvapi).</summary>
public static class DlssIds
{
    public const uint SrOverride = 0x10E41E01;        // Enable DLSS-SR override
    public const uint SrPreset = 0x10E41DF3;          // Override DLSS-SR presets
    public const uint SrMode = 0x10AFB768;            // Override DLSS-SR performance mode
    public const uint FgOverride = 0x10E41E03;        // Enable DLSS-FG override
    public const uint FgPreset = 0x10E41DF1;          // Override DLSS-FG preset
    public const uint MultiFrameCount = 0x104D6667;   // Override DLSSG multi-frame count
    public const uint RrOverride = 0x10E41E02;        // Enable DLSS-RR override
    public const uint RrPreset = 0x10E41DF7;          // Override DLSS-RR preset

    public const uint PresetLatest = 0x00FFFFFF;
    public const uint ModeGameControlled = 3;

    public static readonly uint[] All = { SrOverride, SrPreset, SrMode, FgOverride, FgPreset, MultiFrameCount, RrOverride, RrPreset };
}

/// <summary>Versões das DLLs de DLSS que o jogo traz (null = o jogo não tem aquela parte).</summary>
public sealed record DlssFiles(string? SuperResolution, string? FrameGeneration, string? RayReconstruction)
{
    public bool HasAny => SuperResolution is not null || FrameGeneration is not null || RayReconstruction is not null;

    /// <summary>Versão principal (3.7.10.0 → 3.7) para mostrar.</summary>
    public static string Short(string? version) =>
        version is null ? "" : string.Join('.', version.Split('.').Take(2));
}

/// <summary>Geração da placa RTX (20, 30, 40, 50...) e o que ela aceita de DLSS.</summary>
public sealed record RtxCapability(int Generation)
{
    public bool SuperResolution => Generation >= 20;
    public bool FrameGeneration => Generation >= 40;
    public bool MultiFrame => Generation >= 50;
    /// <summary>Maior multiplicador de geração de quadros: 4x nas RTX 50, 2x nas RTX 40.</summary>
    public int MaxMultiplier => MultiFrame ? 4 : FrameGeneration ? 2 : 1;

    /// <summary>Lê a geração pelo nome da placa ("NVIDIA GeForce RTX 4070 Laptop GPU" → 40). Null = não é RTX.</summary>
    public static RtxCapability? FromName(string? gpuName)
    {
        if (string.IsNullOrWhiteSpace(gpuName)) return null;
        var m = Regex.Match(gpuName, @"\bRTX\s*(?:PRO\s*)?([2-9]\d)(\d{2})\b", RegexOptions.IgnoreCase);
        return m.Success ? new RtxCapability(int.Parse(m.Groups[1].Value)) : null;
    }
}

/// <summary>Uma escolha do DLSS para mostrar: rótulo e o que ela grava no driver.</summary>
public sealed record DlssOption(string Key, string Title, string Detail);

/// <summary>
/// DLSS por jogo pelo driver (como a "Substituição de DLSS" do NVIDIA App e o NVIDIA Profile Inspector): troca o modelo do DLSS sem
/// mexer nos arquivos do jogo. Só funciona em jogos que já usam DLSS; o driver precisa ser novo (R570 ou mais) para ler as substituições.
/// </summary>
public static class DlssTuning
{
    /// <summary>Modelos do DLSS Super Resolution. Os presets vêm do driver: K = transformer (DLSS 4), L e M = transformer de 2ª geração (DLSS 4.5).</summary>
    public static IReadOnlyList<DlssOption> Models { get; } = new[]
    {
        new DlssOption("game", "Padrão do jogo", "O jogo usa o DLSS que veio com ele."),
        new DlssOption("latest", "Mais recente do driver", "O driver escolhe o melhor modelo disponível (vale também para versões novas que chegarem com o driver)."),
        new DlssOption("M", "DLSS 4.5 · preset M", "Transformer de 2ª geração, recomendado para Desempenho."),
        new DlssOption("L", "DLSS 4.5 · preset L", "Transformer de 2ª geração, pensado para Ultra desempenho (4K)."),
        new DlssOption("K", "DLSS 4 · preset K", "Transformer da 1ª geração: imagem mais nítida que o DLSS 3."),
        new DlssOption("E", "DLSS 3 · preset E", "Modelo antigo (CNN): mais leve, um pouco menos nítido."),
    };

    /// <summary>Qualidade (resolução interna) forçada pelo driver.</summary>
    public static IReadOnlyList<(string Key, string Title, uint Value)> Modes { get; } = new[]
    {
        ("game", "Como o jogo escolher", DlssIds.ModeGameControlled),
        ("dlaa", "DLAA (resolução nativa, máxima nitidez)", 4u),
        ("quality", "Qualidade", 2u),
        ("balanced", "Equilíbrio", 1u),
        ("performance", "Desempenho", 0u),
        ("ultra", "Ultra desempenho", 5u),
    };

    public static uint PresetValue(char letter) => (uint)(char.ToUpperInvariant(letter) - 'A' + 1);

    /// <summary>
    /// O que gravar no driver. Null em cada parte = voltar ao padrão do jogo (remover a substituição).
    /// <paramref name="frameGen"/>: 0 = padrão do jogo, 1 = modelo mais recente, 2..4 = multiplicador (RTX 50 para 3x e 4x).
    /// </summary>
    public static (IReadOnlyList<DriverSetting> Write, IReadOnlyList<uint> Remove) Plan(string modelKey, string modeKey, int frameGen, bool rayReconstruction,
        RtxCapability? gpu, DlssFiles files)
    {
        var write = new List<DriverSetting>();
        var remove = new List<uint>();
        if (gpu is not { SuperResolution: true }) return (write, DlssIds.All);

        // Super Resolution: modelo e qualidade
        bool srTouched = false;
        if (modelKey == "game") remove.Add(DlssIds.SrPreset);
        else
        {
            uint preset = modelKey == "latest" ? DlssIds.PresetLatest : PresetValue(modelKey[0]);
            write.Add(new DriverSetting(DlssIds.SrPreset, preset, "DLSS: modelo", Models.FirstOrDefault(m => m.Key == modelKey)?.Title ?? modelKey, "", false));
            srTouched = true;
        }
        var mode = Modes.FirstOrDefault(m => m.Key == modeKey);
        if (mode.Key is null || mode.Key == "game") remove.Add(DlssIds.SrMode);
        else
        {
            write.Add(new DriverSetting(DlssIds.SrMode, mode.Value, "DLSS: qualidade", mode.Title, "", false));
            srTouched = true;
        }
        if (srTouched) write.Insert(0, new DriverSetting(DlssIds.SrOverride, 1, "DLSS: substituição ligada", "Ligada", "", false));
        else remove.Add(DlssIds.SrOverride);

        // Frame Generation (RTX 40 e 50, e só se o jogo tiver)
        if (frameGen > 0 && gpu.FrameGeneration && files.FrameGeneration is not null)
        {
            write.Add(new DriverSetting(DlssIds.FgOverride, 1, "Geração de quadros: substituição", "Ligada", "", false));
            write.Add(new DriverSetting(DlssIds.FgPreset, DlssIds.PresetLatest, "Geração de quadros: modelo", "Mais recente", "", false));
            int multiplier = Math.Clamp(frameGen, 1, gpu.MaxMultiplier);
            if (frameGen >= 2 && gpu.MultiFrame && multiplier >= 2)
                write.Add(new DriverSetting(DlssIds.MultiFrameCount, (uint)(multiplier - 1), "Geração de quadros: multiplicador", multiplier + "x", "", false));
            else remove.Add(DlssIds.MultiFrameCount);
        }
        else remove.AddRange(new[] { DlssIds.FgOverride, DlssIds.FgPreset, DlssIds.MultiFrameCount });

        // Ray Reconstruction (só se o jogo tiver)
        if (rayReconstruction && files.RayReconstruction is not null)
        {
            write.Add(new DriverSetting(DlssIds.RrOverride, 1, "Ray Reconstruction: substituição", "Ligada", "", false));
            write.Add(new DriverSetting(DlssIds.RrPreset, DlssIds.PresetLatest, "Ray Reconstruction: modelo", "Mais recente", "", false));
        }
        else remove.AddRange(new[] { DlssIds.RrOverride, DlssIds.RrPreset });

        return (write, remove.Distinct().ToList());
    }

    /// <summary>Lê as escolhas atuais a partir dos valores do perfil do jogo no driver (para mostrar a tela já marcada).</summary>
    public static (string Model, string Mode, int FrameGen, bool Rr) Current(Func<uint, uint?> read)
    {
        string model = "game";
        if (read(DlssIds.SrOverride) == 1 && read(DlssIds.SrPreset) is { } preset and > 0)
            model = preset == DlssIds.PresetLatest ? "latest" : preset <= 26 ? ((char)('A' + preset - 1)).ToString() : "latest";
        if (model.Length == 1 && Models.All(m => m.Key != model)) model = "latest";
        string mode = "game";
        if (read(DlssIds.SrOverride) == 1 && read(DlssIds.SrMode) is { } m && m != DlssIds.ModeGameControlled)
            mode = Modes.FirstOrDefault(x => x.Value == m).Key ?? "game";
        int fg = 0;
        if (read(DlssIds.FgOverride) == 1) fg = read(DlssIds.MultiFrameCount) is { } c and > 0 ? (int)c + 1 : 1;
        bool rr = read(DlssIds.RrOverride) == 1;
        return (model, mode, fg, rr);
    }

    /// <summary>Procura as DLLs do DLSS na pasta do jogo (até 4 níveis) e lê a versão de cada uma.</summary>
    public static DlssFiles Scan(string installPath)
    {
        string? Find(string name)
        {
            try
            {
                var file = Directory.EnumerateFiles(installPath, name, new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4, IgnoreInaccessible = true }).FirstOrDefault();
                if (file is null) return null;
                var info = FileVersionInfo.GetVersionInfo(file);
                return info.FileVersion is { Length: > 0 } v ? v.Replace(',', '.').Replace(" ", "") : "?";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
        }
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath)) return new DlssFiles(null, null, null);
        return new DlssFiles(Find("nvngx_dlss.dll"), Find("nvngx_dlssg.dll"), Find("nvngx_dlssd.dll"));
    }

    /// <summary>Texto de compatibilidade para a tela.</summary>
    public static string Compatibility(RtxCapability? gpu, DlssFiles files)
    {
        if (gpu is null) return "DLSS precisa de uma placa NVIDIA RTX. Nesta placa, use o FSR do próprio jogo.";
        if (!files.HasAny) return "Este jogo não traz DLSS (não achei o nvngx_dlss.dll na pasta). A substituição do driver não tem efeito nele.";
        var parts = new List<string>();
        if (files.SuperResolution is { } sr) parts.Add($"DLSS {DlssFiles.Short(sr)}");
        if (files.FrameGeneration is { } fg) parts.Add($"geração de quadros {DlssFiles.Short(fg)}");
        if (files.RayReconstruction is { } rr) parts.Add($"Ray Reconstruction {DlssFiles.Short(rr)}");
        string gen = gpu.MultiFrame ? "até 4x de geração de quadros" : gpu.FrameGeneration ? "geração de quadros 2x" : "sem geração de quadros (só RTX 40 e 50)";
        return $"O jogo traz {string.Join(", ", parts)}. Sua RTX {gpu.Generation}xx aceita DLSS 4/4.5 e {gen}. Funciona na maioria dos jogos com DLSS 3.1 ou mais novo; alguns ignoram a troca.";
    }
}
