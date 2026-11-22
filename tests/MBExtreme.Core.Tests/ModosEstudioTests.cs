using MBExtreme.Core.Recording;
using Xunit;

namespace MBExtreme.Core.Tests;

public class ModosEstudioTests
{
    private static readonly EncoderOption H264 = new(VideoCodec.H264, EncoderKind.Nvenc, "h264_nvenc");
    private static readonly EncoderOption Hevc = new(VideoCodec.Hevc, EncoderKind.Nvenc, "hevc_nvenc");
    private static readonly EncoderOption Av1 = new(VideoCodec.Av1, EncoderKind.Nvenc, "av1_nvenc");
    private static readonly EncoderOption Qsv = new(VideoCodec.H264, EncoderKind.Qsv, "h264_qsv");

    [Fact]
    public void Live_usa_o_melhor_codificador_que_a_plataforma_aceita()
    {
        var all = new[] { Qsv, H264, Hevc, Av1 };
        Assert.Equal("h264_nvenc", StudioModes.ForLive(StudioMode.Balanced, all, youTubeOnly: true, 51).Encoder.FfmpegName);   // NVIDIA antes da Intel
        Assert.Equal("av1_nvenc", StudioModes.ForLive(StudioMode.Ultra, all, youTubeOnly: true, 51).Encoder.FfmpegName);
        Assert.Equal(1440, StudioModes.ForLive(StudioMode.Ultra, all, youTubeOnly: true, 51).Height);
        var twitch = StudioModes.ForLive(StudioMode.Ultra, all, youTubeOnly: false, 6);
        Assert.Equal(("h264_nvenc", 1080, 6), (twitch.Encoder.FfmpegName, twitch.Height, twitch.Mbps));
        Assert.Equal((720, 30), (StudioModes.ForLive(StudioMode.Light, all, false, 6).Height, StudioModes.ForLive(StudioMode.Light, all, false, 6).Fps));
    }

    [Fact]
    public void Gravacao_prefere_hevc_e_av1()
    {
        var all = new[] { H264, Hevc, Av1 };
        Assert.Equal("h264_nvenc", StudioModes.ForRecording(StudioMode.Light, all, 1440).Encoder.FfmpegName);
        Assert.Equal("hevc_nvenc", StudioModes.ForRecording(StudioMode.Quality, all, 1440).Encoder.FfmpegName);
        Assert.Equal(("av1_nvenc", 2160), (StudioModes.ForRecording(StudioMode.Ultra, all, 2160).Encoder.FfmpegName, StudioModes.ForRecording(StudioMode.Ultra, all, 2160).Height));
        Assert.Equal("h264_qsv", StudioModes.ForRecording(StudioMode.Ultra, new[] { Qsv }, 1080).Encoder.FfmpegName);
        Assert.Equal(4, StudioModes.All.Count);
    }
}
