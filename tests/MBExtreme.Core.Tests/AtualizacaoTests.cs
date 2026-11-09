using System.Net;
using System.Security.Cryptography;
using System.Text;
using MBExtreme.Core.Update;
using Xunit;

namespace MBExtreme.Core.Tests;

public class AtualizacaoTests
{
    private const string Hash = "4c3f0767eb20e1239899938f36878cb169a59d7619e1fc6a2fc68c1f875f9e42";

    private static string Json(int build = 40, string url = "https://exemplo.com/setup.exe", string sha = Hash, int min = 0) =>
        $"{{\"build\":{build},\"version\":\"0.9.{build}\",\"url\":\"{url}\",\"sha256\":\"{sha}\",\"size\":1000,\"notes\":\"Novidades\",\"minimumBuild\":{min}}}";

    [Fact]
    public void Le_o_manifesto_e_recusa_o_que_e_inseguro_ou_incompleto()
    {
        var ok = AppUpdates.Parse(Json());
        Assert.NotNull(ok);
        Assert.Equal((40, "Novidades"), (ok!.Build, ok.Notes));

        Assert.Null(AppUpdates.Parse(Json(url: "http://exemplo.com/setup.exe")));      // sem https
        Assert.Null(AppUpdates.Parse(Json(url: "file:///C:/x.exe")));
        Assert.Null(AppUpdates.Parse(Json(url: "https://u:senha@exemplo.com/x.exe")));  // credenciais na URL
        Assert.Null(AppUpdates.Parse(Json(sha: "123")));                                 // impressão digital inválida
        Assert.Null(AppUpdates.Parse(Json(build: 0)));
        Assert.Null(AppUpdates.Parse("não é json"));
        Assert.Null(AppUpdates.Parse(""));
        Assert.Null(AppUpdates.Parse("[1,2]"));
    }

    [Theory]
    [InlineData(38, 38, 0, UpdateState.UpToDate)]
    [InlineData(39, 38, 0, UpdateState.UpToDate)]
    [InlineData(38, 40, 0, UpdateState.Available)]
    [InlineData(38, 40, 39, UpdateState.Required)]   // instalada abaixo da mínima: atualização obrigatória
    [InlineData(39, 40, 39, UpdateState.Available)]  // já está na mínima: só avisa
    public void Avisa_quando_ha_compilacao_nova_e_exige_abaixo_da_minima(int current, int latest, int minimum, UpdateState expected)
    {
        var m = AppUpdates.Parse(Json(build: latest, min: minimum))!;
        Assert.Equal(expected, AppUpdates.Evaluate(current, m));
    }

    private sealed class Handler : HttpMessageHandler
    {
        private readonly byte[] _data;
        public Handler(byte[] data) => _data = data;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_data) });
    }

    [Fact]
    public async Task Download_confere_a_impressao_digital_e_apaga_o_arquivo_adulterado()
    {
        string folder = Path.Combine(Path.GetTempPath(), "mbx-upd-" + Guid.NewGuid().ToString("N"));
        try
        {
            var data = Encoding.UTF8.GetBytes("instalador de teste");
            string sha = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
            var good = AppUpdates.Parse(Json(sha: sha))!;
            using var http = new HttpClient(new Handler(data));
            string path = await AppUpdates.DownloadAsync(http, good, folder, null, CancellationToken.None);
            Assert.True(File.Exists(path));
            Assert.Equal(sha, AppUpdates.Sha256Of(path));

            var bad = AppUpdates.Parse(Json(build: 41, sha: Hash))!;   // impressão digital que não é a do arquivo
            await Assert.ThrowsAsync<InvalidDataException>(() => AppUpdates.DownloadAsync(http, bad, folder, null, CancellationToken.None));
            Assert.False(File.Exists(Path.Combine(folder, "MBExtreme-Beta-Setup-41.exe")));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task Sem_servidor_https_nao_pergunta_nada()
    {
        using var http = new HttpClient(new Handler(Array.Empty<byte>()));
        Assert.Null(await AppUpdates.CheckAsync(http, ""));
        Assert.Null(await AppUpdates.CheckAsync(http, "http://servidor.exemplo"));
    }
}
