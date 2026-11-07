using MBExtreme.Core.Network;
using MBExtreme.Core.Sensors;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>Leituras iguais às do Gerenciador de Tarefas (placa certa, motor mais ocupado) e a escala do velocímetro.</summary>
public class SensoresVelocimetroTests
{
    [Fact]
    public void Com_integrada_e_dedicada_mostra_a_dedicada()
    {
        var gpus = new[]
        {
            new GpuReading("GpuAmd", "AMD Radeon(TM) Graphics", 45, 2, 5, 400),
            new GpuReading("GpuNvidia", "NVIDIA GeForce RTX 4070", 38, 1, 20, 210),
        };
        Assert.Equal("NVIDIA GeForce RTX 4070", UsageRules.PickGpu(gpus)!.Name);
        Assert.Equal("NVIDIA GeForce RTX 4070", UsageRules.PickGpu(gpus.Reverse().ToList())!.Name);
    }

    [Theory]
    [InlineData("GpuAmd", "AMD Radeon(TM) Graphics", true)]
    [InlineData("GpuAmd", "AMD Radeon RX 7800 XT", false)]
    [InlineData("GpuIntel", "Intel(R) UHD Graphics 770", true)]
    [InlineData("GpuIntel", "Intel(R) Arc(TM) A770", false)]
    [InlineData("GpuNvidia", "NVIDIA GeForce GTX 1650", false)]
    public void Placa_integrada_reconhecida(string kind, string name, bool integrated) => Assert.Equal(integrated, UsageRules.LooksIntegrated(kind, name));

    [Fact]
    public void Uma_placa_so_e_ela_mesma()
    {
        Assert.Equal("Intel(R) UHD Graphics", UsageRules.PickGpu(new[] { new GpuReading("GpuIntel", "Intel(R) UHD Graphics", 50, 10, null, null) })!.Name);
        Assert.Null(UsageRules.PickGpu(Array.Empty<GpuReading>()));
    }

    [Fact]
    public void Uso_da_placa_e_o_motor_mais_ocupado_de_cada_placa()
    {
        const string dgpu = "0x00000000_0x0000D1E2", igpu = "0x00000000_0x0000A001";
        var samples = new (string, double)[]
        {
            ($"pid_100_luid_{dgpu}_phys_0_eng_0_engtype_3D", 30),
            ($"pid_200_luid_{dgpu}_phys_0_eng_0_engtype_3D", 25),        // dois programas no 3D somam
            ($"pid_100_luid_{dgpu}_phys_0_eng_3_engtype_VideoDecode", 40),
            ($"pid_300_luid_{igpu}_phys_0_eng_0_engtype_3D", 12),
            ("lixo", 99),
        };
        var byAdapter = UsageRules.GpuByAdapter(samples);
        Assert.Equal(55, byAdapter[dgpu]);
        Assert.Equal(12, byAdapter[igpu]);
        Assert.Equal(("0x00000000_0x0000D1E2", "VideoDecode"), UsageRules.ParseEngine($"pid_1_luid_{dgpu}_phys_0_eng_3_engtype_VideoDecode"));
    }

    [Fact]
    public void Placa_dedicada_e_a_que_usa_mais_memoria_propria()
    {
        var memory = new (string, double)[] { ("luid_0x00000000_0x0000A001_phys_0", 512e6), ("luid_0x00000000_0x0000D1E2_phys_0", 3.2e9) };
        Assert.Equal("0x00000000_0x0000D1E2", UsageRules.DedicatedAdapter(memory));
        Assert.Null(UsageRules.DedicatedAdapter(Array.Empty<(string, double)>()));
    }

    [Fact]
    public void Temperatura_do_processador_pelo_sensor_principal()
    {
        Assert.Equal(52, UsageRules.CpuTemperature(new[] { ("CCD1 (Tdie)", 61.0), ("Core (Tctl/Tdie)", 52.0) }));
        Assert.Equal(48, UsageRules.CpuTemperature(new[] { ("CPU Core #1", 55.0), ("CPU Package", 48.0) }));
        Assert.Equal(55, UsageRules.CpuTemperature(new[] { ("Core #1", 50.0), ("Core #2", 55.0) }));
        Assert.Null(UsageRules.CpuTemperature(Array.Empty<(string, double)>()));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 0.125)]
    [InlineData(100, 0.5)]
    [InlineData(175, 0.5625)]
    [InlineData(1000, 1)]
    [InlineData(2000, 1)]
    [InlineData(-3, 0)]
    public void Escala_do_velocimetro(double mbps, double fraction) => Assert.Equal(fraction, SpeedScale.Fraction(mbps, SpeedScale.Normal), 4);

    [Fact]
    public void Internet_acima_de_1_giga_usa_a_escala_rapida()
    {
        Assert.Same(SpeedScale.Normal, SpeedScale.TicksFor(485));
        Assert.Same(SpeedScale.Fast, SpeedScale.TicksFor(1800));
        Assert.Equal("5G", SpeedScale.Label(5000));
        Assert.Equal("1000", SpeedScale.Label(1000));
    }
}
