using System.Management;
using System.Runtime.Versioning;
using Microsoft.Win32;
using MBExtreme.Core.Drivers;
using MBExtreme.Core.Info;
using MBExtreme.Core.Windows;

namespace MBExtreme.Core.Hardware;

/// <summary>Lê o hardware do PC (WMI e registro). Somente leitura.</summary>
[SupportedOSPlatform("windows")]
public static class HardwareService
{
    public static HardwareInventory Collect()
    {
        var cpu = ReadCpu();
        var gpus = ReadGpus();
        bool laptop = ReadIsLaptop();
        return new HardwareInventory(
            cpu,
            ReadBoard(),
            ReadSystemModel(),
            ReadBios(),
            ReadMemory(),
            gpus,
            ReadWindows(),
            laptop,
            cpu.IsAmd ? IsInstalled("AMD Chipset Software", "AMD Ryzen Chipset", "AMD Chipset Drivers") : null,
            gpus.Any(g => g.Vendor == GpuVendor.Nvidia) ? ReadNvidiaResizableBar() : null,
            ReadRefreshRate());
    }

    private static IEnumerable<ManagementBaseObject> Query(string wql)
    {
        using var searcher = new ManagementObjectSearcher(wql);
        foreach (ManagementBaseObject item in searcher.Get()) yield return item;
    }

    private static string Str(ManagementBaseObject o, string name) => o[name]?.ToString()?.Trim() ?? "";

    private static long Long(ManagementBaseObject o, string name) =>
        long.TryParse(o[name]?.ToString(), out long value) ? value : 0;

    private static CpuInfo ReadCpu()
    {
        try
        {
            var item = Query("SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, L2CacheSize, L3CacheSize FROM Win32_Processor").First();
            return new CpuInfo(Str(item, "Name"), Str(item, "Manufacturer"), (int)Long(item, "NumberOfCores"),
                (int)Long(item, "NumberOfLogicalProcessors"), (int)Long(item, "MaxClockSpeed"),
                Long(item, "L2CacheSize"), Long(item, "L3CacheSize"), ReadMicrocode());
        }
        catch (Exception ex) when (ex is ManagementException or InvalidOperationException)
        {
            return new CpuInfo("Processador", "", Environment.ProcessorCount, Environment.ProcessorCount, 0, 0, 0, null);
        }
    }

    private static uint? ReadMicrocode()
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = root.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return HardwareAdvisor.ParseMicrocode(key?.GetValue("Update Revision") as byte[]);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static BoardInfo ReadBoard()
    {
        try
        {
            var item = Query("SELECT Manufacturer, Product FROM Win32_BaseBoard").First();
            return new BoardInfo(Str(item, "Manufacturer"), Str(item, "Product"));
        }
        catch (Exception ex) when (ex is ManagementException or InvalidOperationException)
        {
            return new BoardInfo("", "");
        }
    }

    private static SystemModel ReadSystemModel()
    {
        try
        {
            var item = Query("SELECT Manufacturer, Model FROM Win32_ComputerSystem").First();
            return new SystemModel(Str(item, "Manufacturer"), Str(item, "Model"));
        }
        catch (Exception ex) when (ex is ManagementException or InvalidOperationException)
        {
            return new SystemModel("", "");
        }
    }

    private static BiosInfo ReadBios()
    {
        try
        {
            var item = Query("SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS").First();
            DateTime? date = null;
            if (item["ReleaseDate"] is string raw && raw.Length >= 8)
            {
                try { date = ManagementDateTimeConverter.ToDateTime(raw); }
                catch (ArgumentOutOfRangeException) { }
            }

            return new BiosInfo(Str(item, "Manufacturer"), Str(item, "SMBIOSBIOSVersion"), date);
        }
        catch (Exception ex) when (ex is ManagementException or InvalidOperationException)
        {
            return new BiosInfo("", "", null);
        }
    }

    private static IReadOnlyList<MemoryModule> ReadMemory()
    {
        var list = new List<MemoryModule>();
        try
        {
            foreach (var item in Query("SELECT DeviceLocator, Capacity, Speed, ConfiguredClockSpeed, SMBIOSMemoryType, Manufacturer, PartNumber FROM Win32_PhysicalMemory"))
            {
                list.Add(new MemoryModule(Str(item, "DeviceLocator"), Long(item, "Capacity"), (int)Long(item, "Speed"),
                    (int)Long(item, "ConfiguredClockSpeed"), HardwareAdvisor.MemoryTypeName((int)Long(item, "SMBIOSMemoryType")),
                    Str(item, "Manufacturer"), Str(item, "PartNumber")));
            }
        }
        catch (ManagementException)
        {
            // sem leitura de pentes: os avisos de memória são ignorados
        }

        return list;
    }

    private static IReadOnlyList<GpuSummary> ReadGpus()
    {
        try
        {
            return DriverService.GpuDrivers()
                .Select(g => new GpuSummary(g.Name, (long)(SystemInfoService.VideoMemoryFromRegistry(g.Name) ?? 0), g.WindowsVersion, g.Vendor))
                .ToList();
        }
        catch (Exception ex) when (ex is ManagementException or InvalidOperationException or System.Security.SecurityException or UnauthorizedAccessException)
        {
            return Array.Empty<GpuSummary>();
        }
    }

    private static WindowsInfo ReadWindows()
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        string product = key?.GetValue("ProductName") as string ?? "Windows";
        string display = key?.GetValue("DisplayVersion") as string ?? key?.GetValue("ReleaseId") as string ?? "";
        int build = int.TryParse(key?.GetValue("CurrentBuildNumber") as string, out int b) ? b : Environment.OSVersion.Version.Build;
        if (build >= 22000 && product.Contains("Windows 10")) product = product.Replace("Windows 10", "Windows 11");
        return new WindowsInfo(product, display, build);
    }

    private static bool ReadIsLaptop()
    {
        try
        {
            foreach (var item in Query("SELECT ChassisTypes FROM Win32_SystemEnclosure"))
            {
                if (item["ChassisTypes"] is ushort[] types && types.Any(t => t is 8 or 9 or 10 or 11 or 12 or 14 or 18 or 21 or 31 or 32))
                    return true;
            }
        }
        catch (ManagementException)
        {
            // sem a informação do gabinete: considera desktop
        }

        return false;
    }

    private static bool IsInstalled(params string[] nameParts)
    {
        foreach (string path in new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        })
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var uninstall = root.OpenSubKey(path);
            if (uninstall is null) continue;
            foreach (string sub in uninstall.GetSubKeyNames())
            {
                try
                {
                    using var key = uninstall.OpenSubKey(sub);
                    string? name = key?.GetValue("DisplayName") as string;
                    if (name is not null && nameParts.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase))) return true;
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { }
            }
        }

        return false;
    }

    private static bool? ReadNvidiaResizableBar()
    {
        foreach (string exe in new[] { "nvidia-smi.exe", @"C:\Program Files\NVIDIA Corporation\NVSMI\nvidia-smi.exe" })
        {
            try
            {
                var result = Shell.Run(exe, "-q", "-d", "MEMORY");
                if (result.ExitCode == 0) return HardwareAdvisor.ParseResizableBar(result.Output);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // programa ausente: tenta o próximo caminho
            }
        }

        return null;
    }

    private static int ReadRefreshRate()
    {
        try
        {
            foreach (var item in Query("SELECT CurrentRefreshRate FROM Win32_VideoController"))
            {
                long rate = Long(item, "CurrentRefreshRate");
                if (rate > 1) return (int)rate;
            }
        }
        catch (ManagementException)
        {
            // taxa desconhecida
        }

        return 60;
    }
}
