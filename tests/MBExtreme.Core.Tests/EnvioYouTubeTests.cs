using System.Net;
using MBExtreme.Core.Recording;
using Xunit;

namespace MBExtreme.Core.Tests;

public class EnvioYouTubeTests
{
    [Fact]
    public void Endereco_de_login_pede_so_o_envio_com_pkce()
    {
        var (verifier, challenge) = YouTubeUpload.NewPkce();
        Assert.True(verifier.Length >= 43);
        Assert.DoesNotContain("=", challenge);
        string url = YouTubeUpload.AuthUrl(" id.apps.googleusercontent.com ", "http://127.0.0.1:5000/", challenge, "abc");
        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?client_id=id.apps.googleusercontent.com&", url);
        Assert.Contains("scope=" + Uri.EscapeDataString(YouTubeUpload.Scope), url);
        Assert.Contains("code_challenge_method=S256", url);
        Assert.Contains("redirect_uri=http%3A%2F%2F127.0.0.1%3A5000%2F", url);
        Assert.Contains("access_type=offline", url);
    }

    [Fact]
    public void Retorno_do_google_confere_o_state()
    {
        Assert.Equal(("4/0Ab-c d", null), YouTubeUpload.ParseCallback("/?state=xyz&code=4%2F0Ab-c+d&scope=a", "xyz"));
        Assert.Contains("não confere", YouTubeUpload.ParseCallback("/?state=outro&code=1", "xyz").Error);
        Assert.Contains("não autorizou", YouTubeUpload.ParseCallback("/?error=access_denied&state=xyz", "xyz").Error);
        Assert.NotNull(YouTubeUpload.ParseCallback("/?state=xyz", "xyz").Error);
    }

    [Fact]
    public void Token_e_renovacao()
    {
        var now = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
        var t = YouTubeUpload.ParseToken("{\"access_token\":\"a1\",\"expires_in\":3599,\"refresh_token\":\"r1\"}", now);
        Assert.Equal("r1", t.RefreshToken);
        Assert.False(t.Expired(now.AddMinutes(50)));
        Assert.True(t.Expired(now.AddMinutes(59)));
        var renewed = YouTubeUpload.ParseToken("{\"access_token\":\"a2\",\"expires_in\":3599}", now, "r1");
        Assert.Equal("r1", renewed.RefreshToken);
        var ex = Assert.Throws<InvalidOperationException>(() => YouTubeUpload.ParseToken("{\"error\":\"invalid_grant\"}", now));
        Assert.Contains("Conecte a conta de novo", ex.Message);
    }

    [Fact]
    public void Titulo_descricao_e_dados()
    {
        Assert.Equal("Gol do jogo", YouTubeUpload.CleanTitle(" <Gol> do jogo ", "x"));
        Assert.Equal("gravacao-1", YouTubeUpload.CleanTitle("   ", "gravacao-1"));
        Assert.Equal(100, YouTubeUpload.CleanTitle(new string('a', 150), "x").Length);
        Assert.Equal("ab", YouTubeUpload.CleanDescription("<a>b"));
        string json = YouTubeUpload.Metadata("Título", "Desc", YouTubePrivacy.Unlisted);
        Assert.Contains("\"privacyStatus\":\"unlisted\"", json);
        Assert.Contains("\"categoryId\":\"20\"", json);
        Assert.Equal(1048576, YouTubeUpload.ReceivedFromRange("bytes=0-1048575"));
        Assert.Equal(0, YouTubeUpload.ReceivedFromRange(null));
        Assert.Equal("video/x-matroska", YouTubeUpload.ContentType("C:/v/a.MKV"));
        Assert.Contains("limite", YouTubeUpload.ApiError(HttpStatusCode.Forbidden, "{\"error\":{\"errors\":[{\"reason\":\"quotaExceeded\"}]}}"));
    }

    /// <summary>Servidor falso do YouTube: recebe pedaços, cai uma vez no meio e no fim devolve o código do vídeo.</summary>
    private sealed class FakeYouTube : HttpMessageHandler
    {
        public long Received;
        public bool FailedOnce;
        public int Puts;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal("Bearer tok", request.Headers.Authorization?.ToString());
            if (request.Method == HttpMethod.Post)
            {
                var start = new HttpResponseMessage(HttpStatusCode.OK);
                start.Headers.Location = new Uri("https://upload.example/sessao/1");
                return start;
            }

            Puts++;
            var range = request.Content!.Headers.ContentRange!;
            byte[] data = await request.Content.ReadAsByteArrayAsync(token);
            if (range.HasRange)
            {
                Assert.Equal(Received, range.From);
                if (!FailedOnce && Received > 0) { FailedOnce = true; return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable); }   // caiu: não guardou este pedaço
                Received += data.Length;
            }
            if (Received >= range.Length)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"abc123\"}") };
            var more = new HttpResponseMessage((HttpStatusCode)308);
            if (Received > 0) more.Headers.TryAddWithoutValidation("Range", $"bytes=0-{Received - 1}");
            return more;
        }
    }

    [Fact]
    public async Task Envio_em_pedacos_continua_depois_de_cair()
    {
        string file = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(file, new byte[YouTubeUpload.ChunkBytes * 2 + 1234]);
            var fake = new FakeYouTube();
            using var http = new HttpClient(fake);
            var session = await YouTubeUpload.StartSessionAsync(http, "tok", file, YouTubeUpload.Metadata("t", "", YouTubePrivacy.Private), CancellationToken.None);
            var reports = new List<double>();
            string id = await YouTubeUpload.UploadAsync(http, () => Task.FromResult("tok"), session, file, new Progress<double>(reports.Add), CancellationToken.None);
            Assert.Equal("abc123", id);
            Assert.True(fake.FailedOnce);
            Assert.Equal(new FileInfo(file).Length, fake.Received);
            Assert.Equal("https://youtu.be/abc123", YouTubeUpload.VideoUrl(id));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Retorno_local_recebe_o_codigo()
    {
        using var receiver = new YouTubeUpload.LoopbackReceiver();
        Assert.StartsWith("http://127.0.0.1:", receiver.RedirectUri);
        var wait = receiver.WaitAsync(CancellationToken.None);
        using var http = new HttpClient();
        var page = await http.GetStringAsync(receiver.RedirectUri + "?state=s1&code=c1");
        Assert.Contains("Pronto", page);
        Assert.Equal(("c1", null), YouTubeUpload.ParseCallback(await wait, "s1"));
    }
}
