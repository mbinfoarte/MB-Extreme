namespace MBExtreme.Core.Recording;

/// <summary>Para onde vai o vídeo vertical: cada destino escolhe tamanho, quadros, codec e qualidade de uma vez.</summary>
public enum SocialTarget { Shorts, Reels, TikTok, Maximum }

/// <summary>
/// Gravação vertical para redes sociais: predefinições por destino e o arquivo "pronto para postar"
/// (MP4 comum com o índice no começo, que as redes aceitam e começam a processar mais rápido).
/// </summary>
public static class SocialVideo
{
    /// <param name="Format">Índice em <see cref="VerticalFormat.All"/>.</param>
    /// <param name="Codec">Codec preferido; se a placa não tiver, fica o H.264.</param>
    /// <param name="QualityLevel">0 = taxa (Mbps), 1–3 = qualidade constante.</param>
    public sealed record Preset(SocialTarget Target, string Name, int Format, int Fps, VideoCodec Codec, int BitrateMbps, int QualityLevel, string Note);

    public static IReadOnlyList<Preset> All { get; } = new[]
    {
        new Preset(SocialTarget.Shorts, "YouTube Shorts", 0, 60, VideoCodec.H264, 0, 3, "1080×1920, 60 quadros, H.264 em qualidade máxima. O YouTube aceita até 4K vertical."),
        new Preset(SocialTarget.Reels, "Instagram Reels", 0, 30, VideoCodec.H264, 16, 0, "1080×1920, 30 quadros, H.264 a 16 Mbps: o que o Instagram processa melhor (ele recomprime vídeos pesados)."),
        new Preset(SocialTarget.TikTok, "TikTok", 0, 60, VideoCodec.H264, 20, 0, "1080×1920, 60 quadros, H.264 a 20 Mbps."),
        new Preset(SocialTarget.Maximum, "Máxima (para editar)", 2, 60, VideoCodec.Hevc, 0, 3, "2160×3840 (4K vertical), 60 quadros, HEVC em qualidade máxima: arquivo grande, para editar antes de postar."),
    };

    public static Preset For(SocialTarget target) => All[Math.Clamp((int)target, 0, All.Count - 1)];

    /// <summary>O melhor codificador da lista para o codec pedido (placa de vídeo antes do processador); sem ele, o melhor H.264.</summary>
    public static EncoderOption? PickEncoder(IReadOnlyList<EncoderOption> encoders, VideoCodec codec)
    {
        static int Rank(EncoderOption e) => e.Kind switch { EncoderKind.Nvenc => 0, EncoderKind.Amf => 1, EncoderKind.Qsv => 2, _ => 3 };
        return encoders.Where(e => e.Codec == codec).OrderBy(Rank).FirstOrDefault()
               ?? encoders.Where(e => e.Codec == VideoCodec.H264).OrderBy(Rank).FirstOrDefault();
    }

    /// <summary>Nome do arquivo pronto para postar: o mesmo nome com "-postar.mp4".</summary>
    public static string PostName(string file) =>
        Path.Combine(Path.GetDirectoryName(file) ?? "", Path.GetFileNameWithoutExtension(file) + "-postar.mp4");

    /// <summary>Copia o vídeo e o áudio sem recodificar (rápido e sem perder qualidade) para um MP4 com o índice no começo.</summary>
    public static IReadOnlyList<string> PostArgs(string input, string output) => new[]
    {
        "-hide_banner", "-loglevel", "error", "-y", "-i", input, "-map", "0:v:0", "-map", "0:a?", "-c", "copy", "-movflags", "+faststart", output,
    };
}
