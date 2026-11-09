using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MBExtreme.Core.Progress;

namespace MBExtreme.Core.Memory;

/// <summary>Leitura e limpeza da memória RAM. Requer Windows; a limpeza completa requer administrador.</summary>
[SupportedOSPlatform("windows")]
public static class MemoryService
{
    private const int SystemMemoryListInformation = 80;
    private const int MemoryPurgeStandbyList = 4;
    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const uint SePrivilegeEnabled = 0x00000002;

    public static MemoryStatus GetStatus()
    {
        var info = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref info)
            ? new MemoryStatus((long)info.TotalPhys, (long)info.AvailPhys)
            : new MemoryStatus(0, 0);
    }

    /// <summary>
    /// Reduz o working set dos processos (exceto os da lista de permissões) e esvazia a lista de espera.
    /// Etapas publicadas: "Processos" e "Cache".
    /// </summary>
    public static CleanResult Clean(
        IReadOnlyCollection<string> allowlist,
        IProgress<ProgressEvent>? progress = null,
        CancellationToken ct = default)
    {
        long before = GetStatus().AvailableBytes;
        var skip = new HashSet<string>(allowlist, StringComparer.OrdinalIgnoreCase);
        int self = Environment.ProcessId;
        int trimmed = 0;

        var processes = Process.GetProcesses();
        try
        {
            for (int i = 0; i < processes.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                var process = processes[i];
                string name = SafeName(process);

                try
                {
                    if (process.Id > 4 && process.Id != self && !skip.Contains(name) && EmptyWorkingSet(process.Handle))
                        trimmed++;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                {
                    // Processo protegido ou já encerrado: ignorar.
                }

                progress?.Report(new ProgressEvent("Processos", 70.0 * (i + 1) / processes.Length, $"Liberando memória de {name}"));
            }
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }

        ct.ThrowIfCancellationRequested();
        progress?.Report(new ProgressEvent("Cache", 80, "Esvaziando a lista de espera da memória"));

        bool purged = false;
        string? error = null;
        try
        {
            EnablePrivilege("SeProfileSingleProcessPrivilege");
            int command = MemoryPurgeStandbyList;
            int status = NtSetSystemInformation(SystemMemoryListInformation, ref command, sizeof(int));
            purged = status == 0;
            if (!purged) error = $"O Windows recusou a operação (código 0x{status:X8}). Execute o programa como administrador.";
        }
        catch (Exception ex) when (ex is Win32Exception or DllNotFoundException or EntryPointNotFoundException)
        {
            error = ex.Message;
        }

        Thread.Sleep(300);
        long freed = Math.Max(0, GetStatus().AvailableBytes - before);
        progress?.Report(new ProgressEvent("Cache", 100, "Concluído"));
        return new CleanResult(freed, trimmed, purged, error);
    }

    private static string SafeName(Process process)
    {
        try { return process.ProcessName; }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { return "processo"; }
    }

    private static void EnablePrivilege(string name)
    {
        using var self = Process.GetCurrentProcess();
        if (!OpenProcessToken(self.Handle, TokenAdjustPrivileges | TokenQuery, out var token))
            throw new Win32Exception();
        try
        {
            if (!LookupPrivilegeValue(null, name, out long luid))
                throw new Win32Exception();
            var privileges = new TokenPrivileges { Count = 1, Luid = luid, Attributes = SePrivilegeEnabled };
            if (!AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception();
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TokenPrivileges
    {
        public uint Count;
        public long Luid;
        public uint Attributes;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr process);

    [DllImport("ntdll.dll")]
    private static extern int NtSetSystemInformation(int infoClass, ref int info, int length);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out long luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivileges state, uint length, IntPtr previous, IntPtr returnLength);
}
