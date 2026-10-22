using System.IO.Pipes;
using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MBExtreme.Core.Recording;

public sealed record AudioDevice(string Id, string Name);

/// <summary>Dispositivos de áudio do Windows (microfones e saídas).</summary>
[SupportedOSPlatform("windows")]
public static class AudioDevices
{
    public static IReadOnlyList<AudioDevice> Microphones() => List(DataFlow.Capture);

    public static IReadOnlyList<AudioDevice> Outputs() => List(DataFlow.Render);

    private static IReadOnlyList<AudioDevice> List(DataFlow flow)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active)
                .Select(d => new AudioDevice(d.ID, d.FriendlyName)).ToList();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            return Array.Empty<AudioDevice>();
        }
    }
}

/// <summary>Captura o som do PC (loopback) e o microfone e entrega ao mixer.</summary>
[SupportedOSPlatform("windows")]
public sealed class AudioCapture : IDisposable
{
    private readonly List<IWaveIn> _captures = new();
    private readonly List<IDisposable> _devices = new();

    public AudioInput? SystemSound { get; private set; }

    public AudioInput? Microphone { get; private set; }

    public string? Warning { get; private set; }

    /// <param name="outputId">Saída cujo som será capturado (nulo = padrão do Windows).</param>
    /// <param name="microphoneId">Microfone (nulo = não captura).</param>
    public AudioCapture(AudioMixer mixer, bool captureSystem, string? outputId, float systemVolume,
        bool captureMic, string? microphoneId, float micVolume)
    {
        using var enumerator = new MMDeviceEnumerator();
        if (captureSystem)
        {
            try
            {
                var device = outputId is null
                    ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                    : enumerator.GetDevice(outputId);
                _devices.Add(device);
                var loopback = new WasapiLoopbackCapture(device);
                SystemSound = Attach(mixer, loopback, "Som do PC", systemVolume);
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
            {
                Warning = "Não foi possível capturar o som do PC: " + ex.Message;
            }
        }

        if (captureMic)
        {
            try
            {
                var device = microphoneId is null
                    ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)
                    : enumerator.GetDevice(microphoneId);
                _devices.Add(device);
                var mic = new WasapiCapture(device);
                Microphone = Attach(mixer, mic, "Microfone", micVolume);
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
            {
                Warning = (Warning is null ? "" : Warning + " ") + "Não foi possível usar o microfone: " + ex.Message;
            }
        }
    }

    private AudioInput Attach(AudioMixer mixer, IWaveIn capture, string name, float volume)
    {
        var input = mixer.AddInput(name, capture.WaveFormat, volume);
        capture.DataAvailable += (_, e) => input.Push(e.Buffer, e.BytesRecorded);
        _captures.Add(capture);
        return input;
    }

    public void Start()
    {
        foreach (var capture in _captures) capture.StartRecording();
    }

    public void Dispose()
    {
        foreach (var capture in _captures)
        {
            try { capture.StopRecording(); } catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
            capture.Dispose();
        }
        foreach (var device in _devices) device.Dispose();
        _captures.Clear();
        _devices.Clear();
    }
}

/// <summary>Cano nomeado por onde o áudio mixado chega ao FFmpeg.</summary>
[SupportedOSPlatform("windows")]
public sealed class AudioPipe : IDisposable
{
    private readonly NamedPipeServerStream _server;
    private Task? _connection;

    public AudioPipe()
    {
        Name = "mbx-audio-" + Guid.NewGuid().ToString("N");
        _server = new NamedPipeServerStream(Name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
            0, AudioMixer.SampleRate * AudioMixer.BytesPerFrame);
    }

    public string Name { get; }

    public string Path => @"\\.\pipe\" + Name;

    public Stream Stream => _server;

    /// <summary>Começa a esperar o FFmpeg se conectar (chame antes de iniciar o FFmpeg).</summary>
    public void BeginWait() => _connection = _server.WaitForConnectionAsync();

    public async Task<bool> WaitConnectedAsync(TimeSpan timeout)
    {
        if (_connection is null) BeginWait();
        var finished = await Task.WhenAny(_connection!, Task.Delay(timeout)).ConfigureAwait(false);
        return finished == _connection && _server.IsConnected;
    }

    public void Dispose() => _server.Dispose();
}
