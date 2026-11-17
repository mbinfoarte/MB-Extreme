using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MBExtreme.UI.Recording;

/// <summary>
/// Vídeo cru (BGRA, sem compressão) que um FFmpeg entrega pela saída, desenhado numa imagem que só é atualizada (não recriada):
/// nítido como a tela de verdade e leve, porque não há JPEG para comprimir e descomprimir a cada quadro.
/// </summary>
internal sealed class RawFrameFeed : IDisposable
{
    private readonly System.Windows.Threading.Dispatcher _ui = Application.Current.Dispatcher;
    private readonly int _width, _height, _frameBytes;
    private readonly byte[][] _buffers = { Array.Empty<byte>(), Array.Empty<byte>() };
    private Process? _process;
    private volatile int _ready = -1;
    private volatile bool _pending;
    private volatile bool _stopped;

    public RawFrameFeed(string signature, int width, int height)
    {
        Signature = signature;
        _width = width;
        _height = height;
        _frameBytes = width * height * 4;
        _buffers[0] = new byte[_frameBytes];
        _buffers[1] = new byte[_frameBytes];
        Bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
    }

    public string Signature { get; }

    /// <summary>A imagem que a tela mostra; cada quadro novo é escrito nela.</summary>
    public WriteableBitmap Bitmap { get; }

    public event Action? Frame;

    public event Action<string>? Failed;

    public void Start(string ffmpeg, IReadOnlyList<string> args)
    {
        var info = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string a in args) info.ArgumentList.Add(a);
        Process process;
        try { process = Process.Start(info) ?? throw new InvalidOperationException("sem processo"); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _ui.BeginInvoke(new Action(() => Failed?.Invoke(ex.Message)));
            return;
        }
        _process = process;
        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; }   // a prévia nunca disputa com o jogo nem com a live
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        var error = new System.Text.StringBuilder();
        process.ErrorDataReceived += (_, e) => { if (e.Data is { Length: > 0 } && error.Length < 2000) error.AppendLine(e.Data); };
        process.BeginErrorReadLine();
        new Thread(() => Read(process, error)) { IsBackground = true, Name = "Prévia da tela" }.Start();
    }

    private void Read(Process process, System.Text.StringBuilder error)
    {
        int slot = 0, frames = 0;
        try
        {
            var stream = process.StandardOutput.BaseStream;
            while (!_stopped)
            {
                var buffer = _buffers[slot];
                int filled = 0;
                while (filled < _frameBytes)
                {
                    int read = stream.Read(buffer, filled, _frameBytes - filled);
                    if (read <= 0) goto ended;
                    filled += read;
                }
                frames++;
                if (_pending) continue;   // a tela ainda não desenhou o anterior: este é descartado (sem fila, sem atraso)
                _ready = slot;
                slot ^= 1;
                _pending = true;
                _ui.BeginInvoke(new Action(Draw), System.Windows.Threading.DispatcherPriority.Render);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }

    ended:
        if (_stopped) return;
        string message = frames == 0 ? error.ToString() : "A prévia parou.";
        _ui.BeginInvoke(new Action(() => { if (!_stopped) Failed?.Invoke(message); }));
    }

    private void Draw()
    {
        int ready = _ready;
        if (!_stopped && ready >= 0)
        {
            Bitmap.WritePixels(new Int32Rect(0, 0, _width, _height), _buffers[ready], _width * 4, 0);
            Frame?.Invoke();
        }
        _pending = false;
    }

    public void Dispose()
    {
        _stopped = true;
        var process = _process;
        _process = null;
        if (process is null) return;
        try { if (!process.HasExited) process.Kill(true); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        process.Dispose();
    }
}
