using System.Diagnostics;
using MBExtreme.Core.Sensors;

namespace MBExtreme.Core.Stress;

public sealed record StressOptions(bool Cpu, bool Memory, TimeSpan Duration, double? TemperatureLimit, int MemoryMegabytes);

public sealed record StressSample(TimeSpan Elapsed, double? CpuTemperature, double? CpuLoad, double? GpuTemperature, long Errors);

public sealed record StressResult(bool Completed, string Reason, TimeSpan Elapsed, long Errors, double? MaxCpuTemperature, double? MaxGpuTemperature);

/// <summary>
/// Teste de estresse de CPU e memória. Cada linha de execução repete um cálculo cujo resultado é conhecido
/// e a memória é preenchida e conferida; qualquer diferença conta como erro (sinal de instabilidade).
/// Para sozinho ao chegar na duração, na temperatura limite ou quando o usuário pedir.
/// </summary>
public static class StressService
{
    public static StressResult Run(
        StressOptions options,
        Func<SensorSnapshot?> readSensors,
        Action<StressSample> onSample,
        CancellationToken stop)
    {
        long errors = 0;
        using var workersStop = CancellationTokenSource.CreateLinkedTokenSource(stop);
        var token = workersStop.Token;
        var threads = new List<Thread>();

        if (options.Cpu)
        {
            double reference = CpuUnit();
            for (int i = 0; i < Environment.ProcessorCount; i++)
            {
                var t = new Thread(() =>
                {
                    while (!token.IsCancellationRequested)
                        if (CpuUnit() != reference) Interlocked.Increment(ref errors);
                })
                { IsBackground = true, Priority = ThreadPriority.BelowNormal };
                threads.Add(t);
            }
        }

        if (options.Memory)
        {
            int megabytes = Math.Clamp(options.MemoryMegabytes, 64, 8192);
            var t = new Thread(() => MemoryLoop(megabytes, token, () => Interlocked.Increment(ref errors)))
            { IsBackground = true, Priority = ThreadPriority.BelowNormal };
            threads.Add(t);
        }

        threads.ForEach(t => t.Start());

        var watch = Stopwatch.StartNew();
        double? maxCpu = null, maxGpu = null;
        string reason = "Concluído no tempo escolhido.";
        bool completed = true;

        while (true)
        {
            if (stop.IsCancellationRequested)
            {
                reason = "Interrompido por você.";
                completed = false;
                break;
            }

            var snapshot = readSensors();
            double? cpuTemp = snapshot?.CpuTemperature;
            double? gpuTemp = snapshot?.GpuTemperature;
            maxCpu = Max(maxCpu, cpuTemp);
            maxGpu = Max(maxGpu, gpuTemp);
            onSample(new StressSample(watch.Elapsed, cpuTemp, snapshot?.CpuLoad, gpuTemp, Interlocked.Read(ref errors)));

            if (options.TemperatureLimit is { } limit && (cpuTemp >= limit || gpuTemp >= limit))
            {
                reason = $"Parou: a temperatura chegou ao limite de {limit:0} °C.";
                completed = false;
                break;
            }

            if (watch.Elapsed >= options.Duration) break;

            try { stop.WaitHandle.WaitOne(1000); }
            catch (ObjectDisposedException) { break; }
        }

        workersStop.Cancel();
        threads.ForEach(t => t.Join());

        long total = Interlocked.Read(ref errors);
        if (total > 0) reason += $" Atenção: {total} erro(s) de cálculo ou de memória (instabilidade).";
        return new StressResult(completed, reason, watch.Elapsed, total, maxCpu, maxGpu);
    }

    private static double? Max(double? a, double? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);

    /// <summary>Cálculo determinístico: em um PC estável sempre dá o mesmo número.</summary>
    internal static double CpuUnit()
    {
        double x = 0.5;
        for (int i = 1; i <= 400_000; i++)
        {
            x = Math.Sin(x) * 1.5 + Math.Sqrt(i) / (i + 1.0);
            x -= Math.Floor(x);
        }

        return x;
    }

    private static void MemoryLoop(int megabytes, CancellationToken token, Action onError)
    {
        long words = megabytes * 1024L * 1024 / 8;
        ulong[] block;
        try { block = new ulong[words]; }
        catch (OutOfMemoryException) { return; }

        ulong[] patterns = { 0x5555555555555555UL, 0xAAAAAAAAAAAAAAAAUL, 0x0123456789ABCDEFUL, 0UL, ulong.MaxValue };
        int round = 0;
        while (!token.IsCancellationRequested)
        {
            ulong pattern = patterns[round++ % patterns.Length];
            for (long i = 0; i < block.Length; i++) block[i] = pattern ^ (ulong)i;
            for (long i = 0; i < block.Length; i++)
            {
                if (block[i] != (pattern ^ (ulong)i)) onError();
                if ((i & 0xFFFFF) == 0 && token.IsCancellationRequested) return;
            }
        }
    }
}
