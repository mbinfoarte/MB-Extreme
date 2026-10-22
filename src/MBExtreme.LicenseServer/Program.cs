using System.Text.Json;
using MBExtreme.Core.Licensing;
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
builder.Services.AddSingleton(signing);
builder.Services.AddSingleton(issuer);

var app = builder.Build();

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

// ======================================================= site

app.MapGet("/", (HttpContext c) => Html(Page("Planos", Home(devMode), CurrentUser(c)?.Email)));
app.MapGet("/health", () => Results.Text("ok"));

app.MapGet("/register", (HttpContext c) => Html(Form("Criar conta", "/register", "Criar conta", true, null)));
app.MapPost("/register", async (HttpContext c) =>
{
    if (!limiter.Allow("reg|" + Ip(c), 10 * limitScale, TimeSpan.FromMinutes(10))) return Html(Form("Criar conta", "/register", "Criar conta", true, "Muitas tentativas. Aguarde alguns minutos."), 429);
    var form = await c.Request.ReadFormAsync();
    string email = form["email"].ToString().Trim().ToLowerInvariant(), password = form["password"].ToString();
    if (!Security.ValidEmail(email)) return Html(Form("Criar conta", "/register", "Criar conta", true, "E-mail inválido."), 400);
    if (password.Length < 8 || password.Length > 200) return Html(Form("Criar conta", "/register", "Criar conta", true, "A senha precisa ter de 8 a 200 caracteres."), 400);
    string name = form["name"].ToString().Trim();
    if (name.Length > 80) name = name[..80];
    long? id = db.CreateUser(email, Security.HashPassword(password), name);
    if (id is null) return Html(Form("Criar conta", "/register", "Criar conta", true, "Este e-mail já tem conta. Entre com a senha."), 409);
    StartSession(c, id.Value);
    return Results.Redirect("/account");
});

app.MapGet("/login", (HttpContext c) => Html(Form("Entrar", "/login", "Entrar", false, null, c.Request.Query["next"])));
app.MapPost("/login", async (HttpContext c) =>
{
    var form = await c.Request.ReadFormAsync();
    string next = SafeNext(form["next"]);
    if (!limiter.Allow("login|" + Ip(c), 10 * limitScale, TimeSpan.FromMinutes(5))) return Html(Form("Entrar", "/login", "Entrar", false, "Muitas tentativas. Aguarde alguns minutos.", next), 429);
    var user = db.UserByEmail(form["email"].ToString().Trim().ToLowerInvariant());
    // mesma resposta e mesmo custo para e-mail inexistente e senha errada
    bool ok = Security.VerifyPassword(form["password"].ToString(), user?.PasswordHash ?? Security.HashPassword("x"));
    if (user is null || !ok) return Html(Form("Entrar", "/login", "Entrar", false, "E-mail ou senha incorretos.", next), 401);
    StartSession(c, user.Id);
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

    string planText = resolvedPlan == "pro"
        ? $"<span class=\"ok\">Pro</span> · renova em {sub!.PeriodEnd.LocalDateTime:dd/MM/yyyy}" + (status == "past_due" ? " · <span class=\"warn\">pagamento pendente</span>" : "")
        : "Gratuito";
    var rows = string.Concat(devices.Select(d => $"""
        <tr><td>{E(d.Name)}</td><td class="muted">{d.LastSeen.LocalDateTime:dd/MM/yyyy HH:mm}</td>
        <td><form method="post" action="/account/device/remove">{Csrf(me.Value.Session)}<input type="hidden" name="device" value="{E(d.DeviceId)}"><button class="link bad">Remover</button></form></td></tr>
        """));
    string devTool = devMode
        ? $"<form method=\"post\" action=\"/account/dev-subscribe\">{Csrf(me.Value.Session)}<button class=\"go\">Simular assinatura Pro (modo de teste)</button></form>"
        : "<a class=\"go\" href=\"/\">Assinar o plano Pro</a>";

    return Html(Page("Minha conta", $"""
        <h1>Minha conta</h1><p class="muted">{E(me.Value.Email)}</p>
        <div class="card"><h2>Plano</h2><p>{planText}</p>{(resolvedPlan == "pro" ? "" : devTool)}</div>
        <div class="card"><h2>Computadores ({devices.Count} de {maxDevices})</h2>
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

    db.SetDeviceCodeState(request.Code, "approved", me.Value.UserId);
    return Html(Message("Computador ativado", "Pode voltar ao MB Extreme: a licença chega em alguns segundos.", "ok"));
});

// ======================================================= API do aplicativo

app.MapGet("/v1/public-key", () => Results.Text(signing.PublicKey));

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
