using System.Diagnostics;
using System.IO.Compression;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using MBExtreme.Core.Progress;

namespace MBExtreme.Core.Benchmark;

public sealed record LatencyPoint(int SizeKb, double Nanoseconds);

/// <summary>Uma cena 3D. <see cref="Unit"/> vazio quer dizer quadros por segundo; o cálculo usa GFLOPS e o render do estúdio, milhões de amostras por segundo.</summary>
public sealed record GpuSceneResult(string Scene, double Fps, string? Unit = null)
{
    public string Display => $"{Fps:0.#} {Unit ?? (Scene.Contains("GFLOPS") ? "GFLOPS" : "FPS")}";
}

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
    IReadOnlyList<GpuSceneResult>? GpuScenes = null,
    int RenderSingleScore = 0,
    int RenderMultiScore = 0,
    IReadOnlyList<MBExtreme.Core.Fps.SensorTrack>? Monitoring = null,
    IReadOnlyList<PhaseMark>? Phases = null,
    string? Mode = null,
    string? Resolution = null,
    string? DiskName = null,
    int CpuSingleScore = 0,
    int CpuMultiScore = 0,
    IReadOnlyList<CpuTaskResult>? CpuTasksSingle = null,
    IReadOnlyList<CpuTaskResult>? CpuTasksMulti = null,
    double AiCpuGflops = 0,
    double AiInt8Gops = 0,
    double AiGpuGflops = 0,
    int AiScore = 0,
    IReadOnlyList<DailyTask>? DailyTasks = null,
    int DailyScore = 0,
    double ReflectionsFps = 0,
    double GeometryFps = 0,
    double StudioMsps = 0,
    double GameFps = 0,
    IReadOnlyList<ResolutionFps>? ResolutionLimit = null,
    int Passes = 1);

/// <summary>Em que segundo do teste começou cada etapa (para marcar no gráfico de monitoramento).</summary>
public sealed record PhaseMark(string Name, double StartSeconds);

/// <summary>
/// O que medir e como. <paramref name="Mode"/> manda na duração e nas passadas (Quick continua valendo para quem chama sem modo);
/// <paramref name="DiskDirectory"/> escolhe o disco; a resolução vale para as cenas 3D e <paramref name="Resolutions"/> para o limite por resolução.
/// </summary>
public sealed record BenchmarkOptions(bool Disk = true, bool Gpu3D = false, bool Quick = false, string? Label = null,
    BenchmarkMode? Mode = null, string? DiskDirectory = null, string? DiskName = null, int GpuWidth = 1920, int GpuHeight = 1080,
    bool Cpu = true, bool Memory = true, bool Ai = true, bool Daily = true, IReadOnlyList<BenchmarkResolution>? Resolutions = null, string? FfmpegPath = null);

public sealed record CoreScore(int Core, double Mops);

/// <summary>
/// Benchmark completo em etapas: processador (1 núcleo e todos, com tarefas reais), memória, armazenamento,
/// inteligência artificial, uso do dia a dia (pesado) e a placa de vídeo numa janela 3D separada (reflexos, geometria,
/// estúdio, simulação de jogo e limite por resolução). No modo Preciso as medições do processador, memória, IA e dia a dia
/// rodam 3 vezes e vale a mediana.
/// </summary>
public static class BenchmarkService
{
    public const string StepSingle = "Processador · 1 núcleo";
    public const string StepMulti = "Processador · todos os núcleos";
    public const string StepMemory = "Memória RAM";
    public const string StepDisk = "Armazenamento";
    public const string StepAi = "Inteligência artificial";
    public const string StepDaily = "Uso do dia a dia (pesado)";
    public const string StepGpu = GpuWindowBench.Reflections;

    /// <summary>Etapas da placa de vídeo, na ordem em que a janela 3D roda.</summary>
    public static readonly string[] GpuSteps =
        { GpuWindowBench.Reflections, GpuWindowBench.Geometry, GpuWindowBench.Studio, GpuWindowBench.Game, GpuWindowBench.ResolutionLimit };

    public static string[] StepsFor(BenchmarkOptions options)
    {
        var steps = new List<string>();
        if (options.Cpu) steps.AddRange(new[] { StepSingle, StepMulti });
        if (options.Memory) steps.Add(StepMemory);
        if (options.Disk) steps.Add(StepDisk);
        if (options.Cpu && options.Ai) steps.Add(StepAi);
        if (options.Cpu && options.Daily) steps.Add(StepDaily);
        if (options.Gpu3D) steps.AddRange(GpuSteps);
        return steps.ToArray();
    }

    /// <summary>Resoluções do limite por resolução: as do monitor até a escolhida para as cenas.</summary>
    public static IReadOnlyList<BenchmarkResolution> LimitResolutions(BenchmarkOptions options)
    {
        var list = (options.Resolutions ?? new[] { BenchmarkResolutions.Hd, BenchmarkResolutions.FullHd })
            .Where(r => (long)r.Width * r.Height <= Math.Max((long)options.GpuWidth * options.GpuHeight, BenchmarkResolutions.FullHd.Pixels))
            .OrderBy(r => r.Pixels).ToList();
        return list.Count > 0 ? list : new[] { BenchmarkResolutions.FullHd };
    }

    private static double _sink;

    public static BenchmarkResult Run(IProgress<ProgressEvent> progress, CancellationToken token, BenchmarkOptions? options = null, string? diskDirectory = null,
        Action<byte[], int, int>? gpuPreview = null)
    {
        options ??= new BenchmarkOptions();
        var profile = BenchmarkModes.For(options);
        var steps = StepsFor(options);
        int total = Math.Max(1, steps.Length);
        int passes = profile.Passes;
        int ms = profile.PhaseMs;
        int taskMs = Math.Max(150, ms / 2);
        int threads = Environment.ProcessorCount;

        void Report(string step, double fraction, string message, string? detail = null, int pass = 0)
        {
            int index = Math.Max(0, Array.IndexOf(steps, step));
            fraction = Math.Clamp(fraction, 0, 1);
            progress.Report(new ProgressEvent(step, 100.0 * (index + fraction) / total, message)
                { StepPercent = fraction * 100, Detail = detail, Pass = pass, Passes = passes });
        }

        // ---- processador: 1 núcleo e todos
        IReadOnlyList<CpuTaskResult>? tasksSingle = null, tasksMulti = null;
        double single = 0, multi = 0, simd = 0, crypto = 0, compress = 0;
        if (options.Cpu)
        {
            var runs = new List<IReadOnlyList<CpuTaskResult>>();
            for (int p = 0; p < passes; p++)
            {
                int pass = p;
                runs.Add(CpuTasks.Run(1, taskMs, token, (t, f) =>
                    Report(StepSingle, (pass + (t + f) / CpuTasks.Tasks.Count) / passes, CpuTasks.Tasks[t].Name + " em 1 linha de execução", CpuTasks.Tasks[t].Name, pass + 1)));
            }
            tasksSingle = MedianTasks(runs);
            single = tasksSingle[0].Rate;

            runs.Clear();
            var simdRuns = new List<double>();
            for (int p = 0; p < passes; p++)
            {
                int pass = p;
                runs.Add(CpuTasks.Run(threads, taskMs, token, (t, f) =>
                    Report(StepMulti, (pass + (t + f) / (CpuTasks.Tasks.Count + 1)) / passes, $"{CpuTasks.Tasks[t].Name} em {threads} linhas de execução", CpuTasks.Tasks[t].Name, pass + 1)));
                Report(StepMulti, (pass + CpuTasks.Tasks.Count / (CpuTasks.Tasks.Count + 1.0)) / passes, "Instruções vetoriais (SIMD)", "Vetorial", pass + 1);
                simdRuns.Add(SimdPhase(taskMs, token));
            }
            tasksMulti = MedianTasks(runs);
            multi = tasksMulti[0].Rate;
            simd = Median(simdRuns);
            compress = tasksMulti.First(t => t.Name == "Compressão").Rate;
            crypto = tasksMulti.First(t => t.Name == "Criptografia").Rate;
        }

        // ---- memória
        double read = 0, write = 0, copy = 0, latency = 0;
        IReadOnlyList<LatencyPoint> curve = Array.Empty<LatencyPoint>();
        if (options.Memory)
        {
            var bandwidth = new List<(double Read, double Write, double Copy)>();
            var latencies = new List<IReadOnlyList<LatencyPoint>>();
            for (int p = 0; p < passes; p++)
            {
                Report(StepMemory, (p + 0.05) / passes, "Banda da memória (leitura, escrita e cópia)", "Banda", p + 1);
                bandwidth.Add(MemoryBandwidthPhase(ms, token));
                Report(StepMemory, (p + 0.55) / passes, "Latência da memória (cache L1, L2, L3 e RAM)", "Latência", p + 1);
                latencies.Add(MemoryLatencyCurve(profile.QuickLatency, token));
            }
            var mid = bandwidth.OrderBy(b => b.Copy).ElementAt(bandwidth.Count / 2);
            (read, write, copy) = mid;
            curve = latencies.OrderBy(c => c.Count > 0 ? c[^1].Nanoseconds : 0).ElementAt(latencies.Count / 2);
            latency = curve.Count > 0 ? curve[^1].Nanoseconds : 0;
        }

        // ---- armazenamento
        double diskWrite = 0, diskRead = 0;
        if (options.Disk)
        {
            Report(StepDisk, 0, $"Gravando e lendo um arquivo temporário de {profile.DiskMegabytes} MB", "Gravação");
            (diskWrite, diskRead) = DiskPhase(options.DiskDirectory ?? diskDirectory ?? Path.GetTempPath(), profile.DiskMegabytes, new SyncProgress(e => Report(StepDisk, e.Percent / 100.0, e.Message, e.Message.Contains("Lendo") ? "Leitura" : "Gravação")), 0, 100, token);
        }

        // ---- inteligência artificial
        AiResult? ai = null;
        if (options.Cpu && options.Ai)
        {
            var aiRuns = new List<AiResult>();
            for (int p = 0; p < passes; p++)
            {
                int pass = p;
                Func<CancellationToken, double>? gpu = null;
                if (pass == 0 && options.Gpu3D && OperatingSystem.IsWindows()) gpu = t => GpuWindowBench.GemmGflops(Math.Max(1000, ms), t);
                aiRuns.Add(AiBenchmark.Run(taskMs, token, (part, f) =>
                {
                    int partIndex = part == AiBenchmark.PartFp32 ? 0 : part == AiBenchmark.PartInt8 ? 1 : 2;
                    Report(StepAi, (pass + (partIndex + f) / 3.0) / passes, part, part, pass + 1);
                }, gpu));
            }
            double gpuGflops = aiRuns.Max(r => r.GpuFp32Gflops);
            double fp32 = Median(aiRuns.Select(r => r.CpuFp32Gflops).ToList());
            double int8 = Median(aiRuns.Select(r => r.CpuInt8Gops).ToList());
            ai = new AiResult(fp32, int8, gpuGflops, AiBenchmark.Score(fp32, int8, gpuGflops));
        }

        // ---- uso do dia a dia
        DailyResult? daily = null;
        if (options.Cpu && options.Daily)
        {
            string? ffmpeg = options.FfmpegPath;
            if (ffmpeg is null && OperatingSystem.IsWindows()) ffmpeg = MBExtreme.Core.Recording.FfmpegRecorder.Locate();
            var dailyRuns = new List<DailyResult>();
            for (int p = 0; p < passes; p++)
            {
                int pass = p;
                dailyRuns.Add(DailyBenchmark.Run(token, (task, f) =>
                    Report(StepDaily, (pass + f) / passes, task, task, pass + 1), ffmpeg));
            }
            var tasks = DailyBenchmark.Tasks.Select(t =>
            {
                var all = dailyRuns.Select(r => r.Tasks.FirstOrDefault(x => x.Name == t.Name)).Where(x => x is not null).Select(x => x!).ToList();
                var measured = all.Where(x => x.Seconds > 0).ToList();
                return measured.Count == 0 ? all.FirstOrDefault() ?? new DailyTask(t.Name, 0) : new DailyTask(t.Name, Median(measured.Select(x => x.Seconds).ToList()));
            }).ToList();
            daily = new DailyResult(tasks, DailyBenchmark.Score(tasks));
        }

        // ---- placa de vídeo (janela 3D separada)
        string? gpuName = null;
        int gpuScore = 0;
        IReadOnlyList<GpuSceneResult>? scenes = null;
        GpuWindowResult? window = null;
        if (options.Gpu3D)
        {
            Report(GpuWindowBench.Reflections, 0, "Abrindo a janela 3D");
            try
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("o teste 3D precisa do Windows (Direct3D 11)");
                var limit = LimitResolutions(options).Select(r => (r.Width, r.Height)).ToList();
                window = GpuWindowBench.Run(options.GpuWidth, options.GpuHeight, profile.GpuSecondsPerScene, profile.StudioSamples, limit,
                    (step, f, fps) => Report(step, f, fps > 0 ? $"{step}: {fps:0} FPS" : step, fps > 0 ? $"{fps:0} FPS" : null), token);
                gpuName = window.GpuName;
                gpuScore = window.Score;
                scenes = new[]
                {
                    new GpuSceneResult("Reflexos", window.ReflectionsFps),
                    new GpuSceneResult("Geometria", window.GeometryFps),
                    new GpuSceneResult("Estúdio (path tracing)", window.StudioMsps, "M amostras/s"),
                    new GpuSceneResult("Simulação de jogo", window.GameFps),
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Sem Direct3D 11 (máquina virtual, driver antigo, placa reiniciada): o resto do resultado continua valendo.
                gpuName = "GPU 3D indisponível: " + ex.Message;
            }
        }

        progress.Report(new ProgressEvent(steps.Length > 0 ? steps[^1] : StepDisk, 100, "Concluído") { StepPercent = 100, Passes = passes });
        int cpuSingleScore = tasksSingle is null ? 0 : CpuTasks.Score(tasksSingle);
        int cpuMultiScore = tasksMulti is null ? 0 : CpuTasks.Score(tasksMulti);
        int score = ComputeScore(single, multi, copy, diskWrite, diskRead) + ExtraScore(simd, crypto, compress, latency)
                    + (ai?.Score ?? 0) / 4 + (daily?.Score ?? 0) / 2;
        return new BenchmarkResult(DateTime.Now, threads, single, multi, copy, diskWrite, diskRead, score,
            simd, crypto, compress, read, write, latency, curve, options.Label, gpuName, gpuScore, scenes,
            Mode: profile.Name, Resolution: options.Gpu3D ? $"{options.GpuWidth}×{options.GpuHeight}" : null,
            DiskName: options.Disk ? options.DiskName : null,
            CpuSingleScore: cpuSingleScore, CpuMultiScore: cpuMultiScore, CpuTasksSingle: tasksSingle, CpuTasksMulti: tasksMulti,
            AiCpuGflops: ai?.CpuFp32Gflops ?? 0, AiInt8Gops: ai?.CpuInt8Gops ?? 0, AiGpuGflops: ai?.GpuFp32Gflops ?? 0, AiScore: ai?.Score ?? 0,
            DailyTasks: daily?.Tasks, DailyScore: daily?.Score ?? 0,
            ReflectionsFps: window?.ReflectionsFps ?? 0, GeometryFps: window?.GeometryFps ?? 0, StudioMsps: window?.StudioMsps ?? 0,
            GameFps: window?.GameFps ?? 0, ResolutionLimit: window?.Resolutions, Passes: passes);
    }

    public static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToList();
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    private static IReadOnlyList<CpuTaskResult> MedianTasks(IReadOnlyList<IReadOnlyList<CpuTaskResult>> runs) =>
        runs[0].Select((t, i) => t with { Rate = Median(runs.Select(r => r[i].Rate).ToList()) }).ToList();

    /// <summary>Progresso que chama na hora (sem passar pela thread da interface), para repassar com a etapa certa.</summary>
    private sealed class SyncProgress(Action<ProgressEvent> report) : IProgress<ProgressEvent>
    {
        public void Report(ProgressEvent value) => report(value);
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

    private static (double WriteMBps, double ReadMBps) DiskPhase(string directory, int megabytes, IProgress<ProgressEvent> progress, double from, double span, CancellationToken token)
    {
        const int chunk = 4 * 1024 * 1024;
        int chunks = Math.Max(1, megabytes / 4);
        int diskMegabytes = chunks * 4;
        bool ownFolder = !Directory.Exists(directory);
        if (ownFolder) Directory.CreateDirectory(directory);
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

            double write = diskMegabytes / Math.Max(watch.Elapsed.TotalSeconds, 0.001);

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

            double read = diskMegabytes / Math.Max(watch.Elapsed.TotalSeconds, 0.001);
            return (write, read);
        }
        finally
        {
            try
            {
                File.Delete(path);
                if (ownFolder && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* arquivo temporário; o Windows limpa depois */ }
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
