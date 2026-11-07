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
        Assert.InRange(quick, 15, 45);
        Assert.InRange(precise, 90, 240);
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
            Assert.Equal(0, result.Score - BenchmarkService.ComputeScore(0, 0, 0, result.DiskWriteMBps, result.DiskReadMBps));
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

    private sealed class SyncProgress(List<ProgressEvent> events) : IProgress<ProgressEvent>
    {
        public void Report(ProgressEvent value) => events.Add(value);
    }
}
