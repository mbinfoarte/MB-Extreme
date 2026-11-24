using MBExtreme.Core.Tweaks;
using Xunit;

namespace MBExtreme.Core.Tests;

public class OtimizacoesPorTipoTests
{
    [Theory]
    [InlineData(TweakCategory.Gaming, "Agendamento de GPU acelerado por hardware", TweakType.Gpu)]
    [InlineData(TweakCategory.Performance, "Plano de energia Desempenho máximo", TweakType.Energy)]
    [InlineData(TweakCategory.Performance, "Prioridade do processador para programas", TweakType.Cpu)]
    [InlineData(TweakCategory.Performance, "Arquivo de paginação", TweakType.Memory)]
    [InlineData(TweakCategory.Network, "Nagle", TweakType.Network)]
    [InlineData(TweakCategory.Storage, "TRIM", TweakType.Storage)]
    [InlineData(TweakCategory.Performance, "Outra coisa", TweakType.System)]
    public void Classifica_por_peca(TweakCategory category, string text, TweakType expected) => Assert.Equal(expected, TweakTypes.Of(category, text));

    [Fact]
    public void Niveis_acumulam()
    {
        Assert.True(TweakTypes.InLevel(TweakRisk.Low, TweakLevel.Basic));
        Assert.False(TweakTypes.InLevel(TweakRisk.Medium, TweakLevel.Basic));
        Assert.True(TweakTypes.InLevel(TweakRisk.Medium, TweakLevel.Advanced));
        Assert.False(TweakTypes.InLevel(TweakRisk.High, TweakLevel.Advanced));
        Assert.True(TweakTypes.InLevel(TweakRisk.High, TweakLevel.Extreme));
        Assert.Equal(Enum.GetValues<TweakType>().Length, TweakTypes.All.Count);
    }
}
