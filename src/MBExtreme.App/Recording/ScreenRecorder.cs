using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using MBExtreme.Core.Recording;

namespace MBExtreme.UI.Recording;

/// <summary>Grava a tela principal em AVI (Motion JPEG), na pasta Vídeos\MB Extreme. Só imagem, sem som.</summary>
internal sealed class ScreenRecorder
{
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    private Thread? _thread;
    private volatile bool _stopRequested;

    public bool IsRecording => _thread is { IsAlive: true };
    public string? CurrentFile { get; private set; }
    public DateTime StartedAt { get; private set; }
    public long CurrentBytes { get; private set; }

    /// <summary>Chamado quando a gravação termina sozinha (limite de tamanho ou erro).</summary>
    public event Action<string>? EndedByItself;

    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "MB Extreme");

    public string Start(string label, int fps, double scale)
    {
        if (IsRecording) throw new InvalidOperationException("Já existe uma gravação em andamento.");

        Directory.CreateDirectory(Folder);
        string safe = string.Concat(label.Select(c => char.IsLetterOrDigit(c) ? c : '-')).Trim('-');
        string path = Path.Combine(Folder, $"{(safe.Length == 0 ? "gravacao" : safe)}-{DateTime.Now:yyyyMMdd-HHmmss}.avi");

        int screenWidth = GetSystemMetrics(0);
        int screenHeight = GetSystemMetrics(1);
        int width = Math.Max(16, (int)(screenWidth * scale) / 2 * 2);
        int height = Math.Max(16, (int)(screenHeight * scale) / 2 * 2);

        CurrentFile = path;
        CurrentBytes = 0;
        StartedAt = DateTime.Now;
        _stopRequested = false;
        _thread = new Thread(() => Capture(path, fps, screenWidth, screenHeight, width, height))
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "MBExtreme-Recorder",
        };
        _thread.Start();
        return path;
    }

    public string? Stop()
    {
        _stopRequested = true;
        _thread?.Join(5000);
        return CurrentFile;
    }

    private void Capture(string path, int fps, int screenWidth, int screenHeight, int width, int height)
    {
        string? problem = null;
        try
        {
            var jpeg = ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == ImageFormat.Jpeg.Guid);
            using var quality = new EncoderParameters(1);
            quality.Param[0] = new EncoderParameter(Encoder.Quality, 70L);

            using var file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
            using var writer = new MjpegAviWriter(file, width, height, fps, leaveOpen: true);
            using var full = new Bitmap(screenWidth, screenHeight, PixelFormat.Format24bppRgb);
            using var fullGraphics = Graphics.FromImage(full);
            using var scaled = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using var scaledGraphics = Graphics.FromImage(scaled);
            scaledGraphics.InterpolationMode = InterpolationMode.Bilinear;
            bool needsScale = width != screenWidth || height != screenHeight;

            var clock = System.Diagnostics.Stopwatch.StartNew();
            long frame = 0;
            while (!_stopRequested)
            {
                fullGraphics.CopyFromScreen(0, 0, 0, 0, full.Size, CopyPixelOperation.SourceCopy);
                Bitmap source = full;
                if (needsScale)
                {
                    scaledGraphics.DrawImage(full, 0, 0, width, height);
                    source = scaled;
                }

                using var buffer = new MemoryStream();
                source.Save(buffer, jpeg, quality);
                writer.AddFrame(buffer.ToArray());
                CurrentBytes = writer.Length;

                if (writer.Length > MjpegAviWriter.MaxBytes)
                {
                    problem = "A gravação chegou ao limite de tamanho do arquivo e foi encerrada.";
                    break;
                }

                frame++;
                long due = frame * 1000L / fps;
                long wait = due - clock.ElapsedMilliseconds;
                if (wait > 0) Thread.Sleep((int)wait);
            }

            double seconds = Math.Max(clock.Elapsed.TotalSeconds, 0.1);
            writer.Finish(writer.FrameCount / seconds);
        }
        catch (Exception ex) when (ex is IOException or ExternalException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            problem = "A gravação foi interrompida: " + ex.Message;
        }

        if (problem is not null) EndedByItself?.Invoke(problem);
    }
}
