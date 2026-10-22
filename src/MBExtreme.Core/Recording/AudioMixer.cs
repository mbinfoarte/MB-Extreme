using System.Diagnostics;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MBExtreme.Core.Recording;

/// <summary>Uma fonte de áudio (som do PC ou microfone) conectada ao mixer.</summary>
public sealed class AudioInput
{
    private readonly BufferedWaveProvider _buffer;
    private readonly VolumeSampleProvider _volume;

    internal AudioInput(string name, WaveFormat format, float volume)
    {
        Name = name;
        _buffer = new BufferedWaveProvider(format)
        {
            ReadFully = true,
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromMilliseconds(600),
        };

        ISampleProvider samples = _buffer.ToSampleProvider();
        if (samples.WaveFormat.Channels == 1) samples = new MonoToStereoSampleProvider(samples);
        else if (samples.WaveFormat.Channels > 2) samples = new FirstTwoChannelsProvider(samples);
        if (samples.WaveFormat.SampleRate != AudioMixer.SampleRate) samples = new WdlResamplingSampleProvider(samples, AudioMixer.SampleRate);
        _volume = new VolumeSampleProvider(samples) { Volume = volume };
    }

    public string Name { get; }

    public float Volume
    {
        get => _volume.Volume;
        set => _volume.Volume = Math.Clamp(value, 0f, 2f);
    }

    internal ISampleProvider Output => _volume;

    /// <summary>Entrega o áudio capturado (no formato informado ao criar a fonte).</summary>
    public void Push(byte[] data, int count) => _buffer.AddSamples(data, 0, count);
}

/// <summary>Mistura as fontes de áudio em um único fluxo PCM de 48 kHz, 16 bits, estéreo, no ritmo do relógio.</summary>
public sealed class AudioMixer
{
    public const int SampleRate = 48000;
    public const int BytesPerFrame = 4;

    private readonly MixingSampleProvider _mix = new(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2)) { ReadFully = true };
    private readonly List<AudioInput> _inputs = new();
    private float[] _floats = new float[SampleRate / 2];
    private volatile float _peak;

    /// <summary>Volume do momento (0 a 1) para o medidor da tela.</summary>
    public float Peak => _peak;

    public IReadOnlyList<AudioInput> Inputs => _inputs;

    public AudioInput AddInput(string name, WaveFormat format, float volume = 1f)
    {
        var input = new AudioInput(name, (format as WaveFormatExtensible)?.ToStandardWaveFormat() ?? format, volume);
        lock (_mix)
        {
            _inputs.Add(input);
            _mix.AddMixerInput(input.Output);
        }
        return input;
    }

    /// <summary>Lê <paramref name="frames"/> quadros estéreo de 16 bits. Sem fontes ou sem som, devolve silêncio.</summary>
    public int Read(byte[] buffer, int frames)
    {
        frames = Math.Min(frames, buffer.Length / BytesPerFrame);
        int samples = frames * 2;
        if (_floats.Length < samples) _floats = new float[samples];

        int read;
        lock (_mix) read = _mix.Read(_floats, 0, samples);

        float peak = 0;
        for (int i = 0; i < samples; i++)
        {
            float s = i < read ? Math.Clamp(_floats[i], -1f, 1f) : 0f;
            peak = Math.Max(peak, Math.Abs(s));
            short v = (short)Math.Round(s * short.MaxValue);
            buffer[i * 2] = (byte)(v & 0xFF);
            buffer[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        _peak = peak;
        return frames;
    }

    /// <summary>Escreve áudio na saída em tempo real até cancelar. Mesmo sem som, escreve silêncio para o FFmpeg não esperar.</summary>
    public async Task PumpAsync(Stream output, CancellationToken token)
    {
        var clock = Stopwatch.StartNew();
        long written = 0;
        var buffer = new byte[SampleRate * BytesPerFrame];
        try
        {
            while (!token.IsCancellationRequested)
            {
                long due = (long)(clock.Elapsed.TotalSeconds * SampleRate) - written;
                if (due < SampleRate / 100) // menos de 10 ms
                {
                    await Task.Delay(5, token).ConfigureAwait(false);
                    continue;
                }

                int frames = (int)Math.Min(due, SampleRate);
                Read(buffer, frames);
                await output.WriteAsync(buffer.AsMemory(0, frames * BytesPerFrame), token).ConfigureAwait(false);
                written += frames;
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { /* o FFmpeg fechou a leitura */ }
        catch (ObjectDisposedException) { }
    }
}

internal sealed class FirstTwoChannelsProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private float[] _scratch = Array.Empty<float>();

    public FirstTwoChannelsProvider(ISampleProvider source)
    {
        _source = source;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        int channels = _source.WaveFormat.Channels;
        int frames = count / 2;
        if (_scratch.Length < frames * channels) _scratch = new float[frames * channels];
        int got = _source.Read(_scratch, 0, frames * channels) / channels;
        for (int i = 0; i < got; i++)
        {
            buffer[offset + i * 2] = _scratch[i * channels];
            buffer[offset + i * 2 + 1] = _scratch[i * channels + 1];
        }
        return got * 2;
    }
}
