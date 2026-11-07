using System.Text.Json;
using MBExtreme.Core.Licensing;
using MBExtreme.Core.Remote;
using System.Net.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using MBExtreme.LicenseServer;
using static MBExtreme.LicenseServer.Pages;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

string dataDir = config["Data:Directory"] ?? Path.Combine(AppContext.BaseDirectory, "data");
Directory.CreateDirectory(dataDir);
bool devMode = config.GetValue("Dev:Enabled", false);
string webhookSecret = config["Payments:WebhookSecret"] ?? "";
string csrfSecret = Security.RandomToken();
int limitScale = Math.Max(1, config.GetValue("Limits:Multiplier", 1)); // os testes automáticos aumentam os limites

var db = new Db(Path.Combine(dataDir, "licenses.db"));
var signing = new SigningKey(Path.Combine(dataDir, "signing-key.pem"));
var issuer = new LicenseIssuer(signing);
var limiter = new RateLimiter();

builder.Services.AddSingleton(db);

// ranking anônimo (opcional no app): banco separado e segredo próprio para embaralhar o código de instalação
string pepperPath = Path.Combine(dataDir, "ranking-pepper.txt");
string rankingPepper = config["Ranking:Pepper"] is { Length: >= 16 } configured ? configured : File.Exists(pepperPath) ? File.ReadAllText(pepperPath).Trim() : "";
if (rankingPepper.Length < 16) { rankingPepper = Security.RandomToken(); File.WriteAllText(pepperPath, rankingPepper); }
var ranking = new RankingStore(Path.Combine(dataDir, "ranking.db"), rankingPepper);
ranking.Purge();
builder.Services.AddSingleton(signing);
builder.Services.AddSingleton(issuer);

long monthlyCents = config.GetValue("Billing:MonthlyCents", 1990L);
int trialDays = Math.Clamp(config.GetValue("Billing:TrialDays", 3), 0, 60);
var fixedPrices = new Dictionary<string, long>();
foreach (var (key, cents) in PlanCatalog.DefaultPrices) fixedPrices[key] = config.GetValue($"Billing:Prices:{key}", cents);
monthlyCents = fixedPrices["monthly"];
var affiliateProgram = new AffiliateProgram
{
    CommissionMonths = Math.Clamp(config.GetValue("Affiliates:CommissionMonths", 12), 0, 60),
    HoldDays = Math.Clamp(config.GetValue("Affiliates:HoldDays", 30), 0, 365),
    MinPayoutCents = Math.Max(0, config.GetValue("Affiliates:MinPayoutCents", 5000L)),
    CookieDays = Math.Clamp(config.GetValue("Affiliates:CookieDays", 60), 1, 365),
};
var site = new SiteOptions(
    config["Site:Brand"] ?? "MB Extreme", config["Site:SupportEmail"] ?? "suporte@mbextreme.com.br",
    config["Download:SetupUrl"] ?? "", config["Download:PortableUrl"] ?? "", config["Download:Version"] ?? "Beta 01",
    config["Google:ClientId"] ?? "", trialDays, fixedPrices, monthlyCents, affiliateProgram);
Pages.Options = site;
builder.Services.AddSingleton(sp => new GoogleAuth(site.GoogleClientId));
var adminEmails = new HashSet<string>((config["Admin:Emails"] ?? "").Split(new[] { ',', ';' }).Select(e => e.Trim().ToLowerInvariant()).Where(e => e.Length > 0));
string stripeWebhookSecret = config["Stripe:WebhookSecret"] ?? "";
var stripePrices = new Dictionary<string, string>();
foreach (var option in PlanCatalog.Options)
    if (config[$"Stripe:Prices:{option.Key}"] is { Length: > 0 } priceId) stripePrices[option.Key] = priceId;
builder.Services.AddSingleton(sp => new StripeGateway(new HttpClient { Timeout = TimeSpan.FromSeconds(20) }, config["Stripe:SecretKey"] ?? "", stripePrices));

var relay = new RemoteRelay(db, signing.PublicKey, (config["Remote:Technicians"] ?? "").Split(new[] { ',', ';' }),
    TimeSpan.FromMinutes(config.GetValue("Remote:MaxSessionMinutes", 240)));
builder.Services.AddSingleton(relay);

var app = builder.Build();
app.UseStaticFiles();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });

const string CookieName = "mbx_session";
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

string Ip(HttpContext c) => c.Connection.RemoteIpAddress?.ToString() ?? "?";

(long UserId, string Email, string Session)? CurrentUser(HttpContext c)
{
    if (!c.Request.Cookies.TryGetValue(CookieName, out var token) || string.IsNullOrEmpty(token)) return null;
    long? id = db.SessionUser(token);
    var user = id is long u ? db.UserById(u) : null;
    return user is null ? null : (user.Id, user.Email, token);
}

void StartSession(HttpContext c, long userId)
{
    string token = Security.RandomToken();
    db.CreateSession(token, userId, DateTimeOffset.UtcNow.AddDays(14));
    c.Response.Cookies.Append(CookieName, token, new CookieOptions
    {
        HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = c.Request.IsHttps, Expires = DateTimeOffset.UtcNow.AddDays(14),
    });
}

string SafeNext(string? next) => next is { Length: > 0 } && next.StartsWith('/') && !next.StartsWith("//") && !next.Contains('\\') ? next : "/account";

IResult Html(string html, int status = 200) => Results.Content(html, "text/html; charset=utf-8", null, status);

Task<bool> CsrfOk(HttpContext c, IFormCollection form, string session) => Task.FromResult(
    form.TryGetValue("csrf", out var t) && Security.CsrfToken(session, csrfSecret) == t.ToString());

string Csrf(string session) => $"<input type=\"hidden\" name=\"csrf\" value=\"{Security.CsrfToken(session, csrfSecret)}\">";

string BaseUrl(HttpContext c) => (string.IsNullOrWhiteSpace(config["Site:BaseUrl"]) ? $"{c.Request.Scheme}://{c.Request.Host}" : config["Site:BaseUrl"]!).TrimEnd('/');

const string RefCookie = "mbx_ref";
const string NextCookie = "mbx_next";

/// <summary>Liga a conta recém-criada ao afiliado do link que a trouxe (cookie), sem aceitar auto-indicação.</summary>
void AttachReferral(HttpContext c, long newUserId)
{
    if (!c.Request.Cookies.TryGetValue(RefCookie, out var raw)) return;
    string code = AffiliateProgram.NormalizeCode(raw);
    if (!AffiliateProgram.ValidCode(code)) return;
    var affiliate = db.AffiliateByCode(code);
    if (affiliate is not null && affiliate.UserId != newUserId) db.AddReferral(newUserId, affiliate.UserId);
}

IResult AuthPage(HttpContext c, string title, string action, string button, bool register, string? error, string? next, int status = 200) =>
    Html(SitePages.AuthForm(site, title, action, button, register, error, next, BaseUrl(c)), status);

// ======================================================= site

app.MapGet("/", (HttpContext c) => Html(SitePages.Home(site, CurrentUser(c)?.Email, devMode)));
app.MapGet("/planos", (HttpContext c) => Html(SitePages.Plans(site, CurrentUser(c)?.Email)));
app.MapGet("/download", (HttpContext c) => Html(SitePages.Download(site, CurrentUser(c)?.Email)));
app.MapGet("/afiliados", (HttpContext c) =>
{
    var me = CurrentUser(c);
    return Html(SitePages.AffiliatesLanding(site, me?.Email, me is not null && db.AffiliateOf(me.Value.UserId) is not null));
});
app.MapGet("/health", () => Results.Text("ok"));
app.MapGet("/robots.txt", (HttpContext c) => Results.Text($"User-agent: *\nAllow: /\nDisallow: /account\nDisallow: /affiliate\nDisallow: /v1/\nSitemap: {BaseUrl(c)}/sitemap.xml\n"));
app.MapGet("/sitemap.xml", (HttpContext c) => Results.Content(
    "<?xml version=\"1.0\" encoding=\"UTF-8\"?><urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">" +
    string.Concat(new[] { "/", "/planos", "/download", "/afiliados", "/termos", "/privacidade" }.Select(u => $"<url><loc>{BaseUrl(c)}{u}</loc></url>")) + "</urlset>", "application/xml"));

IResult Legal(HttpContext c, string title, string file)
{
    string path = Path.Combine(AppContext.BaseDirectory, "legal", file);
    string text = File.Exists(path) ? File.ReadAllText(path) : "# " + title + "\n\nTexto em preparação.";
    return Html(SiteLayout.Narrow(site, title, SiteLayout.Markdown(text), CurrentUser(c)?.Email));
}
app.MapGet("/termos", (HttpContext c) => Legal(c, "Termos de uso", "termos.md"));
app.MapGet("/privacidade", (HttpContext c) => Legal(c, "Política de privacidade", "privacidade.md"));

/// <summary>Link de indicação: guarda o código por alguns dias e leva para a página inicial.</summary>
app.MapGet("/r/{code}", (HttpContext c, string code) =>
{
    code = AffiliateProgram.NormalizeCode(code);
    if (AffiliateProgram.ValidCode(code) && db.AffiliateByCode(code) is not null)
        c.Response.Cookies.Append(RefCookie, code, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = c.Request.IsHttps, Expires = DateTimeOffset.UtcNow.AddDays(affiliateProgram.CookieDays) });
    return Results.Redirect("/");
});

app.MapGet("/register", (HttpContext c) => { string next = SafeNext(c.Request.Query["next"]); c.Response.Cookies.Append(NextCookie, next, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = c.Request.IsHttps, MaxAge = TimeSpan.FromMinutes(30) }); return AuthPage(c, "Criar conta", "/register", "Criar conta", true, null, c.Request.Query["next"].ToString() is { Length: > 0 } ? next : null); });
app.MapPost("/register", async (HttpContext c) =>
{
    if (!limiter.Allow("reg|" + Ip(c), 10 * limitScale, TimeSpan.FromMinutes(10))) return AuthPage(c, "Criar conta", "/register", "Criar conta", true, "Muitas tentativas. Aguarde alguns minutos.", null, 429);
    var form = await c.Request.ReadFormAsync();
    string next = SafeNext(form["next"]);
    string email = form["email"].ToString().Trim().ToLowerInvariant(), password = form["password"].ToString();
    if (!Security.ValidEmail(email)) return AuthPage(c, "Criar conta", "/register", "Criar conta", true, "E-mail inválido.", next, 400);
    if (password.Length < 8 || password.Length > 200) return AuthPage(c, "Criar conta", "/register", "Criar conta", true, "A senha precisa ter de 8 a 200 caracteres.", next, 400);
    string name = form["name"].ToString().Trim();
    if (name.Length > 80) name = name[..80];
    long? id = db.CreateUser(email, Security.HashPassword(password), name);
    if (id is null) return AuthPage(c, "Criar conta", "/register", "Criar conta", true, "Este e-mail já tem conta. Entre com a senha.", next, 409);
    AttachReferral(c, id.Value);
    StartSession(c, id.Value);
    return Results.Redirect(next);
});

app.MapGet("/login", (HttpContext c) => { string next = SafeNext(c.Request.Query["next"]); c.Response.Cookies.Append(NextCookie, next, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = c.Request.IsHttps, MaxAge = TimeSpan.FromMinutes(30) }); return AuthPage(c, "Entrar", "/login", "Entrar", false, null, c.Request.Query["next"].ToString() is { Length: > 0 } ? next : null); });
app.MapPost("/login", async (HttpContext c) =>
{
    var form = await c.Request.ReadFormAsync();
    string next = SafeNext(form["next"]);
    if (!limiter.Allow("login|" + Ip(c), 10 * limitScale, TimeSpan.FromMinutes(5))) return AuthPage(c, "Entrar", "/login", "Entrar", false, "Muitas tentativas. Aguarde alguns minutos.", next, 429);
    var user = db.UserByEmail(form["email"].ToString().Trim().ToLowerInvariant());
    // mesma resposta e mesmo custo para e-mail inexistente e senha errada
    bool ok = Security.VerifyPassword(form["password"].ToString(), user?.PasswordHash ?? Security.HashPassword("x"));
    if (user is null || !ok) return AuthPage(c, "Entrar", "/login", "Entrar", false, "E-mail ou senha incorretos.", next, 401);
    StartSession(c, user.Id);
    return Results.Redirect(next);
});

/// <summary>Retorno do "Entrar com o Google": confere o token no servidor e abre a sessão. Conta nova nasce com senha inutilizável.</summary>
app.MapPost("/auth/google", async (HttpContext c) =>
{
    var google = app.Services.GetRequiredService<GoogleAuth>();
    if (!google.Configured) return Results.NotFound();
    if (!limiter.Allow("google|" + Ip(c), 20 * limitScale, TimeSpan.FromMinutes(5))) return AuthPage(c, "Entrar", "/login", "Entrar", false, "Muitas tentativas. Aguarde alguns minutos.", null, 429);
    var form = await c.Request.ReadFormAsync();
    // proteção exigida pelo Google: o valor do formulário deve ser igual ao do cookie g_csrf_token
    string csrfCookie = c.Request.Cookies["g_csrf_token"] ?? "";
    if (csrfCookie.Length == 0 || csrfCookie != form["g_csrf_token"].ToString()) return AuthPage(c, "Entrar", "/login", "Entrar", false, "Não foi possível confirmar o pedido. Tente de novo.", null, 400);

    var identity = await google.ValidateAsync(form["credential"], DateTimeOffset.UtcNow, c.RequestAborted);
    if (identity is null || !identity.EmailVerified) return AuthPage(c, "Entrar", "/login", "Entrar", false, "Não foi possível entrar com o Google. Use e-mail e senha ou tente de novo.", null, 401);

    var user = db.UserByGoogleSub(identity.Subject);
    if (user is null)
    {
        var sameEmail = db.UserByEmail(identity.Email);
        if (sameEmail is not null) { db.LinkGoogle(sameEmail.Id, identity.Subject); user = db.UserByGoogleSub(identity.Subject) ?? sameEmail; }
        else
        {
            long? id = db.CreateGoogleUser(identity.Email, identity.Subject, identity.Name, Security.HashPassword(Security.RandomToken()));
            if (id is null) return AuthPage(c, "Entrar", "/login", "Entrar", false, "Não foi possível criar a conta. Tente de novo.", null, 409);
            AttachReferral(c, id.Value);
            user = db.UserById(id.Value);
        }
    }
    if (user is null) return Results.BadRequest();
    StartSession(c, user.Id);
    string next = SafeNext(c.Request.Cookies[NextCookie]);
    c.Response.Cookies.Delete(NextCookie);
    return Results.Redirect(next);
});

app.MapPost("/logout", (HttpContext c) =>
{
    if (c.Request.Cookies.TryGetValue(CookieName, out var token)) db.DeleteSession(token);
    c.Response.Cookies.Delete(CookieName);
    return Results.Redirect("/");
});

app.MapGet("/account", (HttpContext c) =>
{
    var me = CurrentUser(c);
    if (me is null) return Results.Redirect("/login?next=/account");
    var (resolvedPlan, status, maxDevices, _) = issuer.Resolve(db.SubscriptionOf(me.Value.UserId));
    var sub = db.SubscriptionOf(me.Value.UserId);
    var devices = db.DevicesOf(me.Value.UserId);
    var stripe = app.Services.GetRequiredService<StripeGateway>();

    string planText = resolvedPlan == "pro"
        ? $"<span class=\"ok\">Pro</span> · renova em {sub!.PeriodEnd.LocalDateTime:dd/MM/yyyy}" + (status == "past_due" ? " · <span class=\"warn\">pagamento pendente</span>" : "")
        : "Gratuito";
    var rows = string.Concat(devices.Select(d => $"""
        <tr><td>{E(d.Name)}</td><td class="muted">{d.LastSeen.LocalDateTime:dd/MM/yyyy HH:mm}</td>
        <td><form method="post" action="/account/device/remove">{Csrf(me.Value.Session)}<input type="hidden" name="device" value="{E(d.DeviceId)}"><button class="link bad">Remover</button></form></td></tr>
        """));
    string trialInfo = sub is { Provider: "teste-gratis" } ? $"<p class=\"warn\">Teste grátis até {sub.PeriodEnd.LocalDateTime:dd/MM/yyyy}.</p>" : "";
    string offers = string.Concat(PlanCatalog.Options.Select(o =>
    {
        var one = PlanCatalog.Offer(monthlyCents, o, 1, fixedPrices);
        var two = PlanCatalog.Offer(monthlyCents, o, 2, fixedPrices);
        string buy = stripe.Configured && stripe.HasPrice(o.Key)
            ? $"<form method=\"post\" action=\"/account/checkout\">{Csrf(me.Value.Session)}<input type=\"hidden\" name=\"period\" value=\"{o.Key}\"><select name=\"devices\"><option value=\"1\">1 PC</option><option value=\"2\">2 PCs</option><option value=\"3\">3 PCs</option></select> <button class=\"go\">Assinar</button></form>"
            : "<span class=\"muted\">em breve</span>";
        return $"<tr><td>{E(o.Title)}{(o.DiscountPercent > 0 ? $" <span class=\"ok\">-{o.DiscountPercent}%</span>" : "")}</td><td>{PlanCatalog.Money(one.PerMonthCents)}/mês</td><td class=\"muted\">2 PCs: {PlanCatalog.Money(two.PerMonthCents)}/mês</td><td>{buy}</td></tr>";
    }));
    string portal = sub is { Provider: "stripe" } ? $"<form method=\"post\" action=\"/account/billing\">{Csrf(me.Value.Session)}<button class=\"go\">Gerenciar assinatura e faturas</button></form>" : "";
    string redeem = $"<form method=\"post\" action=\"/account/redeem\" class=\"box\">{Csrf(me.Value.Session)}<h2>Tenho uma chave de ativação</h2><input name=\"key\" placeholder=\"MBX-XXXX-XXXX-XXXX-XXXX\" maxlength=\"40\" required><button class=\"go\">Resgatar</button></form>";
    string devTool = devMode
        ? $"<form method=\"post\" action=\"/account/dev-subscribe\">{Csrf(me.Value.Session)}<button class=\"go\">Simular assinatura Pro (modo de teste)</button></form>"
        : "<a class=\"go\" href=\"/\">Assinar o plano Pro</a>";

    return Html(Page("Minha conta", $"""
        <h1>Minha conta</h1><p class="muted">{E(me.Value.Email)}</p>
        <div class="box"><h2>Plano</h2><p>{planText}</p>{trialInfo}{portal}{(resolvedPlan == "pro" ? "" : devTool)}</div>
        <div class="box"><h2>Baixar o programa</h2><p class="muted">Instale o MB Extreme, abra <b>Licença e conta</b> e clique em <b>Entrar e ativar este PC</b>. O teste de {trialDays} dias começa no primeiro PC ativado.</p><a class="go" href="/download">Ir para o download</a></div>
        <div class="box"><h2>Planos</h2><table>{offers}</table></div>
        <div class="box"><h2>Programa de afiliados</h2><p class="muted">Indique o MB Extreme e ganhe comissão recorrente (Latão, Bronze, Prata e Ouro).</p><a class="go" href="/affiliate">{(db.AffiliateOf(me.Value.UserId) is null ? "Virar afiliado" : "Abrir meu painel")}</a></div>
        {redeem}
        <div class="box"><h2>Computadores ({devices.Count} de {maxDevices})</h2>
        {(devices.Count == 0 ? "<p class=\"muted\">Nenhum computador ativado. No MB Extreme, abra Licença e clique em Entrar.</p>" : $"<table><tr><th>Nome</th><th>Último uso</th><th></th></tr>{rows}</table>")}</div>
        """, me.Value.Email));
});

app.MapPost("/account/device/remove", async (HttpContext c) =>
{
    var me = CurrentUser(c);
    if (me is null) return Results.Redirect("/login");
    var form = await c.Request.ReadFormAsync();
    if (!await CsrfOk(c, form, me.Value.Session)) return Html(Message("Pedido inválido", "Recarregue a página e tente de novo.", "bad"), 400);
    db.RemoveDevice(me.Value.UserId, form["device"].ToString());
    return Results.Redirect("/account");
});

app.MapPost("/account/dev-subscribe", async (HttpContext c) =>
{
    if (!devMode) return Results.NotFound();
    var me = CurrentUser(c);
    if (me is null) return Results.Redirect("/login");
    var form = await c.Request.ReadFormAsync();
    if (!await CsrfOk(c, form, me.Value.Session)) return Html(Message("Pedido inválido", "Recarregue a página e tente de novo.", "bad"), 400);
    db.SetSubscription(new Subscription(me.Value.UserId, "pro", "active", DateTimeOffset.UtcNow.AddDays(30), 2, "teste", null));
    return Results.Redirect("/account");
});

app.MapGet("/activate", (HttpContext c) =>
{
    string code = Security.NormalizeUserCode(c.Request.Query["code"].ToString());
    var me = CurrentUser(c);
    if (me is null) return Results.Redirect("/login?next=" + Uri.EscapeDataString("/activate?code=" + code));
    var request = db.DeviceCodeBy("user_code", code);
    if (request is null || request.ExpiresAt < DateTimeOffset.UtcNow || request.State != "pending")
        return Html(Message("Código inválido", "Este código não existe ou expirou. Volte ao MB Extreme e peça um novo.", "bad"), 404);

    return Html(Page("Ativar computador", $"""
        <h1>Ativar este computador?</h1>
        <div class="card"><p>O MB Extreme em <b>{E(request.DeviceName)}</b> pediu acesso à conta <b>{E(me.Value.Email)}</b>.</p>
        <p class="muted">Código: <span class="code" style="font-size:20px">{E(request.UserCode)}</span> · só aprove se o código for o mesmo que aparece no programa.</p>
        <form method="post" action="/activate" style="display:flex;gap:12px">{Csrf(me.Value.Session)}<input type="hidden" name="code" value="{E(request.UserCode)}">
        <button class="go" name="decision" value="approve">Aprovar</button><button class="go" style="background:#2c2c33;color:#f2f2f4" name="decision" value="deny">Negar</button></form></div>
        """, me.Value.Email));
});

app.MapPost("/activate", async (HttpContext c) =>
{
    var me = CurrentUser(c);
    if (me is null) return Results.Redirect("/login");
    var form = await c.Request.ReadFormAsync();
    if (!await CsrfOk(c, form, me.Value.Session)) return Html(Message("Pedido inválido", "Recarregue a página e tente de novo.", "bad"), 400);
    var request = db.DeviceCodeBy("user_code", Security.NormalizeUserCode(form["code"].ToString()));
    if (request is null || request.ExpiresAt < DateTimeOffset.UtcNow || request.State != "pending")
        return Html(Message("Código inválido", "Este código não existe ou expirou.", "bad"), 404);

    if (form["decision"] != "approve")
    {
        db.SetDeviceCodeState(request.Code, "denied", null);
        return Html(Message("Acesso negado", "Este computador não foi ativado.", "warn"));
    }

    var (_, _, maxDevices, _) = issuer.Resolve(db.SubscriptionOf(me.Value.UserId));
    var devices = db.DevicesOf(me.Value.UserId);
    if (devices.All(d => d.DeviceId != request.DeviceId) && devices.Count >= maxDevices)
        return Html(Message("Limite de computadores", $"Sua conta já usa {devices.Count} de {maxDevices} computadores. Remova um em Minha conta e tente de novo.", "bad"), 409);

    string trialNote = "";
    if (trialDays > 0 && db.SubscriptionOf(me.Value.UserId) is null && db.TryMarkTrial(RemoteRelay.Hash(request.DeviceId), me.Value.UserId))
    {
        db.SetSubscription(new Subscription(me.Value.UserId, "pro", "trialing", DateTimeOffset.UtcNow.AddDays(trialDays), 1, "teste-gratis", null));
        trialNote = $" Você ganhou {trialDays} dias grátis do plano Pro neste computador.";
    }

    db.SetDeviceCodeState(request.Code, "approved", me.Value.UserId);
    return Html(Message("Computador ativado", "Pode voltar ao MB Extreme: a licença chega em alguns segundos." + trialNote, "ok"));
});

// ======================================================= afiliados

app.MapGet("/affiliate", (HttpContext c) =>
{
    var me = CurrentUser(c);
    if (me is null) return Results.Redirect("/login?next=/affiliate");
    var aff = db.AffiliateOf(me.Value.UserId);
    if (aff is null)
        return Html(Pages.Page("Programa de afiliados", $"""
            <h1>Virar afiliado</h1>
            <p class="muted">Receba de {(int)(AffiliateProgram.Tiers[0].Rate * 100)}% a {(int)(AffiliateProgram.Tiers[^1].Rate * 100)}% de comissão recorrente pelos clientes que você indicar.</p>
            <form method="post" action="/affiliate/join" class="box">{Csrf(me.Value.Session)}<button class="go">Criar meu link de afiliado</button></form>
            <p><a href="/afiliados" style="color:var(--accent)">Ver as regras do programa</a></p>
            """, me.Value.Email));
    string? message = c.Request.Query["msg"].ToString() switch { "payout" => "Saque pedido. Você receberá por Pix depois da conferência.", "pix" => "Chave Pix salva.", "min" => $"O saldo disponível ainda não chegou a {PlanCatalog.Money(affiliateProgram.MinPayoutCents)}.", _ => null };
    return Html(SitePages.AffiliateDashboard(site, me.Value.Email, Csrf(me.Value.Session), aff, db.StatsOf(aff.UserId, affiliateProgram, DateTimeOffset.UtcNow),
        db.CommissionsOf(aff.UserId), db.PayoutsOf(aff.UserId, false), BaseUrl(c), message, c.Request.Query["msg"] == "min"));
});

app.MapPost("/affiliate/join", async (HttpContext c) =>
{
    var me = CurrentUser(c);
    if (me is null) return Results.Redirect("/login");
    var form = await c.Request.ReadFormAsync();
    if (!await CsrfOk(c, form, me.Value.Session)) return Html(Message("Pedido inválido", "Recarregue a página e tente de novo.", "bad"), 400);
    db.CreateAffiliate(me.Value.UserId);
    return Results.Redirect("/affiliate");
});

app.MapPost("/affiliate/payout", async (HttpContext c) =>
{
    var me = CurrentUser(c);
    if (me is null) return Results.Redirect("/login");
    var form = await c.Request.ReadFormAsync();
    if (!await CsrfOk(c, form, me.Value.Session)) return Html(Message("Pedido inválido", "Recarregue a página e tente de novo.", "bad"), 400);
    var aff = db.AffiliateOf(me.Value.UserId);
    if (aff is null) return Results.Redirect("/affiliate");
    string pix = form["pix"].ToString().Trim();
    if (pix.Length is < 5 or > 120 || pix.Any(char.IsControl)) return Html(Message("Chave Pix inválida", "Confira a chave e tente de novo.", "bad"), 400);
    db.SetPixKey(aff.UserId, pix);
    var payout = db.RequestPayout(aff.UserId, pix, affiliateProgram, DateTimeOffset.UtcNow);
    return Results.Redirect("/affiliate?msg=" + (payout is not null ? "payout" : db.StatsOf(aff.UserId, affiliateProgram, DateTimeOffset.UtcNow).AvailableCents > 0 ? "min" : "pix"));
});

// ======================================================= API do aplicativo

app.MapGet("/v1/public-key", () => Results.Text(signing.PublicKey));

// ---- atualização dentro do programa: o app pergunta qual é a compilação mais nova; o CI publica o manifesto com um token próprio
string updatePath = Path.Combine(dataDir, "latest.json");
string updateToken = config["Updates:PublishToken"] ?? "";
app.MapGet("/v1/app/latest", () =>
{
    string? json = File.Exists(updatePath) ? File.ReadAllText(updatePath) : null;
    return MBExtreme.Core.Update.AppUpdates.Parse(json) is null ? Results.NotFound() : Results.Text(json!, "application/json");
});
app.MapPost("/v1/admin/app/latest", async (HttpContext c) =>
{
    if (updateToken.Length < 16) return Results.NotFound();   // desligado sem token configurado
    string given = c.Request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase).Trim();
    if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(given), System.Text.Encoding.UTF8.GetBytes(updateToken)))
        return Results.Json(new { message = "Não autorizado." }, statusCode: 403);
    using var reader = new StreamReader(c.Request.Body);
    string json = await reader.ReadToEndAsync();
    if (json.Length > 20_000 || MBExtreme.Core.Update.AppUpdates.Parse(json) is null)
        return Results.Json(new { message = "Manifesto inválido (precisa de build, url https e sha256)." }, statusCode: 400);
    await File.WriteAllTextAsync(updatePath, json);
    return Results.Json(new { ok = true });
});

app.MapPost("/v1/device/code", async (HttpContext c) =>
{
    if (!limiter.Allow("code|" + Ip(c), 20 * limitScale, TimeSpan.FromMinutes(10))) return Results.StatusCode(429);
    var body = await JsonSerializer.DeserializeAsync<JsonElement>(c.Request.Body, jsonOptions);
    string deviceId = body.TryGetProperty("deviceId", out var d) ? d.GetString() ?? "" : "";
    string name = body.TryGetProperty("deviceName", out var n) ? n.GetString() ?? "" : "";
    if (deviceId.Length is < 16 or > 64 || !deviceId.All(char.IsLetterOrDigit)) return Results.BadRequest(new { message = "Identificador de dispositivo inválido." });
    name = name.Length > 60 ? name[..60] : name;

    db.PurgeExpired();
    string userCode = Security.NewUserCode();
    var code = new LoginCode(Security.RandomToken(), userCode, deviceId, string.IsNullOrWhiteSpace(name) ? "Computador" : name, null, "pending", DateTimeOffset.UtcNow.AddMinutes(10), DateTimeOffset.UtcNow);
    db.CreateDeviceCode(code);
    return Results.Json(new { deviceCode = code.Code, userCode, verificationUri = $"{BaseUrl(c)}/activate?code={userCode}", intervalSeconds = 5, expiresInSeconds = 600 });
});

app.MapPost("/v1/device/token", async (HttpContext c) =>
{
    if (!limiter.Allow("poll|" + Ip(c), 200 * limitScale, TimeSpan.FromMinutes(10))) return Results.StatusCode(429);
    var body = await JsonSerializer.DeserializeAsync<JsonElement>(c.Request.Body, jsonOptions);
    string deviceCode = body.TryGetProperty("deviceCode", out var dc) ? dc.GetString() ?? "" : "";
    string deviceId = body.TryGetProperty("deviceId", out var di) ? di.GetString() ?? "" : "";

    var request = db.DeviceCodeBy("code", deviceCode);
    if (request is null || request.DeviceId != deviceId) return Results.Json(new { status = "expired", message = "Código desconhecido." });
    if (request.ExpiresAt < DateTimeOffset.UtcNow) { db.DeleteDeviceCode(request.Code); return Results.Json(new { status = "expired", message = "O código expirou." }); }
    if (DateTimeOffset.UtcNow - request.LastPoll < TimeSpan.FromSeconds(3)) return Results.Json(new { status = "slow_down" });
    db.TouchPoll(request.Code);

    switch (request.State)
    {
        case "denied":
            db.DeleteDeviceCode(request.Code);
            return Results.Json(new { status = "denied", message = "O acesso foi negado no site." });
        case "approved" when request.UserId is long userId:
            var user = db.UserById(userId);
            if (user is null) return Results.Json(new { status = "denied" });
            db.UpsertDevice(userId, request.DeviceId, request.DeviceName);
            string license = issuer.Issue(user, db.SubscriptionOf(userId), request.DeviceId);
            db.DeleteDeviceCode(request.Code);
            return Results.Json(new { status = "approved", license });
        default:
            return Results.Json(new { status = "pending" });
    }
});

(User User, Device Device, LicensePayload Payload)? Authenticate(JsonElement body, out IResult? error)
{
    error = null;
    string license = body.TryGetProperty("license", out var l) ? l.GetString() ?? "" : "";
    string deviceId = body.TryGetProperty("deviceId", out var d) ? d.GetString() ?? "" : "";
    var payload = LicenseToken.Verify(license, signing.PublicKey); // vale mesmo vencida: só prova que fomos nós que emitimos
    if (payload is null || payload.Device != deviceId) { error = Results.Json(new { message = "Licença inválida." }, statusCode: 403); return null; }
    var user = long.TryParse(payload.Subject, out long id) ? db.UserById(id) : null;
    if (user is null) { error = Results.Json(new { message = "Conta não encontrada." }, statusCode: 403); return null; }
    var device = db.DeviceOf(user.Id, deviceId);
    if (device is null) { error = Results.Json(new { message = "Este computador foi removido da conta. Entre de novo para ativá-lo." }, statusCode: 403); return null; }
    return (user, device, payload);
}

app.MapPost("/v1/license/refresh", async (HttpContext c) =>
{
    if (!limiter.Allow("refresh|" + Ip(c), 60 * limitScale, TimeSpan.FromMinutes(10))) return Results.StatusCode(429);
    var body = await JsonSerializer.DeserializeAsync<JsonElement>(c.Request.Body, jsonOptions);
    var auth = Authenticate(body, out var error);
    if (auth is null) return error!;
    var (user, device, _) = auth.Value;
    db.TouchDevice(user.Id, device.DeviceId);
    return Results.Json(new { license = issuer.Issue(user, db.SubscriptionOf(user.Id), device.DeviceId) });
});

app.MapPost("/v1/license/deactivate", async (HttpContext c) =>
{
    if (!limiter.Allow("deact|" + Ip(c), 30 * limitScale, TimeSpan.FromMinutes(10))) return Results.StatusCode(429);
    var body = await JsonSerializer.DeserializeAsync<JsonElement>(c.Request.Body, jsonOptions);
    var auth = Authenticate(body, out var error);
    if (auth is null) return error!;
    db.RemoveDevice(auth.Value.User.Id, auth.Value.Device.DeviceId);
    return Results.Json(new { ok = true });
});

// ======================================================= cobrança (Stripe), chaves de ativação e planos

RankingEndpoints.Map(app, ranking, limiter, limitScale, Ip);

app.MapGet("/v1/plans", () => Results.Json(new
{
    currency = "BRL",
    trialDays,
    plans = PlanCatalog.Options.Select(o => new
    {
        o.Key, o.Title, o.Months, o.DiscountPercent,
        offers = Enumerable.Range(1, 3).Select(d => PlanCatalog.Offer(monthlyCents, o, d, fixedPrices)),
    }),
}));

app.MapPost("/account/checkout", async (HttpContext c) =>
{
    var me = CurrentUser(c);
    if (me is null) return Results.Redirect("/login?next=/account");
    var form = await c.Request.ReadFormAsync();
    if (!await CsrfOk(c, form, me.Value.Session)) return Html(Message("Pedido inválido", "Recarregue a página e tente de novo.", "bad"), 400);
    var stripe = app.Services.GetRequiredService<StripeGateway>();
    string period = form["period"].ToString();
    if (!stripe.Configured || !stripe.HasPrice(period)) return Html(Message("Pagamento indisponível", "A cobrança ainda não está ligada. Use uma chave de ativação.", "warn"), 503);
    int devices = int.TryParse(form["devices"], out int n) ? Math.Clamp(n, 1, 10) : 1;
    try
    {
        string url = await stripe.CheckoutUrlAsync(me.Value.UserId, me.Value.Email, period, devices, BaseUrl(c) + "/account?paid=1", BaseUrl(c) + "/account", c.RequestAborted);
        return Results.Redirect(url);
    }
    catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException)
    {
        return Html(Message("Pagamento indisponível", "Não foi possível abrir a página de pagamento agora. Tente de novo em instantes.", "bad"), 502);
    }
});

app.MapPost("/account/billing", async (HttpContext c) =>
{
    var me = CurrentUser(c);
    if (me is null) return Results.Redirect("/login");
    var form = await c.Request.ReadFormAsync();
    if (!await CsrfOk(c, form, me.Value.Session)) return Html(Message("Pedido inválido", "Recarregue a página e tente de novo.", "bad"), 400);
    var stripe = app.Services.GetRequiredService<StripeGateway>();
    var parts = (db.SubscriptionOf(me.Value.UserId)?.ProviderRef ?? "").Split(':');
    if (!stripe.Configured || parts.Length < 2 || parts[0] != "stripe" || parts[1].Length == 0) return Html(Message("Sem assinatura no cartão", "Sua assinatura não foi feita pelo site.", "warn"), 404);
    try { return Results.Redirect(await stripe.PortalUrlAsync(parts[1], BaseUrl(c) + "/account", c.RequestAborted)); }
    catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException)
    {
        return Html(Message("Portal indisponível", "Tente de novo em instantes.", "bad"), 502);
    }
});

app.MapPost("/account/redeem", async (HttpContext c) =>
{
    var me = CurrentUser(c);
    if (me is null) return Results.Redirect("/login");
    var form = await c.Request.ReadFormAsync();
    if (!await CsrfOk(c, form, me.Value.Session)) return Html(Message("Pedido inválido", "Recarregue a página e tente de novo.", "bad"), 400);
    if (!limiter.Allow("redeem|" + me.Value.UserId, 10 * limitScale, TimeSpan.FromMinutes(10))) return Html(Message("Muitas tentativas", "Aguarde alguns minutos.", "bad"), 429);
    string raw = form["key"].ToString();
    var key = ActivationKeys.LooksValid(raw) ? db.ActivationKeyBy(ActivationKeys.Hash(raw)) : null;
    if (key is null || key.Redeemed) return Html(Message("Chave inválida", "Esta chave não existe ou já foi usada.", "bad"), 404);
    if (!db.RedeemActivationKey(ActivationKeys.Hash(raw), me.Value.UserId)) return Html(Message("Chave inválida", "Esta chave não existe ou já foi usada.", "bad"), 409);

    var current = db.SubscriptionOf(me.Value.UserId);
    var start = current is not null && current.PeriodEnd > DateTimeOffset.UtcNow && current.Status is "active" or "trialing" ? current.PeriodEnd : DateTimeOffset.UtcNow;
    db.SetSubscription(new Subscription(me.Value.UserId, key.Plan, "active", start.AddDays(key.Days), Math.Max(key.MaxDevices, current?.MaxDevices ?? 1), "chave", null));
    return Html(Message("Chave resgatada", $"Plano {key.Plan.ToUpperInvariant()} ativo por mais {key.Days} dias.", "ok"));
});

string? AdminEmail(HttpContext c)
{
    string header = c.Request.Headers.Authorization.ToString();
    string token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : "";
    var payload = LicenseToken.Verify(token, signing.PublicKey);
    return payload is not null && payload.ExpiresAt > DateTimeOffset.UtcNow && adminEmails.Contains(payload.Email.ToLowerInvariant()) ? payload.Email : null;
}

/// <summary>Gera chaves de ativação (quem as emite é um administrador listado em Admin:Emails). As chaves só aparecem nesta resposta.</summary>
app.MapPost("/v1/admin/keys", async (HttpContext c) =>
{
    if (adminEmails.Count == 0) return Results.NotFound();
    if (AdminEmail(c) is null) return Results.Json(new { message = "Não autorizado." }, statusCode: 403);
    var body = await JsonSerializer.DeserializeAsync<JsonElement>(c.Request.Body, jsonOptions);
    int count = body.TryGetProperty("count", out var cn) && cn.TryGetInt32(out int a) ? Math.Clamp(a, 1, 100) : 1;
    int days = body.TryGetProperty("days", out var dn) && dn.TryGetInt32(out int b) ? Math.Clamp(b, 1, 3650) : 30;
    int devices = body.TryGetProperty("maxDevices", out var mn) && mn.TryGetInt32(out int m) ? Math.Clamp(m, 1, 10) : 1;
    string note = body.TryGetProperty("note", out var nn) ? (nn.GetString() ?? "") : "";
    note = note.Length > 80 ? note[..80] : note;
    var keys = new List<string>();
    for (int i = 0; i < count; i++)
    {
        string key = ActivationKeys.Generate();
        db.AddActivationKey(ActivationKeys.Hash(key), "pro", days, devices, note);
        keys.Add(key);
    }
    return Results.Json(new { keys, days, maxDevices = devices });
});

// ---- sugestões enviadas pelo programa (aba Sugestões); lidas pelos administradores
app.MapPost("/v1/suggestions", async (HttpContext c) =>
{
    if (!limiter.Allow("sug|" + Ip(c), 5 * limitScale, TimeSpan.FromMinutes(10))) return Results.Json(new { message = "Muitas sugestões seguidas. Tente daqui a pouco." }, statusCode: 429);
    if (c.Request.ContentLength is > 64_000) return Results.BadRequest();
    JsonElement body;
    try { body = await JsonSerializer.DeserializeAsync<JsonElement>(c.Request.Body, jsonOptions); }
    catch (JsonException) { return Results.BadRequest(); }
    string S(string name) => body.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    string text = S("text").Trim();
    if (text.Length < 10) return Results.BadRequest(new { message = "Texto curto demais." });
    string kind = S("kind") is "Ideia" or "Problema" or "Elogio" ? S("kind") : "Ideia";
    db.AddSuggestion(kind, text, S("contact"), S("appVersion"), S("windows"), S("report"), RemoteRelay.Hash(Ip(c)));
    return Results.Json(new { ok = true });
});

app.MapGet("/v1/admin/suggestions", (HttpContext c) =>
{
    if (adminEmails.Count == 0) return Results.NotFound();
    if (AdminEmail(c) is null) return Results.Json(new { message = "Não autorizado." }, statusCode: 403);
    return Results.Json(new { suggestions = db.Suggestions(200) });
});

/// <summary>Saques de afiliados em aberto (para pagar por Pix). Só administradores.</summary>
app.MapGet("/v1/admin/payouts", (HttpContext c) =>
{
    if (adminEmails.Count == 0) return Results.NotFound();
    if (AdminEmail(c) is null) return Results.Json(new { message = "Não autorizado." }, statusCode: 403);
    var list = db.PayoutsOf(null, onlyOpen: true).Select(p => new { p.Id, p.AffiliateUserId, email = db.UserById(p.AffiliateUserId)?.Email, p.AmountCents, p.PixKey, p.RequestedAt });
    return Results.Json(new { payouts = list });
});

app.MapPost("/v1/admin/payouts/{id:long}/paid", async (HttpContext c, long id) =>
{
    if (adminEmails.Count == 0) return Results.NotFound();
    if (AdminEmail(c) is null) return Results.Json(new { message = "Não autorizado." }, statusCode: 403);
    var body = await JsonSerializer.DeserializeAsync<JsonElement>(c.Request.Body, jsonOptions);
    string note = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("note", out var n) ? n.GetString() ?? "" : "";
    return db.MarkPayoutPaid(id, note.Length > 120 ? note[..120] : note) ? Results.Json(new { ok = true }) : Results.NotFound();
});

app.MapPost("/v1/webhooks/stripe", async (HttpContext c) =>
{
    if (stripeWebhookSecret.Length < 8) return Results.NotFound();
    using var reader = new StreamReader(c.Request.Body);
    string raw = await reader.ReadToEndAsync();
    if (!StripeGateway.VerifySignature(raw, c.Request.Headers["Stripe-Signature"].ToString(), stripeWebhookSecret, DateTimeOffset.UtcNow))
        return Results.Json(new { message = "Assinatura inválida." }, statusCode: 400);

    JsonElement ev;
    try { ev = JsonSerializer.Deserialize<JsonElement>(raw); }
    catch (JsonException) { return Results.BadRequest(); }
    string eventId = ev.TryGetProperty("id", out var idp) ? idp.GetString() ?? "" : "";
    if (eventId.Length == 0) return Results.BadRequest();

    // pagamentos e estornos: geram ou cancelam a comissão do afiliado
    var payment = StripeGateway.TranslatePayment(ev);
    if (payment is { Kind: "refunded" })
    {
        if (!db.RememberWebhook("stripe|" + eventId)) return Results.Json(new { ok = true, duplicate = true });
        db.ReverseCommission(payment.InvoiceId);
        return Results.Json(new { ok = true });
    }
    if (payment is not null)
    {
        User? payer = payment.UserId is { } puid && long.TryParse(puid, out long pid) ? db.UserById(pid)
            : payment.CustomerId is { Length: > 0 } pcus ? db.UserByProviderRef(pcus) : null;
        if (payer is null) return Results.Json(new { ok = false, message = "Conta não encontrada; reenvie depois." }, statusCode: 202); // sem registrar: o Stripe tenta de novo
        if (!db.RememberWebhook("stripe|" + eventId)) return Results.Json(new { ok = true, duplicate = true });

        if (payment.AmountCents > 0 && db.ReferrerOf(payer.Id) is long referrer && db.AffiliateOf(referrer) is { } affiliate
            && affiliateProgram.PaysCommission(db.CommissionCountFor(payer.Id)))
        {
            var tier = AffiliateProgram.TierFor(db.StatsOf(affiliate.UserId, affiliateProgram, DateTimeOffset.UtcNow).PaidCustomers);
            db.AddCommission(affiliate.UserId, payer.Id, payment.InvoiceId, payment.AmountCents, tier.Rate, AffiliateProgram.Commission(payment.AmountCents, tier.Rate));
        }
        return Results.Json(new { ok = true });
    }

    if (!db.RememberWebhook("stripe|" + eventId)) return Results.Json(new { ok = true, duplicate = true });

    var update = StripeGateway.Translate(ev);
    if (update is null) return Results.Json(new { ok = true, ignored = true });

    User? user = update.UserId is { } uid && long.TryParse(uid, out long uidn) ? db.UserById(uidn)
        : update.CustomerId is { Length: > 0 } cus ? db.UserByProviderRef(cus) : null;
    if (user is null) return Results.Json(new { ok = false, message = "Conta não encontrada." }, statusCode: 202);

    var current = db.SubscriptionOf(user.Id);
    var oldRef = (current?.ProviderRef ?? "").Split(':');
    string customer = update.CustomerId ?? (oldRef.Length > 1 ? oldRef[1] : "");
    string subscription = update.SubscriptionId ?? (oldRef.Length > 2 ? oldRef[2] : "");
    var periodEnd = update.PeriodEnd ?? (current is not null && current.PeriodEnd > DateTimeOffset.UtcNow ? current.PeriodEnd : DateTimeOffset.UtcNow.AddDays(31));
    int maxDevices = update.Devices > 0 ? Math.Clamp(update.Devices, 1, 10) : current?.MaxDevices ?? 1;
    db.SetSubscription(new Subscription(user.Id, "pro", update.Status, periodEnd, maxDevices, "stripe", $"stripe:{customer}:{subscription}"));
    return Results.Json(new { ok = true });
});

// ======================================================= acesso remoto (suporte)

/// <summary>O computador do cliente pede um ID numérico. O segredo devolvido fica só no PC dele e autentica a conexão.</summary>
app.MapPost("/v1/remote/register", async (HttpContext c) =>
{
    if (!limiter.Allow("remote-reg|" + Ip(c), 20 * limitScale, TimeSpan.FromHours(1))) return Results.Json(new { message = "Muitas tentativas." }, statusCode: 429);
    JsonElement body;
    try { body = await JsonSerializer.DeserializeAsync<JsonElement>(c.Request.Body, jsonOptions); }
    catch (JsonException) { return Results.BadRequest(new { message = "Pedido inválido." }); }
    string device = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("deviceId", out var d) ? d.GetString() ?? "" : "";
    if (device.Length is < 8 or > 200) return Results.BadRequest(new { message = "Identificador do PC inválido." });

    string secret = Security.RandomToken();
    for (int attempt = 0; attempt < 10; attempt++)
    {
        string id = RemoteIds.NewId();
        if (db.RegisterRemoteDevice(id, RemoteRelay.Hash(secret), RemoteRelay.Hash(device))) return Results.Json(new { id, secret });
    }
    return Results.Json(new { message = "Não foi possível criar o ID agora." }, statusCode: 503);
});

app.Map("/v1/remote/ws/client", async (HttpContext c) =>
{
    if (!c.WebSockets.IsWebSocketRequest) return Results.BadRequest();
    string? id = RemoteIds.ParseId(c.Request.Query["id"]);
    string secret = c.Request.Query["secret"].ToString();
    var device = id is null ? null : db.RemoteDeviceById(id);
    if (device is null || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(device.SecretHash), System.Text.Encoding.UTF8.GetBytes(RemoteRelay.Hash(secret))))
        return Results.Json(new { message = "ID ou segredo inválido." }, statusCode: 401);
    using var socket = await c.WebSockets.AcceptWebSocketAsync();
    await relay.RunClientAsync(socket, id!, c.RequestAborted);
    return Results.Empty;
});

app.Map("/v1/remote/ws/tech", async (HttpContext c) =>
{
    if (!c.WebSockets.IsWebSocketRequest) return Results.BadRequest();
    if (!limiter.Allow("remote-tech|" + Ip(c), 30 * limitScale, TimeSpan.FromMinutes(10))) return Results.Json(new { message = "Muitas tentativas." }, statusCode: 429);
    var technician = relay.Technician(c.Request.Headers.Authorization.ToString());
    if (technician is null) return Results.Json(new { message = "Entre com a sua conta (Licença e conta) para conectar a outro computador." }, statusCode: 403);
    string? id = RemoteIds.ParseId(c.Request.Query["id"]);
    if (id is null) return Results.BadRequest(new { message = "ID inválido." });
    using var socket = await c.WebSockets.AcceptWebSocketAsync();
    await relay.RunTechnicianAsync(socket, id, technician.Value.Email, technician.Value.Name, c.RequestAborted);
    return Results.Empty;
});

app.MapGet("/v1/remote/audit", (HttpContext c) =>
{
    if (AdminEmail(c) is null) return Results.Json(new { message = "Não autorizado." }, statusCode: 403);
    return Results.Json(db.RemoteAudit(50).Select(a => new { a.Id, a.ClientId, a.Technician, a.RequestedAt, a.Accepted, a.EndedAt, a.Bytes, a.Result }));
});

// ======================================================= pagamentos

/// <summary>Recebe eventos do gateway já traduzidos para este formato (ver docs/LICENCIAMENTO.md). A assinatura HMAC é obrigatória.</summary>
app.MapPost("/v1/webhooks/payments", async (HttpContext c) =>
{
    if (webhookSecret.Length < 16) return Results.NotFound();
    using var reader = new StreamReader(c.Request.Body);
    string raw = await reader.ReadToEndAsync();
    if (!Security.SignatureMatches(raw, c.Request.Headers["X-Signature"].ToString(), webhookSecret)) return Results.Json(new { message = "Assinatura inválida." }, statusCode: 401);

    var body = JsonSerializer.Deserialize<JsonElement>(raw, jsonOptions);
    string eventId = body.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
    string kind = body.TryGetProperty("event", out var ev) ? ev.GetString() ?? "" : "";
    string email = body.TryGetProperty("email", out var em) ? em.GetString()?.Trim().ToLowerInvariant() ?? "" : "";
    string plan = body.TryGetProperty("plan", out var pl) ? pl.GetString() ?? "pro" : "pro";
    string reference = body.TryGetProperty("providerRef", out var rf) ? rf.GetString() ?? "" : "";
    DateTimeOffset periodEnd = body.TryGetProperty("periodEnd", out var pe) && pe.TryGetDateTimeOffset(out var parsed) ? parsed : DateTimeOffset.UtcNow.AddDays(31);
    int maxDevices = body.TryGetProperty("maxDevices", out var md) && md.TryGetInt32(out int m) ? Math.Clamp(m, 1, 10) : 2;

    if (eventId.Length == 0) return Results.BadRequest(new { message = "Evento sem id." });
    if (!db.RememberWebhook(eventId)) return Results.Json(new { ok = true, duplicate = true });
    var user = db.UserByEmail(email);
    if (user is null) return Results.Json(new { ok = false, message = "Conta não encontrada; o evento será reenviado quando a conta existir." }, statusCode: 202);

    string status = kind switch
    {
        "subscription.activated" or "subscription.renewed" => "active",
        "subscription.trialing" => "trialing",
        "subscription.past_due" => "past_due",
        "subscription.canceled" or "subscription.expired" => "canceled",
        _ => "",
    };
    if (status.Length == 0) return Results.BadRequest(new { message = "Evento desconhecido." });

    db.SetSubscription(new Subscription(user.Id, plan, status, periodEnd, maxDevices, "pagamentos", reference));
    return Results.Json(new { ok = true });
});

// concessão direta, só para testes locais (Dev:Enabled)
app.MapPost("/dev/grant", async (HttpContext c) =>
{
    if (!devMode) return Results.NotFound();
    var body = await JsonSerializer.DeserializeAsync<JsonElement>(c.Request.Body, jsonOptions);
    string email = body.TryGetProperty("email", out var e) ? e.GetString()?.ToLowerInvariant() ?? "" : "";
    int days = body.TryGetProperty("days", out var d) && d.TryGetInt32(out int n) ? n : 30;
    string status = body.TryGetProperty("status", out var s) ? s.GetString() ?? "active" : "active";
    var user = db.UserByEmail(email);
    if (user is null) return Results.NotFound();
    db.SetSubscription(new Subscription(user.Id, "pro", status, DateTimeOffset.UtcNow.AddDays(days), 2, "teste", null));
    return Results.Json(new { ok = true });
});

app.Run();

public partial class Program { }
