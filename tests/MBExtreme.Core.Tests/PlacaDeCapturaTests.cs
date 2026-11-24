using MBExtreme.Core.Recording;
using Xunit;

namespace MBExtreme.Core.Tests;

public class PlacaDeCapturaTests
{
    private static readonly EncoderOption Nvenc = new(VideoCodec.H264, EncoderKind.Nvenc, "h264_nvenc");
    private static readonly CaptureSource Placa = new(SourceKind.CaptureCard, 0, new PixelRect(0, 0, 1920, 1080), "Placa de captura: Elgato HD60 X", "Elgato HD60 X");

    [Fact]
    public void Placa_vira_a_tela_principal_pelo_dshow()
    {
        CameraSource.CardModes["Elgato HD60 X"] = new CameraMode(1920, 1080, 60, null, "nv12");
        var r = new StudioRequest { Source = Placa, OutputWidth = 1920, OutputHeight = 1080, Fps = 60, Encoder = Nvenc, BitrateMbps = 12, WorkingDirectory = "/tmp/x", RecordFile = "grav.mkv" };
        Assert.Null(StudioCommand.Validate(r));
        var args = StudioCommand.Build(r).ToList();
        int i = args.IndexOf("video=Elgato HD60 X");
        Assert.True(i > 0);
        Assert.Equal("dshow", args[args.LastIndexOf("-f", i) + 1]);
        Assert.Equal("60", args[args.IndexOf("-framerate") + 1]);
        Assert.DoesNotContain(args, a => a.Contains("ddagrab"));
        Assert.DoesNotContain(args, a => a.Contains("hwdownload"));
        Assert.Equal("card:Elgato HD60 X", StudioCommand.CaptureGroupKey(Placa));
    }

    [Fact]
    public void Nao_deixa_a_placa_como_camera_e_tela_ao_mesmo_tempo()
    {
        var r = new StudioRequest
        {
            Source = Placa, OutputWidth = 1920, OutputHeight = 1080, Fps = 60, Encoder = Nvenc, BitrateMbps = 12, WorkingDirectory = "/tmp/x", RecordFile = "grav.mkv",
            Cameras = new[] { new CameraOverlay("Elgato HD60 X", CameraCorner.BottomRight, 25, true) },
        };
        Assert.Contains("câmera", StudioCommand.Validate(r));
    }

    [Fact]
    public void Escolhe_o_maior_modo_com_mais_quadros()
    {
        var modes = new[]
        {
            new CameraMode(1920, 1080, 30, "mjpeg", null), new CameraMode(1920, 1080, 60, null, "nv12"), new CameraMode(3840, 2160, 30, null, "nv12"), new CameraMode(1280, 720, 60, null, "yuyv422"),
        };
        var best = CameraModes.PickCard(modes, 1080)!;
        Assert.Equal((1920, 1080, 60), (best.Width, best.Height, best.CardFps));
        Assert.Equal(3840, CameraModes.PickCard(modes, 2160)!.Width);
    }
}

public class VariasPlataformasTests
{
    private static readonly EncoderOption Nvenc = new(VideoCodec.H264, EncoderKind.Nvenc, "h264_nvenc");
    private static readonly CaptureSource Tela = new(SourceKind.Monitor, 0, new PixelRect(0, 0, 1920, 1080), "Tela 1");

    [Fact]
    public void Envia_o_mesmo_video_para_cada_plataforma()
    {
        var r = new StudioRequest
        {
            Source = Tela, OutputWidth = 1920, OutputHeight = 1080, Fps = 60, Encoder = Nvenc, BitrateMbps = 6, WorkingDirectory = "/tmp/x",
            Stream = new StreamTarget(StreamService.YouTube, "", "abcd-efgh-ijkl-mnop"),
            ExtraStreams = new[] { new StreamTarget(StreamService.Twitch, "", "live_123456_abcdef") },
        };
        Assert.Null(StudioCommand.Validate(r));
        var args = StudioCommand.Build(r).ToList();
        string tee = args[args.IndexOf("tee") + 1];
        Assert.Contains("a.rtmp.youtube.com/live2/abcd-efgh-ijkl-mnop", tee);
        Assert.Contains("live.twitch.tv/app/live_123456_abcdef", tee);
        Assert.Equal(1, args.Count(a => a == "-c:v"));   // codifica uma vez só
        Assert.NotNull(StudioCommand.Validate(r with { ExtraStreams = new[] { new StreamTarget(StreamService.Twitch, "", "") } }));
        Assert.NotNull(StudioCommand.Validate(r with { ExtraStreams = new[] { r.Stream! } }));
    }
}
