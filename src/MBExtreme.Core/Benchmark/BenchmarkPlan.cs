namespace MBExtreme.Core.Benchmark;

/// <summary>Os três modos do benchmark. Cada um exige mais do PC e demora mais, em troca de um resultado mais estável.</summary>
public enum BenchmarkMode { Rapido, Padrao, Preciso }

/// <summary>Quanto cada modo exige: tempo de cada medição da CPU, tamanho do arquivo do disco, segundos por cena 3D e amostras por pixel do render.</summary>
public sealed record BenchmarkProfile(
    BenchmarkMode Mode,
    string Name,
    string Description,
    int PhaseMs,
    int DiskMegabytes,
    int GpuSecondsPerScene,
    int StudioSamples,
    bool QuickLatency);

public static class BenchmarkModes
{
    public static readonly IReadOnlyList<BenchmarkProfile> All = new[]
    {
        new BenchmarkProfile(BenchmarkMode.Rapido, "Rápido", "Uma conferida rápida. Carga leve e curta: bom para ver se algo mudou.", 1200, 128, 3, 2, true),
        new BenchmarkProfile(BenchmarkMode.Padrao, "Padrão", "O equilíbrio entre tempo e precisão. Recomendado para comparar antes e depois.", 2500, 512, 6, 4, false),
        new BenchmarkProfile(BenchmarkMode.Preciso, "Preciso", "Carga longa e pesada em tudo: o resultado varia menos, mas o PC esquenta mais.", 5000, 2048, 12, 8, false),
    };

    public static BenchmarkProfile Get(BenchmarkMode mode) => All.First(p => p.Mode == mode);

    /// <summary>Modo efetivo de um pedido: o escolhido ou, nas chamadas antigas, Rápido quando Quick e Padrão no resto.</summary>
    public static BenchmarkProfile For(BenchmarkOptions options) =>
        Get(options.Mode ?? (options.Quick ? BenchmarkMode.Rapido : BenchmarkMode.Padrao));

    /// <summary>Quantas cenas 3D a GPU roda (fractal, geometria, preenchimento, cálculo e o render do estúdio).</summary>
    public const int GpuScenes = 5;

    /// <summary>Estimativa da duração em segundos (o disco conta 400 MB/s, um SSD comum; um HD demora mais).</summary>
    public static int EstimatedSeconds(BenchmarkOptions options)
    {
        var p = For(options);
        double seconds = 1;
        if (options.Cpu) seconds += 5 * p.PhaseMs / 1000.0;
        if (options.Memory) seconds += 1.5 * p.PhaseMs / 1000.0 + (p.QuickLatency ? 1 : 3);
        if (options.Disk) seconds += p.DiskMegabytes * 2 / 400.0 + 1;
        if (options.Gpu3D) seconds += GpuScenes * p.GpuSecondsPerScene + 2;
        return (int)Math.Ceiling(seconds);
    }

    public static string Duration(int seconds) =>
        seconds < 60 ? $"~{seconds} s" : seconds % 60 < 10 ? $"~{seconds / 60} min" : $"~{seconds / 60} min {seconds % 60} s";
}

/// <summary>Uma resolução para as cenas 3D.</summary>
public sealed record BenchmarkResolution(int Width, int Height, string Name)
{
    public string Label => $"{Name} ({Width}×{Height})";
    public long Pixels => (long)Width * Height;
}

public static class BenchmarkResolutions
{
    public static readonly BenchmarkResolution Hd = new(1280, 720, "HD 720p");
    public static readonly BenchmarkResolution FullHd = new(1920, 1080, "Full HD 1080p");
    public static readonly BenchmarkResolution Qhd = new(2560, 1440, "Quad HD 1440p");
    public static readonly BenchmarkResolution Uhd = new(3840, 2160, "4K 2160p");

    /// <summary>
    /// Resoluções que o monitor aguenta: 720p e 1080p sempre (a cena é renderizada fora da tela), Quad HD e 4K só quando a tela chega lá,
    /// e a resolução nativa quando ela é diferente (ultrawide 3440×1440, notebook 2880×1800...).
    /// </summary>
    public static IReadOnlyList<BenchmarkResolution> For(int nativeWidth, int nativeHeight)
    {
        var list = new List<BenchmarkResolution> { Hd, FullHd };
        if (nativeWidth >= Qhd.Width && nativeHeight >= Qhd.Height) list.Add(Qhd);
        if (nativeWidth >= Uhd.Width && nativeHeight >= Uhd.Height) list.Add(Uhd);
        if (nativeWidth > 0 && nativeHeight > 0 && nativeWidth <= 7680 && nativeHeight <= 4320
            && !list.Any(r => r.Width == nativeWidth && r.Height == nativeHeight)
            && (long)nativeWidth * nativeHeight > Hd.Pixels)
            list.Add(new BenchmarkResolution(nativeWidth, nativeHeight, "Nativa do monitor"));
        return list.OrderBy(r => r.Pixels).ToList();
    }

    /// <summary>A sugerida: a do monitor quando é 1080p ou menos; senão 1080p, que é a referência das notas.</summary>
    public static BenchmarkResolution Suggested(IReadOnlyList<BenchmarkResolution> options) =>
        options.FirstOrDefault(r => r == FullHd) ?? options[0];
}

/// <summary>Um disco para o teste de gravação e leitura.</summary>
public sealed record BenchmarkDisk(string Root, string Label, string Format, long FreeBytes, long TotalBytes, bool IsSystem)
{
    public string Letter => Root.TrimEnd('\\', '/');
    public string Title => Label.Length > 0 ? $"{Label} ({Letter})" : $"Disco local ({Letter})";
    public double UsedPercent => TotalBytes > 0 ? 100.0 * (TotalBytes - FreeBytes) / TotalBytes : 0;
}

public static class BenchmarkDisks
{
    /// <summary>Discos fixos prontos (sem pendrive nem rede), o do Windows primeiro.</summary>
    public static IReadOnlyList<BenchmarkDisk> List()
    {
        string system = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";
        var list = new List<BenchmarkDisk>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                list.Add(new BenchmarkDisk(d.RootDirectory.FullName, d.VolumeLabel ?? "", d.DriveFormat ?? "", d.AvailableFreeSpace, d.TotalSize,
                    string.Equals(d.RootDirectory.FullName, system, StringComparison.OrdinalIgnoreCase)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        return Order(list);
    }

    public static IReadOnlyList<BenchmarkDisk> Order(IEnumerable<BenchmarkDisk> disks) =>
        disks.OrderByDescending(d => d.IsSystem).ThenBy(d => d.Root, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Dá para testar: sobra o dobro do arquivo de teste e mais 1 GB livre depois.</summary>
    public static bool HasRoom(BenchmarkDisk disk, int megabytes) => disk.FreeBytes >= (long)megabytes * 1024 * 1024 * 2 + (1L << 30);

    /// <summary>Pasta do arquivo temporário: a pasta temporária do Windows no disco do sistema; nos outros, uma pasta própria na raiz.</summary>
    public static string DirectoryFor(BenchmarkDisk disk, string tempPath)
    {
        if (string.Equals(Path.GetPathRoot(tempPath), disk.Root, StringComparison.OrdinalIgnoreCase)) return tempPath;
        return Path.Combine(disk.Root, "MBExtreme-teste-de-disco");
    }
}
