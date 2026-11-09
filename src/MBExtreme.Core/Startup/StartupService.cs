using System.Runtime.Versioning;
using Microsoft.Win32;

namespace MBExtreme.Core.Startup;

public enum StartupSource { RunCurrentUser, RunMachine, RunMachine32, FolderUser, FolderAllUsers }

public sealed record StartupEntry(string Name, string Command, StartupSource Source, bool Enabled)
{
    public string SourceText => Source switch
    {
        StartupSource.RunCurrentUser => "Registro (usuário)",
        StartupSource.RunMachine => "Registro (todos os usuários)",
        StartupSource.RunMachine32 => "Registro (32 bits)",
        StartupSource.FolderUser => "Pasta Inicializar (usuário)",
        _ => "Pasta Inicializar (todos)",
    };
}

/// <summary>
/// Programas que abrem junto com o Windows. Ligar e desligar usa o mesmo mecanismo do Gerenciador de Tarefas
/// (chave StartupApproved): nada é apagado e dá para reativar a qualquer momento.
/// </summary>
[SupportedOSPlatform("windows")]
public static class StartupService
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Run32Path = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRun = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ApprovedRun32 = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";
    private const string ApprovedFolder = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    /// <summary>Primeiro byte par (02, 06) = ativo; ímpar (03, 07) = desativado. Sem valor = ativo.</summary>
    public static bool IsEnabledBytes(byte[]? data) => data is null || data.Length == 0 || (data[0] & 1) == 0;

    public static byte[] EncodeState(bool enabled)
    {
        var bytes = new byte[12];
        bytes[0] = enabled ? (byte)0x02 : (byte)0x03;
        if (!enabled) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(bytes, 4);
        return bytes;
    }

    private static string ApprovedPath(StartupSource source) => source switch
    {
        StartupSource.RunCurrentUser or StartupSource.RunMachine => ApprovedRun,
        StartupSource.RunMachine32 => ApprovedRun32,
        _ => ApprovedFolder,
    };

    private static RegistryHive ApprovedHive(StartupSource source) =>
        source is StartupSource.RunMachine or StartupSource.RunMachine32 or StartupSource.FolderAllUsers
            ? RegistryHive.LocalMachine
            : RegistryHive.CurrentUser;

    public static IReadOnlyList<StartupEntry> List()
    {
        var list = new List<StartupEntry>();
        AddRunKey(list, RegistryHive.CurrentUser, RunPath, StartupSource.RunCurrentUser);
        AddRunKey(list, RegistryHive.LocalMachine, RunPath, StartupSource.RunMachine);
        AddRunKey(list, RegistryHive.LocalMachine, Run32Path.Replace("Software\\", "SOFTWARE\\"), StartupSource.RunMachine32);
        AddFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupSource.FolderUser);
        AddFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StartupSource.FolderAllUsers);
        return list.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static bool ReadApproved(StartupSource source, string name)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(ApprovedHive(source), RegistryView.Registry64);
            using var key = root.OpenSubKey(ApprovedPath(source));
            return IsEnabledBytes(key?.GetValue(name) as byte[]);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { return true; }
    }

    private static void AddRunKey(List<StartupEntry> list, RegistryHive hive, string path, StartupSource source)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = root.OpenSubKey(path);
            if (key is null) return;
            foreach (string name in key.GetValueNames())
            {
                string command = key.GetValue(name)?.ToString() ?? "";
                if (name.Length == 0) continue;
                list.Add(new StartupEntry(name, command, source, ReadApproved(source, name)));
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { /* sem permissão para ler esta chave: fica de fora da lista */ }
    }

    private static void AddFolder(List<StartupEntry> list, string folder, StartupSource source)
    {
        if (!Directory.Exists(folder)) return;
        string[] files;
        try { files = Directory.GetFiles(folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        foreach (string file in files)
        {
            string name = Path.GetFileName(file);
            if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(new StartupEntry(name, file, source, ReadApproved(source, name)));
        }
    }

    public static void SetEnabled(StartupEntry entry, bool enabled)
    {
        using var root = RegistryKey.OpenBaseKey(ApprovedHive(entry.Source), RegistryView.Registry64);
        using var key = root.CreateSubKey(ApprovedPath(entry.Source), true)
            ?? throw new InvalidOperationException("Não foi possível abrir a chave de inicialização.");
        key.SetValue(entry.Name, EncodeState(enabled), RegistryValueKind.Binary);
    }
}
