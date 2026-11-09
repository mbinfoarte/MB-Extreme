using MBExtreme.Core.Recording;
using NAudio.Wave;
using Xunit;

namespace MBExtreme.Core.Tests;

public class EstudioVozImagemTests
{
    private sealed class Tone : ISampleProvider
    {
        private readonly Func<int, float> _f;
        private int _n;
        public Tone(Func<int, float> f) => _f = f;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public int Read(float[] buffer, int offset, int count)
        {
            for (int i = 0; i + 1 < count; i += 2) { float v = _f(_n++); buffer[offset + i] = v; buffer[offset + i + 1] = v; }
            return count;
        }
    }

    private static float[] Run(Func<int, float> signal, VoiceEffect effect, int frames = 48000)
    {
        var processor = new VoiceProcessor(new Tone(signal)) { Effect = effect };
        var buffer = new float[frames * 2];
        processor.Read(buffer, 0, buffer.Length);
        return buffer;
    }

    private static float Rms(float[] data, int from, int to)
    {
        double sum = 0;
        for (int i = from; i < to; i++) sum += data[i] * data[i];
        return (float)Math.Sqrt(sum / (to - from));
    }

    private static float Sine(int n, double hz, double amp) => (float)(amp * Math.Sin(2 * Math.PI * hz * n / 48000));

    [Fact]
    public void Sem_efeito_o_audio_passa_igual()
    {
        var output = Run(n => Sine(n, 440, 0.3), VoiceEffect.Off, 4800);
        for (int i = 0; i < output.Length; i += 2) Assert.Equal(Sine(i / 2, 440, 0.3), output[i], 5);
    }

    [Fact]
    public void Ruido_baixo_constante_e_abaixado_e_voz_forte_passa()
    {
        var rng = new Random(7);
        float Noise(int n) => (float)((rng.NextDouble() - 0.5) * 0.012);          // chiado bem baixo
        var quiet = Run(Noise, VoiceEffect.NoiseReduction, 48000);
        var raw = Run(n => (float)((new Random(n).NextDouble() - 0.5) * 0.012), VoiceEffect.Off, 48000);
        Assert.True(Rms(quiet, 48000, 96000) < Rms(raw, 48000, 96000) * 0.3f);     // depois de 0,5 s a porta fechou

        var voice = Run(n => Sine(n, 300, 0.4), VoiceEffect.NoiseReduction, 48000);
        Assert.True(Rms(voice, 48000, 96000) > 0.4f * 0.707f * 0.85f);              // voz quase igual
    }

    [Fact]
    public void Filtro_de_graves_corta_o_ronco()
    {
        var hum = Run(n => Sine(n, 40, 0.5), VoiceEffect.NoiseReduction, 48000);
        Assert.True(Rms(hum, 48000, 96000) < 0.5f * 0.707f * 0.4f);
    }

    [Fact]
    public void Voz_de_estudio_nunca_estoura()
    {
        var loud = Run(n => Sine(n, 500, 1.0) + Sine(n, 2500, 0.8), VoiceEffect.StudioVoice, 48000);
        Assert.All(loud, v => Assert.InRange(v, -1f, 1f));
        // o compressor deixa o volume mais parecido entre fala baixa e alta
        var low = Run(n => Sine(n, 500, 0.08), VoiceEffect.StudioVoice, 48000);
        var high = Run(n => Sine(n, 500, 0.6), VoiceEffect.StudioVoice, 48000);
        float ratioIn = 0.6f / 0.08f;
        float ratioOut = Rms(high, 48000, 96000) / Rms(low, 48000, 96000);
        Assert.True(ratioOut < ratioIn);
    }

    [Fact]
    public void Trocar_o_efeito_na_hora_funciona()
    {
        var processor = new VoiceProcessor(new Tone(n => Sine(n, 300, 0.3)));
        var buffer = new float[9600];
        processor.Read(buffer, 0, buffer.Length);
        processor.Effect = VoiceEffect.StudioVoice;
        processor.Read(buffer, 0, buffer.Length);
        processor.Effect = VoiceEffect.Off;
        processor.Read(buffer, 0, buffer.Length);
        Assert.Equal(VoiceEffect.Off, processor.Effect);
    }

    // ---- vinheta e luz

    [Fact]
    public void Vinheta_entra_no_filtro_e_nao_e_neutra()
    {
        var none = new ImageFilter();
        Assert.True(none.IsNeutral);
        var v = new ImageFilter(Vignette: 2);
        Assert.False(v.IsNeutral);
        string chain = v.ToChain()!;
        Assert.Contains("format=yuv420p", chain);
        Assert.Contains("vignette=angle=0.75", chain);
        Assert.EndsWith("format=bgra", chain);
        Assert.Contains("vignette=angle=0.55", new ImageFilter(Vignette: 3).ToChain());
        Assert.Equal(3, new ImageFilter(Vignette: 9).Clamped().Vignette);
    }

    [Fact]
    public void Novas_predefinicoes_ficam_no_fim_para_nao_trocar_as_salvas()
    {
        var names = ImageFilter.Presets.Select(p => p.Name).ToList();
        Assert.Equal(new[] { "Original", "Natural", "Vívida", "Cinema", "Nítida" }, names.Take(5));
        Assert.Contains("Luz de estúdio", names);
        Assert.Contains("Retrato", names);
        Assert.True(ImageFilter.Presets.Single(p => p.Name == "Retrato").Filter.Vignette > 0);
    }
}
