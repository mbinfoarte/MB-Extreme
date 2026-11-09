using MBExtreme.Core.Wallpaper;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>Módulo de papel de parede: modos de ajuste e galeria online (Pexels).</summary>
public class PapelDeParedeTests
{
    [Theory]
    [InlineData(WallpaperFit.Center, 0, "0", "0")]
    [InlineData(WallpaperFit.Tile, 1, "0", "1")]
    [InlineData(WallpaperFit.Stretch, 2, "2", "0")]
    [InlineData(WallpaperFit.Fit, 3, "6", "0")]
    [InlineData(WallpaperFit.Fill, 4, "10", "0")]
    [InlineData(WallpaperFit.Span, 5, "22", "0")]
    public void Modo_de_ajuste_vira_codigo_do_windows(WallpaperFit fit, int position, string style, string tile)
    {
        Assert.Equal(position, WallpaperFitText.Position(fit));
        Assert.Equal((style, tile), WallpaperFitText.LegacyRegistry(fit));
        Assert.False(string.IsNullOrWhiteSpace(WallpaperFitText.Label(fit)));
    }

    [Fact]
    public void Resolucao_do_monitor_em_texto()
    {
        Assert.Equal("1920 × 1080", new WallpaperMonitor("id", "Monitor 1", 1920, 1080, 0, 0, true).Resolution);
        Assert.Equal("—", new WallpaperMonitor("id", "Monitor 1", 0, 0, 0, 0, true).Resolution);
    }

    [Fact]
    public void Pexels_exige_chave_e_so_aceita_endereco_do_pexels()
    {
        Assert.False(Pexels.HasKey(""));
        Assert.False(Pexels.HasKey("curta"));
        Assert.True(Pexels.HasKey("563492ad6f91700001000001abcdefabcdefabcdabcdefab"));
        Assert.True(Pexels.Safe("https://images.pexels.com/photos/1/x.jpg"));
        Assert.False(Pexels.Safe("http://images.pexels.com/x.jpg"));       // sem https
        Assert.False(Pexels.Safe("https://evil.com/x.jpg"));                // fora do pexels
        Assert.False(Pexels.Safe("https://images.pexels.com.evil.com/x"));  // domínio falso
    }

    [Fact]
    public void Le_as_fotos_do_json_do_pexels_com_autor_e_endereco_seguro()
    {
        const string json = """
            {"photos":[
              {"id":123,"width":3840,"height":2160,"photographer":"Fulano","alt":"paisagem","url":"https://www.pexels.com/photo/123/",
               "src":{"original":"https://images.pexels.com/photos/123/o.jpg","large2x":"https://images.pexels.com/photos/123/l2x.jpg","medium":"https://images.pexels.com/photos/123/m.jpg"}},
              {"id":9,"src":{"medium":"http://images.pexels.com/x.jpg","large2x":"http://images.pexels.com/y.jpg"}}
            ]}
            """;
        var list = Pexels.Parse(json);
        Assert.Single(list);   // a segunda é http (insegura) e foi descartada
        var p = list[0];
        Assert.Equal("123", p.Id);
        Assert.Equal("Fulano", p.Author);
        Assert.Equal(3840, p.Width);
        Assert.EndsWith("l2x.jpg", p.FullUrl);
        Assert.EndsWith("m.jpg", p.ThumbUrl);
        Assert.Empty(Pexels.Parse("não é json"));
        Assert.Empty(Pexels.Parse("{}"));
    }
}
