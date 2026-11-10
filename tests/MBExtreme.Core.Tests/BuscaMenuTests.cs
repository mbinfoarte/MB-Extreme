using MBExtreme.UI;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>Busca do menu: no lugar de "Todas as ferramentas", acha qualquer tela pelo nome ou pelo assunto.</summary>
public class BuscaMenuTests
{
    [Fact]
    public void Acha_pelo_nome_sem_ligar_para_acentos()
    {
        Assert.Equal("fans", NavigationMap.Search("ventoinha")[0].Tile.Key);
        Assert.Equal("fans", NavigationMap.Search("VENTOÍNHAS")[0].Tile.Key);
        Assert.Contains(NavigationMap.Search("bios"), r => r.Tile.Key == "bios");
        Assert.Contains(NavigationMap.Search("privacidade"), r => r.Tile.Key == "privacy");
    }

    [Fact]
    public void Acha_pela_descricao_e_nome_vem_primeiro()
    {
        var dns = NavigationMap.Search("dns");
        Assert.Equal("dns", dns[0].Tile.Key);                                           // o nome "DNS" antes de quem só cita na descrição
        Assert.Contains(NavigationMap.Search("drift"), r => r.Tile.Key == "padcalib");   // pela descrição
    }

    [Fact]
    public void Texto_curto_nao_busca_e_menu_nao_tem_mais_os_duplicados()
    {
        Assert.Empty(NavigationMap.Search("a"));
        Assert.Empty(NavigationMap.Search("   "));
        var side = NavigationMap.Sidebar.SelectMany(s => s.Items).Select(i => i.Key).ToList();
        Assert.DoesNotContain("alltools", side);
        Assert.DoesNotContain("news", side);   // fica dentro de Sobre
        Assert.True(NavigationMap.Search("x", 3).Count <= 3);
    }
}
