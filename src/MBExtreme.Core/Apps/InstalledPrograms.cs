using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Win32;
using MBExtreme.Core.Windows;

namespace MBExtreme.Core.Apps;

public enum ProgramSource { Win32, Store }

/// <summary>Um programa instalado, como o Windows registra (Aplicativos instalados).</summary>
public sealed record InstalledProgram(
    string Name, string Version, string Publisher, ProgramSource Source, DateTime? InstalledOn, long? SizeBytes,
    string? InstallLocation, string? IconPath, string? UninstallCommand, string? QuietUninstallCommand, string? StoreFullName);

/// <summary>Inventário dos programas: os de instalador comum (registro "Uninstall") e os da Microsoft Store.</summary>
public static class InstalledPrograms
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>"20261106" → 06/11/2026; datas quebradas viram nulo.</summary>
    public static DateTime? ParseInstallDate(string? value) =>
        value is { Length: 8 } && DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>"\"C:\\App\\app.exe\",0" → C:\App\app.exe (o caminho do ícone sem aspas nem índice).</summary>
    public static string? IconFile(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon)) return null;
        string text = displayIcon.Trim();
        int comma = text.LastIndexOf(',');
        if (comma > 0 && int.TryParse(text[(comma + 1)..].Trim(), out _)) text = text[..comma];
        text = text.Trim().Trim('"');
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// Separa o programa dos argumentos de um comando de desinstalação: "\"C:\\x\\unins000.exe\" /SILENT" ou "MsiExec.exe /X{GUID}".
    /// </summary>
    public static (string File, string Arguments)? SplitCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        string text = command.Trim();
        if (text.StartsWith('"'))
        {
            int close = text.IndexOf('"', 1);
            if (close < 0) return null;
            return (text[1..close], text[(close + 1)..].Trim());
        }
        int exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe < 0) return (text, "");
        return (text[..(exe + 4)], text[(exe + 4)..].Trim());
    }

    /// <summary>Entra na lista? Componentes do sistema, atualizações e itens sem nome ficam de fora (como no Windows).</summary>
    public static bool ShouldList(string? name, int systemComponent, string? parentKey, string? releaseType) =>
        !string.IsNullOrWhiteSpace(name) && systemComponent != 1 && string.IsNullOrEmpty(parentKey)
        && releaseType is not ("Update" or "Hotfix" or "Security Update" or "Service Pack");

    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<InstalledProgram> List()
    {
        var list = new List<InstalledProgram>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hive, view) in new[] { (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32), (RegistryHive.CurrentUser, RegistryView.Default) })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(UninstallKey);
                if (root is null) continue;
                foreach (string sub in root.GetSubKeyNames())
                {
                    using var key = root.OpenSubKey(sub);
                    if (key is null) continue;
                    string? name = key.GetValue("DisplayName") as string;
                    if (!ShouldList(name, key.GetValue("SystemComponent") is int sc ? sc : 0, key.GetValue("ParentKeyName") as string, key.GetValue("ReleaseType") as string)) continue;
                    string version = key.GetValue("DisplayVersion") as string ?? "";
                    if (!seen.Add(name + "|" + version)) continue;   // o mesmo programa nas visões 32 e 64 bits
                    long? size = key.GetValue("EstimatedSize") is int kb && kb > 0 ? kb * 1024L : null;
                    list.Add(new InstalledProgram(name!.Trim(), version, key.GetValue("Publisher") as string ?? "", ProgramSource.Win32,
                        ParseInstallDate(key.GetValue("InstallDate") as string), size,
                        (key.GetValue("InstallLocation") as string)?.Trim().Trim('"') is { Length: > 0 } location ? location : null,
                        IconFile(key.GetValue("DisplayIcon") as string),
                        key.GetValue("UninstallString") as string, key.GetValue("QuietUninstallString") as string, null));
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        }

        var store = Shell.PowerShellUtf8(
            "Get-AppxPackage -PackageTypeFilter Main | Where-Object { $_.SignatureKind -eq 'Store' -and -not $_.IsFramework } | " +
            "Select-Object Name, Version, PublisherId, PackageFullName, InstallLocation | ConvertTo-Json -Compress");
        if (store.ExitCode == 0) list.AddRange(ParseStore(store.Output));
        return list.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Lê a saída do Get-AppxPackage em JSON (um objeto ou uma lista).</summary>
    public static IReadOnlyList<InstalledProgram> ParseStore(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<InstalledProgram>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : new List<JsonElement> { doc.RootElement };
            string Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            return items.Where(e => e.ValueKind == JsonValueKind.Object && Text(e, "Name").Length > 0)
                .Select(e => new InstalledProgram(FriendlyStoreName(Text(e, "Name")), Text(e, "Version"), "Microsoft Store", ProgramSource.Store, null, null,
                    Text(e, "InstallLocation") is { Length: > 0 } l ? l : null, null, null, null, Text(e, "PackageFullName")))
                .ToList();
        }
        catch (JsonException) { return Array.Empty<InstalledProgram>(); }
    }

    /// <summary>"Microsoft.WindowsCalculator" → "Windows Calculator" (o nome técnico do pacote, mais legível).</summary>
    public static string FriendlyStoreName(string packageName)
    {
        string name = packageName;
        int dot = name.IndexOf('.');
        if (dot > 0 && dot < name.Length - 1) name = name[(dot + 1)..];
        name = name.Replace('.', ' ');
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && char.IsLower(name[i - 1])) sb.Append(' ');
            sb.Append(name[i]);
        }
        return sb.ToString();
    }

    /// <summary>Processos abertos que rodam de dentro da pasta do programa (para avisar antes de atualizar ou desinstalar).</summary>
    public static IReadOnlyList<string> RunningFrom(string? installLocation, IEnumerable<(string Name, string? Path)> processes)
    {
        if (string.IsNullOrWhiteSpace(installLocation)) return Array.Empty<string>();
        string folder = installLocation.TrimEnd('\\', '/') + "\\";
        return processes.Where(p => p.Path is { } path && path.Replace('/', '\\').StartsWith(folder.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>O programa do inventário que corresponde a uma atualização do winget (pelo nome).</summary>
    public static InstalledProgram? Match(IReadOnlyList<InstalledProgram> programs, string wingetName) =>
        programs.FirstOrDefault(p => p.Source == ProgramSource.Win32 && p.Name.Equals(wingetName, StringComparison.OrdinalIgnoreCase))
        ?? programs.FirstOrDefault(p => p.Source == ProgramSource.Win32 && (p.Name.StartsWith(wingetName, StringComparison.OrdinalIgnoreCase) || wingetName.StartsWith(p.Name, StringComparison.OrdinalIgnoreCase)));
}
