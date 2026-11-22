using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using MBExtreme.Core.Windows;

namespace MBExtreme.Core.Storage;

public sealed record VolumeInfo(string Name, string Label, string Format, long TotalBytes, long FreeBytes)
{
    public double UsedPercent => TotalBytes > 0 ? 100.0 * (TotalBytes - FreeBytes) / TotalBytes : 0;
}

public sealed record DiskInfo(
    string Name, string Media, string Bus, string Health, long SizeBytes,
    double? Temperature, double? WearPercent, long? PowerOnHours, long? ReadErrors, long? WriteErrors,
    string Serial = "", string Firmware = "", long? Reallocated = null, long? Pending = null, long? Uncorrectable = null, long? PowerCycles = null);

/// <summary>Volumes, saúde dos discos (SSD, HD) e otimização (TRIM em SSD, desfragmentação em HD) com as ferramentas do próprio Windows.</summary>
[SupportedOSPlatform("windows")]
public static class StorageService
{
    private static readonly Regex DriveName = new(@"^[A-Za-z]:\\?$");

    public static IReadOnlyList<VolumeInfo> Volumes() =>
        DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => new VolumeInfo(d.Name, d.VolumeLabel, d.DriveFormat, d.TotalSize, d.AvailableFreeSpace))
            .ToList();

    public static IReadOnlyList<DiskInfo> PhysicalDisks()
    {
        const string script =
            "Get-PhysicalDisk | ForEach-Object { $c = $_ | Get-StorageReliabilityCounter -ErrorAction SilentlyContinue; " +
            "[pscustomobject]@{ Name=$_.FriendlyName; Media=[string]$_.MediaType; Bus=[string]$_.BusType; Health=[string]$_.HealthStatus; " +
            "Size=[int64]$_.Size; Temp=$c.Temperature; Wear=$c.Wear; Hours=$c.PowerOnHours; ReadErrors=$c.ReadErrorsTotal; WriteErrors=$c.WriteErrorsTotal; " +
            "Serial=[string]$_.SerialNumber; Firmware=[string]$_.FirmwareVersion; Cycles=$c.StartStopCycleCount } } | " +
            "ConvertTo-Json -Compress";

        var result = Shell.PowerShellUtf8(script);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error) ? "Não foi possível ler os discos." : result.Error.Trim());

        return ParseDisks(result.Output);
    }

    public static IReadOnlyList<DiskInfo> ParseDisks(string json)
    {
        string text = json.Trim();
        if (text.Length == 0) return Array.Empty<DiskInfo>();

        using var document = JsonDocument.Parse(text);
        var elements = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray().ToList()
            : new List<JsonElement> { document.RootElement };

        string Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        long? Int(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n) ? n : null;

        return elements.Select(e => new DiskInfo(
            Str(e, "Name"), Str(e, "Media"), Str(e, "Bus"), Str(e, "Health"), Int(e, "Size") ?? 0,
            Int(e, "Temp"), Int(e, "Wear"), Int(e, "Hours"), Int(e, "ReadErrors"), Int(e, "WriteErrors"),
            Str(e, "Serial").Trim(), Str(e, "Firmware").Trim(), PowerCycles: Int(e, "Cycles"))).ToList();
    }

    /// <summary>
    /// SMART cru dos discos SATA (o que o CrystalDiskInfo mostra): setores realocados (5), pendentes (197) e incorrigíveis (198),
    /// lidos do WMI do Windows. NVMe não entrega esse bloco; nele valem o desgaste e os erros do contador de confiabilidade.
    /// </summary>
    public static IReadOnlyList<DiskInfo> WithSmart(IReadOnlyList<DiskInfo> disks)
    {
        const string script =
            "Get-CimInstance -Namespace root\\wmi -ClassName MSStorageDriver_FailurePredictData -ErrorAction SilentlyContinue | " +
            "ForEach-Object { [pscustomobject]@{ Name=$_.InstanceName; Data=[Convert]::ToBase64String([byte[]]$_.VendorSpecific) } } | ConvertTo-Json -Compress";
        var result = Shell.PowerShellUtf8(script);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output)) return disks;
        var entries = new List<(string Instance, IReadOnlyDictionary<int, long> Attributes)>();
        try
        {
            using var document = JsonDocument.Parse(result.Output.Trim());
            var items = document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.EnumerateArray().ToList() : new List<JsonElement> { document.RootElement };
            foreach (var e in items)
            {
                string name = e.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
                string data = e.TryGetProperty("Data", out var d) ? d.GetString() ?? "" : "";
                if (data.Length > 0) entries.Add((name, DiskHealth.ParseSmart(Convert.FromBase64String(data))));
            }
        }
        catch (Exception ex) when (ex is JsonException or FormatException) { return disks; }
        return disks.Select(disk =>
        {
            var match = entries.FirstOrDefault(x => DiskHealth.SameDisk(x.Instance, disk.Name));
            if (match.Attributes is null && entries.Count == 1 && disks.Count(d => !d.Bus.Equals("NVMe", StringComparison.OrdinalIgnoreCase)) == 1 &&
                !disk.Bus.Equals("NVMe", StringComparison.OrdinalIgnoreCase)) match = entries[0];
            if (match.Attributes is not { } a) return disk;
            return disk with
            {
                Reallocated = a.TryGetValue(5, out long r) ? r : null,
                Pending = a.TryGetValue(197, out long p) ? p : null,
                Uncorrectable = a.TryGetValue(198, out long u) ? u : null,
                PowerOnHours = disk.PowerOnHours ?? (a.TryGetValue(9, out long h) ? h : null),
            };
        }).ToList();
    }

    public static bool IsValidDrive(string name) => DriveName.IsMatch(name);

    /// <summary>Verificação de erros sem desligar o PC (chkdsk /scan). Ok = nada para corrigir.</summary>
    public static (bool Ok, string Message) ScanErrors(string drive)
    {
        if (!IsValidDrive(drive)) throw new ArgumentException("Unidade inválida.", nameof(drive));
        var result = Shell.RunOem("chkdsk.exe", drive.TrimEnd('\\'), "/scan");
        string text = (result.Output + " " + result.Error).Trim();
        return (result.ExitCode == 0, text.Length > 400 ? text[^400..] : text);
    }

    /// <summary>"defrag /O": o Windows escolhe a otimização certa (TRIM em SSD, desfragmentar em HD).</summary>
    public static (bool Ok, string Message) Optimize(string drive)
    {
        if (!IsValidDrive(drive)) throw new ArgumentException("Unidade inválida.", nameof(drive));
        var result = Shell.Run("defrag.exe", drive.TrimEnd('\\'), "/O");
        string text = (result.Output + " " + result.Error).Trim();
        return (result.ExitCode == 0, text.Length > 300 ? text[^300..] : text);
    }
}
