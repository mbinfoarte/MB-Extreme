using System.Net;
using System.Text;
using MBExtreme.Core.Ranking;
using Xunit;

namespace MBExtreme.Core.Tests;

public class RankingTests
{
    private static string Id(char c = 'a') => new string(c, 32);

    private static BenchSubmission Bench(string? id = null, int score = 5000, string gpu = "NVIDIA GeForce RTX 3070", string cpu = "AMD Ryzen 5 5600X 6-Core Processor") =>
        new(id ?? Id(), cpu, gpu, 16, 6, 12, score, 1000, 8000, 22, 500, 9000, 300, 2500, "0.9.0");

    private static GameSubmission Game(double avg = 90, string game = "FC26", int seconds = 180) =>
        new(Id(), game, "AMD Ryzen 5 5600X 6-Core Processor", "NVIDIA GeForce RTX 3070", 16, avg, avg * 0.7, avg * 0.5, seconds, "0.9.0");

    [Fact]
    public void Envio_valido_passa_e_cada_defeito_e_recusado()
    {
        Assert.Null(RankingRules.Validate(Bench()));
        Assert.NotNull(RankingRules.Validate(Bench(id: "curto")));
        Assert.NotNull(RankingRules.Validate(Bench(id: new string('!', 32))));
        Assert.NotNull(RankingRules.Validate(Bench(score: 0)));
        Assert.NotNull(RankingRules.Validate(Bench(score: 20_000_000)));
        Assert.NotNull(RankingRules.Validate(Bench(gpu: "")));
        Assert.NotNull(RankingRules.Validate(Bench(gpu: "<script>alert(1)</script>")));
        Assert.NotNull(RankingRules.Validate(Bench(cpu: new string('A', 121))));
        Assert.NotNull(RankingRules.Validate(Bench() with { RamGb = 0 }));
        Assert.NotNull(RankingRules.Validate(Bench() with { CpuMultiMops = double.NaN }));
        Assert.NotNull(RankingRules.Validate(Bench() with { MemoryGBps = -1 }));
    }

    [Fact]
    public void Envio_de_jogo_exige_fps_coerente_e_tempo_minimo()
    {
        Assert.Null(RankingRules.Validate(Game()));
        Assert.NotNull(RankingRules.Validate(Game(avg: 0)));
        Assert.NotNull(RankingRules.Validate(Game(avg: 5000)));
        Assert.NotNull(RankingRules.Validate(Game(seconds: 10)));
        Assert.NotNull(RankingRules.Validate(Game() with { Low1Fps = 500 }));          // o 1% pior não pode passar da média
        Assert.NotNull(RankingRules.Validate(Game(game: "")));
        Assert.Equal("fc26", RankingRules.GameKey("  FC 26 "));
        Assert.Equal("eafc26", RankingRules.GameKey("EA FC™ 26") == "eafc26" ? "eafc26" : "x");
    }

    [Fact]
    public void Contas_de_percentil_e_mediana()
    {
        var values = Enumerable.Range(1, 100).Select(i => (double)i).ToList();
        Assert.Equal(50.5, RankingMath.Median(values), 6);
        Assert.Equal(25.75, RankingMath.Percentile(values, 25), 6);
        Assert.Equal(49.5, RankingMath.BeatsPercent(values, 50), 6);                       // 49 abaixo + meio ponto do empate
        Assert.Equal(0, RankingMath.BeatsPercent(new List<double>(), 10));
        Assert.Equal(0, RankingMath.Median(new List<double>()));
        Assert.Equal(7, RankingMath.Percentile(new List<double> { 7 }, 90));
    }

    [Fact]
    public void Posicao_so_aparece_com_amostras_suficientes()
    {
        var few = RankingMath.Stand(Enumerable.Range(1, 10).Select(i => (double)i).ToList(), 5, RankingRules.MinSamples);
        Assert.False(few.Enough);
        Assert.Equal(10, few.Samples);
        Assert.Equal(0, few.BeatsPercent);
        var many = RankingMath.Stand(Enumerable.Range(1, 40).Select(i => (double)i).ToList(), 30, RankingRules.MinSamples);
        Assert.True(many.Enough);
        Assert.InRange(many.BeatsPercent, 70, 75);
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(string Url, string Body)> Calls { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((request.RequestUri!.ToString(), request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return respond(request);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Cliente_envia_em_json_web_e_le_a_posicao()
    {
        var handler = new FakeHandler(_ => Json("{\"sameGpu\":{\"samples\":40,\"enough\":true,\"beatsPercent\":72.5,\"median\":4800},\"all\":{\"samples\":300,\"enough\":true,\"beatsPercent\":60,\"median\":3000}}"));
        var client = new RankingClient(new HttpClient(handler), "https://exemplo.test/");
        var standing = await client.SubmitBenchAsync(Bench());
        Assert.NotNull(standing);
        Assert.Equal(72.5, standing!.SameGpu.BeatsPercent);
        Assert.True(standing.All.Enough);
        var call = Assert.Single(handler.Calls);
        Assert.Equal("https://exemplo.test/v1/rank/bench", call.Url);
        Assert.Contains("\"installId\"", call.Body);
        Assert.Contains("\"gpu\":\"NVIDIA GeForce RTX 3070\"", call.Body);
        Assert.DoesNotContain("email", call.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Falha_de_rede_ou_resposta_ruim_devolve_nulo_e_sem_servidor_nem_tenta()
    {
        var down = new RankingClient(new HttpClient(new FakeHandler(_ => throw new HttpRequestException("sem rede"))), "https://exemplo.test");
        Assert.Null(await down.SubmitBenchAsync(Bench()));
        Assert.Null(await down.SubmitGameAsync(Game()));
        Assert.False(await down.ForgetAsync(Id()));

        var bad = new RankingClient(new HttpClient(new FakeHandler(_ => Json("{}", HttpStatusCode.TooManyRequests))), "https://exemplo.test");
        Assert.Null(await bad.SubmitBenchAsync(Bench()));

        var garbage = new RankingClient(new HttpClient(new FakeHandler(_ => Json("isto não é json"))), "https://exemplo.test");
        Assert.Null(await garbage.SubmitGameAsync(Game()));

        var handler = new FakeHandler(_ => Json("{}"));
        var none = new RankingClient(new HttpClient(handler), "");
        Assert.False(none.Configured);
        Assert.Null(await none.SubmitBenchAsync(Bench()));
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task Pedido_para_esquecer_confirma_quando_o_servidor_responde()
    {
        var handler = new FakeHandler(_ => Json("{\"deleted\":2}"));
        var client = new RankingClient(new HttpClient(handler), "https://exemplo.test");
        Assert.True(await client.ForgetAsync(Id()));
        Assert.Equal("https://exemplo.test/v1/rank/forget", handler.Calls[0].Url);
    }
}
