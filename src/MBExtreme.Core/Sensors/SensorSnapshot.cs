namespace MBExtreme.Core.Sensors;

public sealed record FanReading(string Name, double Rpm);

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
