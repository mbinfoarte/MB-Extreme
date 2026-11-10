using MBExtreme.Core.Recording;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>AV1 primeiro nas placas novas (RTX 40/50, RX 7000/9000); a live só usa o que a plataforma aceita.</summary>
public class CodificadorAv1Tests
{
    private static EncoderOption E(VideoCodec c, EncoderKind k, string n) => new(c, k, n);

    private static readonly EncoderOption H264Nv = E(VideoCodec.H264, EncoderKind.Nvenc, "h264_nvenc");
    private static readonly EncoderOption HevcNv = E(VideoCodec.Hevc, EncoderKind.Nvenc, "hevc_nvenc");
    private static readonly EncoderOption Av1Nv = E(VideoCodec.Av1, EncoderKind.Nvenc, "av1_nvenc");
    private static readonly EncoderOption H264Amf = E(VideoCodec.H264, EncoderKind.Amf, "h264_amf");
    private static readonly EncoderOption Av1Amf = E(VideoCodec.Av1, EncoderKind.Amf, "av1_amf");
    private static readonly EncoderOption H264Qsv = E(VideoCodec.H264, EncoderKind.Qsv, "h264_qsv");
    private static readonly EncoderOption X264 = E(VideoCodec.H264, EncoderKind.Software, "libx264");

    [Fact]
    public void RTX_40_grava_em_AV1_NVENC()
    {
        var rtx40 = new[] { H264Nv, HevcNv, Av1Nv, H264Qsv, X264 };   // ordem do teste da placa
        Assert.Equal("av1_nvenc", rtx40[EncoderChoice.BestIndex(rtx40)].FfmpegName);
    }

    [Fact]
    public void RTX_30_sem_AV1_fica_no_HEVC_e_placa_antiga_no_H264()
    {
        Assert.Equal("hevc_nvenc", new[] { H264Nv, HevcNv }[EncoderChoice.BestIndex(new[] { H264Nv, HevcNv })].FfmpegName);
        Assert.Equal(0, EncoderChoice.BestIndex(new[] { H264Nv, X264 }));
        Assert.Equal(-1, EncoderChoice.BestIndex(Array.Empty<EncoderOption>()));
    }

    [Fact]
    public void AMD_nova_usa_AV1_AMF_e_o_processador_fica_por_ultimo()
    {
        var rx7000 = new[] { X264, H264Amf, Av1Amf };
        Assert.Equal("av1_amf", rx7000[EncoderChoice.BestIndex(rx7000)].FfmpegName);
        Assert.Equal(1, EncoderChoice.BestIndex(new[] { X264, H264Qsv }));
    }

    [Fact]
    public void Live_troca_para_um_codec_que_a_plataforma_aceita_no_mesmo_fabricante()
    {
        var all = new[] { H264Qsv, H264Nv, HevcNv, Av1Nv };
        Assert.Same(Av1Nv, EncoderChoice.ForStream(all, Av1Nv, StreamService.YouTube));    // YouTube recebe AV1 (RTMP aprimorado)
        Assert.Same(H264Nv, EncoderChoice.ForStream(all, Av1Nv, StreamService.Twitch));    // Twitch: H.264, mas continua na NVIDIA
        Assert.Same(H264Nv, EncoderChoice.ForStream(all, HevcNv, StreamService.Facebook));
        Assert.Same(H264Qsv, EncoderChoice.ForStream(new[] { H264Qsv, Av1Amf }, Av1Amf, StreamService.Twitch));   // AMD sem H.264: o melhor que serve
        Assert.Null(EncoderChoice.ForStream(new[] { Av1Nv }, Av1Nv, StreamService.Twitch));
    }

    [Fact]
    public void Live_leve_no_youtube_usa_AV1_e_na_twitch_H264()
    {
        var rtx40 = new[] { H264Nv, HevcNv, Av1Nv };
        Assert.Equal(2, LiveLight.Plan(rtx40, 4, StreamService.YouTube, 1080, 60).EncoderIndex);
        Assert.Equal(0, LiveLight.Plan(rtx40, 4, StreamService.Twitch, 1080, 60).EncoderIndex);
    }
}
