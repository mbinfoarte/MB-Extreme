using System.Text.RegularExpressions;

namespace MBExtreme.Core.Recording;

/// <summary>Endereço do chat da transmissão, para abrir numa janela própria (só aceita nomes e códigos no formato certo).</summary>
public static class ChatLinks
{
    private static readonly Regex TwitchChannel = new(@"^[A-Za-z0-9_]{3,25}$", RegexOptions.Compiled);
    private static readonly Regex YouTubeId = new(@"^[A-Za-z0-9_-]{11}$", RegexOptions.Compiled);
    private static readonly Regex YouTubeFromUrl = new(@"(?:[?&]v=|youtu\.be/|/live/)([A-Za-z0-9_-]{11})", RegexOptions.Compiled);

    public static bool Supports(StreamService service) => service is StreamService.Twitch or StreamService.YouTube;

    public static string Prompt(StreamService service) => service switch
    {
        StreamService.Twitch => "Nome do seu canal na Twitch",
        StreamService.YouTube => "Endereço ou código da transmissão ao vivo (aparece no link, depois de v=)",
        _ => "Esta plataforma não tem chat para abrir por aqui",
    };

    public static string? Url(StreamService service, string? input)
    {
        string text = (input ?? "").Trim();
        if (text.Length == 0) return null;
        switch (service)
        {
            case StreamService.Twitch:
                text = text.TrimStart('@');
                int slash = text.LastIndexOf('/');
                if (slash >= 0) text = text[(slash + 1)..];
                return TwitchChannel.IsMatch(text) ? $"https://www.twitch.tv/popout/{text.ToLowerInvariant()}/chat?popout=" : null;
            case StreamService.YouTube:
                string? id = YouTubeId.IsMatch(text) ? text : YouTubeFromUrl.Match(text) is { Success: true } m ? m.Groups[1].Value : null;
                return id is null ? null : $"https://www.youtube.com/live_chat?is_popout=1&v={id}";
            default:
                return null;
        }
    }
}
