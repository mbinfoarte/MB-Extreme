namespace MBExtreme.Core.Remote;

/// <summary>Fonte do som do computador do cliente (no Windows, a captura "loopback" das caixas de som).</summary>
public interface IAudioSource : IDisposable
{
    /// <summary>PCM 16 bits mono em <see cref="AudioConverter.TargetRate"/> Hz.</summary>
    event Action<byte[]>? Chunk;

    void Start();
}

/// <summary>Converte o som capturado (float 32 ou PCM 16, qualquer taxa e canais) para PCM 16 bits mono em 22050 Hz.</summary>
public sealed class AudioConverter
{
    public const int TargetRate = 22050;
    private readonly int _rate, _channels, _bits;
    private readonly bool _float;
    private double _position;       // posição fracionária dentro da entrada
    private double _carry;          // última amostra do bloco anterior, para interpolar a emenda
    private bool _hasCarry;

    public AudioConverter(int sampleRate, int channels, int bitsPerSample, bool isFloat)
    {
        if (sampleRate < 8000 || channels < 1 || channels > 8) throw new ArgumentException("Formato de áudio não suportado.");
        if (!(isFloat && bitsPerSample == 32) && bitsPerSample != 16) throw new ArgumentException("Só PCM de 16 bits ou float de 32 bits.");
        _rate = sampleRate;
        _channels = channels;
        _bits = bitsPerSample;
        _float = isFloat;
    }

    private double Sample(ReadOnlySpan<byte> buffer, int frame)
    {
        double sum = 0;
        int step = _bits / 8;
        for (int c = 0; c < _channels; c++)
        {
            int at = (frame * _channels + c) * step;
            sum += _float ? BitConverter.ToSingle(buffer.Slice(at, 4)) : BitConverter.ToInt16(buffer.Slice(at, 2)) / 32768.0;
        }
        return sum / _channels;
    }

    public byte[] Convert(ReadOnlySpan<byte> buffer)
    {
        int frameBytes = _channels * (_bits / 8);
        int frames = buffer.Length / frameBytes;
        if (frames == 0) return Array.Empty<byte>();

        double ratio = (double)_rate / TargetRate;
        var output = new List<byte>((int)(frames / ratio * 2) + 4);
        // _position é medida em quadros do bloco atual; -1 significa "entre o último quadro do bloco anterior e o primeiro deste"
        while (_position <= frames - 1)
        {
            int i0 = (int)Math.Floor(_position);
            double t = _position - i0;
            double a = i0 < 0 ? (_hasCarry ? _carry : Sample(buffer, 0)) : Sample(buffer, i0);
            double b = i0 + 1 < frames ? Sample(buffer, i0 + 1) : a;
            double value = a + (b - a) * t;
            short pcm = (short)Math.Clamp(value * 32767.0, short.MinValue, short.MaxValue);
            output.Add((byte)(pcm & 0xFF));
            output.Add((byte)((pcm >> 8) & 0xFF));
            _position += ratio;
        }

        _carry = Sample(buffer, frames - 1);
        _hasCarry = true;
        _position -= frames;
        return output.ToArray();
    }
}
