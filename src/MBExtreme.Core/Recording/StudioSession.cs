using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace MBExtreme.Core.Recording;

public sealed record AudioConfig(
    bool CaptureSystem, string? OutputId, float SystemVolume,
    bool CaptureMicrophone, string? MicrophoneId, float MicrophoneVolume)
{
    public bool Any => CaptureSystem || CaptureMicrophone;
}

/// <summary>Uma sessão do estúdio: um FFmpeg capturando e codificando uma vez, enviando para arquivo, transmissão e/ou replay, com o áudio mixado por aqui.</summary>
[SupportedOSPlatform("windows")]
public sealed class StudioSession : IDisposable
{
    private readonly object _gate = new();
    private readonly List<string> _warnings = new();
    private Process? _process;
    private AudioMixer? _mixer;
    private AudioCapture? _capture;
    private AudioPipe? _pipe;
    private CancellationTokenSource? _pumpCts;
    private Task? _pump;
    private string _ffmpeg = "";
    private StudioRequest? _request;
    private bool _stopping;
    private volatile bool _starting;

    public FfmpegProgress Progress { get; } = new();

    public bool IsRunning => _process is { HasExited: false };

    public bool EndedUnexpectedly => !_starting && _process is { HasExited: true } && !_stopping;

    public DateTime StartedAt { get; private set; }

    public string? LastMessage { get; private set; }

    /// <summary>Avisos que não derrubam a sessão (queda da transmissão, áudio indisponível...).</summary>
    public IReadOnlyList<string> Warnings
    {
        get { lock (_gate) return _warnings.ToList(); }
    }

    public bool StreamDropped { get; private set; }

    public bool HasAudio => _pipe is not null;

    public float AudioPeak => _mixer?.Peak ?? 0;

    public AudioMixer? Mixer => _mixer;

    public StudioRequest? Request => _request;

    public string? RecordPath => _request?.RecordFile is { } f ? Path.Combine(_request.WorkingDirectory, f) : null;

    public string? ReplayPath => _request?.ReplayFolder is { } f ? Path.Combine(_request.WorkingDirectory, f) : null;

    public event EventHandler? WarningAdded;

    /// <summary>O FFmpeg terminou sozinho (disco cheio, driver reiniciado...). Chamado em outra thread.</summary>
    public event EventHandler? Ended;

    public async Task StartAsync(string ffmpeg, StudioRequest request, AudioConfig? audio)
    {
        if (IsRunning) throw new InvalidOperationException("O estúdio já está em andamento.");
        string? problem = StudioCommand.Validate(request);
        if (problem is not null) throw new InvalidOperationException(problem);

        _ffmpeg = ffmpeg;
        _stopping = false;
        StreamDropped = false;
        lock (_gate) _warnings.Clear();
        Directory.CreateDirectory(request.WorkingDirectory);
        if (request.ReplayFolder is { } replay) ReplayBuffer.ClearSegments(Path.Combine(request.WorkingDirectory, replay));

        bool withAudio = audio is { Any: true };
        _starting = true;
        try
        {
            try
            {
                await LaunchAsync(request, withAudio ? audio : null).ConfigureAwait(false);
            }
            catch (AudioUnavailableException ex)
            {
                AddWarning(ex.Message + " Gravando sem áudio.");
                await LaunchAsync(request, null).ConfigureAwait(false);
            }
        }
        finally
        {
            _starting = false;
        }
    }

    private sealed class AudioUnavailableException(string message) : Exception(message);

    private async Task LaunchAsync(StudioRequest request, AudioConfig? audio)
    {
        StopAudio();
        var effective = request;
        if (audio is not null)
        {
            try
            {
                _mixer = new AudioMixer();
                _capture = new AudioCapture(_mixer, audio.CaptureSystem, audio.OutputId, audio.SystemVolume,
                    audio.CaptureMicrophone, audio.MicrophoneId, audio.MicrophoneVolume);
                if (_capture.Warning is { } w) AddWarning(w);
                if (_mixer.Inputs.Count == 0) throw new InvalidOperationException("nenhuma fonte de áudio disponível");
                _pipe = new AudioPipe();
                _pipe.BeginWait();
                _capture.Start();
                effective = request with { Audio = new AudioSetup(_pipe.Path) };
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or System.Runtime.InteropServices.COMException)
            {
                StopAudio();
                throw new AudioUnavailableException("Não foi possível preparar o áudio (" + ex.Message + ").");
            }
        }

        var info = new ProcessStartInfo(_ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WorkingDirectory = effective.WorkingDirectory,
        };
        foreach (string a in StudioCommand.Build(effective)) info.ArgumentList.Add(a);

        Process? process = null;
        try
        {
        process = Process.Start(info) ?? throw new InvalidOperationException("Não foi possível iniciar o FFmpeg.");
        _process = process;
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            if (!_stopping && !_starting && ReferenceEquals(_process, process)) Ended?.Invoke(this, EventArgs.Empty);
        };
        _request = effective;
        LastMessage = null;
        process.ErrorDataReceived += (_, e) => OnError(e.Data, effective.Stream);
        process.OutputDataReceived += (_, e) => { if (e.Data is { Length: > 0 }) Progress.Feed(e.Data); };
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        if (_pipe is not null)
        {
            bool connected = await _pipe.WaitConnectedAsync(TimeSpan.FromSeconds(8), process.WaitForExitAsync()).ConfigureAwait(false);
            if (!connected)
            {
                if (process.HasExited)
                {
                    await Task.Run(() => process.WaitForExit()).ConfigureAwait(false);
                    throw new InvalidOperationException("O FFmpeg encerrou ao iniciar: " + (LastMessage ?? "configuração não suportada."));
                }

                throw new AudioUnavailableException("O FFmpeg não conseguiu abrir o canal de áudio.");
            }

            _pumpCts = new CancellationTokenSource();
            var pipe = _pipe;
            var mixer = _mixer!;
            var token = _pumpCts.Token;
            _pump = Task.Run(() => mixer.PumpAsync(pipe.Stream, token));
        }

        // Se o FFmpeg fechar logo, a configuração não funciona nesta máquina.
        if (await Task.Run(() => process.WaitForExit(2000)).ConfigureAwait(false))
        {
            await Task.Run(() => process.WaitForExit()).ConfigureAwait(false);
            string message = LastMessage ?? "configuração não suportada nesta placa de vídeo.";
            throw new InvalidOperationException("O FFmpeg encerrou ao iniciar: " + message);
        }

        StartedAt = DateTime.Now;
        }
        catch
        {
            KillProcess();
            StopAudio();
            _request = null;
            throw;
        }
    }

    private void OnError(string? line, StreamTarget? target)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        string text = StudioCommand.MaskSecrets(line.Trim(), target);
        LastMessage = text;
        if (text.Contains("Slave muxer", StringComparison.OrdinalIgnoreCase) && text.Contains("fail", StringComparison.OrdinalIgnoreCase))
        {
            StreamDropped = true;
            AddWarning("Uma das saídas falhou (provavelmente a transmissão caiu): " + text);
        }
    }

    private void AddWarning(string text)
    {
        lock (_gate)
        {
            if (_warnings.Count < 50) _warnings.Add(text);
        }
        WarningAdded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Junta os últimos segundos do replay em um clipe MP4 e devolve o caminho.</summary>
    public Task<string> SaveReplayAsync(string clipsFolder, string? game)
    {
        var request = _request;
        if (request?.ReplayFolder is null) throw new InvalidOperationException("O replay não está ligado.");
        string folder = ReplayPath!;
        int seconds = request.ReplaySeconds;
        string ffmpeg = _ffmpeg;
        return Task.Run(() =>
        {
            var segments = ReplayBuffer.SelectSegments(ReplayBuffer.ListSegments(folder), seconds);
            return ReplayBuffer.SaveClip(ffmpeg, segments, clipsFolder, game, DateTime.Now);
        });
    }

    /// <summary>Encerra a sessão. Devolve o arquivo gravado, se havia.</summary>
    public string? Stop()
    {
        _stopping = true;
        var process = _process;
        if (process is not null)
        {
            try
            {
                if (!process.HasExited)
                {
                    try { process.StandardInput.WriteLine("q"); process.StandardInput.Flush(); }
                    catch (IOException) { }
                    if (!process.WaitForExit(12000)) process.Kill(true);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
        }

        string? file = RecordPath;
        KillProcess();
        StopAudio();
        return file is not null && File.Exists(file) ? file : null;
    }

    private void KillProcess()
    {
        var process = _process;
        _process = null;
        if (process is null) return;
        try
        {
            if (!process.HasExited) process.Kill(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
        process.Dispose();
    }

    private void StopAudio()
    {
        try { _pumpCts?.Cancel(); } catch (ObjectDisposedException) { }
        try { _pump?.Wait(1500); } catch (AggregateException) { }
        _capture?.Dispose();
        _pipe?.Dispose();
        _pumpCts?.Dispose();
        _capture = null;
        _pipe = null;
        _pump = null;
        _pumpCts = null;
        _mixer = null;
    }

    public void Dispose()
    {
        Stop();
    }
}
