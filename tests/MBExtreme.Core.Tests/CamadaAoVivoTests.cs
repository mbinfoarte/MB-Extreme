using MBExtreme.Core.Recording;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>Camada desenhada pelo programa (logo) que entra na live por cano: posição, tamanho e o filtro do FFmpeg.</summary>
public class CamadaAoVivoTests
{
    private static readonly EncoderOption Nvenc = new(VideoCodec.H264, EncoderKind.Nvenc, "h264_nvenc");
    private static readonly CaptureSource Tela1 = new(SourceKind.Monitor, 0, new PixelRect(0, 0, 1920, 1080), "Tela 1");
    private static readonly LiveOverlayInput Camada = new(@"\\.\pipe\mbx-overlay-teste", 1920, 1080, 10);

    private static string Graph(StudioRequest r)
    {
        var args = StudioCommand.Build(r);
        return args[args.ToList().IndexOf("-filter_complex") + 1];
    }

    [Fact]
    public void Camada_entra_por_cima_de_tudo_numa_cena_so()
    {
        var r = new StudioRequest
        {
            Source = Tela1, OutputWidth = 1920, OutputHeight = 1080, Fps = 60, Encoder = Nvenc, BitrateMbps = 16, WorkingDirectory = "/tmp/x",
            RecordFile = "grav.mkv", LiveOverlay = Camada,
        };
        var args = StudioCommand.Build(r).ToList();
        int pipe = args.IndexOf(Camada.PipePath);
        Assert.True(pipe > 0);
        Assert.Equal("bgra", args[args.LastIndexOf("-pix_fmt", pipe)+1]);
        string g = Graph(r);
        Assert.Contains("[1:v]format=bgra,scale=1920:1080:flags=bilinear[lo];[b0][lo]overlay=0:0:format=auto:eof_action=repeat:shortest=0[b1]", g);
        Assert.EndsWith("[b1]format=nv12[v]", g);
    }

    [Fact]
    public void Camada_entra_depois_do_seletor_de_cenas()
    {
        var r = new StudioRequest
        {
            Source = Tela1, OutputWidth = 1920, OutputHeight = 1080, Fps = 60, Encoder = Nvenc, BitrateMbps = 16, WorkingDirectory = "/tmp/x",
            RecordFile = "grav.mkv", LiveOverlay = Camada,
            SceneCaptures = new[] { Tela1, Tela1 },
            Scenes = new[] { new SceneLayout("A", 0, Array.Empty<int>(), false, Array.Empty<int>()), new SceneLayout("B", 1, Array.Empty<int>(), false, Array.Empty<int>()) },
        };
        string g = Graph(r);
        Assert.Contains("[sel][lo]overlay=0:0:format=auto:eof_action=repeat:shortest=0[selo];[selo]format=nv12[v]", g);
    }

    [Theory]
    [InlineData(0, 24, 24)]
    [InlineData(2, 1920 - 24 - 220, 24)]
    [InlineData(4, (1920 - 220) / 2, (1080 - 110) / 2)]
    [InlineData(8, 1920 - 24 - 220, 1080 - 24 - 110)]
    public void Nove_pontos_no_1080p(int anchor, int x, int y)
    {
        var (rx, ry, w, h) = new LogoPlacement(anchor, 220, 24, 100).Rect(1920, 1080, 400, 200);
        Assert.Equal((x, y, 220, 110), (rx, ry, w, h));
    }

    [Fact]
    public void Medidas_acompanham_o_tamanho_do_video_e_ficam_dentro_do_quadro()
    {
        var (x, y, w, h) = new LogoPlacement(8, 220, 24, 100).Rect(3840, 2160, 400, 200);
        Assert.Equal((440, 220), (w, h));
        Assert.Equal((3840 - 48 - 440, 2160 - 48 - 220), (x, y));
        var livre = new LogoPlacement(0, 220, 24, 100, 0.99, 0.01).Rect(1920, 1080, 400, 200);
        Assert.Equal((1920 - 220, 0), (livre.X, livre.Y));
    }

    [Fact]
    public void Canto_antigo_vira_ponto_e_largura()
    {
        var p = LogoPlacement.FromCorner(3, 10);
        Assert.Equal((8, 192), (p.Anchor, p.WidthPx));
    }

    [Fact]
    public void Tira_a_pre_multiplicacao()
    {
        byte[] px = { 64, 32, 0, 128, 10, 10, 10, 0 };
        LiveOverlayCanvas.Unpremultiply(px);
        Assert.Equal(new byte[] { 127, 63, 0, 128, 10, 10, 10, 0 }, px);
    }
}
