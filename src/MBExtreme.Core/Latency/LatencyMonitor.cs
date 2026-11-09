using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;

namespace MBExtreme.Core.Latency;

/// <summary>Mede DPC e ISR do sistema inteiro pelo ETW do kernel durante um tempo, sem instalar driver. Precisa de administrador.</summary>
[SupportedOSPlatform("windows")]
public static class LatencyMonitor
{
    [DllImport("ntdll.dll")]
    private static extern int NtQueryTimerResolution(out uint minimum, out uint maximum, out uint current);

    /// <summary>Resolução atual do temporizador do Windows em milissegundos (0,5 a 15,6). Só leitura.</summary>
    public static double? TimerResolutionMilliseconds()
    {
        try
        {
            return NtQueryTimerResolution(out _, out _, out uint current) == 0 ? current / 10000.0 : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    public static IReadOnlyList<DriverLatency> Run(TimeSpan duration, Action<TimeSpan, long> onTick, CancellationToken token, out long totalEvents)
    {
        var aggregator = new DpcIsrAggregator();
        object gate = new();

        using var session = new TraceEventSession(KernelTraceEventParser.KernelSessionName);
        session.EnableKernelProvider(
            KernelTraceEventParser.Keywords.DeferedProcedureCalls
            | KernelTraceEventParser.Keywords.Interrupt
            | KernelTraceEventParser.Keywords.ImageLoad);

        var kernel = session.Source.Kernel;
        kernel.PerfInfoDPC += d => { lock (gate) aggregator.AddEvent("DPC", d.Routine, d.ElapsedTimeMSec * 1000); };
        kernel.PerfInfoThreadedDPC += d => { lock (gate) aggregator.AddEvent("DPC", d.Routine, d.ElapsedTimeMSec * 1000); };
        kernel.PerfInfoTimerDPC += d => { lock (gate) aggregator.AddEvent("DPC", d.Routine, d.ElapsedTimeMSec * 1000); };
        kernel.PerfInfoISR += d => { lock (gate) aggregator.AddEvent("ISR", d.Routine, d.ElapsedTimeMSec * 1000); };
        kernel.ImageLoad += d => { lock (gate) aggregator.AddImage((ulong)d.ImageBase, (ulong)d.ImageSize, d.FileName); };
        kernel.ImageDCStart += d => { lock (gate) aggregator.AddImage((ulong)d.ImageBase, (ulong)d.ImageSize, d.FileName); };

        var worker = Task.Run(() => session.Source.Process());
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < duration && !token.IsCancellationRequested)
        {
            Thread.Sleep(500);
            long events;
            lock (gate) events = aggregator.TotalEvents;
            onTick(DateTime.UtcNow - started, events);
        }

        session.Stop();
        try { worker.Wait(3000); }
        catch (AggregateException) { /* a sessão foi encerrada */ }

        lock (gate)
        {
            totalEvents = aggregator.TotalEvents;
            return aggregator.Top(12);
        }
    }
}
