using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MBExtreme.Core.Licensing;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MBExtreme.LicenseServer.Tests;

public class PlanosTests
{
    [Fact]
    public void DescontoPorPeriodo()
    {
        var monthly = PlanCatalog.Offer(3000, PlanCatalog.Options[0], 1);
        var annual = PlanCatalog.Offer(3000, PlanCatalog.Find("annual")!, 1);
        Assert.Equal(3000, monthly.TotalCents);
        Assert.Equal(3000 * 12 * 75 / 100, annual.TotalCents);
        Assert.Equal(2250, annual.PerMonthCents);
        Assert.True(PlanCatalog.Offer(3000, PlanCatalog.Find("quarterly")!, 1).PerMonthCents < monthly.PerMonthCents);
    }

    [Fact]
    public void ContaCompartilhadaTemDescontoNoSegundoPc()
    {
        var one = PlanCatalog.Offer(3000, PlanCatalog.Options[0], 1);
        var two = PlanCatalog.Offer(3000, PlanCatalog.Options[0], 2);
        Assert.Equal(3000 + 2100, two.TotalCents);
        Assert.True(two.TotalCents < one.TotalCents * 2);
        Assert.Equal(1, PlanCatalog.Offer(3000, PlanCatalog.Options[0], 0).Devices);
    }

    [Fact]
    public void ValorEmReais() => Assert.Equal("R$ 29,90", PlanCatalog.Money(2990));

    [Fact]
    public void ChaveDeAtivacaoTemFormatoENormalizacao()
    {
        string key = ActivationKeys.Generate();
        Assert.Matches("^MBX-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}$", key);
        Assert.True(ActivationKeys.LooksValid(key));
        Assert.True(ActivationKeys.LooksValid(key.ToLowerInvariant().Replace("-", " ")));
        Assert.Equal(ActivationKeys.Hash(key), ActivationKeys.Hash(key.Replace("-", "").ToLowerInvariant()));
        Assert.False(ActivationKeys.LooksValid("MBX-1234"));
        Assert.NotEqual(key, ActivationKeys.Generate());
    }

    [Fact]
    public void AssinaturaDoStripeValeSoComSegredoCorretoEJanelaDeTempo()
    {
        var now = DateTimeOffset.UtcNow;
        string payload = """{"id":"evt_1"}""";
        string header = StripeGateway.Sign(payload, "segredo-1234", now);
        Assert.True(StripeGateway.VerifySignature(payload, header, "segredo-1234", now));
        Assert.False(StripeGateway.VerifySignature(payload + " ", header, "segredo-1234", now));
        Assert.False(StripeGateway.VerifySignature(payload, header, "outro-segredo", now));
        Assert.False(StripeGateway.VerifySignature(payload, header, "segredo-1234", now.AddMinutes(10)));
        Assert.False(StripeGateway.VerifySignature(payload, "", "segredo-1234", now));
        Assert.False(StripeGateway.VerifySignature(payload, "t=abc,v1=00", "segredo-1234", now));
    }

    [Fact]
    public async Task CheckoutEnviaPrecoQuantidadeEUsuario()
    {
        var handler = new CaptureHandler("""{"url":"https://checkout.stripe.com/c/pay/abc"}""");
        var gateway = new StripeGateway(new HttpClient(handler), "sk_test_123", new() { ["monthly"] = "price_mensal" });
        string url = await gateway.CheckoutUrlAsync(42, "a@b.com", "monthly", 2, "https://x/ok", "https://x/no", CancellationToken.None);
        Assert.Equal("https://checkout.stripe.com/c/pay/abc", url);
        Assert.Equal("Bearer sk_test_123", handler.Authorization);
        Assert.Contains("line_items%5B0%5D%5Bprice%5D=price_mensal", handler.Body);
        Assert.Contains("line_items%5B0%5D%5Bquantity%5D=2", handler.Body);
        Assert.Contains("client_reference_id=42", handler.Body);
        await Assert.ThrowsAsync<InvalidOperationException>(() => gateway.CheckoutUrlAsync(1, "a@b.com", "annual", 1, "", "", CancellationToken.None));
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly string _response;
        public string Body { get; private set; } = "";
        public string Authorization { get; private set; } = "";
        public CaptureHandler(string response) => _response = response;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Authorization = request.Headers.Authorization?.ToString() ?? "";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_response, Encoding.UTF8, "application/json") };
        }
    }
}

public class CobrancaTests : IClassFixture<ServerFixture>
{
    private readonly ServerFixture _server;

    public CobrancaTests(ServerFixture server) => _server = server;

    private HttpClient NewClient() => _server.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private static FormUrlEncodedContent Form(params (string, string)[] fields) => new(fields.Select(f => new KeyValuePair<string, string>(f.Item1, f.Item2)));

    private async Task<(HttpClient Client, string Email, long UserId)> RegisterAsync()
    {
        var client = NewClient();
        string email = $"c{Guid.NewGuid():N}"[..12] + "@exemplo.com";
        var response = await client.PostAsync("/register", Form(("email", email), ("password", "senha-forte-123")));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var db = new Db(Path.Combine(_server.Dir, "licenses.db"));
        return (client, email, db.UserByEmail(email)!.Id);
    }

    private static async Task<string> CsrfFrom(HttpClient client, string url)
    {
        var match = Regex.Match(await client.GetStringAsync(url), "name=\"csrf\" value=\"([^\"]+)\"");
        Assert.True(match.Success);
        return match.Groups[1].Value;
    }

    private string Token(string email)
    {
        _ = _server.Server;
        var key = new SigningKey(Path.Combine(_server.Dir, "signing-key.pem"));
        return LicenseToken.Sign(new LicensePayload { Subject = "1", Email = email, Plan = "pro", Device = "x", IssuedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) }, key.Key);
    }

    private async Task<DateTimeOffset?> PeriodEnd(long userId)
    {
        await Task.Yield();
        return new Db(Path.Combine(_server.Dir, "licenses.db")).SubscriptionOf(userId)?.PeriodEnd;
    }

    private async Task<HttpResponseMessage> PostWebhook(object payload, string? secret = "whsec_segredo_de_teste")
    {
        string json = JsonSerializer.Serialize(payload);
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/webhooks/stripe") { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        request.Headers.Add("Stripe-Signature", StripeGateway.Sign(json, secret ?? "", DateTimeOffset.UtcNow));
        return await _server.CreateClient().SendAsync(request);
    }

    [Fact]
    public async Task PlanosPublicosListamPeriodosEPrecos()
    {
        var plans = await _server.CreateClient().GetFromJsonAsync<JsonElement>("/v1/plans");
        Assert.Equal(3, plans.GetProperty("trialDays").GetInt32());
        Assert.Equal(4, plans.GetProperty("plans").GetArrayLength());
    }

    [Fact]
    public async Task PrimeiraAtivacaoGanhaTesteGratisDeTresDiasUmaVezPorComputador()
    {
        var (site, _, userId) = await RegisterAsync();
        string deviceId = Guid.NewGuid().ToString("N");
        await ActivateAsync(site, deviceId);
        var end = await PeriodEnd(userId);
        Assert.NotNull(end);
        Assert.InRange((end!.Value - DateTimeOffset.UtcNow).TotalDays, 2.9, 3.1);

        // outra conta no mesmo computador não ganha o teste de novo
        var (site2, _, user2) = await RegisterAsync();
        await ActivateAsync(site2, deviceId);
        Assert.Null(await PeriodEnd(user2));
    }

    private async Task ActivateAsync(HttpClient site, string deviceId)
    {
        var api = NewClient();
        var start = await api.PostAsJsonAsync("/v1/device/code", new { deviceId, deviceName = "PC" });
        var code = await start.Content.ReadFromJsonAsync<JsonElement>();
        string userCode = code.GetProperty("userCode").GetString()!;
        string csrf = await CsrfFrom(site, "/activate?code=" + userCode);
        var approve = await site.PostAsync("/activate", Form(("csrf", csrf), ("code", userCode), ("decision", "approve")));
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
    }

    [Fact]
    public async Task AdministradorGeraChavesEUsuarioResgataUmaVez()
    {
        var admin = _server.CreateClient();
        var forbidden = await admin.PostAsJsonAsync("/v1/admin/keys", new { count = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/admin/keys") { Content = JsonContent.Create(new { count = 2, days = 90, maxDevices = 2, note = "revenda" }) };
        request.Headers.Add("Authorization", "Bearer " + Token("admin@exemplo.com"));
        var response = await admin.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var keys = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("keys").EnumerateArray().Select(k => k.GetString()!).ToList();
        Assert.Equal(2, keys.Count);

        var (site, _, userId) = await RegisterAsync();
        string csrf = await CsrfFrom(site, "/account");
        var ok = await site.PostAsync("/account/redeem", Form(("csrf", csrf), ("key", keys[0].ToLowerInvariant())));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var end = await PeriodEnd(userId);
        Assert.InRange((end!.Value - DateTimeOffset.UtcNow).TotalDays, 89.9, 90.1);

        var again = await site.PostAsync("/account/redeem", Form(("csrf", csrf), ("key", keys[0])));
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);

        // uma segunda chave soma ao fim do período atual
        var second = await site.PostAsync("/account/redeem", Form(("csrf", csrf), ("key", keys[1])));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.InRange(((await PeriodEnd(userId))!.Value - DateTimeOffset.UtcNow).TotalDays, 179.9, 180.1);

        var bad = await site.PostAsync("/account/redeem", Form(("csrf", csrf), ("key", "MBX-AAAA-AAAA-AAAA-AAAA")));
        Assert.Equal(HttpStatusCode.NotFound, bad.StatusCode);
    }

    [Fact]
    public async Task WebhookDoStripeAtivaMudaParaPendenteECancela()
    {
        var (_, _, userId) = await RegisterAsync();
        long end = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
        var db = new Db(Path.Combine(_server.Dir, "licenses.db"));

        var active = await PostWebhook(new
        {
            id = "evt_" + Guid.NewGuid().ToString("N"), type = "customer.subscription.updated",
            data = new { @object = new { id = "sub_1", customer = "cus_" + userId, status = "active", current_period_end = end, metadata = new { user_id = userId.ToString() }, items = new { data = new[] { new { quantity = 2 } } } } },
        });
        Assert.Equal(HttpStatusCode.OK, active.StatusCode);
        var sub = db.SubscriptionOf(userId)!;
        Assert.Equal(("pro", "active", 2), (sub.Plan, sub.Status, sub.MaxDevices));
        Assert.Equal(end, sub.PeriodEnd.ToUnixTimeSeconds());
        Assert.Equal($"stripe:cus_{userId}:sub_1", sub.ProviderRef);

        // sem metadados: acha a conta pelo cliente do Stripe
        var failed = await PostWebhook(new { id = "evt_" + Guid.NewGuid().ToString("N"), type = "invoice.payment_failed", data = new { @object = new { customer = "cus_" + userId, subscription = "sub_1" } } });
        Assert.Equal(HttpStatusCode.OK, failed.StatusCode);
        Assert.Equal("past_due", db.SubscriptionOf(userId)!.Status);

        var deleted = await PostWebhook(new { id = "evt_" + Guid.NewGuid().ToString("N"), type = "customer.subscription.deleted", data = new { @object = new { id = "sub_1", customer = "cus_" + userId, status = "canceled", metadata = new { user_id = userId.ToString() } } } });
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Equal("canceled", db.SubscriptionOf(userId)!.Status);
    }

    [Fact]
    public async Task WebhookRepetidoOuComAssinaturaFalsaNaoMudaNada()
    {
        var (_, _, userId) = await RegisterAsync();
        var payload = new { id = "evt_dup_" + userId, type = "customer.subscription.updated", data = new { @object = new { id = "sub_9", customer = "cus_9", status = "active", current_period_end = DateTimeOffset.UtcNow.AddDays(10).ToUnixTimeSeconds(), metadata = new { user_id = userId.ToString() } } } };
        Assert.Equal(HttpStatusCode.OK, (await PostWebhook(payload)).StatusCode);
        var repeated = await (await PostWebhook(payload)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(repeated.GetProperty("duplicate").GetBoolean());

        var forged = await PostWebhook(new { id = "evt_forjado", type = "customer.subscription.deleted", data = new { @object = new { id = "sub_9", customer = "cus_9", status = "canceled", metadata = new { user_id = userId.ToString() } } } }, secret: "chute");
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        Assert.Equal("active", new Db(Path.Combine(_server.Dir, "licenses.db")).SubscriptionOf(userId)!.Status);
    }

    [Fact]
    public async Task CheckoutSemStripeConfiguradoAvisaQueNaoEstaDisponivel()
    {
        var (site, _, _) = await RegisterAsync();
        string csrf = await CsrfFrom(site, "/account");
        var response = await site.PostAsync("/account/checkout", Form(("csrf", csrf), ("period", "monthly"), ("devices", "1")));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
