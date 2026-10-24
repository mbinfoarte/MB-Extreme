using System.ComponentModel;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using MBExtreme.Core.Recording;
using MBExtreme.UI.Overlay;
using MBExtreme.UI.Recording;

namespace MBExtreme.UI;

/// <summary>Configuração escolhida na tela do estúdio.</summary>
internal sealed record StudioConfig(
    CaptureSource Source,
    string? GameName,
    int OutputHeight,
    int Fps,
    EncoderOption Encoder,
    int BitrateMbps,
    bool Record,
    bool Mp4,
    StreamTarget? Stream,
    bool Replay,
    int ReplaySeconds,
    bool Beep,
    AudioConfig? Audio,
    int StreamCapMbps = 0,
    int AudioKbps = 160,
    EncoderQuality Quality = EncoderQuality.Balanced,
    IReadOnlyList<CameraOverlay>? Cameras = null,
    PixOverlay? Pix = null,
    string? PixPayload = null);

/// <summary>Mantém a sessão do estúdio viva enquanto você troca de página e cuida dos atalhos globais (salvar replay, gravar e transmitir).</summary>
internal sealed class StudioController
{
    private const string ReplayFolder = "replay-buffer";

    private readonly Dictionary<StudioAction, int> _hotkeyIds = new();
    private readonly DispatcherTimer _highlightTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private HighlightDetector? _detector;
    private int _detectorSensitivity = -1;
    private bool _autoSaving;
    private bool _saving, _toggling;
    private string? _ffmpeg;

    public StudioController()
    {
        Session.Ended += (_, _) =>
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Session.EndedUnexpectedly)
                {
                    string? last = Session.LastMessage;
                    Stop();
                    Raise("O estúdio terminou sozinho" + (last is null ? "." : ": " + last), false);
                }
            }));
        _highlightTimer.Tick += (_, _) => HighlightTick();
    }

    public StudioSession Session { get; } = new();

    public StudioConfig? Config { get; private set; }

    public bool IsRunning => Session.IsRunning;

    /// <summary>Última configuração iniciada pela tela; os atalhos de gravar e transmitir reaproveitam a origem, o codificador e a chave dela.</summary>
    public StudioConfig? LastConfig { get; private set; }

    public bool HotkeyActive => _hotkeyIds.ContainsKey(StudioAction.SaveReplay);

    /// <summary>Atalhos que o Windows não deixou registrar (outro programa já usa).</summary>
    public IReadOnlyList<string> HotkeyFailures { get; private set; } = Array.Empty<string>();

    public static string Folder => ScreenRecorder.Folder;

    public static string ClipsFolder => ReplayBuffer.ClipsFolder(Folder);

    /// <summary>Aviso para a tela (mensagem, deu certo).</summary>
    public event Action<string, bool>? Notice;

    public async Task StartAsync(string ffmpeg, StudioConfig config)
    {
        var (width, height) = StudioCommand.FitOutput(config.Source.Area.Width, config.Source.Area.Height, config.OutputHeight);
        int fps = config.Fps;
        int bitrate = config.BitrateMbps > 0 ? config.BitrateMbps : FfmpegCommand.DefaultBitrateMbps(width, height, fps);
        int videoKbps = 0, audioKbps = config.AudioKbps, streamMax = StudioCommand.MaxStreamMbps;
        if (config.Stream is { } target)
        {
            // a transmissão segue o padrão da plataforma: resolução, quadros e taxa máximos, e o limite de banda escolhido
            var plan = StreamPlan.Build(target.Service, height, fps, config.BitrateMbps, config.StreamCapMbps, config.AudioKbps);
            if (plan.Height < height) (width, height) = StudioCommand.FitOutput(config.Source.Area.Width, config.Source.Area.Height, plan.Height);
            fps = plan.Fps;
            videoKbps = plan.VideoKbps;
            audioKbps = plan.AudioKbps;
            streamMax = (int)Math.Ceiling(plan.VideoKbps / 1000.0);
        }

        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

        var request = new StudioRequest
        {
            Source = config.Source,
            OutputWidth = width,
            OutputHeight = height,
            Fps = fps,
            Encoder = config.Encoder,
            BitrateMbps = bitrate,
            VideoKbps = videoKbps,
            StreamMaxMbps = streamMax,
            AudioKbps = audioKbps,
            Quality = config.Quality,
            Cameras = config.Cameras ?? Array.Empty<CameraOverlay>(),
            Pix = config.Pix,
            WorkingDirectory = Folder,
            RecordFile = config.Record ? "gravacao-" + stamp + FfmpegCommand.ExtensionFor(config.Mp4) : null,
            RecordMp4 = config.Mp4,
            Stream = config.Stream,
            ReplayFolder = config.Replay ? ReplayFolder : null,
            ReplaySeconds = config.ReplaySeconds,
        };

        await Session.StartAsync(ffmpeg, request, config.Audio);
        _ffmpeg = ffmpeg;
        Config = config;
        LastConfig = config;
        _detector = null;
        _highlightTimer.Start();
    }

    /// <summary>Aplica uma configuração nova à sessão que está rodando: para a captura e começa de novo. A transmissão reconecta (alguns segundos fora do ar), a gravação continua num arquivo novo e o replay recomeça vazio.</summary>
    public async Task RestartAsync(StudioConfig next)
    {
        if (_ffmpeg is null) throw new InvalidOperationException("O estúdio não está ligado.");
        string ffmpeg = _ffmpeg;
        await Task.Run(Stop);
        await StartAsync(ffmpeg, next);
    }

    /// <summary>Texto que muda sempre que algo que exige reiniciar a captura muda. A chave de transmissão entra só como resumo (nunca em texto).</summary>
    public static string Signature(StudioConfig c)
    {
        var parts = new List<string>
        {
            c.Source.Kind.ToString(), c.Source.DisplayIndex.ToString(), $"{c.Source.Area.X},{c.Source.Area.Y},{c.Source.Area.Width},{c.Source.Area.Height}", c.Source.WindowTitle ?? "",
            c.OutputHeight.ToString(), c.Fps.ToString(), c.Encoder.FfmpegName, c.BitrateMbps.ToString(), c.Record.ToString(), c.Mp4.ToString(),
            c.Replay.ToString(), c.ReplaySeconds.ToString(), c.StreamCapMbps.ToString(), c.AudioKbps.ToString(), c.Quality.ToString(),
            c.Audio is null ? "sem-audio" : $"{c.Audio.CaptureSystem}:{c.Audio.OutputId}:{c.Audio.CaptureMicrophone}:{c.Audio.MicrophoneId}",
        };
        if (c.Stream is { } t)
        {
            byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(t.FullUrl));
            parts.Add($"stream:{t.Service}:{Convert.ToHexString(hash, 0, 6)}");
        }
        foreach (var cam in c.Cameras ?? Array.Empty<CameraOverlay>()) parts.Add($"cam:{cam.DeviceName}:{cam.Corner}:{cam.WidthPercent}:{cam.Frame}");
        if (c.Pix is { } pix)
        {
            byte[] pixHash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(c.PixPayload ?? ""));
            parts.Add($"pix:{pix.Corner}:{pix.WidthPercent}:{pix.EveryMinutes}:{pix.ShowSeconds}:{Convert.ToHexString(pixHash, 0, 6)}");
        }
        return string.Join("|", parts);
    }

    // ---------------------------------------------------------------- clipes automáticos de lances

    private void HighlightTick()
    {
        if (!Session.IsRunning || Config is not { Replay: true })
        {
            _highlightTimer.Stop();
            return;
        }

        var settings = AppServices.Settings;
        if (!settings.StudioAutoClip || !Session.HasAudio || !Session.HasSystemAudio) return;
        int sensitivity = Math.Clamp(settings.StudioAutoClipSensitivity, 0, 2);
        if (_detector is null || sensitivity != _detectorSensitivity)
        {
            _detector = new HighlightDetector((HighlightDetector.Sensitivity)sensitivity);
            _detectorSensitivity = sensitivity;
        }

        // só o som do jogo conta: a sua própria voz no microfone não pode disparar o clipe
        if (_detector.Feed(Session.TakeSystemPeak(), _clock.Elapsed.TotalSeconds) && !_autoSaving)
            _ = SaveHighlightAsync(Math.Clamp(settings.StudioAutoClipAfterSeconds, 0, 20));
    }

    private async Task SaveHighlightAsync(int afterSeconds)
    {
        _autoSaving = true;
        try
        {
            if (AppServices.Settings.StudioClipToast) ClipToast.Show("Lance detectado", $"Salvando o clipe em {afterSeconds} s...");
            if (afterSeconds > 0) await Task.Delay(TimeSpan.FromSeconds(afterSeconds));
            await SaveReplayAsync(automatic: true);
        }
        finally
        {
            _autoSaving = false;
        }
    }

    // ---------------------------------------------------------------- áudio ao vivo

    /// <summary>Muda o volume do som do PC ou do microfone na hora, sem reiniciar a captura.</summary>
    public void SetVolume(bool microphone, float volume) => Session.SetVolume(microphone, volume);

    public float GetVolume(bool microphone) => Session.GetVolume(microphone);

    // ---------------------------------------------------------------- atalhos globais

    /// <summary>(Re)registra os atalhos das configurações. Chame ao abrir o aplicativo e sempre que a pessoa trocar um atalho.</summary>
    public void ApplyHotkeys(MBExtreme.Core.Settings.AppSettings settings)
    {
        foreach (int id in _hotkeyIds.Values) AppServices.Hotkeys.Unregister(id);
        _hotkeyIds.Clear();

        var wanted = new Dictionary<StudioAction, HotkeyBinding?>
        {
            [StudioAction.SaveReplay] = HotkeyBinding.Parse(settings.StudioHotkeySave),
            [StudioAction.ToggleRecord] = HotkeyBinding.Parse(settings.StudioHotkeyRecord),
            [StudioAction.ToggleStream] = HotkeyBinding.Parse(settings.StudioHotkeyStream),
        };
        var failed = new List<string>();
        var taken = new HashSet<string>();
        foreach (var (action, binding) in wanted)
        {
            if (binding is null) continue;
            if (!taken.Add(binding.Stored)) { failed.Add(binding.Text + " (repetido)"); continue; }
            var captured = action;
            int id = AppServices.Hotkeys.Register(binding, () => OnHotkey(captured));
            if (id == 0) failed.Add(binding.Text + " (em uso por outro programa)");
            else _hotkeyIds[action] = id;
        }

        HotkeyFailures = failed;
    }

    private void OnHotkey(StudioAction action)
    {
        switch (action)
        {
            case StudioAction.SaveReplay:
                if (!Session.IsRunning || Config is not { Replay: true })
                    Raise("Ligue o Replay instantâneo e inicie o estúdio para salvar clipes.", false);
                else
                    _ = SaveReplayAsync();
                break;
            default:
                _ = ToggleAsync(action);
                break;
        }
    }

    /// <summary>Atalho de gravar ou transmitir: se o estúdio está ligado, para; se não, liga só aquela saída com a última configuração usada.</summary>
    private async Task ToggleAsync(StudioAction action)
    {
        if (_toggling) return;
        _toggling = true;
        try
        {
            if (Session.IsRunning || AppServices.Recorder.IsRecording)
            {
                string? file = await Task.Run(Stop);
                Raise(file is null ? "Estúdio parado." : "Gravação salva em " + file, true);
                return;
            }

            if (LastConfig is not { } last || _ffmpeg is null)
            {
                Raise("Abra Gravação e transmissão e inicie uma vez pela tela: o atalho reaproveita a origem e as configurações escolhidas ali.", false);
                return;
            }

            StudioConfig next;
            if (action == StudioAction.ToggleRecord)
            {
                next = last with { Record = true, Stream = null };
            }
            else
            {
                if (last.Stream is null) { Raise("Para transmitir pelo atalho, inicie uma transmissão uma vez pela tela (com a chave) para o programa guardar o destino.", false); return; }
                next = last with { Record = false };
            }

            await StartAsync(_ffmpeg, next);
            Raise(action == StudioAction.ToggleRecord ? "Gravando." : "Transmitindo para " + next.Stream!.DisplayName + ".", true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or Win32Exception)
        {
            Raise("Não foi possível iniciar: " + StudioCommand.MaskSecrets(ex.Message, LastConfig?.Stream), false);
        }
        finally
        {
            _toggling = false;
        }
    }

    /// <summary>Para tudo. Devolve o arquivo gravado, se havia.</summary>
    public string? Stop()
    {
        string? file = Session.Stop();   // o relógio dos lances se desliga sozinho no próximo tique (este método roda fora da interface)
        if (Config?.Replay == true) ReplayBuffer.ClearSegments(Path.Combine(Folder, ReplayFolder));
        return file;
    }

    public Task<string?> SaveReplayAsync() => SaveReplayAsync(automatic: false);

    public async Task<string?> SaveReplayAsync(bool automatic)
    {
        if (_saving || !Session.IsRunning || Config is not { Replay: true } config) return null;
        _saving = true;
        try
        {
            string clip = await Session.SaveReplayAsync(ClipsFolder, config.GameName);
            Raise((automatic ? "Lance salvo automaticamente: " : "Replay salvo: ") + clip, true);
            if (config.Beep) System.Media.SystemSounds.Asterisk.Play();
            if (AppServices.Settings.StudioClipToast) ClipToast.Show(automatic ? "Lance salvo" : "Clipe salvo", Path.GetFileName(clip));
            return clip;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or Win32Exception)
        {
            Raise("Não foi possível salvar o replay: " + ex.Message, false);
            return null;
        }
        finally
        {
            _saving = false;
        }
    }

    public void Shutdown()
    {
        if (Session.IsRunning) Stop();
    }

    private void Raise(string message, bool ok) =>
        Application.Current?.Dispatcher.BeginInvoke(new Action(() => Notice?.Invoke(message, ok)));
}
