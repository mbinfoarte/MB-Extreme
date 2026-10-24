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
    public static SensorSnapshot Unavailable(string message) =>
        new(false, message, null, null, null, null, null, null, null, null, Array.Empty<FanReading>());
}
