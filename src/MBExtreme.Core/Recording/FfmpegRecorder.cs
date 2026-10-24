using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using MBExtreme.Core.Windows;

namespace MBExtreme.Core.Recording;

/// <summary>Grava a tela com o FFmpeg (se instalado): captura e codificação na placa de vídeo, em 1080p, 1440p ou 4K, a 30 ou 60 fps.</summary>
[SupportedOSPlatform("windows")]
public sealed class FfmpegRecorder
{
    private Process? _process;

    public bool IsRecording => _process is { HasExited: false };

    /// <summary>O FFmpeg terminou sozinho depois de iniciar (disco cheio, driver reiniciado...).</summary>
    public bool EndedUnexpectedly => _process is { HasExited: true };
    public string? CurrentFile { get; private set; }

    public static string? Locate()
    {
        var candidates = new List<string>();
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (path is not null) candidates.AddRange(path.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(d => Path.Combine(d.Trim('"'), "ffmpeg.exe")));

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        candidates.Add(Path.Combine(local, @"Microsoft\WinGet\Links\ffmpeg.exe"));
        candidates.Add(@"C:\ffmpeg\bin\ffmpeg.exe");
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"ffmpeg\bin\ffmpeg.exe"));

        string packages = Path.Combine(local, @"Microsoft\WinGet\Packages");
        try
        {
            if (Directory.Exists(packages))
                candidates.AddRange(Directory.GetDirectories(packages, "*FFmpeg*")
                    .SelectMany(d => Directory.GetFiles(d, "ffmpeg.exe", SearchOption.AllDirectories)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Descobre quais codificadores de GPU realmente funcionam neste PC, codificando alguns quadros de teste de cada um.</summary>
    public static IReadOnlyList<EncoderOption> ProbeEncoders(string ffmpeg)
    {
        var listed = Shell.Run(ffmpeg, "-hide_banner", "-encoders");
        var candidates = FfmpegCommand.ParseEncoders(listed.Output);
        var working = new List<EncoderOption>();
        foreach (var option in candidates)
        {
            var test = Shell.Run(ffmpeg, "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "nullsrc=s=1280x720:r=30",
                "-frames:v", "8", "-c:v", option.FfmpegName, "-f", "null", "-");
            if (test.ExitCode == 0) working.Add(option);
        }

        return working;
    }

    /// <summary>Nomes das câmeras (dispositivos de vídeo do DirectShow) que o FFmpeg enxerga.</summary>
    public static IReadOnlyList<string> ListCameras(string ffmpeg)
    {
        var listed = Shell.Run(ffmpeg, "-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy");
        return ParseCameras(listed.Error + "\n" + listed.Output);
    }

    public static IReadOnlyList<string> ParseCameras(string text) =>
        System.Text.RegularExpressions.Regex.Matches(text, "\"([^\"]+)\"\\s+\\(video\\)")
            .Select(m => m.Groups[1].Value)
            .Where(CameraOverlay.IsValidName)
            .Distinct()
            .ToList();

    public string Start(string ffmpeg, CaptureRequest request)
    {
        if (IsRecording) throw new InvalidOperationException("Já existe uma gravação em andamento.");
        Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath)!);

        var info = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardError = true,
        };
        foreach (string a in FfmpegCommand.Build(request)) info.ArgumentList.Add(a);

        _process = Process.Start(info) ?? throw new InvalidOperationException("Não foi possível iniciar o FFmpeg.");
        _process.ErrorDataReceived += (_, e) => { if (e.Data is { Length: > 0 }) LastMessage = e.Data; };
        _process.BeginErrorReadLine();
        CurrentFile = request.OutputPath;

        // Se o FFmpeg fechar logo, a configuração não funciona nesta placa/versão.
        if (_process.WaitForExit(1500))
        {
            _process.WaitForExit(); // espera terminar de ler a mensagem de erro
            string message = LastMessage ?? "configuração não suportada nesta placa de vídeo.";
            _process.Dispose();
            _process = null;
            throw new InvalidOperationException("O FFmpeg encerrou ao iniciar: " + message);
        }

        return request.OutputPath;
    }

    public string? LastMessage { get; private set; }

    /// <summary>Encerra com "q" (o FFmpeg fecha o arquivo direito); se não responder, finaliza o processo.</summary>
    public string? Stop()
    {
        var process = _process;
        if (process is null) return CurrentFile;
        try
        {
            if (!process.HasExited)
            {
                try { process.StandardInput.WriteLine("q"); process.StandardInput.Flush(); }
                catch (IOException) { }
                if (!process.WaitForExit(10000)) process.Kill(true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
        finally
        {
            process.Dispose();
            _process = null;
        }

        return CurrentFile;
    }
}
