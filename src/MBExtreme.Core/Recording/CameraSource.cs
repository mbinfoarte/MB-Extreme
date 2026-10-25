using System.Globalization;

namespace MBExtreme.Core.Recording;

/// <summary>
/// De onde vem uma câmera: um dispositivo do Windows (webcam, câmera profissional ou câmera virtual) ou o endereço de um
/// vídeo na rede (celular com aplicativo de câmera por Wi-Fi, câmera IP): rtsp://, http:// ou https://.
/// </summary>
public static class CameraSource
{
    private static readonly string[] Schemes = { "rtsp://", "rtsps://", "http://", "https://", "rtmp://", "udp://" };

    public static bool IsStream(string? source) =>
        source is not null && Schemes.Any(s => source.StartsWith(s, StringComparison.OrdinalIgnoreCase));

    /// <summary>Endereço aceito: sem espaços, aspas ou caracteres que quebrariam a linha de comando do FFmpeg.</summary>
    public static bool IsValidStream(string? source)
    {
        if (!IsStream(source) || source!.Length > 300) return false;
        if (source.IndexOfAny(new[] { '"', '\'', '|', '[', ']', '\\', ';', ' ', '\r', '\n', '\t', '`' }) >= 0) return false;
        return Uri.TryCreate(source, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host);
    }

    /// <summary>Texto para mostrar ao usuário: nome do dispositivo, ou o endereço sem usuário e senha.</summary>
    public static string Display(string source)
    {
        if (!IsStream(source) || !Uri.TryCreate(source, UriKind.Absolute, out var uri)) return source;
        return $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? "" : ":" + uri.Port.ToString(CultureInfo.InvariantCulture))}{uri.AbsolutePath}";
    }

    /// <summary>Argumentos de entrada do FFmpeg para esta câmera.</summary>
    public static string[] InputArgs(string source)
    {
        if (!IsStream(source))
            return new[] { "-f", "dshow", "-rtbufsize", "128M", "-use_wallclock_as_timestamps", "1", "-i", "video=" + source };

        var args = new List<string>();
        if (source.StartsWith("rtsp", StringComparison.OrdinalIgnoreCase)) args.AddRange(new[] { "-rtsp_transport", "tcp" }); // TCP perde menos quadros no Wi-Fi
        args.AddRange(new[] { "-fflags", "nobuffer", "-use_wallclock_as_timestamps", "1", "-i", source });
        return args.ToArray();
    }

    /// <summary>Uma imagem só da câmera, para o usuário ver se está funcionando.</summary>
    public static IReadOnlyList<string> StillArgs(string source, string outputPng, int width = 640)
    {
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostats", "-y" };
        if (IsStream(source)) args.AddRange(new[] { "-rw_timeout", "6000000" }); // 6 s sem resposta desiste
        args.AddRange(InputArgs(source));
        args.AddRange(new[] { "-frames:v", "1", "-vf", $"scale={Math.Clamp(width, 160, 1280)}:-2", "-f", "image2", outputPng });
        return args;
    }

    /// <summary>Explica em português o motivo mais provável de a câmera não abrir, a partir do que o FFmpeg disse.</summary>
    public static string Explain(string source, string ffmpegMessage)
    {
        string m = ffmpegMessage.ToLowerInvariant();
        if (IsStream(source))
        {
            if (m.Contains("401") || m.Contains("unauthorized")) return "O aplicativo da câmera pediu usuário e senha. Coloque no endereço assim: rtsp://usuario:senha@192.168.0.10:8080/...";
            if (m.Contains("timed out") || m.Contains("timeout") || m.Contains("refused") || m.Contains("unreachable")) return "Não consegui conectar. Confira se o celular e o PC estão na mesma rede Wi-Fi, se o aplicativo da câmera está aberto e se o endereço está certo.";
            return "O endereço não entregou vídeo. Abra o mesmo endereço no navegador ou no aplicativo do celular para conferir.";
        }
        if (m.Contains("could not run graph") || m.Contains("i/o error") || m.Contains("busy") || m.Contains("in use"))
            return "A câmera está em uso por outro programa (Zoom, Teams, navegador, OBS...). Feche-os e tente de novo.";
        if (m.Contains("permission") || m.Contains("access is denied") || m.Contains("could not find"))
            return "O Windows não liberou a câmera. Em Configurações > Privacidade > Câmera, permita o acesso de aplicativos para computador.";
        return "A câmera não entregou imagem. Veja se ela está conectada, se outro programa a usa e tente \"Procurar câmeras\" de novo.";
    }
}
