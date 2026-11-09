using System.Diagnostics;
using MBExtreme.Core.Recording;
using Xunit;

namespace MBExtreme.Core.Tests;

public class GravacaoVerticalTests
{
    private static readonly EncoderOption Nvenc = new(VideoCodec.H264, EncoderKind.Nvenc, "h264_nvenc");
    private static readonly EncoderOption NvencHevc = new(VideoCodec.Hevc, EncoderKind.Nvenc, "hevc_nvenc");
    private static readonly EncoderOption Amf = new(VideoCodec.H264, EncoderKind.Amf, "h264_amf");
    private static readonly EncoderOption Qsv = new(VideoCodec.Av1, EncoderKind.Qsv, "av1_qsv");
    private static readonly EncoderOption X264 = new(VideoCodec.H264, EncoderKind.Software, "libx264");

    private static VerticalLayout Layout() =>
        new(1080, 1920, true, 55, new VerticalPane(PaneKind.Screen, PaneFit.Fit), new VerticalPane(PaneKind.Screen, PaneFit.Fill, 0, 100, 250), true);

    private static StudioRequest Copy() => new()
    {
        Source = new CaptureSource(SourceKind.Monitor, 0, new PixelRect(0, 0, 1920, 1080), "Tela 1"),
        OutputWidth = 1920, OutputHeight = 1080, Fps = 60, Encoder = Nvenc, BitrateMbps = 16, WorkingDirectory = "/tmp/x",
        RecordFile = "grav.mkv", VerticalCopy = Layout(), VerticalFile = "grav-vertical.mkv",
    };

    private static List<string> AfterVertical(StudioRequest r)
    {
        var args = StudioCommand.Build(r).ToList();
        return args.Skip(args.IndexOf("[vv]")).ToList();
    }

    [Fact]
    public void Qualidade_constante_por_marca_de_placa()
    {
        Assert.Equal(new[] { "-rc", "vbr", "-cq", "17", "-b:v", "0" }, StudioCommand.ConstantQualityArgs(Nvenc, 3));
        Assert.Equal(new[] { "-rc", "cqp", "-qp_i", "20", "-qp_p", "20" }, StudioCommand.ConstantQualityArgs(Amf, 2));
        Assert.Equal(new[] { "-global_quality", "31" }, StudioCommand.ConstantQualityArgs(Qsv, 1));   // AV1 usa escala mais alta
        Assert.Equal(new[] { "-crf", "17" }, StudioCommand.ConstantQualityArgs(X264, 3));
    }

    [Fact]
    public void Gravacao_vertical_com_codificador_proprio_e_qualidade_maxima()
    {
        var after = AfterVertical(Copy() with { VerticalEncoder = NvencHevc, VerticalQualityLevel = 3, VerticalFps = 60, VerticalEffort = EncoderQuality.Quality });
        Assert.Equal("hevc_nvenc", after[after.IndexOf("-c:v") + 1]);
        Assert.Equal("p6", after[after.IndexOf("-preset") + 1]);
        Assert.Equal("17", after[after.IndexOf("-cq") + 1]);
        Assert.Equal("main", after[after.IndexOf("-profile:v") + 1]);
        Assert.Contains("bt709", after);
        Assert.Equal("120", after[after.IndexOf("-g") + 1]);
    }

    [Fact]
    public void Taxa_escolhida_e_perfil_high_no_h264()
    {
        var after = AfterVertical(Copy() with { VerticalBitrateMbps = 25 });
        Assert.Equal("25M", after[after.IndexOf("-b:v") + 1]);
        Assert.Equal("high", after[after.IndexOf("-profile:v") + 1]);
    }

    [Fact]
    public void Com_a_live_vertical_o_codec_proprio_so_vale_se_for_h264()
    {
        var r = Copy() with { VerticalEncoder = NvencHevc, VerticalQualityLevel = 3, VerticalStream = VerticalLive.Target(VerticalPlatform.Instagram, null, "abc") };
        var after = AfterVertical(r);
        Assert.Equal("h264_nvenc", after[after.IndexOf("-c:v") + 1]);
        Assert.Equal("4000k", after[after.IndexOf("-b:v") + 1]);
        Assert.DoesNotContain("-cq", after);
    }

    [Fact]
    public void Video_principal_vertical_em_qualidade_constante_so_sem_transmissao()
    {
        var r = new StudioRequest
        {
            Source = new CaptureSource(SourceKind.Monitor, 0, new PixelRect(0, 0, 1920, 1080), "Tela 1"),
            OutputWidth = 1080, OutputHeight = 1920, Fps = 60, Encoder = Nvenc, BitrateMbps = 16, WorkingDirectory = "/tmp/x",
            RecordFile = "v.mp4", Vertical = Layout(), QualityLevel = 2,
        };
        var args = StudioCommand.Build(r).ToList();
        Assert.Equal("20", args[args.IndexOf("-cq") + 1]);
        Assert.Contains("-profile:v", args);
        var live = StudioCommand.Build(r with { Stream = new StreamTarget(StreamService.YouTube, "", "k1234") }).ToList();
        Assert.DoesNotContain("-cq", live);
    }

    [Fact]
    public void Predefinicoes_e_escolha_do_codificador()
    {
        var encoders = new[] { X264, Nvenc, NvencHevc };
        Assert.Equal("hevc_nvenc", SocialVideo.PickEncoder(encoders, VideoCodec.Hevc)!.FfmpegName);
        Assert.Equal("h264_nvenc", SocialVideo.PickEncoder(encoders, VideoCodec.Av1)!.FfmpegName);   // sem AV1: o melhor H.264
        Assert.Equal(4, SocialVideo.All.Count);
        Assert.Equal(1080, VerticalFormat.ByIndex(SocialVideo.For(SocialTarget.Reels).Format).Width);
        Assert.Equal(2160, VerticalFormat.ByIndex(SocialVideo.For(SocialTarget.Maximum).Format).Width);
        Assert.EndsWith("grav-vertical-postar.mp4", SocialVideo.PostName("/v/grav-vertical.mkv"));
    }

    /// <summary>Grava um vertical de teste com o processador em qualidade constante e prepara o MP4 para postar (quando há FFmpeg na máquina).</summary>
    [Fact]
    public void Arquivo_pronto_para_postar_no_ffmpeg_de_verdade()
    {
        string? ffmpeg = new[] { "/usr/bin/ffmpeg", "/usr/local/bin/ffmpeg" }.FirstOrDefault(File.Exists);
        if (ffmpeg is null || OperatingSystem.IsWindows()) return;
        string dir = Path.Combine(Path.GetTempPath(), "mbx-vert-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string raw = Path.Combine(dir, "v.mkv"), post = SocialVideo.PostName(raw);
            var enc = new List<string> { "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc2=s=360x640:r=30:d=2", "-f", "lavfi", "-i", "sine=d=2", "-c:v", "libx264" };
            enc.AddRange(StudioCommand.ConstantQualityArgs(X264, 3));
            enc.AddRange(StudioCommand.SocialArgs(X264));
            enc.AddRange(new[] { "-pix_fmt", "yuv420p", "-c:a", "aac", "-y", raw });
            Assert.Equal(0, Run(ffmpeg, enc));
            Assert.Equal(0, Run(ffmpeg, SocialVideo.PostArgs(raw, post)));
            byte[] head = File.ReadAllBytes(post).Take(4096).ToArray();
            string text = System.Text.Encoding.ASCII.GetString(head);
            Assert.True(text.IndexOf("moov", StringComparison.Ordinal) is >= 0 and var moov && moov < (text.IndexOf("mdat", StringComparison.Ordinal) is var mdat && mdat >= 0 ? mdat : int.MaxValue), "o índice (moov) deveria vir antes dos dados");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    private static int Run(string exe, IEnumerable<string> args)
    {
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardError = true };
        foreach (string a in args) info.ArgumentList.Add(a);
        using var p = Process.Start(info)!;
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    }
}
