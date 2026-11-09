using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MBExtreme.Core.Ranking;
using MBExtreme.LicenseServer;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MBExtreme.LicenseServer.Tests;

public class RankingServidorTests : IClassFixture<ServerFixture>
{
    private readonly ServerFixture _server;

    public RankingServidorTests(ServerFixture server) => _server = server;

    private HttpClient Client() => _server.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string NewId() => Guid.NewGuid().ToString("N");

    private static BenchSubmission Bench(string id, int score, string gpu) =>
        new(id, "AMD Ryzen 5 5600X 6-Core Processor", gpu, 16, 6, 12, score, 1000, 8000, 22, 500, 9000, 300, 2500, "0.9.0");

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Posicao_aparece_so_depois_de_30_pcs_com_a_mesma_placa()
    {
        var client = Client();
        string gpu = "Placa de Teste " + Guid.NewGuid().ToString("N")[..6];
        BenchStanding? last = null;
        for (int i = 1; i <= 29; i++)
        {
            var response = await client.PostAsJsonAsync("/v1/rank/bench", Bench(NewId(), 1000 + i * 100, gpu));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            last = await response.Content.ReadFromJsonAsync<BenchStanding>(Web);
        }
        Assert.False(last!.SameGpu.Enough);
        Assert.Equal(29, last.SameGpu.Samples);

        var mine = await client.PostAsJsonAsync("/v1/rank/bench", Bench(NewId(), 5000, gpu));
        var standing = (await mine.Content.ReadFromJsonAsync<BenchStanding>(Web))!;
        Assert.True(standing.SameGpu.Enough);
        Assert.Equal(30, standing.SameGpu.Samples);
        Assert.InRange(standing.SameGpu.BeatsPercent, 95, 100);                             // 5000 supera todos os outros 29
        Assert.True(standing.SameGpu.Median > 1000 && standing.SameGpu.Median < 5000);
    }

    [Fact]
    public async Task Mesma_instalacao_atualiza_a_linha_em_vez_de_duplicar()
    {
        var client = Client();
        string gpu = "Placa de Teste " + Guid.NewGuid().ToString("N")[..6];
        string id = NewId();
        await client.PostAsJsonAsync("/v1/rank/bench", Bench(id, 1000, gpu));
        var again = await client.PostAsJsonAsync("/v1/rank/bench", Bench(id, 2000, gpu));
        var standing = (await again.Content.ReadFromJsonAsync<BenchStanding>(Web))!;
        Assert.Equal(1, standing.SameGpu.Samples);
    }

    [Fact]
    public async Task Envios_invalidos_voltam_400_sem_gravar()
    {
        var client = Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/v1/rank/bench", Bench("curto", 1000, "RTX"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/v1/rank/bench", Bench(NewId(), -5, "RTX"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/v1/rank/bench", new StringContent("{ não é json", Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/v1/rank/forget", new { installId = "x" })).StatusCode);
    }

    [Fact]
    public async Task Jogo_mostra_a_mediana_depois_de_10_amostras_e_esquecer_apaga_tudo_do_codigo()
    {
        var client = Client();
        string gpu = "Placa de Jogo " + Guid.NewGuid().ToString("N")[..6];
        string game = "Jogo " + Guid.NewGuid().ToString("N")[..6];
        GameStanding? standing = null;
        string mineId = NewId();
        for (int i = 0; i < 10; i++)
        {
            string id = i == 0 ? mineId : NewId();
            var response = await client.PostAsJsonAsync("/v1/rank/game", new GameSubmission(id, game, "AMD Ryzen 5 5600X 6-Core Processor", gpu, 16, 60 + i * 10, 40, 30, 180, "0.9.0"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            standing = await response.Content.ReadFromJsonAsync<GameStanding>(Web);
        }
        Assert.True(standing!.Enough);
        Assert.Equal(10, standing.Samples);
        Assert.InRange(standing.MedianFps, 100, 110);
        Assert.True(standing.P75Fps > standing.P25Fps);

        await client.PostAsJsonAsync("/v1/rank/bench", Bench(mineId, 3000, gpu));
        var forget = await client.PostAsJsonAsync("/v1/rank/forget", new ForgetRequest(mineId));
        Assert.Equal(HttpStatusCode.OK, forget.StatusCode);
        using var doc = JsonDocument.Parse(await forget.Content.ReadAsStringAsync());
        Assert.Equal(2, doc.RootElement.GetProperty("deleted").GetInt32());                  // uma linha de jogo e uma de benchmark
        var after = await client.PostAsJsonAsync("/v1/rank/game", new GameSubmission(NewId(), game, "AMD Ryzen 5 5600X 6-Core Processor", gpu, 16, 100, 70, 50, 180, "0.9.0"));
        Assert.Equal(9 + 1, (await after.Content.ReadFromJsonAsync<GameStanding>(Web))!.Samples);   // saiu a do código apagado
    }

    [Fact]
    public void Codigo_de_instalacao_nunca_e_gravado_como_veio()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mbx-rank-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "ranking.db");
        string id = NewId();
        try
        {
            var store = new RankingStore(path, "segredo-de-teste-longo-o-bastante");
            store.SaveBench(Bench(id, 1234, "RTX 3070"));
            Assert.NotEqual(id, store.Hash(id));
            Assert.Equal(64, store.Hash(id).Length);
            Assert.NotEqual(store.Hash(id), new RankingStore(Path.Combine(dir, "outro.db"), "outro-segredo-bem-comprido-aqui").Hash(id));
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            string raw = Encoding.Latin1.GetString(File.ReadAllBytes(path));
            Assert.DoesNotContain(id, raw);
            Assert.Equal((1, 0), store.Counts());
            Assert.Equal(1, store.Forget(id));
            Assert.Equal((0, 0), store.Counts());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Medicoes_antigas_saem()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mbx-rank-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new RankingStore(Path.Combine(dir, "ranking.db"), "segredo-de-teste-longo-o-bastante");
            store.SaveBench(Bench(NewId(), 1000, "RTX 3070"));
            store.Purge(months: 18);
            Assert.Equal(1, store.Counts().Bench);                    // recente fica
            store.Purge(months: -1);                                  // limite no futuro: tudo é "antigo"
            Assert.Equal(0, store.Counts().Bench);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
