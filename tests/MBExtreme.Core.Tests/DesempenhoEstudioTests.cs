using System.Diagnostics;
using MBExtreme.Core.Recording;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>Câmera no modo certo, motor de vídeo embutido, codificadores da placa, imagens das câmeras durante a gravação e o monitor de uso.</summary>
public class DesempenhoEstudioTests
{
    private static readonly EncoderOption Nvenc = new(VideoCodec.H264, EncoderKind.Nvenc, "h264_nvenc");
    private static readonly CaptureSource Tela1 = new(SourceKind.Monitor, 0, new PixelRect(0, 0, 2560, 1440), "Tela 1");

    // saída típica do "ffmpeg -f dshow -list_options true -i video=..." (FFmpeg 7)
    private const string Webcam = """
        [dshow @ 000001] DirectShow video device options (from video devices)
        [dshow @ 000001]  Pin "Capture" (alternative pin name "0")
        [dshow @ 000001]   pixel_format=yuyv422  min s=1920x1080 fps=5 max s=1920x1080 fps=5 (tv, bt470bg/bt709/unknown, topleft)
        [dshow @ 000001]   pixel_format=yuyv422  min s=640x480 fps=30 max s=640x480 fps=30
        [dshow @ 000001]   vcodec=mjpeg  min s=1920x1080 fps=30 max s=1920x1080 fps=30
        [dshow @ 000001]   vcodec=mjpeg  min s=1280x720 fps=30 max s=1280x720 fps=60.0002
        [dshow @ 000001]   vcodec=h264  min s=1280x720 fps=30 max s=1280x720 fps=30
        [dshow @ 000001]  Pin "Still" (alternative pin name "1")
        [dshow @ 000001]   pixel_format=yuyv422  min s=320x240 fps=60 max s=320x240 fps=60
        video=Webcam: Immediate exit requested
        """;

    [Fact]
    public void Le_os_modos_da_camera_so_do_pino_de_video()
    {
        var modes = CameraModes.Parse(Webcam);
        Assert.Equal(5, modes.Count);   // o pino "Still" (foto) fica de fora
        Assert.Contains(modes, m => m is { Width: 1280, Height: 720, Codec: "mjpeg" } && m.MaxFps > 60);
        Assert.Contains(modes, m => m is { Width: 1920, PixelFormat: "yuyv422" } && m.MaxFps == 5);
    }

    [Fact]
    public void Escolhe_mjpeg_720p_e_nao_o_1080p_cru_a_5_quadros()
    {
        var best = CameraModes.Pick(CameraModes.Parse(Webcam), 720)!;
        Assert.Equal((1280, 720, "mjpeg"), (best.Width, best.Height, best.Codec));
        Assert.Equal(30, best.RequestFps);   // pede no máximo 30: mais que isso só pesa
        Assert.Equal(new[] { "-video_size", "1280x720", "-framerate", "30", "-vcodec", "mjpeg" }, best.InputArgs());

        var tall = CameraModes.Pick(CameraModes.Parse(Webcam), 1080)!;   // vídeo vertical: a câmera ocupa a largura toda
        Assert.Equal((1920, 1080, "mjpeg"), (tall.Width, tall.Height, tall.Codec));
    }

    [Fact]
    public void Imagem_crua_no_mesmo_tamanho_vence_o_mjpeg()
    {
        var modes = new[]
        {
            new CameraMode(1280, 720, 30, "mjpeg", null),
            new CameraMode(1280, 720, 30, null, "nv12"),
            new CameraMode(1280, 720, 30, "h264", null),
        };
        var best = CameraModes.Pick(modes)!;
        Assert.Equal("nv12", best.PixelFormat);
        Assert.Equal(new[] { "-video_size", "1280x720", "-framerate", "30", "-pixel_format", "nv12" }, best.InputArgs());
        Assert.Null(CameraModes.Pick(Array.Empty<CameraMode>()));
    }

    [Fact]
    public void Modo_escolhido_entra_na_entrada_da_camera()
    {
        const string name = "Teste Modo Camera";
        try
        {
            CameraSource.Modes[name] = new CameraMode(1280, 720, 30, "mjpeg", null);
            var args = CameraSource.InputArgs(name);
            string joined = string.Join(" ", args);
            Assert.Contains("-video_size 1280x720 -framerate 30 -vcodec mjpeg", joined);
            Assert.Contains("-thread_queue_size", args);
            Assert.Equal("video=" + name, args[^1]);
            Assert.True(Array.IndexOf(args, "-video_size") < Array.IndexOf(args, "-i"));
        }
        finally { CameraSource.Modes.TryRemove(name, out _); }

        Assert.DoesNotContain("-video_size", CameraSource.InputArgs(name));   // sem modo: o padrão do Windows
        Assert.Equal(new[] { "-hide_banner", "-f", "dshow", "-list_options", "true", "-i", "video=Cam" }, CameraSource.ListModesArgs("Cam"));
    }

    // ---- motor de vídeo e codificadores

    [Fact]
    public void Teste_do_codificador_usa_nv12_como_na_gravacao()
    {
        var args = FfmpegCommand.ProbeArgs("h264_qsv");
        Assert.Contains("format=nv12", args);
        Assert.Contains("h264_qsv", args);
        Assert.Equal("-", args[^1]);
    }

    [Fact]
    public void Lista_guardada_volta_na_mesma_ordem_e_o_processador_fica_como_reserva()
    {
        var list = FfmpegCommand.FromNames(new[] { "hevc_nvenc", "h264_nvenc", "inventado", "libx264" });
        Assert.Equal(new[] { "hevc_nvenc", "h264_nvenc", "libx264" }, list.Select(e => e.FfmpegName));
        Assert.Equal(EncoderKind.Software, list[^1].Kind);
        Assert.True(FfmpegCommand.HasSoftwareEncoder(" V....D libx264              libx264 H.264 / AVC"));
        Assert.False(FfmpegCommand.HasSoftwareEncoder(" V....D h264_nvenc"));
        Assert.NotEqual(FfmpegCommand.Stamp("a", 1, DateTime.UnixEpoch), FfmpegCommand.Stamp("a", 2, DateTime.UnixEpoch));
    }

    [Fact]
    public void Processador_usa_esforco_leve_para_nao_derrubar_o_jogo()
    {
        Assert.Equal(new[] { "-preset", "veryfast" }, StudioCommand.QualityArgs(FfmpegCommand.Software, EncoderQuality.Balanced));
        Assert.Equal(new[] { "-preset", "superfast" }, StudioCommand.QualityArgs(FfmpegCommand.Software, EncoderQuality.Fast));
        Assert.Equal(new[] { "-preset", "p4" }, StudioCommand.QualityArgs(Nvenc, EncoderQuality.Balanced));
    }

    [Fact]
    public void Codigo_de_verificacao_e_arquivo_do_pacote_oficial()
    {
        string hash = new string('a', 64);
        Assert.Equal(hash, FfmpegProvision.ParseHash(hash.ToUpperInvariant() + "  ffmpeg-release-essentials.zip\n"));
        Assert.Null(FfmpegProvision.ParseHash("<html>erro</html>"));
        Assert.Null(FfmpegProvision.ParseHash(""));
        Assert.True(FfmpegProvision.IsFfmpegEntry("ffmpeg-7.1-essentials_build/bin/ffmpeg.exe"));
        Assert.False(FfmpegProvision.IsFfmpegEntry("ffmpeg-7.1-essentials_build/bin/ffprobe.exe"));
        Assert.False(FfmpegProvision.IsFfmpegEntry("ffmpeg-7.1-essentials_build/doc/ffmpeg.exe.html"));
    }

    // ---- imagens das câmeras durante a gravação

    private static StudioRequest WithCamera(Func<StudioRequest, StudioRequest>? change = null)
    {
        var r = new StudioRequest
        {
            Source = Tela1, OutputWidth = 1920, OutputHeight = 1080, Fps = 60, Encoder = Nvenc, BitrateMbps = 16, WorkingDirectory = "/tmp/x", RecordFile = "grav.mkv",
            Cameras = new[] { new CameraOverlay("Webcam", CameraCorner.BottomRight, 20, true), new CameraOverlay("Celular", CameraCorner.TopLeft, 15, false) },
            CameraPreviewPipes = new string?[] { @"\\.\pipe\mbx-camera-a", null },
        };
        return change is null ? r : change(r);
    }

    [Fact]
    public void Camera_manda_copia_pequena_pelo_cano_e_so_a_que_tem_cano()
    {
        var args = StudioCommand.Build(WithCamera()).ToList();
        string graph = args[args.IndexOf("-filter_complex") + 1];
        Assert.Contains(",split=2[c0][cpp0]", graph);
        Assert.Contains("[cpp0]fps=12,scale=320:-2:flags=bilinear,format=yuvj420p[cpv0]", graph);
        Assert.DoesNotContain("cpp1", graph);
        int map = args.IndexOf("[cpv0]");
        Assert.True(map > 0);
        Assert.Equal("mjpeg", args[map + 2]);
        Assert.Equal(@"\\.\pipe\mbx-camera-a", args[^1]);
        // a tela 1440p desce já em 1080p e a câmera é medida na largura final
        Assert.Contains("[0:v]hwdownload,format=bgra,scale=1920:1080:flags=bilinear[b0]", graph);
        Assert.Contains("scale=384:-2:flags=bilinear", graph);
    }

    [Fact]
    public void Cano_de_camera_invalido_e_recusado()
    {
        Assert.NotNull(StudioCommand.Validate(WithCamera(r => r with { CameraPreviewPipes = new string?[] { "C:/x.jpg" } })));
        Assert.NotNull(StudioCommand.Validate(WithCamera(r => r with { CameraPreviewPipes = new string?[] { @"\\.\pipe\a|b" } })));
        Assert.Null(StudioCommand.Validate(WithCamera()));
    }

    [Fact]
    public void Nas_cenas_ao_vivo_a_camera_tambem_manda_copia_mesmo_fora_das_cenas()
    {
        var r = WithCamera(x => x with
        {
            SceneCaptures = new[] { Tela1 },
            Scenes = new[] { new SceneLayout("Jogo", 0, Array.Empty<int>(), false, Array.Empty<int>()), new SceneLayout("Conversa", 0, new[] { 1 }, false, Array.Empty<int>()) },
        });
        Assert.True(r.LiveScenes);
        var args = StudioCommand.Build(r).ToList();
        string graph = args[args.IndexOf("-filter_complex") + 1];
        // a câmera 0 não está em nenhuma cena, mas a cópia dela continua (o quadro na tela segue mexendo)
        Assert.Contains("[cpp0]fps=12", graph);
        Assert.DoesNotContain("[1:v]nullsink", graph);
        Assert.Contains("[cpv0]", args);
    }

    /// <summary>Roda o grafo com câmera e cópia da câmera no FFmpeg de verdade (quando existe na máquina), com imagens sintéticas.</summary>
    [Fact]
    public void Grafo_com_copia_da_camera_roda_no_ffmpeg_de_verdade()
    {
        string? ffmpeg = new[] { "/usr/bin/ffmpeg", "/usr/local/bin/ffmpeg" }.FirstOrDefault(File.Exists);
        if (ffmpeg is null || OperatingSystem.IsWindows()) return;

        string dir = Path.Combine(Path.GetTempPath(), "mbx-desempenho-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var r = WithCamera(x => x with { CameraPreviewPipes = new string?[] { @"\\.\pipe\a", @"\\.\pipe\b" }, Fps = 20 });
            var args = StudioCommand.Build(r).ToList();
            string graph = args[args.IndexOf("-filter_complex") + 1].Replace("hwdownload,format=bgra", "format=bgra");
            var real = new List<string> { "-hide_banner", "-loglevel", "error", "-nostats" };
            real.AddRange(new[] { "-f", "lavfi", "-i", "color=c=red:s=2560x1440:r=20:d=2", "-f", "lavfi", "-i", "color=c=green:s=1280x720:r=30:d=2", "-f", "lavfi", "-i", "color=c=blue:s=640x480:r=30:d=2" });
            string main = Path.Combine(dir, "main.mkv"), cam0 = Path.Combine(dir, "cam0.mjpeg"), cam1 = Path.Combine(dir, "cam1.mjpeg");
            real.AddRange(new[] { "-filter_complex", graph, "-map", "[v]", "-c:v", "libx264", "-preset", "ultrafast", "-y", main });
            real.AddRange(new[] { "-map", "[cpv0]", "-c:v", "mjpeg", "-q:v", "8", "-f", "mjpeg", "-y", cam0 });
            real.AddRange(new[] { "-map", "[cpv1]", "-c:v", "mjpeg", "-q:v", "8", "-f", "mjpeg", "-y", cam1 });

            var info = new ProcessStartInfo(ffmpeg) { RedirectStandardError = true, UseShellExecute = false };
            foreach (string a in real) info.ArgumentList.Add(a);
            using var process = Process.Start(info)!;
            string errors = process.StandardError.ReadToEnd();
            Assert.True(process.WaitForExit(60000), "o FFmpeg não terminou");
            Assert.True(process.ExitCode == 0, errors);
            Assert.True(new FileInfo(main).Length > 1000);

            var frames = new MjpegSplitter().Push(File.ReadAllBytes(cam0));
            Assert.InRange(frames.Count, 15, 30);   // ~12 por segundo por 2 s
            Assert.NotEmpty(new MjpegSplitter().Push(File.ReadAllBytes(cam1)));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    // ---- monitor de uso

    [Fact]
    public void Le_o_processo_e_o_motor_do_contador_da_gpu()
    {
        Assert.Equal((1234, "VideoEncode"), UsageMath.ParseEngine("pid_1234_luid_0x00000000_0x0000D1B1_phys_0_eng_2_engtype_VideoEncode"));
        Assert.Equal((88, "3D"), UsageMath.ParseEngine("pid_88_luid_0x00000000_0x0000D1B1_phys_0_eng_0_engtype_3D"));
        Assert.Null(UsageMath.ParseEngine("_Total"));
        Assert.True(UsageMath.IsEncoderEngine("VideoEncode"));
        Assert.True(UsageMath.IsEncoderEngine("VideoCodec"));
        Assert.False(UsageMath.IsEncoderEngine("Copy"));
    }

    [Fact]
    public void Soma_da_gpu_separa_o_programa_do_jogo_e_o_codificador_do_3d()
    {
        var samples = new[]
        {
            ("pid_10_luid_x_phys_0_eng_0_engtype_3D", 70.0),           // o jogo
            ("pid_20_luid_x_phys_0_eng_0_engtype_3D", 4.0),            // o MB Extreme
            ("pid_30_luid_x_phys_0_eng_2_engtype_VideoEncode", 35.0),  // o FFmpeg codificando
            ("pid_30_luid_x_phys_0_eng_1_engtype_Copy", 9.0),
        };
        var (system, app, encoder) = UsageMath.Gpu(samples, new[] { 20, 30 });
        Assert.Equal(74, system, 3);
        Assert.Equal(4, app, 3);
        Assert.Equal(35, encoder, 3);
    }

    [Fact]
    public void Processador_em_porcentagem_do_pc_inteiro()
    {
        // 2 s de processador em 1 s de relógio num PC de 8 núcleos = 25%
        Assert.Equal(25, UsageMath.CpuPercent(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(1), 8), 3);
        Assert.Equal(0, UsageMath.CpuPercent(TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.Zero, 8));
        Assert.Equal("512 MB", UsageMath.Bytes(512L << 20));
        Assert.Equal("1,5 GB", UsageMath.Bytes(3L << 29));
    }
}
