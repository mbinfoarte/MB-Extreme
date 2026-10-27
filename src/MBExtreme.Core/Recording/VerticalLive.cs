namespace MBExtreme.Core.Recording;

/// <summary>Plataforma da live vertical (formato de celular, 9:16).</summary>
public enum VerticalPlatform { Instagram, TikTok, YouTube, Other }

/// <summary>
/// Live vertical: só no vertical ou ao mesmo tempo que a horizontal (por exemplo YouTube na horizontal e Instagram ou TikTok na vertical).
/// Cada plataforma tem um endereço e limites; os números seguem o que elas pedem para transmissões de fora (software de live).
/// </summary>
public static class VerticalLive
{
    public sealed record Spec(VerticalPlatform Platform, string Name, string DefaultUrl, int Width, int Fps, int VideoKbps, string HowToKey);

    public static IReadOnlyList<Spec> All { get; } = new[]
    {
        new Spec(VerticalPlatform.Instagram, "Instagram", "rtmps://live-upload.instagram.com:443/rtmp", 720, 30, 3500,
            "No Instagram (computador ou app): Criar › Ao vivo › escolha \"Software de transmissão\" (ou o Produtor ao vivo) e copie a URL e a chave. A chave muda a cada live."),
        new Spec(VerticalPlatform.TikTok, "TikTok", "", 720, 30, 4000,
            "No TikTok LIVE Studio ou em LIVE Center › Chave de transmissão: copie a URL do servidor e a chave (precisa de conta liberada para live por software)."),
        new Spec(VerticalPlatform.YouTube, "YouTube", "rtmp://a.rtmp.youtube.com/live2", 1080, 30, 6000,
            "No YouTube Studio › Transmitir ao vivo, crie uma segunda transmissão (ou use outra chave) e copie a chave. Vídeo vertical aparece como live vertical/Shorts."),
        new Spec(VerticalPlatform.Other, "Outro servidor", "", 1080, 30, 5000,
            "Cole o endereço rtmp:// ou rtmps:// e a chave que a plataforma informa."),
    };

    public static Spec For(VerticalPlatform platform) => All[Math.Clamp((int)platform, 0, All.Count - 1)];

    /// <summary>O destino (servidor + chave). Endereço vazio usa o padrão da plataforma.</summary>
    public static StreamTarget Target(VerticalPlatform platform, string? url, string key)
    {
        string server = string.IsNullOrWhiteSpace(url) ? For(platform).DefaultUrl : url.Trim();
        return new StreamTarget(StreamService.Custom, server, key);
    }

    /// <summary>A montagem vertical reduzida para o tamanho da plataforma (nunca aumenta).</summary>
    public static VerticalLayout Fit(VerticalLayout layout, VerticalPlatform platform)
    {
        int width = For(platform).Width;
        return layout.Width > width ? layout.ScaledToWidth(width) : layout;
    }
}
