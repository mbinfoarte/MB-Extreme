using System.Net;
using MBExtreme.Core.Recording;
using Xunit;

namespace MBExtreme.Core.Tests;

public class EstatisticasTransmissaoTests
{
    private const string YouTubeLive = """
    {"items":[{"snippet":{"title":"Transmissão ao vivo de Lobão Tech","description":"Conversa sobre jogos","categoryId":"28","liveBroadcastContent":"live"},
      "statistics":{"viewCount":"1534","likeCount":"88","commentCount":"41"},
      "liveStreamingDetails":{"actualStartTime":"2026-10-24T23:57:12Z","concurrentViewers":"165"}}]}
    """;

    [Fact]
    public void Youtube_ao_vivo_traz_espectadores_visualizacoes_curtidas_e_categoria()
    {
        var r = LiveStatsService.ParseYouTube(YouTubeLive);
        Assert.True(r.Ok);
        var s = r.Stats!;
        Assert.True(s.IsLive);
        Assert.Equal(165, s.Viewers);
        Assert.Equal(1534, s.TotalViews);
        Assert.Equal(88, s.Likes);
        Assert.Equal(41, s.Comments);
        Assert.Equal("Ciência e tecnologia", s.Category);
        Assert.Equal("Transmissão ao vivo de Lobão Tech", s.Title);
        Assert.NotNull(s.StartedAt);
        Assert.Equal("YouTube", s.Platform);
    }

    [Fact]
    public void Youtube_fora_do_ar_nao_inventa_espectadores()
    {
        var r = LiveStatsService.ParseYouTube("""{"items":[{"snippet":{"title":"x","categoryId":"20","liveBroadcastContent":"none"},"statistics":{"viewCount":"10"}}]}""");
        Assert.True(r.Ok);
        Assert.False(r.Stats!.IsLive);
        Assert.Null(r.Stats.Viewers);
        Assert.Equal("Jogos", r.Stats.Category);
        Assert.Null(r.Stats.Likes);          // curtidas escondidas pelo dono: vem nulo
    }

    [Theory]
    [InlineData("quotaExceeded", "limite diário")]
    [InlineData("keyInvalid", "não foi aceita")]
    [InlineData("accessNotConfigured", "YouTube Data API v3")]
    [InlineData("algumaOutra", "algumaOutra")]
    public void Erros_do_youtube_viram_mensagens_claras(string reason, string expected)
    {
        var r = LiveStatsService.ParseYouTube("{\"error\":{\"code\":403,\"errors\":[{\"reason\":\"" + reason + "\"}]}}");
        Assert.False(r.Ok);
        Assert.Contains(expected, r.Error);
    }

    [Fact]
    public void Youtube_sem_item_ou_resposta_quebrada_da_erro_amigavel()
    {
        Assert.Contains("não achou", LiveStatsService.ParseYouTube("""{"items":[]}""").Error);
        Assert.Contains("inesperada", LiveStatsService.ParseYouTube("<html>").Error);
    }

    [Fact]
    public void Twitch_no_ar_traz_espectadores_titulo_e_jogo()
    {
        var r = LiveStatsService.ParseTwitchStream("""{"data":[{"type":"live","viewer_count":42,"title":"Rumo ao topo","game_name":"EA Sports FC 26","started_at":"2026-10-24T20:00:00Z"}]}""", "canal");
        Assert.True(r.Ok);
        Assert.True(r.Stats!.IsLive);
        Assert.Equal(42, r.Stats.Viewers);
        Assert.Equal("EA Sports FC 26", r.Stats.Category);
        Assert.Null(r.Stats.TotalViews);     // a Twitch não informa visualizações totais por esta consulta
    }

    [Fact]
    public void Twitch_fora_do_ar_nao_e_erro()
    {
        var r = LiveStatsService.ParseTwitchStream("""{"data":[]}""", "canal");
        Assert.True(r.Ok);
        Assert.False(r.Stats!.IsLive);
    }

    [Fact]
    public void Twitch_com_erro_mostra_o_motivo()
    {
        var r = LiveStatsService.ParseTwitchStream("""{"status":401,"message":"Invalid OAuth token"}""", "canal");
        Assert.False(r.Ok);
        Assert.Contains("Invalid OAuth token", r.Error);
    }

    [Theory]
    [InlineData("AIzaSyA1b2C3d4E5f6G7h8I9j0K1l2M3n4O5p6Q", true)]
    [InlineData("curta", false)]
    [InlineData("chave com espaco e outras coisas aqui!!", false)]
    [InlineData("", false)]
    public void Chave_de_api_so_aceita_o_formato_conhecido(string key, bool ok) => Assert.Equal(ok, LiveStatsService.IsValidApiKey(key));

    [Fact]
    public void Endereco_da_consulta_nao_deixa_entrar_nada_alem_do_codigo_e_da_chave()
    {
        Assert.Equal("https://www.googleapis.com/youtube/v3/videos?part=snippet,statistics,liveStreamingDetails&id=dQw4w9WgXcQ&key=abc",
            LiveStatsService.YouTubeUrl("dQw4w9WgXcQ", "abc"));
        Assert.Contains("%26", LiveStatsService.YouTubeUrl("a&b", "k"));
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("curto", null)]
    public void Codigo_do_youtube_vem_do_endereco_ou_do_codigo(string input, string? expected) => Assert.Equal(expected, ChatLinks.YouTubeVideoId(input));

    [Theory]
    [InlineData("@MeuCanal", "meucanal")]
    [InlineData("https://www.twitch.tv/Gaules", "gaules")]
    [InlineData("a", null)]
    public void Canal_da_twitch_vem_do_nome_ou_do_endereco(string input, string? expected) => Assert.Equal(expected, ChatLinks.TwitchLogin(input));

    [Fact]
    public void Painel_da_plataforma_abre_na_pagina_certa()
    {
        Assert.Equal("https://studio.youtube.com/video/dQw4w9WgXcQ/livestreaming", LiveStatsService.ManageUrl(StreamService.YouTube, "dQw4w9WgXcQ"));
        Assert.Equal("https://dashboard.twitch.tv/stream-manager", LiveStatsService.ManageUrl(StreamService.Twitch, ""));
        Assert.Equal("https://www.facebook.com/live/producer", LiveStatsService.ManageUrl(StreamService.Facebook, null));
        Assert.Null(LiveStatsService.ManageUrl(StreamService.Custom, null));
    }

    [Fact]
    public void Historico_guarda_pico_media_e_limita_o_tamanho()
    {
        var h = new ViewerHistory(10);
        foreach (long v in new long[] { 5, 20, 10 }) h.Add(v);
        Assert.Equal(20, h.Peak);
        Assert.Equal(35.0 / 3, h.Average, 3);
        for (int i = 0; i < 30; i++) h.Add(1);
        Assert.Equal(10, h.Values.Count);
        Assert.Equal(20, h.Peak);          // o pico lembra a sessão toda
        h.Reset();
        Assert.Empty(h.Values);
        Assert.Equal(0, h.Peak);
    }

    // ---- consulta de verdade, com servidor de mentira

    private sealed class Fake(Func<HttpRequestMessage, string> reply) : HttpMessageHandler
    {
        public List<string> Urls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply(request)) });
        }
    }

    [Fact]
    public async Task Consulta_do_youtube_usa_o_codigo_e_a_chave()
    {
        var fake = new Fake(_ => YouTubeLive);
        var service = new LiveStatsService(new HttpClient(fake));
        var r = await service.QueryYouTubeAsync("https://youtu.be/dQw4w9WgXcQ", "AIzaSyA1b2C3d4E5f6G7h8I9j0K1l2M3n4O5p6Q", CancellationToken.None);
        Assert.True(r.Ok);
        Assert.Contains("id=dQw4w9WgXcQ", fake.Urls[0]);
        Assert.Equal(165, r.Stats!.Viewers);
    }

    [Fact]
    public async Task Consulta_sem_dados_nao_sai_da_maquina()
    {
        var fake = new Fake(_ => "{}");
        var service = new LiveStatsService(new HttpClient(fake));
        Assert.False((await service.QueryYouTubeAsync("", "AIzaSyA1b2C3d4E5f6G7h8I9j0K1l2M3n4O5p6Q", CancellationToken.None)).Ok);
        Assert.False((await service.QueryYouTubeAsync("dQw4w9WgXcQ", "x", CancellationToken.None)).Ok);
        Assert.False((await service.QueryTwitchAsync("canal", "curto", "segredo", CancellationToken.None)).Ok);
        Assert.Empty(fake.Urls);
    }

    [Fact]
    public async Task Consulta_da_twitch_pega_o_token_uma_vez_e_reaproveita()
    {
        var fake = new Fake(r => r.RequestUri!.Host == "id.twitch.tv"
            ? """{"access_token":"tok123","expires_in":5000,"token_type":"bearer"}"""
            : """{"data":[{"type":"live","viewer_count":7,"title":"t","game_name":"g","started_at":"2026-10-24T20:00:00Z"}]}""");
        var service = new LiveStatsService(new HttpClient(fake));
        var a = await service.QueryTwitchAsync("@canal", "abcdefghij1234567890", "segredo", CancellationToken.None);
        var b = await service.QueryTwitchAsync("canal", "abcdefghij1234567890", "segredo", CancellationToken.None);
        Assert.Equal(7, a.Stats!.Viewers);
        Assert.Equal(7, b.Stats!.Viewers);
        Assert.Single(fake.Urls.Where(u => u.StartsWith("https://id.twitch.tv")));
        Assert.Equal(2, fake.Urls.Count(u => u.Contains("user_login=canal")));
    }
}
