using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using MBExtreme.Core.Recording;

namespace MBExtreme.UI.Recording;

/// <summary>
/// Vídeo ao vivo de uma câmera para a pré-visualização: um FFmpeg próprio entrega imagens JPEG pequenas, com os mesmos filtros da gravação.
/// Só funciona enquanto a câmera está livre (durante a gravação, quem a usa é o FFmpeg da gravação).
/// </summary>
internal sealed class CameraLiveFeed : IDisposable
{
    private Process? _process;
    private readonly System.Windows.Threading.Dispatcher _ui = Application.Current.Dispatcher;
    private int _generation;
    private volatile bool _pending;

    public string Signature { get; }
    public bool IsRunning => _process is { HasExited: false };

    /// <summary>Quadro novo (já no thread da tela).</summary>
    public event Action<BitmapSource>? Frame;

    /// <summary>A câmera não abriu ou parou (mensagem do FFmpeg).</summary>
    public event Action<string>? Failed;

    public CameraLiveFeed(string signature) => Signature = signature;

    public void Start(string ffmpeg, string source, string? chain)
    {
        Stop();
        int generation = ++_generation;
        // primeiro descobre o melhor modo da câmera (só na primeira vez), depois abre o vídeo, tudo fora da tela
        _ = Task.Run(() =>
        {
            if (!CameraSource.Modes.ContainsKey(source)) FfmpegRecorder.EnsureCameraMode(ffmpeg, source);
            if (generation == _generation) _ui.BeginInvoke(new Action(() => { if (generation == _generation) Launch(ffmpeg, source, chain, generation); }));
        });
    }

    private void Launch(string ffmpeg, string source, string? chain, int generation)
    {
        var info = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string a in CameraSource.LiveArgs(source, chain)) info.ArgumentList.Add(a);

        Process process;
        try { process = Process.Start(info) ?? throw new InvalidOperationException("sem processo"); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Report(generation, ex.Message);
            return;
        }

        _process = process;
        var error = new System.Text.StringBuilder();
        process.ErrorDataReceived += (_, e) => { if (e.Data is { Length: > 0 } && error.Length < 2000) error.AppendLine(e.Data); };
        process.BeginErrorReadLine();
        _ = Task.Run(() => ReadFrames(process, generation, error));
    }

    private void ReadFrames(Process process, int generation, System.Text.StringBuilder error)
    {
        var splitter = new MjpegSplitter();
        var buffer = new byte[64 * 1024];
        int count = 0;
        try
        {
            var stream = process.StandardOutput.BaseStream;
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                foreach (var jpeg in splitter.Push(buffer.AsSpan(0, read)))
                {
                    if (generation != _generation) return;
                    count++;
                    if (_pending) continue;                // a tela ainda desenha o quadro anterior: pula este
                    BitmapSource? image = Decode(jpeg);
                    if (image is null) continue;
                    _pending = true;
                    _ui.BeginInvoke(new Action(() =>
                    {
                        _pending = false;
                        if (generation == _generation) Frame?.Invoke(image);
                    }));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }

        if (generation != _generation) return;
        try { process.WaitForExit(500); } catch (InvalidOperationException) { }
        Report(generation, count == 0 ? error.ToString() : "A câmera parou de enviar imagem.");
    }

    private static BitmapSource? Decode(byte[] jpeg)
    {
        try
        {
            using var ms = new MemoryStream(jpeg);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or IOException) { return null; }
    }

    private void Report(int generation, string message) =>
        _ui.BeginInvoke(new Action(() => { if (generation == _generation) Failed?.Invoke(message); }));

    public void Stop()
    {
        _generation++;
        var process = _process;
        _process = null;
        if (process is null) return;
        try { if (!process.HasExited) process.Kill(true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        process.Dispose();
    }

    /// <summary>Para o vídeo e espera o FFmpeg fechar de verdade: só assim o Windows libera a câmera para a gravação.</summary>
    public void StopAndWait()
    {
        _generation++;
        var process = _process;
        _process = null;
        if (process is null) return;
        try
        {
            if (!process.HasExited) process.Kill(true);
            process.WaitForExit(2000);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        process.Dispose();
    }

    public void Dispose() => Stop();
}
