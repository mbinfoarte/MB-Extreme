using System.Text.RegularExpressions;

namespace MBExtreme.Core.Recording;

public enum VideoCodec { H264, Hevc, Av1 }

/// <summary>Software = processador (x264), usado só quando a placa de vídeo não tem codificador.</summary>
public enum EncoderKind { Nvenc, Amf, Qsv, Software }

/// <summary>Um codificador de vídeo da placa de vídeo, como aparece no FFmpeg (ex.: h264_nvenc).</summary>
public sealed record EncoderOption(VideoCodec Codec, EncoderKind Kind, string FfmpegName)
{
    public string CodecText => Codec switch { VideoCodec.H264 => "H.264", VideoCodec.Hevc => "H.265 (HEVC)", _ => "AV1" };

    public string KindText => Kind switch { EncoderKind.Nvenc => "NVIDIA NVENC", EncoderKind.Amf => "AMD AMF", EncoderKind.Software => "Processador (x264)", _ => "Intel Quick Sync" };

    public string Title => $"{CodecText} · {KindText}";
}

public sealed record CaptureRequest(
    int Width, int Height, int NativeWidth, int NativeHeight, int Fps, EncoderOption Encoder, int BitrateMbps, string OutputPath, int DisplayIndex = 0);

/// <summary>Monta os argumentos do FFmpeg para gravar a tela com captura e codificação aceleradas pela placa de vídeo.</summary>
public static class FfmpegCommand
{
    private static readonly (string Name, VideoCodec Codec, EncoderKind Kind)[] Known =
    {
        ("h264_nvenc", VideoCodec.H264, EncoderKind.Nvenc),
        ("hevc_nvenc", VideoCodec.Hevc, EncoderKind.Nvenc),
        ("av1_nvenc", VideoCodec.Av1, EncoderKind.Nvenc),
        ("h264_amf", VideoCodec.H264, EncoderKind.Amf),
        ("hevc_amf", VideoCodec.Hevc, EncoderKind.Amf),
        ("av1_amf", VideoCodec.Av1, EncoderKind.Amf),
        ("h264_qsv", VideoCodec.H264, EncoderKind.Qsv),
        ("hevc_qsv", VideoCodec.Hevc, EncoderKind.Qsv),
        ("av1_qsv", VideoCodec.Av1, EncoderKind.Qsv),
    };

    /// <summary>Codificadores de GPU que a compilação do FFmpeg conhece (saída de "ffmpeg -encoders"). Ainda falta testar quais funcionam na sua placa.</summary>
    public static IReadOnlyList<EncoderOption> ParseEncoders(string ffmpegEncodersOutput) =>
        Known.Where(k => Regex.IsMatch(ffmpegEncodersOutput, @"\b" + Regex.Escape(k.Name) + @"\b"))
             .Select(k => new EncoderOption(k.Codec, k.Kind, k.Name))
             .ToList();

    public static IReadOnlyList<EncoderOption> AllKnown() => Known.Select(k => new EncoderOption(k.Codec, k.Kind, k.Name)).ToList();

    /// <summary>Taxa sugerida (Mbps) para gravação de jogo, conforme resolução e quadros por segundo.</summary>
    public static int DefaultBitrateMbps(int width, int height, int fps)
    {
        long pixels = (long)width * height;
        double mbps = pixels switch
        {
            <= 1_300_000 => 8,
            <= 2_200_000 => 16,
            <= 4_000_000 => 30,
            _ => 50,
        };
        if (fps >= 50) mbps *= 1.5;
        return (int)Math.Round(mbps);
    }

    public static bool IsNativeSize(CaptureRequest r) => r.Width == r.NativeWidth && r.Height == r.NativeHeight;

    /// <summary>
    /// Captura pela GPU (ddagrab). Se a resolução pedida é a da tela e o codificador é NVENC/AMF, os quadros ficam na GPU do começo ao fim.
    /// Caso contrário (outra resolução ou Quick Sync), os quadros descem para a memória, são redimensionados e voltam; funciona, mas pesa mais.
    /// </summary>
    public static IReadOnlyList<string> Build(CaptureRequest r)
    {
        bool direct = IsNativeSize(r) && r.Encoder.Kind != EncoderKind.Qsv;
        string source = $"ddagrab=output_idx={r.DisplayIndex}:framerate={r.Fps}:draw_mouse=1";
        if (!direct)
            source += $",hwdownload,format=bgra,scale={r.Width}:{r.Height}:flags=bilinear,format=nv12";

        int rate = Math.Max(2, r.BitrateMbps);
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "warning", "-y",
            "-f", "lavfi", "-i", source,
            "-c:v", r.Encoder.FfmpegName,
            "-b:v", rate + "M", "-maxrate", (int)(rate * 1.5) + "M", "-bufsize", rate * 2 + "M",
            "-g", (r.Fps * 2).ToString(),
            r.OutputPath,
        };
        return args;
    }

    public static string ExtensionFor(bool mp4) => mp4 ? ".mp4" : ".mkv";
}
