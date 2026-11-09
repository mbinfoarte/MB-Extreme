using MBExtreme.Core.Sensors;
using Xunit;

namespace MBExtreme.Core.Tests;

public class SensoresTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(-5, false)]
    [InlineData(0.5, false)]
    [InlineData(34, true)]
    [InlineData(98.5, true)]
    [InlineData(130, false)]
    public void Temperatura_zerada_ou_absurda_nao_e_leitura_valida(double celsius, bool valid) =>
        Assert.Equal(valid, SensorRules.IsValidTemperature(celsius));

    [Fact]
    public void Zona_termica_converte_kelvin_e_descarta_o_invalido()
    {
        Assert.Equal(46.9, SensorRules.FromTenthsOfKelvin(3200));
        Assert.Null(SensorRules.FromTenthsOfKelvin(0));
        Assert.Equal(50.0, SensorRules.FromKelvin(323.15));
        Assert.Null(SensorRules.FromKelvin(0));
    }

    [Fact]
    public void Motivo_da_falta_de_temperatura_aponta_a_causa_mais_provavel()
    {
        Assert.Contains("administrador", SensorRules.WhyCpuTemperatureIsMissing(false, null, true, 0));
        Assert.Contains("não abriu", SensorRules.WhyCpuTemperatureIsMissing(true, "erro", false, 0));
        Assert.Contains("reconhecido pela biblioteca", SensorRules.WhyCpuTemperatureIsMissing(true, null, false, 0));
        Assert.Contains("PawnIO", SensorRules.WhyCpuTemperatureIsMissing(true, null, true, 0));
        Assert.Contains("zerado", SensorRules.WhyCpuTemperatureIsMissing(true, null, true, 3));
    }
}
