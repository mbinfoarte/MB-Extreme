using System.Runtime.Versioning;
using LibreHardwareMonitor.Hardware;

namespace MBExtreme.Core.Sensors;

/// <summary>Lê temperaturas, cargas, energia e ventoinhas pela biblioteca LibreHardwareMonitor. Requer administrador.</summary>
[SupportedOSPlatform("windows")]
public sealed class SensorService : IDisposable
{
    private readonly Computer? _computer;
    private readonly string? _openError;

    public SensorService()
    {
        try
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMotherboardEnabled = true,
                IsControllerEnabled = true,
            };
            _computer.Open();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _openError = ex.Message;
            _computer = null;
        }
    }

    private static bool IsAdministrator { get; } = DetectAdmin();

    private static bool DetectAdmin()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { return false; }
    }

    public SensorSnapshot Read()
    {
        if (_computer is null)
            return SensorSnapshot.Unavailable(_openError ?? "Sensores indisponíveis.");

        bool cpuFound = false;
        int cpuSensors = 0;
        double? cpuTemp = null, cpuLoad = null, cpuPower = null, cpuClock = null;
        double? gpuTemp = null, gpuLoad = null, gpuPower = null, gpuClock = null;
        var fans = new List<FanReading>();

        try
        {
            foreach (var hardware in Flatten(_computer.Hardware))
            {
                hardware.Update();
                bool isCpu = hardware.HardwareType == HardwareType.Cpu;
                if (isCpu) { cpuFound = true; cpuSensors += hardware.Sensors.Count(x => x.SensorType == SensorType.Temperature && x.Value is float t && SensorRules.IsValidTemperature(t)); }
                bool isGpu = hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;

                foreach (var sensor in hardware.Sensors)
                {
                    if (sensor.Value is not float raw) continue;
                    double value = raw;
                    if (sensor.SensorType == SensorType.Temperature && !SensorRules.IsValidTemperature(value)) continue; // 0 °C é leitura quebrada, não temperatura
                    string name = sensor.Name;

                    if (sensor.SensorType == SensorType.Fan && value > 0)
                        fans.Add(new FanReading(name, value, isGpu));

                    if (isCpu)
                    {
                        switch (sensor.SensorType)
                        {
                            case SensorType.Temperature when IsPreferredCpuTemperature(name):
                                cpuTemp = Math.Max(cpuTemp ?? 0, value);
                                break;
                            case SensorType.Temperature:
                                cpuTemp ??= value;
                                break;
                            case SensorType.Load when name == "CPU Total":
                                cpuLoad = value;
                                break;
                            case SensorType.Power when name.Contains("Package", StringComparison.OrdinalIgnoreCase):
                                cpuPower = value;
                                break;
                            case SensorType.Clock when name.StartsWith("Core #", StringComparison.OrdinalIgnoreCase):
                                cpuClock = cpuClock is null ? value : (cpuClock + value) / 2;
                                break;
                        }
                    }
                    else if (isGpu)
                    {
                        switch (sensor.SensorType)
                        {
                            case SensorType.Temperature when name == "GPU Core":
                                gpuTemp = value;
                                break;
                            case SensorType.Temperature:
                                gpuTemp ??= value;
                                break;
                            case SensorType.Load when name == "GPU Core":
                                gpuLoad = value;
                                break;
                            case SensorType.Power:
                                gpuPower ??= value;
                                break;
                            case SensorType.Clock when name == "GPU Core":
                                gpuClock = value;
                                break;
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return SensorSnapshot.Unavailable("Falha ao ler os sensores: " + ex.Message);
        }

        string? message = null;
        if (cpuTemp is null)
        {
            // leitura reserva: zona térmica do Windows (aproximada, mas não precisa de driver)
            cpuTemp = ThermalZone.Read();
            message = cpuTemp is null
                ? SensorRules.WhyCpuTemperatureIsMissing(IsAdministrator, _openError, cpuFound, cpuSensors)
                : "Temperatura aproximada (zona térmica do Windows); para a leitura exata do núcleo, rode como administrador e instale o PawnIO.";
        }
        return new SensorSnapshot(true, message, cpuTemp, cpuLoad, cpuPower, cpuClock,
            gpuTemp, gpuLoad, gpuPower, gpuClock, fans);
    }

    /// <summary>Texto para o suporte: administrador, o que a biblioteca enxergou e cada sensor com valor.</summary>
    public string Diagnose()
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine($"Administrador: {(IsAdministrator ? "sim" : "não")}");
        text.AppendLine($"Sistema: {Environment.OSVersion.VersionString}");
        if (_openError is not null) text.AppendLine("Erro ao abrir: " + _openError);
        if (_computer is null) return text.ToString();
        try
        {
            foreach (var hardware in Flatten(_computer.Hardware))
            {
                hardware.Update();
                text.AppendLine($"\n[{hardware.HardwareType}] {hardware.Name} ({hardware.Sensors.Length} sensores)");
                foreach (var sensor in hardware.Sensors)
                    text.AppendLine($"  {sensor.SensorType,-12} {sensor.Name,-28} {(sensor.Value is float v ? v.ToString("0.##") : "sem valor")}");
            }
            text.AppendLine($"\nZona térmica do Windows: {(ThermalZone.Read() is { } z ? z + " °C" : "indisponível")}");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { text.AppendLine("Falha na leitura: " + ex.Message); }
        return text.ToString();
    }

    private static bool IsPreferredCpuTemperature(string name) =>
        name.Contains("Package", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Tctl", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Tdie", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Average", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<IHardware> Flatten(IEnumerable<IHardware> hardware)
    {
        foreach (var item in hardware)
        {
            yield return item;
            foreach (var child in Flatten(item.SubHardware))
                yield return child;
        }
    }

    public void Dispose() => _computer?.Close();
}
