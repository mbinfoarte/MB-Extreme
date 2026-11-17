using MBExtreme.Core.Recording;
using NAudio.Wave;
using Xunit;

namespace MBExtreme.Core.Tests;

public class AudioProfissionalTests
{
    private sealed class Tone : ISampleProvider
    {
        private readonly float _amp; private double _phase; private readonly int _rate = 48000;
        public Tone(float amp) => _amp = amp;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public int Read(float[] buffer, int offset, int count)
        {
            for (int i = 0; i + 1 < count; i += 2)
            {
                float v = (float)(Math.Sin(_phase) * _amp);
                _phase += 2 * Math.PI * 440 / _rate;
                buffer[offset + i] = v; buffer[offset + i + 1] = v;
            }
            return count;
        }
    }

    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(-6, 0.501)]
    [InlineData(-20, 0.1)]
    public void Conversao_db_linear(double db, double linear) => Assert.Equal(linear, Db.ToLinear(db), 3);

    [Fact]
    public void Limitador_nunca_deixa_passar_do_teto_mesmo_com_som_estourado()
    {
        var limiter = new PeakLimiter(48000);
        float ceiling = Db.ToLinear(-3);
        float worst = 0;
        for (int i = 0; i < 48000; i++)
        {
            float l = (float)Math.Sin(i * 0.05) * 3f, r = l;   // 3x acima do máximo
            limiter.Process(ref l, ref r, ceiling);
            worst = Math.Max(worst, Math.Max(Math.Abs(l), Math.Abs(r)));
        }
        Assert.True(worst <= ceiling + 1e-6, $"passou do teto: {worst}");
    }

    [Fact]
    public void Limitador_nao_mexe_no_que_esta_abaixo_do_teto()
    {
        var limiter = new PeakLimiter(48000);
        float l = 0.2f, r = -0.2f;
        limiter.Process(ref l, ref r, Db.ToLinear(-1));
        Assert.Equal(0.2f, l, 5);
        Assert.Equal(-0.2f, r, 5);
        Assert.Equal(0, limiter.ReductionDb, 3);
    }

    [Fact]
    public void Porta_de_ruido_abaixa_o_som_fraco_e_deixa_a_voz_forte()
    {
        float Rms(float amp, AudioProcessing p)
        {
            var chain = new AudioChainProcessor(new Tone(amp)) { Settings = p };
            var buf = new float[48000 * 2];
            chain.Read(buf, 0, buf.Length);
            double sum = 0; int n = 0;
            for (int i = buf.Length / 2; i < buf.Length; i++) { sum += buf[i] * buf[i]; n++; }
            return (float)Math.Sqrt(sum / n);
        }
        var gate = AudioProcessing.Safe with { Gate = true, GateDb = -40 };
        Assert.True(Rms(0.002f, gate) < Rms(0.002f, AudioProcessing.Safe) * 0.2f);   // ruído de fundo (-54 dB) cai
        Assert.True(Rms(0.3f, gate) > Rms(0.3f, AudioProcessing.Safe) * 0.95f);       // voz passa
    }

    [Fact]
    public void Compressor_segura_picos_e_o_ganho_sobe_o_nivel_baixo()
    {
        float Peak(float amp, AudioProcessing p)
        {
            var chain = new AudioChainProcessor(new Tone(amp)) { Settings = p };
            var buf = new float[48000 * 2];
            chain.Read(buf, 0, buf.Length);
            return buf.Skip(buf.Length / 2).Max(Math.Abs);
        }
        var comp = AudioProcessing.Safe with { Compressor = true, CompressorDb = -20, CompressorRatio = 4, MakeupDb = 0 };
        Assert.True(Peak(0.9f, comp) < 0.6f);                          // pico alto é segurado
        var makeup = comp with { MakeupDb = 6 };
        Assert.True(Peak(0.05f, makeup) > Peak(0.05f, comp) * 1.8f);  // +6 dB ≈ 2x
    }

    [Fact]
    public void Ajustes_fora_da_faixa_sao_corrigidos_e_os_predefinidos_sao_sensatos()
    {
        var c = new AudioProcessing(true, true, 5, true, 0, 99, 50, true, 5).Clamped();
        Assert.Equal((-20, -6, 10, 12, 0), (c.GateDb, c.CompressorDb, c.CompressorRatio, c.MakeupDb, c.CeilingDb));
        Assert.True(AudioProcessing.Safe.Limiter);
        Assert.True(AudioProcessing.CleanVoice.Gate && AudioProcessing.CleanVoice.Compressor && AudioProcessing.CleanVoice.HighPass);
        Assert.True(AudioProcessing.CleanVoice.CeilingDb < 0);
    }
}
