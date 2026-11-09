using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MBExtreme.Core.Recording;

/// <summary>O que a plataforma informa sobre a transmissão agora. Valores nulos = a plataforma não informou.</summary>
public sealed record LiveStats(
    bool IsLive, long? Viewers, long? TotalViews, long? Likes, long? Comments,
    string Title, string Category, string Description, DateTime? StartedAt, string Platform);

public sealed record LiveStatsResult(LiveStats? Stats, string? Error)
{
    public bool Ok => Stats is not null;
}

/// <summary>Histórico de espectadores da sessão: atual, pico e média.</summary>
public sealed class ViewerHistory
{
    private readonly List<double> _values = new();
    private readonly int _max;

    public ViewerHistory(int max = 120) => _max = Math.Max(10, max);

    public IReadOnlyList<double> Values => _values;

    public long Peak { get; private set; }

    public double Average => _values.Count == 0 ? 0 : _values.Average();

    public void Add(long viewers)
    {
        _values.Add(viewers);
        if (_values.Count > _max) _values.RemoveAt(0);
        Peak = Math.Max(Peak, viewers);
    }

    public void Reset()
    {
        _values.Clear();
        Peak = 0;
    }
}

/// <summary>
/// Estatísticas ao vivo pelas APIs oficiais das plataformas. YouTube: Data API v3 com a chave de API do próprio usuário (dados públicos da live).
/// Twitch: Helix com o aplicativo do próprio usuário. Facebook: a API exige aprovação do aplicativo e login do dono, então não é consultada aqui.
/// Mudar título, categoria e privacidade pela API exige o login do dono (OAuth); por isso o programa só mostra esses dados e abre o painel da plataforma.
/// </summary>
public sealed class LiveStatsService
{
    private static readonly Regex ApiKey = new(@"^[A-Za-z0-9_\-]{20,80}$", RegexOptions.Compiled);
    private static readonly Regex ClientId = new(@"^[A-Za-z0-9]{10,64}$", RegexOptions.Compiled);

    private readonly HttpClient _http;
    private string? _twitchToken;
    private DateTime _twitchTokenUntil;

    public LiveStatsService(HttpClient http) => _http = http;

    public static bool IsValidApiKey(string? key) => key is not null && ApiKey.IsMatch(key.Trim());

    public static bool IsValidClientId(string? id) => id is not null && ClientId.IsMatch(id.Trim());

    private static readonly Dictionary<string, string> YouTubeCategories = new()
    {
        ["1"] = "Filmes e animações", ["2"] = "Veículos", ["10"] = "Música", ["15"] = "Animais", ["17"] = "Esportes", ["19"] = "Viagens",
        ["20"] = "Jogos", ["22"] = "Pessoas e blogs", ["23"] = "Comédia", ["24"] = "Entretenimento", ["25"] = "Notícias e política",
        ["26"] = "Como fazer e estilo", ["27"] = "Educação", ["28"] = "Ciência e tecnologia", ["29"] = "Ativismo",
    };

    // ---------------------------------------------------------------- YouTube

    public static string YouTubeUrl(string videoId, string apiKey) =>
        $"https://www.googleapis.com/youtube/v3/videos?part=snippet,statistics,liveStreamingDetails&id={Uri.EscapeDataString(videoId)}&key={Uri.EscapeDataString(apiKey)}";

    public static LiveStatsResult ParseYouTube(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var error))
                return new LiveStatsResult(null, YouTubeError(error));

            if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
                return new LiveStatsResult(null, "O YouTube não achou esse vídeo ou essa live. Confira o código (aparece no endereço, depois de v=).");

            var item = items[0];
            string title = "", description = "", category = "";
            bool live = false;
            if (item.TryGetProperty("snippet", out var snippet))
            {
                title = Str(snippet, "title");
                description = Str(snippet, "description");
                string id = Str(snippet, "categoryId");
                category = YouTubeCategories.TryGetValue(id, out var name) ? name : id;
                live = Str(snippet, "liveBroadcastContent") == "live";
            }

            long? viewers = null, views = null, likes = null, comments = null;
            DateTime? started = null;
            if (item.TryGetProperty("liveStreamingDetails", out var details))
            {
                viewers = Number(details, "concurrentViewers");
                if (DateTime.TryParse(Str(details, "actualStartTime"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)) started = t.ToLocalTime();
                if (viewers is not null) live = true;
            }

            if (item.TryGetProperty("statistics", out var stats))
            {
                views = Number(stats, "viewCount");
                likes = Number(stats, "likeCount");
                comments = Number(stats, "commentCount");
            }

            return new LiveStatsResult(new LiveStats(live, viewers, views, likes, comments, title, category, description, started, "YouTube"), null);
        }
        catch (JsonException)
        {
            return new LiveStatsResult(null, "Resposta inesperada do YouTube.");
        }
    }

    private static string YouTubeError(JsonElement error)
    {
        string reason = "";
        if (error.TryGetProperty("errors", out var list) && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0) reason = Str(list[0], "reason");
        return reason switch
        {
            "quotaExceeded" or "dailyLimitExceeded" => "O limite diário gratuito da API do YouTube acabou. Volta amanhã ou aumente a cota no Google Cloud.",
            "keyInvalid" or "badRequest" => "A chave de API do YouTube não foi aceita. Confira se copiou inteira.",
            "accessNotConfigured" or "forbidden" or "ipRefererBlocked" => "A chave não tem permissão: ative a \"YouTube Data API v3\" no projeto dela no Google Cloud e tire restrições de endereço.",
            _ => "O YouTube recusou a consulta" + (reason.Length > 0 ? " (" + reason + ")" : "") + ".",
        };
    }

    public async Task<LiveStatsResult> QueryYouTubeAsync(string videoInput, string apiKey, CancellationToken token)
    {
        string? id = ChatLinks.YouTubeVideoId(videoInput);
        if (id is null) return new LiveStatsResult(null, "Informe o código ou o endereço da live do YouTube (aba Transmissão, campo do chat).");
        if (!IsValidApiKey(apiKey)) return new LiveStatsResult(null, "Informe a chave de API do YouTube (aba Estatísticas).");
        try
        {
            using var response = await _http.GetAsync(YouTubeUrl(id, apiKey.Trim()), token).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            return ParseYouTube(body);
        }
        catch (HttpRequestException) { return new LiveStatsResult(null, "Sem conexão com o YouTube."); }
        catch (TaskCanceledException) { return new LiveStatsResult(null, "O YouTube demorou demais para responder."); }
    }

    // ---------------------------------------------------------------- Twitch

    public static LiveStatsResult ParseTwitchStream(string json, string login)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.Number && status.GetInt32() >= 400)
                return new LiveStatsResult(null, "A Twitch recusou a consulta: " + Str(root, "message"));
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return new LiveStatsResult(null, "Resposta inesperada da Twitch.");

            if (data.GetArrayLength() == 0)
                return new LiveStatsResult(new LiveStats(false, null, null, null, null, "", "", "", null, "Twitch"), null);   // canal fora do ar

            var s = data[0];
            DateTime? started = DateTime.TryParse(Str(s, "started_at"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t.ToLocalTime() : null;
            return new LiveStatsResult(new LiveStats(Str(s, "type") == "live", Number(s, "viewer_count"), null, null, null, Str(s, "title"), Str(s, "game_name"), "", started, "Twitch"), null);
        }
        catch (JsonException)
        {
            return new LiveStatsResult(null, "Resposta inesperada da Twitch.");
        }
    }

    public async Task<LiveStatsResult> QueryTwitchAsync(string channelInput, string clientId, string clientSecret, CancellationToken token)
    {
        string? login = ChatLinks.TwitchLogin(channelInput);
        if (login is null) return new LiveStatsResult(null, "Informe o nome do seu canal da Twitch (aba Transmissão, campo do chat).");
        if (!IsValidClientId(clientId) || string.IsNullOrWhiteSpace(clientSecret)) return new LiveStatsResult(null, "Informe o Client ID e o segredo do seu aplicativo da Twitch (aba Estatísticas).");
        try
        {
            if (_twitchToken is null || DateTime.UtcNow >= _twitchTokenUntil)
            {
                using var form = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId.Trim(), ["client_secret"] = clientSecret.Trim(), ["grant_type"] = "client_credentials",
                });
                using var tokenResponse = await _http.PostAsync("https://id.twitch.tv/oauth2/token", form, token).ConfigureAwait(false);
                using var tokenDoc = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync(token).ConfigureAwait(false));
                if (!tokenDoc.RootElement.TryGetProperty("access_token", out var at) || at.GetString() is not { Length: > 0 } accessToken)
                    return new LiveStatsResult(null, "A Twitch não aceitou o Client ID e o segredo. Confira no console de desenvolvedores.");
                _twitchToken = accessToken;
                _twitchTokenUntil = DateTime.UtcNow.AddSeconds(Math.Max(60, (Number(tokenDoc.RootElement, "expires_in") ?? 3600) - 120));
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.twitch.tv/helix/streams?user_login=" + Uri.EscapeDataString(login));
            request.Headers.Add("Client-Id", clientId.Trim());
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _twitchToken);
            using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized) _twitchToken = null;
            return ParseTwitchStream(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false), login);
        }
        catch (HttpRequestException) { return new LiveStatsResult(null, "Sem conexão com a Twitch."); }
        catch (TaskCanceledException) { return new LiveStatsResult(null, "A Twitch demorou demais para responder."); }
        catch (JsonException) { return new LiveStatsResult(null, "Resposta inesperada da Twitch."); }
    }

    /// <summary>Página da plataforma onde se muda título, categoria, privacidade e latência (o programa não muda isso sozinho).</summary>
    public static string? ManageUrl(StreamService service, string? videoInput) => service switch
    {
        StreamService.YouTube => ChatLinks.YouTubeVideoId(videoInput) is { } id ? $"https://studio.youtube.com/video/{id}/livestreaming" : "https://studio.youtube.com/channel/UC/livestreaming",
        StreamService.Twitch => "https://dashboard.twitch.tv/stream-manager",
        StreamService.Facebook => "https://www.facebook.com/live/producer",
        _ => null,
    };

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static long? Number(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n)) return n;
        return v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? parsed : null;
    }
}
