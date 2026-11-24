namespace MBExtreme.Core.Recording;

/// <summary>
/// Qual codificador usar. A lista já vem testada na placa (só entra o que funcionou), então "tem AV1" quer dizer placa atual:
/// NVIDIA RTX 40 e 50 (av1_nvenc), AMD RX 7000 e 9000 (av1_amf), Intel Arc (av1_qsv).
/// Ordem: chip de vídeo da placa (NVIDIA, AMD, Intel) antes do processador; dentro dele, AV1, depois H.265 e por último H.264.
/// </summary>
public static class EncoderChoice
{
    /// <summary>Nota do codificador: maior é melhor.</summary>
    public static int Score(EncoderOption e) =>
        (e.Kind switch { EncoderKind.Nvenc => 300, EncoderKind.Amf => 200, EncoderKind.Qsv => 100, _ => 0 })
        + (e.Codec switch { VideoCodec.Av1 => 30, VideoCodec.Hevc => 20, _ => 10 });

    /// <summary>Codecs que a plataforma recebe pela live (RTMP). O YouTube aceita AV1 e H.265 pelo RTMP aprimorado; as outras, só H.264.</summary>
    public static IReadOnlyList<VideoCodec> StreamCodecs(StreamService service) => service == StreamService.YouTube
        ? new[] { VideoCodec.Av1, VideoCodec.Hevc, VideoCodec.H264 }
        : new[] { VideoCodec.H264 };

    public static bool AcceptedBy(StreamService service, VideoCodec codec) => StreamCodecs(service).Contains(codec);

    /// <summary>O melhor para gravar (posição na lista; -1 = lista vazia).</summary>
    public static int BestIndex(IReadOnlyList<EncoderOption> encoders) => BestIndex(encoders, _ => true);

    /// <summary>O melhor que a plataforma aceita na live (posição na lista; -1 = nenhum serve).</summary>
    public static int BestStreamIndex(IReadOnlyList<EncoderOption> encoders, StreamService service) =>
        BestIndex(encoders, e => AcceptedBy(service, e.Codec));

    /// <summary>
    /// Para transmitir: mantém o escolhido se a plataforma aceita; senão, o melhor aceito do mesmo fabricante (ex.: AV1 NVENC vira H.264 NVENC na Twitch);
    /// sem nenhum do mesmo fabricante, o melhor aceito de todos. Null = nenhum codificador serve para essa plataforma.
    /// </summary>
    public static EncoderOption? ForStream(IReadOnlyList<EncoderOption> encoders, EncoderOption chosen, StreamService service)
    {
        if (AcceptedBy(service, chosen.Codec)) return chosen;
        int same = BestIndex(encoders, e => e.Kind == chosen.Kind && AcceptedBy(service, e.Codec));
        if (same >= 0) return encoders[same];
        int any = BestStreamIndex(encoders, service);
        return any >= 0 ? encoders[any] : null;
    }

    private static int BestIndex(IReadOnlyList<EncoderOption> encoders, Func<EncoderOption, bool> allowed)
    {
        int best = -1, bestScore = -1;
        for (int i = 0; i < encoders.Count; i++)
        {
            if (!allowed(encoders[i])) continue;
            int score = Score(encoders[i]);
            if (score > bestScore) { best = i; bestScore = score; }
        }
        return best;
    }
}
