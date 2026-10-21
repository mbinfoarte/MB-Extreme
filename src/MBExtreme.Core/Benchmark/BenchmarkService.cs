using System.Diagnostics;
using System.IO.Compression;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using MBExtreme.Core.Progress;

namespace MBExtreme.Core.Benchmark;

public sealed record LatencyPoint(int SizeKb, double Nanoseconds);

public sealed record GpuSceneResult(string Scene, double Fps);

/// <summary>Resultado de uma rodada. A pontuação é relativa: serve para comparar este mesmo PC antes e depois de uma mudança.</summary>
public sealed record BenchmarkResult(
    DateTime When,
    int Threads,
    double CpuSingleMops,
    double CpuMultiMops,
    double MemoryGBps,
    double DiskWriteMBps,
    double DiskReadMBps,
    int Score,
    double CpuSimdGflops = 0,
    double CryptoMBps = 0,
    double CompressMBps = 0,
    double MemoryReadGBps = 0,
    double MemoryWriteGBps = 0,
    double MemoryLatencyNs = 0,
    IReadOnlyList<LatencyPoint>? LatencyCurve = null,
    string? Label = null,
    string? GpuName = null,
    int Gpu3DScore = 0,
    IReadOnlyList<GpuSceneResult>? GpuScenes = null);

public sealed record BenchmarkOptions(bool Disk = true, bool Gpu3D = false, bool Quick = false, string? Label = null);

public sealed record CoreScore(int Core, double Mops);

/// <summary>Benchmark próprio de CPU (1 núcleo, todos, vetorial, criptografia e compressão), memória (banda e latência), disco e GPU 3D.</summary>
public static class BenchmarkService
{
    public const string StepSingle = "CPU (1 núcleo)";
    public const string StepMulti = "CPU (todos os núcleos)";
    public const string StepExtra = "CPU (vetorial, criptografia, compressão)";
    public const string StepMemory = "Memória";
    public const string StepDisk = "Disco";
    public const string StepGpu = "GPU (3D)";

    public static string[] StepsFor(BenchmarkOptions options)
    {
        var steps = new List<string> { StepSingle, StepMulti, StepExtra, StepMemory };
        if (options.Disk) steps.Add(StepDisk);
        if (options.Gpu3D) steps.Add(StepGpu);
        return steps.ToArray();
    }

    private const int DiskMegabytes = 256;

    private static double _sink;

    private static int PhaseMs(BenchmarkOptions o) => o.Quick ? 1200 : 2500;

    public static BenchmarkResult Run(IProgress<ProgressEvent> progress, CancellationToken token, BenchmarkOptions? options = null, string? diskDirectory = null)
    {
        options ??= new BenchmarkOptions();
        int total = StepsFor(options).Length;
        int done = 0;
        double Pct() => 100.0 * done / total;
        int ms = PhaseMs(options);

        progress.Report(new ProgressEvent(StepSingle, Pct(), "Medindo um núcleo"));
        double single = CpuPhase(1, ms, token);
        done++;

        progress.Report(new ProgressEvent(StepMulti, Pct(), "Medindo todos os núcleos"));
        int threads = Environment.ProcessorCount;
        double multi = CpuPhase(threads, ms, token);
        done++;

        progress.Report(new ProgressEvent(StepExtra, Pct(), "Instruções vetoriais"));
        double simd = SimdPhase(ms, token);
        progress.Report(new ProgressEvent(StepExtra, Pct() + 4, "Criptografia (AES)"));
        double crypto = CryptoPhase(ms, token);
        progress.Report(new ProgressEvent(StepExtra, Pct() + 8, "Compressão"));
        double compress = CompressPhase(ms, token);
        done++;

        progress.Report(new ProgressEvent(StepMemory, Pct(), "Banda da memória"));
        var (read, write, copy) = MemoryBandwidthPhase(ms, token);
        progress.Report(new ProgressEvent(StepMemory, Pct() + 6, "Latência da memória"));
        var curve = MemoryLatencyCurve(options.Quick, token);
        double latency = curve.Count > 0 ? curve[^1].Nanoseconds : 0;
        done++;

        double diskWrite = 0, diskRead = 0;
        if (options.Disk)
        {
            progress.Report(new ProgressEvent(StepDisk, Pct(), "Gravando e lendo um arquivo temporário"));
            (diskWrite, diskRead) = DiskPhase(diskDirectory ?? Path.GetTempPath(), progress, Pct(), 100.0 / total, token);
            done++;
        }

        string? gpuName = null;
        int gpuScore = 0;
        IReadOnlyList<GpuSceneResult>? scenes = null;
        if (options.Gpu3D)
        {
            progress.Report(new ProgressEvent(StepGpu, Pct(), "Preparando a placa de vídeo"));
            var gpu = Gpu3DBenchmark.Run(progress, StepGpu, Pct(), 100.0 / total, null, token);
            gpuName = gpu.GpuName;
            gpuScore = gpu.Score;
            scenes = gpu.Scenes;
            done++;
        }

        progress.Report(new ProgressEvent(StepDisk, 100, "Concluído"));
        int score = ComputeScore(single, multi, copy, diskWrite, diskRead) + ExtraScore(simd, crypto, compress, latency);
        return new BenchmarkResult(DateTime.Now, threads, single, multi, copy, diskWrite, diskRead, score,
            simd, crypto, compress, read, write, latency, curve, options.Label, gpuName, gpuScore, scenes);
    }

    public static int ComputeScore(double single, double multi, double memoryGBps, double writeMBps, double readMBps) =>
        (int)Math.Round(single * 4 + multi * 1 + memoryGBps * 50 + (writeMBps + readMBps) / 2 * 0.5);

    public static int ExtraScore(double simdGflops, double cryptoMBps, double compressMBps, double latencyNs) =>
        (int)Math.Round(simdGflops * 2 + cryptoMBps * 0.05 + compressMBps * 0.1 + (latencyNs > 0 ? 5000 / latencyNs : 0));

    /// <summary>Milhões de iterações por segundo, somando todas as linhas de execução.</summary>
    private static double CpuPhase(int threads, int milliseconds, CancellationToken token)
    {
        var counts = new long[threads];
        var watch = Stopwatch.StartNew();
        var workers = Enumerable.Range(0, threads).Select(index => new Thread(() =>
        {
            long total = 0;
            double acc = 0;
            while (watch.ElapsedMilliseconds < milliseconds && !token.IsCancellationRequested)
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

    /// <summary>Teste de cada núcleo lógico, um de cada vez, para achar o mais fraco ou o mais forte. Só Windows (afinidade de thread).</summary>
    public static IReadOnlyList<CoreScore> PerCore(IProgress<ProgressEvent> progress, string step, CancellationToken token, int millisecondsPerCore = 800)
    {
        int count = Math.Min(Environment.ProcessorCount, 64);
        var results = new List<CoreScore>();
        for (int core = 0; core < count; core++)
        {
            token.ThrowIfCancellationRequested();
            progress.Report(new ProgressEvent(step, 100.0 * core / count, $"Núcleo lógico {core + 1} de {count}"));
            double mops = 0;
            var thread = new Thread(() =>
            {
                CoreAffinity.PinCurrentThread(core);
                long total = 0;
                double acc = 0;
                var watch = Stopwatch.StartNew();
                while (watch.ElapsedMilliseconds < millisecondsPerCore)
                {
                    acc += Kernel(1_000_000);
                    total += 1_000_000;
                }

                mops = total / watch.Elapsed.TotalSeconds / 1_000_000.0;
                _sink += acc;
            }) { IsBackground = true };
            thread.Start();
            thread.Join();
            results.Add(new CoreScore(core, mops));
        }

        progress.Report(new ProgressEvent(step, 100, "Concluído"));
        return results;
    }

    /// <summary>GFLOPS em ponto flutuante com instruções vetoriais (SIMD), em um núcleo.</summary>
    private static double SimdPhase(int milliseconds, CancellationToken token)
    {
        int width = Vector<float>.Count;
        var data = new float[width * 512];
        for (int i = 0; i < data.Length; i++) data[i] = 1f + (i % 7) * 0.001f;
        var mul = new Vector<float>(1.0000001f);
        var add = new Vector<float>(0.0000001f);

        long vectors = 0;
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds && !token.IsCancellationRequested)
        {
            for (int round = 0; round < 200; round++)
            {
                for (int i = 0; i <= data.Length - width; i += width)
                {
                    var v = new Vector<float>(data, i);
                    v = v * mul + add;
                    v.CopyTo(data, i);
                }
            }

            vectors += 200L * (data.Length / width);
        }

        token.ThrowIfCancellationRequested();
        _sink += data[0];
        return vectors * width * 2 / Math.Max(watch.Elapsed.TotalSeconds, 0.001) / 1e9;
    }

    private static double CryptoPhase(int milliseconds, CancellationToken token)
    {
        var data = new byte[1 << 20];
        new Random(3).NextBytes(data);
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.GenerateKey();
        aes.GenerateIV();
        using var encryptor = aes.CreateEncryptor();
        var output = new byte[data.Length];

        long bytes = 0;
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds && !token.IsCancellationRequested)
        {
            encryptor.TransformBlock(data, 0, data.Length, output, 0);
            bytes += data.Length;
        }

        token.ThrowIfCancellationRequested();
        _sink += output[0];
        return bytes / Math.Max(watch.Elapsed.TotalSeconds, 0.001) / 1_000_000.0;
    }

    private static double CompressPhase(int milliseconds, CancellationToken token)
    {
        var data = new byte[4 << 20];
        var rng = new Random(5);
        for (int i = 0; i < data.Length; i += 64)
        {
            int run = rng.Next(8, 56);
            byte value = (byte)rng.Next(256);
            for (int k = 0; k < run && i + k < data.Length; k++) data[i + k] = value;
            for (int k = run; k < 64 && i + k < data.Length; k++) data[i + k] = (byte)rng.Next(256);
        }

        long bytes = 0;
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds && !token.IsCancellationRequested)
        {
            using var sink = new MemoryStream(data.Length);
            using (var gzip = new GZipStream(sink, CompressionLevel.Fastest, true)) gzip.Write(data, 0, data.Length);
            bytes += data.Length;
            _sink += sink.Length;
        }

        token.ThrowIfCancellationRequested();
        return bytes / Math.Max(watch.Elapsed.TotalSeconds, 0.001) / 1_000_000.0;
    }

    private static (double ReadGBps, double WriteGBps, double CopyGBps) MemoryBandwidthPhase(int milliseconds, CancellationToken token)
    {
        const int size = 128 * 1024 * 1024;
        var source = new byte[size];
        var target = new byte[size];
        new Random(42).NextBytes(source);

        double Measure(Action action)
        {
            long bytes = 0;
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < milliseconds / 2 && !token.IsCancellationRequested)
            {
                action();
                bytes += size;
            }

            token.ThrowIfCancellationRequested();
            return bytes / Math.Max(watch.Elapsed.TotalSeconds, 0.001) / 1_000_000_000.0;
        }

        double read = Measure(() =>
        {
            var longs = MemoryMarshal.Cast<byte, long>(source.AsSpan());
            long sum = 0;
            for (int i = 0; i < longs.Length; i++) sum += longs[i];
            _sink += sum;
        });
        double write = Measure(() => target.AsSpan().Fill(0x5A));
        double copy = Measure(() => Buffer.BlockCopy(source, 0, target, 0, size));
        _sink += target[size / 2];
        return (read, write, copy);
    }

    /// <summary>
    /// Latência de acesso aleatório (nanossegundos) para tamanhos crescentes: os degraus mostram L1, L2, L3 e a RAM.
    /// </summary>
    public static IReadOnlyList<LatencyPoint> MemoryLatencyCurve(bool quick, CancellationToken token)
    {
        int[] sizesKb = quick ? new[] { 32, 1024, 16384, 131072 } : new[] { 16, 32, 128, 512, 2048, 8192, 32768, 131072 };
        var points = new List<LatencyPoint>();
        foreach (int kb in sizesKb)
        {
            token.ThrowIfCancellationRequested();
            points.Add(new LatencyPoint(kb, ChaseLatency(kb)));
        }

        return points;
    }

    private static double ChaseLatency(int kilobytes)
    {
        int count = kilobytes * 1024 / sizeof(int);
        var next = new int[count];
        for (int i = 0; i < count; i++) next[i] = i;
        var rng = new Random(11);
        for (int i = count - 1; i > 0; i--)
        {
            int j = rng.Next(i);
            (next[i], next[j]) = (next[j], next[i]);
        }

        // Ciclo único (Sattolo): cada posição aponta para a próxima, sem voltar antes de visitar todas.
        const int steps = 4_000_000;
        int position = 0;
        for (int i = 0; i < Math.Min(count, 200_000); i++) position = next[position];

        var watch = Stopwatch.StartNew();
        for (int i = 0; i < steps; i++) position = next[position];
        watch.Stop();
        _sink += position;
        return watch.Elapsed.TotalMilliseconds * 1_000_000 / steps;
    }

    private static (double WriteMBps, double ReadMBps) DiskPhase(string directory, IProgress<ProgressEvent> progress, double from, double span, CancellationToken token)
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
                    progress.Report(new ProgressEvent(StepDisk, from + span * 0.5 * i / chunks, "Gravando no disco"));
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
                    progress.Report(new ProgressEvent(StepDisk, from + span * (0.5 + 0.5 * i++ / chunks), "Lendo do disco"));
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

/// <summary>Fixa a thread atual num núcleo lógico (Windows).</summary>
internal static class CoreAffinity
{
    [DllImport("kernel32.dll")]
    private static extern UIntPtr SetThreadAffinityMask(IntPtr thread, UIntPtr mask);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();

    public static void PinCurrentThread(int logicalCore)
    {
        if (logicalCore is < 0 or > 63) return;
        try { SetThreadAffinityMask(GetCurrentThread(), (UIntPtr)(1UL << logicalCore)); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // fora do Windows: roda sem fixar o núcleo
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
