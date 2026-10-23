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
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MBExtreme.LicenseServer.Tests;

public class AfiliadosRegrasTests
{
    [Theory]
    [InlineData(0, "latao", 0.20)]
    [InlineData(4, "latao", 0.20)]
    [InlineData(5, "bronze", 0.25)]
    [InlineData(19, "bronze", 0.25)]
    [InlineData(20, "prata", 0.30)]
    [InlineData(49, "prata", 0.30)]
    [InlineData(50, "ouro", 0.35)]
    [InlineData(500, "ouro", 0.35)]
    public void NivelPorClientesPagantes(int clientes, string chave, double taxa)
    {
        var t = AffiliateProgram.TierFor(clientes);
        Assert.Equal(chave, t.Key);
        Assert.Equal(taxa, t.Rate);
    }

    [Fact]
    public void ProximoNivelEQuantosFaltam()
    {
        var (tier, missing) = AffiliateProgram.Next(3)!.Value;
        Assert.Equal("bronze", tier.Key);
        Assert.Equal(2, missing);
        Assert.Null(AffiliateProgram.Next(50));
    }

    [Fact]
    public void ComissaoArredondaParaBaixoENaoPagaValorZero()
    {
        Assert.Equal(398, AffiliateProgram.Commission(1990, 0.20));
        Assert.Equal(1, AffiliateProgram.Commission(5, 0.25));
        Assert.Equal(0, AffiliateProgram.Commission(0, 0.35));
        Assert.Equal(0, AffiliateProgram.Commission(-100, 0.35));
    }

    [Theory]
    [InlineData("ABCD", true)]
    [InlineData("meu-canal-2026", true)]
    [InlineData("abc", false)]
    [InlineData("com espaço", false)]
    [InlineData("a;b;c;d", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void CodigoDoLink(string? codigo, bool valido) => Assert.Equal(valido, AffiliateProgram.ValidCode(codigo));

    [Fact]
    public void CodigosGeradosSaoValidosEDiferentes()
    {
        var codes = Enumerable.Range(0, 50).Select(_ => AffiliateProgram.NewCode()).ToList();
        Assert.All(codes, c => Assert.True(AffiliateProgram.ValidCode(c)));
        Assert.True(codes.Distinct().Count() > 45);
    }

    [Fact]
    public void ComissaoRecorrenteSoPorOPeriodoDefinidoECarencia()
    {
        var p = new AffiliateProgram { CommissionMonths = 12, HoldDays = 30 };
        Assert.True(p.PaysCommission(0));
        Assert.True(p.PaysCommission(11));
        Assert.False(p.PaysCommission(12));
        Assert.False(new AffiliateProgram { CommissionMonths = 0 }.PaysCommission(1));
        var created = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.False(p.Available(created, created.AddDays(29)));
        Assert.True(p.Available(created, created.AddDays(30)));
    }

    private static Db NewDb() => new(Path.Combine(Path.GetTempPath(), $"mbx-aff-{Guid.NewGuid():N}.db"));

    private static long NewUser(Db db) => db.CreateUser($"u{Guid.NewGuid():N}"[..12] + "@x.com", "h")!.Value;

    [Fact]
    public void BancoAfiliadoNaoIndicaASiMesmoEPrimeiraIndicacaoVale()
    {
        var db = NewDb();
        long a = NewUser(db), b = NewUser(db), c = NewUser(db);
        db.CreateAffiliate(a);
        db.CreateAffiliate(c);
        Assert.False(db.AddReferral(a, a));
        Assert.True(db.AddReferral(b, a));
        Assert.False(db.AddReferral(b, c)); // só a primeira indicação vale
        Assert.Equal(a, db.ReferrerOf(b));
    }

    [Fact]
    public void ComissaoNaoDuplicaPorFaturaEEstornoCancela()
    {
        var db = NewDb();
        long a = NewUser(db), b = NewUser(db);
        db.CreateAffiliate(a);
        var program = new AffiliateProgram { HoldDays = 0, MinPayoutCents = 100 };
        Assert.True(db.AddCommission(a, b, "in_1", 1990, 0.2, 398));
        Assert.False(db.AddCommission(a, b, "in_1", 1990, 0.2, 398));
        Assert.Equal(1, db.CommissionCountFor(b));
        Assert.Equal(398, db.StatsOf(a, program, DateTimeOffset.UtcNow).AvailableCents);
        Assert.True(db.ReverseCommission("in_1"));
        Assert.Equal(0, db.StatsOf(a, program, DateTimeOffset.UtcNow).AvailableCents);
        Assert.False(db.ReverseCommission("in_1"));
    }

    [Fact]
    public void CarenciaSeparaPendenteDeDisponivel()
    {
        var db = NewDb();
        long a = NewUser(db), b = NewUser(db);
        db.CreateAffiliate(a);
        db.AddCommission(a, b, "in_1", 1990, 0.2, 398);
        var stats = db.StatsOf(a, new AffiliateProgram { HoldDays = 30 }, DateTimeOffset.UtcNow);
        Assert.Equal(398, stats.PendingCents);
        Assert.Equal(0, stats.AvailableCents);
        Assert.Equal(0, stats.PaidCustomers); // só conta cliente com pagamento já fora da carência
        var later = db.StatsOf(a, new AffiliateProgram { HoldDays = 30 }, DateTimeOffset.UtcNow.AddDays(31));
        Assert.Equal(398, later.AvailableCents);
        Assert.Equal(1, later.PaidCustomers);
    }

    [Fact]
    public void SaqueExigeMinimoJuntaTudoEPagamentoFechaOCiclo()
    {
        var db = NewDb();
        long a = NewUser(db), b = NewUser(db);
        db.CreateAffiliate(a);
        var program = new AffiliateProgram { HoldDays = 0, MinPayoutCents = 1000 };
        db.AddCommission(a, b, "in_1", 1990, 0.2, 398);
        Assert.Null(db.RequestPayout(a, "pix@x.com", program, DateTimeOffset.UtcNow)); // abaixo do mínimo
        db.AddCommission(a, b, "in_2", 1990, 0.2, 398);
        db.AddCommission(a, b, "in_3", 1990, 0.2, 398);
        var payout = db.RequestPayout(a, "pix@x.com", program, DateTimeOffset.UtcNow);
        Assert.NotNull(payout);
        Assert.Equal(1194, payout!.AmountCents);
        Assert.Null(db.RequestPayout(a, "pix@x.com", program, DateTimeOffset.UtcNow)); // não paga duas vezes
        var open = db.StatsOf(a, program, DateTimeOffset.UtcNow);
        Assert.Equal(1194, open.RequestedCents);
        Assert.Equal(0, open.AvailableCents);
        Assert.True(db.MarkPayoutPaid(payout.Id, "pago"));
        Assert.False(db.MarkPayoutPaid(payout.Id, "de novo"));
        Assert.Equal(1194, db.StatsOf(a, program, DateTimeOffset.UtcNow).PaidCents);
    }

    [Fact]
    public void CincoClientesViramBronze()
    {
        var db = NewDb();
        long a = NewUser(db);
        db.CreateAffiliate(a);
        var program = new AffiliateProgram { HoldDays = 0 };
        for (int i = 0; i < 5; i++) db.AddCommission(a, NewUser(db), "in_" + i, 1990, 0.2, 398);
        Assert.Equal("bronze", AffiliateProgram.TierFor(db.StatsOf(a, program, DateTimeOffset.UtcNow).PaidCustomers).Key);
    }
}

public class GoogleTokenTests
{
    private const string Client = "cliente-de-teste.apps.googleusercontent.com";

    private sealed class Keys
    {
        public RSA Rsa { get; } = RSA.Create(2048);
        public string Jwks()
        {
            var p = Rsa.ExportParameters(false);
            string B(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return JsonSerializer.Serialize(new { keys = new[] { new { kty = "RSA", kid = "k1", n = B(p.Modulus!), e = B(p.Exponent!), alg = "RS256", use = "sig" } } });
        }
    }

    private static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Jwt(RSA rsa, object payload, string alg = "RS256", string kid = "k1")
    {
        string h = B64(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { alg, kid, typ = "JWT" })));
        string p = B64(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));
        string sig = B64(rsa.SignData(Encoding.ASCII.GetBytes(h + "." + p), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        return $"{h}.{p}.{sig}";
    }

    private static object Claims(DateTimeOffset now, string aud = Client, string iss = "https://accounts.google.com", long? exp = null, bool verified = true, string email = "Pessoa@Gmail.com") => new
    {
        iss, aud, sub = "1234567890", email, email_verified = verified, name = "Pessoa Teste", iat = now.ToUnixTimeSeconds(), exp = exp ?? now.AddHours(1).ToUnixTimeSeconds(),
    };

    private static GoogleAuth Auth(Keys k, Action? onFetch = null) => new(Client, _ => { onFetch?.Invoke(); return Task.FromResult(k.Jwks()); });

    [Fact]
    public async Task TokenValidoDevolveIdentidade()
    {
        var k = new Keys();
        var now = DateTimeOffset.UtcNow;
        var id = await Auth(k).ValidateAsync(Jwt(k.Rsa, Claims(now)), now);
        Assert.NotNull(id);
        Assert.Equal("pessoa@gmail.com", id!.Email);
        Assert.Equal("1234567890", id.Subject);
        Assert.True(id.EmailVerified);
    }

    [Fact]
    public async Task AssinaturaDeOutraChaveEhRecusada()
    {
        var k = new Keys();
        var other = new Keys();
        var now = DateTimeOffset.UtcNow;
        Assert.Null(await Auth(k).ValidateAsync(Jwt(other.Rsa, Claims(now)), now));
    }

    [Fact]
    public async Task PublicoEmissorEValidadeSaoConferidos()
    {
        var k = new Keys();
        var now = DateTimeOffset.UtcNow;
        var auth = Auth(k);
        Assert.Null(await auth.ValidateAsync(Jwt(k.Rsa, Claims(now, aud: "outro-cliente")), now));
        Assert.Null(await auth.ValidateAsync(Jwt(k.Rsa, Claims(now, iss: "https://evil.example")), now));
        Assert.Null(await auth.ValidateAsync(Jwt(k.Rsa, Claims(now, exp: now.AddHours(-2).ToUnixTimeSeconds())), now));
        Assert.NotNull(await auth.ValidateAsync(Jwt(k.Rsa, Claims(now, iss: "accounts.google.com")), now));
    }

    [Fact]
    public async Task AlgoritmoNoneEMalFormadosSaoRecusados()
    {
        var k = new Keys();
        var now = DateTimeOffset.UtcNow;
        var auth = Auth(k);
        string none = B64(Encoding.UTF8.GetBytes("{\"alg\":\"none\"}")) + "." + B64(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Claims(now)))) + ".";
        Assert.Null(await auth.ValidateAsync(none, now));
        Assert.Null(await auth.ValidateAsync("abc", now));
        Assert.Null(await auth.ValidateAsync("", now));
        Assert.Null(await auth.ValidateAsync(null, now));
        Assert.Null(await auth.ValidateAsync(new string('a', 5000), now));
        Assert.Null(await new GoogleAuth("").ValidateAsync(Jwt(k.Rsa, Claims(now)), now)); // sem Client ID
    }

    [Fact]
    public async Task EmailNaoVerificadoVoltaComoNaoVerificado()
    {
        var k = new Keys();
        var now = DateTimeOffset.UtcNow;
        var id = await Auth(k).ValidateAsync(Jwt(k.Rsa, Claims(now, verified: false)), now);
        Assert.NotNull(id);
        Assert.False(id!.EmailVerified);
    }

    [Fact]
    public async Task ChaveNovaDesconhecidaFazUmaBuscaAMais()
    {
        var k = new Keys();
        var now = DateTimeOffset.UtcNow;
        int fetches = 0;
        var auth = Auth(k, () => fetches++);
        await auth.ValidateAsync(Jwt(k.Rsa, Claims(now)), now);
        await auth.ValidateAsync(Jwt(k.Rsa, Claims(now)), now);
        Assert.Equal(1, fetches); // chaves em cache
        Assert.Null(await auth.ValidateAsync(Jwt(k.Rsa, Claims(now), kid: "desconhecida"), now));
        Assert.Equal(2, fetches); // tentou atualizar uma vez
    }
}

public sealed class SiteFixture : WebApplicationFactory<Program>
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "mbx-site-" + Guid.NewGuid().ToString("N"));
    public const string Secret = "whsec_segredo_de_teste";
    public const string ClientId = "cliente-de-teste.apps.googleusercontent.com";
    public RSA GoogleKey { get; } = RSA.Create(2048);

    private string Jwks()
    {
        var p = GoogleKey.ExportParameters(false);
        string B(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return JsonSerializer.Serialize(new { keys = new[] { new { kty = "RSA", kid = "k1", n = B(p.Modulus!), e = B(p.Exponent!) } } });
    }

    public string GoogleToken(string email, string sub, bool verified = true, string name = "Pessoa Google")
    {
        string B(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var now = DateTimeOffset.UtcNow;
        string h = B(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"kid\":\"k1\",\"typ\":\"JWT\"}"));
        string p = B(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { iss = "https://accounts.google.com", aud = ClientId, sub, email, email_verified = verified, name, iat = now.ToUnixTimeSeconds(), exp = now.AddHours(1).ToUnixTimeSeconds() })));
        return $"{h}.{p}.{B(GoogleKey.SignData(Encoding.ASCII.GetBytes(h + "." + p), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))}";
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Data:Directory", Dir);
        builder.UseSetting("Dev:Enabled", "true");
        builder.UseSetting("Limits:Multiplier", "1000");
        builder.UseSetting("Admin:Emails", "admin@exemplo.com");
        builder.UseSetting("Stripe:WebhookSecret", Secret);
        builder.UseSetting("Google:ClientId", ClientId);
        builder.ConfigureTestServices(s => s.AddSingleton(new GoogleAuth(ClientId, _ => Task.FromResult(Jwks()))));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(Dir, true); } catch (IOException) { }
    }
}

public class SiteAfiliadosTests : IClassFixture<SiteFixture>
{
    private readonly SiteFixture _server;

    public SiteAfiliadosTests(SiteFixture server) => _server = server;

    private HttpClient Client(bool cookies = true) => _server.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = cookies });

    private Db Database() { _ = _server.Server; return new Db(Path.Combine(_server.Dir, "licenses.db")); }

    private static FormUrlEncodedContent Form(params (string, string)[] fields) => new(fields.Select(f => new KeyValuePair<string, string>(f.Item1, f.Item2)));

    private static string NewEmail() => $"s{Guid.NewGuid():N}"[..12] + "@exemplo.com";

    private async Task<(HttpClient Client, string Email, long UserId)> RegisterAsync(HttpClient? client = null)
    {
        client ??= Client();
        string email = NewEmail();
        var response = await client.PostAsync("/register", Form(("email", email), ("password", "senha-forte-123")));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return (client, email, Database().UserByEmail(email)!.Id);
    }

    private static async Task<string> CsrfFrom(HttpClient client, string url)
    {
        var match = Regex.Match(await client.GetStringAsync(url), "name=\"csrf\" value=\"([^\"]+)\"");
        Assert.True(match.Success);
        return match.Groups[1].Value;
    }

    private async Task<HttpResponseMessage> PostWebhook(object payload)
    {
        string json = JsonSerializer.Serialize(payload);
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/webhooks/stripe") { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        request.Headers.Add("Stripe-Signature", StripeGateway.Sign(json, SiteFixture.Secret, DateTimeOffset.UtcNow));
        return await Client().SendAsync(request);
    }

    private static object InvoicePaid(string eventId, string invoiceId, long userId, long amount) => new
    {
        id = eventId, type = "invoice.paid",
        data = new { @object = new { id = invoiceId, customer = "cus_x", amount_paid = amount, subscription_details = new { metadata = new { user_id = userId.ToString() } } } },
    };

    private static object ChargeRefunded(string eventId, string invoiceId, long amount, long refunded) => new
    {
        id = eventId, type = "charge.refunded", data = new { @object = new { id = "ch_1", invoice = invoiceId, amount, amount_refunded = refunded, customer = "cus_x" } },
    };

    private string AdminToken()
    {
        _ = _server.Server;
        var key = new SigningKey(Path.Combine(_server.Dir, "signing-key.pem"));
        return LicenseToken.Sign(new LicensePayload { Subject = "1", Email = "admin@exemplo.com", Plan = "pro", Device = "x", IssuedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) }, key.Key);
    }

    // ------------------------------------------------------------ páginas públicas

    [Fact]
    public async Task PaginaInicialMostraTesteDeTresDiasPrecosENiveis()
    {
        string html = await Client().GetStringAsync("/");
        Assert.Contains("3 dias", html);
        Assert.Contains("R$ 19,90", html);
        Assert.Contains("R$ 149,90", html);
        Assert.Contains("Baixar grátis", html);
        Assert.Contains("desfaz automaticamente", html);
        Assert.Contains("Perguntas frequentes", html);
    }

    [Fact]
    public async Task PlanosDownloadTermosEAfiliadosAbrem()
    {
        var c = Client();
        Assert.Contains("R$ 49,90", await c.GetStringAsync("/planos"));
        string download = await c.GetStringAsync("/download");
        Assert.Contains("Baixar instalador", download);
        Assert.Contains("github.com", download);
        Assert.Contains("Teste grátis", await c.GetStringAsync("/termos"));
        Assert.Contains("Privacidade", await c.GetStringAsync("/privacidade"), StringComparison.OrdinalIgnoreCase);
        string aff = WebUtility.HtmlDecode(await c.GetStringAsync("/afiliados"));
        foreach (string tier in new[] { "Latão", "Bronze", "Prata", "Ouro" }) Assert.Contains(tier, aff);
        Assert.Contains("20%", aff);
        Assert.Contains("35%", aff);
        Assert.Contains("Disallow: /account", await c.GetStringAsync("/robots.txt"));
        Assert.Contains("/planos", await c.GetStringAsync("/sitemap.xml"));
    }

    [Fact]
    public async Task LoginECadastroMostramBotaoDoGoogleComEscape()
    {
        var c = Client();
        foreach (string url in new[] { "/login", "/register" })
        {
            string html = await c.GetStringAsync(url);
            Assert.Contains("accounts.google.com/gsi/client", html);
            Assert.Contains(SiteFixture.ClientId, html);
            Assert.Contains("/auth/google", html);
        }
    }

    // ------------------------------------------------------------ login com Google

    private async Task<HttpResponseMessage> GoogleLogin(string credential, string? csrfCookie = "csrf123", string? csrfForm = "csrf123")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/google") { Content = Form(("credential", credential), ("g_csrf_token", csrfForm ?? "")) };
        if (csrfCookie is not null) request.Headers.Add("Cookie", "g_csrf_token=" + csrfCookie);
        return await Client(cookies: false).SendAsync(request);
    }

    private static string SessionCookie(HttpResponseMessage r) =>
        r.Headers.TryGetValues("Set-Cookie", out var v) ? v.Select(x => x.Split(';')[0]).FirstOrDefault(x => x.StartsWith("mbx_session=")) ?? "" : "";

    [Fact]
    public async Task GoogleCriaContaNovaEAbreSessao()
    {
        string email = NewEmail();
        var response = await GoogleLogin(_server.GoogleToken(email, "sub-" + Guid.NewGuid().ToString("N")));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        string session = SessionCookie(response);
        Assert.NotEqual("", session);

        var request = new HttpRequestMessage(HttpMethod.Get, "/account");
        request.Headers.Add("Cookie", session);
        var account = await Client(cookies: false).SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, account.StatusCode);
        Assert.Contains(email, await account.Content.ReadAsStringAsync());
        Assert.NotNull(Database().UserByEmail(email));
    }

    [Fact]
    public async Task GoogleContaComSenhaInutilizavelNaoEntraPorSenha()
    {
        string email = NewEmail();
        await GoogleLogin(_server.GoogleToken(email, "sub-" + Guid.NewGuid().ToString("N")));
        var login = await Client().PostAsync("/login", Form(("email", email), ("password", "qualquer-senha-123")));
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task GoogleLigaContaExistenteComOMesmoEmailSemDuplicar()
    {
        var (_, email, userId) = await RegisterAsync();
        string sub = "sub-" + Guid.NewGuid().ToString("N");
        var response = await GoogleLogin(_server.GoogleToken(email, sub));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(userId, Database().UserByGoogleSub(sub)!.Id);
        // a segunda entrada usa o sub, mesmo que o e-mail do Google mude
        var again = await GoogleLogin(_server.GoogleToken("outro-" + email, sub));
        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
        Assert.NotEqual("", SessionCookie(again));
        Assert.Equal(userId, Database().UserByGoogleSub(sub)!.Id);
    }

    [Fact]
    public async Task GoogleRecusaTokenInvalidoEmailNaoVerificadoECsrfErrado()
    {
        string sub = "sub-" + Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.Unauthorized, (await GoogleLogin("token.invalido.aqui")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GoogleLogin(_server.GoogleToken(NewEmail(), sub, verified: false))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await GoogleLogin(_server.GoogleToken(NewEmail(), sub), csrfCookie: "a", csrfForm: "b")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await GoogleLogin(_server.GoogleToken(NewEmail(), sub), csrfCookie: null)).StatusCode);
    }

    // ------------------------------------------------------------ afiliados

    private async Task<(HttpClient Client, long UserId, Db.Affiliate Affiliate)> NewAffiliate()
    {
        var (client, _, id) = await RegisterAsync();
        string csrf = await CsrfFrom(client, "/affiliate");
        var join = await client.PostAsync("/affiliate/join", Form(("csrf", csrf)));
        Assert.Equal(HttpStatusCode.Redirect, join.StatusCode);
        return (client, id, Database().AffiliateOf(id)!);
    }

    [Fact]
    public async Task PainelDoAfiliadoExigeLoginEMostraLinkENivel()
    {
        var anon = await Client().GetAsync("/affiliate");
        Assert.Equal(HttpStatusCode.Redirect, anon.StatusCode);
        var (client, _, aff) = await NewAffiliate();
        string html = WebUtility.HtmlDecode(await client.GetStringAsync("/affiliate"));
        Assert.Contains("/r/" + aff.Code, html);
        Assert.Contains("Latão", html);
        Assert.Contains("Faltam", html);
    }

    [Fact]
    public async Task LinkDeIndicacaoLigaOCadastroAoAfiliado()
    {
        var (_, affiliateId, aff) = await NewAffiliate();
        var visitor = Client();
        var click = await visitor.GetAsync("/r/" + aff.Code.ToLowerInvariant());
        Assert.Equal(HttpStatusCode.Redirect, click.StatusCode);
        var (_, _, referredId) = await RegisterAsync(visitor);
        Assert.Equal(affiliateId, Database().ReferrerOf(referredId));

        // código inexistente ou inválido não grava nada
        var other = Client();
        await other.GetAsync("/r/NAOEXISTE");
        await other.GetAsync("/r/a;b");
        var (_, _, plainId) = await RegisterAsync(other);
        Assert.Null(Database().ReferrerOf(plainId));
    }

    [Fact]
    public async Task IndicacaoTambemValePeloGoogle()
    {
        var (_, affiliateId, aff) = await NewAffiliate();
        string email = NewEmail();
        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/google") { Content = Form(("credential", _server.GoogleToken(email, "sub-" + Guid.NewGuid().ToString("N"))), ("g_csrf_token", "x")) };
        request.Headers.Add("Cookie", $"g_csrf_token=x; mbx_ref={aff.Code}");
        var response = await Client(cookies: false).SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(affiliateId, Database().ReferrerOf(Database().UserByEmail(email)!.Id));
    }

    [Fact]
    public async Task PagamentoDoIndicadoGeraComissaoNoNivelEEstornoCancela()
    {
        var (_, affiliateId, aff) = await NewAffiliate();
        var visitor = Client();
        await visitor.GetAsync("/r/" + aff.Code);
        var (_, _, referredId) = await RegisterAsync(visitor);

        var paid = await PostWebhook(InvoicePaid("evt_a1", "in_a1", referredId, 1990));
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        var stats = Database().StatsOf(affiliateId, new AffiliateProgram(), DateTimeOffset.UtcNow);
        Assert.Equal(398, stats.PendingCents); // 20% de R$ 19,90, em carência

        // o mesmo evento e a mesma fatura não pagam duas vezes
        await PostWebhook(InvoicePaid("evt_a1", "in_a1", referredId, 1990));
        await PostWebhook(InvoicePaid("evt_a2", "in_a1", referredId, 1990));
        Assert.Equal(398, Database().StatsOf(affiliateId, new AffiliateProgram(), DateTimeOffset.UtcNow).PendingCents);

        // fatura de valor zero (teste grátis) não gera comissão
        await PostWebhook(InvoicePaid("evt_a3", "in_zero", referredId, 0));
        Assert.Equal(1, Database().CommissionCountFor(referredId));

        // estorno parcial não cancela; total cancela
        await PostWebhook(ChargeRefunded("evt_r0", "in_a1", 1990, 500));
        Assert.Equal(398, Database().StatsOf(affiliateId, new AffiliateProgram(), DateTimeOffset.UtcNow).PendingCents);
        await PostWebhook(ChargeRefunded("evt_r1", "in_a1", 1990, 1990));
        Assert.Equal(0, Database().StatsOf(affiliateId, new AffiliateProgram(), DateTimeOffset.UtcNow).PendingCents);
    }

    [Fact]
    public async Task PagamentoDeQuemNaoFoiIndicadoNaoGeraComissao()
    {
        var (_, _, plainId) = await RegisterAsync();
        var ok = await PostWebhook(InvoicePaid("evt_p1", "in_p1", plainId, 1990));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(0, Database().CommissionCountFor(plainId));
    }

    [Fact]
    public async Task PagamentoDeContaDesconhecidaPedeNovoEnvioSemRegistrar()
    {
        var first = await PostWebhook(InvoicePaid("evt_u1", "in_u1", 999999, 1990));
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        var again = await PostWebhook(InvoicePaid("evt_u1", "in_u1", 999999, 1990));
        Assert.Equal(HttpStatusCode.Accepted, again.StatusCode); // não foi marcado como processado
    }

    [Fact]
    public async Task ChavePixInvalidaERecusadaEValidaFicaSalva()
    {
        var (client, _, aff) = await NewAffiliate();
        string csrf = await CsrfFrom(client, "/affiliate");
        var bad = await client.PostAsync("/affiliate/payout", Form(("csrf", csrf), ("pix", "ab")));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var ok = await client.PostAsync("/affiliate/payout", Form(("csrf", csrf), ("pix", "meu-pix@exemplo.com")));
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        Assert.Equal("meu-pix@exemplo.com", Database().AffiliateOf(aff.UserId)!.PixKey);
        var noCsrf = await client.PostAsync("/affiliate/payout", Form(("pix", "meu-pix@exemplo.com")));
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
    }

    [Fact]
    public async Task AdministradorVeESaldaSaquesPorPix()
    {
        var (_, affiliateId, _) = await NewAffiliate();
        var db = Database();
        long referred = db.CreateUser(NewEmail(), "h")!.Value;
        db.AddCommission(affiliateId, referred, "in_pay_" + Guid.NewGuid().ToString("N"), 14990, 0.35, 5246);
        var payout = db.RequestPayout(affiliateId, "pix@exemplo.com", new AffiliateProgram { HoldDays = 0, MinPayoutCents = 5000 }, DateTimeOffset.UtcNow);
        Assert.NotNull(payout);

        var anon = await Client().GetAsync("/v1/admin/payouts");
        Assert.Equal(HttpStatusCode.Forbidden, anon.StatusCode);

        var list = new HttpRequestMessage(HttpMethod.Get, "/v1/admin/payouts");
        list.Headers.Add("Authorization", "Bearer " + AdminToken());
        var json = await (await Client().SendAsync(list)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(json.GetProperty("payouts").EnumerateArray(), p => p.GetProperty("id").GetInt64() == payout!.Id && p.GetProperty("pixKey").GetString() == "pix@exemplo.com");

        var pay = new HttpRequestMessage(HttpMethod.Post, $"/v1/admin/payouts/{payout!.Id}/paid") { Content = JsonContent.Create(new { note = "pago em teste" }) };
        pay.Headers.Add("Authorization", "Bearer " + AdminToken());
        Assert.Equal(HttpStatusCode.OK, (await Client().SendAsync(pay)).StatusCode);

        var list2 = new HttpRequestMessage(HttpMethod.Get, "/v1/admin/payouts");
        list2.Headers.Add("Authorization", "Bearer " + AdminToken());
        var after = await (await Client().SendAsync(list2)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain(after.GetProperty("payouts").EnumerateArray(), p => p.GetProperty("id").GetInt64() == payout.Id);
    }

    [Fact]
    public async Task PlanosPublicosTrazemOsPrecosFechadosDeVitrine()
    {
        var plans = await Client().GetFromJsonAsync<JsonElement>("/v1/plans");
        var monthly = plans.GetProperty("plans").EnumerateArray().First(p => p.GetProperty("key").GetString() == "monthly");
        Assert.Equal(1990, monthly.GetProperty("offers")[0].GetProperty("totalCents").GetInt64());
        var annual = plans.GetProperty("plans").EnumerateArray().First(p => p.GetProperty("key").GetString() == "annual");
        Assert.Equal(14990, annual.GetProperty("offers")[0].GetProperty("totalCents").GetInt64());
    }
}
