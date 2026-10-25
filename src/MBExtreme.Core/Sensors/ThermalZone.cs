using System.Management;
using System.Runtime.Versioning;

namespace MBExtreme.Core.Sensors;

/// <summary>Temperatura aproximada pelas zonas térmicas do Windows (ACPI). Serve de reserva quando o sensor do núcleo não responde.</summary>
[SupportedOSPlatform("windows")]
public static class ThermalZone
{
    public static double? Read()
    {
        // contador de desempenho: funciona sem administrador na maioria dos PCs
        double? best = Query("root\\CIMV2", "SELECT Temperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation", "Temperature", SensorRules.FromKelvin);
        if (best is not null) return best;
        return Query("root\\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature", "CurrentTemperature", SensorRules.FromTenthsOfKelvin);
    }

    private static double? Query(string scope, string query, string property, Func<double, double?> convert)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, query);
            double? max = null;
            foreach (ManagementBaseObject item in searcher.Get())
            {
                if (item[property] is null) continue;
                if (convert(Convert.ToDouble(item[property])) is { } c) max = Math.Max(max ?? 0, c);
            }
            return max;
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidCastException)
        {
            return null;
        }
    }
}
