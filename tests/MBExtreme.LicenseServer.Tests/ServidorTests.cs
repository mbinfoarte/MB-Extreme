using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MBExtreme.Core.Licensing;
using MBExtreme.LicenseServer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MBExtreme.LicenseServer.Tests;

public sealed class ServerFixture : WebApplicationFactory<Program>
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "mbx-server-" + Guid.NewGuid().ToString("N"));
    public const string Secret = "segredo-de-teste-com-mais-de-16";

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        // UseSetting vale antes do código de inicialização do servidor ler a configuração
        builder.UseSetting("Data:Directory", Dir);
        builder.UseSetting("Dev:Enabled", "true");
        builder.UseSetting("Payments:WebhookSecret", Secret);
        builder.UseSetting("Limits:Multiplier", "1000");
        builder.UseSetting("Remote:Technicians", "tecnico@exemplo.com");
        builder.UseSetting("Admin:Emails", "admin@exemplo.com");
        builder.UseSetting("Stripe:WebhookSecret", "whsec_segredo_de_teste");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(Dir, true); } catch (IOException) { }
    }
}

public class ServidorTests : IClassFixture<ServerFixture>
{
    private readonly ServerFixture _server;

    public ServidorTests(ServerFixture server) => _server = server;

    private HttpClient NewClient() => _server.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private static string Unique() => Guid.NewGuid().ToString("N")[..10];

    private static FormUrlEncodedContent Form(params (string, string)[] fields) =>
        new(fields.Select(f => new KeyValuePair<string, string>(f.Item1, f.Item2)));

    private async Task<(HttpClient Client, string Email)> RegisterAsync(string password = "senha-forte-123")
    {
        var client = NewClient();
        string email = $"u{Unique()}@exemplo.com";
        var response = await client.PostAsync("/register", Form(("email", email), ("password", password)));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return (client, email);
    }

    private static async Task<string> CsrfFrom(HttpClient client, string url)
    {
        string html = await client.GetStringAsync(url);
        var match = Regex.Match(html, "name=\"csrf\" value=\"([^\"]+)\"");
        Assert.True(match.Success, "formulário sem token CSRF");
        return match.Groups[1].Value;
    }

    private async Task GrantProAsync(string email, int days = 30, string status = "active")
    {
        var response = await NewClient().PostAsJsonAsync("/dev/grant", new { email, days, status });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static string DeviceId() => Guid.NewGuid().ToString("N");

    /// <summary>Faz o fluxo completo do app: pede o código, aprova no site e recebe a licença.</summary>
    private async Task<(string License, string DeviceId)> ActivateAsync(HttpClient site, string? deviceId = null)
    {
        deviceId ??= DeviceId();
        var api = NewClient();
        var start = await api.PostAsJsonAsync("/v1/device/code", new { deviceId, deviceName = "PC de teste" });
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var code = await start.Content.ReadFromJsonAsync<DeviceCode>();

        string csrf = await CsrfFrom(site, "/activate?code=" + code!.UserCode);
        var approve = await site.PostAsync("/activate", Form(("csrf", csrf), ("code", code.UserCode), ("decision", "approve")));
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        var poll = await api.PostAsJsonAsync("/v1/device/token", new { deviceCode = code.DeviceCodeValue, deviceId });
        using var doc = JsonDocument.Parse(await poll.Content.ReadAsStringAsync());
        Assert.Equal("approved", doc.RootElement.GetProperty("status").GetString());
        return (doc.RootElement.GetProperty("license").GetString()!, deviceId);
    }

    private async Task<string> PublicKeyAsync() => await NewClient().GetStringAsync("/v1/public-key");

    [Fact]
    public async Task Fluxo_completo_cria_conta_assina_ativa_e_o_app_confere_a_licenca()
    {
        var (site, email) = await RegisterAsync();
        await GrantProAsync(email);
        var (license, deviceId) = await ActivateAsync(site);

        var status = LicenseVerifier.Evaluate(license, await PublicKeyAsync(), deviceId, DateTimeOffset.UtcNow);
        Assert.True(status.IsValid);
        Assert.Equal("pro", status.Plan);
        Assert.Equal(email, status.Payload!.Email);
        Assert.Equal(2, status.Payload.MaxDevices);
        Assert.Equal(LicenseState.WrongDevice, LicenseVerifier.Evaluate(license, await PublicKeyAsync(), DeviceId(), DateTimeOffset.UtcNow).State);
    }

    [Fact]
    public async Task Licenca_traz_o_nome_do_cliente_e_o_fim_da_assinatura()
    {
        var client = NewClient();
        string email = $"n{Unique()}@exemplo.com";
        await client.PostAsync("/register", Form(("name", "Maria Silva"), ("email", email), ("password", "senha-forte-123")));
        await GrantProAsync(email, 20);
        var (license, deviceId) = await ActivateAsync(client);
        var status = LicenseVerifier.Evaluate(license, await PublicKeyAsync(), deviceId, DateTimeOffset.UtcNow);
        Assert.Equal("Maria Silva", status.Payload!.Name);
        Assert.Equal(20, status.DaysLeft(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Cliente_real_do_app_fala_com_o_servidor()
    {
        var (site, email) = await RegisterAsync();
        await GrantProAsync(email);
        string deviceId = DeviceId();
        var client = new LicenseClient(_server.CreateClient(), "http://localhost");

        var code = await client.StartDeviceLoginAsync(deviceId, "PC do teste", CancellationToken.None);
        Assert.Equal(PollState.Pending, (await client.PollAsync(code.DeviceCodeValue, deviceId, CancellationToken.None)).State);
        Assert.Contains("/activate?code=", code.VerificationUri);

        string csrf = await CsrfFrom(site, "/activate?code=" + code.UserCode);
        await site.PostAsync("/activate", Form(("csrf", csrf), ("code", code.UserCode), ("decision", "approve")));
        await Task.Delay(3200); // intervalo mínimo entre consultas
        var approved = await client.PollAsync(code.DeviceCodeValue, deviceId, CancellationToken.None);
        Assert.Equal(PollState.Approved, approved.State);

        var refreshed = await client.RefreshAsync(approved.License!, deviceId, CancellationToken.None);
        Assert.Equal(RefreshState.Renewed, refreshed.State);
        Assert.True(LicenseVerifier.Evaluate(refreshed.License, await PublicKeyAsync(), deviceId, DateTimeOffset.UtcNow).IsValid);

        await client.DeactivateAsync(refreshed.License!, deviceId, CancellationToken.None);
        Assert.Equal(RefreshState.Revoked, (await client.RefreshAsync(refreshed.License!, deviceId, CancellationToken.None)).State);
    }

    [Fact]
    public async Task Consultas_rapidas_demais_recebem_slow_down()
    {
        var api = NewClient();
        string deviceId = DeviceId();
        var code = await (await api.PostAsJsonAsync("/v1/device/code", new { deviceId, deviceName = "x" })).Content.ReadFromJsonAsync<DeviceCode>();
        await api.PostAsJsonAsync("/v1/device/token", new { deviceCode = code!.DeviceCodeValue, deviceId });
        var second = await api.PostAsJsonAsync("/v1/device/token", new { deviceCode = code.DeviceCodeValue, deviceId });
        Assert.Contains("slow_down", await second.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Conta_sem_assinatura_recebe_licenca_gratuita()
    {
        var (site, _) = await RegisterAsync();
        string deviceId0 = DeviceId();
        _ = _server.Server;
        new Db(Path.Combine(_server.Dir, "licenses.db")).TryMarkTrial(RemoteRelay.Hash(deviceId0), 0);   // este PC já usou o teste grátis
        var (license, deviceId) = await ActivateAsync(site, deviceId0);
        var status = LicenseVerifier.Evaluate(license, await PublicKeyAsync(), deviceId, DateTimeOffset.UtcNow);
        Assert.True(status.IsValid);
        Assert.Equal("free", status.Plan);
        Assert.False(FeaturePolicy.IsAllowed("record", status, enforce: true));
    }

    [Fact]
    public async Task Primeira_ativacao_sem_assinatura_ganha_teste_gratis_pro()
    {
        var (site, _) = await RegisterAsync();
        var (license, deviceId) = await ActivateAsync(site);
        var status = LicenseVerifier.Evaluate(license, await PublicKeyAsync(), deviceId, DateTimeOffset.UtcNow);
        Assert.Equal("pro", status.Plan);
        Assert.Equal("trialing", status.Payload!.Status);
        Assert.InRange(status.DaysLeft(DateTimeOffset.UtcNow) ?? -1, 6, 7);
    }

    [Fact]
    public async Task Limite_de_computadores_e_respeitado_e_remover_libera_vaga()
    {
        var (site, _) = await RegisterAsync(); // plano gratuito: 1 computador
        var (_, firstDevice) = await ActivateAsync(site);

        var api = NewClient();
        string second = DeviceId();
        var code = await (await api.PostAsJsonAsync("/v1/device/code", new { deviceId = second, deviceName = "Segundo" })).Content.ReadFromJsonAsync<DeviceCode>();
        string csrf = await CsrfFrom(site, "/activate?code=" + code!.UserCode);
        var blocked = await site.PostAsync("/activate", Form(("csrf", csrf), ("code", code.UserCode), ("decision", "approve")));
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        string accountCsrf = await CsrfFrom(site, "/account");
        await site.PostAsync("/account/device/remove", Form(("csrf", accountCsrf), ("device", firstDevice)));
        var allowed = await site.PostAsync("/activate", Form(("csrf", csrf), ("code", code.UserCode), ("decision", "approve")));
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task Negar_no_site_resulta_em_negado_no_app()
    {
        var (site, _) = await RegisterAsync();
        var api = NewClient();
        string deviceId = DeviceId();
        var code = await (await api.PostAsJsonAsync("/v1/device/code", new { deviceId, deviceName = "x" })).Content.ReadFromJsonAsync<DeviceCode>();
        string csrf = await CsrfFrom(site, "/activate?code=" + code!.UserCode);
        await site.PostAsync("/activate", Form(("csrf", csrf), ("code", code.UserCode), ("decision", "deny")));
        var poll = await api.PostAsJsonAsync("/v1/device/token", new { deviceCode = code.DeviceCodeValue, deviceId });
        Assert.Contains("denied", await poll.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Codigo_de_outro_dispositivo_nao_resgata_a_licenca()
    {
        var (site, _) = await RegisterAsync();
        var api = NewClient();
        string deviceId = DeviceId();
        var code = await (await api.PostAsJsonAsync("/v1/device/code", new { deviceId, deviceName = "x" })).Content.ReadFromJsonAsync<DeviceCode>();
        string csrf = await CsrfFrom(site, "/activate?code=" + code!.UserCode);
        await site.PostAsync("/activate", Form(("csrf", csrf), ("code", code.UserCode), ("decision", "approve")));
        var stolen = await api.PostAsJsonAsync("/v1/device/token", new { deviceCode = code.DeviceCodeValue, deviceId = DeviceId() });
        Assert.DoesNotContain("license", await stolen.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Renovacao_recusa_licenca_forjada_ou_de_outro_pc()
    {
        var (site, _) = await RegisterAsync();
        var (license, deviceId) = await ActivateAsync(site);
        var api = NewClient();

        string forged = license.Replace(license.Split('.')[2], LicenseToken.Base64Url(new byte[64]));
        Assert.Equal(HttpStatusCode.Forbidden, (await api.PostAsJsonAsync("/v1/license/refresh", new { license = forged, deviceId })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.PostAsJsonAsync("/v1/license/refresh", new { license, deviceId = DeviceId() })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.PostAsJsonAsync("/v1/license/refresh", new { license, deviceId })).StatusCode);
    }

    [Fact]
    public async Task Senha_errada_e_formulario_sem_csrf_sao_recusados()
    {
        var (site, email) = await RegisterAsync();
        var other = NewClient();
        var wrong = await other.PostAsync("/login", Form(("email", email), ("password", "errada-errada")));
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.PostAsync("/login", Form(("email", "naoexiste@exemplo.com"), ("password", "qualquer-coisa")))).StatusCode);

        var noCsrf = await site.PostAsync("/account/device/remove", Form(("device", "x")));
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
    }

    [Fact]
    public async Task Login_nao_redireciona_para_outro_site()
    {
        var (_, email) = await RegisterAsync("senha-forte-123");
        var client = NewClient();
        var response = await client.PostAsync("/login", Form(("email", email), ("password", "senha-forte-123"), ("next", "//malicioso.com")));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Cadastro_valida_email_senha_e_duplicidade()
    {
        var client = NewClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/register", Form(("email", "sem-arroba"), ("password", "senha-forte-123")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/register", Form(("email", $"a{Unique()}@exemplo.com"), ("password", "curta")))).StatusCode);
        var (_, email) = await RegisterAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await NewClient().PostAsync("/register", Form(("email", email), ("password", "senha-forte-123")))).StatusCode);
    }

    [Fact]
    public async Task Conta_exige_login()
    {
        var response = await NewClient().GetAsync("/account");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/login", response.Headers.Location!.OriginalString);
    }

    // ---- pagamentos

    private static HttpRequestMessage Webhook(object body, string? secret = ServerFixture.Secret)
    {
        string json = JsonSerializer.Serialize(body);
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/webhooks/payments") { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (secret is not null)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            request.Headers.Add("X-Signature", "sha256=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(json))).ToLowerInvariant());
        }
        return request;
    }

    [Fact]
    public async Task Webhook_exige_assinatura_valida()
    {
        var api = NewClient();
        var bad = await api.SendAsync(Webhook(new { id = "e1", @event = "subscription.activated", email = "x@y.com" }, "outro-segredo-qualquer-123"));
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        var missing = await api.SendAsync(Webhook(new { id = "e1", @event = "subscription.activated", email = "x@y.com" }, null));
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
    }

    [Fact]
    public async Task Webhook_ativa_renova_e_cancela_a_assinatura()
    {
        var (site, email) = await RegisterAsync();
        var (license, deviceId) = await ActivateAsync(site);
        var api = NewClient();
        string publicKey = await PublicKeyAsync();

        string id = Unique();
        var activated = await api.SendAsync(Webhook(new { id = "a" + id, @event = "subscription.activated", email, plan = "pro", periodEnd = DateTimeOffset.UtcNow.AddDays(30), providerRef = "sub_1" }));
        Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
        var renewed = await (await api.PostAsJsonAsync("/v1/license/refresh", new { license, deviceId })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("pro", LicenseVerifier.Evaluate(renewed.GetProperty("license").GetString(), publicKey, deviceId, DateTimeOffset.UtcNow).Plan);

        // evento repetido não faz nada
        var duplicate = await (await api.SendAsync(Webhook(new { id = "a" + id, @event = "subscription.canceled", email }))).Content.ReadAsStringAsync();
        Assert.Contains("duplicate", duplicate);
        var stillPro = await (await api.PostAsJsonAsync("/v1/license/refresh", new { license, deviceId })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("pro", LicenseVerifier.Evaluate(stillPro.GetProperty("license").GetString(), publicKey, deviceId, DateTimeOffset.UtcNow).Plan);

        await api.SendAsync(Webhook(new { id = "c" + id, @event = "subscription.canceled", email, periodEnd = DateTimeOffset.UtcNow.AddDays(-10) }));
        var downgraded = await (await api.PostAsJsonAsync("/v1/license/refresh", new { license, deviceId })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("free", LicenseVerifier.Evaluate(downgraded.GetProperty("license").GetString(), publicKey, deviceId, DateTimeOffset.UtcNow).Plan);
    }

    [Fact]
    public async Task Webhook_de_conta_inexistente_e_evento_desconhecido()
    {
        var api = NewClient();
        Assert.Equal(HttpStatusCode.Accepted, (await api.SendAsync(Webhook(new { id = "n" + Unique(), @event = "subscription.activated", email = "ninguem@exemplo.com" }))).StatusCode);
        var (_, email) = await RegisterAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await api.SendAsync(Webhook(new { id = "u" + Unique(), @event = "coisa.estranha", email }))).StatusCode);
    }

    [Fact]
    public async Task Pagina_da_conta_escapa_texto_do_usuario()
    {
        var (site, _) = await RegisterAsync();
        var api = NewClient();
        string deviceId = DeviceId();
        var code = await (await api.PostAsJsonAsync("/v1/device/code", new { deviceId, deviceName = "<script>alert(1)</script>" })).Content.ReadFromJsonAsync<DeviceCode>();
        string html = await site.GetStringAsync("/activate?code=" + code!.UserCode);
        Assert.DoesNotContain("<script>alert(1)", html);
        Assert.Contains("&lt;script&gt;", html);
    }
}

public class SegurancaServidorTests
{
    [Fact]
    public void Senha_e_guardada_com_hash_e_conferida()
    {
        string hash = Security.HashPassword("minha-senha");
        Assert.DoesNotContain("minha-senha", hash);
        Assert.True(Security.VerifyPassword("minha-senha", hash));
        Assert.False(Security.VerifyPassword("outra-senha", hash));
        Assert.NotEqual(hash, Security.HashPassword("minha-senha")); // sal diferente a cada vez
        Assert.False(Security.VerifyPassword("x", "lixo"));
    }

    [Fact]
    public void Codigo_do_usuario_e_legivel_e_normalizado()
    {
        string code = Security.NewUserCode();
        Assert.Matches("^[A-Z0-9]{4}-[A-Z0-9]{4}$", code);
        Assert.Equal(code, Security.NormalizeUserCode(code.ToLowerInvariant().Replace("-", " ")));
    }

    [Fact]
    public void Limite_de_tentativas_libera_depois_da_janela()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var limiter = new RateLimiter(() => now);
        for (int i = 0; i < 3; i++) Assert.True(limiter.Allow("ip", 3, TimeSpan.FromMinutes(1)));
        Assert.False(limiter.Allow("ip", 3, TimeSpan.FromMinutes(1)));
        Assert.True(limiter.Allow("outro", 3, TimeSpan.FromMinutes(1)));
        now = now.AddMinutes(2);
        Assert.True(limiter.Allow("ip", 3, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void Emissor_aplica_periodo_de_tolerancia_depois_do_vencimento()
    {
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        string path = Path.Combine(Path.GetTempPath(), "mbx-key-" + Guid.NewGuid().ToString("N") + ".pem");
        var issuer = new LicenseIssuer(new SigningKey(path), () => now);

        Assert.Equal("free", issuer.Resolve(null).Plan);
        var active = issuer.Resolve(new Subscription(1, "pro", "active", now.AddDays(30), 2, null, null));
        Assert.Equal(("pro", "active"), (active.Plan, active.Status));
        Assert.Equal(now + LicenseIssuer.TokenLife, active.Expires);

        var nearEnd = issuer.Resolve(new Subscription(1, "pro", "active", now.AddDays(2), 2, null, null));
        Assert.Equal(now.AddDays(2) + LicenseIssuer.GracePeriod, nearEnd.Expires);

        var grace = issuer.Resolve(new Subscription(1, "pro", "active", now.AddDays(-1), 2, null, null));
        Assert.Equal(("pro", "past_due"), (grace.Plan, grace.Status));

        Assert.Equal("free", issuer.Resolve(new Subscription(1, "pro", "active", now.AddDays(-5), 2, null, null)).Plan);
        Assert.Equal("free", issuer.Resolve(new Subscription(1, "pro", "canceled", now.AddDays(10), 2, null, null)).Plan);
        File.Delete(path);
    }

    [Fact]
    public void Chave_de_assinatura_e_criada_uma_vez_e_reaproveitada()
    {
        string path = Path.Combine(Path.GetTempPath(), "mbx-key-" + Guid.NewGuid().ToString("N") + ".pem");
        string first = new SigningKey(path).PublicKey;
        Assert.Equal(first, new SigningKey(path).PublicKey);
        Assert.DoesNotContain("PRIVATE", first);
        File.Delete(path);
    }

    [Fact]
    public void Assinatura_do_webhook_e_conferida_em_tempo_constante()
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("segredo-bem-grande-123"));
        string good = "sha256=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes("corpo"))).ToLowerInvariant();
        Assert.True(Security.SignatureMatches("corpo", good, "segredo-bem-grande-123"));
        Assert.False(Security.SignatureMatches("corpo!", good, "segredo-bem-grande-123"));
        Assert.False(Security.SignatureMatches("corpo", "sha256=zzzz", "segredo-bem-grande-123"));
        Assert.False(Security.SignatureMatches("corpo", "", "segredo-bem-grande-123"));
    }
}
