namespace MBExtreme.Core.Recording;

public enum SourceKind
{
    /// <summary>Tela inteira (captura pela placa de vídeo).</summary>
    Monitor,

    /// <summary>Área onde a janela está na tela (captura pela placa de vídeo; ideal para jogos).</summary>
    WindowRegion,

    /// <summary>Conteúdo da janela pelo GDI: funciona com a janela coberta, mas jogos em DirectX costumam sair pretos.</summary>
    WindowGdi,
}

public sealed record PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;
}

/// <summary>O que será capturado.</summary>
public sealed record CaptureSource(
    SourceKind Kind,
    int DisplayIndex,
    PixelRect Area,
    string Description,
    string? WindowTitle = null);

public enum StreamService { Twitch, YouTube, Custom }

/// <summary>Destino da transmissão. A chave nunca deve ser gravada em log nem mostrada.</summary>
public sealed class StreamTarget
{
    public StreamTarget(StreamService service, string customUrl, string key)
    {
        Service = service;
        CustomUrl = customUrl.Trim();
        Key = key.Trim();
    }

    public StreamService Service { get; }

    public string CustomUrl { get; }

    public string Key { get; }

    public string BaseUrl => Service switch
    {
        StreamService.Twitch => "rtmp://live.twitch.tv/app",
        StreamService.YouTube => "rtmp://a.rtmp.youtube.com/live2",
        _ => CustomUrl.TrimEnd('/'),
    };

    public string FullUrl => Service == StreamService.Custom && Key.Length == 0 ? BaseUrl : BaseUrl + "/" + Key;

    /// <summary>Servidor próprio: a parte do caminho depois do primeiro segmento costuma ser a chave quando o usuário cola tudo no endereço.</summary>
    public string? PathSecret
    {
        get
        {
            if (!Uri.TryCreate(CustomUrl, UriKind.Absolute, out var uri)) return null;
            var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 1 ? string.Join("/", parts.Skip(1)) : null;
        }
    }

    public string DisplayName => Service switch { StreamService.Twitch => "Twitch", StreamService.YouTube => "YouTube", _ => "servidor próprio" };

    public override string ToString() => DisplayName;
}

public sealed record AudioSetup(string PipePath);

/// <summary>Tudo que define uma sessão do estúdio: captura, qualidade e quais saídas estão ligadas.</summary>
public sealed record StudioRequest
{
    public required CaptureSource Source { get; init; }

    public int OutputWidth { get; init; }

    public int OutputHeight { get; init; }

    public int Fps { get; init; } = 60;

    public required EncoderOption Encoder { get; init; }

    public int BitrateMbps { get; init; } = 16;

    /// <summary>Pasta de trabalho do FFmpeg; os arquivos de saída usam nomes relativos a ela.</summary>
    public required string WorkingDirectory { get; init; }

    public string? RecordFile { get; init; }

    public bool RecordMp4 { get; init; }

    public StreamTarget? Stream { get; init; }

    /// <summary>Pasta (relativa à de trabalho) onde ficam os pedaços do replay; nulo = replay desligado.</summary>
    public string? ReplayFolder { get; init; }

    public int ReplaySeconds { get; init; } = 60;

    public AudioSetup? Audio { get; init; }

    public bool AnyOutput => RecordFile is not null || Stream is not null || ReplayFolder is not null;
}
