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
    bool QuickLatency,
    int Passes = 1);

public static class BenchmarkModes
{
    public static readonly IReadOnlyList<BenchmarkProfile> All = new[]
    {
        new BenchmarkProfile(BenchmarkMode.Rapido, "Rápido", "Uma conferida rápida (~1 min). Carga leve e curta: bom para ver se algo mudou.", 1200, 128, 4, 2, true, 1),
        new BenchmarkProfile(BenchmarkMode.Padrao, "Padrão", "Cerca de 7 minutos de carga contínua, com 2 passadas: dá tempo de o PC esquentar e mostra se a temperatura derruba o desempenho. Recomendado para comparar antes e depois.", 6000, 1024, 20, 6, false, 2),
        new BenchmarkProfile(BenchmarkMode.Preciso, "Preciso", "Cerca de 12 minutos de carga pesada em tudo, 3 passadas e vale a mediana: o teste de estabilidade e de calor. O PC esquenta bastante.", 9000, 4096, 26, 12, false, 3),
    };

    public static BenchmarkProfile Get(BenchmarkMode mode) => All.First(p => p.Mode == mode);

    /// <summary>Modo efetivo de um pedido: o escolhido ou, nas chamadas antigas, Rápido quando Quick e Padrão no resto.</summary>
    public static BenchmarkProfile For(BenchmarkOptions options) =>
        Get(options.Mode ?? (options.Quick ? BenchmarkMode.Rapido : BenchmarkMode.Padrao));

    /// <summary>Tempo estimado de uma etapa em segundos (o disco conta 400 MB/s, um SSD comum; um HD demora mais).</summary>
    public static double StepSeconds(BenchmarkOptions options, string step)
    {
        var p = For(options);
        double task = Math.Max(150, p.PhaseMs / 2) / 1000.0;
        int resolutions = BenchmarkService.LimitResolutions(options).Count;
        double random = 3 * Math.Max(400, p.PhaseMs / 2) / 1000.0;
        return step switch
        {
            BenchmarkService.StepSingle => CpuTasks.Tasks.Count * task * p.Passes + 0.3,
            BenchmarkService.StepMulti => (CpuTasks.Tasks.Count + 1) * task * p.Passes + 0.3,
            BenchmarkService.StepMemory => (1.5 * p.PhaseMs / 1000.0 + (p.QuickLatency ? 1 : 3)) * p.Passes,
            BenchmarkService.StepDisk => p.DiskMegabytes * 2 / 400.0 + random + 1,
            BenchmarkService.StepAi => (4 * task + 1) * p.Passes + (options.Gpu3D ? Math.Max(1.0, p.PhaseMs / 1000.0) + 1 : 0),
            BenchmarkService.StepDaily => 14.0 * p.Passes,
            GpuWindowBench.ResolutionLimit => resolutions * (Math.Max(3, p.GpuSecondsPerScene / 2) + 0.6),
            GpuWindowBench.Studio => (p.GpuSecondsPerScene + 1.5) * p.Passes,
            Dx12RayBench.Step => 2 * p.GpuSecondsPerScene + 3.0,
            FsrBench.Step => UpscaleModes.All.Count * (FsrSecondsPerMode(p) + 4.0),
            _ when BenchmarkService.GpuSteps.Contains(step) => (p.GpuSecondsPerScene + 0.7) * p.Passes + 0.5,
            _ => 1,
        };
    }

    /// <summary>Segundos de cada modo do FSR: metade de uma cena 3D (no mínimo 5), para o benchmark não passar do tempo do modo.</summary>
    public static int FsrSecondsPerMode(BenchmarkProfile p) => Math.Max(5, p.GpuSecondsPerScene / 2);

    /// <summary>Estimativa da duração total em segundos.</summary>
    public static int EstimatedSeconds(BenchmarkOptions options) =>
        (int)Math.Ceiling(1 + BenchmarkService.StepsFor(options).Sum(s => StepSeconds(options, s)));

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
