using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;

namespace MBExtreme.Core.Info;

public sealed record InfoRow(string Label, string Value);

public sealed record InfoSection(string Title, string Icon, IReadOnlyList<InfoRow> Rows);

/// <summary>Informações de hardware e sistema (estilo CPU-Z, GPU-Z e AIDA), lidas por WMI.</summary>
[SupportedOSPlatform("windows")]
public static class SystemInfoService
{
    private const double Gb = 1024.0 * 1024 * 1024;

    public static IReadOnlyList<InfoSection> Collect() => new[]
    {
        SafeSection("Processador", "", Cpu),
        SafeSection("Placa-mãe e BIOS", "", Board),
        SafeSection("Memória", "", Memory),
        SafeSection("Placa de vídeo", "", Gpu),
        SafeSection("Armazenamento", "", Storage),
        SafeSection("Rede", "", Network),
        SafeSection("Sistema", "", Os),
    };

    private static InfoSection SafeSection(string title, string icon, Func<List<InfoRow>> build)
    {
        try
        {
            return new InfoSection(title, icon, build());
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new InfoSection(title, icon, new[] { new InfoRow("Erro", "Não foi possível ler: " + ex.Message) });
        }
    }

    public static string ToText(IEnumerable<InfoSection> sections)
    {
        var text = new StringBuilder();
        text.AppendLine($"MB Extreme (Beta 01) - relatório do sistema ({DateTime.Now:dd/MM/yyyy HH:mm})");
        foreach (var section in sections)
        {
            text.AppendLine().AppendLine("== " + section.Title);
            foreach (var row in section.Rows) text.AppendLine($"{row.Label}: {row.Value}");
        }

        return text.ToString();
    }

    private static IEnumerable<ManagementBaseObject> Query(string wql, string scope = @"root\cimv2")
    {
        using var searcher = new ManagementObjectSearcher(scope, wql);
        foreach (ManagementBaseObject item in searcher.Get()) yield return item;
    }

    private static string Text(ManagementBaseObject item, string property)
    {
        object? value = item[property];
        string? text = value?.ToString()?.Trim();
        return string.IsNullOrEmpty(text) ? "—" : text;
    }

    private static double? Number(ManagementBaseObject item, string property) =>
        item[property] is null ? null : Convert.ToDouble(item[property]);

    private static string GbText(double bytes) => $"{bytes / Gb:0.#} GB";

    private static string Date(ManagementBaseObject item, string property)
    {
        if (item[property] is not string raw || raw.Length < 8) return "—";
        try { return ManagementDateTimeConverter.ToDateTime(raw).ToString("dd/MM/yyyy"); }
        catch (ArgumentOutOfRangeException) { return "—"; }
    }

    private static List<InfoRow> Cpu()
    {
        var rows = new List<InfoRow>();
        foreach (var cpu in Query("SELECT * FROM Win32_Processor"))
        {
            rows.Add(new("Nome", Text(cpu, "Name")));
            rows.Add(new("Fabricante", Text(cpu, "Manufacturer")));
            rows.Add(new("Núcleos / threads", $"{Text(cpu, "NumberOfCores")} / {Text(cpu, "NumberOfLogicalProcessors")}"));
            if (Number(cpu, "MaxClockSpeed") is double max) rows.Add(new("Clock máximo", $"{max / 1000:0.00} GHz"));
            if (Number(cpu, "L2CacheSize") is double l2 and > 0) rows.Add(new("Cache L2", $"{l2 / 1024:0.#} MB"));
            if (Number(cpu, "L3CacheSize") is double l3 and > 0) rows.Add(new("Cache L3", $"{l3 / 1024:0.#} MB"));
            rows.Add(new("Soquete", Text(cpu, "SocketDesignation")));
            rows.Add(new("Arquitetura", ArchitectureName(Convert.ToInt32(cpu["Architecture"] ?? -1))));
        }

        return rows;
    }

    private static string ArchitectureName(int code) => code switch
    {
        0 => "x86", 5 => "ARM", 9 => "x64", 12 => "ARM64", _ => "desconhecida",
    };

    private static List<InfoRow> Board()
    {
        var rows = new List<InfoRow>();
        foreach (var board in Query("SELECT * FROM Win32_BaseBoard"))
        {
            rows.Add(new("Fabricante", Text(board, "Manufacturer")));
            rows.Add(new("Modelo", Text(board, "Product")));
        }

        foreach (var bios in Query("SELECT * FROM Win32_BIOS"))
        {
            rows.Add(new("BIOS (fabricante)", Text(bios, "Manufacturer")));
            rows.Add(new("BIOS (versão)", Text(bios, "SMBIOSBIOSVersion")));
            rows.Add(new("BIOS (data)", Date(bios, "ReleaseDate")));
        }

        return rows;
    }

    private static string MemoryType(int code) => code switch
    {
        20 => "DDR", 21 => "DDR2", 24 => "DDR3", 26 => "DDR4", 34 => "DDR5", 35 => "LPDDR5", 29 => "LPDDR3", 30 => "LPDDR4",
        _ => "RAM",
    };

    private static List<InfoRow> Memory()
    {
        var rows = new List<InfoRow>();
        double total = 0;
        foreach (var module in Query("SELECT * FROM Win32_PhysicalMemory"))
        {
            double bytes = Number(module, "Capacity") ?? 0;
            total += bytes;
            int type = Convert.ToInt32(module["SMBIOSMemoryType"] ?? 0);
            double speed = Number(module, "ConfiguredClockSpeed") ?? Number(module, "Speed") ?? 0;
            string slot = Text(module, "DeviceLocator");
            string maker = Text(module, "Manufacturer");
            string part = Text(module, "PartNumber");
            rows.Add(new($"Módulo {slot}", $"{GbText(bytes)} {MemoryType(type)} · {speed:0} MHz · {maker} {part}"));
        }

        rows.Insert(0, new InfoRow("Total instalado", total > 0 ? GbText(total) : "—"));
        return rows;
    }

    private static List<InfoRow> Gpu()
    {
        var rows = new List<InfoRow>();
        foreach (var gpu in Query("SELECT * FROM Win32_VideoController"))
        {
            string name = Text(gpu, "Name");
            rows.Add(new("Placa", name));
            rows.Add(new("Fabricante", Text(gpu, "AdapterCompatibility")));
            rows.Add(new("Versão do driver", Text(gpu, "DriverVersion")));
            rows.Add(new("Data do driver", Date(gpu, "DriverDate")));

            double? vram = VideoMemoryFromRegistry(name) ?? Number(gpu, "AdapterRAM");
            if (vram is > 0) rows.Add(new("Memória de vídeo", GbText(vram.Value)));

            if (Number(gpu, "CurrentHorizontalResolution") is double w && Number(gpu, "CurrentVerticalResolution") is double h)
                rows.Add(new("Resolução atual", $"{w:0}x{h:0} @ {Text(gpu, "CurrentRefreshRate")} Hz"));
        }

        return rows;
    }

    /// <summary>O WMI limita AdapterRAM a 4 GB; o registro guarda o valor real.</summary>
    internal static double? VideoMemoryFromRegistry(string adapterName)
    {
        const string classPath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var cls = root.OpenSubKey(classPath);
        if (cls is null) return null;

        foreach (var sub in cls.GetSubKeyNames())
        {
            using var key = cls.OpenSubKey(sub);
            if (key?.GetValue("DriverDesc") as string != adapterName) continue;
            object? value = key.GetValue("HardwareInformation.qwMemorySize");
            if (value is long l) return l;
            if (value is byte[] bytes && bytes.Length >= 8) return BitConverter.ToInt64(bytes, 0);
        }

        return null;
    }

    private static List<InfoRow> Storage()
    {
        var rows = new List<InfoRow>();
        try
        {
            foreach (var disk in Query("SELECT * FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage"))
            {
                string type = Convert.ToInt32(disk["MediaType"] ?? 0) switch { 3 => "HD", 4 => "SSD", 5 => "SCM", _ => "disco" };
                string bus = Convert.ToInt32(disk["BusType"] ?? 0) switch { 17 => "NVMe", 11 => "SATA", 7 => "USB", 8 => "RAID", _ => "" };
                string health = Convert.ToInt32(disk["HealthStatus"] ?? 0) switch { 0 => "saudável", 1 => "atenção", 2 => "ruim", _ => "" };
                string detail = string.Join(" · ", new[] { type, bus, GbText(Number(disk, "Size") ?? 0), health }.Where(p => p.Length > 0));
                rows.Add(new(Text(disk, "FriendlyName"), detail));
            }
        }
        catch (ManagementException)
        {
            // Espaço de nomes de armazenamento indisponível: usar a lista simples.
        }

        if (rows.Count == 0)
            foreach (var disk in Query("SELECT * FROM Win32_DiskDrive"))
                rows.Add(new(Text(disk, "Model"), $"{Text(disk, "InterfaceType")} · {GbText(Number(disk, "Size") ?? 0)}"));

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
            rows.Add(new($"Unidade {drive.Name}", $"{GbText(drive.TotalSize - drive.AvailableFreeSpace)} usados de {GbText(drive.TotalSize)} · {drive.DriveFormat}"));
        return rows;
    }

    private static List<InfoRow> Network()
    {
        var rows = new List<InfoRow>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(a =>
                     a.OperationalStatus == OperationalStatus.Up
                     && a.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)))
        {
            string ip = adapter.GetIPProperties().UnicastAddresses
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.Address.ToString()).FirstOrDefault() ?? "—";
            rows.Add(new(adapter.Name, $"{ip} · {adapter.Speed / 1_000_000} Mb/s · {adapter.NetworkInterfaceType}"));
        }

        return rows;
    }

    private static List<InfoRow> Os()
    {
        var rows = new List<InfoRow>();
        foreach (var os in Query("SELECT * FROM Win32_OperatingSystem"))
        {
            rows.Add(new("Windows", Text(os, "Caption")));
            rows.Add(new("Versão", $"{Text(os, "Version")} (build {Text(os, "BuildNumber")})"));
            rows.Add(new("Arquitetura", Text(os, "OSArchitecture")));
            rows.Add(new("Instalado em", Date(os, "InstallDate")));
            if (os["LastBootUpTime"] is string raw && raw.Length >= 8)
            {
                var up = DateTime.Now - ManagementDateTimeConverter.ToDateTime(raw);
                rows.Add(new("Ligado há", $"{(int)up.TotalDays} d {up.Hours} h {up.Minutes} min"));
            }
        }

        rows.Add(new("Nome do computador", Environment.MachineName));
        return rows;
    }
}
