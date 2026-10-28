using System.Diagnostics;
using MBExtreme.Core.Recording;
using Xunit;

namespace MBExtreme.Core.Tests;

public class CenasAoVivoTests
{
    private static readonly EncoderOption Nvenc = new(VideoCodec.H264, EncoderKind.Nvenc, "h264_nvenc");
    private static readonly CaptureSource Tela1 = new(SourceKind.Monitor, 0, new PixelRect(0, 0, 1920, 1080), "Tela 1");
    private static readonly CaptureSource JanelaNaTela1 = new(SourceKind.WindowRegion, 0, new PixelRect(100, 50, 1280, 720), "Jogo");
    private static readonly CaptureSource Tela2 = new(SourceKind.Monitor, 1, new PixelRect(0, 0, 2560, 1440), "Tela 2");

    private static StudioRequest Request(SceneTransition transition = SceneTransition.Cut, int active = 0) => new()
    {
        Source = Tela1,
        OutputWidth = 1920, OutputHeight = 1080, Fps = 60, Encoder = Nvenc, BitrateMbps = 16, WorkingDirectory = "/tmp/x", RecordFile = "grav.mkv",
        Audio = new AudioSetup(@"\\.\pipe\mbx-audio-teste"),
        Cameras = new[] { new CameraOverlay("Webcam", CameraCorner.BottomRight, 22, true) },
        Pix = new PixOverlay("C:/x/pix.png", CameraCorner.TopRight, 14, 0, 20),
        Layers = new[] { new OverlayLayer(OverlayKind.Logo, "C:/x/logo.png", CameraCorner.TopLeft, 8, 0, 0, 0) },
        SceneCaptures = new[] { Tela1, JanelaNaTela1, Tela2 },
        Scenes = new[]
        {
            new SceneLayout("Gravação YouTube", 0, new[] { 0 }, false, new[] { 0 }),
            new SceneLayout("Jogo", 1, new[] { 0 }, true, Array.Empty<int>()),
            new SceneLayout("Tela 2", 2, Array.Empty<int>(), false, new[] { 0 }),
        },
        ActiveScene = active,
        Transition = transition,
    };

    private static string Graph(StudioRequest r)
    {
        var args = StudioCommand.Build(r);
        return args[args.ToList().IndexOf("-filter_complex") + 1];
    }

    [Fact]
    public void Todas_as_cenas_ficam_montadas_e_um_seletor_escolhe()
    {
        var r = Request(active: 1);
        Assert.True(r.LiveScenes);
        string graph = Graph(r);
        Assert.Contains("[s0][s1][s2]streamselect@cena=inputs=3:map=1[sel]", graph);
        Assert.DoesNotContain("mix@", graph);
        Assert.EndsWith("[sel]format=nv12[v]", graph);
    }

    [Fact]
    public void Cada_tela_abre_uma_vez_e_a_janela_e_um_recorte_dela()
    {
        var args = StudioCommand.Build(Request());
        string joined = string.Join(" ", args);
        Assert.Equal(1, joined.Split("ddagrab=output_idx=0").Length - 1);
        Assert.Equal(1, joined.Split("ddagrab=output_idx=1").Length - 1);
        string graph = Graph(Request());
        Assert.Contains("[0:v]hwdownload,format=bgra,split=2[cap0][cap1]", graph);
        Assert.Contains("crop=1280:720:100:50", graph);
        Assert.Contains("scale=1920:1080:force_original_aspect_ratio=decrease,pad=1920:1080", graph);
    }

    [Fact]
    public void Audio_continua_sendo_a_entrada_1()
    {
        var args = StudioCommand.Build(Request()).ToList();
        int second = args.Select((a, i) => (a, i)).Where(x => x.a == "-i").Skip(1).First().i;
        Assert.Equal(@"\\.\pipe\mbx-audio-teste", args[second + 1]);
        Assert.Contains("1:a", args);
    }

    [Fact]
    public void Camera_e_camadas_abrem_uma_vez_e_sao_copiadas_para_as_cenas()
    {
        var args = StudioCommand.Build(Request());
        Assert.Single(args, a => a == "video=Webcam");
        string graph = Graph(Request());
        Assert.Contains(",split=2[s0c0][s1c0]", graph);   // a mesma câmera nas cenas 0 e 1
        Assert.Contains(",split=2[s0l0][s2l0]", graph);   // o logo nas cenas 0 e 2
        Assert.Contains("[s1b1][s1pix]overlay", graph);   // o Pix só na cena 1
        Assert.DoesNotContain("[s0pix]", graph);
    }

    [Fact]
    public void Esmaecer_monta_dois_seletores_e_a_mistura()
    {
        string graph = Graph(Request(SceneTransition.Fade, 2));
        Assert.Contains("streamselect@cena_sai=inputs=3:map=2[ska]", graph);
        Assert.Contains("streamselect@cena_entra=inputs=3:map=2[skb]", graph);
        Assert.Contains("[ska][skb]mix@cena_mistura=inputs=2:weights='1 0':scale=1[sel]", graph);
    }

    [Fact]
    public void Imagem_no_ar_sai_por_um_cano_em_jpeg()
    {
        var r = Request() with { ProgramPipe = @"\\.\pipe\mbx-program-teste" };
        var args = StudioCommand.Build(r).ToList();
        Assert.Contains("[v]split=2[vo][pvsrc];[pvsrc]fps=15,scale=640:-2:flags=bilinear,format=yuvj420p[pv]", Graph(r));
        Assert.Equal("[vo]", args[args.IndexOf("-map") + 1]);
        int pv = args.IndexOf("[pv]");
        Assert.True(pv > 0);
        Assert.Equal("mjpeg", args[pv + 2]);
        Assert.Equal(@"\\.\pipe\mbx-program-teste", args[^1]);
    }

    [Fact]
    public void Imagem_no_ar_tambem_no_modo_simples_sem_tirar_os_quadros_da_placa()
    {
        var r = new StudioRequest
        {
            Source = Tela1, OutputWidth = 1920, OutputHeight = 1080, Fps = 60, Encoder = Nvenc, BitrateMbps = 16, WorkingDirectory = "/tmp/x",
            RecordFile = "grav.mkv", ProgramPipe = @"\\.\pipe\mbx-program-teste",
        };
        Assert.False(r.LiveScenes);
        Assert.Equal("[0:v]split=2[vo][pvsrc];[pvsrc]fps=15,hwdownload,format=bgra,scale=640:-2:flags=bilinear,format=yuvj420p[pv]", Graph(r));
    }

    [Fact]
    public void Vertical_e_uma_cena_so_continuam_no_modo_antigo()
    {
        Assert.False((Request() with { Scenes = new[] { Request().Scenes[0] } }).LiveScenes);
        Assert.False((Request() with { VerticalCopy = new VerticalLayout(1080, 1920, false, 55, new VerticalPane(PaneKind.Screen, PaneFit.Fit), new VerticalPane(PaneKind.Screen, PaneFit.Fit), false), VerticalFile = "v.mkv" }).LiveScenes);
    }

    [Fact]
    public void Problemas_das_cenas_sao_explicados()
    {
        var many = Enumerable.Range(0, StudioCommand.MaxLiveScenes + 1).Select(i => new SceneLayout("C" + i, 0, Array.Empty<int>(), false, Array.Empty<int>())).ToList();
        Assert.Contains("até", StudioCommand.Validate(Request() with { Scenes = many }));
        Assert.Contains("câmera inválida", StudioCommand.Validate(Request() with { Scenes = new[] { new SceneLayout("A", 0, new[] { 5 }, false, Array.Empty<int>()), Request().Scenes[1] } }));
        Assert.Contains("Pix", StudioCommand.Validate(Request() with { Pix = null }));
        Assert.Contains("não existe", StudioCommand.Validate(Request() with { ActiveScene = 9 }));
        Assert.Contains("No ar", StudioCommand.Validate(Request() with { ProgramPipe = "C:/arquivo.jpg" }));
        Assert.Null(StudioCommand.Validate(Request()));
    }

    [Fact]
    public void Plano_da_troca()
    {
        var cut = SceneSwitch.Plan(SceneTransition.Cut, SceneTransition.Fade, 2);
        Assert.Equal("streamselect@cena -1 map 2", Assert.Single(cut).Line);

        var quick = SceneSwitch.Plan(SceneTransition.Fade, SceneTransition.Cut, 1);
        Assert.Equal(new[] { "streamselect@cena_sai -1 map 1", "streamselect@cena_entra -1 map 1", "mix@cena_mistura -1 weights 1 0" }, quick.Select(c => c.Line));

        var fade = SceneSwitch.Plan(SceneTransition.Fade, SceneTransition.Fade, 1);
        Assert.Equal("streamselect@cena_entra -1 map 1", fade[0].Line);
        Assert.Equal("mix@cena_mistura -1 weights 0.8 0.2", fade[1].Line);
        Assert.Equal("mix@cena_mistura -1 weights 0 1", fade[SceneSwitch.FadeSteps].Line);
        Assert.Equal("streamselect@cena_sai -1 map 1", fade[^2].Line);
        Assert.Equal("mix@cena_mistura -1 weights 1 0", fade[^1].Line);
        Assert.True(SceneSwitch.IsCommandEcho("Command reply for stream 0: ret:0 res:"));
        Assert.False(SceneSwitch.IsCommandEcho("Error opening output"));
    }

    /// <summary>
    /// Roda o grafo de verdade no FFmpeg (quando ele existe na máquina), trocando as capturas e a câmera por imagens sintéticas,
    /// e troca de cena ao vivo pela entrada do FFmpeg. Confere a cor do quadro antes e depois.
    /// </summary>
    [Fact]
    public void Troca_ao_vivo_no_ffmpeg_de_verdade()
    {
        string? ffmpeg = new[] { "/usr/bin/ffmpeg", "/usr/local/bin/ffmpeg" }.FirstOrDefault(File.Exists);
        if (ffmpeg is null || OperatingSystem.IsWindows()) return;   // no Windows a captura é real (ddagrab): este teste é só para a máquina de desenvolvimento

        string dir = Path.Combine(Path.GetTempPath(), "mbx-cenas-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string logo = Path.Combine(dir, "logo.png"), pix = Path.Combine(dir, "pix.png");
            Run(ffmpeg, $"-hide_banner -loglevel error -f lavfi -i color=c=white:s=200x60 -frames:v 1 -y {logo}");
            Run(ffmpeg, $"-hide_banner -loglevel error -f lavfi -i color=c=white:s=120x120 -frames:v 1 -y {pix}");

            var r = Request(SceneTransition.Fade) with
            {
                Audio = null, OutputWidth = 320, OutputHeight = 180, Fps = 20, RecordFile = null, ReplayFolder = null,
                Encoder = new EncoderOption(VideoCodec.H264, EncoderKind.Nvenc, "libx264"),
                Pix = new PixOverlay(pix, CameraCorner.TopRight, 14, 0, 20),
                Layers = new[] { new OverlayLayer(OverlayKind.Logo, logo, CameraCorner.TopLeft, 8, 0, 0, 0) },
                SceneCaptures = new[] { Tela1, JanelaNaTela1, Tela2 },
                Stream = null,
            };
            r = r with { RecordFile = "x.mkv" };
            var args = StudioCommand.Build(r).ToList();
            string graph = args[args.IndexOf("-filter_complex") + 1].Replace("hwdownload,format=bgra", "format=bgra");
            var real = new List<string> { "-hide_banner", "-loglevel", "info", "-nostats", "-re" };
            // tela 1 vermelha, tela 2 azul, câmera verde
            real.AddRange(new[] { "-f", "lavfi", "-i", "color=c=red:s=1920x1080:r=20", "-f", "lavfi", "-i", "color=c=blue:s=2560x1440:r=20", "-f", "lavfi", "-i", "color=c=green:s=640x360:r=20" });
            real.AddRange(new[] { "-loop", "1", "-framerate", "5", "-i", pix, "-loop", "1", "-framerate", "25", "-i", logo });
            real.AddRange(new[] { "-filter_complex", graph + ";[v]format=rgb24[o]", "-map", "[o]", "-t", "4", "-f", "rawvideo", "-y", Path.Combine(dir, "out.raw") });

            var info = new ProcessStartInfo(ffmpeg) { RedirectStandardInput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (string a in real) info.ArgumentList.Add(a);
            using var process = Process.Start(info)!;
            var errors = process.StandardError.ReadToEndAsync();
            Thread.Sleep(1500);
            foreach (var command in SceneSwitch.Plan(SceneTransition.Fade, SceneTransition.Fade, 2))
            {
                if (command.DelayMs > 0) Thread.Sleep(command.DelayMs);
                process.StandardInput.Write("c" + command.Line + "\n");
                process.StandardInput.Flush();
            }
            Assert.True(process.WaitForExit(30000), "o FFmpeg não terminou");
            Assert.True(process.ExitCode == 0, errors.Result);

            byte[] data = File.ReadAllBytes(Path.Combine(dir, "out.raw"));
            int frame = 320 * 180 * 3;
            int frames = data.Length / frame;
            Assert.True(frames > 40, $"poucos quadros: {frames}");
            (int R, int G, int B) Center(int f) { int o = f * frame + (90 * 320 + 160) * 3; return (data[o], data[o + 1], data[o + 2]); }
            var first = Center(2);
            var last = Center(frames - 2);
            Assert.True(first.R > 200 && first.B < 50, $"começo deveria ser a tela 1 (vermelha): {first}");
            Assert.True(last.B > 200 && last.R < 50, $"fim deveria ser a tela 2 (azul): {last}");
            Assert.Contains(Enumerable.Range(2, frames - 4).Select(Center), c => c.R is > 60 and < 200 && c.B is > 60 and < 200);   // passou pela mistura
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    private static void Run(string exe, string arguments)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = false, RedirectStandardError = true })!;
        p.StandardError.ReadToEnd();
        p.WaitForExit();
    }
}
