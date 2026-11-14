using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MBExtreme.Core.Licensing;

/// <summary>Sessão da conta do site (Firebase Authentication): o token vai para o servidor de licenças ativar o PC.</summary>
public sealed record AccountSession(string IdToken, string RefreshToken, string Email, string Name, bool EmailVerified);

/// <summary>Erro de conta já traduzido para mostrar ao cliente.</summary>
public sealed class AccountException(string message, string code = "") : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Login, cadastro e "esqueci a senha" da conta do site direto do programa (API REST do Firebase Authentication).
/// A senha vai só para o Google (Firebase); o servidor de licenças recebe apenas o token de sessão.
/// </summary>
public sealed class AccountAuth
{
    private const string Base = "https://identitytoolkit.googleapis.com/v1/accounts:";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _functionsBase;

    public AccountAuth(HttpClient http, string apiKey, string projectId, string region = "southamerica-east1")
    {
        _http = http;
        _apiKey = apiKey;
        _functionsBase = $"https://{region}-{projectId}.cloudfunctions.net/";
    }

    public async Task<AccountSession> SignInAsync(string email, string password, CancellationToken token = default)
    {
        var r = await PostAsync("signInWithPassword", new { email = email.Trim(), password, returnSecureToken = true }, token);
        return await WithInfoAsync(r, token);
    }

    public async Task<AccountSession> SignUpAsync(string name, string email, string password, CancellationToken token = default)
    {
        var r = await PostAsync("signUp", new { email = email.Trim(), password, returnSecureToken = true }, token);
        string idToken = r.GetProperty("idToken").GetString()!;
        if (name.Trim().Length > 0)
            await PostAsync("update", new { idToken, displayName = name.Trim()[..Math.Min(60, name.Trim().Length)], returnSecureToken = false }, token);
        return await WithInfoAsync(r, token, name.Trim());
    }

    public async Task SendPasswordResetAsync(string email, CancellationToken token = default) =>
        await PostAsync("sendOobCode", new { requestType = "PASSWORD_RESET", email = email.Trim() }, token);

    /// <summary>Token novo (depois de confirmar o e-mail, por exemplo).</summary>
    public async Task<AccountSession> RefreshAsync(AccountSession s, CancellationToken token = default)
    {
        var body = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = s.RefreshToken });
        using var resp = await _http.PostAsync("https://securetoken.googleapis.com/v1/token?key=" + _apiKey, body, token);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(token));
        if (!resp.IsSuccessStatusCode) throw Translate(doc.RootElement);
        string id = doc.RootElement.GetProperty("id_token").GetString()!;
        string refresh = doc.RootElement.GetProperty("refresh_token").GetString()!;
        var info = await LookupAsync(id, token);
        return s with { IdToken = id, RefreshToken = refresh, EmailVerified = info.Verified };
    }

    /// <summary>Manda o código de 6 dígitos para o e-mail (a mesma confirmação do site).</summary>
    public Task SendEmailCodeAsync(AccountSession s, CancellationToken token = default) => CallAsync("sendEmailCode", s.IdToken, new { }, token);

    public Task VerifyEmailCodeAsync(AccountSession s, string code, CancellationToken token = default) =>
        CallAsync("verifyEmailCode", s.IdToken, new { code = new string(code.Where(char.IsAsciiDigit).ToArray()) }, token);

    private async Task<AccountSession> WithInfoAsync(JsonElement r, CancellationToken token, string? name = null)
    {
        string id = r.GetProperty("idToken").GetString()!;
        string refresh = r.GetProperty("refreshToken").GetString()!;
        var info = await LookupAsync(id, token);
        return new AccountSession(id, refresh, info.Email, name ?? info.Name, info.Verified);
    }

    private async Task<(string Email, string Name, bool Verified)> LookupAsync(string idToken, CancellationToken token)
    {
        var r = await PostAsync("lookup", new { idToken }, token);
        var u = r.GetProperty("users")[0];
        return (u.TryGetProperty("email", out var e) ? e.GetString() ?? "" : "",
                u.TryGetProperty("displayName", out var n) ? n.GetString() ?? "" : "",
                u.TryGetProperty("emailVerified", out var v) && v.GetBoolean());
    }

    private async Task<JsonElement> PostAsync(string method, object body, CancellationToken token)
    {
        HttpResponseMessage resp;
        try { resp = await _http.PostAsJsonAsync(Base + method + "?key=" + _apiKey, body, Json, token); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !token.IsCancellationRequested)
        {
            throw new AccountException("Sem conexão com a internet.", "NETWORK");
        }
        using (resp)
        {
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(token));
            if (!resp.IsSuccessStatusCode) throw Translate(doc.RootElement);
            return doc.RootElement.Clone();
        }
    }

    private async Task CallAsync(string function, string idToken, object data, CancellationToken token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, _functionsBase + function) { Content = JsonContent.Create(new { data }, options: Json) };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", idToken);
        HttpResponseMessage resp;
        try { resp = await _http.SendAsync(req, token); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !token.IsCancellationRequested)
        {
            throw new AccountException("Sem conexão com a internet.", "NETWORK");
        }
        using (resp)
        {
            if (resp.IsSuccessStatusCode) return;
            string message = "Não deu certo agora. Tente de novo.";
            try
            {
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(token));
                if (doc.RootElement.TryGetProperty("error", out var e) && e.TryGetProperty("message", out var m) && m.GetString() is { Length: > 0 } text) message = text;
            }
            catch (JsonException) { }
            throw new AccountException(message);
        }
    }

    /// <summary>Erros do Firebase em português (a mensagem vem como "CODIGO : detalhe").</summary>
    public static AccountException Translate(JsonElement root)
    {
        string raw = root.TryGetProperty("error", out var e)
            ? e.ValueKind == JsonValueKind.Object && e.TryGetProperty("message", out var m) ? m.GetString() ?? "" : e.ToString()
            : "";
        string code = raw.Split(' ', ':')[0].Trim();
        string text = code switch
        {
            "EMAIL_NOT_FOUND" or "INVALID_PASSWORD" or "INVALID_LOGIN_CREDENTIALS" => "E-mail ou senha incorretos.",
            "USER_DISABLED" => "Esta conta foi desativada. Fale com o suporte.",
            "EMAIL_EXISTS" => "Já existe uma conta com este e-mail. Use Entrar (ou Esqueci a senha).",
            "INVALID_EMAIL" or "MISSING_EMAIL" => "Confira o e-mail digitado.",
            "WEAK_PASSWORD" or "MISSING_PASSWORD" => "A senha precisa ter pelo menos 8 caracteres.",
            "TOO_MANY_ATTEMPTS_TRY_LATER" => "Muitas tentativas. Espere alguns minutos e tente de novo.",
            "OPERATION_NOT_ALLOWED" or "PASSWORD_LOGIN_DISABLED" => "Entrar com e-mail e senha está temporariamente indisponível. Use Continuar com o Google.",
            "TOKEN_EXPIRED" or "INVALID_REFRESH_TOKEN" or "USER_NOT_FOUND" => "Sua sessão expirou. Entre de novo.",
            _ => "Não deu para entrar agora. Tente de novo em instantes.",
        };
        return new AccountException(text, code);
    }
}
