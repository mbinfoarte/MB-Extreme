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
    int Passes = 1,
    double DiskRandReadMBps = 0,
    double DiskRandQ8Iops = 0,
    double DiskRandWriteMBps = 0,
    double DiskAccessUs = 0,
    double AiGenerateTps = 0,
    double AiPromptTps = 0,
    double GameLow1Fps = 0,
    IReadOnlyList<GpuSceneStat>? GpuStats = null,
    bool FocusLost = false,
    IReadOnlyList<CategoryScore>? Categories = null,
    int Version = 0,
    double RtOnFps = 0,
    double RtOffFps = 0,
    string? RtNote = null,
    IReadOnlyList<UpscaleRun>? Upscaling = null,
    string? UpscaleNote = null);

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
        { GpuWindowBench.Reflections, GpuWindowBench.Geometry, GpuWindowBench.Studio, GpuWindowBench.Game, GpuWindowBench.ResolutionLimit, Dx12RayBench.Step, FsrBench.Step };

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

        void Report(string step, double fraction, string message, string? detail = null, int pass = 0, int ofPasses = 0)
        {
            int index = Math.Max(0, Array.IndexOf(steps, step));
            fraction = Math.Clamp(fraction, 0, 1);
            progress.Report(new ProgressEvent(step, 100.0 * (index + fraction) / total, message)
                { StepPercent = fraction * 100, Detail = detail, Pass = pass, Passes = ofPasses > 0 ? ofPasses : passes });
        }

        // resultado da etapa (aparece na lista de etapas ao lado)
        void ReportResult(string step, string result)
        {
            int index = Math.Max(0, Array.IndexOf(steps, step));
            progress.Report(new ProgressEvent(step, 100.0 * (index + 1) / total, result) { StepPercent = 100, Result = result, Passes = passes });
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
            ReportResult(StepSingle, $"{BenchmarkScoring.Cpu1(tasksSingle):N0} pts");

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
            ReportResult(StepMulti, $"{BenchmarkScoring.CpuN(tasksMulti):N0} pts · {tasksMulti[0].Rate / Math.Max(0.001, tasksSingle[0].Rate):0.0}x");
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
            ReportResult(StepMemory, $"{BenchmarkScoring.Ram(read, write, copy, latency):N0} pts · {read:0.#} GB/s");
        }

        // ---- armazenamento
        double diskWrite = 0, diskRead = 0;
        DiskNumbers? disk = null;
        if (options.Disk)
        {
            Report(StepDisk, 0, $"Gravando e lendo um arquivo temporário de {profile.DiskMegabytes} MB", "Gravação");
            disk = DiskPhase(options.DiskDirectory ?? diskDirectory ?? Path.GetTempPath(), profile.DiskMegabytes,
                new SyncProgress(e => Report(StepDisk, e.Percent / 100.0, e.Message, e.Message)), 0, 100, token, Math.Max(400, ms / 2));
            (diskWrite, diskRead) = (disk.WriteMBps, disk.ReadMBps);
            ReportResult(StepDisk, $"{disk.ReadMBps:N0} MB/s leitura · {disk.RandQ8Iops:N0} IOPS");
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
                    int partIndex = part switch { AiBenchmark.PartGenerate => 0, AiBenchmark.PartPrompt => 1, AiBenchmark.PartFp32 => 2, AiBenchmark.PartInt8 => 3, _ => 4 };
                    Report(StepAi, (pass + (partIndex + f) / 5.0) / passes, part, part, pass + 1);
                }, gpu));
            }
            double gpuGflops = aiRuns.Max(r => r.GpuFp32Gflops);
            double fp32 = Median(aiRuns.Select(r => r.CpuFp32Gflops).ToList());
            double int8 = Median(aiRuns.Select(r => r.CpuInt8Gops).ToList());
            double gen = Median(aiRuns.Select(r => r.GenerationTokensPerSec).ToList());
            double prompt = Median(aiRuns.Select(r => r.PromptTokensPerSec).ToList());
            ai = new AiResult(fp32, int8, gpuGflops, AiBenchmark.Score(fp32, int8, gpuGflops), gen, prompt);
            ReportResult(StepAi, $"{BenchmarkScoring.Ai(gen, prompt, fp32, int8, gpuGflops):N0} pts · {gen:0} tokens/s");
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
            ReportResult(StepDaily, $"{daily.Score:N0} pts · {tasks.Sum(t => t.Seconds):0.0} s no total");
        }

        // ---- placa de vídeo (janela 3D separada)
        string? gpuName = null;
        int gpuScore = 0;
        IReadOnlyList<GpuSceneResult>? scenes = null;
        GpuWindowResult? window = null;
        RayTracingResult? rt = null;
        IReadOnlyList<UpscaleRun>? upscale = null;
        string? upscaleNote = null;
        if (options.Gpu3D)
        {
            Report(GpuWindowBench.Reflections, 0, "Abrindo a janela 3D");
            try
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("o teste 3D precisa do Windows (Direct3D 11)");
                var limit = LimitResolutions(options).Select(r => (r.Width, r.Height)).ToList();
                window = GpuWindowBench.Run(options.GpuWidth, options.GpuHeight, profile.GpuSecondsPerScene, profile.StudioSamples, limit,
                    e =>
                    {
                        Report(e.Step, e.Fraction, e.Fps > 0 ? $"{e.Step}: {e.Fps:0} FPS" : e.Step, e.Fps > 0 ? $"FPS atual: {e.Fps:0}" : null, e.Pass, e.Passes);
                    }, token, passes: passes);
                gpuName = window.GpuName;
                long pixels = (long)options.GpuWidth * options.GpuHeight;
                ReportResult(GpuWindowBench.Reflections, $"{window.ReflectionsFps:0.0} FPS · 1% low {window.Reflections?.Low1Fps ?? 0:0.0}");
                ReportResult(GpuWindowBench.Geometry, $"{window.GeometryFps:0.0} FPS · 1% low {window.Geometry?.Low1Fps ?? 0:0.0}");
                ReportResult(GpuWindowBench.Studio, $"{window.StudioMsps:N0} M amostras/s");
                ReportResult(GpuWindowBench.Game, $"{window.GameFps:0.0} FPS · 1% low {window.Game?.Low1Fps ?? 0:0.0}");
                if (window.Resolutions.Count > 0)
                    ReportResult(GpuWindowBench.ResolutionLimit, window.Max60 is { } max ? $"60+ FPS até {max.Label}" : "abaixo de 60 FPS");
                gpuScore = BenchmarkScoring.Gpu(window.ReflectionsFps, window.GeometryFps, window.StudioMsps, pixels);
                scenes = new[]
                {
                    new GpuSceneResult("Reflexos", window.ReflectionsFps),
                    new GpuSceneResult("Geometria", window.GeometryFps),
                    new GpuSceneResult("Estúdio (render em blocos)", window.StudioMsps, "M amostras/s"),
                    new GpuSceneResult("Simulação de jogo", window.GameFps),
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Sem Direct3D 11 (máquina virtual, driver antigo, placa reiniciada): o resto do resultado continua valendo.
                gpuName = "GPU 3D indisponível: " + ex.Message;
            }

            // ray tracing em DirectX 12 (janela própria): placas sem DXR 1.1 ficam com o aviso e o resto continua valendo
            Report(Dx12RayBench.Step, 0, "Abrindo o DirectX 12");
            try
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("o ray tracing precisa do Windows (DirectX 12)");
                // uma passada (desligado e ligado na mesma janela): é uma comparação, não precisa de mediana
                rt = Dx12RayBench.Run(options.GpuWidth, options.GpuHeight, profile.GpuSecondsPerScene, 0, 1,
                    e => Report(e.Step, e.Fraction, e.Fps > 0 ? $"{e.Step}: {e.Fps:0} FPS" : e.Step, e.Fps > 0 ? $"FPS atual: {e.Fps:0}" : null, e.Pass, e.Passes), token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                rt = new RayTracingResult(gpuName ?? "", null, null, "não rodou: " + ex.Message);
            }
            ReportResult(Dx12RayBench.Step, rt.Unsupported is { } why ? "sem ray tracing · " + why
                : $"ligado {rt.OnFps:0.0} FPS · desligado {rt.OffFps:0.0} FPS · custo {rt.CostPercent:0}%");
            if (rt.Unsupported is null)
                scenes = (scenes ?? Array.Empty<GpuSceneResult>()).Concat(new[]
                {
                    new GpuSceneResult("Ray tracing ligado (DX12)", rt.OnFps),
                    new GpuSceneResult("Ray tracing desligado (DX12)", rt.OffFps),
                }).ToList();

            // AMD FSR (upscaling e geração de quadros) na mesma arena com RT ligado: precisa do ray tracing funcionando
            Report(FsrBench.Step, 0, "Preparando o AMD FSR");
            if (rt.Unsupported is not null) upscaleNote = "precisa do ray tracing (DirectX 12)";
            else
            {
                try
                {
                    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("o FSR precisa do Windows (DirectX 12)");
                    string folder = FsrAddon.Folder;
                    if (!FsrAddon.IsReady(folder))
                    {
                        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
                        folder = FsrAddon.EnsureAsync(http, f => Report(FsrBench.Step, f * 0.1, $"Baixando o AMD FSR: {f:P0} de {FsrAddon.ZipBytes / 1_000_000} MB (só na primeira vez)"), token)
                            .GetAwaiter().GetResult();
                    }
                    upscale = FsrBench.Run(options.GpuWidth, options.GpuHeight, BenchmarkModes.FsrSecondsPerMode(profile), folder,
                        e => Report(e.Step, 0.1 + e.Fraction * 0.9, e.Fps > 0 ? $"{e.Step}: {e.Fps:0} FPS" : e.Step, e.Fps > 0 ? $"FPS atual: {e.Fps:0}" : null, 1, 1), token);
                    if (upscale.All(u => !u.Ok)) upscaleNote = upscale.FirstOrDefault()?.Error;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    upscaleNote = "não rodou: " + ex.Message;
                }
            }
            var good = upscale?.Where(u => u.Ok).ToList() ?? new List<UpscaleRun>();
            ReportResult(FsrBench.Step, good.Count == 0 ? "sem FSR · " + (upscaleNote ?? "não rodou")
                : string.Join(" · ", good.Select(u => $"{u.Title.Replace("FSR ", "")} {u.DisplayedFps:0} FPS")));
            if (good.Count > 0)
                scenes = (scenes ?? Array.Empty<GpuSceneResult>()).Concat(good.Select(u => new GpuSceneResult(u.Title, u.DisplayedFps))).ToList();
        }

        progress.Report(new ProgressEvent(steps.Length > 0 ? steps[^1] : StepDisk, 100, "Concluído") { StepPercent = 100, Passes = passes });
        int cpuSingleScore = BenchmarkScoring.Cpu1(tasksSingle);
        int cpuMultiScore = BenchmarkScoring.CpuN(tasksMulti);
        var result = new BenchmarkResult(DateTime.Now, threads, single, multi, copy, diskWrite, diskRead, 0,
            simd, crypto, compress, read, write, latency, curve, options.Label, gpuName, gpuScore, scenes,
            Mode: profile.Name, Resolution: options.Gpu3D ? $"{options.GpuWidth}×{options.GpuHeight}" : null,
            DiskName: options.Disk ? options.DiskName : null,
            CpuSingleScore: cpuSingleScore, CpuMultiScore: cpuMultiScore, CpuTasksSingle: tasksSingle, CpuTasksMulti: tasksMulti,
            AiCpuGflops: ai?.CpuFp32Gflops ?? 0, AiInt8Gops: ai?.CpuInt8Gops ?? 0, AiGpuGflops: ai?.GpuFp32Gflops ?? 0, AiScore: ai?.Score ?? 0,
            DailyTasks: daily?.Tasks, DailyScore: daily?.Score ?? 0,
            ReflectionsFps: window?.ReflectionsFps ?? 0, GeometryFps: window?.GeometryFps ?? 0, StudioMsps: window?.StudioMsps ?? 0,
            GameFps: window?.GameFps ?? 0, ResolutionLimit: window?.Resolutions, Passes: passes,
            DiskRandReadMBps: disk?.RandReadMBps ?? 0, DiskRandQ8Iops: disk?.RandQ8Iops ?? 0, DiskRandWriteMBps: disk?.RandWriteMBps ?? 0, DiskAccessUs: disk?.AccessMicroseconds ?? 0,
            AiGenerateTps: ai?.GenerationTokensPerSec ?? 0, AiPromptTps: ai?.PromptTokensPerSec ?? 0, GameLow1Fps: window?.Game?.Low1Fps ?? 0,
            GpuStats: window is null && rt?.On is null ? null : new[] { window?.Reflections, window?.Geometry, window?.Game, rt?.On, rt?.Off }
                .Concat(upscale?.Where(u => u.Ok).Select(u => new GpuSceneStat(u.Title, u.DisplayedFps, u.Low1Fps, u.Low01Fps, u.FrameMs)) ?? Array.Empty<GpuSceneStat>())
                .Where(x => x is not null).Select(x => x!).ToList(),
            FocusLost: window?.FocusLost ?? false, Version: BenchmarkScoring.Version,
            RtOnFps: rt?.OnFps ?? 0, RtOffFps: rt?.OffFps ?? 0, RtNote: rt?.Unsupported,
            Upscaling: upscale?.Select(u => u with { FrameMs = Array.Empty<double>() }).ToList(), UpscaleNote: upscaleNote);
        var categories = BenchmarkScoring.For(result);
        return result with { Categories = categories, Score = BenchmarkScoring.Overall(categories) };
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

    /// <summary>Resultado do disco: sequencial, aleatório 4K (1 e 8 filas), gravação aleatória e tempo de acesso.</summary>
    public sealed record DiskNumbers(double WriteMBps, double ReadMBps, double RandReadMBps = 0, double RandQ8Iops = 0, double RandWriteMBps = 0, double AccessMicroseconds = 0);

    // FILE_FLAG_NO_BUFFERING: lê direto do disco, sem o cache do Windows (exige buffers alinhados de 4 KB)
    private const FileOptions NoBuffering = (FileOptions)0x20000000;

    private static unsafe DiskNumbers DiskPhase(string directory, int megabytes, IProgress<ProgressEvent> progress, double from, double span, CancellationToken token, int randomMs = 1000)
    {
        const int chunk = 4 * 1024 * 1024;
        int chunks = Math.Max(1, megabytes / 4);
        int diskMegabytes = chunks * 4;
        bool ownFolder = !Directory.Exists(directory);
        if (ownFolder) Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "mbextreme-bench-" + Guid.NewGuid().ToString("N") + ".tmp");
        bool windows = OperatingSystem.IsWindows();
        byte* big = (byte*)NativeMemory.AlignedAlloc(chunk, 4096);
        try
        {
            var data = new Span<byte>(big, chunk);
            new Random(7).NextBytes(data);
            void Report(double f, string message) => progress.Report(new ProgressEvent(StepDisk, from + span * f, message));

            // 1) gravação sequencial (40% da etapa)
            var watch = Stopwatch.StartNew();
            using (var handle = File.OpenHandle(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, FileOptions.WriteThrough | (windows ? NoBuffering : FileOptions.None)))
            {
                for (int i = 0; i < chunks; i++)
                {
                    token.ThrowIfCancellationRequested();
                    RandomAccess.Write(handle, data, (long)i * chunk);
                    Report(0.4 * i / chunks, "Gravação sequencial");
                }
                RandomAccess.FlushToDisk(handle);
            }
            double write = diskMegabytes / Math.Max(watch.Elapsed.TotalSeconds, 0.001);

            // 2) leitura sequencial sem cache (20%)
            watch.Restart();
            using (var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, windows ? NoBuffering : FileOptions.SequentialScan))
            {
                for (int i = 0; i < chunks; i++)
                {
                    token.ThrowIfCancellationRequested();
                    RandomAccess.Read(handle, data, (long)i * chunk);
                    Report(0.4 + 0.2 * i / chunks, "Leitura sequencial");
                }
            }
            double read = diskMegabytes / Math.Max(watch.Elapsed.TotalSeconds, 0.001);

            // 3) aleatório 4K: leitura com 1 fila, leitura com 8 filas e gravação com 1 fila
            long blocks = (long)diskMegabytes * 1024 * 1024 / 4096;
            (double Ops, double Seconds) Random4K(int queues, bool writes, double f0, double f1, string label)
            {
                long total = 0;
                var w = Stopwatch.StartNew();
                using var handle = File.OpenHandle(path, FileMode.Open, writes ? FileAccess.ReadWrite : FileAccess.Read, FileShare.ReadWrite,
                    (windows ? NoBuffering : FileOptions.None) | (writes ? FileOptions.WriteThrough : FileOptions.None) | FileOptions.RandomAccess);
                var counts = new long[queues];
                Parallel.For(0, queues, new ParallelOptions { MaxDegreeOfParallelism = queues }, q =>
                {
                    byte* buffer = (byte*)NativeMemory.AlignedAlloc(4096, 4096);
                    try
                    {
                        var span4k = new Span<byte>(buffer, 4096);
                        span4k.Fill((byte)q);
                        var rng = new Random(31 + q);
                        long n = 0;
                        while (w.ElapsedMilliseconds < randomMs && !token.IsCancellationRequested)
                        {
                            long offset = rng.NextInt64(0, blocks) * 4096;
                            if (writes) RandomAccess.Write(handle, (ReadOnlySpan<byte>)span4k, offset);
                            else RandomAccess.Read(handle, span4k, offset);
                            n++;
                            if (q == 0 && (n & 63) == 0) Report(f0 + (f1 - f0) * Math.Min(1, w.ElapsedMilliseconds / (double)randomMs), label);
                        }
                        counts[q] = n;
                    }
                    finally
                    {
                        NativeMemory.AlignedFree(buffer);
                    }
                });
                token.ThrowIfCancellationRequested();
                total = counts.Sum();
                return (total, w.Elapsed.TotalSeconds);
            }

            var (r1, t1) = Random4K(1, false, 0.6, 0.73, "Leitura aleatória 4K (1 fila)");
            var (r8, t8) = Random4K(8, false, 0.73, 0.86, "Leitura aleatória 4K (8 filas)");
            var (w1, tw) = Random4K(1, true, 0.86, 1.0, "Gravação aleatória 4K (1 fila)");
            double randRead = r1 * 4096 / Math.Max(t1, 0.001) / 1e6;
            double iops = r8 / Math.Max(t8, 0.001);
            double randWrite = w1 * 4096 / Math.Max(tw, 0.001) / 1e6;
            double access = r1 > 0 ? t1 / r1 * 1e6 : 0;
            return new DiskNumbers(write, read, randRead, iops, randWrite, access);
        }
        finally
        {
            NativeMemory.AlignedFree(big);
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
