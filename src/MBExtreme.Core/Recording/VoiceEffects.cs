using NAudio.Dsp;
using NAudio.Wave;

namespace MBExtreme.Core.Recording;

/// <summary>Tratamento da voz do microfone em tempo real. Não é inteligência artificial (como o RTX Voice): usa filtro de graves, porta de ruído, equalização e compressor.</summary>
public enum VoiceEffect
{
    /// <summary>Sem tratamento.</summary>
    Off,

    /// <summary>Corta graves de ventilador e ronco e fecha o som entre as falas (teclado, ar-condicionado, chiado).</summary>
    NoiseReduction,

    /// <summary>Reduz ruído e ainda deixa a voz mais clara e encorpada (realce de presença e compressor), como num estúdio.</summary>
    StudioVoice,
}

/// <summary>Processa áudio estéreo em ponto flutuante. A mudança de efeito vale na hora, sem reiniciar nada.</summary>
public sealed class VoiceProcessor : ISampleProvider
{
    private const float GateOpenLevel = 0.012f;     // ≈ -38 dB
    private const float GateCloseLevel = 0.007f;    // ≈ -43 dB (histerese: não fica abrindo e fechando)
    private const float ClosedGain = 0.08f;         // ≈ -22 dB: abaixa em vez de calar de vez, para soar natural
    private const float CompressorThreshold = 0.126f; // ≈ -18 dB
    private const float CompressorRatio = 3f;

    private readonly ISampleProvider _source;
    private readonly int _rate;
    private readonly BiQuadFilter[] _highPass = new BiQuadFilter[2];
    private readonly BiQuadFilter[] _presence = new BiQuadFilter[2];
    private readonly BiQuadFilter[] _body = new BiQuadFilter[2];
    private readonly float _attack, _release, _holdSamples, _compAttack, _compRelease;
    private float _envelope, _gateGain = 1f, _compGain = 1f;
    private bool _open = true;
    private int _hold;
    private volatile VoiceEffect _effect;

    public VoiceProcessor(ISampleProvider source)
    {
        _source = source;
        _rate = source.WaveFormat.SampleRate;
        for (int c = 0; c < 2; c++)
        {
            _highPass[c] = BiQuadFilter.HighPassFilter(_rate, 90, 0.707f);
            _presence[c] = BiQuadFilter.PeakingEQ(_rate, 3500, 0.9f, 3.5f);
            _body[c] = BiQuadFilter.PeakingEQ(_rate, 180, 0.8f, 2f);
        }
        _attack = Coefficient(0.004f);
        _release = Coefficient(0.12f);
        _compAttack = Coefficient(0.01f);
        _compRelease = Coefficient(0.15f);
        _holdSamples = 0.12f * _rate;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public VoiceEffect Effect
    {
        get => _effect;
        set => _effect = value;
    }

    private float Coefficient(float seconds) => 1f - MathF.Exp(-1f / (seconds * _rate));

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        var effect = _effect;
        if (effect == VoiceEffect.Off)
        {
            _gateGain = 1f;
            _compGain = 1f;
            _open = true;
            return read;
        }

        bool studio = effect == VoiceEffect.StudioVoice;
        for (int i = 0; i + 1 < read; i += 2)
        {
            float l = _highPass[0].Transform(buffer[offset + i]);
            float r = _highPass[1].Transform(buffer[offset + i + 1]);
            if (studio)
            {
                l = _presence[0].Transform(_body[0].Transform(l));
                r = _presence[1].Transform(_body[1].Transform(r));
            }

            // porta de ruído: acompanha o volume da voz e abre ou fecha com histerese e um pouco de espera
            float level = Math.Max(Math.Abs(l), Math.Abs(r));
            _envelope = level > _envelope ? level : _envelope + (level - _envelope) * _release;
            if (_envelope >= GateOpenLevel) { _open = true; _hold = (int)_holdSamples; }
            else if (_open && _envelope < GateCloseLevel)
            {
                if (_hold > 0) _hold--;
                else _open = false;
            }
            float target = _open ? 1f : ClosedGain;
            _gateGain += (target - _gateGain) * (target > _gateGain ? _attack : _release);
            l *= _gateGain;
            r *= _gateGain;

            if (studio)
            {
                // compressor: segura os picos da voz e levanta o que ficou baixo
                float peak = Math.Max(Math.Abs(l), Math.Abs(r));
                float wanted = 1f;
                if (peak > CompressorThreshold) wanted = (CompressorThreshold + (peak - CompressorThreshold) / CompressorRatio) / peak;
                _compGain += (wanted - _compGain) * (wanted < _compGain ? _compAttack : _compRelease);
                const float makeup = 1.35f;
                l = Limit(l * _compGain * makeup);
                r = Limit(r * _compGain * makeup);
            }

            buffer[offset + i] = l;
            buffer[offset + i + 1] = r;
        }
        return read;
    }

    /// <summary>Limitador suave: nada passa de ~0,95, sem estalo.</summary>
    private static float Limit(float v)
    {
        float a = Math.Abs(v);
        if (a <= 0.8f) return v;
        float limited = 0.8f + 0.15f * MathF.Tanh((a - 0.8f) / 0.15f);
        return v < 0 ? -limited : limited;
    }
}
