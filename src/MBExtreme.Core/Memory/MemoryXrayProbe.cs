using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using MBExtreme.Core.Apps;
using MBExtreme.Core.Operations;
using MBExtreme.Core.Startup;
using MBExtreme.Core.Tools;
using MBExtreme.Core.Windows;
using Microsoft.Win32;

namespace MBExtreme.Core.Memory;

/// <summary>O que o "Liberar" fez: quantos itens de cada tipo, a memória antes e depois e a pasta da cópia (para desfazer).</summary>
public sealed record XrayResult(int Closed, int ServicesStopped, int AppsBlocked, int StartupOff, int Failed, long UsedBefore, long UsedAfter, string? BackupFolder)
{
    public long Freed => Math.Max(0, UsedBefore - UsedAfter);
}

/// <summary>Leitura do Windows para o Raio-X da memória e execução do "Liberar" (fecha, para serviço, bloqueia segundo plano, tira da inicialização).</summary>
[SupportedOSPlatform("windows")]
public static class MemoryXrayProbe
{
    private const string BackgroundKey = @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications";
    private const string ServicesKey = @"SYSTEM\CurrentControlSet\Services";

    /// <summary>Lê processos, serviços, apps da Store e inicialização e monta o Raio-X.</summary>
    public static XrayReport Scan(IReadOnlyCollection<string> allowlist)
    {
        var startup = new List<string>();
        try { startup = StartupService.List().Where(e => e.Enabled).Select(e => XrayRules.StartupExe(e.Command)).OfType<string>().ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        return XrayRules.Build(Processes(), Services(), StoreApps(), startup, allowlist, ForegroundProcess.Name(), Environment.ProcessId);
    }

    /// <summary>
    /// Para a Limpeza, o 1 clique e o botão "Liberar RAM": fecha os ajudantes de fundo e para os serviços seguros que estão rodando,
    /// sem mudar nada que fique para depois (nada de Manual, bloqueio ou inicialização).
    /// </summary>
    public static XrayResult QuickFree(IReadOnlyCollection<string> allowlist, CancellationToken token = default) =>
        Free(XrayRules.QuickPick(Scan(allowlist)), persistent: false, null, token);

    public static IReadOnlyList<XrayProcess> Processes()
    {
        var list = new List<XrayProcess>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.Id is 0 or 4) continue;
                    long priv = 0;
                    try { priv = p.PrivateMemorySize64; } catch (Exception) { try { priv = p.WorkingSet64; } catch (Exception) { } }
                    bool window = false; string? title = null; DateTime? start = null;
                    try { window = p.MainWindowHandle != IntPtr.Zero; title = string.IsNullOrWhiteSpace(p.MainWindowTitle) ? null : p.MainWindowTitle; } catch (Exception) { }
                    try { start = p.StartTime; } catch (Exception) { }
                    list.Add(new XrayProcess(p.Id, p.ProcessName, priv, window, ImagePath(p.Id), title, start));
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
            }
        }
        return list;
    }

    public static IReadOnlyList<XrayService> Services()
    {
        var list = new List<XrayService>();
        IntPtr scm = OpenSCManager(null, null, 0x0004 /* ENUMERATE_SERVICE */);
        if (scm == IntPtr.Zero) return list;
        try
        {
            uint resume = 0;
            EnumServicesStatusEx(scm, 0, 0x30 /* WIN32 */, 0x1 /* ACTIVE */, IntPtr.Zero, 0, out uint needed, out _, ref resume, null);
            if (needed == 0) return list;
            IntPtr buffer = Marshal.AllocHGlobal((int)needed);
            try
            {
                resume = 0;
                if (!EnumServicesStatusEx(scm, 0, 0x30, 0x1, buffer, needed, out _, out uint count, ref resume, null)) return list;
                int size = Marshal.SizeOf<EnumServiceStatusProcess>();
                for (int i = 0; i < count; i++)
                {
                    var e = Marshal.PtrToStructure<EnumServiceStatusProcess>(buffer + i * size);
                    string name = Marshal.PtrToStringUni(e.ServiceName) ?? "";
                    string display = Marshal.PtrToStringUni(e.DisplayName) ?? name;
                    list.Add(new XrayService(name, display, (int)e.ProcessId, StartType(name)));
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { CloseServiceHandle(scm); }
        return list;
    }

    public static IReadOnlyList<XrayStoreApp> StoreApps()
    {
        var list = new List<XrayStoreApp>();
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(BackgroundKey);
            if (root is null) return list;
            foreach (string family in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(family);
                bool disabled = key?.GetValue("Disabled") is int d && d == 1 || key?.GetValue("DisabledByUser") is int u && u == 1;
                list.Add(new XrayStoreApp(family, disabled));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        return list;
    }

    private static int StartType(string service)
    {
        try { using var key = Registry.LocalMachine.OpenSubKey(ServicesKey + "\\" + service); return key?.GetValue("Start") is int s ? s : -1; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return -1; }
    }

    /// <summary>
    /// Libera os itens escolhidos. <paramref name="persistent"/> = false (Limpeza e 1 clique): só fecha e para, sem mudar nada que
    /// fique para depois. Com true, o que muda o Windows ganha cópia antes e um registro na Central Desfazer.
    /// </summary>
    public static XrayResult Free(IReadOnlyList<XrayItem> items, bool persistent, IProgress<string>? progress = null, CancellationToken token = default)
    {
        long before = MemoryService.GetStatus().UsedBytes;
        int closed = 0, stopped = 0, blocked = 0, startupOff = 0, failed = 0;
        string? backup = null;
        string Backup() => backup ??= RegistryBackup.NewFolder("raio-x da memoria");
        List<StartupEntry>? startupEntries = null;
        bool startupSaved = false;

        foreach (var item in items)
        {
            token.ThrowIfCancellationRequested();
            if (item.Protected) continue;
            progress?.Report(item.Name);
            try
            {
                switch (item.Action)
                {
                    case XrayAction.StopService when item.ServiceName is { } svc:
                        if (persistent && StartType(svc) == 2)
                        {
                            RegistryBackup.Export(@"HKLM\" + ServicesKey + "\\" + svc, Backup());
                            using var key = Registry.LocalMachine.OpenSubKey(ServicesKey + "\\" + svc, writable: true);
                            key?.SetValue("Start", 3, RegistryValueKind.DWord);
                        }
                        if (Shell.Run("sc.exe", "stop", svc).ExitCode is 0 or 1062 /* já parado */) stopped++; else failed++;
                        break;
                    case XrayAction.BlockBackground when item.PackageFamily is { } family:
                        if (persistent)
                        {
                            SaveBackgroundState(family, Backup());
                            using var key = Registry.CurrentUser.CreateSubKey(BackgroundKey + "\\" + family, true);
                            key.SetValue("Disabled", 1, RegistryValueKind.DWord);
                            key.SetValue("DisabledByUser", 1, RegistryValueKind.DWord);
                            blocked++;
                        }
                        closed += Kill(item, ref failed);
                        break;
                    case XrayAction.CloseAndStartup:
                        if (persistent && item.StartupName is { } exe)
                        {
                            startupEntries ??= StartupService.List().ToList();
                            foreach (var entry in startupEntries.Where(e => e.Enabled && string.Equals(XrayRules.StartupExe(e.Command), exe, StringComparison.OrdinalIgnoreCase)))
                            {
                                if (!startupSaved) { SaveStartupApproved(Backup()); startupSaved = true; }
                                StartupService.SetEnabled(entry, false);
                                startupOff++;
                            }
                        }
                        closed += Kill(item, ref failed);
                        break;
                    case XrayAction.Close:
                        closed += Kill(item, ref failed);
                        break;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
            {
                failed++;
            }
        }

        if (backup is not null && (stopped > 0 || blocked > 0 || startupOff > 0))
            OperationJournal.Shared.Record(new OperationInfo("memory.xray", OperationKind.Registry,
                    $"Raio-X da memória: {stopped} serviço(s) em Manual, {blocked} app(s) sem segundo plano, {startupOff} fora da inicialização", TargetId: backup, Reversible: true),
                OperationResult.Ok("Cópia guardada antes de mudar."), DateTimeOffset.Now);

        Thread.Sleep(1200);   // o Windows leva um instante para devolver a memória
        long after = MemoryService.GetStatus().UsedBytes;
        return new XrayResult(closed, stopped, blocked, startupOff, failed, before, after, backup);
    }

    private static int Kill(XrayItem item, ref int failed)
    {
        int ok = 0;
        var procs = item.Processes.Count > 0 ? item.Processes : Processes().Where(p => p.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var p in procs)
        {
            if (OptimizeAdvisor.ProcessProtected(p.Name)) continue;
            var (done, _) = ProcessList.Kill(p.Pid, p.Name, p.StartTime);
            if (done) ok++; else failed++;
        }
        return ok;
    }

    /// <summary>Guarda como estava a permissão de segundo plano. Se a chave não existe, guarda "liberado" (o padrão do Windows).</summary>
    private static void SaveBackgroundState(string family, string folder)
    {
        string path = @"HKEY_CURRENT_USER\" + BackgroundKey + "\\" + family;
        using var existing = Registry.CurrentUser.OpenSubKey(BackgroundKey + "\\" + family);
        if (existing is not null && RegistryBackup.Export(path, folder)) return;
        string file = Path.Combine(folder, Directory.GetFiles(folder, "*.reg").Length.ToString("D3") + ".reg");
        File.WriteAllText(file, "Windows Registry Editor Version 5.00\r\n\r\n[" + path + "]\r\n\"Disabled\"=dword:00000000\r\n\"DisabledByUser\"=dword:00000000\r\n",
            Encoding.Unicode);
    }

    private static void SaveStartupApproved(string folder)
    {
        const string approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";
        foreach (string hive in new[] { "HKCU", "HKLM" })
            foreach (string sub in new[] { "Run", "Run32", "StartupFolder" })
            {
                var root = hive == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;   // chaves raiz: não descartar
                bool exists;
                using (var key = root.OpenSubKey(approved + "\\" + sub)) exists = key is not null;
                if (exists) RegistryBackup.Export($@"{hive}\{approved}\{sub}", folder);
            }
    }

    // ---------------------------------------------------------------- Windows

    private static string? ImagePath(int pid)
    {
        IntPtr h = OpenProcess(0x1000 /* QUERY_LIMITED_INFORMATION */, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EnumServiceStatusProcess
    {
        public IntPtr ServiceName;
        public IntPtr DisplayName;
        public uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint, ProcessId, ServiceFlags;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManager(string? machine, string? database, uint access);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumServicesStatusEx(IntPtr scm, int infoLevel, uint serviceType, uint serviceState, IntPtr services, uint bufSize,
        out uint bytesNeeded, out uint servicesReturned, ref uint resumeHandle, string? groupName);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
