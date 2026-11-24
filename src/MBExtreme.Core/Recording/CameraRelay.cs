using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MBExtreme.Core.Recording;

/// <summary>Entrada de câmera vinda do relé: imagem crua YUV 4:2:0 num cano nomeado, num ritmo fixo.</summary>
public sealed record RelayInput(string PipePath, int Width, int Height, int Fps)
{
    public string[] InputArgs() => new[]
    {
        "-f", "rawvideo", "-pix_fmt", "yuv420p", "-video_size", $"{Width}x{Height}",
        "-framerate", Fps.ToString(CultureInfo.InvariantCulture), "-thread_queue_size", "64",
        "-use_wallclock_as_timestamps", "1", "-i", PipePath,
    };
}

/// <summary>Contas do relé (separadas para testar sem câmera nem Windows).</summary>
public static class CameraRelayPlan
{
    /// <summary>Teto do relé: 4K (3840×2160). Acima disso (celular em 8K) a imagem é reduzida para 4K, o máximo que as lives e gravações usam.</summary>
    public const int MaxWidth = 3840;
    public const int MaxHeight = 2160;
    public const int Fps = 30;

    /// <summary>Tamanho da imagem do relé: o tamanho nativo da câmera (até 4K), na proporção dela e em medidas pares; sem o modo, 1920×1080 com faixas.</summary>
    public static (int Width, int Height) Size(CameraMode? mode)
    {
        if (mode is null || mode.Width < 16 || mode.Height < 16) return (1920, 1080);
        // 4K deitado ou em pé: o lado maior até 3840 e o menor até 2160
        double scale = Math.Min(1.0, Math.Min(MaxWidth / (double)Math.Max(mode.Width, mode.Height), MaxHeight / (double)Math.Min(mode.Width, mode.Height)));
        int w = (int)Math.Round(mode.Width * scale);
        int h = (int)Math.Round(mode.Height * scale);
        return (Math.Max(2, w - w % 2), Math.Max(2, h - h % 2));
    }

    public static int FrameBytes(int width, int height) => width * height * 3 / 2;

    /// <summary>Abre a câmera com atraso mínimo e entrega imagens cruas de tamanho fixo pela saída do FFmpeg.</summary>
    public static IReadOnlyList<string> Args(string source, string? chain, int width, int height, int fps = Fps)
    {
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostats" };
        if (CameraSource.IsStream(source)) args.AddRange(new[] { "-rw_timeout", "8000000" });
        else args.AddRange(new[] { "-fflags", "nobuffer", "-flags", "low_delay", "-probesize", "32", "-analyzeduration", "0" });
        args.AddRange(CameraSource.InputArgs(source));
        string vf = $"fps={fps}," + (string.IsNullOrEmpty(chain) ? "" : chain + ",")
                  + $"scale={width}:{height}:force_original_aspect_ratio=decrease:flags=bilinear,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,format=yuv420p";
        args.AddRange(new[] { "-an", "-vf", vf, "-f", "rawvideo", "-pix_fmt", "yuv420p", "pipe:1" });
        return args;
    }
}

/// <summary>Converte a imagem do relé (YUV 4:2:0, BT.601 de TV) para BGRA pequena, pulando pixels: é só para mostrar na tela.</summary>
public static class Yuv420Preview
{
    public static (int Width, int Height) OutputSize(int width, int height, int step) => (width / step, height / step);

    public static void ToBgra(ReadOnlySpan<byte> yuv, int width, int height, int step, Span<byte> bgra)
    {
        var (ow, oh) = OutputSize(width, height, step);
        int ySize = width * height, cw = width / 2;
        var u = yuv.Slice(ySize, ySize / 4);
        var v = yuv.Slice(ySize + ySize / 4, ySize / 4);
        int o = 0;
        for (int row = 0; row < oh; row++)
        {
            int sy = row * step;
            int yRow = sy * width, cRow = (sy / 2) * cw;
            for (int col = 0; col < ow; col++)
            {
                int sx = col * step;
                int c = yuv[yRow + sx] - 16;
                int d = u[cRow + sx / 2] - 128;
                int e = v[cRow + sx / 2] - 128;
                int y1 = 298 * c + 128;
                bgra[o] = Clamp((y1 + 516 * d) >> 8);
                bgra[o + 1] = Clamp((y1 - 100 * d - 208 * e) >> 8);
                bgra[o + 2] = Clamp((y1 + 409 * e) >> 8);
                bgra[o + 3] = 255;
                o += 4;
            }
        }
    }

    private static byte Clamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
}

/// <summary>
/// Relé da câmera, como o OBS faz: um FFmpeg leve abre a câmera uma vez só e entrega imagens cruas; o programa guarda sempre a última.
/// A prévia mostra essa imagem e a gravação/live recebe a última imagem num ritmo fixo (<see cref="OpenSink"/>):
/// a live nunca fica esperando a câmera (antes, cada atraso da câmera travava o vídeo inteiro) e a câmera não precisa ser fechada e reaberta para gravar.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CameraRelay : IDisposable
{
    private const int Buffers = 4;
    private readonly byte[][] _frames;
    private readonly List<CameraSink> _sinks = new();
    private readonly object _gate = new();
    private Process? _process;
    private volatile int _latest = -1;
    private long _sequence;
    private volatile bool _disposed;

    public CameraRelay(string device, string? chain, CameraMode? mode)
    {
        Device = device;
        Chain = chain;
        (Width, Height) = CameraRelayPlan.Size(mode);
        FrameBytes = CameraRelayPlan.FrameBytes(Width, Height);
        _frames = Enumerable.Range(0, Buffers).Select(_ => new byte[FrameBytes]).ToArray();
    }

    public string Device { get; }
    public string? Chain { get; }
    public int Width { get; }
    public int Height { get; }
    public int FrameBytes { get; }

    /// <summary>Número da última imagem (0 = nenhuma ainda).</summary>
    public long Sequence => Interlocked.Read(ref _sequence);

    public bool HasFrames => Sequence > 0;

    public bool IsRunning => _process is { HasExited: false };

    public string? LastError { get; private set; }

    public bool HasSinks
    {
        get { lock (_gate) return _sinks.Count > 0; }
    }

    /// <summary>A câmera parou ou não abriu (mensagem do FFmpeg). Chamado em outra thread.</summary>
    public event Action<string>? Failed;

    /// <summary>A última imagem (não copie para guardar: o espaço é reaproveitado algumas imagens depois).</summary>
    public byte[]? LatestFrame => _latest >= 0 ? _frames[_latest] : null;

    public void Start(string ffmpeg)
    {
        var info = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string a in CameraRelayPlan.Args(Device, Chain, Width, Height)) info.ArgumentList.Add(a);
        var process = Process.Start(info) ?? throw new InvalidOperationException("Não foi possível abrir a câmera.");
        _process = process;
        // a câmera precisa chegar sempre na hora: um pouco acima do normal, como a gravação
        try { process.PriorityClass = ProcessPriorityClass.AboveNormal; }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        var error = new System.Text.StringBuilder();
        process.ErrorDataReceived += (_, e) => { if (e.Data is { Length: > 0 } && error.Length < 2000) error.AppendLine(e.Data); };
        process.BeginErrorReadLine();
        var thread = new Thread(() => Read(process, error)) { IsBackground = true, Name = "Relé da câmera", Priority = ThreadPriority.AboveNormal };
        thread.Start();
    }

    private void Read(Process process, System.Text.StringBuilder error)
    {
        try
        {
            var stream = process.StandardOutput.BaseStream;
            int slot = 0;
            while (!_disposed)
            {
                var buffer = _frames[slot];
                int filled = 0;
                while (filled < FrameBytes)
                {
                    int read = stream.Read(buffer, filled, FrameBytes - filled);
                    if (read <= 0) goto ended;
                    filled += read;
                }
                _latest = slot;
                Interlocked.Increment(ref _sequence);
                slot = (slot + 1) % Buffers;
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }

    ended:
        if (_disposed) return;
        try { process.WaitForExit(500); } catch (InvalidOperationException) { }
        LastError = Sequence == 0 ? error.ToString() : "A câmera parou de enviar imagem.";
        Failed?.Invoke(LastError);
    }

    /// <summary>
    /// Abre um cano para o FFmpeg da gravação/live e manda nele a última imagem, <paramref name="fps"/> vezes por segundo, sem esperar a câmera.
    /// Enquanto não houver imagem, manda preto (o FFmpeg nunca fica parado esperando).
    /// </summary>
    public (RelayInput Input, IDisposable Sink) OpenSink(int fps)
    {
        fps = Math.Clamp(fps, 10, 60);
        var sink = new CameraSink(this, fps);
        lock (_gate) _sinks.Add(sink);
        return (new RelayInput(sink.Path, Width, Height, fps), sink);
    }

    private void Remove(CameraSink sink)
    {
        lock (_gate) _sinks.Remove(sink);
    }

    public void Dispose()
    {
        _disposed = true;
        List<CameraSink> sinks;
        lock (_gate) sinks = _sinks.ToList();
        foreach (var s in sinks) s.Dispose();
        var process = _process;
        _process = null;
        if (process is null) return;
        try { if (!process.HasExited) process.Kill(true); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        process.Dispose();
    }

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint period);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint period);

    private sealed class CameraSink : IDisposable
    {
        private readonly CameraRelay _relay;
        private readonly int _fps;
        private readonly NamedPipeServerStream _pipe;
        private readonly CancellationTokenSource _cts = new();

        public CameraSink(CameraRelay relay, int fps)
        {
            _relay = relay;
            _fps = fps;
            string name = "mbx-camfeed-" + Guid.NewGuid().ToString("N");
            Path = @"\\.\pipe\" + name;
            _pipe = new NamedPipeServerStream(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, relay.FrameBytes * 2);
            var thread = new Thread(Run) { IsBackground = true, Name = "Câmera para a gravação", Priority = ThreadPriority.AboveNormal };
            thread.Start();
        }

        public string Path { get; }

        private void Run()
        {
            var black = new byte[_relay.FrameBytes];
            Array.Fill(black, (byte)16, 0, _relay.Width * _relay.Height);
            Array.Fill(black, (byte)128, _relay.Width * _relay.Height, black.Length - _relay.Width * _relay.Height);
            bool fineTimer = false;
            try
            {
                _pipe.WaitForConnectionAsync(_cts.Token).GetAwaiter().GetResult();
                fineTimer = timeBeginPeriod(1) == 0;   // relógio de 1 ms: o ritmo fica estável
                var clock = Stopwatch.StartNew();
                double interval = 1000.0 / _fps;
                double next = 0;
                while (!_cts.IsCancellationRequested)
                {
                    var frame = _relay.LatestFrame ?? black;
                    _pipe.Write(frame, 0, frame.Length);
                    next += interval;
                    double wait = next - clock.Elapsed.TotalMilliseconds;
                    if (wait < -interval * 3) next = clock.Elapsed.TotalMilliseconds;   // ficou muito para trás (PC travou): recomeça o ritmo
                    else if (wait > 1) Thread.Sleep((int)wait);
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException) { }
            finally
            {
                if (fineTimer) timeEndPeriod(1);
            }
        }

        public void Dispose()
        {
            _relay.Remove(this);
            try { _cts.Cancel(); } catch (ObjectDisposedException) { }
            _pipe.Dispose();
        }
    }
}

/// <summary>
/// As câmeras abertas pelo relé, compartilhadas entre a prévia (tela do estúdio) e a gravação/live.
/// Cada um que usa avisa (<see cref="Acquire"/>) e solta (<see cref="Release"/>); sem ninguém usando, a câmera fecha.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CameraHub
{
    private sealed class Entry(CameraRelay relay)
    {
        public CameraRelay Relay { get; set; } = relay;
        public HashSet<object> Owners { get; } = new();
    }

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    /// <summary>
    /// Abre (ou reaproveita) o relé da câmera. Com outro filtro, reabre, menos se a gravação estiver usando (aí o filtro novo vale na próxima).
    /// </summary>
    public CameraRelay Acquire(string ffmpeg, string device, string? chain, object owner)
    {
        CameraRelay? old = null;
        CameraRelay relay;
        lock (_gate)
        {
            if (_entries.TryGetValue(device, out var entry))
            {
                bool dead = !entry.Relay.IsRunning;
                bool otherChain = entry.Relay.Chain != chain && !entry.Relay.HasSinks;
                if (dead || otherChain)
                {
                    old = entry.Relay;
                    entry.Relay = Create(ffmpeg, device, chain);
                }
                entry.Owners.Add(owner);
                relay = entry.Relay;
            }
            else
            {
                relay = Create(ffmpeg, device, chain);
                var created = new Entry(relay);
                created.Owners.Add(owner);
                _entries[device] = created;
            }
        }
        old?.Dispose();
        return relay;
    }

    private static CameraRelay Create(string ffmpeg, string device, string? chain)
    {
        CameraMode? mode;
        if (CameraSource.IsStream(device)) mode = FfmpegRecorder.ProbeStreamMode(ffmpeg, device);   // celular/câmera IP: lê o tamanho real do vídeo (até 4K)
        else if (!CameraSource.Modes.TryGetValue(device, out mode)) mode = FfmpegRecorder.EnsureCameraMode(ffmpeg, device, CameraSource.PreferredHeight);   // ~1 s na primeira vez: chame fora da tela
        var relay = new CameraRelay(device, chain, mode);
        relay.Start(ffmpeg);
        return relay;
    }

    public CameraRelay? Find(string device)
    {
        lock (_gate) return _entries.TryGetValue(device, out var entry) ? entry.Relay : null;
    }

    public void Release(string device, object owner)
    {
        CameraRelay? closing = null;
        lock (_gate)
        {
            if (!_entries.TryGetValue(device, out var entry)) return;
            entry.Owners.Remove(owner);
            if (entry.Owners.Count == 0)
            {
                closing = entry.Relay;
                _entries.Remove(device);
            }
        }
        closing?.Dispose();
    }

    public void ReleaseAll(object owner)
    {
        List<string> devices;
        lock (_gate) devices = _entries.Where(e => e.Value.Owners.Contains(owner)).Select(e => e.Key).ToList();
        foreach (string device in devices) Release(device, owner);
    }

    /// <summary>Fecha todas as câmeras (programa fechando).</summary>
    public void CloseAll()
    {
        List<CameraRelay> all;
        lock (_gate)
        {
            all = _entries.Values.Select(e => e.Relay).ToList();
            _entries.Clear();
        }
        foreach (var relay in all) relay.Dispose();
    }

    public IReadOnlyList<string> Owned(object owner)
    {
        lock (_gate) return _entries.Where(e => e.Value.Owners.Contains(owner)).Select(e => e.Key).ToList();
    }
}
