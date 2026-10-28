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

    /// <summary>O que vem com o programa primeiro; depois um FFmpeg que já exista no PC.</summary>
    public static string? Locate()
    {
        var candidates = new List<string>(FfmpegProvision.BundledCandidates());
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

    /// <summary>
    /// Descobre quais codificadores da placa de vídeo realmente funcionam neste PC, codificando alguns quadros de teste de cada um (todos ao mesmo tempo).
    /// O do processador (x264) entra no fim da lista, só como reserva.
    /// </summary>
    public static IReadOnlyList<EncoderOption> ProbeEncoders(string ffmpeg)
    {
        var listed = Shell.Run(ffmpeg, "-hide_banner", "-encoders");
        var candidates = FfmpegCommand.ParseEncoders(listed.Output);
        var tests = candidates.Select(option => Task.Run(() => TestEncoder(ffmpeg, option.FfmpegName) ? option : null)).ToArray();
        Task.WaitAll(tests);
        var working = tests.Select(t => t.Result).OfType<EncoderOption>().ToList();
        if (FfmpegCommand.HasSoftwareEncoder(listed.Output)) working.Add(FfmpegCommand.Software);
        return working;
    }

    /// <summary>Codifica 10 quadros em NV12 (o formato que a placa recebe na gravação); desiste em 20 s se o driver travar.</summary>
    private static bool TestEncoder(string ffmpeg, string name)
    {
        var info = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (string a in FfmpegCommand.ProbeArgs(name)) info.ArgumentList.Add(a);
        try
        {
            using var process = Process.Start(info);
            if (process is null) return false;
            process.ErrorDataReceived += (_, _) => { };
            process.OutputDataReceived += (_, _) => { };
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();
            if (!process.WaitForExit(20000))
            {
                try { process.Kill(true); } catch (InvalidOperationException) { }
                return false;
            }
            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { return false; }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyList<CameraMode>> KnownModes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Pergunta à câmera (uma vez por nome) quais modos ela tem e guarda o melhor em <see cref="CameraSource.Modes"/>.
    /// A câmera precisa estar livre; se não responder, fica no modo padrão do Windows.
    /// </summary>
    public static CameraMode? EnsureCameraMode(string ffmpeg, string device, int targetHeight = 720)
    {
        if (CameraSource.IsStream(device) || !CameraOverlay.IsValidName(device)) return null;
        if (!KnownModes.TryGetValue(device, out var modes))
        {
            try
            {
                var listed = Shell.Run(ffmpeg, CameraSource.ListModesArgs(device));
                modes = CameraModes.Parse(listed.Error + "\n" + listed.Output);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { modes = Array.Empty<CameraMode>(); }
            if (modes.Count > 0) KnownModes[device] = modes;
        }
        var best = CameraModes.Pick(modes, targetHeight);
        if (best is not null) CameraSource.Modes[device] = best;
        return best;
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
