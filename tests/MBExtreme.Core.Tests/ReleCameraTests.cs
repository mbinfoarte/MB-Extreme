using System.Diagnostics;
using MBExtreme.Core.Recording;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>
/// Câmera pelo relé (como o OBS): a live recebe a última imagem da câmera num ritmo fixo e nunca espera a câmera.
/// Antes, a câmera entrava direto no FFmpeg e o filtro de sobreposição segurava o vídeo inteiro a cada atraso dela.
/// </summary>
public class ReleCameraTests
{
    private static readonly EncoderOption Nvenc = new(VideoCodec.H264, EncoderKind.Nvenc, "h264_nvenc");

    [Fact]
    public void Tamanho_do_rele_segue_a_proporcao_da_camera()
    {
        Assert.Equal((1280, 720), CameraRelayPlan.Size(new CameraMode(1280, 720, 30, "mjpeg", null)));
        Assert.Equal((1920, 1080), CameraRelayPlan.Size(new CameraMode(1920, 1080, 30, "mjpeg", null)));   // Full HD inteiro
        Assert.Equal((3840, 2160), CameraRelayPlan.Size(new CameraMode(3840, 2160, 30, "mjpeg", null)));   // webcam 4K: nativa
        Assert.Equal((3840, 2160), CameraRelayPlan.Size(new CameraMode(7680, 4320, 30, null, null)));      // celular 8K: vira 4K
        Assert.Equal((2160, 3840), CameraRelayPlan.Size(new CameraMode(2160, 3840, 30, null, null)));      // celular em pé em 4K: inteiro
        Assert.Equal((640, 480), CameraRelayPlan.Size(new CameraMode(640, 480, 30, null, "yuyv422")));
        Assert.Equal((1920, 1080), CameraRelayPlan.Size(null));   // sem saber o modo: Full HD com faixas
        Assert.Equal(960 * 540 * 3 / 2, CameraRelayPlan.FrameBytes(960, 540));
    }

    [Fact]
    public void Qualidade_maxima_escolhe_o_maior_modo_fluido_da_camera()
    {
        var modes = new[]
        {
            new CameraMode(3840, 2160, 30, "mjpeg", null),
            new CameraMode(3840, 2160, 5, null, "yuyv422"),     // 4K cru a 5 quadros: travado, fica de fora
            new CameraMode(1920, 1080, 60, "mjpeg", null),
            new CameraMode(1280, 720, 30, null, "nv12"),
        };
        var max = CameraModes.Pick(modes, CameraRelayPlan.MaxHeight)!;
        Assert.Equal((3840, 2160, "mjpeg"), (max.Width, max.Height, max.Codec));
        Assert.Equal(1080, CameraModes.Pick(modes, 1080)!.Height);
        Assert.Equal(720, CameraModes.Pick(modes, 720)!.Height);
    }

    [Fact]
    public void Le_o_tamanho_real_do_video_do_celular()
    {
        const string ffmpegOut = "Input #0, rtsp, from 'rtsp://192.168.0.5:8080/h264':\n  Stream #0:0: Video: h264 (High), yuv420p(progressive), 3840x2160, 30 fps, 30 tbr, 90k tbn";
        var mode = FfmpegRecorder.ParseStreamMode(ffmpegOut)!;
        Assert.Equal((3840, 2160, 30.0), (mode.Width, mode.Height, mode.MaxFps));
        Assert.Null(FfmpegRecorder.ParseStreamMode("Connection refused"));
    }

    [Fact]
    public void Rele_abre_a_camera_sem_fila_e_entrega_imagem_crua_de_tamanho_fixo()
    {
        var args = CameraRelayPlan.Args("Webcam", "hflip", 960, 540).ToList();
        Assert.Contains("nobuffer", args);
        Assert.Contains("video=Webcam", args);
        string vf = args[args.IndexOf("-vf") + 1];
        Assert.StartsWith("fps=30,hflip,scale=960:540:force_original_aspect_ratio=decrease", vf);
        Assert.EndsWith("pad=960:540:(ow-iw)/2:(oh-ih)/2,format=yuv420p", vf);
        Assert.Equal("yuv420p", args[args.IndexOf("-pix_fmt") + 1]);
        Assert.Equal("pipe:1", args[^1]);
    }

    [Fact]
    public void Gravacao_le_a_camera_do_cano_do_rele_e_nao_do_dshow()
    {
        var relay = new RelayInput(@"\\.\pipe\mbx-camfeed-teste", 960, 540, 60);
        var r = new StudioRequest
        {
            Source = new CaptureSource(SourceKind.Monitor, 0, new PixelRect(0, 0, 1920, 1080), "Tela 1"),
            OutputWidth = 1920, OutputHeight = 1080, Fps = 60, Encoder = Nvenc, BitrateMbps = 16, WorkingDirectory = "/tmp/x", RecordFile = "g.mkv",
            Cameras = new[] { new CameraOverlay("Webcam", CameraCorner.BottomRight, 22, false) },
            CameraRelays = new Dictionary<string, RelayInput>(StringComparer.OrdinalIgnoreCase) { ["webcam"] = relay },
        };
        Assert.Null(StudioCommand.Validate(r));
        var args = StudioCommand.Build(r).ToList();
        Assert.DoesNotContain("dshow", args);
        int input = args.IndexOf(@"\\.\pipe\mbx-camfeed-teste");
        Assert.True(input > 0);
        Assert.Equal("-i", args[input - 1]);
        Assert.Contains("rawvideo", args);
        Assert.Contains("960x540", args);
        Assert.NotNull(StudioCommand.Validate(r with { CameraRelays = new Dictionary<string, RelayInput> { ["Webcam"] = relay with { PipePath = "C:/x" } } }));
    }

    [Fact]
    public void Live_usa_os_ajustes_do_obs_no_codificador()
    {
        Assert.Equal(new[] { "-rc", "cbr", "-bf", "2", "-spatial-aq", "1" }, StudioCommand.LiveTuning(Nvenc));
        Assert.DoesNotContain("-bf", StudioCommand.LiveTuning(new EncoderOption(VideoCodec.Hevc, EncoderKind.Nvenc, "hevc_nvenc")));
        Assert.Equal(new[] { "-rc", "cbr" }, StudioCommand.LiveTuning(new EncoderOption(VideoCodec.H264, EncoderKind.Amf, "h264_amf")));

        var r = new StudioRequest
        {
            Source = new CaptureSource(SourceKind.Monitor, 0, new PixelRect(0, 0, 1920, 1080), "Tela 1"),
            OutputWidth = 1920, OutputHeight = 1080, Fps = 60, Encoder = Nvenc, BitrateMbps = 6, WorkingDirectory = "/tmp/x",
            Stream = new StreamTarget(StreamService.YouTube, "", "abcd-1234"),
        };
        string live = string.Join(" ", StudioCommand.Build(r));
        Assert.Contains("-rc cbr -bf 2 -spatial-aq 1", live);
        Assert.DoesNotContain("-spatial-aq", string.Join(" ", StudioCommand.Build(r with { EncoderTuning = false })));
        Assert.DoesNotContain("-spatial-aq", string.Join(" ", StudioCommand.Build(r with { Stream = null, RecordFile = "g.mkv" })));   // só gravando: não precisa
    }

    [Fact]
    public void Imagem_do_rele_vira_bgra_com_as_cores_certas()
    {
        // 4×2: metade esquerda branca, direita preta (Y), sem cor (U=V=128); depois uma vermelha
        byte[] yuv = { 235, 235, 16, 16, 235, 235, 16, 16, 128, 128, 128, 128 };
        var bgra = new byte[4 * 2 * 4];
        Yuv420Preview.ToBgra(yuv, 4, 2, 1, bgra);
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, bgra[..4]);
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, bgra[8..12]);

        byte[] red = { 81, 81, 81, 81, 90, 240 };   // vermelho puro em BT.601 de TV (2×2)
        var px = new byte[2 * 2 * 4];
        Yuv420Preview.ToBgra(red, 2, 2, 1, px);
        Assert.True(px[2] > 240 && px[1] < 20 && px[0] < 20, $"{px[2]},{px[1]},{px[0]}");

        Assert.Equal((480, 270), Yuv420Preview.OutputSize(960, 540, 2));
    }

    /// <summary>
    /// O grafo com a câmera vinda do relé roda no FFmpeg de verdade: a "câmera" é um arquivo de imagens cruas no formato do relé
    /// e a tela é uma imagem sintética. Confere que sai vídeo com a câmera sobreposta.
    /// </summary>
    [Fact]
    public void Camera_do_rele_entra_no_video_no_ffmpeg_de_verdade()
    {
        string? ffmpeg = new[] { "/usr/bin/ffmpeg", "/usr/local/bin/ffmpeg" }.FirstOrDefault(File.Exists);
        if (ffmpeg is null || OperatingSystem.IsWindows()) return;
        string dir = Path.Combine(Path.GetTempPath(), "mbx-rele-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // 2 s de "câmera" verde no formato do relé (960×540 yuv420p)
            string cam = Path.Combine(dir, "cam.yuv");
            var make = Process.Start(new ProcessStartInfo(ffmpeg, $"-hide_banner -loglevel error -f lavfi -i color=c=green:s=960x540:r=30:d=2 -f rawvideo -pix_fmt yuv420p -y {cam}") { UseShellExecute = false })!;
            make.WaitForExit();
            Assert.Equal(60L * CameraRelayPlan.FrameBytes(960, 540), new FileInfo(cam).Length);

            var r = new StudioRequest
            {
                Source = new CaptureSource(SourceKind.Monitor, 0, new PixelRect(0, 0, 640, 360), "Tela 1"),
                OutputWidth = 640, OutputHeight = 360, Fps = 30, Encoder = new EncoderOption(VideoCodec.H264, EncoderKind.Software, "libx264"),
                BitrateMbps = 2, WorkingDirectory = dir, RecordFile = "out.mkv",
                Cameras = new[] { new CameraOverlay("Webcam", CameraCorner.TopLeft, 40, false) },
                CameraRelays = new Dictionary<string, RelayInput>(StringComparer.OrdinalIgnoreCase) { ["Webcam"] = new(@"\\.\pipe\x", 960, 540, 30) },
            };
            var args = StudioCommand.Build(r).ToList();
            // no Linux não há ddagrab nem cano do Windows: troca a tela por uma imagem vermelha e o cano pelo arquivo
            for (int i = 0; i < args.Count; i++)
            {
                if (args[i].StartsWith("ddagrab=", StringComparison.Ordinal)) args[i] = "color=c=red:s=640x360:r=30:d=2";
                if (args[i] == @"\\.\pipe\x") args[i] = cam;
                if (args[i] == "-use_wallclock_as_timestamps") { args.RemoveAt(i); args.RemoveAt(i); i--; }
            }
            int fc = args.IndexOf("-filter_complex");
            args[fc + 1] = args[fc + 1].Replace("hwdownload,format=bgra", "format=bgra");
            int tee = args.IndexOf("tee");
            args[tee] = "matroska";
            args[tee + 1] = Path.Combine(dir, "out.mkv");
            args.Remove("-progress"); args.Remove("pipe:1");

            var info = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, RedirectStandardError = true, WorkingDirectory = dir };
            foreach (string a in args) info.ArgumentList.Add(a);
            using var p = Process.Start(info)!;
            string err = p.StandardError.ReadToEnd();
            Assert.True(p.WaitForExit(60000));
            Assert.True(p.ExitCode == 0, err);

            // canto de cima à esquerda verde (câmera), canto de baixo à direita vermelho (tela)
            string frame = Path.Combine(dir, "f.rgb");
            var grab = Process.Start(new ProcessStartInfo(ffmpeg, $"-hide_banner -loglevel error -ss 1 -i {Path.Combine(dir, "out.mkv")} -frames:v 1 -f rawvideo -pix_fmt rgb24 -y {frame}") { UseShellExecute = false })!;
            grab.WaitForExit();
            byte[] rgb = File.ReadAllBytes(frame);
            (int R, int G, int B) At(int x, int y) { int o = (y * 640 + x) * 3; return (rgb[o], rgb[o + 1], rgb[o + 2]); }
            var camPixel = At(60, 40);
            var screenPixel = At(600, 330);
            Assert.True(camPixel.G > 100 && camPixel.R < 80, $"câmera: {camPixel}");
            Assert.True(screenPixel.R > 200 && screenPixel.G < 60, $"tela: {screenPixel}");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
