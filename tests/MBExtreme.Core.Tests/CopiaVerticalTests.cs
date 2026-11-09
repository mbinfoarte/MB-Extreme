using MBExtreme.Core.Recording;
using Xunit;

namespace MBExtreme.Core.Tests;

public class CopiaVerticalTests
{
    private static readonly EncoderOption Nvenc = new(VideoCodec.H264, EncoderKind.Nvenc, "h264_nvenc");

    private static VerticalLayout Copy(VerticalPane? bottom = null, bool split = true) =>
        new(1080, 1920, split, 55, new VerticalPane(PaneKind.Screen, PaneFit.Fit), bottom ?? new VerticalPane(PaneKind.Screen, PaneFit.Fill, 0, 100, 250), true);

    private static StudioRequest Request(VerticalLayout? copy, Func<StudioRequest, StudioRequest>? change = null)
    {
        var r = new StudioRequest
        {
            Source = new CaptureSource(SourceKind.Monitor, 0, new PixelRect(0, 0, 1920, 1080), "Tela 1"),
            OutputWidth = 1920, OutputHeight = 1080, Fps = 60, Encoder = Nvenc, BitrateMbps = 16, WorkingDirectory = "/tmp/x",
            RecordFile = "grav.mkv", VerticalCopy = copy, VerticalFile = copy is null ? null : "grav-vertical.mkv",
        };
        return change is null ? r : change(r);
    }

    private static List<string> Args(StudioRequest r) => StudioCommand.Build(r).ToList();

    private static string Graph(StudioRequest r)
    {
        var a = Args(r);
        return a[a.IndexOf("-filter_complex") + 1];
    }

    [Fact]
    public void Gera_duas_saidas_a_horizontal_e_a_vertical()
    {
        var a = Args(Request(Copy()));
        string g = Graph(Request(Copy()));
        Assert.Contains("[scr0]split=2[b0][vsrc]", g);
        Assert.Contains("[vsrc]null[v_scr]", g);
        Assert.Contains("[v_scr]split=2[v_sa][v_sb]", g);
        Assert.Contains("[b0]format=nv12[v]", g);
        Assert.Contains("[v_b0]format=nv12[vv]", g);
        int firstMap = a.IndexOf("[v]");
        int secondMap = a.IndexOf("[vv]");
        Assert.True(firstMap > 0 && secondMap > firstMap);
        Assert.Equal("grav-vertical.mkv", a[^1]);
        Assert.Equal("matroska", a[^2]);
        Assert.Equal(1, a.Count(x => x == "tee"));
    }

    [Fact]
    public void Rotulos_da_copia_nao_batem_com_os_do_video_normal()
    {
        string g = Graph(Request(Copy()));
        foreach (string label in new[] { "[v_pt]", "[v_pb]", "[v_bp]", "[v_bv]", "[v_b0]" }) Assert.Contains(label, g);
        Assert.DoesNotContain("[pt]", g);
        Assert.DoesNotContain("[bp]", g);
    }

    [Fact]
    public void Segunda_saida_leva_o_audio_e_a_taxa_propria()
    {
        var a = Args(Request(Copy(), r => r with { Audio = new AudioSetup("pipe") }));
        int second = a.IndexOf("[vv]");
        Assert.Contains("1:a", a.Skip(second));
        Assert.Contains("-b:v", a.Skip(second));
        Assert.Equal("h264_nvenc", a[second + 2]);
    }

    [Fact]
    public void Camera_em_uso_no_canto_e_dividida_entre_as_duas_saidas()
    {
        var cam = new CameraOverlay("Logitech C920", CameraCorner.BottomRight, 20, false);
        var copy = Copy(new VerticalPane(PaneKind.Camera, PaneFit.Fill, Source: "Logitech C920"));
        var r = Request(copy, x => x with { Cameras = new[] { cam } });
        string g = Graph(r);
        Assert.Contains("[1:v]split=2[cm0][cv0]", g);
        Assert.Contains("[cm0]scale=", g);
        Assert.Contains("[cv0]format=bgra,", g);
        Assert.Equal(1, string.Join(" ", Args(r)).Split("-f dshow").Length - 1);   // a câmera abre uma vez só
    }

    [Fact]
    public void Camera_que_nao_esta_no_canto_abre_como_entrada_propria()
    {
        var copy = Copy(new VerticalPane(PaneKind.Camera, PaneFit.Fill, Source: "Outra Camera"));
        var r = Request(copy);
        Assert.Contains("video=Outra Camera", string.Join(" ", Args(r)));
        Assert.Contains("[1:v]format=bgra,scale=", Graph(r));
    }

    [Fact]
    public void Arquivo_de_fundo_e_camera_dividida_mantem_os_numeros_das_entradas()
    {
        var cam = new CameraOverlay("Cam A", CameraCorner.TopRight, 20, false);
        var copy = Copy(new VerticalPane(PaneKind.File, Source: @"C:\f\fundo.png"));
        var r = Request(copy, x => x with { Cameras = new[] { cam }, Audio = new AudioSetup("pipe") });
        string g = Graph(r);
        Assert.Contains("[2:v]format=bgra,scale=", g);       // arquivo de fundo (entrada 2: depois do vídeo e do áudio)
        Assert.Contains("[3:v]scale=", g);
        Assert.DoesNotContain("split=2[cm0]", g);                    // a câmera do canto vem depois
    }

    [Fact]
    public void Validacoes_da_copia()
    {
        Assert.Null(StudioCommand.Validate(Request(Copy())));
        Assert.NotNull(StudioCommand.Validate(Request(Copy(), r => r with { RecordFile = null, ReplayFolder = "rep" })));
        Assert.NotNull(StudioCommand.Validate(Request(Copy(), r => r with { VerticalFile = null })));
        Assert.NotNull(StudioCommand.Validate(Request(Copy(), r => r with { Vertical = Copy() with { Divider = false }, OutputWidth = 1080, OutputHeight = 1920 })));
        var noScreen = new VerticalLayout(1080, 1920, false, 50, new VerticalPane(PaneKind.File, Source: "a.png"), new VerticalPane(PaneKind.File, Source: "b.png"), false);
        Assert.NotNull(StudioCommand.Validate(Request(noScreen)));
    }

    [Fact]
    public void Mp4_usa_mp4_fragmentado_na_copia()
    {
        var a = Args(Request(Copy(), r => r with { RecordMp4 = true, VerticalFile = "v.mp4" }));
        Assert.Equal("v.mp4", a[^1]);
        Assert.Contains("frag_keyframe+empty_moov+default_base_moof", a);
    }
}
