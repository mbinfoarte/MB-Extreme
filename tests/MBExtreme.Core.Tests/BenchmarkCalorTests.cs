using MBExtreme.Core.Benchmark;
using MBExtreme.Core.Fps;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>Benchmark mais longo e a análise de calor: picos de temperatura e queda de clock do começo para o fim da carga.</summary>
public class BenchmarkCalorTests
{
    private static SensorTrack T(string name, params double[] v) => new(name, "", v);
    private static double[] Repeat(double value, int n) => Enumerable.Repeat(value, n).ToArray();

    [Fact]
    public void Padrao_dura_uns_6_minutos_e_preciso_uns_12_com_todos_os_testes()
    {
        int padrao = BenchmarkModes.EstimatedSeconds(new BenchmarkOptions { Mode = BenchmarkMode.Padrao, Gpu3D = true });
        int preciso = BenchmarkModes.EstimatedSeconds(new BenchmarkOptions { Mode = BenchmarkMode.Preciso, Gpu3D = true });
        Assert.InRange(padrao, 5 * 60, 10 * 60);
        Assert.InRange(preciso, 10 * 60, 15 * 60);
        Assert.InRange(BenchmarkModes.EstimatedSeconds(new BenchmarkOptions { Mode = BenchmarkMode.Rapido, Gpu3D = true }), 30, 120);
    }

    [Fact]
    public void Notebook_que_esquenta_e_perde_clock_aparece_como_limitado_pelo_calor()
    {
        var clock = Repeat(4500, 20).Concat(Repeat(3600, 20)).ToArray();   // 4,5 GHz no começo, 3,6 GHz no fim: caiu 20%
        var tracks = new[] { T("Temperatura da CPU", Repeat(97, 40)), T("Clock da CPU", clock), T("Uso da CPU", Repeat(100, 40)), T("Temperatura da GPU", Repeat(70, 40)) };
        var r = BenchmarkThermals.Analyze(tracks);
        Assert.Equal(ThermalLevel.Hot, r.Level);
        Assert.Equal(97, r.CpuPeak);
        Assert.Equal(20, r.CpuClockDropPercent);
        Assert.Contains(r.Notes, n => n.Contains("caiu 20%"));
        Assert.Contains(r.Notes, n => n.Contains("pasta térmica"));
    }

    [Fact]
    public void PC_frio_e_estavel_fica_sob_controle_e_sem_sensor_avisa()
    {
        var tracks = new[] { T("Temperatura da CPU", Repeat(70, 30)), T("Clock da CPU", Repeat(4200, 30)), T("Temperatura da GPU", Repeat(65, 30)) };
        var ok = BenchmarkThermals.Analyze(tracks);
        Assert.Equal(ThermalLevel.Ok, ok.Level);
        Assert.Equal(0, ok.CpuClockDropPercent);
        Assert.Equal(ThermalLevel.Unknown, BenchmarkThermals.Analyze(Array.Empty<SensorTrack>()).Level);
        Assert.Equal(ThermalLevel.Warm, BenchmarkThermals.Analyze(new[] { T("Temperatura da GPU", Repeat(82, 10)) }).Level);
    }

    [Fact]
    public void Clock_parado_sem_carga_nao_conta_como_queda()
    {
        // no fim o teste acabou (uso baixo, clock baixo): só a parte com carga conta
        var clock = Repeat(4400, 30).Concat(Repeat(800, 10)).ToArray();
        var load = Repeat(100, 30).Concat(Repeat(5, 10)).ToArray();
        Assert.Equal(0, BenchmarkThermals.ClockDrop(T("Clock da CPU", clock), T("Uso da CPU", load)));
    }
}
