using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace MBExtreme.Core.Recording;

/// <summary>
/// Camada desenhada pelo programa (logo do canal) que entra na live/gravação por um cano: imagem BGRA transparente do tamanho do vídeo.
/// Mudar tamanho, posição ou transparência só troca a imagem mandada pelo cano: nada reinicia.
/// </summary>
public sealed record LiveOverlayInput(string PipePath, int Width, int Height, int Fps)
{
    public string[] InputArgs() => new[]
    {
        "-f", "rawvideo", "-pix_fmt", "bgra", "-video_size", $"{Width}x{Height}",
        "-framerate", Fps.ToString(CultureInfo.InvariantCulture), "-thread_queue_size", "16",
        "-use_wallclock_as_timestamps", "1", "-i", PipePath,
    };

    /// <summary>Filtro que põe a camada por cima de <paramref name="input"/> (redimensionada para o quadro de baixo).</summary>
    public static string Chain(int inputIndex, string input, string output, int width, int height, string tag = "lo") =>
        $"[{inputIndex}:v]format=bgra,scale={width}:{height}:flags=bilinear[{tag}];{input}[{tag}]overlay=0:0:format=auto:eof_action=repeat:shortest=0{output}";
}

/// <summary>Onde o logo fica: um dos 9 pontos (0 = em cima à esquerda ... 8 = embaixo à direita) ou arrastado para um lugar livre.</summary>
public sealed record LogoPlacement(int Anchor, int WidthPx, int MarginPx, int OpacityPercent, double? CenterX = null, double? CenterY = null)
{
    /// <summary>Medidas em pixels são de um vídeo de 1920 de largura; em outros tamanhos, tudo cresce ou diminui junto.</summary>
    public const int Reference = 1920;

    public static LogoPlacement FromCorner(int corner, int percent) =>
        new(corner switch { 0 => 0, 1 => 2, 2 => 6, _ => 8 }, Math.Clamp(Reference * Math.Clamp(percent, 4, 60) / 100, 40, 1200), 24, 100);

    /// <summary>Retângulo do logo num quadro <paramref name="frameW"/> × <paramref name="frameH"/>, mantendo a proporção da imagem e dentro do quadro.</summary>
    public (int X, int Y, int W, int H) Rect(int frameW, int frameH, int imageW, int imageH)
    {
        double scale = frameW / (double)Reference;
        int w = (int)Math.Round(Math.Clamp(WidthPx, 16, 1600) * scale);
        w = Math.Clamp(w, 8, frameW);
        int h = Math.Max(2, (int)Math.Round(w * Math.Max(1, imageH) / (double)Math.Max(1, imageW)));
        if (h > frameH) { h = frameH; w = Math.Max(2, (int)Math.Round(h * Math.Max(1, imageW) / (double)Math.Max(1, imageH))); }
        int m = (int)Math.Round(Math.Clamp(MarginPx, 0, 400) * scale);
        int x, y;
        if (CenterX is { } cx && CenterY is { } cy)
        {
            x = (int)Math.Round(cx * frameW - w / 2.0);
            y = (int)Math.Round(cy * frameH - h / 2.0);
        }
        else
        {
            int col = Math.Clamp(Anchor, 0, 8) % 3, row = Math.Clamp(Anchor, 0, 8) / 3;
            x = col switch { 0 => m, 1 => (frameW - w) / 2, _ => frameW - w - m };
            y = row switch { 0 => m, 1 => (frameH - h) / 2, _ => frameH - h - m };
        }
        x = Math.Clamp(x, 0, Math.Max(0, frameW - w));
        y = Math.Clamp(y, 0, Math.Max(0, frameH - h));
        return (x, y, w, h);
    }

    public double Opacity => Math.Clamp(OpacityPercent, 5, 100) / 100.0;
}

/// <summary>
/// Cano da camada do programa para o FFmpeg: manda a última imagem <see cref="Fps"/> vezes por segundo (transparente até a primeira).
/// </summary>
public sealed class LiveOverlayCanvas : IDisposable
{
    private readonly NamedPipeServerStream _pipe;
    private readonly CancellationTokenSource _cts = new();
    private byte[] _frame;

    public LiveOverlayCanvas(int width, int height, int fps = 10)
    {
        Width = Math.Max(2, width);
        Height = Math.Max(2, height);
        Fps = Math.Clamp(fps, 2, 30);
        _frame = new byte[Width * Height * 4];
        string name = "mbx-overlay-" + Guid.NewGuid().ToString("N");
        Input = new LiveOverlayInput(@"\\.\pipe\" + name, Width, Height, Fps);
        _pipe = new NamedPipeServerStream(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, _frame.Length);
        new Thread(Run) { IsBackground = true, Name = "Camada do programa para a live" }.Start();
    }

    public int Width { get; }
    public int Height { get; }
    public int Fps { get; }
    public LiveOverlayInput Input { get; }

    /// <summary>Troca a imagem (BGRA, Width × Height). A próxima volta do cano já manda a nova.</summary>
    public void Update(byte[] bgra)
    {
        if (bgra.Length != _frame.Length) throw new ArgumentException("Tamanho da imagem diferente da camada.", nameof(bgra));
        Volatile.Write(ref _frame, bgra);
    }

    private void Run()
    {
        try
        {
            _pipe.WaitForConnectionAsync(_cts.Token).GetAwaiter().GetResult();
            var clock = Stopwatch.StartNew();
            double interval = 1000.0 / Fps, next = 0;
            while (!_cts.IsCancellationRequested)
            {
                var frame = Volatile.Read(ref _frame);
                _pipe.Write(frame, 0, frame.Length);
                next += interval;
                double wait = next - clock.Elapsed.TotalMilliseconds;
                if (wait < -interval * 3) next = clock.Elapsed.TotalMilliseconds;
                else if (wait > 1) Thread.Sleep((int)wait);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException) { }
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch (ObjectDisposedException) { }
        _pipe.Dispose();
    }

    /// <summary>Converte BGRA pré-multiplicado (o que o Windows desenha) para BGRA normal (o que o FFmpeg espera).</summary>
    public static void Unpremultiply(Span<byte> bgra)
    {
        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            int a = bgra[i + 3];
            if (a == 0 || a == 255) continue;
            bgra[i] = (byte)Math.Min(255, bgra[i] * 255 / a);
            bgra[i + 1] = (byte)Math.Min(255, bgra[i + 1] * 255 / a);
            bgra[i + 2] = (byte)Math.Min(255, bgra[i + 2] * 255 / a);
        }
    }
}
