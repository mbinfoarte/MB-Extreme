using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MBExtreme.Core.Apps;

public enum LeftoverType { Folder, RegistryKey, Shortcut }

/// <summary>Um resto de programa desinstalado. <paramref name="Sure"/>: com certeza era dele (apaga sozinho); senão a pessoa confere.</summary>
public sealed record Leftover(LeftoverType Type, string Path, bool Sure, string Why, long Bytes, string Program)
{
    public string TypeText => Type switch { LeftoverType.Folder => "Pasta", LeftoverType.RegistryKey => "Registro", _ => "Atalho" };
}

/// <summary>
/// Procura o que um programa deixou depois de desinstalado: a pasta dele, dados em AppData e ProgramData, atalhos, chaves do registro
/// com o nome dele e a entrada órfã em "Uninstall". O que pode ser de outro programa do mesmo fabricante vem como "confira antes".
/// </summary>
public static class LeftoverFinder
{
    /// <summary>Nomes que nunca são tratados como de um programa só (pastas e chaves usadas por muita coisa).</summary>
    private static readonly HashSet<string> Shared = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft", "Windows", "Common Files", "Microsoft Shared", "WindowsApps", "Windows NT", "Windows Defender", "Internet Explorer", "dotnet",
        "Package Cache", "Packages", "Temp", "Programs", "Intel", "NVIDIA", "NVIDIA Corporation", "AMD", "ATI", "Realtek", "Classes", "Policies",
        "Wow6432Node", "Google", "Mozilla", "Apple", "Adobe", "Oracle", "Java", "Steam", "Valve", "Epic Games", "Python", "MBExtreme", "MB Extreme",
        "Software", "Users", "Public", "Default", "Desktop", "Documents", "Downloads", "Start Menu", "Startup", "Accessories", "Administrative Tools",
        "System Tools", "Maintenance", "Uninstall", "Update", "Updater", "Launcher", "Tools", "Bin", "Data", "Cache", "Logs", "Settings", "Config",
    };

    private static readonly Regex Version = new(@"\s+v?\d+(\.\d+)+.*$|\s+\d{4}$", RegexOptions.Compiled);
    private static readonly Regex Brackets = new(@"\s*[\(\[][^\)\]]*[\)\]]", RegexOptions.Compiled);
    private static readonly Regex Arch = new(@"\s+(x64|x86|64-bit|32-bit|64 bits|32 bits|amd64)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Company = new(@"[,\s]+(inc\.?|llc|ltd\.?|limited|gmbh|s\.?a\.?|corp\.?|corporation|co\.?|company|software|technologies|ltda\.?)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>"Discord 1.0.9 (x64)" → "Discord"; "7-Zip 24.08 (x64 edition)" → "7-Zip".</summary>
    public static string CleanName(string name)
    {
        string n = Brackets.Replace(name ?? "", "");
        n = Arch.Replace(n, "");
        n = Version.Replace(n, "");
        return n.Trim().TrimEnd('-', '–', ',', '.').Trim();
    }

    /// <summary>"Discord Inc." → "Discord"; "Bytedance Ltd." → "Bytedance".</summary>
    public static string CleanPublisher(string publisher)
    {
        string p = (publisher ?? "").Trim();
        for (int i = 0; i < 3; i++) p = Company.Replace(p, "").Trim();
        return p.TrimEnd(',', '.').Trim();
    }

    /// <summary>Nome serve para procurar restos? Curto demais ou compartilhado (Microsoft, Common Files...) não.</summary>
    public static bool Usable(string name) => name.Length >= 3 && !Shared.Contains(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    /// <summary>Procura os restos de um programa que acabou de ser removido.</summary>
    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<Leftover> Find(InstalledProgram program, string? installFolder)
    {
        var list = new List<Leftover>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string name = CleanName(program.Name), publisher = CleanPublisher(program.Publisher);
        bool nameOk = Usable(name), publisherOk = Usable(publisher) && !publisher.Equals(name, StringComparison.OrdinalIgnoreCase)
            && !publisher.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase);

        void Folder(string path, bool sure, string why)
        {
            path = path.TrimEnd('\\');
            if (!Directory.Exists(path) || !LeftoverGuard.CanDeleteFolder(path) || !seen.Add(path)) return;
            // pasta do fabricante que ainda tem outro programa dele instalado: nunca vem marcada
            list.Add(new Leftover(LeftoverType.Folder, path, sure, why, ProgramSizes.Measure(path), program.Name));
        }

        void Key(string path, bool sure, string why)
        {
            if (!LeftoverGuard.CanDeleteKey(path) || !KeyExists(path) || !seen.Add(path)) return;
            list.Add(new Leftover(LeftoverType.RegistryKey, path, sure, why, 0, program.Name));
        }

        if (installFolder is not null) Folder(installFolder, true, "pasta da instalação");

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var bases = new[] { (local, "cache e dados do programa"), (roaming, "configurações do programa"), (common, "dados de todos os usuários"),
            (Path.Combine(local, "Programs"), "pasta da instalação"), (pf, "pasta da instalação"), (pf86, "pasta da instalação") };
        foreach (var (root, why) in bases)
        {
            if (string.IsNullOrEmpty(root)) continue;
            if (nameOk) Folder(Path.Combine(root, name), true, why);
            if (nameOk && publisherOk) Folder(Path.Combine(root, publisher, name), true, why);
            if (publisherOk && Directory.Exists(Path.Combine(root, publisher)) && IsEmptyOrSmall(Path.Combine(root, publisher)))
                Folder(Path.Combine(root, publisher), false, "pasta do fabricante (pode ter outro programa dele)");
        }

        // atalhos do Menu Iniciar e da Área de Trabalho com o nome do programa
        if (nameOk)
        {
            foreach (string menu in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Programs), Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms) })
            {
                if (string.IsNullOrEmpty(menu)) continue;
                Folder(Path.Combine(menu, name), true, "pasta no Menu Iniciar");
                if (publisherOk) Folder(Path.Combine(menu, publisher), false, "pasta do fabricante no Menu Iniciar");
                foreach (string lnk in SafeFiles(menu, name + "*.lnk"))
                    if (seen.Add(lnk)) list.Add(new Leftover(LeftoverType.Shortcut, lnk, true, "atalho no Menu Iniciar", 0, program.Name));
            }
            foreach (string desk in new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) })
                foreach (string lnk in SafeFiles(desk, name + "*.lnk"))
                    if (seen.Add(lnk)) list.Add(new Leftover(LeftoverType.Shortcut, lnk, true, "atalho na Área de Trabalho", 0, program.Name));
        }

        // registro
        if (nameOk)
        {
            foreach (string hive in new[] { "HKCU", "HKLM" })
            {
                Key($@"{hive}\SOFTWARE\{name}", true, "configurações dele");
                if (publisherOk) Key($@"{hive}\SOFTWARE\{publisher}\{name}", true, "configurações dele");
            }
            Key($@"HKLM\SOFTWARE\WOW6432Node\{name}", true, "configurações dele (32 bits)");
            if (publisherOk) Key($@"HKLM\SOFTWARE\WOW6432Node\{publisher}\{name}", true, "configurações dele (32 bits)");
        }
        if (publisherOk)
            foreach (string hive in new[] { "HKCU", "HKLM" })
                if (SubKeyCount($@"{hive}\SOFTWARE\{publisher}") is <= 1)
                    Key($@"{hive}\SOFTWARE\{publisher}", false, "chave do fabricante (pode ter outro programa dele)");
        if (InstalledPrograms.RegistryPath(program) is { } entry) Key(entry, true, "entrada órfã na lista de programas");

        return list;
    }

    private static bool IsEmptyOrSmall(string folder)
    {
        try { return Directory.EnumerateDirectories(folder).Take(3).Count() <= 1; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static IEnumerable<string> SafeFiles(string folder, string pattern)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return Array.Empty<string>();
        try { return Directory.EnumerateFiles(folder, pattern, new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 2 }).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    // ------------------------------------------------------------------ registro

    [SupportedOSPlatform("windows")]
    internal static RegistryKey? Open(string path)
    {
        var (hive, view, sub) = Split(path);
        if (sub is null) return null;
        try { return RegistryKey.OpenBaseKey(hive, view).OpenSubKey(sub); }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return null; }
    }

    [SupportedOSPlatform("windows")]
    private static bool KeyExists(string path) { using var key = Open(path); return key is not null; }

    [SupportedOSPlatform("windows")]
    private static int? SubKeyCount(string path)
    {
        using var key = Open(path);
        try { return key?.SubKeyCount; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>"HKLM\SOFTWARE\x" → (LocalMachine, 64 bits, "SOFTWARE\x"). WOW6432Node fica no caminho (visão de 64 bits).</summary>
    internal static (RegistryHive Hive, RegistryView View, string? Sub) Split(string path)
    {
        int slash = path.IndexOf('\\');
        if (slash < 0) return (RegistryHive.CurrentUser, RegistryView.Default, null);
        string root = path[..slash].ToUpperInvariant(), sub = path[(slash + 1)..];
        return root switch
        {
            "HKCU" or "HKEY_CURRENT_USER" => (RegistryHive.CurrentUser, RegistryView.Default, sub),
            "HKLM" or "HKEY_LOCAL_MACHINE" => (RegistryHive.LocalMachine, RegistryView.Registry64, sub),
            _ => (RegistryHive.CurrentUser, RegistryView.Default, null),
        };
    }
}

/// <summary>Trava de segurança: o que o MB nunca apaga como "resto", mesmo que o nome combine.</summary>
public static class LeftoverGuard
{
    private static readonly string[] ProtectedKeys =
    {
        @"SOFTWARE", @"SOFTWARE\Microsoft", @"SOFTWARE\Classes", @"SOFTWARE\Policies", @"SOFTWARE\WOW6432Node", @"SOFTWARE\WOW6432Node\Microsoft",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", @"SYSTEM",
    };

    /// <summary>A pasta está DENTRO (nunca a própria) de Program Files, ProgramData, AppData ou do Menu Iniciar, e fora do Windows?</summary>
    public static bool CanDeleteFolder(string path, IReadOnlyList<string>? bases = null)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || Regex.IsMatch(path, @"~\d")) return false;
        string full;
        try { full = Path.GetFullPath(path).TrimEnd('\\', '/'); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (windows.Length > 0 && (full.Equals(windows, StringComparison.OrdinalIgnoreCase) || full.StartsWith(windows + "\\", StringComparison.OrdinalIgnoreCase))) return false;
        string leaf = Path.GetFileName(full);
        if (leaf.Length < 3 || !LeftoverFinder.Usable(leaf)) return false;
        foreach (string b in bases ?? DefaultBases())
        {
            string root = b.TrimEnd('\\', '/');
            if (root.Length == 0) continue;
            if (full.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase) && full.Length > root.Length + 1) return true;
        }
        return false;
    }

    public static IReadOnlyList<string> DefaultBases() => new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
    }.Where(p => p.Length > 0).ToList();

    /// <summary>Chave que pode ser apagada: dentro de HKCU/HKLM\SOFTWARE, nunca a raiz de Microsoft, Classes, Policies nem a lista Uninstall em si.</summary>
    public static bool CanDeleteKey(string path)
    {
        int slash = path.IndexOf('\\');
        if (slash < 0) return false;
        string root = path[..slash].ToUpperInvariant(), sub = path[(slash + 1)..].TrimEnd('\\');
        if (root is not ("HKCU" or "HKLM")) return false;
        if (!sub.StartsWith(@"SOFTWARE\", StringComparison.OrdinalIgnoreCase)) return false;
        if (ProtectedKeys.Any(k => sub.Equals(k, StringComparison.OrdinalIgnoreCase))) return false;
        string[] parts = sub.Split('\\');
        if (parts.Length >= 2 && parts[1].Equals("Microsoft", StringComparison.OrdinalIgnoreCase)
            && !sub.Contains(@"\CurrentVersion\Uninstall\", StringComparison.OrdinalIgnoreCase)
            && !sub.Contains(@"\CurrentVersion\App Paths\", StringComparison.OrdinalIgnoreCase)
            && !sub.Contains(@"\CurrentVersion\Run", StringComparison.OrdinalIgnoreCase)
            && !sub.Contains(@"\CurrentVersion\SharedDLLs", StringComparison.OrdinalIgnoreCase)) return false;
        if (parts.Length >= 3 && parts[1].Equals("WOW6432Node", StringComparison.OrdinalIgnoreCase) && parts[2].Equals("Microsoft", StringComparison.OrdinalIgnoreCase)
            && !sub.Contains(@"\CurrentVersion\Uninstall\", StringComparison.OrdinalIgnoreCase)
            && !sub.Contains(@"\CurrentVersion\Run", StringComparison.OrdinalIgnoreCase)) return false;
        return parts.Length >= 2;
    }
}
