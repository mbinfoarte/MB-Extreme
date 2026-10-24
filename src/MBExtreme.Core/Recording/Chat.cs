using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace MBExtreme.Core.Recording;

public enum ChatKind { Message, Cheer, Subscription, SuperChat, Raid, Notice }

/// <summary>Uma mensagem do chat da transmissão.</summary>
public sealed record ChatMessage(string Id, string Platform, string Author, string Text, DateTime At, string? Color, ChatKind Kind, string? Amount)
{
    /// <summary>Mensagem que gerou dinheiro para o canal (cheer da Twitch, assinatura ou Super Chat do YouTube).</summary>
    public bool IsPaid => Kind is ChatKind.Cheer or ChatKind.Subscription or ChatKind.SuperChat;
}

public static class ChatRules
{
    private static readonly string[] Interrogatives = { "como ", "quando ", "onde ", "porque ", "por que ", "qual ", "quais ", "quem ", "quanto ", "quantos ", "será que ", "pode ", "dá pra ", "da pra ", "tem como " };

    /// <summary>Parece uma pergunta: tem interrogação ou começa com palavra de pergunta.</summary>
    public static bool IsQuestion(string text)
    {
        string t = (text ?? "").Trim().ToLowerInvariant();
        if (t.Length < 4) return false;
        return t.Contains('?') || Interrogatives.Any(w => t.StartsWith(w, StringComparison.Ordinal));
    }

    public static IEnumerable<ChatMessage> Filter(IEnumerable<ChatMessage> messages, int mode) => mode switch
    {
        1 => messages.Where(m => m.IsPaid),
        2 => messages.Where(m => m.IsPaid || IsQuestion(m.Text)),
        _ => messages,
    };
}

/// <summary>Leitura do chat da Twitch (IRC por WebSocket, sem login: o mesmo que o site usa para quem só assiste).</summary>
public static class TwitchIrc
{
    public static bool IsPing(string line) => line.StartsWith("PING", StringComparison.Ordinal);

    private static string Unescape(string value)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 >= value.Length) { sb.Append(value[i]); continue; }
            char next = value[++i];
            sb.Append(next switch { 's' => ' ', ':' => ';', 'r' => '\r', 'n' => '\n', '\\' => '\\', _ => next });
        }
        return sb.ToString();
    }

    public static ChatMessage? Parse(string line, DateTime? now = null)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var tags = new Dictionary<string, string>(StringComparer.Ordinal);
        string rest = line.TrimEnd('\r', '\n');
        if (rest.StartsWith('@'))
        {
            int space = rest.IndexOf(' ');
            if (space < 0) return null;
            foreach (string pair in rest[1..space].Split(';'))
            {
                int eq = pair.IndexOf('=');
                if (eq > 0) tags[pair[..eq]] = Unescape(pair[(eq + 1)..]);
            }
            rest = rest[(space + 1)..];
        }

        if (rest.StartsWith(':'))
        {
            int space = rest.IndexOf(' ');
            if (space < 0) return null;
            rest = rest[(space + 1)..];
        }

        int cmdEnd = rest.IndexOf(' ');
        if (cmdEnd < 0) return null;
        string command = rest[..cmdEnd];
        string trailing = "";
        int colon = rest.IndexOf(" :", StringComparison.Ordinal);
        if (colon >= 0) trailing = rest[(colon + 2)..];

        string Tag(string name) => tags.TryGetValue(name, out var v) ? v : "";
        string author = Tag("display-name").Length > 0 ? Tag("display-name") : Tag("login");
        string id = Tag("id").Length > 0 ? Tag("id") : Guid.NewGuid().ToString("N");
        string? color = Tag("color").Length > 0 ? Tag("color") : null;
        var at = now ?? DateTime.Now;

        if (command == "PRIVMSG")
        {
            bool cheer = long.TryParse(Tag("bits"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long bits) && bits > 0;
            string text = trailing;
            if (text.StartsWith("\u0001ACTION ", StringComparison.Ordinal)) text = text[8..].TrimEnd('\u0001');
            return new ChatMessage(id, "Twitch", author.Length == 0 ? "?" : author, text, at, color, cheer ? ChatKind.Cheer : ChatKind.Message, cheer ? $"{bits} bits" : null);
        }

        if (command == "USERNOTICE")
        {
            string kind = Tag("msg-id");
            string system = Tag("system-msg");
            string text = (system + (trailing.Length > 0 ? " — " + trailing : "")).Trim();
            if (kind is "sub" or "resub" or "subgift" or "anonsubgift" or "submysterygift" or "giftpaidupgrade" or "primepaidupgrade")
                return new ChatMessage(id, "Twitch", author.Length == 0 ? "?" : author, text, at, color, ChatKind.Subscription, "assinatura");
            if (kind == "raid")
                return new ChatMessage(id, "Twitch", author.Length == 0 ? "?" : author, text, at, color, ChatKind.Raid, null);
            return text.Length == 0 ? null : new ChatMessage(id, "Twitch", author.Length == 0 ? "?" : author, text, at, color, ChatKind.Notice, null);
        }

        return null;
    }
}

public sealed record YouTubeChatPage(IReadOnlyList<ChatMessage> Messages, string? NextPageToken, int PollMs, string? Error);

/// <summary>Leitura do chat do YouTube pela API oficial (liveChatMessages.list).</summary>
public static class YouTubeChat
{
    public static string? ChatIdFromVideo(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0) return null;
            return items[0].TryGetProperty("liveStreamingDetails", out var d) && d.TryGetProperty("activeLiveChatId", out var id) ? id.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    public static string MessagesUrl(string chatId, string apiKey, string? pageToken) =>
        $"https://www.googleapis.com/youtube/v3/liveChat/messages?liveChatId={Uri.EscapeDataString(chatId)}&part=snippet,authorDetails&maxResults=200&key={Uri.EscapeDataString(apiKey)}"
        + (string.IsNullOrEmpty(pageToken) ? "" : "&pageToken=" + Uri.EscapeDataString(pageToken));

    public static YouTubeChatPage Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var error)) return new YouTubeChatPage(Array.Empty<ChatMessage>(), null, 10000, Error(error));

            int poll = root.TryGetProperty("pollingIntervalMillis", out var p) && p.TryGetInt32(out int ms) ? ms : 10000;
            string? next = root.TryGetProperty("nextPageToken", out var n) ? n.GetString() : null;
            var list = new List<ChatMessage>();
            if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (!item.TryGetProperty("snippet", out var snippet)) continue;
                    string type = Str(snippet, "type");
                    string author = item.TryGetProperty("authorDetails", out var a) ? Str(a, "displayName") : "?";
                    string text = Str(snippet, "displayMessage");
                    string id = Str(item, "id");
                    DateTime at = DateTime.TryParse(Str(snippet, "publishedAt"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t.ToLocalTime() : DateTime.Now;
                    switch (type)
                    {
                        case "textMessageEvent":
                            list.Add(new ChatMessage(id, "YouTube", author, text, at, null, ChatKind.Message, null));
                            break;
                        case "superChatEvent":
                        {
                            string amount = snippet.TryGetProperty("superChatDetails", out var sc) ? Str(sc, "amountDisplayString") : "";
                            string comment = snippet.TryGetProperty("superChatDetails", out var sc2) ? Str(sc2, "userComment") : text;
                            list.Add(new ChatMessage(id, "YouTube", author, comment, at, null, ChatKind.SuperChat, amount));
                            break;
                        }
                        case "superStickerEvent":
                        {
                            string amount = snippet.TryGetProperty("superStickerDetails", out var ss) ? Str(ss, "amountDisplayString") : "";
                            list.Add(new ChatMessage(id, "YouTube", author, "(Super Sticker)", at, null, ChatKind.SuperChat, amount));
                            break;
                        }
                        case "newSponsorEvent":
                        case "memberMilestoneChatEvent":
                            list.Add(new ChatMessage(id, "YouTube", author, text.Length > 0 ? text : "virou membro do canal", at, null, ChatKind.Subscription, "membro"));
                            break;
                    }
                }
            }

            return new YouTubeChatPage(list, next, poll, null);
        }
        catch (JsonException)
        {
            return new YouTubeChatPage(Array.Empty<ChatMessage>(), null, 10000, "Resposta inesperada do YouTube.");
        }
    }

    private static string Error(JsonElement error)
    {
        string reason = error.TryGetProperty("errors", out var list) && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0 ? Str(list[0], "reason") : "";
        return reason switch
        {
            "liveChatEnded" => "O chat dessa live já terminou.",
            "liveChatNotFound" or "videoNotFound" => "O YouTube não achou o chat dessa live. Confira o código e se a live está no ar.",
            "quotaExceeded" or "dailyLimitExceeded" => "O limite diário grátis da API do YouTube acabou.",
            "forbidden" or "liveChatDisabled" => "O YouTube não deixou ler o chat com essa chave (chat desligado ou exige login do dono da live). Use \"Abrir o chat\" para ver numa janela.",
            "rateLimitExceeded" => "Consultas rápidas demais ao YouTube; o programa vai esperar mais.",
            _ => "O YouTube recusou a leitura do chat" + (reason.Length > 0 ? " (" + reason + ")" : "") + ".",
        };
    }

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}

/// <summary>Lista de perguntas escolhidas no chat para responder na transmissão.</summary>
public sealed class QuestionBoard
{
    public sealed class Item(ChatMessage message)
    {
        public ChatMessage Message { get; } = message;

        public bool Answered { get; set; }
    }

    private readonly List<Item> _items = new();

    public IReadOnlyList<Item> Items => _items;

    public int Pending => _items.Count(i => !i.Answered);

    /// <summary>Adiciona (uma mensagem só entra uma vez). Devolve false se já estava na lista.</summary>
    public bool Add(ChatMessage message)
    {
        if (_items.Any(i => i.Message.Id == message.Id)) return false;
        _items.Add(new Item(message));
        return true;
    }

    public void Remove(string id) => _items.RemoveAll(i => i.Message.Id == id);

    public void SetAnswered(string id, bool answered)
    {
        foreach (var item in _items.Where(i => i.Message.Id == id)) item.Answered = answered;
    }

    public void ClearAnswered() => _items.RemoveAll(i => i.Answered);

    /// <summary>Texto para copiar: uma linha por pergunta, com marca de respondida e valor pago.</summary>
    public string ToText() => string.Join(Environment.NewLine, _items.Select(i =>
        $"[{(i.Answered ? "x" : " ")}] {i.Message.Author}{(i.Message.Amount is { } a ? " (" + a + ")" : "")}: {i.Message.Text}"));
}

/// <summary>Conexão com o chat da plataforma em segundo plano. As mensagens chegam pelo evento <see cref="Message"/> (em outra thread).</summary>
public sealed class ChatFeed : IDisposable
{
    private CancellationTokenSource? _cts;

    public event Action<ChatMessage>? Message;

    /// <summary>Estado da conexão para mostrar ao usuário (texto, deu certo).</summary>
    public event Action<string, bool>? Status;

    public bool IsRunning => _cts is not null;

    public void Stop()
    {
        try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
        _cts = null;
    }

    public void StartTwitch(string login)
    {
        Stop();
        var cts = _cts = new CancellationTokenSource();
        _ = Task.Run(() => TwitchLoopAsync(login, cts.Token));
    }

    public void StartYouTube(HttpClient http, string videoId, string apiKey)
    {
        Stop();
        var cts = _cts = new CancellationTokenSource();
        _ = Task.Run(() => YouTubeLoopAsync(http, videoId, apiKey, cts.Token));
    }

    private async Task TwitchLoopAsync(string login, CancellationToken token)
    {
        int attempt = 0;
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(new Uri("wss://irc-ws.chat.twitch.tv:443"), token).ConfigureAwait(false);
                async Task Send(string text) => await socket.SendAsync(Encoding.UTF8.GetBytes(text + "\r\n"), WebSocketMessageType.Text, true, token).ConfigureAwait(false);
                await Send("CAP REQ :twitch.tv/tags twitch.tv/commands").ConfigureAwait(false);
                await Send("PASS SCHMOOPIIE").ConfigureAwait(false);
                await Send("NICK justinfan" + Random.Shared.Next(10000, 99999)).ConfigureAwait(false);
                await Send("JOIN #" + login).ConfigureAwait(false);
                Status?.Invoke($"Conectado ao chat da Twitch (#{login}).", true);
                attempt = 0;

                var buffer = new byte[16384];
                var text = new StringBuilder();
                while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    var result = await socket.ReceiveAsync(buffer, token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    text.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    if (!result.EndOfMessage) continue;
                    foreach (string line in text.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (TwitchIrc.IsPing(line)) { await Send("PONG :tmi.twitch.tv").ConfigureAwait(false); continue; }
                        if (TwitchIrc.Parse(line) is { } message) Message?.Invoke(message);
                    }
                    text.Clear();
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) when (ex is WebSocketException or HttpRequestException or IOException or InvalidOperationException) { }

            if (token.IsCancellationRequested) break;
            attempt++;
            Status?.Invoke("Chat da Twitch desconectado. Tentando de novo...", false);
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 2 * attempt)), token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
        }
    }

    private async Task YouTubeLoopAsync(HttpClient http, string videoId, string apiKey, CancellationToken token)
    {
        string? chatId = null, page = null;
        var seen = new HashSet<string>();
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (chatId is null)
                {
                    using var videoResponse = await http.GetAsync(LiveStatsService.YouTubeUrl(videoId, apiKey), token).ConfigureAwait(false);
                    chatId = YouTubeChat.ChatIdFromVideo(await videoResponse.Content.ReadAsStringAsync(token).ConfigureAwait(false));
                    if (chatId is null)
                    {
                        Status?.Invoke("Essa live ainda não tem chat ativo no YouTube (ela precisa estar no ar). Tentando de novo...", false);
                        await Task.Delay(TimeSpan.FromSeconds(20), token).ConfigureAwait(false);
                        continue;
                    }

                    Status?.Invoke("Conectado ao chat do YouTube.", true);
                }

                using var response = await http.GetAsync(YouTubeChat.MessagesUrl(chatId, apiKey, page), token).ConfigureAwait(false);
                var parsed = YouTubeChat.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
                if (parsed.Error is { } error)
                {
                    Status?.Invoke(error, false);
                    if (error.Contains("terminou") || error.Contains("limite diário") || error.Contains("não deixou")) return;
                    await Task.Delay(TimeSpan.FromSeconds(30), token).ConfigureAwait(false);
                    continue;
                }

                page = parsed.NextPageToken;
                foreach (var message in parsed.Messages)
                    if (seen.Add(message.Id)) Message?.Invoke(message);
                // cada consulta custa 5 unidades da cota diária grátis: nunca mais rápido que 8 s
                await Task.Delay(Math.Max(8000, parsed.PollMs), token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            Status?.Invoke("Sem conexão com o YouTube. Reconecte o chat.", false);
        }
    }

    public void Dispose() => Stop();
}
