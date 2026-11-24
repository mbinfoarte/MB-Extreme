using System.Net.Http;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MBExtreme.Core.Social;

/// <summary>Um vídeo do canal.</summary>
public sealed record YouTubeVideo(string Id, string Title, DateTimeOffset Published)
{
    public string Url => $"https://www.youtube.com/watch?v={Id}";
    public string Thumbnail => $"https://i.ytimg.com/vi/{Id}/mqdefault.jpg";
}

/// <summary>
/// Vídeos novos do canal pelo feed público do YouTube (youtube.com/feeds/videos.xml), sem chave de API.
/// O código do canal (UC...) é descoberto uma vez pela página do @nome e guardado.
/// </summary>
public static class YouTubeFeed
{
    private static readonly Regex[] ChannelPatterns =
    {
        new(@"<link rel=""canonical"" href=""https://www\.youtube\.com/channel/(UC[\w-]{22})"""),
        new(@"""externalId""\s*:\s*""(UC[\w-]{22})"""),
        new(@"""channelId""\s*:\s*""(UC[\w-]{22})"""),
        new(@"youtube\.com/channel/(UC[\w-]{22})"),
    };

    public static bool IsChannelId(string? id) => id is not null && Regex.IsMatch(id, @"^UC[\w-]{22}$");

    /// <summary>Acha o código do canal (UC + 22 letras) na página do @nome.</summary>
    public static string? ExtractChannelId(string html)
    {
        foreach (var pattern in ChannelPatterns)
            if (pattern.Match(html) is { Success: true } m) return m.Groups[1].Value;
        return null;
    }

    /// <summary>Lê o feed Atom do canal: vídeos do mais novo para o mais velho.</summary>
    public static IReadOnlyList<YouTubeVideo> ParseFeed(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            XNamespace atom = "http://www.w3.org/2005/Atom", yt = "http://www.youtube.com/xml/schemas/2015";
            return doc.Root?.Elements(atom + "entry").Select(e => new YouTubeVideo(
                    e.Element(yt + "videoId")?.Value ?? "",
                    e.Element(atom + "title")?.Value ?? "",
                    DateTimeOffset.TryParse(e.Element(atom + "published")?.Value, out var d) ? d : DateTimeOffset.MinValue))
                .Where(v => Regex.IsMatch(v.Id, @"^[\w-]{11}$"))
                .OrderByDescending(v => v.Published)
                .ToList() ?? new List<YouTubeVideo>();
        }
        catch (System.Xml.XmlException) { return Array.Empty<YouTubeVideo>(); }
    }

    /// <summary>
    /// Vale avisar? Só se é outro vídeo (não o último avisado) e saiu há no máximo 3 dias (na primeira vez que o programa abre,
    /// não avisa de um vídeo antigo).
    /// </summary>
    public static bool ShouldNotify(YouTubeVideo? latest, string lastNotifiedId, DateTimeOffset now) =>
        latest is not null && latest.Id != lastNotifiedId && now - latest.Published <= TimeSpan.FromDays(3);

    /// <summary>"publicado hoje", "ontem", "há 3 dias"...</summary>
    public static string Ago(DateTimeOffset published, DateTimeOffset now)
    {
        var days = (int)Math.Floor((now.LocalDateTime.Date - published.LocalDateTime.Date).TotalDays);
        return days switch { <= 0 => "Publicado hoje", 1 => "Publicado ontem", < 30 => $"Publicado há {days} dias", _ => $"Publicado em {published.LocalDateTime:dd/MM/yyyy}" };
    }

    /// <summary>Descobre o código do canal pelo @nome (uma vez) e lê os vídeos mais recentes.</summary>
    public static async Task<(string? ChannelId, IReadOnlyList<YouTubeVideo> Videos)> LatestAsync(HttpClient http, string handle, string? knownId, CancellationToken ct = default)
    {
        string? id = IsChannelId(knownId) ? knownId : null;
        try
        {
            if (id is null)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"https://www.youtube.com/@{Uri.EscapeDataString(handle)}");
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) MBExtreme");
                request.Headers.AcceptLanguage.ParseAdd("pt-BR");
                using var response = await http.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode) return (null, Array.Empty<YouTubeVideo>());
                id = ExtractChannelId(await response.Content.ReadAsStringAsync(ct));
                if (id is null) return (null, Array.Empty<YouTubeVideo>());
            }
            string feed = await http.GetStringAsync($"https://www.youtube.com/feeds/videos.xml?channel_id={id}", ct);
            return (id, ParseFeed(feed));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return (id, Array.Empty<YouTubeVideo>()); }
    }
}
