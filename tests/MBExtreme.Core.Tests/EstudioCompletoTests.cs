using MBExtreme.Core.Network;
using MBExtreme.Core.Recording;
using Xunit;

namespace MBExtreme.Core.Tests;

public class EstudioCompletoTests
{
    private static readonly EncoderOption Nvenc = new(VideoCodec.H264, EncoderKind.Nvenc, "h264_nvenc");
    private static readonly EncoderOption Amf = new(VideoCodec.H264, EncoderKind.Amf, "h264_amf");
    private static readonly EncoderOption Qsv = new(VideoCodec.H264, EncoderKind.Qsv, "h264_qsv");

    private static StudioRequest Request(Func<StudioRequest, StudioRequest>? change = null)
    {
        var r = new StudioRequest
        {
            Source = new CaptureSource(SourceKind.Monitor, 0, new PixelRect(0, 0, 1920, 1080), "Tela 1"),
            OutputWidth = 1920,
            OutputHeight = 1080,
            Fps = 60,
            Encoder = Nvenc,
            BitrateMbps = 16,
            WorkingDirectory = "/tmp/x",
            RecordFile = "grav.mkv",
        };
        return change is null ? r : change(r);
    }

    // ---- padrões das plataformas

    [Fact]
    public void Twitch_nunca_passa_de_6_mbps_nem_de_1080p()
    {
        var plan = StreamPlan.Build(StreamService.Twitch, 1440, 60, requestedMbps: 20, userCapMbps: 0, audioKbps: 320);
        Assert.Equal(1080, plan.Height);
        Assert.Equal(6000, plan.VideoKbps);
        Assert.Equal(160, plan.AudioKbps);
        Assert.Contains(plan.Notes, n => n.Contains("1080p"));
        Assert.Contains(plan.Notes, n => n.Contains("6 Mbps"));
    }

    [Fact]
    public void Facebook_usa_30_fps_e_o_automatico_cabe_na_faixa()
    {
        var plan = StreamPlan.Build(StreamService.Facebook, 1080, 60, 0, 0, 0);
        Assert.Equal(30, plan.Fps);
        Assert.InRange(plan.VideoKbps, 3000, 6000);
        Assert.Equal(128, plan.AudioKbps);
    }

    [Fact]
    public void Youtube_aceita_1440p60_com_taxa_na_faixa_recomendada()
    {
        var plan = StreamPlan.Build(StreamService.YouTube, 1440, 60, 0, 0, 160);
        Assert.Equal(1440, plan.Height);
        Assert.InRange(plan.VideoKbps, 9000, 18000);
    }

    [Fact]
    public void Limite_de_banda_do_usuario_reduz_a_taxa_e_avisa_quando_fica_pouco()
    {
        var plan = StreamPlan.Build(StreamService.YouTube, 1080, 60, 8, userCapMbps: 3, audioKbps: 128);
        Assert.Equal(3000, plan.VideoKbps);
        Assert.Contains(plan.Notes, n => n.Contains("limite de banda"));
        Assert.Contains(plan.Notes, n => n.Contains("pouco"));
    }

    [Fact]
    public void Envio_necessario_tem_folga_e_o_teste_de_upload_sugere_taxa()
    {
        Assert.Equal(7700, StreamPlan.RequiredUpload(6000, 160));  // (6000 + 160) * 1,25
        Assert.Equal(6, StreamPlan.FitToUpload(10, 160));          // 70% de 10 Mbps = 7,0; menos o áudio = 6,84; arredonda para baixo
        Assert.Equal(1, StreamPlan.FitToUpload(0.5, 160));         // nunca abaixo de 1 Mbps
    }

    [Fact]
    public void Faixa_de_taxa_cresce_com_a_resolucao()
    {
        Assert.True(PlatformSpecs.BitrateRange(2160, 60).Min > PlatformSpecs.BitrateRange(1440, 60).Min);
        Assert.True(PlatformSpecs.BitrateRange(1080, 60).Min > PlatformSpecs.BitrateRange(1080, 30).Min);
    }

    // ---- comando: qualidade, áudio e taxa

    [Fact]
    public void Qualidade_do_codificador_muda_o_parametro_certo_de_cada_marca()
    {
        Assert.Contains("-preset p6", string.Join(" ", StudioCommand.Build(Request(r => r with { Quality = EncoderQuality.Quality }))));
        Assert.Contains("-preset p2", string.Join(" ", StudioCommand.Build(Request(r => r with { Quality = EncoderQuality.Fast }))));
        Assert.Contains("-quality speed", string.Join(" ", StudioCommand.Build(Request(r => r with { Encoder = Amf, Quality = EncoderQuality.Fast }))));
        Assert.Contains("-preset slower", string.Join(" ", StudioCommand.Build(Request(r => r with { Encoder = Qsv, Quality = EncoderQuality.Quality }))));
    }

    [Fact]
    public void Taxa_em_kbps_respeita_o_limite_da_transmissao()
    {
        var stream = new StreamTarget(StreamService.Twitch, "", "live_abc");
        var args = StudioCommand.Build(Request(r => r with { Stream = stream, VideoKbps = 9000, StreamMaxMbps = 6 })).ToList();
        Assert.Equal("6000k", args[args.IndexOf("-b:v") + 1]);
        var rec = StudioCommand.Build(Request(r => r with { VideoKbps = 9000 })).ToList();
        Assert.Equal("9000k", rec[rec.IndexOf("-b:v") + 1]);
    }

    [Fact]
    public void Qualidade_do_audio_vai_para_o_aac()
    {
        var args = StudioCommand.Build(Request(r => r with { Audio = new AudioSetup("\\\\.\\pipe\\x"), AudioKbps = 256 })).ToList();
        Assert.Equal("256k", args[args.IndexOf("-b:a") + 1]);
    }

    // ---- câmera

    [Fact]
    public void Camera_entra_como_entrada_extra_e_vira_filtro_complexo()
    {
        var cam = new CameraOverlay("Integrated Camera", CameraCorner.BottomRight, 22, true);
        var args = StudioCommand.Build(Request(r => r with { Camera = cam })).ToList();
        string cmd = string.Join(" ", args);
        Assert.Contains("-f dshow", cmd);
        Assert.Contains("video=Integrated Camera", cmd);
        Assert.Contains("-filter_complex", cmd);
        Assert.Contains("overlay=x=main_w-overlay_w-", cmd);
        Assert.Contains("drawbox", cmd);
        Assert.Equal("[v]", args[args.IndexOf("-map") + 1]);
        Assert.Contains("hwdownload", cmd);
    }

    [Fact]
    public void Camera_com_audio_usa_a_terceira_entrada()
    {
        var cam = new CameraOverlay("Cam", CameraCorner.TopLeft, 20, false);
        string cmd = string.Join(" ", StudioCommand.Build(Request(r => r with { Camera = cam, Audio = new AudioSetup("p") })));
        Assert.Contains("[2:v]scale=", cmd);
        Assert.DoesNotContain("drawbox", cmd);
        Assert.Contains("overlay=x=", cmd);
    }

    [Fact]
    public void Camera_com_tela_reduzida_reduz_a_tela_antes_de_juntar()
    {
        // a tela desce já no tamanho final: a câmera e o resto trabalham em menos pixels (bem mais leve)
        var cam = new CameraOverlay("Cam", CameraCorner.TopRight, 20, false);
        string cmd = string.Join(" ", StudioCommand.Build(Request(r => r with { Camera = cam, OutputWidth = 1280, OutputHeight = 720 })));
        Assert.Contains("[0:v]hwdownload,format=bgra,scale=1280:720:flags=bilinear[b0]", cmd);
        Assert.Contains("scale=256:-2:flags=bilinear", cmd);   // 20% da largura final (1280)
        Assert.Contains("format=nv12[v]", cmd);
        Assert.DoesNotContain("scale=1280:720:flags=bilinear,format=nv12[v]", cmd);
    }

    [Theory]
    [InlineData("HD Webcam C920", true)]
    [InlineData("OBS Virtual Camera", true)]
    [InlineData("Cam\"; rm", false)]
    [InlineData("a|b", false)]
    [InlineData("video=x:audio=y", false)]
    [InlineData("", false)]
    public void Nome_da_camera_so_aceita_caracteres_seguros(string name, bool valid)
    {
        Assert.Equal(valid, CameraOverlay.IsValidName(name));
        if (!valid && name.Length > 0)
            Assert.NotNull(StudioCommand.Validate(Request(r => r with { Camera = new CameraOverlay(name, CameraCorner.TopLeft, 20, false) })));
    }

    [Fact]
    public void Lista_de_cameras_do_dshow_e_lida_sem_duplicar()
    {
        const string text = "[dshow @ 000001] DirectShow video devices (some may be both video and audio devices)\n" +
                            "[dshow @ 000001]  \"Integrated Camera\" (video)\n" +
                            "[dshow @ 000001]   Alternative name \"@device_pnp_\\\\?\\usb#vid_0c45\"\n" +
                            "[dshow @ 000001]  \"OBS Virtual Camera\" (video)\n" +
                            "[dshow @ 000001] DirectShow audio devices\n" +
                            "[dshow @ 000001]  \"Microfone (USB)\" (audio)\n" +
                            "[dshow @ 000001]  \"Integrated Camera\" (video)\n";
        Assert.Equal(new[] { "Integrated Camera", "OBS Virtual Camera" }, FfmpegRecorder.ParseCameras(text));
    }

    // ---- atalhos

    [Fact]
    public void Atalho_guardado_volta_igual_e_valores_ruins_sao_ignorados()
    {
        var b = HotkeyBinding.Parse("CtrlAlt+F8");
        Assert.NotNull(b);
        Assert.Equal("Ctrl + Alt + F8", b!.Text);
        Assert.Equal("CtrlAlt+F8", b.Stored);
        Assert.Equal(0x77u, b.VirtualKey);
        Assert.Null(HotkeyBinding.Parse("CtrlShift+F4"));   // F1 a F4 ficam de fora (ajuda, Alt+F4)
        Assert.Null(HotkeyBinding.Parse("Win+F9"));
        Assert.Null(HotkeyBinding.Parse(""));
        Assert.Null(HotkeyBinding.Parse(null));
        Assert.Null(HotkeyBinding.Parse("CtrlShift+F13"));
    }

    [Fact]
    public void Atalhos_padrao_nao_se_repetem_e_repeticao_e_detectada()
    {
        var defaults = Enum.GetValues<StudioAction>().ToDictionary(a => a, a => HotkeyBinding.Parse(StudioHotkeys.DefaultFor(a)));
        Assert.Empty(StudioHotkeys.Conflicts(defaults));

        defaults[StudioAction.ToggleStream] = defaults[StudioAction.SaveReplay];
        var conflicts = StudioHotkeys.Conflicts(defaults);
        Assert.Single(conflicts);
        Assert.Contains("Ctrl + Shift + F9", conflicts[0]);
    }

    // ---- chat

    [Theory]
    [InlineData(StreamService.Twitch, "meu_canal", "https://www.twitch.tv/popout/meu_canal/chat?popout=")]
    [InlineData(StreamService.Twitch, "@MeuCanal", "https://www.twitch.tv/popout/meucanal/chat?popout=")]
    [InlineData(StreamService.Twitch, "https://www.twitch.tv/Gaules", "https://www.twitch.tv/popout/gaules/chat?popout=")]
    [InlineData(StreamService.YouTube, "dQw4w9WgXcQ", "https://www.youtube.com/live_chat?is_popout=1&v=dQw4w9WgXcQ")]
    [InlineData(StreamService.YouTube, "https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=5", "https://www.youtube.com/live_chat?is_popout=1&v=dQw4w9WgXcQ")]
    [InlineData(StreamService.YouTube, "https://youtu.be/dQw4w9WgXcQ", "https://www.youtube.com/live_chat?is_popout=1&v=dQw4w9WgXcQ")]
    public void Chat_abre_so_enderecos_conhecidos(StreamService service, string input, string expected) =>
        Assert.Equal(expected, ChatLinks.Url(service, input));

    [Theory]
    [InlineData(StreamService.Twitch, "a")]
    [InlineData(StreamService.Twitch, "canal com espaco")]
    [InlineData(StreamService.Twitch, "x\"><script>")]
    [InlineData(StreamService.YouTube, "curto")]
    [InlineData(StreamService.Facebook, "qualquer")]
    [InlineData(StreamService.Custom, "qualquer")]
    [InlineData(StreamService.Twitch, "")]
    public void Chat_recusa_entradas_estranhas(StreamService service, string input) =>
        Assert.Null(ChatLinks.Url(service, input));

    // ---- rede

    [Fact]
    public void Velocidade_e_calculada_em_megabits_por_segundo()
    {
        Assert.Equal(8.0, NetThroughput.Mbps(0, 1_000_000, 1), 3);
        Assert.Equal(4.0, NetThroughput.Mbps(1_000_000, 2_000_000, 2), 3);
        Assert.Equal(0, NetThroughput.Mbps(5, 1, 1));   // contador que reiniciou
        Assert.Equal(0, NetThroughput.Mbps(0, 100, 0));
    }
}
