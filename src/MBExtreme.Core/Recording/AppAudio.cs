using System.Diagnostics;
using System.Runtime.Versioning;
using NAudio.CoreAudioApi;

namespace MBExtreme.Core.Recording;

public enum AppAudioKind { Game, Voice, Music, Browser, Other }

/// <summary>Um programa que está usando o áudio agora (todas as janelas de som do mesmo programa juntas).</summary>
public sealed record AppAudioSession(string ProcessName, string Display, AppAudioKind Kind, float Volume, bool Muted, float Peak)
{
    public string KindText => Kind switch { AppAudioKind.Game => "Jogo", AppAudioKind.Voice => "Voz", AppAudioKind.Music => "Música", AppAudioKind.Browser => "Navegador", _ => "" };
}

/// <summary>Reconhece programas de áudio comuns (Discord, Spotify, navegadores) e os jogos da biblioteca.</summary>
public static class AppAudioRules
{
    private static readonly Dictionary<string, (string Name, AppAudioKind Kind)> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["discord"] = ("Discord", AppAudioKind.Voice), ["discordptb"] = ("Discord PTB", AppAudioKind.Voice), ["discordcanary"] = ("Discord Canary", AppAudioKind.Voice),
        ["teamspeak"] = ("TeamSpeak", AppAudioKind.Voice), ["ts3client_win64"] = ("TeamSpeak 3", AppAudioKind.Voice), ["mumble"] = ("Mumble", AppAudioKind.Voice),
        ["zoom"] = ("Zoom", AppAudioKind.Voice), ["teams"] = ("Microsoft Teams", AppAudioKind.Voice), ["ms-teams"] = ("Microsoft Teams", AppAudioKind.Voice),
        ["skype"] = ("Skype", AppAudioKind.Voice), ["telegram"] = ("Telegram", AppAudioKind.Voice), ["whatsapp"] = ("WhatsApp", AppAudioKind.Voice),
        ["spotify"] = ("Spotify", AppAudioKind.Music), ["music.ui"] = ("Groove/Mídia", AppAudioKind.Music), ["wmplayer"] = ("Windows Media Player", AppAudioKind.Music),
        ["vlc"] = ("VLC", AppAudioKind.Music), ["foobar2000"] = ("foobar2000", AppAudioKind.Music), ["deezer"] = ("Deezer", AppAudioKind.Music),
        ["chrome"] = ("Google Chrome", AppAudioKind.Browser), ["msedge"] = ("Microsoft Edge", AppAudioKind.Browser), ["firefox"] = ("Firefox", AppAudioKind.Browser),
        ["opera"] = ("Opera", AppAudioKind.Browser), ["brave"] = ("Brave", AppAudioKind.Browser), ["vivaldi"] = ("Vivaldi", AppAudioKind.Browser),
        ["steamwebhelper"] = ("Steam", AppAudioKind.Other), ["epicgameslauncher"] = ("Epic Games", AppAudioKind.Other),
    };

    public static (string Name, AppAudioKind Kind) Describe(string processName, IEnumerable<string>? gameProcesses = null)
    {
        string clean = (processName ?? "").Trim();
        if (clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) clean = clean[..^4];
        if (gameProcesses is not null && gameProcesses.Any(g => string.Equals(g.Replace(".exe", "", StringComparison.OrdinalIgnoreCase), clean, StringComparison.OrdinalIgnoreCase)))
            return (clean, AppAudioKind.Game);
        if (Known.TryGetValue(clean, out var known)) return known;
        return (clean.Length == 0 ? "Programa" : clean, AppAudioKind.Other);
    }

    /// <summary>Jogos primeiro, depois voz, música e o resto; dentro de cada grupo, os mais barulhentos antes.</summary>
    public static IReadOnlyList<AppAudioSession> Order(IEnumerable<AppAudioSession> sessions) =>
        sessions.OrderBy(s => s.Kind switch { AppAudioKind.Game => 0, AppAudioKind.Voice => 1, AppAudioKind.Music => 2, AppAudioKind.Browser => 3, _ => 4 })
                .ThenByDescending(s => s.Peak).ThenBy(s => s.Display, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Junta as janelas de som de um mesmo programa (um navegador pode ter várias).</summary>
    public static AppAudioSession Merge(string processName, string display, AppAudioKind kind, IReadOnlyList<(float Volume, bool Muted, float Peak)> parts) =>
        new(processName, display, kind, parts.Count == 0 ? 1f : parts.Max(p => p.Volume), parts.Count > 0 && parts.All(p => p.Muted), parts.Count == 0 ? 0f : parts.Max(p => p.Peak));
}

/// <summary>
/// Lê os programas que estão tocando som e o volume de cada um, como o "Mixer de volume" do Windows. Mudar o volume de um programa aqui muda o som de verdade
/// (você também passa a ouvir diferente): não é um volume só da transmissão. O volume só da transmissão exige capturar cada programa separado (Windows 10 build 20348 ou mais novo).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AppAudioMixer : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private MMDevice? _render;
    private MMDevice? _capture;
    private readonly Dictionary<uint, (string Name, DateTime At)> _names = new();

    private string? NameOf(uint pid)
    {
        if (_names.TryGetValue(pid, out var cached) && DateTime.UtcNow - cached.At < TimeSpan.FromSeconds(10)) return cached.Name.Length == 0 ? null : cached.Name;
        string name;
        try { name = Process.GetProcessById((int)pid).ProcessName; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { name = ""; }
        _names[pid] = (name, DateTime.UtcNow);
        return name.Length == 0 ? null : name;
    }

    private MMDevice Render() => _render ??= _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

    /// <summary>Volume do momento (0 a 1) de uma saída. <paramref name="id"/> nulo = a padrão.</summary>
    public float OutputPeak(string? id)
    {
        try
        {
            var device = id is null ? Render() : _enumerator.GetDevice(id);
            return device.AudioMeterInformation.MasterPeakValue;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException) { _render = null; return 0; }
    }

    public float MicrophonePeak(string? id)
    {
        try
        {
            _capture ??= id is null ? _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications) : _enumerator.GetDevice(id);
            return _capture.AudioMeterInformation.MasterPeakValue;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException) { _capture = null; return 0; }
    }

    public void ForgetMicrophone() => _capture = null;

    public IReadOnlyList<AppAudioSession> Snapshot(IEnumerable<string>? gameProcesses = null)
    {
        var groups = new Dictionary<string, List<(float, bool, float)>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var manager = Render().AudioSessionManager;
            manager.RefreshSessions();
            var sessions = manager.Sessions;
            int self = Environment.ProcessId;
            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                if (s.IsSystemSoundsSession) continue;
                uint pid = s.GetProcessID;
                if (pid == 0 || pid == self) continue;
                string? name = NameOf(pid);
                if (name is null) continue;
                if (s.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateExpired) continue;
                if (!groups.TryGetValue(name, out var list)) groups[name] = list = new();
                list.Add((s.SimpleAudioVolume.Volume, s.SimpleAudioVolume.Mute, s.AudioMeterInformation.MasterPeakValue));
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            _render = null;
            return Array.Empty<AppAudioSession>();
        }

        var result = new List<AppAudioSession>();
        foreach (var (name, parts) in groups)
        {
            var (display, kind) = AppAudioRules.Describe(name, gameProcesses);
            result.Add(AppAudioRules.Merge(name, display, kind, parts));
        }

        return AppAudioRules.Order(result);
    }

    public void SetVolume(string processName, float volume) => Apply(processName, s => s.SimpleAudioVolume.Volume = Math.Clamp(volume, 0f, 1f));

    public void SetMuted(string processName, bool muted) => Apply(processName, s => s.SimpleAudioVolume.Mute = muted);

    private void Apply(string processName, Action<AudioSessionControl> change)
    {
        try
        {
            var manager = Render().AudioSessionManager;
            manager.RefreshSessions();
            var sessions = manager.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                if (s.IsSystemSoundsSession || s.GetProcessID == 0) continue;
                if (string.Equals(NameOf(s.GetProcessID), processName, StringComparison.OrdinalIgnoreCase)) change(s);
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { _render = null; }
    }

    public void Dispose()
    {
        _render?.Dispose();
        _capture?.Dispose();
        _enumerator.Dispose();
    }
}
