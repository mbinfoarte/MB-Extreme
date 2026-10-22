using MBExtreme.Core.Bottleneck;
using Xunit;

namespace MBExtreme.Core.Tests;

public class GargalosTests
{
    private static BottleneckSample S(double fps = 100, double low = 90, double? total = 40, double? core = 50, double? cpuT = 60,
        double? gpu = 60, double? gpuT = 60, double ram = 50, double? disk = 0) =>
        new(fps, 1000 / fps, low, total, core, cpuT, gpu, gpuT, ram, disk);

    [Fact]
    public void PlacaNoLimiteEProcessadorSobrando() =>
        Assert.Equal(BottleneckKind.Gpu, BottleneckAnalyzer.Classify(S(gpu: 99, core: 60)));

    [Fact]
    public void ProcessadorNoLimiteEPlacaSobrando() =>
        Assert.Equal(BottleneckKind.Cpu, BottleneckAnalyzer.Classify(S(gpu: 60, core: 96)));

    [Fact]
    public void MemoriaCheiaEhGargalo() =>
        Assert.Equal(BottleneckKind.Memory, BottleneckAnalyzer.Classify(S(ram: 93)));

    [Fact]
    public void CalorTemPrioridade() =>
        Assert.Equal(BottleneckKind.Thermal, BottleneckAnalyzer.Classify(S(gpu: 99, gpuT: 90)));

    [Fact]
    public void DiscoSoQuandoHaTravadaEFila() =>
        Assert.Equal(BottleneckKind.Disk, BottleneckAnalyzer.Classify(S(fps: 100, low: 40, disk: 5)));

    [Fact]
    public void SemGargaloQuandoTudoFolgado() =>
        Assert.Equal(BottleneckKind.Balanced, BottleneckAnalyzer.Classify(S()));

    [Fact]
    public void FilaDeDiscoSemTravadaNaoCulpaODisco() =>
        Assert.Equal(BottleneckKind.Balanced, BottleneckAnalyzer.Classify(S(low: 95, disk: 9)));

    [Fact]
    public void RelatorioApontaOGargaloPrincipalEaMeta()
    {
        var samples = Enumerable.Range(0, 100).Select(i => i < 70 ? S(fps: 70, gpu: 99, core: 55) : S(fps: 70)).ToList();
        var r = BottleneckAnalyzer.Analyze("cs2", samples, 144);
        Assert.Equal(BottleneckKind.Gpu, r.Primary);
        Assert.InRange(r.Percent[BottleneckKind.Gpu], 69, 71);
        Assert.Equal(70, r.AverageFps, 1);
        Assert.Contains(r.Advice, a => a.Contains("144") && a.Contains("placa de vídeo"));
    }

    [Fact]
    public void RelatorioSemQuadrosOrientaOUsuario()
    {
        var r = BottleneckAnalyzer.Analyze("x", new[] { S(fps: 0) }, 60);
        Assert.Equal(0, r.AverageFps);
        Assert.Single(r.Advice);
    }

    [Fact]
    public void GargaloPequenoNaoViraPrincipal()
    {
        var samples = Enumerable.Range(0, 100).Select(i => i < 10 ? S(gpu: 99) : S()).ToList();
        Assert.Equal(BottleneckKind.Balanced, BottleneckAnalyzer.Analyze("x", samples, 60).Primary);
    }

    [Fact]
    public void HistoricoSugereAMelhorSessaoDoJogo()
    {
        string file = Path.Combine(Path.GetTempPath(), "mbx-sess-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var h = new SessionHistory(file);
            h.Add(new SessionRecord { Game = "cs2", Date = new DateTime(2026, 10, 1), DurationSeconds = 600, AverageFps = 100, Low1PercentFps = 60, Settings = "Alto" });
            h.Add(new SessionRecord { Game = "cs2", Date = new DateTime(2026, 10, 2), DurationSeconds = 600, AverageFps = 130, Low1PercentFps = 100, Settings = "Médio" });
            h.Add(new SessionRecord { Game = "cs2", Date = new DateTime(2026, 10, 3), DurationSeconds = 30, AverageFps = 300, Low1PercentFps = 300, Settings = "Curta demais" });
            var again = new SessionHistory(file);
            Assert.Equal(3, again.For("CS2").Count);
            Assert.Equal("Médio", again.Best("cs2")!.Settings);
            Assert.Contains("130", again.Suggestion("cs2"));
            Assert.Null(again.Suggestion("outro"));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void ResumoAnonimoNaoTemIdentidadeEArredondaARam()
    {
        var r = BottleneckAnalyzer.Analyze("CS2.exe", Enumerable.Range(0, 60).Select(_ => S(fps: 99.6)).ToList(), 60);
        var stats = AnonymousSessionStats.From("CS2.exe", 3, 15L * 1024 * 1024 * 1024, r);
        Assert.Equal("cs2.exe", stats.GameExe);
        Assert.Equal(16, stats.RamGb);
        Assert.Equal(100, stats.AverageFps);
        string json = System.Text.Json.JsonSerializer.Serialize(stats);
        Assert.DoesNotContain("user", json, StringComparison.OrdinalIgnoreCase);
    }
}
