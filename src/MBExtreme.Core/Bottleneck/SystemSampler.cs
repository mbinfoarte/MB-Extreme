using System.Diagnostics;
using System.Runtime.Versioning;

namespace MBExtreme.Core.Bottleneck;

/// <summary>Contadores do Windows para o monitor de gargalos: uso por núcleo e fila do disco.</summary>
[SupportedOSPlatform("windows")]
public sealed class SystemSampler : IDisposable
{
    private readonly List<PerformanceCounter> _cores = new();
    private PerformanceCounter? _disk;

    public SystemSampler()
    {
        try
        {
            var category = new PerformanceCounterCategory("Processor");
            foreach (string instance in category.GetInstanceNames().Where(i => i != "_Total"))
                _cores.Add(new PerformanceCounter("Processor", "% Processor Time", instance, true));
            _disk = new PerformanceCounter("PhysicalDisk", "Current Disk Queue Length", "_Total", true);
            foreach (var c in _cores) c.NextValue();
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // contadores desativados no Windows: o relatório avisa que faltaram dados
        }
    }

    public bool Available => _cores.Count > 0;

    /// <summary>Uso do núcleo mais ocupado (0 a 100), ou nulo se os contadores não existem.</summary>
    public double? MaxCoreLoad()
    {
        if (_cores.Count == 0) return null;
        try { return _cores.Max(c => (double)c.NextValue()); }
        catch (InvalidOperationException) { return null; }
    }

    public double? DiskQueue()
    {
        try { return _disk?.NextValue(); }
        catch (InvalidOperationException) { return null; }
    }

    public void Dispose()
    {
        foreach (var c in _cores) c.Dispose();
        _disk?.Dispose();
    }
}
