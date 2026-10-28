using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace MBExtreme.Core.Recording;

/// <summary>Quanto o programa (MB Extreme + motor de vídeo) e o PC inteiro estão usando agora.</summary>
public sealed record UsageSnapshot(
    double AppCpu, long AppRamBytes, double AppGpu, double AppEncoder,
    double SystemCpu, double SystemGpu, long SystemRamUsed, long SystemRamTotal);

/// <summary>Contas do monitor de uso (separadas para dar para testar sem o Windows).</summary>
public static class UsageMath
{
    private static readonly Regex Instance = new(@"pid_(?<pid>\d+)_.*engtype_(?<eng>[A-Za-z0-9 ]+)$", RegexOptions.Compiled);

    /// <summary>Lê "pid_1234_luid_..._eng_0_engtype_VideoEncode" do contador "GPU Engine".</summary>
    public static (int Pid, string Engine)? ParseEngine(string instance)
    {
        var m = Instance.Match(instance);
        return m.Success && int.TryParse(m.Groups["pid"].Value, out int pid) ? (pid, m.Groups["eng"].Value.Trim()) : null;
    }

    /// <summary>Chip de vídeo da placa (NVENC, VCN da AMD, Quick Sync): o Windows chama de VideoEncode, VideoCodec ou parecido.</summary>
    public static bool IsEncoderEngine(string engine) =>
        engine.Contains("Encode", StringComparison.OrdinalIgnoreCase) || engine.Contains("Codec", StringComparison.OrdinalIgnoreCase);

    public static bool Is3dEngine(string engine) => engine.Equals("3D", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Soma por tipo de motor (como o Gerenciador de Tarefas): a GPU do sistema e a do programa são o motor 3D; o codificador é à parte.
    /// </summary>
    public static (double SystemGpu, double AppGpu, double AppEncoder) Gpu(IEnumerable<(string Instance, double Percent)> samples, IReadOnlyCollection<int> appPids)
    {
        double system3d = 0, systemEnc = 0, app3d = 0, appEnc = 0;
        foreach (var (instance, percent) in samples)
        {
            if (ParseEngine(instance) is not { } e || percent <= 0) continue;
            bool mine = appPids.Contains(e.Pid);
            if (Is3dEngine(e.Engine)) { system3d += percent; if (mine) app3d += percent; }
            else if (IsEncoderEngine(e.Engine)) { systemEnc += percent; if (mine) appEnc += percent; }
        }
        return (Math.Min(100, Math.Max(system3d, systemEnc)), Math.Min(100, app3d), Math.Min(100, appEnc));
    }

    /// <summary>Uso de processador de um processo entre duas leituras, em % do PC inteiro (todos os núcleos).</summary>
    public static double CpuPercent(TimeSpan cpuBefore, TimeSpan cpuAfter, TimeSpan wall, int cores) =>
        wall <= TimeSpan.Zero || cores <= 0 ? 0 : Math.Clamp((cpuAfter - cpuBefore).TotalMilliseconds / (wall.TotalMilliseconds * cores) * 100, 0, 100);

    public static string Bytes(long bytes) => bytes >= 1L << 30
        ? (bytes / (double)(1L << 30)).ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")) + " GB"
        : (bytes >> 20) + " MB";
}

/// <summary>
/// Mede CPU, GPU (3D e chip de vídeo) e memória do programa e do PC. Chame <see cref="Sample"/> a cada segundo ou dois, fora da tela (lê contadores do Windows).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ResourceMonitor : IDisposable
{
    private readonly Dictionary<int, TimeSpan> _cpu = new();
    private DateTime _at = DateTime.UtcNow;
    private PerformanceCounter? _systemCpu;
    private PerformanceCounterCategory? _gpu;
    private Dictionary<string, CounterSample>? _gpuBefore;
    private bool _gpuBroken;

    public ResourceMonitor()
    {
        try
        {
            _systemCpu = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
            _systemCpu.NextValue();
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { _systemCpu = null; }
    }

    public UsageSnapshot Sample(IReadOnlyCollection<int> appPids)
    {
        var now = DateTime.UtcNow;
        var wall = now - _at;
        _at = now;

        double appCpu = 0;
        long ram = 0;
        var seen = new HashSet<int>();
        foreach (int pid in appPids)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                var total = p.TotalProcessorTime;
                if (_cpu.TryGetValue(pid, out var before)) appCpu += UsageMath.CpuPercent(before, total, wall, Environment.ProcessorCount);
                _cpu[pid] = total;
                ram += p.WorkingSet64;
                seen.Add(pid);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
        foreach (int gone in _cpu.Keys.Where(k => !seen.Contains(k)).ToList()) _cpu.Remove(gone);

        double systemCpu = 0;
        try { systemCpu = _systemCpu?.NextValue() ?? 0; }
        catch (InvalidOperationException) { }

        var (systemGpu, appGpu, appEnc) = ReadGpu(appPids);
        var (used, totalRam) = Memory();
        return new UsageSnapshot(Math.Min(100, appCpu), ram, appGpu, appEnc, Math.Clamp(systemCpu, 0, 100), systemGpu, used, totalRam);
    }

    /// <summary>Lê todos os motores da GPU de uma vez e calcula o uso desde a leitura anterior.</summary>
    private (double, double, double) ReadGpu(IReadOnlyCollection<int> appPids)
    {
        if (_gpuBroken) return (0, 0, 0);
        try
        {
            _gpu ??= new PerformanceCounterCategory("GPU Engine");
            var data = _gpu.ReadCategory()["Utilization Percentage"];
            var now = new Dictionary<string, CounterSample>(StringComparer.Ordinal);
            if (data is not null)
                foreach (InstanceData item in data.Values) now[item.InstanceName] = item.Sample;
            var before = _gpuBefore;
            _gpuBefore = now;
            if (before is null) return (0, 0, 0);
            var samples = now.Where(kv => before.ContainsKey(kv.Key))
                .Select(kv => (kv.Key, (double)CounterSample.Calculate(before[kv.Key], kv.Value)));
            return UsageMath.Gpu(samples, appPids);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or FormatException)
        {
            _gpuBroken = true;   // Windows sem os contadores de GPU (muito antigo ou desativados)
            return (0, 0, 0);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    private static (long Used, long Total) Memory()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref status)) return (0, 0);
        return ((long)(status.TotalPhys - status.AvailPhys), (long)status.TotalPhys);
    }

    public void Dispose() => _systemCpu?.Dispose();
}
