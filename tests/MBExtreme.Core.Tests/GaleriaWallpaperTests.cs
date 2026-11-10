using MBExtreme.Core.Wallpaper;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>Galeria de wallpapers do MB Extreme (4K, feitos no Canva) lida do repositório de downloads.</summary>
public class GaleriaWallpaperTests
{
    [Fact]
    public void Le_a_lista_e_monta_os_enderecos()
    {
        const string json = """
        { "version": 1, "items": [
          { "id": "05-carro-de-corrida", "title": "Carro de corrida", "theme": "Velocidade", "width": 3840, "height": 2160,
            "file": "wallpapers/05-carro-de-corrida.jpg", "thumb": "wallpapers/miniaturas/05-carro-de-corrida.jpg", "bytes": 1111997 } ] }
        """;
        var item = Assert.Single(MbGallery.Parse(json));
        Assert.Equal(("Carro de corrida", 3840), (item.Title, item.Width));
        Assert.Equal("https://raw.githubusercontent.com/mbinfoarte/MB-Extreme-Download/main/wallpapers/05-carro-de-corrida.jpg", MbGallery.UrlOf(item.File));
    }

    [Theory]
    [InlineData("wallpapers/a.jpg", true)]
    [InlineData("wallpapers/miniaturas/a-1.png", true)]
    [InlineData("wallpapers/../config/canal.json", false)]
    [InlineData("https://outro.site/a.jpg", false)]
    [InlineData("wallpapers\\a.jpg", false)]
    [InlineData("wallpapers/a.exe", false)]
    [InlineData("outra/a.jpg", false)]
    public void So_aceita_imagens_dentro_da_pasta_da_galeria(string path, bool ok) => Assert.Equal(ok, MbGallery.IsSafePath(path));

    [Fact]
    public void Lista_estragada_ou_com_caminho_perigoso_nao_quebra()
    {
        Assert.Empty(MbGallery.Parse("isto não é json"));
        Assert.Empty(MbGallery.Parse("""{ "items": [ { "id": "x", "title": "X", "theme": "", "width": 1, "height": 1, "file": "../x.jpg", "thumb": "wallpapers/t.jpg", "bytes": 0 } ] }"""));
    }
}
