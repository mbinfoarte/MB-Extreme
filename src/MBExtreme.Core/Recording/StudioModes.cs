namespace MBExtreme.Core.Recording;

/// <summary>Modos prontos do estúdio (Live e Gravação): o usuário escolhe um e só edita se quiser.</summary>
public enum StudioMode { Light, Balanced, Quality, Ultra }

/// <summary>O que um modo usa: altura da imagem, quadros, taxa (Mbps), codificador e o esforço do codificador.</summary>
public sealed record ModePlan(StudioMode Mode, string Title, string Hint, int Height, int Fps, int Mbps, EncoderOption Encoder, EncoderQuality Quality)
{
    public string Summary => $"{Height}p · {Fps} fps · {Mbps} Mbps · {Encoder.Title}";
}

public static class StudioModes
{
    public static IReadOnlyList<StudioMode> All { get; } = new[] { StudioMode.Light, StudioMode.Balanced, StudioMode.Quality, StudioMode.Ultra };

    public static string Title(StudioMode m) => m switch
    {
        StudioMode.Light => "Leve", StudioMode.Balanced => "Equilibrado", StudioMode.Quality => "Qualidade", _ => "Ultra",
    };

    /// <summary>
    /// Plano da live. O codificador é o da placa de vídeo (NVENC, AMF ou Quick Sync); o processador só quando não há outro.
    /// Só YouTube: AV1 no Qualidade e no Ultra (se a placa tiver). Com Twitch, Facebook ou várias plataformas: H.264 e no máximo
    /// 6 Mbps no Twitch/Facebook. O Ultra sobe para 1440p só no YouTube.
    /// </summary>
    public static ModePlan ForLive(StudioMode mode, IReadOnlyList<EncoderOption> encoders, bool youTubeOnly, int maxMbps)
    {
        var h264 = Best(encoders, VideoCodec.H264) ?? encoders.FirstOrDefault() ?? FfmpegCommand.Software;
        var av1 = youTubeOnly ? Best(encoders, VideoCodec.Av1) : null;
        int cap = Math.Max(2, maxMbps);
        return mode switch
        {
            StudioMode.Light => new(mode, "Leve", "Internet fraca ou PC simples", 720, 30, Math.Min(3, cap), h264, EncoderQuality.Fast),
            StudioMode.Balanced => new(mode, "Equilibrado", "O melhor para a maioria", 1080, 60, Math.Min(6, cap), h264, EncoderQuality.Balanced),
            StudioMode.Quality => new(mode, "Qualidade", "Internet boa, jogo rápido", 1080, 60, Math.Min(av1 is null ? 8 : 9, cap), av1 ?? h264, EncoderQuality.Quality),
            _ => youTubeOnly
                ? new(mode, "Ultra", "1440p no YouTube", 1440, 60, Math.Min(18, cap), av1 ?? h264, EncoderQuality.Quality)
                : new(mode, "Ultra", "O máximo que a plataforma aceita", 1080, 60, Math.Min(8, cap), h264, EncoderQuality.Quality),
        };
    }

    /// <summary>Plano da gravação: H.265 ou AV1 quando a placa tem (arquivo menor com a mesma imagem), H.264 no Leve.</summary>
    public static ModePlan ForRecording(StudioMode mode, IReadOnlyList<EncoderOption> encoders, int screenHeight)
    {
        var h264 = Best(encoders, VideoCodec.H264) ?? encoders.FirstOrDefault() ?? FfmpegCommand.Software;
        var hevc = Best(encoders, VideoCodec.Hevc);
        var av1 = Best(encoders, VideoCodec.Av1);
        int top = Math.Clamp(screenHeight, 720, 2160);
        return mode switch
        {
            StudioMode.Light => new(mode, "Leve", "Arquivo pequeno, PC simples", 720, 60, 10, h264, EncoderQuality.Fast),
            StudioMode.Balanced => new(mode, "Equilibrado", "Full HD para YouTube", Math.Min(1080, top), 60, 20, hevc ?? h264, EncoderQuality.Balanced),
            StudioMode.Quality => new(mode, "Qualidade", "Para editar depois", Math.Min(1440, top), 60, 40, hevc ?? h264, EncoderQuality.Quality),
            _ => new(mode, "Ultra", "Resolução da tela, máxima nitidez", top, 60, top >= 2160 ? 90 : 60, av1 ?? hevc ?? h264, EncoderQuality.Quality),
        };
    }

    /// <summary>Melhor codificador de um formato: placa de vídeo primeiro (NVIDIA, AMD, Intel), processador por último.</summary>
    private static EncoderOption? Best(IReadOnlyList<EncoderOption> encoders, VideoCodec codec) =>
        encoders.Where(e => e.Codec == codec)
                .OrderBy(e => e.Kind switch { EncoderKind.Nvenc => 0, EncoderKind.Amf => 1, EncoderKind.Qsv => 2, _ => 3 })
                .FirstOrDefault();
}
