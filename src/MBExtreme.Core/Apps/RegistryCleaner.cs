using System.Runtime.Versioning;
using Microsoft.Win32;
using MBExtreme.Core.Windows;

namespace MBExtreme.Core.Apps;

public enum RegistryIssueKind { GhostProgram, BrokenStartup, AppPath, FileType, EmptyKey, SharedDll }

/// <summary>Um item do registro comprovadamente quebrado. Sem <paramref name="ValueName"/>, apaga a chave inteira.</summary>
public sealed record RegistryIssue(RegistryIssueKind Kind, string KeyPath, string? ValueName, string Title, string Why, bool Recommended);

/// <summary>
/// Limpeza de registro segura: só acha o que aponta para algo que não existe mais (programas fantasma, inicialização quebrada,
/// App Paths, "Abrir com", chaves vazias de programas removidos e DLLs compartilhadas apagadas). Antes de apagar, salva uma
/// cópia (.reg) de tudo; a Central Desfazer importa a cópia de volta.
/// </summary>
public static class RegistryCleaner
{
    public static string Title(RegistryIssueKind kind) => kind switch
    {
        RegistryIssueKind.GhostProgram => "Programas fantasma",
        RegistryIssueKind.BrokenStartup => "Inicialização quebrada",
        RegistryIssueKind.AppPath => "Atalhos de programas",
        RegistryIssueKind.FileType => "Tipos de arquivo",
        RegistryIssueKind.EmptyKey => "Restos de desinstalação",
        _ => "DLLs compartilhadas",
    };

    public static string Hint(RegistryIssueKind kind) => kind switch
    {
        RegistryIssueKind.GhostProgram => "Aparecem nos instalados, mas o desinstalador sumiu",
        RegistryIssueKind.BrokenStartup => "Abrem com o Windows um arquivo que não existe",
        RegistryIssueKind.AppPath => "App Paths que apontam para um .exe apagado",
        RegistryIssueKind.FileType => "\"Abrir com\" um programa que não existe mais",
        RegistryIssueKind.EmptyKey => "Chaves vazias de programas já removidos",
        _ => "Registros de arquivos que não existem",
    };

    private static readonly string[] Launchers = { "msiexec.exe", "rundll32.exe", "cmd.exe", "powershell.exe", "wscript.exe", "cscript.exe", "explorer.exe", "regsvr32.exe" };
    private static readonly string[] DriverWords = { "driver", "realtek", "intel", "nvidia", "amd ", "radeon", "chipset", "synaptics", "elan", "qualcomm", "broadcom", "mediatek" };

    /// <summary>
    /// O caminho do .exe de um comando aponta para um arquivo que sumiu? Só conta caminho completo (C:\...) e fora dos
    /// lançadores do Windows (msiexec, rundll32...), que acham o arquivo sozinhos.
    /// </summary>
    public static bool PointsToMissing(string? command, Func<string, bool> exists)
    {
        if (CommandLine.ExePath(command) is not { } exe) return false;
        if (exe.Length < 3 || exe[1] != ':' || exe[2] != '\\') return false;
        if (Launchers.Any(l => CommandLine.FileName(exe).Equals(l, StringComparison.OrdinalIgnoreCase))) return false;
        return !exists(exe);
    }

    public static bool LooksLikeDriver(string name, string publisher) =>
        DriverWords.Any(w => (name + " " + publisher + " ").Contains(w, StringComparison.OrdinalIgnoreCase));

    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<RegistryIssue> Scan(CancellationToken ct = default)
    {
        var list = new List<RegistryIssue>();
        Func<string, bool> exists = File.Exists;

        // 1. programas fantasma
        foreach (var (hive, view, prefix) in new[]
                 {
                     (RegistryHive.LocalMachine, RegistryView.Registry64, @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\"),
                     (RegistryHive.LocalMachine, RegistryView.Registry32, @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\"),
                     (RegistryHive.CurrentUser, RegistryView.Default, @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\"),
                 })
        {
            ct.ThrowIfCancellationRequested();
            Each(hive, view, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", (sub, key) =>
            {
                if (key.GetValue("DisplayName") is not string name || string.IsNullOrWhiteSpace(name)) return;
                if (key.GetValue("SystemComponent") is int sc && sc == 1) return;
                string? uninstall = key.GetValue("UninstallString") as string;
                string? location = (key.GetValue("InstallLocation") as string)?.Trim().Trim('"');
                bool locationGone = string.IsNullOrWhiteSpace(location) || !Directory.Exists(Environment.ExpandEnvironmentVariables(location));
                if (!PointsToMissing(uninstall, exists) || !locationGone) return;
                string publisher = key.GetValue("Publisher") as string ?? "";
                bool driver = LooksLikeDriver(name, publisher);
                list.Add(new RegistryIssue(RegistryIssueKind.GhostProgram, prefix + sub, null, name.Trim(),
                    driver ? "driver: confira antes" : "desinstalador não existe mais", !driver));
            });
        }

        // 2. inicialização apontando para arquivo apagado
        foreach (var (hive, view, path, label) in new[]
                 {
                     (RegistryHive.CurrentUser, RegistryView.Default, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
                     (RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
                     (RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"),
                 })
        {
            using var key = OpenKey(hive, view, path);
            if (key is null) continue;
            foreach (string value in SafeValueNames(key))
                if (key.GetValue(value) is string command && PointsToMissing(command, exists))
                    list.Add(new RegistryIssue(RegistryIssueKind.BrokenStartup, label, value, value.Length == 0 ? "(sem nome)" : value, "arquivo não existe: " + CommandLine.ExePath(command), true));
        }

        // 3. App Paths
        foreach (var (hive, view, label) in new[]
                 {
                     (RegistryHive.LocalMachine, RegistryView.Registry64, @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\"),
                     (RegistryHive.CurrentUser, RegistryView.Default, @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\"),
                 })
            Each(hive, view, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths", (sub, key) =>
            {
                if (key.GetValue("") is string target && PointsToMissing("\"" + Environment.ExpandEnvironmentVariables(target.Trim('"')) + "\"", exists))
                    list.Add(new RegistryIssue(RegistryIssueKind.AppPath, label + sub, null, sub, "aponta para um .exe apagado", true));
            });

        // 4. "Abrir com" de programas que não existem mais
        Each(RegistryHive.CurrentUser, RegistryView.Default, @"SOFTWARE\Classes\Applications", (sub, key) =>
        {
            using var command = key.OpenSubKey(@"shell\open\command");
            if (command?.GetValue("") is string cmd && PointsToMissing(cmd, exists))
                list.Add(new RegistryIssue(RegistryIssueKind.FileType, @"HKCU\SOFTWARE\Classes\Applications\" + sub, null, sub, "programa não existe mais", true));
        });

        // 5. chaves vazias em HKCU\Software (fabricante\programa sem nenhum valor nem subchave)
        using (var software = OpenKey(RegistryHive.CurrentUser, RegistryView.Default, "SOFTWARE"))
        {
            foreach (string vendor in software is null ? Array.Empty<string>() : SafeSubKeys(software))
            {
                if (!LeftoverFinder.Usable(vendor)) continue;
                using var v = Sub(software!, vendor);
                if (v is null) continue;
                if (IsEmpty(v)) { list.Add(new RegistryIssue(RegistryIssueKind.EmptyKey, @"HKCU\SOFTWARE\" + vendor, null, vendor, "chave vazia", true)); continue; }
                foreach (string app in SafeSubKeys(v))
                {
                    using var a = Sub(v, app);
                    if (a is not null && IsEmpty(a) && LeftoverFinder.Usable(app))
                        list.Add(new RegistryIssue(RegistryIssueKind.EmptyKey, $@"HKCU\SOFTWARE\{vendor}\{app}", null, $"{vendor} › {app}", "chave vazia", true));
                }
            }
        }

        // 6. DLLs compartilhadas que não existem
        using (var shared = OpenKey(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs"))
            if (shared is not null)
                foreach (string file in SafeValueNames(shared))
                    if (file.Length > 3 && file[1] == ':' && !File.Exists(file))
                        list.Add(new RegistryIssue(RegistryIssueKind.SharedDll, @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs", file, Path.GetFileName(file), "arquivo não existe: " + file, true));

        return list;
    }

    /// <summary>Salva a cópia de cada chave numa pasta e apaga. Devolve (apagados, falhas, pasta da cópia).</summary>
    [SupportedOSPlatform("windows")]
    public static (int Done, int Failed, string BackupFolder) Clean(IReadOnlyList<RegistryIssue> issues, string label)
    {
        var backup = RegistryBackup.NewFolder(label);
        int done = 0, failed = 0;
        foreach (var group in issues.GroupBy(i => i.KeyPath, StringComparer.OrdinalIgnoreCase))
        {
            if (!LeftoverGuard.CanDeleteKey(group.Key)) { failed += group.Count(); continue; }
            if (!RegistryBackup.Export(group.Key, backup)) { failed += group.Count(); continue; }
            foreach (var issue in group)
            {
                bool ok = issue.ValueName is null ? RegistryBackup.DeleteKey(issue.KeyPath) : RegistryBackup.DeleteValue(issue.KeyPath, issue.ValueName);
                if (ok) done++; else failed++;
            }
        }
        return (done, failed, backup);
    }

    // ------------------------------------------------------------------ leitura segura

    [SupportedOSPlatform("windows")]
    private static RegistryKey? OpenKey(RegistryHive hive, RegistryView view, string path)
    {
        try { return RegistryKey.OpenBaseKey(hive, view).OpenSubKey(path); }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return null; }
    }

    [SupportedOSPlatform("windows")]
    private static RegistryKey? Sub(RegistryKey key, string name)
    {
        try { return key.OpenSubKey(name); }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return null; }
    }

    [SupportedOSPlatform("windows")]
    private static void Each(RegistryHive hive, RegistryView view, string path, Action<string, RegistryKey> visit)
    {
        using var root = OpenKey(hive, view, path);
        if (root is null) return;
        foreach (string sub in SafeSubKeys(root))
        {
            using var key = Sub(root, sub);
            if (key is null) continue;
            try { visit(sub, key); }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        }
    }

    [SupportedOSPlatform("windows")]
    private static string[] SafeSubKeys(RegistryKey key)
    {
        try { return key.GetSubKeyNames(); }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return Array.Empty<string>(); }
    }

    [SupportedOSPlatform("windows")]
    private static string[] SafeValueNames(RegistryKey key)
    {
        try { return key.GetValueNames(); }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return Array.Empty<string>(); }
    }

    [SupportedOSPlatform("windows")]
    private static bool IsEmpty(RegistryKey key)
    {
        try { return key.SubKeyCount == 0 && key.ValueCount == 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}

/// <summary>Cópias do registro antes de apagar (uma pasta por limpeza, um .reg por chave) e a volta pela Central Desfazer.</summary>
public static class RegistryBackup
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "registro");

    public static string NewFolder(string label)
    {
        string safe = new string(label.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-').ToArray()).Trim();
        string folder = Path.Combine(Root, DateTime.Now.ToString("yyyyMMdd-HHmmss") + (safe.Length > 0 ? "-" + safe[..Math.Min(40, safe.Length)] : ""));
        Directory.CreateDirectory(folder);
        return folder;
    }

    public static bool Export(string keyPath, string folder)
    {
        string file = Path.Combine(folder, Directory.GetFiles(folder, "*.reg").Length.ToString("D3") + ".reg");
        return Shell.Run("reg.exe", "export", keyPath, file, "/y").ExitCode == 0;
    }

    public static bool DeleteKey(string keyPath) => Shell.Run("reg.exe", "delete", keyPath, "/f").ExitCode == 0;

    public static bool DeleteValue(string keyPath, string value) =>
        (value.Length == 0 ? Shell.Run("reg.exe", "delete", keyPath, "/ve", "/f") : Shell.Run("reg.exe", "delete", keyPath, "/v", value, "/f")).ExitCode == 0;

    /// <summary>Importa de volta todas as cópias da pasta. Devolve quantas voltaram e quantas falharam.</summary>
    public static (int Ok, int Failed) Restore(string folder)
    {
        int ok = 0, failed = 0;
        if (!Directory.Exists(folder)) return (0, 0);
        foreach (string file in Directory.GetFiles(folder, "*.reg").OrderBy(f => f, StringComparer.Ordinal))
            if (Shell.Run("reg.exe", "import", file).ExitCode == 0) ok++; else failed++;
        return (ok, failed);
    }
}
