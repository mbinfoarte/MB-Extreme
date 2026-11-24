using MBExtreme.Core.Recording;
using Xunit;

namespace MBExtreme.Core.Tests;

public class LiveVerticalTests
{
    private static readonly EncoderOption Nvenc = new(VideoCodec.H264, EncoderKind.Nvenc, "h264_nvenc");

    private static VerticalLayout Layout() =>
        new(1080, 1920, true, 55, new VerticalPane(PaneKind.Screen, PaneFit.Fit), new VerticalPane(PaneKind.Screen, PaneFit.Fill, 0, 100, 250), true);

    private static StudioRequest Dual(bool record = true) => new()
    {
        Source = new CaptureSource(SourceKind.Monitor, 0, new PixelRect(0, 0, 1920, 1080), "Tela 1"),
        OutputWidth = 1920, OutputHeight = 1080, Fps = 60, Encoder = Nvenc, BitrateMbps = 6, WorkingDirectory = "/tmp/x",
        RecordFile = record ? "grav.mkv" : null,
        Stream = new StreamTarget(StreamService.YouTube, "", "yt-key-1234"),
        VerticalCopy = VerticalLive.Fit(Layout(), VerticalPlatform.Instagram),
        VerticalFile = record ? "grav-vertical.mkv" : null,
        VerticalStream = VerticalLive.Target(VerticalPlatform.Instagram, null, "IG-chave-5678"),
        VerticalStreamKbps = 3500,
        VerticalFps = 30,
    };

    [Fact]
    public void Live_dupla_manda_a_horizontal_e_a_vertical_para_servidores_diferentes()
    {
        var r = Dual();
        Assert.Null(StudioCommand.Validate(r));
        var args = StudioCommand.Build(r).ToList();
        string joined = string.Join(" ", args);
        Assert.Contains("rtmp://a.rtmp.youtube.com/live2/yt-key-1234", joined);
        Assert.Contains("[f=flv:flvflags=no_duration_filesize:onfail=ignore]rtmps://live-upload.instagram.com:443/rtmp/IG-chave-5678", joined);
        Assert.Contains("[f=matroska]grav-vertical.mkv|", joined);
        Assert.Contains("fps=30,format=nv12[vv]", joined);
        int vv = args.IndexOf("[vv]");
        Assert.Equal("3500k", args[args.IndexOf("-b:v", vv) + 1]);
        Assert.Equal("3500k", args[args.IndexOf("-bufsize", vv) + 1]);
        Assert.Equal("60", args[args.IndexOf("-g", vv) + 1]);   // quadro-chave a cada 2 s em 30 fps
        Assert.Equal(720, r.VerticalCopy!.Width);
    }

    [Fact]
    public void Live_vertical_sem_gravar_nao_cria_arquivo()
    {
        var r = Dual(record: false);
        Assert.Null(StudioCommand.Validate(r));
        string joined = string.Join(" ", StudioCommand.Build(r));
        Assert.DoesNotContain("grav-vertical", joined);
        Assert.Contains("live-upload.instagram.com", joined);
    }

    [Fact]
    public void Chave_da_live_vertical_nunca_aparece_nas_mensagens()
    {
        var r = Dual();
        string masked = StudioCommand.MaskSecrets("erro em rtmps://live-upload.instagram.com:443/rtmp/IG-chave-5678 e yt-key-1234", r.Stream, r.VerticalStream);
        Assert.DoesNotContain("IG-chave-5678", masked);
        Assert.DoesNotContain("yt-key-1234", masked);
        Assert.DoesNotContain("IG-chave-5678", StudioCommand.Describe(r));
    }

    [Fact]
    public void Problemas_da_live_vertical_sao_explicados()
    {
        Assert.Contains("live vertical", StudioCommand.Validate(Dual() with { VerticalStream = new StreamTarget(StreamService.Custom, "http://x", "k") }));
        Assert.Contains("caracteres", StudioCommand.Validate(Dual() with { VerticalStream = VerticalLive.Target(VerticalPlatform.Instagram, null, "chave com espaço") }));
        Assert.Contains("algum lugar", StudioCommand.Validate(Dual(record: false) with { VerticalStream = null }));
        Assert.Contains("H.264", StudioCommand.Validate(Dual() with { Encoder = new EncoderOption(VideoCodec.Hevc, EncoderKind.Nvenc, "hevc_nvenc"), Stream = null }));
    }

    [Fact]
    public void Plataformas()
    {
        Assert.Equal("rtmps://live-upload.instagram.com:443/rtmp/abc", VerticalLive.Target(VerticalPlatform.Instagram, " ", "abc").FullUrl);
        Assert.Equal("rtmp://meu.servidor/live/abc", VerticalLive.Target(VerticalPlatform.TikTok, "rtmp://meu.servidor/live", "abc").FullUrl);
        Assert.Equal(1080, VerticalLive.Fit(Layout(), VerticalPlatform.YouTube).Width);
        Assert.Equal(720, VerticalLive.Fit(Layout(), VerticalPlatform.TikTok).Width);
        Assert.All(VerticalLive.All, s => Assert.False(string.IsNullOrWhiteSpace(s.HowToKey)));
    }
}
