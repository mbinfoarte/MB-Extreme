using MBExtreme.Core.Recording;
using Xunit;

namespace MBExtreme.Core.Tests;

public class ShortsTests
{
    private static readonly EncoderOption Nvenc = new(VideoCodec.H264, EncoderKind.Nvenc, "h264_nvenc");
    private static readonly VerticalLayout Fit = new(1080, 1920, false, 50, new VerticalPane(PaneKind.Screen, PaneFit.Fit), new VerticalPane(PaneKind.Screen), false);
    private static readonly VerticalLayout Split = new(1080, 1920, true, 55, new VerticalPane(PaneKind.Screen, PaneFit.Fit), new VerticalPane(PaneKind.Screen, PaneFit.Fill, 100, 100, 250), true);

    [Theory]
    [InlineData("83", 83.0)]
    [InlineData("1:23", 83.0)]
    [InlineData("01:02:03", 3723.0)]
    [InlineData("1:02,5", 62.5)]
    [InlineData(" 7 ", 7.0)]
    public void Tempo_digitado_e_entendido(string text, double expected) => Assert.Equal(expected, TimeText.Parse(text)!.Value, 3);

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1:2:3:4")]
    [InlineData("-5")]
    [InlineData("1:xx")]
    public void Tempo_invalido_e_recusado(string text) => Assert.Null(TimeText.Parse(text));

    [Fact]
    public void Tempo_volta_para_texto()
    {
        Assert.Equal("1:23", TimeText.Format(83.4));
        Assert.Equal("0:05", TimeText.Format(5));
        Assert.Equal("1:02:03", TimeText.Format(3723));
        Assert.Equal("0:00", TimeText.Format(-4));
    }

    [Fact]
    public void Duracao_e_tamanho_saem_do_texto_do_ffmpeg()
    {
        const string text = "Input #0, matroska,webm, from 'g.mkv':\n  Duration: 00:42:10.50, start: 0.000000, bitrate: 17000 kb/s\n  Stream #0:0: Video: h264 (High), yuv420p(tv, bt709), 1920x1080 [SAR 1:1 DAR 16:9], 60 fps\n  Stream #0:1: Audio: aac (LC), 48000 Hz, stereo";
        Assert.Equal(42 * 60 + 10.5, MediaProbe.ParseDuration(text)!.Value, 2);
        Assert.Equal((1920, 1080), MediaProbe.ParseVideoSize(text));
        Assert.True(MediaProbe.HasAudio(text));
        Assert.False(MediaProbe.HasAudio("Stream #0:0: Video: h264"));
        Assert.Null(MediaProbe.ParseDuration("sem nada"));
    }

    [Fact]
    public void Volume_por_segundo_e_lido_da_saida()
    {
        const string text = "frame:0    pts:0       pts_time:0\nlavfi.astats.Overall.RMS_level=-30.5\nframe:1    pts:16000   pts_time:1\nlavfi.astats.Overall.RMS_level=-inf\nframe:2 pts:32000 pts_time:2\nlavfi.astats.Overall.RMS_level=-12.25\n";
        var s = MediaProbe.ParseLoudness(text);
        Assert.Equal(3, s.Count);
        Assert.Equal((0.0, -30.5), s[0]);
        Assert.Equal(-100, s[1].Db);
        Assert.Equal((2.0, -12.25), s[2]);
    }

    [Fact]
    public void Corte_por_tempo_cobre_o_video_sem_sobras_pequenas()
    {
        var parts = ShortsPlanner.FixedSplit(100, 30);
        Assert.Equal(4, parts.Count);
        Assert.Equal(0, parts[0].Start);
        Assert.Equal(90, parts[3].Start);
        Assert.Equal(100, parts[3].End);
        Assert.All(parts, p => Assert.True(p.IsValid));
        Assert.Equal(3, ShortsPlanner.FixedSplit(91, 30).Count);                        // sobra de 1 s não vira vídeo
        Assert.Empty(ShortsPlanner.FixedSplit(2, 30));
    }

    [Fact]
    public void Picos_de_volume_viram_cortes_sem_sobreposicao()
    {
        var series = new List<(double, double)>();
        var rng = new Random(3);
        for (int t = 0; t < 600; t++) series.Add((t, -35 + rng.NextDouble() * 4));
        foreach (int peak in new[] { 100, 300, 301, 450 }) series[peak] = (peak, -8);
        var cuts = ShortsPlanner.FromLoudness(series, 600, 5);
        Assert.Equal(3, cuts.Count);                                                    // 300 e 301 são o mesmo momento
        Assert.True(cuts[0].Start < cuts[1].Start);
        for (int i = 1; i < cuts.Count; i++) Assert.True(cuts[i].Start >= cuts[i - 1].End);
        Assert.All(cuts, c => Assert.True(c.IsValid));
        Assert.Contains(cuts, c => c.Start <= 100 && c.End >= 100);
        Assert.StartsWith("Momento 1 (", cuts[0].Name);
    }

    [Fact]
    public void Sem_picos_nao_inventa_cortes()
    {
        var flat = Enumerable.Range(0, 300).Select(t => ((double)t, -30.0)).ToList();
        Assert.Empty(ShortsPlanner.FromLoudness(flat, 300, 5));
        Assert.Empty(ShortsPlanner.FromLoudness(flat.Take(3).ToList(), 3, 5));
    }

    [Fact]
    public void Comando_do_corte_vertical()
    {
        var r = new ShortsRequest(@"C:\Videos\jogo.mkv", new ShortSegment(125.5, 160, "Gol"), Fit, Nvenc, @"C:\Videos\Shorts\gol.mp4", true);
        var a = ShortsCommand.Build(r).ToList();
        Assert.Equal("-ss", a[a.IndexOf("-ss")]);
        Assert.Equal("125.5", a[a.IndexOf("-ss") + 1]);
        Assert.Equal("34.5", a[a.IndexOf("-t") + 1]);
        Assert.Equal(@"C:\Videos\jogo.mkv", a[a.IndexOf("-i") + 1]);
        string g = a[a.IndexOf("-filter_complex") + 1];
        Assert.StartsWith("[0:v]format=bgra[scr];[scr]split=2", g);
        Assert.Contains("[b0]fps=30,format=yuv420p[v]", g);
        Assert.Contains("0:a:0", a);
        Assert.Equal(@"C:\Videos\Shorts\gol.mp4", a[^1]);
        Assert.Contains("+faststart", a);
        Assert.Equal("h264_nvenc", a[a.IndexOf("-c:v") + 1]);
    }

    [Fact]
    public void Corte_dividido_recorta_a_mesma_tela_duas_vezes_e_sem_audio_nao_mapeia()
    {
        var r = new ShortsRequest("a.mp4", new ShortSegment(0, 30, "x"), Split, Nvenc, "b.mp4", false);
        var a = ShortsCommand.Build(r).ToList();
        string g = a[a.IndexOf("-filter_complex") + 1];
        Assert.Contains("[scr]split=2[sa][sb]", g);
        Assert.Contains("(iw-ow)*100/100:(ih-oh)*100/100", g);
        Assert.DoesNotContain("0:a:0", a);
    }

    [Fact]
    public void Arquivo_de_fundo_entra_como_segunda_entrada()
    {
        var layout = Split with { Bottom = new VerticalPane(PaneKind.File, Source: @"C:\f\fundo.mp4") };
        var a = ShortsCommand.Build(new ShortsRequest("a.mp4", new ShortSegment(0, 30, "x"), layout, Nvenc, "b.mp4", true)).ToList();
        Assert.Contains("-stream_loop", a);
        Assert.Contains("[1:v]format=bgra,scale=", a[a.IndexOf("-filter_complex") + 1]);
    }

    [Fact]
    public void Validacoes()
    {
        ShortsRequest R(ShortSegment s, VerticalLayout? l = null, string input = "a.mp4") => new(input, s, l ?? Fit, Nvenc, "b.mp4", true);
        Assert.Null(ShortsCommand.Validate(R(new ShortSegment(0, 30, "ok"))));
        Assert.NotNull(ShortsCommand.Validate(R(new ShortSegment(0, 1, "curto"))));
        Assert.NotNull(ShortsCommand.Validate(R(new ShortSegment(0, 500, "longo"))));
        Assert.NotNull(ShortsCommand.Validate(R(new ShortSegment(-2, 30, "negativo"))));
        Assert.NotNull(ShortsCommand.Validate(R(new ShortSegment(0, 30, "x"), input: "a';b.mp4")));
        Assert.NotNull(ShortsCommand.Validate(R(new ShortSegment(0, 30, "x"), Split with { Bottom = new VerticalPane(PaneKind.Camera, Source: "Cam") })));
        Assert.Throws<InvalidOperationException>(() => ShortsCommand.Build(R(new ShortSegment(0, 1, "x"))));
    }

    [Fact]
    public void Analise_de_volume_pede_saida_em_texto()
    {
        var a = ShortsCommand.LoudnessArgs("v.mkv");
        Assert.Contains("-vn", a);
        Assert.Contains(a, x => x.Contains("astats") && x.Contains("RMS_level") && x.Contains("file=-"));
        Assert.Equal(new[] { "-hide_banner", "-i", "v.mkv" }, ShortsCommand.ProbeArgs("v.mkv"));
    }

    [Theory]
    [InlineData("Gol de bicicleta!", "Gol-de-bicicleta")]
    [InlineData("a/b\\c:d", "a-b-c-d")]
    [InlineData("???", "corte")]
    public void Nome_de_arquivo_seguro(string text, string expected) => Assert.Equal(expected, ShortsCommand.SafeName(text));

    [Fact]
    public void Previa_e_um_quadro_reduzido_montado_pelo_mesmo_desenho()
    {
        var r = new ShortsRequest(@"C:\Videos\jogo.mkv", new ShortSegment(0, 10, "x"), Split, Nvenc, "x.mp4", true);
        var a = ShortsCommand.PreviewArgs(r, 83.5, @"C:\tmp\prev.png").ToList();
        Assert.Equal("83.5", a[a.IndexOf("-ss") + 1]);
        Assert.Equal(@"C:\Videos\jogo.mkv", a[a.IndexOf("-i") + 1]);
        Assert.Equal("1", a[a.IndexOf("-frames:v") + 1]);
        Assert.Equal(@"C:\tmp\prev.png", a[^1]);
        Assert.DoesNotContain("-c:a", a);
        Assert.DoesNotContain("-re", a);
        string g = a[a.IndexOf("-filter_complex") + 1];
        Assert.Contains("pad=360:640:0:0:black", g);                       // 1080x1920 reduzido para 360 de largura
        Assert.EndsWith("[b0]format=rgb24[v]", g);
        Assert.Contains("[scr]split=2[sa][sb]", g);
    }

    [Fact]
    public void Previa_com_fundo_em_video_nao_usa_tempo_real_e_recusa_entradas_ruins()
    {
        var layout = Split with { Bottom = new VerticalPane(PaneKind.File, Source: @"C:\f\fundo.mp4") };
        var a = ShortsCommand.PreviewArgs(new ShortsRequest("a.mp4", new ShortSegment(0, 10, "x"), layout, Nvenc, "b.mp4", true), -5, "p.png", 99999).ToList();
        Assert.Contains("-stream_loop", a);
        Assert.DoesNotContain("-re", a);
        Assert.Equal("0", a[a.IndexOf("-ss") + 1]);                         // tempo negativo vira zero
        Assert.Contains("pad=1080:1920", a[a.IndexOf("-filter_complex") + 1]);   // largura máxima do formato
        Assert.Throws<InvalidOperationException>(() => ShortsCommand.PreviewArgs(new ShortsRequest("a';b.mp4", new ShortSegment(0, 10, "x"), Fit, Nvenc, "b.mp4", true), 1, "p.png"));
        Assert.Throws<InvalidOperationException>(() => ShortsCommand.PreviewArgs(new ShortsRequest("a.mp4", new ShortSegment(0, 10, "x"), Fit, Nvenc, "b.mp4", true), 1, "p;.png"));
        Assert.Throws<InvalidOperationException>(() => ShortsCommand.PreviewArgs(new ShortsRequest("a.mp4", new ShortSegment(0, 10, "x"), Split with { Bottom = new VerticalPane(PaneKind.Camera, Source: "Cam") }, Nvenc, "b.mp4", true), 1, "p.png"));
    }
}
