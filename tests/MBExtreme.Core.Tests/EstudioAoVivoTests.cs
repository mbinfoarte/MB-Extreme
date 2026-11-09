using MBExtreme.Core.Recording;
using Xunit;

namespace MBExtreme.Core.Tests;

public class EstudioAoVivoTests
{
    private static readonly EncoderOption Nvenc = new(VideoCodec.H264, EncoderKind.Nvenc, "h264_nvenc");

    private static StudioRequest Request(Func<StudioRequest, StudioRequest>? change = null)
    {
        var r = new StudioRequest
        {
            Source = new CaptureSource(SourceKind.Monitor, 0, new PixelRect(0, 0, 1920, 1080), "Tela 1"),
            OutputWidth = 1920, OutputHeight = 1080, Fps = 60, Encoder = Nvenc, BitrateMbps = 16, WorkingDirectory = "/tmp/x", RecordFile = "grav.mkv",
        };
        return change is null ? r : change(r);
    }

    private static string Joined(StudioRequest r) => string.Join(" ", StudioCommand.Build(r));

    // ---- várias câmeras

    [Fact]
    public void Tres_cameras_viram_tres_entradas_e_camadas_encadeadas()
    {
        var cams = new[]
        {
            new CameraOverlay("Cam A", CameraCorner.TopLeft, 20, true),
            new CameraOverlay("Cam B", CameraCorner.TopRight, 20, false),
            new CameraOverlay("Cam C", CameraCorner.BottomRight, 15, false),
        };
        string cmd = Joined(Request(r => r with { Cameras = cams, Audio = new AudioSetup("p") }));
        Assert.Equal(3, cmd.Split("-f dshow").Length - 1);
        Assert.Contains("video=Cam A", cmd);
        Assert.Contains("video=Cam C", cmd);
        Assert.Contains("[2:v]scale=", cmd);   // primeira câmera: depois do vídeo (0) e do áudio (1)
        Assert.Contains("[4:v]scale=", cmd);
        Assert.Contains("[b0][c0]overlay=", cmd);
        Assert.Contains("[b1][c1]overlay=", cmd);
        Assert.Contains("[b2][c2]overlay=", cmd);
        Assert.Contains("[b3]format=nv12[v]", cmd);
    }

    [Fact]
    public void Mais_de_tres_cameras_ou_camera_repetida_sao_recusadas()
    {
        var four = Enumerable.Range(0, 4).Select(i => new CameraOverlay("Cam" + i, CameraCorner.TopLeft, 15, false)).ToArray();
        Assert.Contains("no máximo", StudioCommand.Validate(Request(r => r with { Cameras = four })));
        var twice = new[] { new CameraOverlay("Cam", CameraCorner.TopLeft, 15, false), new CameraOverlay("cam", CameraCorner.TopRight, 15, false) };
        Assert.Contains("duas vezes", StudioCommand.Validate(Request(r => r with { Cameras = twice })));
    }

    [Fact]
    public void Propriedade_Camera_continua_funcionando_como_a_primeira_da_lista()
    {
        var one = new CameraOverlay("Cam", CameraCorner.TopLeft, 20, false);
        var r = Request(x => x with { Camera = one });
        Assert.Single(r.Cameras);
        Assert.Equal(one, r.Camera);
        Assert.Null(Request().Camera);
    }

    // ---- Pix na tela

    [Fact]
    public void Qr_do_pix_entra_como_imagem_e_pode_aparecer_so_de_vez_em_quando()
    {
        var pix = new PixOverlay("C:/Users/x/pix.png", CameraCorner.BottomLeft, 12, EveryMinutes: 5, ShowSeconds: 15);
        string cmd = Joined(Request(r => r with { Pix = pix }));
        Assert.Contains("-loop 1 -framerate 5 -i C:/Users/x/pix.png", cmd);
        Assert.Contains("[1:v]scale=", cmd);
        Assert.Contains("enable='lt(mod(t,300),15)'", cmd);
        Assert.Contains("[b1]format=nv12[v]", cmd);

        string always = Joined(Request(r => r with { Pix = pix with { EveryMinutes = 0 } }));
        Assert.DoesNotContain("enable=", always);
    }

    [Fact]
    public void Camera_e_pix_juntos_usam_as_entradas_na_ordem_certa()
    {
        var cam = new CameraOverlay("Cam", CameraCorner.TopLeft, 20, false);
        var pix = new PixOverlay("p.png", CameraCorner.BottomRight, 10, 0, 10);
        string cmd = Joined(Request(r => r with { Cameras = new[] { cam }, Pix = pix, Audio = new AudioSetup("p") }));
        Assert.Contains("[2:v]scale=", cmd);   // câmera
        Assert.Contains("[3:v]scale=", cmd);   // QR
        Assert.Contains("[b1][pix]overlay=", cmd);
    }

    [Theory]
    [InlineData("C:/x/pix.jpg")]
    [InlineData("C:/x/pix.png\" -y")]
    [InlineData("a|b.png")]
    [InlineData("")]
    public void Caminho_estranho_da_imagem_do_pix_e_recusado(string path) =>
        Assert.NotNull(StudioCommand.Validate(Request(r => r with { Pix = new PixOverlay(path, CameraCorner.TopLeft, 10, 0, 10) })));

    // ---- Pix: BR Code

    [Fact]
    public void Crc16_confere_com_o_valor_de_referencia()
    {
        Assert.Equal((ushort)0x29B1, PixPayload.Crc16("123456789"));
    }

    [Fact]
    public void Payload_tem_campos_certos_e_o_crc_bate()
    {
        string code = PixPayload.Build("123e4567-e12b-12d1-a456-426655440000", "Fulano de Tal", "Brasília", 12.5m, "Apoio a live");
        Assert.StartsWith("000201", code);
        Assert.Contains("0014br.gov.bcb.pix", code);
        Assert.Contains("0136123e4567-e12b-12d1-a456-426655440000", code);
        Assert.Contains("0212APOIO A LIVE", code);
        Assert.Contains("540512.50", code);
        Assert.Contains("5303986", code);
        Assert.Contains("5802BR", code);
        Assert.Contains("5913FULANO DE TAL", code);
        Assert.Contains("6008BRASILIA", code);
        Assert.Contains("62070503***", code);
        string body = code[..^4];
        Assert.EndsWith("6304", body);
        Assert.Equal(code[^4..], PixPayload.Crc16(body).ToString("X4"));
    }

    [Fact]
    public void Sem_valor_o_campo_54_nao_existe_e_texto_e_limpo()
    {
        string code = PixPayload.Build("fulano@exemplo.com", "José da Conceição & Cia Ltda Enorme Nome", "São Paulo do Interior Longe");
        Assert.DoesNotContain("5405", code);
        Assert.DoesNotContain("5404", code);
        Assert.Equal("JOSE DA CONCEICAO CIA LTD", PixPayload.Clean("José da Conceição & Cia Ltda Enorme Nome", 25));
        Assert.Equal("SAO PAULO DO IN", PixPayload.Clean("São Paulo do Interior Longe", 15));
        Assert.Contains("5925JOSE DA CONCEICAO CIA LTD", code);
        Assert.Contains("6015SAO PAULO DO IN", code);
    }

    [Theory]
    [InlineData("529.982.247-25", "52998224725")]                 // CPF válido
    [InlineData("11.222.333/0001-81", "11222333000181")]          // CNPJ válido
    [InlineData("+55 (11) 91234-5678", "+5511912345678")]
    [InlineData("Fulano@Exemplo.COM", "fulano@exemplo.com")]
    [InlineData("123E4567-E12B-12D1-A456-426655440000", "123e4567-e12b-12d1-a456-426655440000")]
    public void Chaves_validas_sao_normalizadas(string input, string expected) => Assert.Equal(expected, PixPayload.NormalizeKey(input));

    [Theory]
    [InlineData("")]
    [InlineData("123.456.789-00")]       // dígitos verificadores errados
    [InlineData("11111111111")]
    [InlineData("abc")]
    [InlineData("+5511")]
    [InlineData("a@b")]
    public void Chaves_invalidas_sao_recusadas(string input)
    {
        Assert.Null(PixPayload.NormalizeKey(input));
        Assert.Throws<ArgumentException>(() => PixPayload.Build(input, "Fulano", "Cidade"));
    }

    [Fact]
    public void Nome_e_cidade_sao_obrigatorios()
    {
        Assert.Throws<ArgumentException>(() => PixPayload.Build("fulano@exemplo.com", "!!!", "Cidade"));
        Assert.Throws<ArgumentException>(() => PixPayload.Build("fulano@exemplo.com", "Fulano", ""));
        Assert.Throws<ArgumentException>(() => PixPayload.Build("fulano@exemplo.com", "Fulano", "Cidade", -1m));
    }

    // ---- lances marcantes pelo som

    private static (bool fired, double at) Run(HighlightDetector d, Func<double, double> level, double seconds)
    {
        for (double t = 0; t < seconds; t += 0.1)
            if (d.Feed(level(t), t)) return (true, t);
        return (false, 0);
    }

    [Fact]
    public void Som_estavel_nao_dispara()
    {
        var (fired, _) = Run(new HighlightDetector(), t => 0.3, 120);
        Assert.False(fired);
    }

    [Fact]
    public void Salto_de_volume_depois_do_aquecimento_dispara_uma_vez_e_respeita_a_pausa()
    {
        var d = new HighlightDetector();
        var hits = new List<double>();
        for (double t = 0; t < 120; t += 0.1)
        {
            double level = t is >= 60 and < 66 ? 0.85 : 0.2;
            if (d.Feed(level, t)) hits.Add(t);
        }

        Assert.Single(hits);
        Assert.InRange(hits[0], 60, 64);
    }

    [Fact]
    public void Barulho_nos_primeiros_segundos_e_silencio_nao_disparam()
    {
        var d = new HighlightDetector();
        Assert.False(Run(d, t => t is >= 5 and < 10 ? 0.9 : 0.2, 15).fired);          // aquecimento
        Assert.False(Run(new HighlightDetector(), t => t > 40 ? 0.05 : 0.01, 100).fired); // abaixo do mínimo
    }

    [Fact]
    public void Sensibilidade_alta_pega_saltos_menores_que_a_baixa()
    {
        Func<double, double> level = t => t is >= 60 and < 66 ? 0.34 : 0.2;   // 1,7 vez o normal
        Assert.True(Run(new HighlightDetector(HighlightDetector.Sensitivity.High), level, 90).fired);
        Assert.False(Run(new HighlightDetector(HighlightDetector.Sensitivity.Low), level, 90).fired);
    }

    [Fact]
    public void Dois_lances_longe_um_do_outro_disparam_duas_vezes()
    {
        var d = new HighlightDetector();
        int hits = 0;
        for (double t = 0; t < 240; t += 0.1)
        {
            double level = (t is >= 60 and < 65) || (t is >= 150 and < 155) ? 0.9 : 0.2;
            if (d.Feed(level, t)) hits++;
        }
        Assert.Equal(2, hits);
    }
}

public class PixQrTests
{
    [Fact]
    public void Qr_do_pix_e_quadrado_com_marcadores_nos_cantos()
    {
        string code = PixPayload.Build("fulano@exemplo.com", "Fulano", "Cidade", 10m, "Apoie a live");
        var m = PixQr.Modules(code);
        int n = m.GetLength(0);
        Assert.Equal(n, m.GetLength(1));
        Assert.InRange(n, 29, 77);                     // versões 3 a 12
        Assert.True(m[0, 0] && m[0, 6] && m[6, 0] && m[6, 6]);          // canto do marcador superior esquerdo
        Assert.True(m[0, n - 1] && m[n - 1, 0]);                         // outros dois marcadores
    }

    [Fact]
    public void Texto_vazio_ou_enorme_e_recusado()
    {
        Assert.Throws<ArgumentException>(() => PixQr.Modules(""));
        Assert.Throws<ArgumentException>(() => PixQr.Modules(new string('a', 700)));
    }
}

public class AudioPorFonteTests
{
    [Fact]
    public void Volume_de_cada_fonte_e_medido_separado_e_zera_depois_de_lido()
    {
        var mixer = new AudioMixer();
        var format = new NAudio.Wave.WaveFormat(AudioMixer.SampleRate, 16, 2);
        var system = mixer.AddInput("Som do PC", format);
        var mic = mixer.AddInput("Microfone", format);

        // 100 ms de som alto só no microfone e silêncio no som do PC
        var loud = new byte[AudioMixer.SampleRate / 10 * AudioMixer.BytesPerFrame];
        for (int i = 0; i < loud.Length; i += 2) { loud[i] = 0x00; loud[i + 1] = 0x60; }   // ~0,75
        mic.Push(loud, loud.Length);
        system.Push(new byte[loud.Length], loud.Length);

        var buffer = new byte[loud.Length];
        mixer.Read(buffer, AudioMixer.SampleRate / 10);

        Assert.True(mic.TakePeak() > 0.5f);
        Assert.Equal(0f, system.TakePeak());
        Assert.Equal(0f, mic.TakePeak());           // zera ao ler
    }

    [Fact]
    public void Mudo_ao_vivo_zera_o_volume_da_fonte_sem_reiniciar()
    {
        var mixer = new AudioMixer();
        var input = mixer.AddInput("Som do PC", new NAudio.Wave.WaveFormat(AudioMixer.SampleRate, 16, 2), 1f);
        input.Volume = 0f;
        var data = new byte[AudioMixer.SampleRate / 10 * AudioMixer.BytesPerFrame];
        for (int i = 0; i < data.Length; i += 2) data[i + 1] = 0x60;
        input.Push(data, data.Length);
        mixer.Read(new byte[data.Length], AudioMixer.SampleRate / 10);
        Assert.Equal(0f, input.TakePeak());
        input.Volume = 1f;
        Assert.Equal(1f, input.Volume);
    }
}
