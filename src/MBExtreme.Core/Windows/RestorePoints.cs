using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MBExtreme.Core.Windows;

/// <summary>Um ponto de restauração do Windows.</summary>
public sealed record RestorePoint(int Sequence, string Description, DateTime Created, int Type)
{
    public string TypeText => RestorePoints.TypeName(Type, Description);
}

/// <summary>Espaço que a Proteção do Sistema usa no disco do Windows.</summary>
public sealed record ShadowStorage(long Used, long Max);

/// <summary>
/// Pontos de restauração: lista, cria (mesmo se o Windows já criou um nas últimas 24 horas), apaga e volta o Windows para um deles.
/// Os arquivos pessoais não são mexidos por um ponto de restauração; programas e drivers instalados depois somem.
/// </summary>
public static class RestorePoints
{
    public static string TypeName(int type, string description) => type switch
    {
        0 => "Instalação de programa",
        1 => "Remoção de programa",
        10 => "Instalação de driver",
        12 => description.Contains("Agendado", StringComparison.OrdinalIgnoreCase) || description.Contains("Scheduled", StringComparison.OrdinalIgnoreCase)
            ? "Automático do Windows" : "Mudança de configurações",
        13 => "Cancelado",
        _ => "Ponto de restauração",
    };

    /// <summary>Data no formato do WMI (20261106141100.000000-180).</summary>
    public static DateTime ParseWmiDate(string value)
    {
        if (value.Length >= 14 && DateTime.TryParseExact(value[..14], "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var utc))
            return utc.ToLocalTime();
        return DateTime.MinValue;
    }

    public static IReadOnlyList<RestorePoint> List()
    {
        var list = new List<RestorePoint>();
        using var searcher = new ManagementObjectSearcher(@"root\default", "SELECT SequenceNumber, Description, CreationTime, RestorePointType FROM SystemRestore");
        foreach (ManagementObject o in searcher.Get())
            list.Add(new RestorePoint(Convert.ToInt32(o["SequenceNumber"], CultureInfo.InvariantCulture), o["Description"] as string ?? "",
                ParseWmiDate(o["CreationTime"] as string ?? ""), Convert.ToInt32(o["RestorePointType"] ?? 12, CultureInfo.InvariantCulture)));
        return list.OrderByDescending(p => p.Created).ToList();
    }

    private const string SrKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore";

    /// <summary>Cria agora. O Windows ignora um segundo ponto em 24 horas; o limite é zerado só durante a criação e volta como estava.</summary>
    public static (bool Ok, string? Error) CreateNow(string description)
    {
        object? previous = null;
        bool changed = false;
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(SrKey, writable: true);
            previous = key.GetValue("SystemRestorePointCreationFrequency");
            key.SetValue("SystemRestorePointCreationFrequency", 0, RegistryValueKind.DWord);
            changed = true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        try
        {
            return RestorePointService.TryCreate(description.Length == 0 ? "Ponto criado no MB Extreme" : description);
        }
        finally
        {
            if (changed)
                try
                {
                    using var key = Registry.LocalMachine.CreateSubKey(SrKey, writable: true);
                    if (previous is null) key.DeleteValue("SystemRestorePointCreationFrequency", false);
                    else key.SetValue("SystemRestorePointCreationFrequency", previous, RegistryValueKind.DWord);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        }
    }

    [DllImport("srclient.dll")]
    private static extern int SRRemoveRestorePoint(int sequence);

    public static bool Delete(int sequence)
    {
        try { return SRRemoveRestorePoint(sequence) == 0; }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    /// <summary>Marca a volta para o ponto; o Windows aplica ao reiniciar.</summary>
    public static (bool Ok, string Message) Restore(int sequence)
    {
        try
        {
            using var sr = new ManagementClass(@"root\default", "SystemRestore", null);
            var result = sr.InvokeMethod("Restore", new object[] { (uint)sequence });
            int code = Convert.ToInt32(result ?? -1, CultureInfo.InvariantCulture);
            return code == 0 ? (true, "Pronto. Reinicie o computador para o Windows voltar para o ponto.")
                : (false, $"O Windows recusou a volta (código {code}). Tente pelo assistente do Windows (rstrui).");
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException)
        {
            return (false, "Não foi possível pedir a volta: " + ex.Message);
        }
    }

    /// <summary>Proteção ligada no disco do Windows? (o Windows guarda "RPSessionInterval" = 1 quando ligada.)</summary>
    public static bool? ProtectionOn()
    {
        using var key = Registry.LocalMachine.OpenSubKey(SrKey);
        if (key?.GetValue("RPSessionInterval") is int interval) return interval != 0;
        return null;
    }

    /// <summary>Espaço usado e máximo, lido do "vssadmin list shadowstorage" (o do disco do Windows; o primeiro se não achar).</summary>
    public static ShadowStorage? ParseShadowStorage(string output, string systemDrive)
    {
        var blocks = Regex.Split(output, @"\r?\n\s*\r?\n").Where(b => b.Contains(':')).ToList();
        var block = blocks.FirstOrDefault(b => b.Contains($"({systemDrive.TrimEnd('\\')})", StringComparison.OrdinalIgnoreCase)) ?? blocks.FirstOrDefault(b => Sizes(b).Count >= 3);
        if (block is null) return null;
        var sizes = Sizes(block);
        // ordem: usado, alocado, máximo (em qualquer idioma)
        return sizes.Count >= 3 ? new ShadowStorage(sizes[0], sizes[2]) : null;
    }

    private static List<long> Sizes(string block)
    {
        var list = new List<long>();
        foreach (Match m in Regex.Matches(block, @"([\d.,]+)\s*(B|KB|MB|GB|TB)\b", RegexOptions.IgnoreCase))
        {
            string number = m.Groups[1].Value;
            // "12,3" (vírgula decimal) ou "12.3"
            number = number.Contains(',') && !number.Contains('.') ? number.Replace(',', '.') : number.Replace(",", "");
            if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) continue;
            double mult = m.Groups[2].Value.ToUpperInvariant() switch { "KB" => 1024, "MB" => 1024d * 1024, "GB" => 1024d * 1024 * 1024, "TB" => 1024d * 1024 * 1024 * 1024, _ => 1 };
            list.Add((long)(v * mult));
        }
        return list;
    }

    public static ShadowStorage? ReadShadowStorage()
    {
        var (_, output, _) = Shell.RunOem("vssadmin.exe", "list", "shadowstorage");
        return ParseShadowStorage(output, Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
    }
}
