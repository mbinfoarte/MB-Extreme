using NAudio.CoreAudioApi;
using NAudio.Wave;
using MBExtreme.Core.Remote;

namespace MBExtreme.UI.Remote;

/// <summary>Captura o som que sai nas caixas do PC (loopback do WASAPI) e entrega em PCM 16 bits mono de 22050 Hz.</summary>
internal sealed class LoopbackAudioSource : IAudioSource
{
    private WasapiLoopbackCapture? _capture;
    private AudioConverter? _converter;

    public event Action<byte[]>? Chunk;

    public void Start()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        _capture = new WasapiLoopbackCapture(device);
        var f = _capture.WaveFormat;
        _converter = new AudioConverter(f.SampleRate, f.Channels, f.BitsPerSample, f.Encoding == WaveFormatEncoding.IeeeFloat
            || (f.Encoding == WaveFormatEncoding.Extensible && f.BitsPerSample == 32));
        _capture.DataAvailable += (_, e) =>
        {
            if (e.BytesRecorded == 0 || _converter is null) return;
            byte[] pcm = _converter.Convert(e.Buffer.AsSpan(0, e.BytesRecorded));
            if (pcm.Length > 0) Chunk?.Invoke(pcm);
        };
        _capture.StartRecording();
    }

    public void Dispose()
    {
        try { _capture?.StopRecording(); } catch (InvalidOperationException) { }
        _capture?.Dispose();
        _capture = null;
    }
}

/// <summary>Toca o som recebido do computador remoto com um atraso pequeno (meio segundo no máximo).</summary>
internal sealed class RemoteAudioPlayer : IDisposable
{
    private readonly BufferedWaveProvider _buffer = new(new WaveFormat(AudioConverter.TargetRate, 16, 1))
    {
        BufferDuration = TimeSpan.FromMilliseconds(600),
        DiscardOnBufferOverflow = true,
    };
    private WasapiOut? _out;
    private bool _failed;

    public bool Muted { get; set; }

    public void Play(AudioMessage message)
    {
        if (Muted || _failed) return;
        try
        {
            if (_out is null) Start();
            _buffer.AddSamples(message.Pcm, 0, message.Pcm.Length);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException or NAudio.MmException)
        {
            _failed = true;   // sem saída de som: a sessão segue só com imagem
        }
    }

    private void Start()
    {
        // converte para o formato que a saída de som do Windows usa (taxa e canais do dispositivo)
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var mix = device.AudioClient.MixFormat;
        NAudio.Wave.ISampleProvider samples = _buffer.ToSampleProvider();
        if (mix.Channels >= 2) samples = new NAudio.Wave.SampleProviders.MonoToStereoSampleProvider(samples);
        if (mix.SampleRate != AudioConverter.TargetRate) samples = new NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(samples, mix.SampleRate);
        _out = new WasapiOut(AudioClientShareMode.Shared, 120);
        _out.Init(samples.ToWaveProvider());
        _out.Play();
    }

    public void Dispose()
    {
        _out?.Stop();
        _out?.Dispose();
        _out = null;
        _buffer.ClearBuffer();
    }
}
