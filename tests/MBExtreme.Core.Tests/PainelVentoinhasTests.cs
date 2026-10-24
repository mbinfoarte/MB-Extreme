using MBExtreme.Core.Sensors;
using Xunit;

namespace MBExtreme.Core.Tests;

public class PainelVentoinhasTests
{
    [Fact]
    public void Ventoinhas_sao_separadas_entre_cpu_placa_de_video_e_gabinete()
    {
        var groups = FanGroups.Split(new[]
        {
            new FanReading("CPU Fan", 2277), new FanReading("Pump Fan", 2047),
            new FanReading("GPU Fan 1", 1500, true), new FanReading("GPU Fan 2", 1480, true),
            new FanReading("Fan #3", 900), new FanReading("Chassis Fan", 850),
        });
        Assert.Equal(new[] { "CPU Fan", "Pump Fan" }, groups.Cpu.Select(f => f.Name));
        Assert.Equal(2, groups.Gpu.Count);
        Assert.Equal(2, groups.Case.Count);
    }

    [Fact]
    public void Ventoinha_da_placa_de_video_nao_vira_ventoinha_da_cpu_mesmo_com_nome_parecido()
    {
        var groups = FanGroups.Split(new[] { new FanReading("GPU Fan", 1200, true) });
        Assert.Empty(groups.Cpu);
        Assert.Single(groups.Gpu);
    }

    [Theory]
    [InlineData("CPU Fan", false, "CPU")]
    [InlineData("CPU Optional Fan", false, "CPU (opcional)")]
    [InlineData("Pump Fan", false, "Bomba")]
    [InlineData("AIO Pump", false, "Bomba")]
    [InlineData("Fan #3", false, "Gabinete 3")]
    [InlineData("GPU Fan 2", true, "Ventoinha 2")]
    [InlineData("GPU Fan", true, "Ventoinha")]
    [InlineData("Liquid Cooler", false, "Cooler")]
    public void Nomes_sao_mostrados_em_portugues(string name, bool gpu, string expected) =>
        Assert.Equal(expected, FanGroups.Label(new FanReading(name, 1000, gpu)));

    [Fact]
    public void Linha_de_texto_junta_as_rotacoes_e_fica_vazia_sem_leitura()
    {
        Assert.Equal("CPU 2277 rpm  ·  Bomba 2047 rpm", FanGroups.Describe(new[] { new FanReading("CPU Fan", 2277.4), new FanReading("Pump Fan", 2047) }));
        Assert.Equal("", FanGroups.Describe(Array.Empty<FanReading>()));
    }
}
