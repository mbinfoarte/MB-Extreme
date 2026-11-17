using System.Diagnostics;

namespace MBExtreme.UI;

/// <summary>Quais programas estão usando mais processador agora (mede o tempo de CPU de cada processo por 1 segundo).</summary>
internal static class CpuHogs
{
    public sealed record Hog(string Name, double Percent);

    public static IReadOnlyList<Hog> Top(int count)
    {
        var first = new Dictionary<int, (string Name, TimeSpan Cpu)>();
        foreach (var p in Process.GetProcesses())
        {
            try { if (p.Id > 4) first[p.Id] = (p.ProcessName, p.TotalProcessorTime); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
            finally { p.Dispose(); }
        }
        var watch = Stopwatch.StartNew();
        Thread.Sleep(1000);
        double wall = watch.Elapsed.TotalMilliseconds * Environment.ProcessorCount;
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, (name, cpu)) in first)
        {
            try
            {
                using var p = Process.GetProcessById(id);
                double used = (p.TotalProcessorTime - cpu).TotalMilliseconds / wall * 100;
                if (used > 0.5) result[name] = result.GetValueOrDefault(name) + used;
            }
            catch (Exception ex) when (ex is ArgumentException or System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
        }
        return result.Where(r => !r.Key.Equals("Idle", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Value).Take(count).Select(r => new Hog(r.Key, r.Value)).ToList();
    }
}
