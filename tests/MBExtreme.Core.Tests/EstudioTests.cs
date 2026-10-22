using MBExtreme.Core.Recording;
using NAudio.Wave;
using Xunit;

namespace MBExtreme.Core.Tests;

public class EstudioTests
{
    private static readonly EncoderOption Nvenc = new(VideoCodec.H264, EncoderKind.Nvenc, "h264_nvenc");
    private static readonly EncoderOption Hevc = new(VideoCodec.Hevc, EncoderKind.Nvenc, "hevc_nvenc");
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

    private static string Joined(StudioRequest r) => string.Join(" ", StudioCommand.Build(r));

    [Fact]
    public void Gravacao_na_resolucao_da_tela_mantem_os_quadros_na_gpu()
    {
        string cmd = Joined(Request());
        Assert.Contains("ddagrab=output_idx=0:framerate=60:draw_mouse=1", cmd);
        Assert.DoesNotContain("hwdownload", cmd);
        Assert.Contains("-f tee [f=matroska]grav.mkv", cmd);
        Assert.Contains("-flags +global_header", cmd);
    }

    [Fact]
    public void Resolucao_diferente_redimensiona_na_cpu()
    {
        string cmd = Joined(Request(r => r with { OutputWidth = 1280, OutputHeight = 720 }));
        Assert.Contains("hwdownload,format=bgra,scale=1280:720", cmd);
    }

    [Fact]
    public void Quick_Sync_nunca_usa_quadros_de_gpu_direto()
    {
        string cmd = Joined(Request(r => r with { Encoder = Qsv }));
        Assert.Contains("hwdownload", cmd);
    }

    [Fact]
    public void Janela_em_regiao_usa_recorte_do_ddagrab()
    {
        var source = new CaptureSource(SourceKind.WindowRegion, 1, new PixelRect(100, 40, 1280, 720), "Janela");
        string cmd = Joined(Request(r => r with { Source = source, OutputWidth = 1280, OutputHeight = 720 }));
        Assert.Contains("ddagrab=output_idx=1:framerate=60:draw_mouse=1:video_size=1280x720:offset_x=100:offset_y=40", cmd);
        Assert.DoesNotContain("hwdownload", cmd);
    }

    [Fact]
    public void Janela_gdi_usa_gdigrab_com_o_titulo_e_barras_pretas()
    {
        var source = new CaptureSource(SourceKind.WindowGdi, 0, new PixelRect(0, 0, 1000, 600), "Janela (GDI)", "Meu Jogo");
        var args = StudioCommand.Build(Request(r => r with { Source = source, OutputWidth = 1280, OutputHeight = 720 }));
        Assert.Contains("gdigrab", args);
        Assert.Contains("title=Meu Jogo", args);
        Assert.Contains(args, a => a.Contains("pad=1280:720"));
    }

    [Fact]
    public void Audio_entra_como_segunda_entrada_e_e_codificado_em_aac()
    {
        string cmd = Joined(Request(r => r with { Audio = new AudioSetup(@"\\.\pipe\mbx-audio-x") }));
        Assert.Contains(@"-f s16le -ar 48000 -ac 2", cmd);
        Assert.Contains(@"-i \\.\pipe\mbx-audio-x", cmd);
        Assert.Contains("-map 1:a -c:a aac", cmd);
    }

    [Fact]
    public void Saidas_ligadas_viram_destinos_do_tee()
    {
        var stream = new StreamTarget(StreamService.Twitch, "", "live_123_abcDEF");
        var args = StudioCommand.Build(Request(r => r with { Stream = stream, ReplayFolder = "replay", ReplaySeconds = 60 }));
        string tee = args[^1];
        Assert.Contains("[f=matroska]grav.mkv", tee);
        Assert.Contains("[f=flv:flvflags=no_duration_filesize:onfail=ignore]rtmp://live.twitch.tv/app/live_123_abcDEF", tee);
        Assert.Contains($"segment_time={StudioCommand.SegmentSeconds}", tee);
        Assert.Contains($"segment_wrap={StudioCommand.ReplaySegmentCount(60)}", tee);
        Assert.Contains("replay/seg%03d.mkv", tee);
        Assert.Equal(2, tee.Count(c => c == '|'));
    }

    [Fact]
    public void Mp4_grava_fragmentado_para_sobreviver_a_travamentos()
    {
        string tee = StudioCommand.Build(Request(r => r with { RecordFile = "grav.mp4", RecordMp4 = true }))[^1];
        Assert.Contains("movflags=frag_keyframe+empty_moov", tee);
    }

    [Fact]
    public void Transmissao_limita_a_taxa_e_usa_cbr()
    {
        var stream = new StreamTarget(StreamService.YouTube, "", "abcd-efgh-ijkl-mnop");
        var args = StudioCommand.Build(Request(r => r with { Stream = stream, BitrateMbps = 40 }));
        int i = args.ToList().IndexOf("-b:v");
        Assert.Equal("8M", args[i + 1]);
        Assert.Equal("8M", args[args.ToList().IndexOf("-maxrate") + 1]);
    }

    [Fact]
    public void Sem_saida_nao_e_valido() =>
        Assert.Contains("Ligue pelo menos uma saída", StudioCommand.Validate(Request(x => x with { RecordFile = null })));

    [Fact]
    public void Transmissao_exige_h264_chave_e_endereco_valido()
    {
        var good = new StreamTarget(StreamService.Twitch, "", "live_1");
        Assert.Null(StudioCommand.Validate(Request(r => r with { Stream = good })));
        Assert.Contains("H.264", StudioCommand.Validate(Request(r => r with { Stream = good, Encoder = Hevc })));
        Assert.Contains("chave", StudioCommand.Validate(Request(r => r with { Stream = new StreamTarget(StreamService.Twitch, "", "") })));
        Assert.Contains("caracteres", StudioCommand.Validate(Request(r => r with { Stream = new StreamTarget(StreamService.Twitch, "", "abc|def") })));
        Assert.Contains("rtmp", StudioCommand.Validate(Request(r => r with { Stream = new StreamTarget(StreamService.Custom, "http://exemplo.com/live", "k") })));
        Assert.Null(StudioCommand.Validate(Request(r => r with { Stream = new StreamTarget(StreamService.Custom, "rtmps://exemplo.com:443/live", "k1") })));
    }

    [Fact]
    public void Chave_nunca_aparece_no_texto_mostrado()
    {
        var stream = new StreamTarget(StreamService.Twitch, "", "live_123_segredo");
        var request = Request(r => r with { Stream = stream });
        Assert.DoesNotContain("segredo", StudioCommand.Describe(request));
        Assert.DoesNotContain("segredo", StudioCommand.MaskSecrets("Failed to open rtmp://live.twitch.tv/app/live_123_segredo", stream));
    }

    [Theory]
    [InlineData(2560, 1440, 1080, 1920, 1080)]
    [InlineData(1920, 1080, 0, 1920, 1080)]
    [InlineData(1920, 1080, 1440, 1920, 1080)]
    [InlineData(1281, 721, 0, 1280, 720)]
    [InlineData(1000, 600, 360, 600, 360)]
    public void Tamanho_de_saida_mantem_a_proporcao_com_medidas_pares(int sw, int sh, int target, int ew, int eh) =>
        Assert.Equal((ew, eh), StudioCommand.FitOutput(sw, sh, target));

    // ---- telas e janelas

    private static readonly DisplayInfo Main = new(0, "\\\\.\\DISPLAY1", new PixelRect(0, 0, 2560, 1440), true);
    private static readonly DisplayInfo Side = new(1, "\\\\.\\DISPLAY2", new PixelRect(2560, 0, 1920, 1080), false);

    private static WindowInfo Window(string title, PixelRect bounds, string cls = "Game", bool visible = true, bool cloaked = false,
        bool minimized = false, bool tool = false, bool owner = false, int pid = 10) =>
        new(IntPtr.Zero, title, "jogo", cls, bounds, visible, cloaked, minimized, tool, owner, pid);

    [Fact]
    public void Lista_de_janelas_ignora_o_que_nao_faz_sentido_gravar()
    {
        var area = new PixelRect(100, 100, 800, 600);
        Assert.True(CaptureRules.IsCapturable(Window("Meu jogo", area), 1));
        Assert.False(CaptureRules.IsCapturable(Window("", area), 1));
        Assert.False(CaptureRules.IsCapturable(Window("x", area, visible: false), 1));
        Assert.False(CaptureRules.IsCapturable(Window("x", area, cloaked: true), 1));
        Assert.False(CaptureRules.IsCapturable(Window("x", area, minimized: true), 1));
        Assert.False(CaptureRules.IsCapturable(Window("x", area, tool: true), 1));
        Assert.False(CaptureRules.IsCapturable(Window("x", area, owner: true), 1));
        Assert.False(CaptureRules.IsCapturable(Window("MB Extreme", area, pid: 1), 1));
        Assert.False(CaptureRules.IsCapturable(Window("Barra", area, cls: "Shell_TrayWnd"), 1));
        Assert.False(CaptureRules.IsCapturable(Window("pequena", new PixelRect(0, 0, 50, 50)), 1));
    }

    [Fact]
    public void Janela_dentro_da_tela_vira_recorte_relativo_a_ela()
    {
        var result = CaptureRules.RegionOnDisplay(new PixelRect(3001, 101, 1281, 721), new[] { Main, Side });
        Assert.NotNull(result);
        Assert.Equal(1, result!.Value.Display.Index);
        Assert.Equal(new PixelRect(440, 100, 1280, 720), result.Value.Region);
    }

    [Fact]
    public void Janela_que_passa_da_borda_e_cortada()
    {
        var result = CaptureRules.RegionOnDisplay(new PixelRect(2000, 900, 1200, 800), new[] { Main });
        Assert.Equal(new PixelRect(2000, 900, 560, 540), result!.Value.Region);
    }

    [Fact]
    public void Janela_em_tela_cheia_captura_o_monitor_inteiro()
    {
        var w = Window("Jogo", new PixelRect(0, 0, 2560, 1440));
        var source = CaptureRules.ForWindow(w, new[] { Main, Side }, gdi: false);
        Assert.Equal(SourceKind.Monitor, source!.Kind);
        var windowed = CaptureRules.ForWindow(Window("Jogo", new PixelRect(200, 100, 1280, 720)), new[] { Main }, gdi: false);
        Assert.Equal(SourceKind.WindowRegion, windowed!.Kind);
        var gdi = CaptureRules.ForWindow(Window("Jogo", new PixelRect(200, 100, 1281, 721)), new[] { Main }, gdi: true);
        Assert.Equal(SourceKind.WindowGdi, gdi!.Kind);
        Assert.Equal(1280, gdi.Area.Width);
        Assert.Equal("Jogo", gdi.WindowTitle);
    }

    // ---- replay

    private static List<SegmentFile> Segments(params int[] secondsAgo)
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        return secondsAgo.Select((s, i) => new SegmentFile($"seg{i:000}.mkv", now.AddSeconds(-s))).ToList();
    }

    [Fact]
    public void Replay_ignora_o_pedaco_em_gravacao_e_pega_os_mais_recentes_em_ordem()
    {
        // 0 = em gravação agora; 4, 8, 12, ... = pedaços completos
        var files = Segments(0, 4, 8, 12, 16, 20);
        var picked = ReplayBuffer.SelectSegments(files, 12);
        Assert.Equal(3, picked.Count);
        Assert.True(picked[0].WrittenUtc < picked[2].WrittenUtc);
        Assert.Equal(files[3].WrittenUtc, picked[0].WrittenUtc);
        Assert.Equal(files[1].WrittenUtc, picked[2].WrittenUtc);
    }

    [Fact]
    public void Replay_para_quando_ha_buraco_de_uma_sessao_antiga()
    {
        var picked = ReplayBuffer.SelectSegments(Segments(0, 4, 8, 600, 604), 60);
        Assert.Equal(2, picked.Count);
    }

    [Fact]
    public void Replay_sem_pedacos_completos_devolve_vazio()
    {
        Assert.Empty(ReplayBuffer.SelectSegments(Segments(0), 30));
        Assert.Empty(ReplayBuffer.SelectSegments(Array.Empty<SegmentFile>(), 30));
    }

    [Fact]
    public void Lista_do_concat_escapa_aspas_e_barras()
    {
        string text = ReplayBuffer.ConcatList(new[] { @"C:\Vídeos\it's\seg001.mkv" });
        Assert.Equal("file 'C:/Vídeos/it'\\''s/seg001.mkv'\n", text);
    }

    [Fact]
    public void Nome_do_clipe_e_seguro()
    {
        var when = new DateTime(2026, 3, 4, 5, 6, 7);
        Assert.Equal("Replay_20260304_050607.mp4", ReplayBuffer.ClipName(null, when));
        Assert.Equal("Replay_Meu_Jogo_20260304_050607.mp4", ReplayBuffer.ClipName("Meu Jogo:/\\*?", when));
    }

    // ---- progresso

    [Fact]
    public void Progresso_do_ffmpeg_e_lido_linha_a_linha()
    {
        var p = new FfmpegProgress();
        foreach (string line in new[] { "fps=59.94", "bitrate=7890.5kbits/s", "total_size=1048576", "out_time_us=5500000", "drop_frames=3", "dup_frames=1", "speed=1.01x", "progress=continue" })
            p.Feed(line);
        Assert.Equal(59.94, p.Fps, 2);
        Assert.Equal(7890.5, p.BitrateKbps, 1);
        Assert.Equal(1048576, p.TotalBytes);
        Assert.Equal(TimeSpan.FromSeconds(5.5), p.Elapsed);
        Assert.Equal(3, p.DroppedFrames);
        Assert.Equal(1.01, p.Speed, 2);
        p.Feed("bitrate=N/A");
        Assert.Equal(0, p.BitrateKbps);
    }

    // ---- áudio

    private static byte[] Tone16(int sampleRate, int seconds, double hz, short amplitude)
    {
        var bytes = new byte[sampleRate * seconds * 2];
        for (int i = 0; i < sampleRate * seconds; i++)
        {
            short v = (short)(Math.Sin(2 * Math.PI * hz * i / sampleRate) * amplitude);
            bytes[i * 2] = (byte)(v & 0xFF);
            bytes[i * 2 + 1] = (byte)(v >> 8);
        }
        return bytes;
    }

    private static (int Peak, int Count) Stats(byte[] pcm, int frames)
    {
        int peak = 0;
        for (int i = 0; i < frames * 2; i++) peak = Math.Max(peak, Math.Abs((short)(pcm[i * 2] | pcm[i * 2 + 1] << 8)));
        return (peak, frames);
    }

    [Fact]
    public void Mixer_sem_fontes_gera_silencio()
    {
        var mixer = new AudioMixer();
        var buffer = new byte[4800 * AudioMixer.BytesPerFrame];
        Assert.Equal(4800, mixer.Read(buffer, 4800));
        Assert.All(buffer, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Mixer_converte_mono_44100_para_estereo_48000()
    {
        var mixer = new AudioMixer();
        var input = mixer.AddInput("teste", new WaveFormat(44100, 16, 1));
        var tone = Tone16(44100, 1, 440, 10000);
        input.Push(tone, tone.Length);

        var buffer = new byte[24000 * AudioMixer.BytesPerFrame];
        mixer.Read(buffer, 24000);
        var (peak, _) = Stats(buffer, 24000);
        Assert.InRange(peak, 9000, 11000);
        // estéreo: esquerda == direita
        for (int frame = 100; frame < 200; frame++)
            Assert.Equal(buffer[frame * 4], buffer[frame * 4 + 2]);
    }

    [Fact]
    public void Volume_da_fonte_atenua_o_som()
    {
        var mixer = new AudioMixer();
        var input = mixer.AddInput("teste", new WaveFormat(48000, 16, 2), 0.5f);
        var tone = new byte[48000 * 4];
        var mono = Tone16(48000, 1, 440, 20000);
        for (int i = 0; i < 48000; i++) { tone[i * 4] = tone[i * 4 + 2] = mono[i * 2]; tone[i * 4 + 1] = tone[i * 4 + 3] = mono[i * 2 + 1]; }
        input.Push(tone, tone.Length);

        var buffer = new byte[24000 * AudioMixer.BytesPerFrame];
        mixer.Read(buffer, 24000);
        var (peak, _) = Stats(buffer, 24000);
        Assert.InRange(peak, 9000, 11000);
        Assert.InRange(mixer.Peak, 0.25f, 0.35f);
    }

    [Fact]
    public void Duas_fontes_sao_somadas_sem_estourar()
    {
        var mixer = new AudioMixer();
        var format = new WaveFormat(48000, 16, 1);
        var tone = Tone16(48000, 1, 440, 30000);
        mixer.AddInput("a", format).Push(tone, tone.Length);
        mixer.AddInput("b", format).Push(tone, tone.Length);
        var buffer = new byte[12000 * AudioMixer.BytesPerFrame];
        mixer.Read(buffer, 12000);
        var (peak, _) = Stats(buffer, 12000);
        Assert.InRange(peak, 30000, 32767);
    }

    [Fact]
    public async Task Pump_escreve_no_ritmo_do_relogio_ate_cancelar()
    {
        var mixer = new AudioMixer();
        using var output = new MemoryStream();
        using var cts = new CancellationTokenSource(500);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await mixer.PumpAsync(output, cts.Token);
        double seconds = clock.Elapsed.TotalSeconds;
        long frames = output.Length / AudioMixer.BytesPerFrame;
        Assert.InRange(frames, 48000 * (seconds - 0.1) * 0.9, 48000 * (seconds + 0.1) * 1.1);
    }
}
