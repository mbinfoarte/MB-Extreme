using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MBExtreme.Core.Social;

/// <summary>Um link de rede social ou grupo de ofertas.</summary>
public sealed record SocialLink(string Name, string Url, string Description = "", string Icon = "");

/// <summary>Um parceiro com cupom de desconto.</summary>
public sealed record Partner(string Name, string Description, string Coupon, string Discount, string Url, string Color = "#FFC400");

/// <summary>
/// Redes, ofertas, parceiros e suporte da MB InfoArt. Vem de um arquivo online (config/canal.json no GitHub) para trocar links, cupons e
/// parceiros sem lançar versão nova; sem internet, usa a última cópia baixada; sem nada, os padrões abaixo (tudo com o nome "mbinfoarte").
/// </summary>
public sealed record ChannelConfig
{
    public string Handle { get; init; } = "mbinfoarte";
    public string Name { get; init; } = "MB InfoArt";
    public string Tagline { get; init; } = "Tecnologia sem complicação";
    public string YouTubeChannelId { get; init; } = "";
    public string SupportWhatsApp { get; init; } = "";
    public string SupportHours { get; init; } = "";
    public string SupportEmail { get; init; } = "";
    public List<SocialLink> Links { get; init; } = new();
    public List<SocialLink> Offers { get; init; } = new();
    public List<Partner> Partners { get; init; } = new();

    [JsonIgnore] public string YouTube => $"https://www.youtube.com/@{Handle}";
    [JsonIgnore] public string Subscribe => YouTube + "?sub_confirmation=1";
    [JsonIgnore] public string Membership => YouTube + "/join";

    /// <summary>Links padrão a partir do nome (@mbinfoarte em todas as redes).</summary>
    public static ChannelConfig Defaults(string handle = "mbinfoarte") => new()
    {
        Handle = handle,
        Links = new()
        {
            new("YouTube", $"https://www.youtube.com/@{handle}", "Vídeos novos toda semana", "youtube"),
            new("Instagram", $"https://www.instagram.com/{handle}", "Dicas rápidas e bastidores", "instagram"),
            new("TikTok", $"https://www.tiktok.com/@{handle}", "Vídeos curtos", "tiktok"),
            new("Facebook", $"https://www.facebook.com/{handle}", "Página oficial", "facebook"),
            new("Threads", $"https://www.threads.net/@{handle}", "Conversas e novidades", "threads"),
        },
    };

    public static ChannelConfig? Parse(string json)
    {
        try
        {
            var config = JsonSerializer.Deserialize<ChannelConfig>(json, Options);
            if (config is null || string.IsNullOrWhiteSpace(config.Handle)) return null;
            // só endereços https (nada de programas ou arquivos)
            return config with
            {
                Links = config.Links.Where(l => IsHttps(l.Url)).ToList(),
                Offers = config.Offers.Where(l => IsHttps(l.Url)).ToList(),
                Partners = config.Partners.Where(p => IsHttps(p.Url)).ToList(),
            };
        }
        catch (JsonException) { return null; }
    }

    public static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public static bool IsHttps(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps;

    /// <summary>Link do WhatsApp do suporte (só números; com mensagem pronta).</summary>
    public string? WhatsAppLink(string message = "")
    {
        string digits = new(SupportWhatsApp.Where(char.IsDigit).ToArray());
        if (digits.Length < 10) return null;
        if (digits.Length <= 11) digits = "55" + digits;   // número do Brasil sem o código do país
        return $"https://wa.me/{digits}" + (message.Length > 0 ? "?text=" + Uri.EscapeDataString(message) : "");
    }

    public const string RemoteUrl = "https://raw.githubusercontent.com/mbinfoarte/MB-Extreme/main/config/canal.json";

    private static string CachePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "canal.json");

    /// <summary>A última cópia baixada, ou os padrões.</summary>
    public static ChannelConfig Load()
    {
        try { if (File.Exists(CachePath) && Parse(File.ReadAllText(CachePath)) is { } cached) return cached; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return Defaults();
    }

    /// <summary>Baixa o arquivo online e guarda uma cópia. Sem internet (ou repositório privado), fica com a cópia/padrão.</summary>
    public static async Task<ChannelConfig> RefreshAsync(HttpClient http, CancellationToken ct = default)
    {
        try
        {
            string json = await http.GetStringAsync(RemoteUrl, ct);
            if (Parse(json) is { } config)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
                await File.WriteAllTextAsync(CachePath, json, ct);
                return config;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException) { }
        return Load();
    }
}
