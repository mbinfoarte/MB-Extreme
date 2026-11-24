using MBExtreme.Core.Recording;
using Xunit;

namespace MBExtreme.Core.Tests;

public class LiveLeveTests
{
    private static EncoderOption E(VideoCodec c, EncoderKind k, string n) => new(c, k, n);

    [Fact]
    public void Melhor_codificador_prefere_nvidia_h264_e_deixa_o_processador_por_ultimo()
    {
        var list = new[] { E(VideoCodec.H264, EncoderKind.Software, "libx264"), E(VideoCodec.Hevc, EncoderKind.Nvenc, "hevc_nvenc"), E(VideoCodec.H264, EncoderKind.Nvenc, "h264_nvenc"), E(VideoCodec.H264, EncoderKind.Amf, "h264_amf") };
        Assert.Equal(2, LiveLight.BestEncoderIndex(list));
        Assert.Equal(1, LiveLight.BestEncoderIndex(new[] { E(VideoCodec.H264, EncoderKind.Software, "libx264"), E(VideoCodec.H264, EncoderKind.Qsv, "h264_qsv") }));
        Assert.Equal(-1, LiveLight.BestEncoderIndex(Array.Empty<EncoderOption>()));
    }

    [Fact]
    public void Plano_leve_limita_resolucao_quadros_e_taxa_conforme_o_pc_e_a_plataforma()
    {
        var gpu = new[] { E(VideoCodec.H264, EncoderKind.Nvenc, "h264_nvenc") };
        var strong = LiveLight.Plan(gpu, 4, StreamService.Twitch, 1440, 60);
        Assert.Equal((1080, 60, 6), (strong.Height, strong.Fps, strong.CapMbps));
        Assert.Equal(0, strong.QualityIndex);

        var weak = LiveLight.Plan(gpu, 1, StreamService.YouTube, 1080, 60);
        Assert.Equal((720, 30), (weak.Height, weak.Fps));

        var fb = LiveLight.Plan(gpu, 4, StreamService.Facebook, 1080, 60);
        Assert.Equal(30, fb.Fps);                  // a plataforma pede 30 fps
        Assert.Contains(strong.Notes, n => n.Contains("NVIDIA NVENC"));
    }

    [Fact]
    public void Sem_codificador_de_placa_avisa_o_motivo()
    {
        var soft = LiveLight.Plan(new[] { E(VideoCodec.H264, EncoderKind.Software, "libx264") }, 3, StreamService.Twitch, 1080, 60);
        Assert.Contains(soft.Notes, n => n.Contains("processador"));
        var none = LiveLight.Plan(Array.Empty<EncoderOption>(), 3, StreamService.Twitch, 1080, 60);
        Assert.Contains(none.Notes, n => n.Contains("Detectar"));
    }
}
