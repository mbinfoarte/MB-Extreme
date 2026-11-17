using System.Diagnostics;
using System.Numerics;
using MBExtreme.Core.Benchmark;
using MBExtreme.Core.Sensors;

namespace MBExtreme.Core.Stress;

public sealed record PerCoreOptions(int SecondsPerCore, double? TemperatureLimit, bool PhysicalOnly);

public sealed record PerCoreProgress(int Index, int Total, int LogicalCore, TimeSpan Elapsed, long Errors, double? CpuTemperature);

public sealed record PerCoreResult(int LogicalCore, long Errors, double? MaxTemperature, bool Completed);

/// <summary>
/// Teste de estresse núcleo por núcleo, no estilo usado para ajustar o Curve Optimizer do Ryzen: um núcleo de cada vez
/// roda cálculos conferidos; o núcleo que errar está instável com o ajuste atual da BIOS.
/// </summary>
public static class PerCoreStress
{
    /// <summary>Com SMT/Hyper-Threading, os núcleos lógicos 2k e 2k+1 são irmãos; testar só os pares cobre cada núcleo físico.</summary>
    public static IReadOnlyList<int> CoresToTest(int logicalProcessors, int physicalCores, bool physicalOnly)
    {
        int count = Math.Min(logicalProcessors, 64);
        bool smt = physicalCores > 0 && logicalProcessors >= physicalCores * 2;
        var cores = Enumerable.Range(0, count);
        return physicalOnly && smt ? cores.Where(c => c % 2 == 0).ToList() : cores.ToList();
    }

    public static IReadOnlyList<PerCoreResult> Run(
        PerCoreOptions options,
        IReadOnlyList<int> cores,
        Func<SensorSnapshot?> readSensors,
        Action<PerCoreProgress> onProgress,
        CancellationToken stop)
    {
        var results = new List<PerCoreResult>();
        double referenceScalar = StressService.CpuUnit();
        double referenceVector = VectorUnit();

        for (int i = 0; i < cores.Count; i++)
        {
            int core = cores[i];
            long errors = 0;
            using var worker = CancellationTokenSource.CreateLinkedTokenSource(stop);
            var token = worker.Token;
            var thread = new Thread(() =>
            {
                CoreAffinity.PinCurrentThread(core);
                bool vector = false;
                while (!token.IsCancellationRequested)
                {
                    double value = vector ? VectorUnit() : StressService.CpuUnit();
                    if (value != (vector ? referenceVector : referenceScalar)) Interlocked.Increment(ref errors);
                    vector = !vector;
                }
            }) { IsBackground = true };
            thread.Start();

            var watch = Stopwatch.StartNew();
            double? max = null;
            bool completed = true;
            while (watch.Elapsed.TotalSeconds < options.SecondsPerCore)
            {
                if (stop.IsCancellationRequested) { completed = false; break; }

                double? temp = readSensors()?.CpuTemperature;
                if (temp is { } t) max = max is null ? t : Math.Max(max.Value, t);
                onProgress(new PerCoreProgress(i, cores.Count, core, watch.Elapsed, Interlocked.Read(ref errors), temp));

                if (options.TemperatureLimit is { } limit && temp >= limit) { completed = false; break; }
                try { stop.WaitHandle.WaitOne(1000); }
                catch (ObjectDisposedException) { break; }
            }

            worker.Cancel();
            thread.Join();
            results.Add(new PerCoreResult(core, Interlocked.Read(ref errors), max, completed));
            if (!completed) break;
        }

        return results;
    }

    /// <summary>Cálculo vetorial determinístico (SIMD): o resultado só muda se a CPU errar.</summary>
    internal static double VectorUnit()
    {
        int width = Vector<double>.Count;
        var data = new double[width * 64];
        for (int i = 0; i < data.Length; i++) data[i] = 1.0 + (i % 13) * 0.0625;
        var a = new Vector<double>(1.0009765625);
        var b = new Vector<double>(0.0009765625);

        for (int round = 0; round < 20_000; round++)
        {
            for (int i = 0; i <= data.Length - width; i += width)
            {
                var v = new Vector<double>(data, i);
                v = v * a + b;
                v = Vector.SquareRoot(v * v);
                v.CopyTo(data, i);
            }
        }

        double sum = 0;
        foreach (double d in data) sum += d;
        return sum;
    }
}
