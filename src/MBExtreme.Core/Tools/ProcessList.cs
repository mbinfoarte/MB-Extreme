using System.Diagnostics;
using System.Runtime.Versioning;

namespace MBExtreme.Core.Tools;

public sealed record ProcessEntry(int Id, string Name, double CpuPercent, long WorkingSetBytes, string? Title, DateTime? StartTime = null);

/// <summary>Lista de processos com uso de CPU entre duas leituras e memória, e encerramento com proteção dos processos do Windows.</summary>
[SupportedOSPlatform("windows")]
public sealed class ProcessList
{
    private static readonly HashSet<string> Critical = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "fontdrvhost", "dwm", "memory compression", "secure system",
        "svchost", "msmpeng", "securityhealthservice", "audiodg", "spoolsv", "lsm", "sihost", "ctfmon", "taskhostw", "explorer",
    };

    private readonly Dictionary<int, (TimeSpan Cpu, DateTime At)> _previous = new();

    public static bool IsProtectedName(string name) => Critical.Contains(name);

    public static double CpuPercent(TimeSpan cpuDelta, TimeSpan wallDelta, int cores) =>
        wallDelta.TotalMilliseconds <= 0 || cores <= 0 ? 0 : Math.Clamp(100.0 * cpuDelta.TotalMilliseconds / (wallDelta.TotalMilliseconds * cores), 0, 100);

    public IReadOnlyList<ProcessEntry> Sample()
    {
        var now = DateTime.UtcNow;
        var list = new List<ProcessEntry>();
        var seen = new HashSet<int>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    TimeSpan cpu = process.TotalProcessorTime;
                    double percent = 0;
                    if (_previous.TryGetValue(process.Id, out var prev)) percent = CpuPercent(cpu - prev.Cpu, now - prev.At, Environment.ProcessorCount);
                    _previous[process.Id] = (cpu, now);
                    seen.Add(process.Id);
                    DateTime? started = null;
                    try { started = process.StartTime; } catch (Exception) { }
                    list.Add(new ProcessEntry(process.Id, process.ProcessName, percent, process.WorkingSet64,
                        string.IsNullOrWhiteSpace(process.MainWindowTitle) ? null : process.MainWindowTitle, started));
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    list.Add(new ProcessEntry(process.Id, SafeName(process), 0, SafeMemory(process), null));
                }
            }
        }

        foreach (int gone in _previous.Keys.Where(k => !seen.Contains(k)).ToList()) _previous.Remove(gone);
        return list;
    }

    private static string SafeName(Process p) { try { return p.ProcessName; } catch (Exception) { return "?"; } }

    private static long SafeMemory(Process p) { try { return p.WorkingSet64; } catch (Exception) { return 0; } }

    /// <summary>Encerra o processo. Recusa os do Windows e o próprio MB Extreme.</summary>
    public static (bool Ok, string Message) Kill(int id, string expectedName, DateTime? expectedStart)
    {
        if (id == Environment.ProcessId) return (false, "Este é o próprio MB Extreme.");
        try
        {
            using var process = Process.GetProcessById(id);
            if (IsProtectedName(process.ProcessName)) return (false, $"{process.ProcessName} é um processo essencial do Windows e não pode ser encerrado por aqui.");
            string name = process.ProcessName;
            // o número do processo pode ter sido reaproveitado por outro programa enquanto a confirmação estava aberta
            if (!name.Equals(expectedName, StringComparison.OrdinalIgnoreCase)) return (false, "O programa já foi encerrado.");
            if (expectedStart is DateTime start)
            {
                try { if (Math.Abs((process.StartTime - start).TotalSeconds) > 2) return (false, "O programa já foi encerrado."); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
            }
            process.Kill(entireProcessTree: false);
            process.WaitForExit(3000);
            return (true, $"{name} encerrado.");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return (false, "Não foi possível encerrar: " + ex.Message);
        }
    }
}
