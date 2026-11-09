using MBExtreme.Core.Games;
using Xunit;

namespace MBExtreme.Core.Tests;

public class CapasTests
{
    [Fact]
    public void Le_a_busca_da_loja_e_ignora_itens_sem_numero()
    {
        string json = "{\"total\":3,\"items\":[{\"type\":\"app\",\"name\":\"Battlefield 6\",\"id\":2807960},{\"name\":\"sem id\"},{\"type\":\"app\",\"name\":\"EA SPORTS FC 25\",\"id\":2669320}]}";
        var found = CoverSearch.ParseSearch(json);
        Assert.Equal(2, found.Count);
        Assert.Equal("Battlefield 6", found[0].Name);
        Assert.EndsWith("/2807960/library_600x900.jpg", found[0].ImageUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("não é json")]
    [InlineData("{\"items\":5}")]
    public void Resposta_estranha_vira_lista_vazia(string json) => Assert.Empty(CoverSearch.ParseSearch(json));

    [Fact]
    public void Busca_codifica_o_nome_e_o_arquivo_da_capa_e_estavel()
    {
        Assert.Contains("term=EA%20FC%2027", CoverSearch.SearchUrl(" EA FC 27 "));
        Assert.Equal(CoverSearch.FileKey("manual:C:\\Jogos\\a.exe"), CoverSearch.FileKey("manual:C:\\Jogos\\a.exe"));
        Assert.NotEqual(CoverSearch.FileKey("a"), CoverSearch.FileKey("b"));
        Assert.DoesNotContain('\\', CoverSearch.FileKey("manual:C:\\x"));
    }

    [Fact]
    public async Task Guarda_e_acha_a_capa_escolhida_e_remove()
    {
        string folder = Path.Combine(Path.GetTempPath(), "mbx-capas-" + Guid.NewGuid().ToString("N"));
        try
        {
            string id = "manual:x";
            Assert.Null(CoverSearch.FindCustom(folder, id));
            Directory.CreateDirectory(Path.Combine(folder, "custom"));
            await File.WriteAllBytesAsync(Path.Combine(folder, "custom", CoverSearch.FileKey(id) + ".jpg"), new byte[2000]);
            Assert.NotNull(CoverSearch.FindCustom(folder, id));
            CoverSearch.RemoveCustom(folder, id);
            Assert.Null(CoverSearch.FindCustom(folder, id));
            Assert.Null(await CoverSearch.SaveCustomAsync(new HttpClient(), new CoverCandidate("abc", "x", ""), folder, id)); // número inválido
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}

public class SteamGridDbTests
{
    [Fact]
    public void Le_busca_e_capas_e_so_aceita_enderecos_do_proprio_servico()
    {
        var games = SteamGridDb.ParseSearch("{\"success\":true,\"data\":[{\"id\":5252,\"name\":\"EA SPORTS FC 27\"},{\"name\":\"sem id\"}]}");
        Assert.Single(games);
        Assert.Equal(5252, games[0].Id);

        string grids = "{\"data\":[{\"url\":\"https://cdn2.steamgriddb.com/grid/a.png\",\"thumb\":\"https://cdn2.steamgriddb.com/thumb/a.jpg\"}," +
                       "{\"url\":\"https://malicioso.example/x.png\",\"thumb\":\"https://malicioso.example/x.png\"}," +
                       "{\"url\":\"http://cdn2.steamgriddb.com/grid/b.png\"}]}";
        var found = SteamGridDb.ParseGrids(grids);
        Assert.Single(found);
        Assert.EndsWith("a.png", found[0].Url);
    }

    [Theory]
    [InlineData("https://cdn2.steamgriddb.com/grid/x.png", true)]
    [InlineData("https://steamgriddb.com/x", true)]
    [InlineData("https://steamgriddb.com.evil.com/x", false)]
    [InlineData("http://cdn2.steamgriddb.com/x", false)]
    [InlineData("file:///C:/x.png", false)]
    [InlineData(null, false)]
    public void Download_so_do_steamgriddb_por_https(string? url, bool ok) => Assert.Equal(ok, CoverCandidate.IsAllowedDownload(url));

    [Fact]
    public async Task Sem_chave_nao_consulta_nada()
    {
        Assert.Empty(await SteamGridDb.SearchAsync(new HttpClient(), "", "EA FC"));
        Assert.Contains("/search/autocomplete/EA%20FC", SteamGridDb.SearchUrl(" EA FC "));
    }
}
