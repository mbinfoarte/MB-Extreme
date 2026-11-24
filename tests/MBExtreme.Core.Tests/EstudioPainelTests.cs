using MBExtreme.Core.Recording;
using Xunit;

namespace MBExtreme.Core.Tests;

public class EstudioPainelTests
{
    [Theory]
    [InlineData(0, "Leve")]
    [InlineData(1, "Leve")]
    [InlineData(2, "Equilibrado")]
    [InlineData(4, "Equilibrado")]
    public void PredefinicaoSugeridaDependeDoNivelDoPc(int tier, string nome) =>
        Assert.Equal(nome, StudioPresets.ForTier(tier).Name);

    [Fact]
    public void TransmissaoNuncaPassaDe8Mbps() =>
        Assert.All(StudioPresets.All, p => Assert.True(StudioPresets.StreamBitrate(p) <= 8));

    [Fact]
    public void SaudeBoaQuandoTudoEstavel()
    {
        var h = StreamHealthRules.Evaluate(60, 60, 8000, 8000, 0, 6000, false);
        Assert.Equal(StreamHealthLevel.Good, h.Level);
    }

    [Fact]
    public void SaudeRuimComMuitosQuadrosPerdidos()
    {
        var h = StreamHealthRules.Evaluate(58, 60, 8000, 8000, 400, 6000, false);
        Assert.Equal(StreamHealthLevel.Bad, h.Level);
        Assert.Contains("Leve", h.Message);
    }

    [Fact]
    public void SaudeAvisaFpsBaixoEConexaoCaida()
    {
        Assert.Equal(StreamHealthLevel.Warning, StreamHealthRules.Evaluate(40, 60, 8000, 8000, 0, 6000, false).Level);
        Assert.Equal(StreamHealthLevel.Bad, StreamHealthRules.Evaluate(60, 60, 8000, 8000, 0, 6000, true).Level);
    }

    [Fact]
    public void ImagemPretaEDetectada()
    {
        var black = new byte[4 * 100];
        Assert.True(PreviewRules.IsMostlyBlack(black));
        var bright = new byte[4 * 100];
        Array.Fill(bright, (byte)200);
        Assert.False(PreviewRules.IsMostlyBlack(bright));
        Assert.False(PreviewRules.IsMostlyBlack(Array.Empty<byte>()));
    }

    [Fact]
    public void PreVisualizacaoMantemProporcao()
    {
        Assert.Equal((480, 270), PreviewRules.Fit(1920, 1080, 480));
        Assert.Equal((320, 240), PreviewRules.Fit(320, 240, 480));
    }

    [Fact]
    public void FacebookUsaRtmpsEChaveNoCaminho()
    {
        var t = new StreamTarget(StreamService.Facebook, "", "abc123");
        Assert.StartsWith("rtmps://", t.FullUrl);
        Assert.EndsWith("/abc123", t.FullUrl);
        Assert.Equal("Facebook", t.DisplayName);
    }

    [Fact]
    public void NumerosDosServicosAntigosNaoMudaram()
    {
        Assert.Equal(0, (int)StreamService.Twitch);
        Assert.Equal(1, (int)StreamService.YouTube);
        Assert.Equal(2, (int)StreamService.Custom);
    }
}
