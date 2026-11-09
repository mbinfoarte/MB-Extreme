using MBExtreme.Core.Benchmark;
using MBExtreme.Core.Progress;
using Xunit;

namespace MBExtreme.Core.Tests;

public class BenchmarkNovoTests
{
    [Fact]
    public void Modos_ExigemMaisACadaPasso()
    {
        var all = BenchmarkModes.All;
        Assert.Equal(new[] { "Rápido", "Padrão", "Preciso" }, all.Select(m => m.Name));
        for (int i = 1; i < all.Count; i++)
        {
            Assert.True(all[i].PhaseMs > all[i - 1].PhaseMs);
            Assert.True(all[i].DiskMegabytes > all[i - 1].DiskMegabytes);
            Assert.True(all[i].GpuSecondsPerScene > all[i - 1].GpuSecondsPerScene);
            Assert.True(all[i].StudioSamples > all[i - 1].StudioSamples);
        }
        var full = new BenchmarkOptions(Disk: true, Gpu3D: true);
        int quick = BenchmarkModes.EstimatedSeconds(full with { Mode = BenchmarkMode.Rapido });
        int normal = BenchmarkModes.EstimatedSeconds(full with { Mode = BenchmarkMode.Padrao });
        int precise = BenchmarkModes.EstimatedSeconds(full with { Mode = BenchmarkMode.Preciso });
        Assert.True(quick < normal && normal < precise);
        Assert.InRange(quick, 35, 110);
        Assert.InRange(precise, 250, 720);
        Assert.Equal(new[] { 1, 1, 3 }, all.Select(m => m.Passes));
    }

    [Fact]
    public void Modos_ChamadasAntigasContinuamValendo()
    {
        Assert.Equal(BenchmarkMode.Rapido, BenchmarkModes.For(new BenchmarkOptions(Quick: true)).Mode);
        Assert.Equal(BenchmarkMode.Padrao, BenchmarkModes.For(new BenchmarkOptions()).Mode);
        Assert.Equal(BenchmarkMode.Preciso, BenchmarkModes.For(new BenchmarkOptions(Quick: true, Mode: BenchmarkMode.Preciso)).Mode);
        Assert.Equal("~25 s", BenchmarkModes.Duration(25));
        Assert.Equal("~2 min", BenchmarkModes.Duration(125));
        Assert.Equal("~1 min 40 s", BenchmarkModes.Duration(100));
    }

    [Fact]
    public void Resolucoes_SegueOMonitor()
    {
        var fullHd = BenchmarkResolutions.For(1920, 1080);
        Assert.Equal(new[] { "HD 720p", "Full HD 1080p" }, fullHd.Select(r => r.Name));

        var qhd = BenchmarkResolutions.For(2560, 1440);
        Assert.Contains(BenchmarkResolutions.Qhd, qhd);
        Assert.DoesNotContain(BenchmarkResolutions.Uhd, qhd);

        var uhd = BenchmarkResolutions.For(3840, 2160);
        Assert.Equal(new[] { 720, 1080, 1440, 2160 }, uhd.Select(r => r.Height));

        var ultrawide = BenchmarkResolutions.For(3440, 1440);
        Assert.Contains(ultrawide, r => r.Width == 3440 && r.Name == "Nativa do monitor");
        Assert.Equal(ultrawide.OrderBy(r => r.Pixels), ultrawide);

        var notebook = BenchmarkResolutions.For(1366, 768);
        Assert.Equal(3, notebook.Count);
        Assert.Equal(BenchmarkResolutions.FullHd, BenchmarkResolutions.Suggested(notebook));

        // monitor desconhecido: as duas de sempre
        Assert.Equal(2, BenchmarkResolutions.For(0, 0).Count);
        Assert.Equal("Full HD 1080p (1920×1080)", BenchmarkResolutions.FullHd.Label);
    }

    [Fact]
    public void Discos_SistemaPrimeiroEEspaco()
    {
        var disks = BenchmarkDisks.Order(new[]
        {
            new BenchmarkDisk("E:\\", "Jogos", "NTFS", 900L << 30, 1000L << 30, false),
            new BenchmarkDisk("C:\\", "", "NTFS", 100L << 30, 500L << 30, true),
            new BenchmarkDisk("D:\\", "Dados", "NTFS", 1L << 30, 1000L << 30, false),
        });
        Assert.Equal(new[] { "C:\\", "D:\\", "E:\\" }, disks.Select(d => d.Root));
        Assert.Equal("Disco local (C:)", disks[0].Title);
        Assert.Equal("Jogos (E:)", disks[2].Title);
        Assert.Equal(80, disks[0].UsedPercent, 1);

        Assert.True(BenchmarkDisks.HasRoom(disks[0], 2048));
        Assert.False(BenchmarkDisks.HasRoom(disks[1], 128));

        Assert.Equal("C:\\Temp\\", BenchmarkDisks.DirectoryFor(disks[0] with { Root = Path.GetPathRoot("C:\\Temp\\")! }, "C:\\Temp\\"));
        Assert.EndsWith("MBExtreme-teste-de-disco", BenchmarkDisks.DirectoryFor(disks[2], "C:\\Temp\\"));
    }

    [Fact]
    public void Disco_GravaNaPastaEscolhidaEApagaTudo()
    {
        string root = Path.Combine(Path.GetTempPath(), "mbx-bench-" + Guid.NewGuid().ToString("N"));
        string folder = Path.Combine(root, "MBExtreme-teste-de-disco");
        Directory.CreateDirectory(root);
        try
        {
            var events = new List<ProgressEvent>();
            var result = BenchmarkService.Run(new SyncProgress(events), CancellationToken.None,
                new BenchmarkOptions(Disk: true, Mode: BenchmarkMode.Rapido, DiskDirectory: folder, DiskName: "Teste (T:)", Cpu: false, Memory: false));
            Assert.True(result.DiskWriteMBps > 0 && result.DiskReadMBps > 0);
            Assert.True(result.DiskRandReadMBps > 0 && result.DiskRandQ8Iops > 0 && result.DiskRandWriteMBps > 0 && result.DiskAccessUs > 0);
            Assert.Equal(3, result.Version);
            Assert.Contains(result.Categories!, c => c.Key == "disk");
            Assert.Equal(result.Categories!.Single().Score, result.Score);
            Assert.Equal("Rápido", result.Mode);
            Assert.Equal("Teste (T:)", result.DiskName);
            Assert.Null(result.Resolution);
            Assert.Equal(0, result.CpuSingleMops);
            Assert.False(Directory.Exists(folder));
            Assert.Contains(events, e => e.Message.Contains("128 MB"));
            Assert.Equal(new[] { BenchmarkService.StepDisk }, BenchmarkService.StepsFor(new BenchmarkOptions(Disk: true, Cpu: false, Memory: false)));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Gpu_NotaConvertidaPara1080p()
    {
        long p1080 = 1920L * 1080;
        long p4k = 3840L * 2160;
        int fullHd = Gpu3DBenchmark.Score(60, 100, 120, 5000, 400, p1080);
        // em 4K a placa faz 1/4 dos quadros nas cenas presas aos pixels: a nota deve ficar igual
        int uhd = Gpu3DBenchmark.Score(15, 100, 30, 5000, 400, p4k);
        Assert.Equal(fullHd, uhd);
        Assert.True(Gpu3DBenchmark.Score(60, 100, 120, 5000, 800, p1080) > fullHd);

        var scene = new GpuSceneResult("Estúdio 3D (path tracing)", 1234.5, "M amostras/s");
        Assert.Equal("1234.5 M amostras/s", scene.Display.Replace(',', '.'));
        Assert.EndsWith("FPS", new GpuSceneResult("Fractal", 60).Display);
        Assert.EndsWith("GFLOPS", new GpuSceneResult("Compute (GFLOPS)", 9000).Display);
    }

    [Fact]
    public void Dicas_PlanoBateriaEProgramas()
    {
        Assert.Equal("Equilibrado", BenchmarkAdvice.PowerPlanName("GUID do Esquema de Energia: 381b4222-f694-41f0-9685-ff5bb260df2e  (Equilibrado)"));
        Assert.Equal("Alto desempenho", BenchmarkAdvice.PowerPlanName("Power Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (Alto desempenho) *\r\n"));
        Assert.Null(BenchmarkAdvice.PowerPlanName(""));

        var tips = BenchmarkAdvice.Tips(laptop: true, onBattery: true, powerPlan: "Equilibrado", BenchmarkMode.Rapido, openWindows: 9);
        Assert.Equal("Ligue o carregador", tips[0].Title);
        Assert.True(tips[0].Important);
        Assert.Contains(tips, t => t.Title.Contains("Equilibrado") && t.Important);
        Assert.Contains(tips, t => t.Title.Contains("9 janelas"));
        Assert.Contains(tips, t => t.Title.Contains("Notebook"));
        Assert.Contains(tips, t => t.Title.Contains("Preciso"));

        var calm = BenchmarkAdvice.Tips(laptop: false, onBattery: false, powerPlan: "Alto desempenho", BenchmarkMode.Preciso);
        Assert.DoesNotContain(calm, t => t.Important);
        Assert.Contains(calm, t => t.Title == "Compare no mesmo modo");
    }

    [Fact]
    public void ComparacaoAutomatica_FrasesCertas()
    {
        BenchmarkResult R(int score, int minutes, string mode = "Padrão") =>
            new(new DateTime(2026, 11, 7, 10, 0, 0).AddMinutes(minutes), 8, 1, 1, 1, 0, 0, score, Mode: mode);

        var first = R(1000, 0);
        Assert.StartsWith("Primeira medição", BenchmarkAdvice.Compare(first, new[] { first }));

        var history = new[] { R(1000, 0), R(1040, 1), R(1100, 2) };
        Assert.StartsWith("Seu melhor resultado no modo Padrão", BenchmarkAdvice.Compare(history[2], history));
        var normal = R(1060, 3);
        Assert.StartsWith("Dentro do normal", BenchmarkAdvice.Compare(normal, history.Append(normal).ToList()));
        var low = R(800, 3);
        Assert.Contains("abaixo da sua média", BenchmarkAdvice.Compare(low, history.Append(low).ToList()));

        // outro modo não entra na conta quando há medições no mesmo modo
        var quick = R(5000, 4, "Rápido");
        Assert.Contains("abaixo da sua média no modo Padrão", BenchmarkAdvice.Compare(low, history.Append(quick).Append(low).ToList()));
        Assert.StartsWith("Medição parcial", BenchmarkAdvice.Compare(R(0, 5), history));
    }

    [Fact]
    public void Etapas_NaOrdemDoTesteCompleto()
    {
        var steps = BenchmarkService.StepsFor(new BenchmarkOptions(Disk: true, Gpu3D: true));
        Assert.Equal(new[]
        {
            "Processador · 1 núcleo", "Processador · todos os núcleos", "Memória RAM", "Armazenamento", "Inteligência artificial",
            "Uso do dia a dia (pesado)", "Cena 1 · Reflexos", "Cena 2 · Geometria", "Cena 3 · Estúdio (path tracing)", "Simulação de jogo", "Limite por resolução",
        }, steps);
        Assert.DoesNotContain(BenchmarkService.StepAi, BenchmarkService.StepsFor(new BenchmarkOptions(Cpu: false)));
        Assert.DoesNotContain(BenchmarkService.StepDaily, BenchmarkService.StepsFor(new BenchmarkOptions(Daily: false)));
        var options = new BenchmarkOptions(Gpu3D: true, Mode: BenchmarkMode.Padrao);
        Assert.All(steps, s => Assert.True(BenchmarkModes.StepSeconds(options, s) > 0));
    }

    [Fact]
    public void LimitePorResolucao_AteAEscolhida()
    {
        var monitor = BenchmarkResolutions.For(3840, 2160);
        var upTo1440 = BenchmarkService.LimitResolutions(new BenchmarkOptions(GpuWidth: 2560, GpuHeight: 1440, Resolutions: monitor));
        Assert.Equal(new[] { 720, 1080, 1440 }, upTo1440.Select(r => r.Height));
        var at720 = BenchmarkService.LimitResolutions(new BenchmarkOptions(GpuWidth: 1280, GpuHeight: 720, Resolutions: monitor));
        Assert.Equal(new[] { 720, 1080 }, at720.Select(r => r.Height)); // 1080p sempre entra como referência
        Assert.Equal(2, BenchmarkService.LimitResolutions(new BenchmarkOptions()).Count);

        GpuSceneStat Stat(double fps) => new("x", fps, fps * 0.8, fps * 0.6, Array.Empty<double>());
        var result = new GpuWindowResult("GPU", Stat(100), Stat(300), 500, Stat(90), new[] { new ResolutionFps(1280, 720, 150), new ResolutionFps(1920, 1080, 75), new ResolutionFps(2560, 1440, 48), new ResolutionFps(3840, 2160, 20) }, 0);
        Assert.Equal(new[] { "Competitivo 144+", "Fluido 60+", "Jogável 30+", "Abaixo de 30" }, result.Resolutions.Select(r => r.Tier));
        Assert.Equal(100, result.ReflectionsFps);
        Assert.Equal("1920×1080", result.Max60!.Label);
        Assert.Equal(GpuWindowBench.Score(100, 300, 500, 90, 1920L * 1080), GpuWindowBench.Score(25, 300, 500, 22.5, 3840L * 2160));
    }

    [Fact]
    public void Mediana_DasPassadas()
    {
        Assert.Equal(5, BenchmarkService.Median(new double[] { 9, 1, 5 }));
        Assert.Equal(4, BenchmarkService.Median(new double[] { 3, 5 }));
        Assert.Equal(0, BenchmarkService.Median(Array.Empty<double>()));
    }

    [Fact]
    public void Processador_TarefasReais()
    {
        var results = CpuTasks.Run(2, 60, CancellationToken.None);
        Assert.Equal(CpuTasks.Tasks.Select(t => t.Name), results.Select(r => r.Name));
        Assert.All(results, r => Assert.True(r.Rate > 0, r.Name));
        Assert.True(CpuTasks.Score(results) > 0);
        // exatamente na referência dá 1000
        Assert.Equal(1000, CpuTasks.Score(CpuTasks.Tasks.Select(t => new CpuTaskResult(t.Name, t.Reference, t.Unit)).ToList()));
    }

    [Fact]
    public void InteligenciaArtificial_FP32eINT8()
    {
        var parts = new List<string>();
        var ai = AiBenchmark.Run(80, CancellationToken.None, (p, _) => { if (!parts.Contains(p)) parts.Add(p); }, _ => 1234);
        Assert.True(ai.CpuFp32Gflops > 0 && ai.CpuInt8Gops > 0 && ai.GenerationTokensPerSec > 0 && ai.PromptTokensPerSec > 0);
        Assert.Equal(1234, ai.GpuFp32Gflops);
        Assert.Equal(new[] { AiBenchmark.PartGenerate, AiBenchmark.PartPrompt, AiBenchmark.PartFp32, AiBenchmark.PartInt8, AiBenchmark.PartGpu }, parts);
        Assert.InRange(AiBenchmark.TinyModel.Create().Parameters, 27_000_000, 30_000_000);
        var failed = AiBenchmark.Run(30, CancellationToken.None, null, _ => throw new InvalidOperationException("sem placa"));
        Assert.Equal(0, failed.GpuFp32Gflops);
    }

    [Fact]
    public void DiaADia_TarefasComTempo()
    {
        var seen = new List<string>();
        var daily = DailyBenchmark.Run(CancellationToken.None, (t, _) => { if (!seen.Contains(t)) seen.Add(t); }, ffmpeg: null);
        Assert.Equal(DailyBenchmark.Tasks.Select(t => t.Name), daily.Tasks.Select(t => t.Name));
        Assert.Equal(DailyBenchmark.Tasks.Select(t => t.Name), seen);
        var video = daily.Tasks.Single(t => t.Name == DailyBenchmark.Video);
        Assert.Equal(0, video.Seconds);
        Assert.Contains("FFmpeg", video.Note);
        Assert.All(daily.Tasks.Where(t => t.Name != DailyBenchmark.Video), t => Assert.True(t.Seconds > 0, t.Name));
        Assert.True(daily.Score > 0);
        Assert.Equal(1000, DailyBenchmark.Score(DailyBenchmark.Tasks.Select(t => new DailyTask(t.Name, t.Reference)).ToList()));
    }

    [Fact]
    public void Sombreadores_VemDentroDoPrograma()
    {
        var names = typeof(GpuWindowBench).Assembly.GetManifestResourceNames();
        foreach (string shader in new[] { "Common", "Reflections", "Geometry", "Studio", "Game", "AiGemm" })
            Assert.Contains("MBExtreme.Bench." + shader + ".hlsl", names);
    }

    [Fact]
    public void Notas_PorCategoriaEGeral()
    {
        var all = BenchmarkScoring.Categories.Select(c => new CategoryScore(c.Key, c.Name, 1000, c.Weight)).ToList();
        Assert.Equal(100, BenchmarkScoring.Categories.Sum(c => c.Weight));
        Assert.Equal(1000, BenchmarkScoring.Overall(all));
        // dobrar só a placa de vídeo (peso 20%) sobe a geral em 2^0,2
        var gpu2x = all.Select(c => c.Key == "gpu" ? c with { Score = 2000 } : c).ToList();
        Assert.Equal((int)Math.Round(1000 * Math.Pow(2, 0.2)), BenchmarkScoring.Overall(gpu2x));
        Assert.Equal(0, BenchmarkScoring.Overall(Array.Empty<CategoryScore>()));
        Assert.Equal(1000, BenchmarkScoring.Ram(30, 20, 15, 90));
        Assert.Equal(1000, BenchmarkScoring.Disk(2000, 1000, 40, 60000, 150, 100));
        Assert.Equal(1000, BenchmarkScoring.Game(350, 220, 1920L * 1080));
        Assert.Equal(1000, BenchmarkScoring.Game(87.5, 55, 3840L * 2160));
        Assert.Equal(14.4, BenchmarkScoring.Delta(1447, 1656), 1);
        Assert.Equal("Ganho expressivo de desempenho!", BenchmarkScoring.Verdict(14.4));
        Assert.StartsWith("Dentro da variação", BenchmarkScoring.Verdict(-1));

        var r = new BenchmarkResult(DateTime.Now, 16, 1, 1, 15, 1000, 2000, 0, MemoryReadGBps: 30, MemoryWriteGBps: 20, MemoryLatencyNs: 90,
            DiskRandReadMBps: 40, DiskRandQ8Iops: 60000, DiskRandWriteMBps: 150, DiskAccessUs: 100, DailyScore: 1500, Resolution: "1920×1080");
        var cats = BenchmarkScoring.For(r);
        Assert.Equal(new[] { "ram", "disk", "daily" }, cats.Select(c => c.Key));
        Assert.Equal(1000, cats[0].Score);
    }

    [Fact]
    public void Fonte_ConsumoERecomendacao()
    {
        var (peak, psu) = PowerAdvice.Estimate(120, 220);
        Assert.Equal(400, peak);
        Assert.Equal(650, psu); // 400 x 1,4 = 560 -> 650
        Assert.Equal(400, PowerAdvice.Estimate(0, 0).RecommendedWatts);
        Assert.Equal(1600, PowerAdvice.Estimate(900, 900).RecommendedWatts);
    }

    [Fact]
    public void Jogo_UnidadesAndamEBatem()
    {
        var sim = new ArmySim(400);
        var (x0, _) = sim.Position(0);
        for (int i = 0; i < 400; i++) sim.Step(1 / 60f, i / 60f);
        var (x1, _) = sim.Position(0);
        Assert.True(x1 > x0, "o exército amarelo avança para a direita");
        var buffer = new float[sim.Count * 4];
        sim.WriteUnits(buffer);
        Assert.Equal(0, buffer[3]);
        Assert.Equal(1, buffer[(sim.Count - 1) * 4 + 3]);
        Assert.True(sim.Collisions > 0, "os exércitos se encontram");
        Assert.Equal(ArmySim.MaxSparks * 4, sim.Sparks.Length);
    }

    private sealed class SyncProgress(List<ProgressEvent> events) : IProgress<ProgressEvent>
    {
        public void Report(ProgressEvent value) => events.Add(value);
    }
}
