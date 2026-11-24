using MBExtreme.Core.Benchmark;
using MBExtreme.Core.Fps;
using MBExtreme.Core.Stress;
using Xunit;

namespace MBExtreme.Core.Tests;

public class EstresseGpuTests
{
    [Fact]
    public void Combinado_alterna_as_quatro_cenas_a_cada_10_segundos_e_uma_cena_fica_fixa()
    {
        Assert.Equal(BurnScene.Fractal, GpuBurnPlan.Current(BurnScene.Combined, TimeSpan.FromSeconds(0)));
        Assert.Equal(BurnScene.Fractal, GpuBurnPlan.Current(BurnScene.Combined, TimeSpan.FromSeconds(9.9)));
        Assert.Equal(BurnScene.Cubes, GpuBurnPlan.Current(BurnScene.Combined, TimeSpan.FromSeconds(10)));
        Assert.Equal(BurnScene.Fill, GpuBurnPlan.Current(BurnScene.Combined, TimeSpan.FromSeconds(25)));
        Assert.Equal(BurnScene.Compute, GpuBurnPlan.Current(BurnScene.Combined, TimeSpan.FromSeconds(39)));
        Assert.Equal(BurnScene.Fractal, GpuBurnPlan.Current(BurnScene.Combined, TimeSpan.FromSeconds(40)));       // volta ao começo
        Assert.Equal(BurnScene.Fill, GpuBurnPlan.Current(BurnScene.Fill, TimeSpan.FromMinutes(7)));
        Assert.All(GpuBurnPlan.Rotation, s => Assert.NotEqual("Combinado", GpuBurnPlan.Name(s)));
    }

    [Fact]
    public void Hash_de_quadro_muda_com_qualquer_byte_e_e_estavel()
    {
        var frame = Enumerable.Range(0, 5000).Select(i => (byte)(i * 7)).ToArray();
        ulong a = GpuBurnPlan.Fnv(frame);
        Assert.Equal(a, GpuBurnPlan.Fnv(frame));
        frame[4321] ^= 1;
        Assert.NotEqual(a, GpuBurnPlan.Fnv(frame));
        Assert.Equal(14695981039346656037UL, GpuBurnPlan.Fnv(Array.Empty<byte>()));
    }

    private static StressRun Run(bool completed = true, long cpuErrors = 0, long gpuErrors = 0, string reason = "Concluído no tempo escolhido.") =>
        new(new DateTime(2026, 10, 24, 20, 0, 0), TimeSpan.FromMinutes(10), true, true, true, "Combinado", completed, reason, cpuErrors, gpuErrors, 78, 81.5, 143.2, 120.4,
            "AMD Ryzen 5 5600X <6-Core>", "NVIDIA GeForce RTX 3070", new[] { new SensorTrack("Temperatura da GPU", "°C", new double[] { 50, 60, 70, 80 }), new SensorTrack("Uso da GPU", "%", new double[] { 99, 100, 100, 99 }) });

    [Fact]
    public void Veredito_estavel_instavel_e_interrompido()
    {
        var stable = Run();
        Assert.True(stable.Stable);
        Assert.StartsWith("Estável: 10:00", stable.Verdict);
        Assert.Equal("CPU + Memória + GPU 3D", stable.Title);

        var bad = Run(cpuErrors: 2, gpuErrors: 1);
        Assert.False(bad.Stable);
        Assert.Equal(3, bad.Errors);
        Assert.StartsWith("Instável: 3 erro(s)", bad.Verdict);

        var stopped = Run(completed: false, reason: "Interrompido por você.");
        Assert.StartsWith("Interrompido: Interrompido por você.", stopped.Verdict);
        Assert.False(stopped.Stable);
        Assert.False(Run(completed: false, gpuErrors: 0, reason: "TDR").Stable);
    }

    [Fact]
    public void Relatorio_html_e_texto_trazem_os_numeros_e_escapam_nomes()
    {
        string html = StressReportBuilder.ToHtml(Run(cpuErrors: 1));
        Assert.StartsWith("<!doctype html>", html);
        Assert.Contains("AMD Ryzen 5 5600X &lt;6-Core&gt;", html);
        Assert.DoesNotContain("<6-Core>", html);
        Assert.Contains("Instável", html);
        Assert.Contains("Temperatura da GPU", html);
        Assert.Contains("polyline", html);
        Assert.Contains("81.5", html);
        string text = StressReportBuilder.ToText(Run());
        Assert.Contains("Estável", text);
        Assert.Contains("GPU 3D:", text);
        Assert.Contains("FPS em média", text);
        Assert.Contains("Uso da GPU: média", text);
    }

    [Fact]
    public void Historico_guarda_as_ultimas_20_e_aceita_arquivo_estragado()
    {
        string path = Path.Combine(Path.GetTempPath(), "mbx-stress-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Assert.Empty(StressHistory.Load(path));
            for (int i = 0; i < 25; i++) StressHistory.Add(Run() with { When = DateTime.Today.AddMinutes(i) }, path);
            var all = StressHistory.Load(path);
            Assert.Equal(20, all.Count);
            Assert.Equal(DateTime.Today.AddMinutes(24), all[^1].When);
            Assert.Equal("Temperatura da GPU", all[0].Tracks[0].Name);
            File.WriteAllText(path, "{ quebrado");
            Assert.Empty(StressHistory.Load(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
