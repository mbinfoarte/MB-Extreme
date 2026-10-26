using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MBExtreme.LicenseServer.Tests;

public class AtualizacaoServidorTests : IClassFixture<ServerFixture>
{
    private readonly ServerFixture _server;
    public AtualizacaoServidorTests(ServerFixture server) => _server = server;

    private const string Token = "token-de-atualizacao-com-mais-de-16";
    private static readonly string Manifest = "{\"build\":41,\"version\":\"0.9.41\",\"url\":\"https://exemplo.com/setup.exe\",\"sha256\":\"4c3f0767eb20e1239899938f36878cb169a59d7619e1fc6a2fc68c1f875f9e42\",\"size\":10,\"notes\":\"x\",\"minimumBuild\":0}";

    private HttpClient Client() => _server.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Sem_manifesto_publicado_a_consulta_devolve_404_e_depois_do_envio_devolve_o_manifesto()
    {
        var client = Client();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/v1/app/latest")).StatusCode);

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/admin/app/latest") { Content = new StringContent(Manifest, Encoding.UTF8, "application/json") };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);

        var got = await client.GetAsync("/v1/app/latest");
        Assert.Equal(HttpStatusCode.OK, got.StatusCode);
        Assert.Contains("\"build\":41", await got.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Envio_sem_token_certo_ou_com_manifesto_invalido_e_recusado()
    {
        var client = Client();
        var wrong = new HttpRequestMessage(HttpMethod.Post, "/v1/admin/app/latest") { Content = new StringContent(Manifest) };
        wrong.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "token-errado-token-errado");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(wrong)).StatusCode);

        var bad = new HttpRequestMessage(HttpMethod.Post, "/v1/admin/app/latest") { Content = new StringContent("{\"build\":5,\"url\":\"http://inseguro/x\",\"sha256\":\"abc\"}") };
        bad.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(bad)).StatusCode);
    }
}
