using MBExtreme.Core.Recording;
using Xunit;

namespace MBExtreme.Core.Tests;

public class EstudioVerticalTests
{
    private static readonly EncoderOption Nvenc = new(VideoCodec.H264, EncoderKind.Nvenc, "h264_nvenc");

    private static VerticalLayout Layout(bool split = true, VerticalPane? top = null, VerticalPane? bottom = null, bool divider = false, int percent = 55) =>
        new(1080, 1920, split, percent, top ?? new VerticalPane(PaneKind.Screen, PaneFit.Fit), bottom ?? new VerticalPane(PaneKind.Camera, PaneFit.Fill, Source: "Logitech C920"), divider);

    private static StudioRequest Request(VerticalLayout? v = null, CaptionOverlay? c = null, Func<StudioRequest, StudioRequest>? change = null)
    {
        var r = new StudioRequest
        {
            Source = new CaptureSource(SourceKind.Monitor, 0, new PixelRect(0, 0, 1920, 1080), "Tela 1"),
            OutputWidth = v?.Width ?? 1920, OutputHeight = v?.Height ?? 1080, Fps = 60, Encoder = Nvenc, BitrateMbps = 16, WorkingDirectory = "/tmp/x",
            RecordFile = "grav.mkv", Vertical = v, Captions = c,
        };
        return change is null ? r : change(r);
    }

    private static CaptionOverlay Caption(CaptionPosition pos = CaptionPosition.Bottom) =>
        new(@"C:\Users\Ana\AppData\Roaming\MBExtreme\legenda1.txt", @"C:\Users\Ana\AppData\Roaming\MBExtreme\legenda2.txt", @"C:\Windows\Fonts\arialbd.ttf", 6, pos, CaptionColor.White, true);

    private static string Joined(StudioRequest r) => string.Join(" ", StudioCommand.Build(r));

    private static string Graph(StudioRequest r)
    {
        var a = StudioCommand.Build(r).ToList();
        return a[a.IndexOf("-filter_complex") + 1];
    }

    // ---- formatos

    [Fact]
    public void Formatos_verticais_tem_medidas_pares_e_altas()
    {
        Assert.All(VerticalFormat.All, f => { Assert.Equal(0, f.Width % 2); Assert.Equal(0, f.Height % 2); });
        Assert.Contains(VerticalFormat.All, f => f is { Width: 1080, Height: 1920 });
        Assert.Contains(VerticalFormat.All, f => f is { Width: 1440, Height: 2560 });
        Assert.Contains(VerticalFormat.All, f => f is { Width: 2160, Height: 3840 });
        Assert.Equal(1080, VerticalFormat.ByIndex(-3).Width);
        Assert.Equal(1080, VerticalFormat.ByIndex(99).Width);
    }

    [Fact]
    public void Reduzir_para_a_plataforma_mantem_a_proporcao()
    {
        var (w, h) = VerticalFormat.ByIndex(2).ScaledToWidth(1080);
        Assert.Equal((1080, 1920), (w, h));
        var (w2, h2) = VerticalFormat.ByIndex(0).ScaledToWidth(5000);
        Assert.Equal((1080, 1920), (w2, h2));
    }

    [Fact]
    public void Alturas_das_partes_somam_o_quadro_e_sao_pares()
    {
        foreach (int p in new[] { 10, 25, 33, 55, 61, 75, 90 })
        {
            var l = Layout(percent: p);
            Assert.Equal(l.Height, l.TopHeight + l.BottomHeight);
            Assert.Equal(0, l.TopHeight % 2);
            Assert.InRange(l.TopHeight, 1920 * 25 / 100 - 2, 1920 * 75 / 100 + 2);
        }
        Assert.Equal(1920, Layout(split: false).TopHeight);
        Assert.Equal(0, Layout(split: false).BottomHeight);
    }

    // ---- validação

    [Fact]
    public void Pelo_menos_uma_parte_precisa_ser_a_tela()
    {
        var l = Layout(top: new VerticalPane(PaneKind.Camera, Source: "A"), bottom: new VerticalPane(PaneKind.Camera, Source: "B"));
        Assert.NotNull(StudioCommand.Validate(Request(l)));
        Assert.NotNull(Layout(split: false, top: new VerticalPane(PaneKind.File, Source: "a.png")).Validate());
    }

    [Theory]
    [InlineData("fundo.mp4", true)]
    [InlineData("fundo.PNG", true)]
    [InlineData("fundo.gif", true)]
    [InlineData("fundo.exe", false)]
    [InlineData("a'b.mp4", false)]
    [InlineData("a;b.mp4", false)]
    [InlineData("", false)]
    public void Arquivos_do_fundo_sao_validados(string path, bool ok) => Assert.Equal(ok, VerticalPane.IsValidFile(path));

    [Fact]
    public void Camera_do_vertical_nao_pode_repetir_a_do_canto()
    {
        var cam = new CameraOverlay("Logitech C920", CameraCorner.BottomRight, 20, false);
        var r = Request(Layout(), change: x => x with { Cameras = new[] { cam } });
        Assert.NotNull(StudioCommand.Validate(r));
    }

    [Fact]
    public void Tamanho_de_saida_precisa_bater_com_o_quadro_vertical()
    {
        var r = Request(Layout(), change: x => x with { OutputWidth = 720, OutputHeight = 1280 });
        Assert.NotNull(StudioCommand.Validate(r));
        Assert.Null(StudioCommand.Validate(Request(Layout())));
    }

    // ---- filtro

    [Fact]
    public void Tela_em_cima_e_camera_embaixo_monta_o_quadro()
    {
        var r = Request(Layout());
        string g = Graph(r);
        Assert.Contains("[0:v]hwdownload,format=bgra[scr]", g);
        Assert.Contains("[scr]split=2[p0a][p0b]", g);                       // tela inteira com fundo desfocado
        Assert.Contains("boxblur", g);
        Assert.Contains("[1:v]format=bgra,scale=1080:", g);                  // sem áudio: câmera é a entrada 1
        Assert.Contains($"pad=1080:1920:0:0:black[bp]", g);
        Assert.Contains($"overlay=0:{Layout().TopHeight}:shortest=0[b0]", g);
        Assert.EndsWith("format=nv12[v]", g);
        string all = Joined(r);
        Assert.Contains("-f dshow", all);
        Assert.Contains("video=Logitech C920", all);
        Assert.Contains("-map [v]", all);
    }

    [Fact]
    public void Com_audio_as_entradas_extras_comecam_na_2()
    {
        var r = Request(Layout(), change: x => x with { Audio = new AudioSetup("pipe") });
        Assert.Contains("[2:v]format=bgra,scale=1080:", Graph(r));
    }

    [Fact]
    public void Camera_em_cima_faz_a_tela_virar_o_fundo_de_baixo()
    {
        var l = Layout(top: new VerticalPane(PaneKind.Camera, Source: "Cam"), bottom: new VerticalPane(PaneKind.Screen, PaneFit.Fill, 30, 70, 150));
        string g = Graph(Request(l));
        Assert.Contains($"[pb]pad=1080:1920:0:{l.TopHeight}:black[bp]", g);
        Assert.Contains("[bp][pt]overlay=0:0:shortest=0[b0]", g);
        Assert.Contains("crop=1080:" + l.BottomHeight + ":(iw-ow)*30/100:(ih-oh)*70/100", g);
        Assert.Contains("scale=1620:", g);                                    // zoom 150%
    }

    [Fact]
    public void Duas_partes_da_tela_dividem_a_captura()
    {
        var l = Layout(top: new VerticalPane(PaneKind.Screen, PaneFit.Fit), bottom: new VerticalPane(PaneKind.Screen, PaneFit.Fill, 0, 100, 250));
        string g = Graph(Request(l));
        Assert.Contains("[scr]split=2[sa][sb]", g);
        Assert.Contains("[sa]split=2[p0a][p0b]", g);
        Assert.Contains("[sb]scale=", g);
        Assert.DoesNotContain("-f dshow", Joined(Request(l)));
    }

    [Fact]
    public void Sem_dividir_a_tela_ocupa_o_quadro_todo()
    {
        var l = Layout(split: false, top: new VerticalPane(PaneKind.Screen, PaneFit.Fill, 40));
        string g = Graph(Request(l));
        Assert.Contains($"crop=1080:1920:(iw-ow)*40/100", g);
        Assert.Contains("[pt]null[b0]", g);
        Assert.DoesNotContain("overlay=0:", g);
    }

    [Fact]
    public void Divisoria_amarela_fica_entre_as_partes()
    {
        var l = Layout(divider: true);
        string g = Graph(Request(l));
        Assert.Contains("[bv]drawbox=", g);
        Assert.Contains("0xFFC400", g);
        Assert.Contains("[b0]", g);
    }

    [Fact]
    public void Arquivo_de_video_repete_em_tempo_real_e_imagem_fica_parada()
    {
        var video = Layout(bottom: new VerticalPane(PaneKind.File, Source: @"C:\fundos\corrida.mp4"));
        string a = Joined(Request(video));
        Assert.Contains("-stream_loop -1 -re -i C:\\fundos\\corrida.mp4", a);
        var image = Layout(bottom: new VerticalPane(PaneKind.File, Source: @"C:\fundos\fundo.png"));
        Assert.Contains("-loop 1 -framerate 30 -i C:\\fundos\\fundo.png", Joined(Request(image)));
    }

    [Fact]
    public void Filtro_de_imagem_vale_so_para_a_tela_e_camera_do_canto_continua_funcionando()
    {
        var cam = new CameraOverlay("Outra", CameraCorner.TopRight, 20, true);
        var r = Request(Layout(), change: x => x with { Filter = new ImageFilter(0, 1.1, 1.2), Cameras = new[] { cam } });
        string g = Graph(r);
        Assert.Contains("[0:v]hwdownload,format=bgra,format=yuv420p,eq=", g);
        Assert.Contains("[2:v]scale=", g);                                    // câmera do canto vem depois da câmera da parte
        Assert.Contains("overlay=x=main_w-overlay_w-13:y=13", g);             // margem de 1080/80
    }

    [Fact]
    public void Transmissao_vertical_usa_o_tamanho_reduzido()
    {
        var (w, h) = VerticalFormat.ByIndex(1).ScaledToWidth(1080);
        var l = Layout() with { Width = w, Height = h };
        var r = Request(l, change: x => x with { Stream = new StreamTarget(StreamService.YouTube, "", "abcd-1234") });
        Assert.Null(StudioCommand.Validate(r));
        Assert.Contains($"pad=1080:{h}:0:0:black", Graph(r));
    }

    // ---- legendas

    [Fact]
    public void Caminho_no_filtro_usa_barras_normais_e_escapa_dois_pontos()
        => Assert.Equal(@"'C\:/Windows/Fonts/arialbd.ttf'", CaptionFilter.EscapePath(@"C:\Windows\Fonts\arialbd.ttf"));

    [Fact]
    public void Legenda_vira_dois_drawtext_que_releem_o_arquivo()
    {
        string g = Graph(Request(Layout(), Caption()));
        Assert.Contains(@"drawtext=textfile='C\:/Users/Ana/AppData/Roaming/MBExtreme/legenda1.txt'", g);
        Assert.Contains(@"drawtext=textfile='C\:/Users/Ana/AppData/Roaming/MBExtreme/legenda2.txt'", g);
        Assert.Equal(2, g.Split("reload=1").Length - 1);
        Assert.Contains("expansion=none", g);
        Assert.Contains("fontsize=64", g);                                    // 6% de 1080, par
        Assert.Contains("x=(w-text_w)/2", g);
        Assert.Contains("fontcolor=0xFFFFFF", g);
        Assert.EndsWith("format=nv12[v]", g);
    }

    [Fact]
    public void Posicao_da_legenda_segue_a_escolha()
    {
        string bottom = Graph(Request(Layout(), Caption(CaptionPosition.Bottom)));
        string center = Graph(Request(Layout(), Caption(CaptionPosition.Center)));
        string top = Graph(Request(Layout(), Caption(CaptionPosition.Top)));
        string divider = Graph(Request(Layout(), Caption(CaptionPosition.Divider)));
        Assert.Contains("y=1429", bottom);                                    // 74% de 1920 = 1421 + folga de 8
        Assert.Contains("y=968", center);
        Assert.Contains("y=277", top);
        int topHeight = Layout().TopHeight;
        Assert.Contains($"y={topHeight + 8}", divider);
    }

    [Fact]
    public void Legenda_funciona_tambem_no_video_normal_sem_vertical()
    {
        var r = Request(null, Caption());
        string g = Graph(r);
        Assert.Contains("drawtext=textfile=", g);
        Assert.Contains("fontsize=114", g);
    }

    [Fact]
    public void Caminho_de_legenda_perigoso_e_recusado()
    {
        var bad = Caption() with { FontFile = "C:/a'b.ttf" };
        Assert.NotNull(StudioCommand.Validate(Request(Layout(), bad)));
    }

    // ---- composição do texto

    [Fact]
    public void Composer_mostra_o_parcial_e_mantem_as_duas_ultimas_linhas()
    {
        var c = new CaptionComposer(12, false);
        c.Partial("olá pessoal", 1);
        Assert.Equal(("", "olá pessoal"), c.Current(1.1));
        c.Final("olá pessoal tudo bem", 2);
        Assert.Equal(("olá pessoal", "tudo bem"), c.Current(2.1));
        c.Partial("com vocês hoje", 3);
        var (l1, l2) = c.Current(3.1);
        Assert.True(l1.Length <= 12 && l2.Length <= 12);
        Assert.Contains("hoje", l2);
    }

    [Fact]
    public void Composer_apaga_depois_do_silencio_e_aplica_maiusculas()
    {
        var c = new CaptionComposer(20, true, 3);
        c.Final("ação rápida", 1);
        Assert.Equal(("", "AÇÃO RÁPIDA"), c.Current(2));
        Assert.Equal(("", ""), c.Current(5));
        Assert.Equal(("", ""), c.Current(5.1));
    }

    [Fact]
    public void Composer_quebra_palavra_gigante_e_ignora_porcentagem()
    {
        var lines = CaptionComposer.Wrap(new[] { "anticonstitucionalissimamente", "ok" }, 10);
        Assert.Equal(new[] { "anticonsti", "ok" }, lines);
        var c = new CaptionComposer(30, false);
        c.Final("100% certo", 1);
        Assert.DoesNotContain('%', c.Current(1).Line2);
    }

    [Fact]
    public void Largura_da_linha_depende_do_tamanho_da_letra()
    {
        var small = Caption() with { SizePercent = 4 };
        var big = Caption() with { SizePercent = 9 };
        Assert.True(small.MaxChars(1080) > big.MaxChars(1080));
        Assert.InRange(Caption().MaxChars(1080), 8, 60);
    }

    // ---- arquivos de legenda

    [Fact]
    public void Arquivos_de_legenda_nunca_ficam_vazios_e_so_gravam_o_que_mudou()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mbx-leg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string a = Path.Combine(dir, "l1.txt"), b = Path.Combine(dir, "l2.txt");
            var files = new CaptionFiles(a, b);
            Assert.True(files.Write("", ""));
            Assert.Equal(" ", File.ReadAllText(a));
            Assert.True(files.Write("OLÁ", "mundo ação"));
            Assert.Equal("OLÁ", File.ReadAllText(a));
            Assert.Equal("mundo ação", File.ReadAllText(b));
            Assert.False(File.Exists(a + ".tmp"));
            Assert.Equal(new byte[] { 0x4F, 0x4C }, File.ReadAllBytes(a).Take(2).ToArray());   // sem marca BOM
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---- áudio para o reconhecedor

    [Fact]
    public void Converte_estereo_48k_float_para_16k_mono()
    {
        var pcm = new SpeechPcm(new WaveFormatInfo(48000, 2, 32, true));
        var data = new byte[48000 * 2 * 4 / 10];             // 100 ms
        for (int i = 0; i < data.Length / 4; i++) BitConverter.GetBytes(0.5f).CopyTo(data, i * 4);
        var output = pcm.Convert(data, data.Length);
        Assert.InRange(output.Length / 2, 1590, 1610);        // ~1600 amostras
        short mid = BitConverter.ToInt16(output, 100);
        Assert.InRange(mid, 16000, 16500);                    // 0,5 de escala
    }

    [Fact]
    public void Conversao_em_pedacos_mantem_a_quantidade_total()
    {
        var pcm = new SpeechPcm(new WaveFormatInfo(44100, 1, 16, false));
        int total = 0;
        for (int i = 0; i < 100; i++) total += pcm.Convert(new byte[441 * 2], 441 * 2).Length / 2;   // 100 x 10 ms
        Assert.InRange(total, 15990, 16010);
    }

    [Fact]
    public void Formato_invalido_ou_pedaco_vazio_nao_quebra()
    {
        Assert.Empty(new SpeechPcm(new WaveFormatInfo(0, 2, 16, false)).Convert(new byte[8], 8));
        Assert.Empty(new SpeechPcm(new WaveFormatInfo(48000, 2, 16, false)).Convert(new byte[2], 2));
    }

    // ---- serviço de legendas

    private sealed class FakeEngine : ISpeechEngine
    {
        public event Action<string>? Partial;
        public event Action<string>? Final;
        public int Bytes;
        public void Accept(byte[] pcm16kMono, int count) => Bytes += count;
        public void Say(string partial, string? final = null) { Partial?.Invoke(partial); if (final is not null) Final?.Invoke(final); }
        public void Dispose() { }
    }

    [Fact]
    public async Task Servico_escreve_o_que_o_reconhecedor_ouve()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mbx-leg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var overlay = Caption() with { Line1File = Path.Combine(dir, "a.txt"), Line2File = Path.Combine(dir, "b.txt"), Uppercase = false };
            using var service = new CaptionService(overlay, 1080);
            var engine = new FakeEngine();
            service.Attach(engine, new WaveFormatInfo(48000, 2, 16, false));
            service.Feed(new byte[4800 * 4], 4800 * 4);
            Assert.True(engine.Bytes > 0);

            engine.Say("boa noite", "boa noite");
            for (int i = 0; i < 30 && service.Shown.Line2 != "boa noite"; i++) await Task.Delay(50);
            Assert.Equal("boa noite", service.Shown.Line2);
            Assert.Equal("boa noite", File.ReadAllText(overlay.Line2File));
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---- modelo de voz

    [Fact]
    public void Modelo_so_conta_como_instalado_com_os_arquivos_principais()
    {
        string root = Path.Combine(Path.GetTempPath(), "mbx-vosk-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.False(SpeechModels.IsInstalled(root));
            Directory.CreateDirectory(Path.Combine(root, SpeechModels.FolderName, "am"));
            Assert.False(SpeechModels.IsInstalled(root));
            File.WriteAllText(Path.Combine(root, SpeechModels.FolderName, "am", "final.mdl"), "x");
            Assert.True(SpeechModels.IsInstalled(root));
            Assert.StartsWith("https://", SpeechModels.DownloadUrl);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
