using NAudio.Dsp;
using NAudio.Wave;

namespace MBExtreme.Core.Recording;

public static class Db
{
    public static float ToLinear(double db) => (float)Math.Pow(10, db / 20.0);

    public static double ToDb(double linear) => linear <= 1e-9 ? -180 : 20 * Math.Log10(linear);
}

/// <summary>
/// Tratamento de áudio profissional, aplicado ao vivo (sem reiniciar nada): filtro de graves, porta de ruído, compressor e limitador de volume em dB.
/// O limitador fica no som final (tudo que vai para a gravação e para a live); o resto trata só o microfone.
/// </summary>
public sealed record AudioProcessing(
    bool HighPass, bool Gate, double GateDb, bool Compressor, double CompressorDb, double CompressorRatio, double MakeupDb,
    bool Limiter, double CeilingDb)
{
    /// <summary>Padrão seguro: só o limitador em -1 dB (evita estourar o áudio), nada mais.</summary>
    public static AudioProcessing Safe { get; } = new(false, false, -45, false, -18, 3, 0, true, -1);

    /// <summary>Voz limpa para live: corta graves, fecha o ruído entre as falas, nivela a voz e limita em -2 dB.</summary>
    public static AudioProcessing CleanVoice { get; } = new(true, true, -45, true, -20, 3, 4, true, -2);

    /// <summary>Podcast / voz forte e constante: compressão mais firme e mais ganho.</summary>
    public static AudioProcessing Podcast { get; } = new(true, true, -42, true, -24, 4, 6, true, -1.5);

    public AudioProcessing Clamped() => this with
    {
        GateDb = Math.Clamp(GateDb, -70, -20),
        CompressorDb = Math.Clamp(CompressorDb, -40, -6),
        CompressorRatio = Math.Clamp(CompressorRatio, 1.5, 10),
        MakeupDb = Math.Clamp(MakeupDb, 0, 12),
        CeilingDb = Math.Clamp(CeilingDb, -12, 0),
    };
}

/// <summary>Limitador de pico: nada passa do teto. Ataque instantâneo (sem estourar), soltura suave (sem "bombear").</summary>
public sealed class PeakLimiter
{
    private float _gain = 1f;
    private readonly float _release;

    public PeakLimiter(int sampleRate) => _release = 1f - MathF.Exp(-1f / (0.08f * sampleRate));

    /// <summary>Redução aplicada agora, em dB (0 = sem limitar).</summary>
    public double ReductionDb => -Db.ToDb(_gain);

    public void Process(ref float l, ref float r, float ceiling)
    {
        float peak = Math.Max(Math.Abs(l), Math.Abs(r));
        float target = peak > ceiling ? ceiling / peak : 1f;
        if (target < _gain) _gain = target;
        else _gain += (target - _gain) * _release;
        l *= _gain;
        r *= _gain;
        // arredondamentos: garante o teto
        if (Math.Abs(l) > ceiling) l = Math.Sign(l) * ceiling;
        if (Math.Abs(r) > ceiling) r = Math.Sign(r) * ceiling;
    }
}

/// <summary>Filtro de graves, porta de ruído e compressor do microfone, em estéreo. Muda na hora.</summary>
public sealed class AudioChainProcessor : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _rate;
    private readonly BiQuadFilter[] _highPass = new BiQuadFilter[2];
    private readonly float _gateAttack, _gateRelease, _compAttack, _compRelease, _holdSamples;
    private float _envelope, _gateGain = 1f, _compGain = 1f;
    private int _hold;
    private bool _open = true;
    private volatile AudioProcessing _settings = AudioProcessing.Safe;

    public AudioChainProcessor(ISampleProvider source)
    {
        _source = source;
        _rate = source.WaveFormat.SampleRate;
        for (int c = 0; c < 2; c++) _highPass[c] = BiQuadFilter.HighPassFilter(_rate, 85, 0.707f);
        _gateAttack = Coef(0.004f);
        _gateRelease = Coef(0.15f);
        _compAttack = Coef(0.008f);
        _compRelease = Coef(0.2f);
        _holdSamples = 0.1f * _rate;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public AudioProcessing Settings
    {
        get => _settings;
        set => _settings = value.Clamped();
    }

    private float Coef(float seconds) => 1f - MathF.Exp(-1f / (seconds * _rate));

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        var s = _settings;
        if (!s.HighPass && !s.Gate && !s.Compressor) { _gateGain = _compGain = 1f; _open = true; return read; }

        float gateOpen = Db.ToLinear(s.GateDb), gateClose = Db.ToLinear(s.GateDb - 5);
        float threshold = Db.ToLinear(s.CompressorDb), makeup = Db.ToLinear(s.Compressor ? s.MakeupDb : 0), ratio = (float)s.CompressorRatio;
        for (int i = 0; i + 1 < read; i += 2)
        {
            float l = buffer[offset + i], r = buffer[offset + i + 1];
            if (s.HighPass) { l = _highPass[0].Transform(l); r = _highPass[1].Transform(r); }

            if (s.Gate)
            {
                float level = Math.Max(Math.Abs(l), Math.Abs(r));
                _envelope = level > _envelope ? level : _envelope + (level - _envelope) * _gateRelease;
                if (_envelope >= gateOpen) { _open = true; _hold = (int)_holdSamples; }
                else if (_open && _envelope < gateClose) { if (_hold > 0) _hold--; else _open = false; }
                float target = _open ? 1f : 0.05f;   // fechada = -26 dB: natural, sem cortar seco
                _gateGain += (target - _gateGain) * (target > _gateGain ? _gateAttack : _gateRelease);
                l *= _gateGain; r *= _gateGain;
            }
            else _gateGain = 1f;

            if (s.Compressor)
            {
                float peak = Math.Max(Math.Abs(l), Math.Abs(r));
                float wanted = peak > threshold ? (threshold + (peak - threshold) / ratio) / peak : 1f;
                _compGain += (wanted - _compGain) * (wanted < _compGain ? _compAttack : _compRelease);
                l *= _compGain * makeup; r *= _compGain * makeup;
            }
            else _compGain = 1f;

            buffer[offset + i] = l;
            buffer[offset + i + 1] = r;
        }
        return read;
    }
}
