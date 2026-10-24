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

/// <summary>Os números gravados nas configurações não podem mudar: serviços novos entram no fim.</summary>
public enum StreamService { Twitch, YouTube, Custom, Facebook }

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
        StreamService.Facebook => "rtmps://live-api-s.facebook.com:443/rtmp",
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

    public string DisplayName => Service switch { StreamService.Twitch => "Twitch", StreamService.YouTube => "YouTube", StreamService.Facebook => "Facebook", _ => "servidor próprio" };

    public override string ToString() => DisplayName;
}

public sealed record AudioSetup(string PipePath);

/// <summary>Esforço do codificador: rápido pesa menos no jogo; qualidade deixa a imagem melhor com a mesma taxa.</summary>
public enum EncoderQuality { Fast, Balanced, Quality }

public enum CameraCorner { TopLeft, TopRight, BottomLeft, BottomRight }

/// <summary>Imagem do QR do Pix (PNG pronto) mostrada num canto. <paramref name="EveryMinutes"/> 0 = sempre visível.</summary>
public sealed record PixOverlay(string ImagePath, CameraCorner Corner, int WidthPercent, int EveryMinutes, int ShowSeconds)
{
    public int ClampedPercent => Math.Clamp(WidthPercent, 8, 30);

    public static bool IsValidPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && path.IndexOfAny(new[] { '"', '|', '\'', '\r', '\n', ';', '[', ']' }) < 0;
}

/// <summary>Câmera (webcam) que aparece num canto do vídeo, como nas transmissões.</summary>
public sealed record CameraOverlay(string DeviceName, CameraCorner Corner, int WidthPercent, bool Frame, ImageFilter? Filter = null)
{
    public static bool IsValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= 120 && name.IndexOfAny(new[] { '"', '\'', '|', '[', ']', '\\', ':', '=', ',', ';', '\r', '\n' }) < 0;

    public int ClampedPercent => Math.Clamp(WidthPercent, 10, 40);
}

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

    /// <summary>Taxa de vídeo em kbps; 0 = usa BitrateMbps.</summary>
    public int VideoKbps { get; init; }

    /// <summary>Limite da taxa quando há transmissão (o padrão da plataforma escolhida).</summary>
    public int StreamMaxMbps { get; init; } = StudioCommand.MaxStreamMbps;

    public int AudioKbps { get; init; } = 160;

    public EncoderQuality Quality { get; init; } = EncoderQuality.Balanced;

    /// <summary>Até três câmeras, cada uma num canto (o DirectShow não abre a mesma câmera duas vezes).</summary>
    public IReadOnlyList<CameraOverlay> Cameras { get; init; } = Array.Empty<CameraOverlay>();

    /// <summary>Atalho para a primeira câmera (compatibilidade).</summary>
    public CameraOverlay? Camera
    {
        get => Cameras.Count > 0 ? Cameras[0] : null;
        init => Cameras = value is null ? Array.Empty<CameraOverlay>() : new[] { value };
    }

    /// <summary>Ajustes de cor e nitidez da imagem principal (tela ou janela).</summary>
    public ImageFilter? Filter { get; init; }

    /// <summary>Entrada do canal, faixa com o nome e logo, por cima de tudo.</summary>
    public IReadOnlyList<OverlayLayer> Layers { get; init; } = Array.Empty<OverlayLayer>();

    /// <summary>QR do Pix por cima do vídeo (sempre ou por alguns segundos a cada tantos minutos).</summary>
    public PixOverlay? Pix { get; init; }

    public bool AnyOutput => RecordFile is not null || Stream is not null || ReplayFolder is not null;
}
