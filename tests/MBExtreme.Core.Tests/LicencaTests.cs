using System.Net;
using System.Security.Cryptography;
using System.Text;
using MBExtreme.Core.Licensing;
using Xunit;

namespace MBExtreme.Core.Tests;

public class LicencaTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static (ECDsa Key, string Public) NewKey()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key, LicenseToken.ExportPublicKey(key));
    }

    private static LicensePayload Payload(string device = "dev1", int days = 7, string plan = "pro") => new()
    {
        Subject = "u1", Email = "a@b.com", Plan = plan, MaxDevices = 2, Device = device,
        IssuedAt = Now, ExpiresAt = Now.AddDays(days), Status = "active",
    };

    [Fact]
    public void Licenca_assinada_e_conferida_com_a_chave_publica()
    {
        var (key, pub) = NewKey();
        string token = LicenseToken.Sign(Payload(), key);
        Assert.StartsWith("mbx1.", token);
        var read = LicenseToken.Verify(token, pub);
        Assert.NotNull(read);
        Assert.Equal("pro", read!.Plan);
        Assert.Equal("dev1", read.Device);
    }

    [Fact]
    public void Licenca_adulterada_ou_de_outra_chave_e_rejeitada()
    {
        var (key, pub) = NewKey();
        var (_, otherPub) = NewKey();
        string token = LicenseToken.Sign(Payload(plan: "free"), key);
        Assert.Null(LicenseToken.Verify(token, otherPub));

        // troca o plano no conteúdo mantendo a assinatura
        var parts = token.Split('.');
        string forged = LicenseToken.Base64Url(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(LicenseToken.FromBase64Url(parts[1])).Replace("\"free\"", "\"pro\"")));
        Assert.Null(LicenseToken.Verify($"{parts[0]}.{forged}.{parts[2]}", pub));
        Assert.Null(LicenseToken.Verify("lixo", pub));
        Assert.Null(LicenseToken.Verify(token, "chave-invalida"));
    }

    [Fact]
    public void Estado_da_licenca_depende_de_validade_e_dispositivo()
    {
        var (key, pub) = NewKey();
        string token = LicenseToken.Sign(Payload("meu-pc", 7), key);

        Assert.Equal(LicenseState.None, LicenseVerifier.Evaluate(null, pub, "meu-pc", Now).State);
        var ok = LicenseVerifier.Evaluate(token, pub, "meu-pc", Now.AddDays(1));
        Assert.True(ok.IsValid);
        Assert.Equal("pro", ok.Plan);
        Assert.Equal(LicenseState.WrongDevice, LicenseVerifier.Evaluate(token, pub, "outro-pc", Now).State);
        var expired = LicenseVerifier.Evaluate(token, pub, "meu-pc", Now.AddDays(8));
        Assert.Equal(LicenseState.Expired, expired.State);
        Assert.Equal("free", expired.Plan);
        Assert.Equal(LicenseState.Invalid, LicenseVerifier.Evaluate("mbx1.a.b", pub, "meu-pc", Now).State);
        Assert.Equal(LicenseState.Invalid, LicenseVerifier.Evaluate(token, "", "meu-pc", Now).State);
    }

    [Fact]
    public void Relogio_voltado_para_tras_invalida_ate_corrigir()
    {
        var (key, pub) = NewKey();
        string token = LicenseToken.Sign(Payload("pc", 30), key);
        var seen = Now.AddDays(10);
        Assert.Equal(LicenseState.ClockTampered, LicenseVerifier.Evaluate(token, pub, "pc", Now, seen).State);
        Assert.True(LicenseVerifier.Evaluate(token, pub, "pc", seen.AddHours(-3), seen).IsValid); // diferença pequena é tolerada
    }

    [Fact]
    public void Renovar_so_quando_falta_pouco()
    {
        var (key, pub) = NewKey();
        string token = LicenseToken.Sign(Payload("pc", 7), key);
        Assert.False(LicenseVerifier.Evaluate(token, pub, "pc", Now.AddDays(1)).ShouldRefresh);
        Assert.True(LicenseVerifier.Evaluate(token, pub, "pc", Now.AddDays(5)).ShouldRefresh);
        Assert.True(LicenseVerifier.Evaluate(token, pub, "pc", Now.AddDays(9)).ShouldRefresh);
    }

    [Fact]
    public void Historico_guarda_so_a_ultima_e_a_penultima()
    {
        var h = LicenseHistory.Push(Array.Empty<string>(), "a");
        h = LicenseHistory.Push(h, "b");
        h = LicenseHistory.Push(h, "c");
        Assert.Equal(new[] { "c", "b" }, h);
        Assert.Equal(new[] { "b", "c" }, LicenseHistory.Push(h, "b")); // repetida sobe para a frente sem duplicar
        Assert.Equal(new[] { "x" }, LicenseHistory.Push(new[] { "", "  " }, "x"));
    }

    [Fact]
    public void Identificador_do_pc_e_estavel_e_nao_expoe_o_guid()
    {
        string a = DeviceIdentity.Compute("ABCD-1234");
        Assert.Equal(a, DeviceIdentity.Compute("abcd-1234 "));
        Assert.NotEqual(a, DeviceIdentity.Compute("abcd-1235"));
        Assert.Equal(32, a.Length);
        Assert.DoesNotContain("abcd", a);
    }

    [Fact]
    public void Politica_so_bloqueia_paginas_pro_quando_a_aplicacao_esta_ligada()
    {
        var (key, pub) = NewKey();
        var none = LicenseVerifier.Evaluate(null, pub, "pc", Now);
        var pro = LicenseVerifier.Evaluate(LicenseToken.Sign(Payload("pc", 7, "pro"), key), pub, "pc", Now);
        var free = LicenseVerifier.Evaluate(LicenseToken.Sign(Payload("pc", 7, "free"), key), pub, "pc", Now);

        Assert.True(FeaturePolicy.IsAllowed("record", none, enforce: false));
        Assert.False(FeaturePolicy.IsAllowed("record", none, enforce: true));
        Assert.False(FeaturePolicy.IsAllowed("record", free, enforce: true));
        Assert.True(FeaturePolicy.IsAllowed("record", pro, enforce: true));
        Assert.True(FeaturePolicy.IsAllowed("cleanup", none, enforce: true));
        Assert.True(FeaturePolicy.RequiresPro("bench"));
        Assert.False(FeaturePolicy.RequiresPro("home"));
    }

    [Fact]
    public void Recurso_avulso_na_licenca_libera_a_pagina()
    {
        var (key, pub) = NewKey();
        var payload = Payload("pc", 7, "free") with { Features = new List<string> { "record" } };
        var status = LicenseVerifier.Evaluate(LicenseToken.Sign(payload, key), pub, "pc", Now);
        Assert.True(FeaturePolicy.IsAllowed("record", status, enforce: true));
        Assert.False(FeaturePolicy.IsAllowed("bench", status, enforce: true));
    }

    // ---- cliente HTTP

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Paths { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Cliente_inicia_o_login_e_le_a_resposta_do_polling()
    {
        var handler = new FakeHandler(r => r.RequestUri!.AbsolutePath switch
        {
            "/v1/device/code" => Json(HttpStatusCode.OK, "{\"deviceCode\":\"dc\",\"userCode\":\"ABCD-1234\",\"verificationUri\":\"http://x/activate\",\"intervalSeconds\":2,\"expiresInSeconds\":600}"),
            _ => Json(HttpStatusCode.OK, "{\"status\":\"pending\"}"),
        });
        var client = new LicenseClient(new HttpClient(handler), "http://servidor/");
        var code = await client.StartDeviceLoginAsync("dev", "PC", CancellationToken.None);
        Assert.Equal("ABCD-1234", code.UserCode);
        Assert.Equal(PollState.Pending, (await client.PollAsync("dc", "dev", CancellationToken.None)).State);
    }

    [Theory]
    [InlineData("{\"status\":\"approved\",\"license\":\"mbx1.a.b\"}", PollState.Approved)]
    [InlineData("{\"status\":\"slow_down\"}", PollState.SlowDown)]
    [InlineData("{\"status\":\"denied\"}", PollState.Denied)]
    [InlineData("{\"status\":\"expired\"}", PollState.Expired)]
    public async Task Cliente_traduz_cada_estado_do_polling(string body, PollState expected)
    {
        var client = new LicenseClient(new HttpClient(new FakeHandler(_ => Json(HttpStatusCode.OK, body))), "http://s");
        Assert.Equal(expected, (await client.PollAsync("dc", "dev", CancellationToken.None)).State);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "{\"license\":\"mbx1.x.y\"}", RefreshState.Renewed)]
    [InlineData(HttpStatusCode.PaymentRequired, "{\"message\":\"assinatura vencida\"}", RefreshState.Revoked)]
    [InlineData(HttpStatusCode.Conflict, "{}", RefreshState.DeviceLimit)]
    [InlineData(HttpStatusCode.InternalServerError, "oops", RefreshState.Failed)]
    public async Task Cliente_traduz_a_renovacao(HttpStatusCode code, string body, RefreshState expected)
    {
        var client = new LicenseClient(new HttpClient(new FakeHandler(_ => Json(code, body))), "http://s");
        Assert.Equal(expected, (await client.RefreshAsync("mbx1.a.b", "dev", CancellationToken.None)).State);
    }

    [Fact]
    public async Task Sem_rede_vira_excecao_clara()
    {
        var http = new HttpClient(new FakeHandler(_ => throw new HttpRequestException("falhou")));
        var client = new LicenseClient(http, "http://s");
        await Assert.ThrowsAsync<LicenseNetworkException>(() => client.RefreshAsync("t", "d", CancellationToken.None));
    }

    [Fact]
    public void Configuracao_vem_das_configuracoes_e_so_vale_completa()
    {
        var settings = new MBExtreme.Core.Settings.AppSettings { LicenseServerUrl = "http://s/", LicensePublicKey = "k", LicenseEnforce = true };
        var cfg = LicenseConfig.From(settings);
        Assert.True(cfg.Configured);
        Assert.Equal("http://s", cfg.ServerUrl);
        Assert.True(cfg.Enforce);
        Assert.False(LicenseConfig.From(new MBExtreme.Core.Settings.AppSettings()).Configured);
    }
}
