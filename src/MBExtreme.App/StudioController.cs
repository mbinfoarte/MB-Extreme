using System.IO;
using System.Windows;
using MBExtreme.Core.Recording;
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
    AudioConfig? Audio);

/// <summary>Mantém a sessão do estúdio viva enquanto você troca de página e cuida do atalho do replay (Ctrl + Shift + F9).</summary>
internal sealed class StudioController
{
    private const uint VkF9 = 0x78;
    private const string ReplayFolder = "replay-buffer";

    private int _hotkeyId;
    private bool _saving;
    private string? _ffmpeg;

    public StudioSession Session { get; } = new();

    public StudioConfig? Config { get; private set; }

    public bool IsRunning => Session.IsRunning;

    public bool HotkeyActive => _hotkeyId != 0;

    public static string Folder => ScreenRecorder.Folder;

    public static string ClipsFolder => ReplayBuffer.ClipsFolder(Folder);

    /// <summary>Aviso para a tela (mensagem, deu certo).</summary>
    public event Action<string, bool>? Notice;

    public async Task StartAsync(string ffmpeg, StudioConfig config)
    {
        var (width, height) = StudioCommand.FitOutput(config.Source.Area.Width, config.Source.Area.Height, config.OutputHeight);
        int bitrate = config.BitrateMbps > 0 ? config.BitrateMbps : FfmpegCommand.DefaultBitrateMbps(width, height, config.Fps);
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

        var request = new StudioRequest
        {
            Source = config.Source,
            OutputWidth = width,
            OutputHeight = height,
            Fps = config.Fps,
            Encoder = config.Encoder,
            BitrateMbps = bitrate,
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

        if (config.Replay)
        {
            _hotkeyId = AppServices.Hotkeys.RegisterCtrlShift(VkF9, () => _ = SaveReplayAsync());
            if (_hotkeyId == 0) Notice?.Invoke("O atalho Ctrl + Shift + F9 está em uso por outro programa. Use o botão Salvar replay.", false);
        }
    }

    /// <summary>Para tudo. Devolve o arquivo gravado, se havia.</summary>
    public string? Stop()
    {
        AppServices.Hotkeys.Unregister(_hotkeyId);
        _hotkeyId = 0;
        string? file = Session.Stop();
        if (Config?.Replay == true) ReplayBuffer.ClearSegments(Path.Combine(Folder, ReplayFolder));
        return file;
    }

    public async Task<string?> SaveReplayAsync()
    {
        if (_saving || !Session.IsRunning || Config is not { Replay: true } config) return null;
        _saving = true;
        try
        {
            string clip = await Session.SaveReplayAsync(ClipsFolder, config.GameName);
            Raise("Replay salvo: " + clip, true);
            if (config.Beep) System.Media.SystemSounds.Asterisk.Play();
            return clip;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
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
