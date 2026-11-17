using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MBExtreme.Core.Sensors;

/// <summary>Leitura de uma placa de vídeo pela biblioteca de sensores (para escolher a certa quando há duas).</summary>
public sealed record GpuReading(string Kind, string Name, double? Temperature, double? Load, double? Power, double? Clock);

/// <summary>
/// Contas para o MB Extreme mostrar os mesmos números do Gerenciador de Tarefas:
/// processador pelo "% Processor Utility" (conta o turbo, como o Gerenciador desde o Windows 8) e placa de vídeo pelo motor mais ocupado
/// (3D, cópia, vídeo...) de cada placa, escolhendo a placa dedicada quando há uma integrada também.
/// </summary>
public static class UsageRules
{
    private static readonly Regex Engine = new(@"luid_(0x[0-9A-Fa-f]+_0x[0-9A-Fa-f]+)_phys_\d+_eng_\d+_engtype_(.+)$");
    private static readonly Regex Adapter = new(@"luid_(0x[0-9A-Fa-f]+_0x[0-9A-Fa-f]+)");

    /// <summary>Lê o adaptador e o tipo de motor de "pid_1234_luid_0x..._0x..._phys_0_eng_3_engtype_3D".</summary>
    public static (string Luid, string EngineType)? ParseEngine(string instance)
    {
        var m = Engine.Match(instance);
        return m.Success ? (m.Groups[1].Value, m.Groups[2].Value) : null;
    }

    /// <summary>Uso de cada placa como no Gerenciador: soma por tipo de motor (todos os programas) e fica com o tipo mais ocupado.</summary>
    public static IReadOnlyDictionary<string, double> GpuByAdapter(IEnumerable<(string Instance, double Percent)> samples)
    {
        var perEngine = new Dictionary<(string, string), double>();
        foreach (var (instance, percent) in samples)
        {
            if (percent <= 0 || ParseEngine(instance) is not { } e) continue;
            perEngine[(e.Luid, e.EngineType)] = perEngine.GetValueOrDefault((e.Luid, e.EngineType)) + percent;
        }
        return perEngine.GroupBy(kv => kv.Key.Item1).ToDictionary(g => g.Key, g => Math.Min(100, g.Max(kv => kv.Value)), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A placa dedicada é a que usa mais memória própria (a integrada usa só um pedacinho reservado da RAM).</summary>
    public static string? DedicatedAdapter(IEnumerable<(string Instance, double DedicatedBytes)> memory)
    {
        string? best = null;
        double max = -1;
        foreach (var (instance, bytes) in memory)
        {
            var m = Adapter.Match(instance);
            if (!m.Success || bytes <= max) continue;
            max = bytes;
            best = m.Groups[1].Value;
        }
        return best;
    }

    /// <summary>Placa integrada no processador (Radeon dos Ryzen, Intel UHD/Iris)?</summary>
    public static bool LooksIntegrated(string kind, string name)
    {
        if (kind.Equals("GpuIntel", StringComparison.OrdinalIgnoreCase)) return !name.Contains("Arc", StringComparison.OrdinalIgnoreCase);
        if (kind.Equals("GpuAmd", StringComparison.OrdinalIgnoreCase))
            return !Regex.IsMatch(name, @"\b(RX|R9|R7|Pro W|Vega 56|Vega 64|Radeon VII)\b", RegexOptions.IgnoreCase);
        return false;
    }

    /// <summary>Com duas placas, mostra a dedicada (NVIDIA, AMD RX, Intel Arc); com uma só, ela mesma.</summary>
    public static GpuReading? PickGpu(IReadOnlyList<GpuReading> gpus)
    {
        if (gpus.Count == 0) return null;
        return gpus.OrderBy(g => LooksIntegrated(g.Kind, g.Name) ? 1 : 0)
            .ThenBy(g => g.Kind.Equals("GpuNvidia", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenByDescending(g => g.Load ?? 0)
            .First();
    }

    /// <summary>Temperatura do processador: o sensor principal (Package da Intel, Tctl/Tdie da AMD) e, se faltar, o maior dos núcleos.</summary>
    public static double? CpuTemperature(IEnumerable<(string Name, double Value)> sensors)
    {
        var list = sensors.ToList();
        foreach (var key in new[] { "Package", "Tctl/Tdie", "Tctl", "Tdie" })
        {
            var hit = list.Where(s => s.Name.Contains(key, StringComparison.OrdinalIgnoreCase)).Select(s => (double?)s.Value).FirstOrDefault();
            if (hit is not null) return hit;
        }
        var cores = list.Where(s => s.Name.StartsWith("Core", StringComparison.OrdinalIgnoreCase) || s.Name.StartsWith("CCD", StringComparison.OrdinalIgnoreCase)).ToList();
        if (cores.Count > 0) return cores.Max(s => s.Value);
        return list.Count > 0 ? list.Max(s => s.Value) : null;
    }
}

/// <summary>Contadores do Windows lidos entre uma chamada e outra (o uso é a diferença desde a leitura anterior).</summary>
public sealed class WindowsUsage : IDisposable
{
    private PerformanceCounter? _cpu;
    private bool _cpuBroken, _gpuBroken;
    private PerformanceCounterCategory? _gpu, _gpuMemory;
    private Dictionary<string, CounterSample>? _gpuBefore;

    /// <summary>Uso do processador como no Gerenciador de Tarefas (0 a 100) ou null sem o contador.</summary>
    public double? Cpu()
    {
        if (_cpuBroken) return null;
        try
        {
            if (_cpu is null)
            {
                // "% Processor Utility" existe do Windows 8 em diante; nos mais antigos, o clássico "% Processor Time"
                _cpu = PerformanceCounterCategory.CounterExists("% Processor Utility", "Processor Information")
                    ? new PerformanceCounter("Processor Information", "% Processor Utility", "_Total", true)
                    : new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
                _cpu.NextValue();
                return null;   // a primeira leitura é sempre zero
            }
            return Math.Clamp(_cpu.NextValue(), 0, 100);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            _cpuBroken = true;
            return null;
        }
    }

    /// <summary>Uso da placa dedicada como no Gerenciador de Tarefas (0 a 100) ou null sem os contadores.</summary>
    public double? Gpu()
    {
        if (_gpuBroken) return null;
        try
        {
            _gpu ??= new PerformanceCounterCategory("GPU Engine");
            var data = _gpu.ReadCategory()["Utilization Percentage"];
            var now = new Dictionary<string, CounterSample>(StringComparer.Ordinal);
            if (data is not null) foreach (InstanceData item in data.Values) now[item.InstanceName] = item.Sample;
            var before = _gpuBefore;
            _gpuBefore = now;
            if (before is null) return null;
            var byAdapter = UsageRules.GpuByAdapter(now.Where(kv => before.ContainsKey(kv.Key))
                .Select(kv => (kv.Key, (double)CounterSample.Calculate(before[kv.Key], kv.Value))));
            string? dedicated = null;
            try
            {
                _gpuMemory ??= new PerformanceCounterCategory("GPU Adapter Memory");
                var mem = _gpuMemory.ReadCategory()["Dedicated Usage"];
                if (mem is not null) dedicated = UsageRules.DedicatedAdapter(mem.Values.Cast<InstanceData>().Select(i => (i.InstanceName, (double)i.RawValue)));
            }
            catch (InvalidOperationException) { }
            if (dedicated is not null && byAdapter.TryGetValue(dedicated, out var value)) return value;
            return byAdapter.Count == 0 ? 0 : byAdapter.Values.Max();
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or FormatException or PlatformNotSupportedException)
        {
            _gpuBroken = true;
            return null;
        }
    }

    private PerformanceCounter? _disk;
    private bool _diskBroken;

    /// <summary>Atividade do disco (0 a 100, "% Disk Time" de todos os discos) ou null sem o contador.</summary>
    public double? Disk()
    {
        if (_diskBroken) return null;
        try
        {
            if (_disk is null)
            {
                _disk = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total", true);
                _disk.NextValue();
                return null;   // a primeira leitura é sempre zero
            }
            return Math.Clamp(_disk.NextValue(), 0, 100);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            _diskBroken = true;
            return null;
        }
    }

    public void Dispose() { _cpu?.Dispose(); _disk?.Dispose(); }
}
