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
        CameraSource.IsStream(name) ? CameraSource.IsValidStream(name) : IsValidDeviceName(name);

    private static bool IsValidDeviceName(string? name) =>
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

    /// <summary>Vídeo vertical (Shorts, Reels, TikTok): montagem em uma ou duas partes. Quando definido, OutputWidth/OutputHeight são o tamanho do quadro vertical.</summary>
    public VerticalLayout? Vertical { get; init; }

    /// <summary>
    /// Cópia vertical gravada ao mesmo tempo que o vídeo normal (o mesmo jogo, recortado e montado em 9:16 em tempo real).
    /// Precisa de <see cref="VerticalFile"/>. Não vale junto com <see cref="Vertical"/>.
    /// </summary>
    public VerticalLayout? VerticalCopy { get; init; }

    /// <summary>Arquivo (relativo à pasta de trabalho) da cópia vertical.</summary>
    public string? VerticalFile { get; init; }

    /// <summary>Live vertical ao mesmo tempo que a horizontal (Instagram, TikTok, YouTube...): a cópia vertical também vai para este servidor.</summary>
    public StreamTarget? VerticalStream { get; init; }

    /// <summary>Taxa de vídeo da live vertical (kbps).</summary>
    public int VerticalStreamKbps { get; init; } = 4000;

    /// <summary>Quadros por segundo da cópia vertical (0 = os mesmos do vídeo principal).</summary>
    public int VerticalFps { get; init; }

    /// <summary>Codificador próprio da gravação vertical (ex.: HEVC ou AV1 para arquivos menores); null = o do vídeo principal.</summary>
    public EncoderOption? VerticalEncoder { get; init; }

    /// <summary>Taxa da gravação vertical em Mbps (0 = automática pelo tamanho).</summary>
    public int VerticalBitrateMbps { get; init; }

    /// <summary>Qualidade constante da gravação vertical (0 = desligada; 1 alta, 2 muito alta, 3 máxima): o arquivo gasta o que precisar para manter a imagem.</summary>
    public int VerticalQualityLevel { get; init; }

    /// <summary>Esforço do codificador da gravação vertical (null = o do vídeo principal).</summary>
    public EncoderQuality? VerticalEffort { get; init; }

    /// <summary>Qualidade constante do vídeo principal (0 = taxa normal). Só vale sem transmissão.</summary>
    public int QualityLevel { get; init; }

    /// <summary>
    /// Cópia pequena de cada câmera (na ordem de <see cref="Cameras"/>), mandada pelo próprio FFmpeg da gravação: com a câmera ocupada pela gravação,
    /// é assim que os quadros das câmeras na tela continuam se mexendo. Nulo (ou lista menor) = sem cópia daquela câmera.
    /// </summary>
    public IReadOnlyList<string?> CameraPreviewPipes { get; init; } = Array.Empty<string?>();

    /// <summary>
    /// Câmeras que chegam pelo relé (nome da câmera → cano com a imagem crua num ritmo fixo), no lugar de abrir a câmera direto.
    /// Assim a live não espera a câmera: cada atraso dela não trava mais o vídeo inteiro.
    /// </summary>
    public IReadOnlyDictionary<string, RelayInput> CameraRelays { get; init; } = new Dictionary<string, RelayInput>();

    /// <summary>Ajustes de live que o OBS usa no codificador da placa (taxa constante, quadros B, quantização adaptativa). Desligado sozinho se a placa recusar.</summary>
    public bool EncoderTuning { get; init; } = true;

    /// <summary>Pede as cópias pequenas das câmeras (a sessão cria os canais e preenche <see cref="CameraPreviewPipes"/>).</summary>
    public bool CameraPreviews { get; init; }

    /// <summary>Legendas automáticas queimadas no vídeo em tempo real (vertical ou não).</summary>
    public CaptionOverlay? Captions { get; init; }

    /// <summary>Ensaio: monta tudo (cenas, câmeras, sobreposições, codificador) e mostra a imagem final, sem gravar nem transmitir.</summary>
    public bool Rehearsal { get; init; }

    public bool AnyOutput => RecordFile is not null || Stream is not null || ReplayFolder is not null || Rehearsal;

    /// <summary>
    /// Cenas montadas ao mesmo tempo (troca sem reiniciar). Cada cena aponta para uma captura de <see cref="SceneCaptures"/>,
    /// para câmeras de <see cref="Cameras"/>, para o <see cref="Pix"/> e para camadas de <see cref="Layers"/>.
    /// </summary>
    public IReadOnlyList<SceneLayout> Scenes { get; init; } = Array.Empty<SceneLayout>();

    /// <summary>Capturas usadas pelas cenas (telas e janelas, sem repetir).</summary>
    public IReadOnlyList<CaptureSource> SceneCaptures { get; init; } = Array.Empty<CaptureSource>();

    /// <summary>Cena que começa no ar.</summary>
    public int ActiveScene { get; init; }

    /// <summary>Esmaecer exige uma etapa a mais (mistura de duas imagens); cortar é instantâneo.</summary>
    public SceneTransition Transition { get; init; }

    /// <summary>Gera também uma imagem pequena do que está no ar (para a tela "No ar" do programa).</summary>
    public bool ProgramView { get; init; }

    /// <summary>Cano nomeado da imagem "No ar" (preenchido pela sessão).</summary>
    public string? ProgramPipe { get; init; }

    /// <summary>As cenas trocam sem reiniciar: duas ou mais cenas e sem vídeo vertical (o vertical tem a sua própria montagem).</summary>
    public bool LiveScenes => Scenes.Count > 1 && Vertical is null && VerticalCopy is null;
}

public enum SceneTransition { Cut, Fade }

/// <summary>Uma cena para a troca ao vivo: qual captura é o fundo e o que vai por cima (índices das listas do pedido).</summary>
public sealed record SceneLayout(string Name, int Capture, IReadOnlyList<int> Cameras, bool Pix, IReadOnlyList<int> Layers);
