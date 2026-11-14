namespace MBExtreme.Core.Sensors;

public sealed record FanReading(string Name, double Rpm, bool IsGpu = false);

/// <summary>Ventoinhas separadas por quem elas esfriam: processador (inclui a bomba do cooler líquido), placa de vídeo e gabinete.</summary>
public sealed record FanGroups(IReadOnlyList<FanReading> Cpu, IReadOnlyList<FanReading> Gpu, IReadOnlyList<FanReading> Case)
{
    public static FanGroups Split(IEnumerable<FanReading> fans)
    {
        var cpu = new List<FanReading>();
        var gpu = new List<FanReading>();
        var other = new List<FanReading>();
        foreach (var fan in fans)
        {
            if (fan.IsGpu) gpu.Add(fan);
            else if (IsCpuSide(fan.Name)) cpu.Add(fan);
            else other.Add(fan);
        }

        return new FanGroups(cpu, gpu, other);
    }

    private static bool IsCpuSide(string name) =>
        name.Contains("cpu", StringComparison.OrdinalIgnoreCase) || name.Contains("pump", StringComparison.OrdinalIgnoreCase)
        || name.Contains("aio", StringComparison.OrdinalIgnoreCase) || name.Contains("cooler", StringComparison.OrdinalIgnoreCase);

    /// <summary>Nome curto em português para mostrar ao lado da rotação.</summary>
    public static string Label(FanReading fan)
    {
        string name = fan.Name;
        string digits = new(name.Where(char.IsDigit).ToArray());
        if (name.Contains("pump", StringComparison.OrdinalIgnoreCase)) return "Bomba";
        if (fan.IsGpu) return digits.Length > 0 ? "Ventoinha " + digits : "Ventoinha";
        if (name.Contains("cpu", StringComparison.OrdinalIgnoreCase))
            return name.Contains("opt", StringComparison.OrdinalIgnoreCase) ? "CPU (opcional)" : "CPU";
        if (name.Contains("aio", StringComparison.OrdinalIgnoreCase) || name.Contains("cooler", StringComparison.OrdinalIgnoreCase)) return "Cooler";
        return digits.Length > 0 ? "Gabinete " + digits : name;
    }

    /// <summary>Linha de texto: "CPU 2277 rpm · Bomba 2047 rpm". Vazio quando não há leitura.</summary>
    public static string Describe(IEnumerable<FanReading> fans) =>
        string.Join("  ·  ", fans.Select(f => $"{Label(f)} {f.Rpm:0} rpm"));
}

/// <summary>Leitura dos sensores num instante. Valores nulos significam "sensor indisponível".</summary>
public sealed record SensorSnapshot(
    bool Available,
    string? Message,
    double? CpuTemperature,
    double? CpuLoad,
    double? CpuPowerWatts,
    double? CpuClockMhz,
    double? GpuTemperature,
    double? GpuLoad,
    double? GpuPowerWatts,
    double? GpuClockMhz,
    IReadOnlyList<FanReading> Fans)
{
    /// <summary>Detalhes para o Painel ao vivo (núcleos, tensões, memória de vídeo, temperatura dos pentes). Null quando não leu.</summary>
    public SensorDetail? Detail { get; init; }

    public static SensorSnapshot Unavailable(string message) =>
        new(false, message, null, null, null, null, null, null, null, null, Array.Empty<FanReading>());
}

/// <summary>Leituras extras do Painel ao vivo. Listas vazias e nulos = o PC não informa.</summary>
public sealed record SensorDetail(
    string? CpuName,
    IReadOnlyList<double> CoreLoads,
    double? CpuVoltage,
    string? GpuName,
    double? GpuVoltage,
    double? GpuMemoryClockMhz,
    double? GpuHotspot,
    double? GpuVramUsedMb,
    double? GpuVramTotalMb,
    IReadOnlyList<(string Name, double Celsius)> MemoryTemperatures);

/// <summary>Escolhas das leituras extras (nomes dos sensores mudam entre Intel, AMD e placas-mãe).</summary>
public static class SensorPick
{
    /// <summary>Uso por thread na ordem do processador: "CPU Core #2 Thread #1" vem depois de "CPU Core #1 Thread #2". Sem threads, usa os núcleos.</summary>
    public static IReadOnlyList<double> CoreLoads(IEnumerable<(string Name, double Value)> loads)
    {
        var list = loads.Where(l => l.Name.StartsWith("CPU Core #", StringComparison.OrdinalIgnoreCase)).ToList();
        bool threads = list.Any(l => l.Name.Contains("Thread", StringComparison.OrdinalIgnoreCase));
        return list
            .Where(l => !threads || l.Name.Contains("Thread", StringComparison.OrdinalIgnoreCase))
            .Select(l => (Key: Numbers(l.Name), l.Value))
            .OrderBy(l => l.Key.Core).ThenBy(l => l.Key.Thread)
            .Select(l => Math.Clamp(l.Value, 0, 100))
            .ToList();
    }

    private static (int Core, int Thread) Numbers(string name)
    {
        var digits = System.Text.RegularExpressions.Regex.Matches(name, "\\d+").Select(m => int.Parse(m.Value)).ToList();
        return (digits.Count > 0 ? digits[0] : 0, digits.Count > 1 ? digits[1] : 0);
    }

    /// <summary>Tensão do núcleo: a medida (SVI2, "CPU Core", Vcore da placa-mãe) antes da pedida (VID).</summary>
    public static double? CpuVoltage(IEnumerable<(string Name, double Value)> cpuVolts, IEnumerable<(string Name, double Value)> boardVolts)
    {
        static bool Plausible(double v) => v is > 0.4 and < 2.0;
        var cpu = cpuVolts.Where(v => Plausible(v.Value)).ToList();
        var measured = cpu.FirstOrDefault(v => v.Name.Contains("SVI2", StringComparison.OrdinalIgnoreCase) && v.Name.Contains("Core", StringComparison.OrdinalIgnoreCase));
        if (measured.Name is not null) return measured.Value;
        var core = cpu.FirstOrDefault(v => v.Name.Equals("CPU Core", StringComparison.OrdinalIgnoreCase) || v.Name.Equals("Core", StringComparison.OrdinalIgnoreCase));
        if (core.Name is not null) return core.Value;
        var board = boardVolts.FirstOrDefault(v => Plausible(v.Value) && (v.Name.Contains("Vcore", StringComparison.OrdinalIgnoreCase) || v.Name.Equals("CPU Core", StringComparison.OrdinalIgnoreCase)));
        if (board.Name is not null) return board.Value;
        var vids = cpu.Where(v => v.Name.Contains("VID", StringComparison.OrdinalIgnoreCase)).Select(v => v.Value).ToList();
        return vids.Count > 0 ? vids.Max() : null;
    }

    /// <summary>Water cooler quando alguma leitura é da bomba (ou de um AIO).</summary>
    public static bool IsLiquid(IEnumerable<FanReading> fans) =>
        fans.Any(f => f.Name.Contains("pump", StringComparison.OrdinalIgnoreCase) || f.Name.Contains("aio", StringComparison.OrdinalIgnoreCase)
                      || f.Name.Contains("bomba", StringComparison.OrdinalIgnoreCase));
}
