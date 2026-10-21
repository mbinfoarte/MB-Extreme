using System.Management;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using MBExtreme.Core.Windows;

namespace MBExtreme.Core.Drivers;

public enum GpuVendor { Nvidia, Amd, Intel, Other }

public sealed record GpuDriverInfo(string Name, GpuVendor Vendor, string WindowsVersion, string DisplayVersion, DateTime? DriverDate);

public sealed record PendingDriver(string Id, string Title, string Manufacturer, string Model, string Date);

/// <summary>Drivers de vídeo instalados e atualizações de driver do Windows Update. Páginas oficiais dos fabricantes para baixar o mais recente.</summary>
[SupportedOSPlatform("windows")]
public static class DriverService
{
    private static readonly Regex GuidPattern = new(@"^[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}$");

    public static GpuVendor VendorFromPnpId(string pnpId) => pnpId.ToUpperInvariant() switch
    {
        var id when id.Contains("VEN_10DE") => GpuVendor.Nvidia,
        var id when id.Contains("VEN_1002") || id.Contains("VEN_1022") => GpuVendor.Amd,
        var id when id.Contains("VEN_8086") => GpuVendor.Intel,
        _ => GpuVendor.Other,
    };

    /// <summary>
    /// O Windows guarda a versão da NVIDIA como 32.0.15.6094; o número que a NVIDIA divulga (560.94)
    /// são o último dígito da terceira parte e a quarta parte juntos, com um ponto antes dos dois últimos dígitos.
    /// </summary>
    public static string NvidiaDisplayVersion(string windowsVersion)
    {
        var parts = windowsVersion.Split('.');
        if (parts.Length != 4 || parts[2].Length == 0 || parts[3].Length < 3) return windowsVersion;
        string digits = parts[2][^1] + parts[3];
        return digits.Length < 3 ? windowsVersion : $"{digits[..^2]}.{digits[^2..]}";
    }

    public static IReadOnlyList<GpuDriverInfo> GpuDrivers()
    {
        var list = new List<GpuDriverInfo>();
        using var searcher = new ManagementObjectSearcher("SELECT Name, DriverVersion, DriverDate, PNPDeviceID FROM Win32_VideoController");
        foreach (ManagementBaseObject item in searcher.Get())
        {
            string name = item["Name"]?.ToString() ?? "Placa de vídeo";
            string version = item["DriverVersion"]?.ToString() ?? "—";
            var vendor = VendorFromPnpId(item["PNPDeviceID"]?.ToString() ?? "");

            DateTime? date = null;
            if (item["DriverDate"] is string raw && raw.Length >= 8)
            {
                try { date = ManagementDateTimeConverter.ToDateTime(raw); }
                catch (ArgumentOutOfRangeException) { }
            }

            string display = vendor == GpuVendor.Nvidia ? NvidiaDisplayVersion(version) : version;
            list.Add(new GpuDriverInfo(name, vendor, version, display, date));
        }

        return list;
    }

    /// <summary>Entre as atualizações do Windows Update, só as de vídeo do fabricante informado.</summary>
    public static IReadOnlyList<PendingDriver> FilterGpuDrivers(IEnumerable<PendingDriver> drivers, GpuVendor vendor)
    {
        string[] makers = vendor switch
        {
            GpuVendor.Amd => new[] { "amd", "advanced micro", "radeon" },
            GpuVendor.Intel => new[] { "intel" },
            GpuVendor.Nvidia => new[] { "nvidia" },
            _ => Array.Empty<string>(),
        };
        string[] kinds = { "display", "graphics", "video", "radeon", "geforce", "arc", "uhd", "iris" };

        return drivers.Where(d =>
        {
            string text = (d.Title + " " + d.Manufacturer + " " + d.Model).ToLowerInvariant();
            return makers.Any(text.Contains) && kinds.Any(text.Contains);
        }).ToList();
    }

    public static string OfficialPage(GpuVendor vendor) => vendor switch
    {
        GpuVendor.Nvidia => "https://www.nvidia.com/Download/index.aspx?lang=pt-br",
        GpuVendor.Amd => "https://www.amd.com/pt/support/download/drivers.html",
        GpuVendor.Intel => "https://www.intel.com.br/content/www/br/pt/support/detect.html",
        _ => "https://www.microsoft.com/pt-br/windows/",
    };

    /// <summary>Atualizações de driver oferecidas pelo Windows Update e ainda não instaladas.</summary>
    public static IReadOnlyList<PendingDriver> SearchWindowsUpdateDrivers()
    {
        const string script =
            "$s = New-Object -ComObject Microsoft.Update.Session; " +
            "$r = $s.CreateUpdateSearcher().Search(\"IsInstalled=0 and Type='Driver'\"); " +
            "$items = @($r.Updates | ForEach-Object { [pscustomobject]@{ Id=$_.Identity.UpdateID; Title=$_.Title; Manufacturer=$_.DriverManufacturer; Model=$_.DriverModel; Date=([string]$_.DriverVerDate) } }); " +
            "if ($items.Count -eq 0) { '[]' } else { ConvertTo-Json -InputObject $items -Compress }";

        var result = Shell.PowerShellUtf8(script);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error)
                ? "Não foi possível consultar o Windows Update." : result.Error.Trim());
        return ParsePendingDrivers(result.Output);
    }

    public static IReadOnlyList<PendingDriver> ParsePendingDrivers(string json)
    {
        string text = json.Trim();
        if (text.Length == 0) return Array.Empty<PendingDriver>();

        using var document = JsonDocument.Parse(text);
        string Get(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        var elements = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray().ToList()
            : new List<JsonElement> { document.RootElement };
        return elements
            .Select(e => new PendingDriver(Get(e, "Id"), Get(e, "Title"), Get(e, "Manufacturer"), Get(e, "Model"), Get(e, "Date")))
            .Where(d => GuidPattern.IsMatch(d.Id))
            .ToList();
    }

    /// <summary>Baixa e instala as atualizações escolhidas. Retorna se precisa reiniciar.</summary>
    public static (bool Ok, bool RebootRequired, string Message) InstallWindowsUpdateDrivers(IReadOnlyCollection<string> updateIds)
    {
        if (updateIds.Count == 0) return (true, false, "Nada para instalar.");
        if (updateIds.Any(id => !GuidPattern.IsMatch(id)))
            throw new ArgumentException("Identificador de atualização inválido.", nameof(updateIds));

        string ids = string.Join(",", updateIds.Select(id => $"'{id}'"));
        string script =
            $"$ids = @({ids}); " +
            "$s = New-Object -ComObject Microsoft.Update.Session; " +
            "$r = $s.CreateUpdateSearcher().Search(\"IsInstalled=0 and Type='Driver'\"); " +
            "$c = New-Object -ComObject Microsoft.Update.UpdateColl; " +
            "foreach ($u in $r.Updates) { if ($ids -contains $u.Identity.UpdateID) { if (-not $u.EulaAccepted) { $u.AcceptEula() }; [void]$c.Add($u) } }; " +
            "if ($c.Count -eq 0) { 'NENHUM'; exit 0 }; " +
            "$d = $s.CreateUpdateDownloader(); $d.Updates = $c; [void]$d.Download(); " +
            "$i = $s.CreateUpdateInstaller(); $i.Updates = $c; $x = $i.Install(); " +
            "'OK;' + $x.ResultCode + ';' + $x.RebootRequired";

        var result = Shell.PowerShellUtf8(script);
        string output = result.Output.Trim();
        if (result.ExitCode != 0)
            return (false, false, string.IsNullOrWhiteSpace(result.Error) ? "O Windows Update recusou a instalação." : result.Error.Trim());
        if (output.StartsWith("NENHUM")) return (false, false, "A atualização não está mais disponível.");

        var parts = output.Split(';');
        bool ok = parts.Length >= 3 && (parts[1] == "2" || parts[1] == "3");
        bool reboot = parts.Length >= 3 && parts[2].Equals("True", StringComparison.OrdinalIgnoreCase);
        return (ok, reboot, ok ? "Instalado." : "O Windows não conseguiu instalar (código " + (parts.Length > 1 ? parts[1] : "?") + ").");
    }
}
