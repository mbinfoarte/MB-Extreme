using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MBExtreme.Core.Recording;

public enum YouTubePrivacy { Private, Unlisted, Public }

/// <summary>Resposta de token do Google: o de acesso (dura uma hora) e o de renovação (fica guardado, protegido).</summary>
public sealed record YouTubeToken(string AccessToken, string? RefreshToken, DateTime ExpiresAtUtc)
{
    public bool Expired(DateTime nowUtc) => nowUtc >= ExpiresAtUtc.AddMinutes(-2);
}

/// <summary>
/// Login no Google para enviar vídeos (OAuth de aplicativo para computador, com PKCE e retorno no próprio PC)
/// e envio "retomável" da API do YouTube: o arquivo vai em pedaços e, se a internet cair, continua de onde parou.
/// O projeto do Google Cloud é da própria pessoa (Client ID e segredo dela), como nas chaves das estatísticas.
/// </summary>
public static class YouTubeUpload
{
    public const string Scope = "https://www.googleapis.com/auth/youtube.upload";
    public const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    public const string UploadEndpoint = "https://www.googleapis.com/upload/youtube/v3/videos?uploadType=resumable&part=snippet,status";
    public const int ChunkBytes = 8 * 1024 * 1024;   // múltiplo de 256 KB, como a API pede
    public const int MaxTitle = 100;
    public const int MaxDescription = 5000;

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Par do PKCE: o segredo que fica no PC e o desafio que vai para o Google.</summary>
    public static (string Verifier, string Challenge) NewPkce()
    {
        string verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        return (verifier, Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    public static string NewState() => Base64Url(RandomNumberGenerator.GetBytes(18));

    public static string AuthUrl(string clientId, string redirectUri, string challenge, string state)
    {
        var q = new Dictionary<string, string>
        {
            ["client_id"] = clientId.Trim(), ["redirect_uri"] = redirectUri, ["response_type"] = "code", ["scope"] = Scope,
            ["code_challenge"] = challenge, ["code_challenge_method"] = "S256", ["state"] = state,
            ["access_type"] = "offline", ["prompt"] = "consent",
        };
        return AuthEndpoint + "?" + string.Join("&", q.Select(kv => kv.Key + "=" + Uri.EscapeDataString(kv.Value)));
    }

    /// <summary>Lê o retorno do Google no endereço local: o código ou o erro, e confere o "state" (evita que outro site injete um código).</summary>
    public static (string? Code, string? Error) ParseCallback(string requestTarget, string expectedState)
    {
        int at = requestTarget.IndexOf('?');
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (at >= 0)
            foreach (string pair in requestTarget[(at + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                string key = Uri.UnescapeDataString(eq < 0 ? pair : pair[..eq]);
                string value = eq < 0 ? "" : Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
                values[key] = value;
            }

        if (values.TryGetValue("error", out var error)) return (null, error == "access_denied" ? "Você não autorizou o acesso." : "O Google respondeu: " + error);
        if (!values.TryGetValue("state", out var state) || state != expectedState) return (null, "A resposta do Google não confere (state). Tente conectar de novo.");
        if (!values.TryGetValue("code", out var code) || code.Length == 0) return (null, "O Google não mandou o código de acesso.");
        return (code, null);
    }

    public static YouTubeToken ParseToken(string json, DateTime nowUtc, string? keepRefresh = null)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("error", out var error))
        {
            string text = root.TryGetProperty("error_description", out var d) ? d.GetString() ?? "" : error.ToString();
            throw new InvalidOperationException(error.ToString() == "invalid_grant" ? "O acesso ao YouTube expirou ou foi removido. Conecte a conta de novo." : "Google: " + text);
        }
        string access = root.GetProperty("access_token").GetString() ?? throw new InvalidOperationException("O Google não mandou o token.");
        int seconds = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out int s) ? s : 3600;
        string? refresh = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : keepRefresh;
        return new YouTubeToken(access, refresh, nowUtc.AddSeconds(seconds));
    }

    public static async Task<YouTubeToken> ExchangeCodeAsync(HttpClient http, string clientId, string clientSecret, string code, string verifier, string redirectUri, CancellationToken token)
    {
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId.Trim(), ["client_secret"] = clientSecret.Trim(), ["code"] = code, ["code_verifier"] = verifier,
            ["grant_type"] = "authorization_code", ["redirect_uri"] = redirectUri,
        });
        using var response = await http.PostAsync(TokenEndpoint, body, token).ConfigureAwait(false);
        return ParseToken(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false), DateTime.UtcNow);
    }

    public static async Task<YouTubeToken> RefreshAsync(HttpClient http, string clientId, string clientSecret, string refreshToken, CancellationToken token)
    {
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId.Trim(), ["client_secret"] = clientSecret.Trim(), ["refresh_token"] = refreshToken, ["grant_type"] = "refresh_token",
        });
        using var response = await http.PostAsync(TokenEndpoint, body, token).ConfigureAwait(false);
        return ParseToken(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false), DateTime.UtcNow, refreshToken);
    }

    /// <summary>
    /// Espera o navegador voltar para http://127.0.0.1:porta/ com o código. Usa um socket simples (não precisa de permissão de administrador).
    /// </summary>
    public sealed class LoopbackReceiver : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

        public LoopbackReceiver()
        {
            _listener.Start();
            RedirectUri = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
        }

        public string RedirectUri { get; }

        /// <summary>Devolve o caminho pedido pelo navegador (ex.: "/?code=...&amp;state=...") e responde com uma página curta.</summary>
        public async Task<string> WaitAsync(CancellationToken token)
        {
            while (true)
            {
                using var client = await _listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                using var stream = client.GetStream();
                var buffer = new byte[8192];
                int read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
                string request = Encoding.ASCII.GetString(buffer, 0, Math.Max(0, read));
                string line = request.Split("\r\n")[0];
                string[] parts = line.Split(' ');
                if (parts.Length < 2 || parts[0] != "GET" || !parts[1].Contains("state=", StringComparison.Ordinal))
                {
                    await Respond(stream, 404, "", token).ConfigureAwait(false);   // favicon e afins
                    continue;
                }

                await Respond(stream, 200, "<!doctype html><meta charset=utf-8><title>MB Extreme</title><body style=\"background:#0b0b0c;color:#f4f4f5;font-family:Segoe UI,sans-serif;text-align:center;padding-top:80px\"><h2 style=\"color:#ffc400\">Pronto!</h2><p>Pode fechar esta aba e voltar para o MB Extreme.</p></body>", token).ConfigureAwait(false);
                return parts[1];
            }
        }

        private static async Task Respond(NetworkStream stream, int status, string html, CancellationToken token)
        {
            byte[] body = Encoding.UTF8.GetBytes(html);
            string head = $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Not Found")}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head), token).ConfigureAwait(false);
            await stream.WriteAsync(body, token).ConfigureAwait(false);
        }

        public void Dispose() => _listener.Stop();
    }

    // ---------------------------------------------------------------- envio

    /// <summary>O YouTube recusa títulos vazios, com mais de 100 letras ou com &lt; e &gt;.</summary>
    public static string CleanTitle(string? title, string fallback)
    {
        string t = new string((title ?? "").Where(c => c != '<' && c != '>' && !char.IsControl(c)).ToArray()).Trim();
        if (t.Length == 0) t = fallback.Trim();
        if (t.Length == 0) t = "Vídeo";
        return t.Length > MaxTitle ? t[..MaxTitle].TrimEnd() : t;
    }

    public static string CleanDescription(string? text)
    {
        string d = new string((text ?? "").Where(c => c != '<' && c != '>').ToArray()).Trim();
        return d.Length > MaxDescription ? d[..MaxDescription] : d;
    }

    public static string PrivacyValue(YouTubePrivacy privacy) => privacy switch
    {
        YouTubePrivacy.Public => "public",
        YouTubePrivacy.Unlisted => "unlisted",
        _ => "private",
    };

    /// <summary>Dados do vídeo (título, descrição, categoria Jogos, privacidade).</summary>
    public static string Metadata(string title, string description, YouTubePrivacy privacy) => JsonSerializer.Serialize(new
    {
        snippet = new { title, description, categoryId = "20" },
        status = new { privacyStatus = PrivacyValue(privacy), selfDeclaredMadeForKids = false },
    });

    /// <summary>"bytes=0-1048575" → 1048576 (quanto o servidor já recebeu). Sem cabeçalho = nada recebido.</summary>
    public static long ReceivedFromRange(string? range)
    {
        if (string.IsNullOrWhiteSpace(range)) return 0;
        int dash = range.LastIndexOf('-');
        return dash >= 0 && long.TryParse(range[(dash + 1)..], out long last) ? last + 1 : 0;
    }

    public static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".mp4" => "video/mp4",
        ".mkv" => "video/x-matroska",
        ".mov" => "video/quicktime",
        ".webm" => "video/webm",
        _ => "application/octet-stream",
    };

    public static string VideoUrl(string id) => "https://youtu.be/" + id;

    /// <summary>Começa o envio e devolve o endereço da sessão retomável.</summary>
    public static async Task<Uri> StartSessionAsync(HttpClient http, string accessToken, string file, string metadata, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, UploadEndpoint)
        {
            Content = new StringContent(metadata, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("X-Upload-Content-Length", new FileInfo(file).Length.ToString());
        request.Headers.Add("X-Upload-Content-Type", ContentType(file));
        using var response = await http.SendAsync(request, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(ApiError(response.StatusCode, await response.Content.ReadAsStringAsync(token).ConfigureAwait(false)));
        return response.Headers.Location ?? throw new InvalidOperationException("O YouTube não abriu a sessão de envio.");
    }

    /// <summary>Mensagem curta para os erros mais comuns da API.</summary>
    public static string ApiError(HttpStatusCode status, string body)
    {
        string reason = "";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var e) && e.TryGetProperty("errors", out var list) && list.GetArrayLength() > 0)
                reason = list[0].TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";
        }
        catch (JsonException) { }
        return reason switch
        {
            "quotaExceeded" or "uploadLimitExceeded" => "O limite de envios do dia acabou (cota da API ou do canal). Tente amanhã.",
            "youtubeSignupRequired" => "Esta conta do Google ainda não tem canal no YouTube. Crie o canal e tente de novo.",
            "forbidden" or "insufficientPermissions" => "O YouTube não deixou enviar com esta conta. Conecte de novo e aceite a permissão de envio.",
            "invalidTitle" => "O título não foi aceito pelo YouTube.",
            _ => status == HttpStatusCode.Unauthorized ? "O acesso ao YouTube expirou. Conecte a conta de novo." : $"O YouTube recusou o envio ({(int)status}{(reason.Length > 0 ? ", " + reason : "")}).",
        };
    }

    /// <summary>
    /// Envia o arquivo em pedaços. Se a conexão cair no meio, pergunta ao servidor quanto chegou e continua dali (até 5 vezes seguidas).
    /// Devolve o código do vídeo.
    /// </summary>
    public static async Task<string> UploadAsync(HttpClient http, Func<Task<string>> accessToken, Uri session, string file, IProgress<double>? progress, CancellationToken token)
    {
        long total = new FileInfo(file).Length;
        long sent = 0;
        int failures = 0;
        var buffer = new byte[ChunkBytes];
        await using var stream = File.OpenRead(file);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                stream.Position = sent;
                int length = (int)Math.Min(ChunkBytes, total - sent);
                int read = 0;
                while (read < length)
                {
                    int n = await stream.ReadAsync(buffer.AsMemory(read, length - read), token).ConfigureAwait(false);
                    if (n == 0) break;
                    read += n;
                }

                using var request = new HttpRequestMessage(HttpMethod.Put, session) { Content = new ByteArrayContent(buffer, 0, read) };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await accessToken().ConfigureAwait(false));
                request.Content.Headers.ContentRange = read == 0 ? new ContentRangeHeaderValue(total) : new ContentRangeHeaderValue(sent, sent + read - 1, total);   // tudo já chegou: só pede a resposta final
                using var response = await http.SendAsync(request, token).ConfigureAwait(false);
                if ((int)response.StatusCode == 308)
                {
                    sent = ReceivedFromRange(response.Headers.TryGetValues("Range", out var r) ? r.FirstOrDefault() : null);
                    failures = 0;
                    progress?.Report(total == 0 ? 1 : sent / (double)total);
                    continue;
                }
                string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    progress?.Report(1);
                    using var doc = JsonDocument.Parse(body);
                    return doc.RootElement.GetProperty("id").GetString() ?? throw new InvalidOperationException("O YouTube não devolveu o código do vídeo.");
                }
                if ((int)response.StatusCode >= 500 && ++failures <= 5) { await Task.Delay(TimeSpan.FromSeconds(2 * failures), token).ConfigureAwait(false); sent = await QueryAsync(http, accessToken, session, total, token).ConfigureAwait(false); continue; }
                throw new InvalidOperationException(ApiError(response.StatusCode, body));
            }
            catch (HttpRequestException) when (++failures <= 5)
            {
                await Task.Delay(TimeSpan.FromSeconds(3 * failures), token).ConfigureAwait(false);
                sent = await QueryAsync(http, accessToken, session, total, token).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Pergunta quanto do arquivo já chegou (depois de uma queda).</summary>
    private static async Task<long> QueryAsync(HttpClient http, Func<Task<string>> accessToken, Uri session, long total, CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, session) { Content = new ByteArrayContent(Array.Empty<byte>()) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await accessToken().ConfigureAwait(false));
            request.Content.Headers.ContentRange = new ContentRangeHeaderValue(total);   // "bytes */total"
            using var response = await http.SendAsync(request, token).ConfigureAwait(false);
            return (int)response.StatusCode == 308 ? ReceivedFromRange(response.Headers.TryGetValues("Range", out var r) ? r.FirstOrDefault() : null) : 0;
        }
        catch (HttpRequestException)
        {
            return 0;
        }
    }
}
