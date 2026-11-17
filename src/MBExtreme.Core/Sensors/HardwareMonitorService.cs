using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using LibreHardwareMonitor.Hardware;

namespace MBExtreme.Core.Sensors;

/// <summary>Uma leitura de sensor. <see cref="Kind"/> é a categoria (Clock, Load, Temperature, Voltage, Power, Fan...).</summary>
public sealed record SensorValue(string Id, string Name, string Kind, double? Value, double? Min, double? Max);

public sealed record SensorGroup(string Name, string Type, IReadOnlyList<SensorValue> Sensors);

/// <summary>
/// Leitura completa dos sensores em tempo real: clock e carga por núcleo, temperaturas, tensões, potência, ventoinhas,
/// memória, discos e rede. Usa instância própria da biblioteca, aberta só enquanto a página está em uso.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HardwareMonitorService : IDisposable
{
    private readonly Computer? _computer;

    public HardwareMonitorService()
    {
        try
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsControllerEnabled = true,
                IsStorageEnabled = true,
                IsNetworkEnabled = true,
                IsBatteryEnabled = true,
            };
            _computer.Open();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            OpenError = ex.Message;
            _computer = null;
        }
    }

    public string? OpenError { get; }

    public bool IsAvailable => _computer is not null;

    public IReadOnlyList<SensorGroup> ReadAll()
    {
        var groups = new List<SensorGroup>();
        if (_computer is null) return groups;

        foreach (var hardware in Flatten(_computer.Hardware))
        {
            hardware.Update();
            var sensors = hardware.Sensors
                .Where(s => s.Value is not null)
                .Select(s => new SensorValue(s.Identifier.ToString(), s.Name, s.SensorType.ToString(), s.Value, s.Min, s.Max))
                .ToList();
            if (sensors.Count == 0) continue;
            groups.Add(new SensorGroup(hardware.Name, hardware.HardwareType.ToString(), sensors));
        }

        return groups.OrderBy(g => TypeOrder(g.Type)).ThenBy(g => g.Name).ToList();
    }

    private static int TypeOrder(string type) => type switch
    {
        "Cpu" => 0,
        "GpuNvidia" or "GpuAmd" or "GpuIntel" => 1,
        "Memory" => 2,
        "Motherboard" or "SuperIO" => 3,
        "EmbeddedController" or "Cooler" => 4,
        "Storage" => 5,
        "Network" => 6,
        _ => 9,
    };

    private static IEnumerable<IHardware> Flatten(IEnumerable<IHardware> hardware)
    {
        foreach (var item in hardware)
        {
            yield return item;
            foreach (var child in Flatten(item.SubHardware)) yield return child;
        }
    }

    public static string KindTitle(string kind) => kind switch
    {
        "Clock" => "Clocks",
        "Load" => "Uso",
        "Temperature" => "Temperaturas",
        "Voltage" => "Tensões",
        "Power" => "Potência",
        "Current" => "Corrente",
        "Fan" => "Ventoinhas",
        "Control" => "Controle das ventoinhas",
        "Data" or "SmallData" => "Memória e dados",
        "Throughput" => "Taxas de transferência",
        "Level" => "Níveis",
        "Energy" => "Energia",
        "Factor" => "Fatores",
        _ => kind,
    };

    public static int KindOrder(string kind) => kind switch
    {
        "Clock" => 0,
        "Load" => 1,
        "Temperature" => 2,
        "Voltage" => 3,
        "Power" => 4,
        "Current" => 5,
        "Fan" => 6,
        "Control" => 7,
        _ => 9,
    };

    /// <summary>Texto do valor com a unidade certa (MHz/GHz, °C, V, W, %, RPM, GB, MB/s).</summary>
    public static string Format(string kind, double? value)
    {
        if (value is not { } v) return "—";
        var c = CultureInfo.CurrentCulture;
        return kind switch
        {
            "Clock" => v >= 1000 ? (v / 1000).ToString("0.00", c) + " GHz" : v.ToString("0", c) + " MHz",
            "Temperature" => v.ToString("0.#", c) + " °C",
            "Voltage" => v.ToString("0.000", c) + " V",
            "Power" => v.ToString("0.0", c) + " W",
            "Current" => v.ToString("0.00", c) + " A",
            "Load" or "Control" or "Level" => v.ToString("0.#", c) + " %",
            "Fan" => v.ToString("0", c) + " RPM",
            "Data" => v.ToString("0.0", c) + " GB",
            "SmallData" => v.ToString("0", c) + " MB",
            "Throughput" => v >= 1_000_000 ? (v / 1_000_000).ToString("0.0", c) + " MB/s" : (v / 1000).ToString("0.0", c) + " kB/s",
            "Energy" => v.ToString("0", c) + " mWh",
            _ => v.ToString("0.##", c),
        };
    }

    public static string ToCsv(IEnumerable<SensorGroup> groups)
    {
        var csv = new StringBuilder();
        csv.AppendLine("Componente;Categoria;Sensor;Valor;Mínimo;Máximo");
        foreach (var g in groups)
            foreach (var s in g.Sensors.OrderBy(s => KindOrder(s.Kind)))
                csv.AppendLine($"{Escape(g.Name)};{s.Kind};{Escape(s.Name)};{Format(s.Kind, s.Value)};{Format(s.Kind, s.Min)};{Format(s.Kind, s.Max)}");
        return csv.ToString();
    }

    private static string Escape(string text) => text.Contains(';') ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;

    public void Dispose() => _computer?.Close();
}
