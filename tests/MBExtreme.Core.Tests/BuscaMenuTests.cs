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

/// <summary>Atualização: procura nos dois repositórios (downloads e o principal) e fica com a maior compilação.</summary>
public class AtualizacaoDoisRepositoriosTests
{
    [Fact]
    public void Procura_no_repositorio_de_downloads_e_no_principal()
    {
        Assert.Equal(new[] { "mbinfoarte/MB-Extreme-Download", "mbinfoarte/MB-Extreme" }, MBExtreme.Core.AppInfo.UpdateRepositories);
    }

    [Fact]
    public void Versao_com_data_antiga_ainda_e_a_mais_nova_pela_compilacao()
    {
        // a beta-61 ficou com a data do commit antigo (21/10) e veio depois da beta-59 na lista: vale o número, não a ordem
        const string json = """
        [
          { "tag_name": "beta-59", "name": "59", "draft": false, "prerelease": false, "body": "",
            "assets": [ { "name": "MBExtreme-Beta-Setup.exe", "browser_download_url": "https://github.com/mbinfoarte/MB-Extreme/releases/download/beta-59/MBExtreme-Beta-Setup.exe", "size": 10, "digest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" } ] },
          { "tag_name": "beta-61", "name": "61", "draft": false, "prerelease": false, "body": "",
            "assets": [ { "name": "MBExtreme-Beta-Setup.exe", "browser_download_url": "https://github.com/mbinfoarte/MB-Extreme/releases/download/beta-61/MBExtreme-Beta-Setup.exe", "size": 10, "digest": "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" } ] }
        ]
        """;
        Assert.Equal(61, MBExtreme.Core.Update.GitHubReleases.Newest(json, true)!.Build);
    }
}
