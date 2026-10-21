using System.Diagnostics;
using System.Text.Json;
using MBExtreme.Core.Progress;

namespace MBExtreme.Core.Benchmark;

/// <summary>Resultado de uma rodada. A pontuação é relativa: serve para comparar este mesmo PC antes e depois de uma mudança.</summary>
public sealed record BenchmarkResult(
    DateTime When,
    int Threads,
    double CpuSingleMops,
    double CpuMultiMops,
    double MemoryGBps,
    double DiskWriteMBps,
    double DiskReadMBps,
    int Score);

/// <summary>Benchmark próprio de CPU (1 núcleo e todos), memória e disco. Cada etapa dura poucos segundos.</summary>
public static class BenchmarkService
{
    public const string StepSingle = "CPU (1 núcleo)";
    public const string StepMulti = "CPU (todos os núcleos)";
    public const string StepMemory = "Memória";
    public const string StepDisk = "Disco";

    public static string[] Steps => new[] { StepSingle, StepMulti, StepMemory, StepDisk };

    private const int PhaseMilliseconds = 2500;
    private const int DiskMegabytes = 256;

    private static double _sink;

    public static BenchmarkResult Run(IProgress<ProgressEvent> progress, CancellationToken token, string? diskDirectory = null)
    {
        progress.Report(new ProgressEvent(StepSingle, 2, "Medindo um núcleo"));
        double single = CpuPhase(1, token);

        progress.Report(new ProgressEvent(StepMulti, 25, "Medindo todos os núcleos"));
        int threads = Environment.ProcessorCount;
        double multi = CpuPhase(threads, token);

        progress.Report(new ProgressEvent(StepMemory, 50, "Medindo a velocidade da memória"));
        double memory = MemoryPhase(token);

        progress.Report(new ProgressEvent(StepDisk, 70, "Gravando e lendo um arquivo temporário"));
        var (write, read) = DiskPhase(diskDirectory ?? Path.GetTempPath(), progress, token);

        progress.Report(new ProgressEvent(StepDisk, 100, "Concluído"));
        return new BenchmarkResult(DateTime.Now, threads, single, multi, memory, write, read,
            ComputeScore(single, multi, memory, write, read));
    }

    public static int ComputeScore(double single, double multi, double memoryGBps, double writeMBps, double readMBps) =>
        (int)Math.Round(single * 4 + multi * 1 + memoryGBps * 50 + (writeMBps + readMBps) / 2 * 0.5);

    /// <summary>Milhões de iterações por segundo, somando todas as linhas de execução.</summary>
    private static double CpuPhase(int threads, CancellationToken token)
    {
        var counts = new long[threads];
        var watch = Stopwatch.StartNew();
        var workers = Enumerable.Range(0, threads).Select(index => new Thread(() =>
        {
            long total = 0;
            double acc = 0;
            while (watch.ElapsedMilliseconds < PhaseMilliseconds && !token.IsCancellationRequested)
            {
                acc += Kernel(1_000_000);
                total += 1_000_000;
            }

            counts[index] = total;
            _sink += acc;
        }) { IsBackground = true }).ToList();

        workers.ForEach(t => t.Start());
        workers.ForEach(t => t.Join());
        token.ThrowIfCancellationRequested();

        double seconds = Math.Max(watch.Elapsed.TotalSeconds, 0.001);
        return counts.Sum() / seconds / 1_000_000.0;
    }

    private static double Kernel(long iterations)
    {
        double x = 1.0001;
        ulong h = 1469598103934665603UL;
        for (long i = 0; i < iterations; i++)
        {
            x = x * 1.0000001 + 0.5 / (i + 1);
            h = (h ^ (ulong)i) * 1099511628211UL;
            if ((i & 0xFFFF) == 0) x = Math.Sqrt(x * x + 1);
        }

        return x + (h & 1);
    }

    private static double MemoryPhase(CancellationToken token)
    {
        const int size = 128 * 1024 * 1024;
        var source = new byte[size];
        var target = new byte[size];
        new Random(42).NextBytes(source);

        long copied = 0;
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < PhaseMilliseconds && !token.IsCancellationRequested)
        {
            Buffer.BlockCopy(source, 0, target, 0, size);
            copied += size;
        }

        token.ThrowIfCancellationRequested();
        _sink += target[size / 2];
        return copied / Math.Max(watch.Elapsed.TotalSeconds, 0.001) / 1_000_000_000.0;
    }

    private static (double WriteMBps, double ReadMBps) DiskPhase(string directory, IProgress<ProgressEvent> progress, CancellationToken token)
    {
        const int chunk = 4 * 1024 * 1024;
        const int chunks = DiskMegabytes / 4;
        string path = Path.Combine(directory, "mbextreme-bench-" + Guid.NewGuid().ToString("N") + ".tmp");
        var buffer = new byte[chunk];
        new Random(7).NextBytes(buffer);

        try
        {
            var watch = Stopwatch.StartNew();
            using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.WriteThrough))
            {
                for (int i = 0; i < chunks; i++)
                {
                    token.ThrowIfCancellationRequested();
                    file.Write(buffer, 0, chunk);
                    progress.Report(new ProgressEvent(StepDisk, 70 + 15.0 * i / chunks, "Gravando no disco"));
                }

                file.Flush(true);
            }

            double write = DiskMegabytes / Math.Max(watch.Elapsed.TotalSeconds, 0.001);

            watch.Restart();
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None, 1 << 16, FileOptions.SequentialScan))
            {
                int i = 0;
                while (file.Read(buffer, 0, chunk) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    progress.Report(new ProgressEvent(StepDisk, 85 + 14.0 * i++ / chunks, "Lendo do disco"));
                }
            }

            double read = DiskMegabytes / Math.Max(watch.Elapsed.TotalSeconds, 0.001);
            return (write, read);
        }
        finally
        {
            try { File.Delete(path); }
            catch (IOException) { /* arquivo temporário; o Windows limpa depois */ }
        }
    }
}

/// <summary>Histórico das últimas rodadas, em %AppData%\MBExtreme\bench-history.json.</summary>
public static class BenchmarkHistory
{
    private static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "bench-history.json");

    public static IReadOnlyList<BenchmarkResult> Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path)) return Array.Empty<BenchmarkResult>();
            return JsonSerializer.Deserialize<List<BenchmarkResult>>(File.ReadAllText(path)) ?? new List<BenchmarkResult>();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return Array.Empty<BenchmarkResult>();
        }
    }

    public static void Add(BenchmarkResult result, string? path = null)
    {
        path ??= DefaultPath;
        var all = Load(path).Append(result).TakeLast(20).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
    }
}
