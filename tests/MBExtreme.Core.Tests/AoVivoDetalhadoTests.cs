using MBExtreme.Core.Sensors;
using Xunit;

namespace MBExtreme.Core.Tests;

public class AoVivoDetalhadoTests
{
    [Fact]
    public void Uso_por_thread_na_ordem_do_processador()
    {
        var loads = new (string, double)[]
        {
            ("CPU Total", 30), ("CPU Core Max", 90),
            ("CPU Core #2 Thread #1", 20), ("CPU Core #1 Thread #2", 15), ("CPU Core #10 Thread #1", 99), ("CPU Core #1 Thread #1", 10),
            ("CPU Core #1", 12), ("CPU Core #2", 22),
        };
        Assert.Equal(new double[] { 10, 15, 20, 99 }, SensorPick.CoreLoads(loads));
        // sem threads (Intel antigo, AMD em alguns drivers): usa os núcleos
        Assert.Equal(new double[] { 12, 22 }, SensorPick.CoreLoads(new (string, double)[] { ("CPU Core #2", 22), ("CPU Core #1", 12), ("CPU Total", 5) }));
        Assert.Empty(SensorPick.CoreLoads(Array.Empty<(string, double)>()));
        Assert.Equal(new double[] { 100, 0 }, SensorPick.CoreLoads(new (string, double)[] { ("CPU Core #1", 140), ("CPU Core #2", -3) }));
    }

    [Fact]
    public void Tensao_do_nucleo_prefere_a_medida()
    {
        var amd = new (string, double)[] { ("Core #1 VID", 1.35), ("Core (SVI2 TFN)", 1.181), ("SoC (SVI2 TFN)", 1.09) };
        Assert.Equal(1.181, SensorPick.CpuVoltage(amd, Array.Empty<(string, double)>()));
        var intel = new (string, double)[] { ("CPU Core", 1.24), ("CPU Core #1 VID", 1.30) };
        Assert.Equal(1.24, SensorPick.CpuVoltage(intel, Array.Empty<(string, double)>()));
        var board = new (string, double)[] { ("+12V", 12.1), ("Vcore", 1.21) };
        Assert.Equal(1.21, SensorPick.CpuVoltage(new (string, double)[] { ("Core #1 VID", 1.4) }, board));
        Assert.Equal(1.42, SensorPick.CpuVoltage(new (string, double)[] { ("Core #1 VID", 1.4), ("Core #2 VID", 1.42), ("CPU Core", 0) }, Array.Empty<(string, double)>()));
        Assert.Null(SensorPick.CpuVoltage(Array.Empty<(string, double)>(), new (string, double)[] { ("+12V", 12) }));
    }

    [Fact]
    public void Water_cooler_pela_bomba()
    {
        Assert.True(SensorPick.IsLiquid(new[] { new FanReading("CPU Fan", 1100), new FanReading("Pump Fan", 2650) }));
        Assert.True(SensorPick.IsLiquid(new[] { new FanReading("AIO Pump", 2400) }));
        Assert.False(SensorPick.IsLiquid(new[] { new FanReading("CPU Fan", 1100), new FanReading("System Fan #1", 800) }));
        var groups = FanGroups.Split(new[] { new FanReading("Pump Fan", 2650), new FanReading("GPU Fan 1", 1450, true), new FanReading("System Fan #2", 900) });
        Assert.Single(groups.Cpu);
        Assert.Single(groups.Gpu);
        Assert.Single(groups.Case);
    }

    [Fact]
    public void Leitura_sem_detalhe_continua_valida()
    {
        var s = SensorSnapshot.Unavailable("x");
        Assert.Null(s.Detail);
        var d = new SensorDetail("Ryzen", new double[] { 1, 2 }, 1.2, "RTX", 1.0, 10501, 48, 3100, 12282, new[] { ("DIMM #1", 41.0) });
        var full = s with { Detail = d };
        Assert.Equal(2, full.Detail!.CoreLoads.Count);
        Assert.Equal(41, full.Detail.MemoryTemperatures[0].Celsius);
    }
}
