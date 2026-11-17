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
    double? Temperature, double? WearPercent, long? PowerOnHours, long? ReadErrors, long? WriteErrors);

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
            "Size=[int64]$_.Size; Temp=$c.Temperature; Wear=$c.Wear; Hours=$c.PowerOnHours; ReadErrors=$c.ReadErrorsTotal; WriteErrors=$c.WriteErrorsTotal } } | " +
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
            Int(e, "Temp"), Int(e, "Wear"), Int(e, "Hours"), Int(e, "ReadErrors"), Int(e, "WriteErrors"))).ToList();
    }

    public static bool IsValidDrive(string name) => DriveName.IsMatch(name);

    /// <summary>"defrag /O": o Windows escolhe a otimização certa (TRIM em SSD, desfragmentar em HD).</summary>
    public static (bool Ok, string Message) Optimize(string drive)
    {
        if (!IsValidDrive(drive)) throw new ArgumentException("Unidade inválida.", nameof(drive));
        var result = Shell.Run("defrag.exe", drive.TrimEnd('\\'), "/O");
        string text = (result.Output + " " + result.Error).Trim();
        return (result.ExitCode == 0, text.Length > 300 ? text[^300..] : text);
    }
}
