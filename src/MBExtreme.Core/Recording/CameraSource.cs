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

    /// <summary>Modo escolhido para cada câmera do Windows (pelo nome). Preenchido pelo programa depois de perguntar os modos à câmera.</summary>
    public static System.Collections.Concurrent.ConcurrentDictionary<string, CameraMode> Modes { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Argumentos que perguntam à câmera quais tamanhos, quadros e formatos ela entrega.</summary>
    public static string[] ListModesArgs(string device) => new[] { "-hide_banner", "-f", "dshow", "-list_options", "true", "-i", "video=" + device };

    /// <summary>Argumentos de entrada do FFmpeg para esta câmera.</summary>
    public static string[] InputArgs(string source)
    {
        if (!IsStream(source))
        {
            // modo escolhido (tamanho, quadros e formato): sem ele, o Windows entrega o primeiro da lista, às vezes 1080p cru a 5 quadros por segundo
            var dshow = new List<string> { "-f", "dshow", "-rtbufsize", "64M", "-thread_queue_size", "512" };
            if (Modes.TryGetValue(source, out var mode)) dshow.AddRange(mode.InputArgs());
            dshow.AddRange(new[] { "-use_wallclock_as_timestamps", "1", "-i", "video=" + source });
            return dshow.ToArray();
        }

        var args = new List<string>();
        if (source.StartsWith("rtsp", StringComparison.OrdinalIgnoreCase)) args.AddRange(new[] { "-rtsp_transport", "tcp" }); // TCP perde menos quadros no Wi-Fi
        args.AddRange(new[] { "-fflags", "nobuffer", "-thread_queue_size", "512", "-use_wallclock_as_timestamps", "1", "-i", source });
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

    /// <summary>
    /// Vídeo ao vivo da câmera para a pré-visualização: imagens JPEG pequenas, com os mesmos filtros da gravação (<paramref name="chain"/>).
    /// Feito para atraso mínimo: sem fila na entrada, quadros a mais descartados logo no começo (antes dos filtros) e cada imagem enviada na hora.
    /// </summary>
    public static IReadOnlyList<string> LiveArgs(string source, string? chain, int fps = 20, int width = 480)
    {
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostats" };
        if (IsStream(source)) args.AddRange(new[] { "-rw_timeout", "8000000" });
        else args.AddRange(new[] { "-fflags", "nobuffer", "-flags", "low_delay", "-probesize", "32", "-analyzeduration", "0" });
        args.AddRange(InputArgs(source));
        string rate = Math.Clamp(fps, 5, 30).ToString(CultureInfo.InvariantCulture);
        string filters = $"fps={rate}," + (string.IsNullOrEmpty(chain) ? "" : chain + ",") + $"scale={Math.Clamp(width, 160, 960)}:-2:flags=fast_bilinear,format=yuvj420p";
        args.AddRange(new[] { "-an", "-vf", filters, "-f", "image2pipe", "-c:v", "mjpeg", "-q:v", "5", "-flush_packets", "1", "pipe:1" });
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
