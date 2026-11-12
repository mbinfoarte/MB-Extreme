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
        // cada plano inclui o anterior; "pro" (licenças antigas) vale como o maior
        Assert.True(FeaturePolicy.PlanAllows("essencial", "drivers"));
        Assert.False(FeaturePolicy.PlanAllows("essencial", "bench"));
        Assert.True(FeaturePolicy.PlanAllows("gamer", "bench"));
        Assert.False(FeaturePolicy.PlanAllows("gamer", "studio"));
        Assert.True(FeaturePolicy.PlanAllows("creator", "studio"));
        Assert.False(FeaturePolicy.PlanAllows("creator", "postinstall"));
        Assert.True(FeaturePolicy.PlanAllows("tecnico", "postinstall"));
        Assert.True(FeaturePolicy.PlanAllows("pro", "postinstall"));
        Assert.False(FeaturePolicy.PlanAllows("free", "drivers"));
        Assert.True(FeaturePolicy.PlanAllows("free", "home"));
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
        // Sem nada configurado, vale o servidor oficial embutido no programa (Cloud Function do site).
        var padrao = LicenseConfig.From(new MBExtreme.Core.Settings.AppSettings());
        Assert.Equal(AppInfo.DefaultServerUrl.Length > 0 && AppInfo.DefaultPublicKey.Length > 0, padrao.Configured);
        if (padrao.Configured)
        {
            Assert.StartsWith("https://", padrao.ServerUrl);
            Assert.Null(LicenseToken.Verify("mbx1.e30.AAAA", AppInfo.DefaultPublicKey)); // chave embutida é uma P-256 válida
        }
    }
}

public class FluxoDeLoginTests
{
    private sealed class Script(params Func<HttpRequestMessage, HttpResponseMessage>[] steps) : HttpMessageHandler
    {
        private int _i;
        public int Calls => _i;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var step = steps[Math.Min(_i++, steps.Length - 1)];
            return Task.FromResult(step(request));
        }
    }

    private static HttpResponseMessage J(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private const string Code = "{\"deviceCode\":\"dc\",\"userCode\":\"ABCD-1234\",\"verificationUri\":\"http://x\",\"intervalSeconds\":2,\"expiresInSeconds\":600}";

    private static Task NoWait(TimeSpan span, CancellationToken token) => Task.CompletedTask;

    [Fact]
    public async Task Espera_a_aprovacao_e_devolve_a_licenca()
    {
        var handler = new Script(_ => J(Code), _ => J("{\"status\":\"pending\"}"), _ => J("{\"status\":\"slow_down\"}"), _ => J("{\"status\":\"approved\",\"license\":\"mbx1.a.b\"}"));
        string? shown = null;
        var result = await LicenseFlow.LoginAsync(new LicenseClient(new HttpClient(handler), "http://s"), "dev", "PC", c => shown = c.UserCode, CancellationToken.None, NoWait);
        Assert.Equal(PollState.Approved, result.State);
        Assert.Equal("mbx1.a.b", result.License);
        Assert.Equal("ABCD-1234", shown);
        Assert.Equal(4, handler.Calls);
    }

    [Fact]
    public async Task Acesso_negado_encerra_o_fluxo()
    {
        var handler = new Script(_ => J(Code), _ => J("{\"status\":\"denied\"}"));
        var result = await LicenseFlow.LoginAsync(new LicenseClient(new HttpClient(handler), "http://s"), "dev", "PC", _ => { }, CancellationToken.None, NoWait);
        Assert.Equal(PollState.Denied, result.State);
    }

    [Fact]
    public async Task Quedas_curtas_de_internet_nao_derrubam_o_login()
    {
        var handler = new Script(_ => J(Code), _ => throw new HttpRequestException("rede"), _ => throw new HttpRequestException("rede"), _ => J("{\"status\":\"approved\",\"license\":\"mbx1.x.y\"}"));
        var result = await LicenseFlow.LoginAsync(new LicenseClient(new HttpClient(handler), "http://s"), "dev", "PC", _ => { }, CancellationToken.None, NoWait);
        Assert.Equal(PollState.Approved, result.State);
    }

    [Fact]
    public async Task Rede_fora_por_muito_tempo_vira_erro()
    {
        var handler = new Script(_ => J(Code), _ => throw new HttpRequestException("rede"));
        await Assert.ThrowsAsync<LicenseNetworkException>(() =>
            LicenseFlow.LoginAsync(new LicenseClient(new HttpClient(handler), "http://s"), "dev", "PC", _ => { }, CancellationToken.None, NoWait));
    }

    [Fact]
    public async Task Codigo_vencido_pelo_relogio_devolve_expirado()
    {
        var now = DateTimeOffset.UtcNow;
        var handler = new Script(_ => J(Code), _ => J("{\"status\":\"pending\"}"));
        var result = await LicenseFlow.LoginAsync(new LicenseClient(new HttpClient(handler), "http://s"), "dev", "PC", _ => { }, CancellationToken.None,
            (span, _) => { now += TimeSpan.FromMinutes(5); return Task.CompletedTask; }, () => now);
        Assert.Equal(PollState.Expired, result.State);
    }
}

public class PerfilDoClienteTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly LicenseConfig Configured = new(true, "http://s", "k");

    private static LicenseStatus Status(string plan, int periodDays, LicenseState state = LicenseState.Valid, string name = "Maria Silva") =>
        new(state, new LicensePayload { Name = name, Email = "maria@exemplo.com", Plan = plan, PeriodEnd = Now.AddDays(periodDays), ExpiresAt = Now.AddDays(5) }, "", TimeSpan.FromDays(5));

    [Fact]
    public void Pro_mostra_os_dias_restantes_da_assinatura_e_nao_da_licenca()
    {
        var info = LicenseProfile.Describe(Status("pro", 23), Configured, Now, "pc");
        Assert.Equal("Pro · 23 dias restantes", info.PlanText);
        Assert.Equal(ProfileLevel.Ok, info.Level);
        Assert.False(info.ShowAction);
        Assert.Equal("Maria Silva", info.Name);
        Assert.Equal("MS", info.Initials);
    }

    [Theory]
    [InlineData(7, "Pro · 7 dias restantes")]
    [InlineData(1, "Pro · 1 dia restante")]
    [InlineData(0, "Pro · vence hoje")]
    public void Perto_do_fim_avisa_e_oferece_renovar(int days, string text)
    {
        var info = LicenseProfile.Describe(Status("pro", days), Configured, Now, "pc");
        Assert.Equal(text, info.PlanText);
        Assert.Equal(ProfileLevel.Warn, info.Level);
        Assert.True(info.ShowAction);
        Assert.Equal("Renovar assinatura", info.ActionText);
    }

    [Fact]
    public void Gratuito_e_sem_licenca_convidam_a_ativar_o_pro()
    {
        var free = LicenseProfile.Describe(Status("free", 0), Configured, Now, "pc");
        Assert.Equal("Plano gratuito", free.PlanText);
        Assert.Equal("Ativar o Pro", free.ActionText);

        var none = LicenseProfile.Describe(new LicenseStatus(LicenseState.None, null, "", null), Configured, Now, "mbinfoarte");
        Assert.Equal("mbinfoarte", none.Name);
        Assert.True(none.ShowAction);
        Assert.Equal("Beta aberto", LicenseProfile.Describe(new LicenseStatus(LicenseState.None, null, "", null), new LicenseConfig(false, "", ""), Now, "x").PlanText);
    }

    [Fact]
    public void Licenca_vencida_ou_invalida_aparece_em_vermelho()
    {
        Assert.Equal(ProfileLevel.Bad, LicenseProfile.Describe(Status("pro", 5, LicenseState.Expired), Configured, Now, "pc").Level);
        Assert.Equal("Licença de outro PC", LicenseProfile.Describe(Status("pro", 5, LicenseState.WrongDevice), Configured, Now, "pc").PlanText);
    }

    [Theory]
    [InlineData("Maria Silva", "MS")]
    [InlineData("joao", "J")]
    [InlineData("ana.paula-souza", "AP")]
    [InlineData("", "MB")]
    public void Iniciais_do_avatar(string name, string expected) => Assert.Equal(expected, LicenseProfile.Initials(name));

    [Fact]
    public void Sem_nome_usa_o_inicio_do_email()
    {
        var info = LicenseProfile.Describe(Status("free", 0, name: ""), Configured, Now, "pc");
        Assert.Equal("maria", info.Name);
    }

    [Fact]
    public void Dias_restantes_arredondam_para_cima_e_nunca_ficam_negativos()
    {
        var s = new LicenseStatus(LicenseState.Valid, new LicensePayload { PeriodEnd = Now.AddHours(30) }, "", null);
        Assert.Equal(2, s.DaysLeft(Now));
        Assert.Equal(0, s.DaysLeft(Now.AddDays(10)));
        Assert.Null(new LicenseStatus(LicenseState.None, null, "", null).DaysLeft(Now));
    }
}
