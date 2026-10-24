using MBExtreme.Core.Recording;
using Xunit;

namespace MBExtreme.Core.Tests;

public class EstudioProfissionalTests
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

    // ---- mixer por programa

    [Theory]
    [InlineData("Discord", "Discord", AppAudioKind.Voice)]
    [InlineData("discord.exe", "Discord", AppAudioKind.Voice)]
    [InlineData("Spotify", "Spotify", AppAudioKind.Music)]
    [InlineData("chrome", "Google Chrome", AppAudioKind.Browser)]
    [InlineData("msedge", "Microsoft Edge", AppAudioKind.Browser)]
    [InlineData("algumprograma", "algumprograma", AppAudioKind.Other)]
    public void Programas_comuns_sao_reconhecidos(string process, string name, AppAudioKind kind)
    {
        var (n, k) = AppAudioRules.Describe(process);
        Assert.Equal(name, n);
        Assert.Equal(kind, k);
    }

    [Fact]
    public void Jogos_da_biblioteca_aparecem_como_jogo_antes_de_tudo()
    {
        var (name, kind) = AppAudioRules.Describe("FC27", new[] { "FC27.exe", "Valorant" });
        Assert.Equal(AppAudioKind.Game, kind);
        Assert.Equal("FC27", name);

        var ordered = AppAudioRules.Order(new[]
        {
            new AppAudioSession("spotify", "Spotify", AppAudioKind.Music, 1, false, 0.9f),
            new AppAudioSession("discord", "Discord", AppAudioKind.Voice, 1, false, 0.1f),
            new AppAudioSession("FC27", "FC27", AppAudioKind.Game, 1, false, 0.2f),
            new AppAudioSession("zzz", "Zzz", AppAudioKind.Other, 1, false, 0.95f),
        });
        Assert.Equal(new[] { "FC27", "Discord", "Spotify", "Zzz" }, ordered.Select(s => s.Display));
    }

    [Fact]
    public void Varias_janelas_de_som_do_mesmo_programa_viram_uma_linha()
    {
        var merged = AppAudioRules.Merge("chrome", "Google Chrome", AppAudioKind.Browser, new (float, bool, float)[] { (0.5f, true, 0.1f), (0.8f, true, 0.4f) });
        Assert.Equal(0.8f, merged.Volume);
        Assert.True(merged.Muted);
        Assert.Equal(0.4f, merged.Peak);
        Assert.False(AppAudioRules.Merge("x", "x", AppAudioKind.Other, new (float, bool, float)[] { (1, false, 0), (1, true, 0) }).Muted);   // só é "mudo" se todas estiverem
    }

    // ---- filtros de imagem

    [Fact]
    public void Imagem_sem_ajuste_nao_gera_filtro()
    {
        Assert.True(ImageFilter.None.IsNeutral);
        Assert.Null(ImageFilter.None.ToChain());
        Assert.DoesNotContain("filter_complex", Joined(Request(r => r with { Filter = ImageFilter.None })));
    }

    [Fact]
    public void Filtro_de_cor_vira_eq_unsharp_e_denoise_com_numeros_em_formato_universal()
    {
        string chain = new ImageFilter(0.02, 1.12, 1.3, 1, 0.6, 2).ToChain()!;
        Assert.Contains("format=yuv420p", chain);
        Assert.Contains("hqdn3d=4:3:6:4.5", chain);
        Assert.Contains("eq=brightness=0.02:contrast=1.12:saturation=1.3:gamma=1", chain);
        Assert.Contains("unsharp=luma_msize_x=5:luma_msize_y=5:luma_amount=0.6", chain);
        Assert.EndsWith("format=bgra", chain);
        Assert.DoesNotContain(",5", chain.Replace("0.5", ""));       // nada de vírgula decimal
    }

    [Fact]
    public void Valores_absurdos_sao_limitados_e_espelho_funciona_sozinho()
    {
        var wild = new ImageFilter(5, 9, 9, 9, 9, 9).Clamped();
        Assert.Equal(0.3, wild.Brightness);
        Assert.Equal(1.8, wild.Contrast);
        Assert.Equal(2.0, wild.Saturation);
        Assert.Equal(2, wild.Sharpness);
        Assert.Equal(3, wild.Denoise);
        Assert.Equal("hflip,format=bgra", new ImageFilter(Mirror: true).ToChain());
    }

    [Fact]
    public void Filtro_da_imagem_principal_entra_antes_das_camadas()
    {
        string cmd = Joined(Request(r => r with { Filter = ImageFilter.Presets[2].Filter }));
        Assert.Contains("[0:v]hwdownload,format=bgra,format=yuv420p,eq=", cmd);
        Assert.Contains("[b0]format=nv12[v]", cmd);
    }

    [Fact]
    public void Filtro_por_camera_so_vale_para_aquela_camera()
    {
        var a = new CameraOverlay("Cam A", CameraCorner.TopLeft, 20, false, new ImageFilter(Sharpness: 1, Mirror: true));
        var b = new CameraOverlay("Cam B", CameraCorner.TopRight, 20, false);
        string cmd = Joined(Request(r => r with { Cameras = new[] { a, b } }));
        int posA = cmd.IndexOf("[1:v]", StringComparison.Ordinal);
        int posB = cmd.IndexOf("[2:v]", StringComparison.Ordinal);
        string chainA = cmd[posA..posB];
        string chainB = cmd[posB..];
        Assert.Contains("hflip", chainA);
        Assert.Contains("unsharp", chainA);
        Assert.DoesNotContain("hflip", chainB.Split("[b0]")[0]);
    }

    // ---- camadas do canal

    [Fact]
    public void Entrada_do_canal_esmaece_e_aparece_no_meio_so_no_tempo_certo()
    {
        var intro = new OverlayLayer(OverlayKind.Intro, "C:/x/intro.png", CameraCorner.TopLeft, 46, 2, 0, 8);
        string cmd = Joined(Request(r => r with { Layers = new[] { intro } }));
        Assert.Contains("-loop 1 -framerate 25 -i C:/x/intro.png", cmd);
        Assert.Contains("fade=t=in:st=2:d=0.6:alpha=1", cmd);
        Assert.Contains("fade=t=out:st=9.4:d=0.6:alpha=1", cmd);
        Assert.Contains("x=(main_w-overlay_w)/2:y=(main_h-overlay_h)/2:enable='between(t,2,10)'", cmd);
    }

    [Fact]
    public void Faixa_com_o_nome_desliza_e_volta_de_tempos_em_tempos()
    {
        var lower = new OverlayLayer(OverlayKind.LowerThird, "C:/x/faixa.png", CameraCorner.BottomLeft, 38, 10, 5, 8);
        string cmd = Joined(Request(r => r with { Layers = new[] { lower } }));
        Assert.Contains("enable='gte(t,10)*lt(mod(t-10,300),8)'", cmd);
        Assert.Contains("x='if(lt(mod(t-10,300),0.6),-overlay_w+(overlay_w+24)*mod(t-10,300)/0.6,", cmd);
        Assert.Contains("y=main_h-overlay_h-24", cmd);

        var once = lower with { EveryMinutes = 0, Corner = CameraCorner.TopRight };
        string single = Joined(Request(r => r with { Layers = new[] { once } }));
        Assert.Contains("enable='between(t,10,18)'", single);
        Assert.Contains("main_w-(overlay_w+24)*(t-10)/0.6", single);
        Assert.Contains("y=24", single);
    }

    [Fact]
    public void Logo_fica_sempre_no_canto_e_a_ordem_das_entradas_e_camera_pix_camadas()
    {
        var logo = new OverlayLayer(OverlayKind.Logo, "C:/x/logo.png", CameraCorner.TopRight, 8, 0, 0, 0);
        var cam = new CameraOverlay("Cam", CameraCorner.BottomLeft, 20, false);
        var pix = new PixOverlay("C:/x/pix.png", CameraCorner.BottomRight, 10, 0, 10);
        string cmd = Joined(Request(r => r with { Cameras = new[] { cam }, Pix = pix, Layers = new[] { logo }, Audio = new AudioSetup("p") }));
        Assert.Contains("[2:v]scale=", cmd);    // câmera
        Assert.Contains("[3:v]scale=", cmd);    // pix
        Assert.Contains("[4:v]scale=", cmd);    // logo
        Assert.Contains("[b2][l0]overlay=x=main_w-overlay_w-24:y=24:shortest=0[b3]", cmd);
        Assert.Contains("[b3]format=nv12[v]", cmd);
    }

    [Fact]
    public void Camadas_demais_ou_caminho_estranho_sao_recusados()
    {
        var five = Enumerable.Range(0, 5).Select(i => new OverlayLayer(OverlayKind.Logo, $"C:/x/{i}.png", CameraCorner.TopLeft, 8, 0, 0, 0)).ToArray();
        Assert.Contains("no máximo", StudioCommand.Validate(Request(r => r with { Layers = five })));
        Assert.NotNull(StudioCommand.Validate(Request(r => r with { Layers = new[] { new OverlayLayer(OverlayKind.Logo, "C:/x/a|b.png", CameraCorner.TopLeft, 8, 0, 0, 0) } })));
    }

    // ---- chat da Twitch

    [Fact]
    public void Mensagem_comum_da_twitch_e_lida_com_nome_cor_e_texto()
    {
        var m = TwitchIrc.Parse("@badge-info=;badges=moderator/1;color=#FF4500;display-name=Fulano;id=abc-1;tmi-sent-ts=1700000000000 :fulano!fulano@fulano.tmi.twitch.tv PRIVMSG #canal :Oi pessoal, como joga isso?")!;
        Assert.Equal("Fulano", m.Author);
        Assert.Equal("Oi pessoal, como joga isso?", m.Text);
        Assert.Equal("#FF4500", m.Color);
        Assert.Equal(ChatKind.Message, m.Kind);
        Assert.False(m.IsPaid);
        Assert.Equal("abc-1", m.Id);
        Assert.Equal("Twitch", m.Platform);
    }

    [Fact]
    public void Cheer_com_bits_e_mensagem_paga()
    {
        var m = TwitchIrc.Parse("@bits=500;display-name=Doador;id=c1 :doador!doador@doador.tmi.twitch.tv PRIVMSG #canal :cheer500 Mandou bem!")!;
        Assert.True(m.IsPaid);
        Assert.Equal(ChatKind.Cheer, m.Kind);
        Assert.Equal("500 bits", m.Amount);
    }

    [Fact]
    public void Assinatura_e_raid_vem_do_texto_do_sistema_com_escapes_resolvidos()
    {
        var sub = TwitchIrc.Parse("@display-name=Ana;msg-id=resub;system-msg=Ana\\ssubscribed\\sfor\\s3\\smonths!;id=s1 :tmi.twitch.tv USERNOTICE #canal :Adoro a live")!;
        Assert.Equal(ChatKind.Subscription, sub.Kind);
        Assert.True(sub.IsPaid);
        Assert.Contains("Ana subscribed for 3 months!", sub.Text);
        Assert.Contains("Adoro a live", sub.Text);

        var raid = TwitchIrc.Parse("@display-name=Bia;msg-id=raid;system-msg=15\\sraiders\\sfrom\\sBia\\shave\\sjoined!;id=r1 :tmi.twitch.tv USERNOTICE #canal")!;
        Assert.Equal(ChatKind.Raid, raid.Kind);
        Assert.False(raid.IsPaid);
    }

    [Theory]
    [InlineData("")]
    [InlineData(":tmi.twitch.tv 001 justinfan123 :Welcome, GLHF!")]
    [InlineData("PING :tmi.twitch.tv")]
    [InlineData("@x=y")]
    public void Linhas_que_nao_sao_mensagens_sao_ignoradas(string line) => Assert.Null(TwitchIrc.Parse(line));

    [Fact]
    public void Ping_e_reconhecido_e_acao_me_perde_a_marca_do_irc()
    {
        Assert.True(TwitchIrc.IsPing("PING :tmi.twitch.tv"));
        var me = TwitchIrc.Parse("@display-name=Cris;id=m1 :cris!cris@cris.tmi.twitch.tv PRIVMSG #canal :\u0001ACTION foi jogar\u0001")!;
        Assert.Equal("foi jogar", me.Text);
    }

    // ---- chat do YouTube

    [Fact]
    public void Chat_do_youtube_traz_texto_super_chat_e_membros_e_o_proximo_ponto()
    {
        const string json = """
        {"pollingIntervalMillis":6000,"nextPageToken":"TOKEN2","items":[
          {"id":"1","snippet":{"type":"textMessageEvent","publishedAt":"2026-10-25T00:10:00Z","displayMessage":"Qual é a sua placa de vídeo?"},"authorDetails":{"displayName":"Luis"}},
          {"id":"2","snippet":{"type":"superChatEvent","publishedAt":"2026-10-25T00:11:00Z","displayMessage":"R$ 20,00 Parabéns","superChatDetails":{"amountDisplayString":"R$ 20,00","userComment":"Parabéns pela live"}},"authorDetails":{"displayName":"Marta"}},
          {"id":"3","snippet":{"type":"newSponsorEvent","publishedAt":"2026-10-25T00:12:00Z","displayMessage":""},"authorDetails":{"displayName":"Nando"}},
          {"id":"4","snippet":{"type":"messageDeletedEvent","publishedAt":"2026-10-25T00:12:00Z"},"authorDetails":{"displayName":"x"}}
        ]}
        """;
        var page = YouTubeChat.Parse(json);
        Assert.Null(page.Error);
        Assert.Equal("TOKEN2", page.NextPageToken);
        Assert.Equal(6000, page.PollMs);
        Assert.Equal(3, page.Messages.Count);
        Assert.Equal(ChatKind.SuperChat, page.Messages[1].Kind);
        Assert.Equal("R$ 20,00", page.Messages[1].Amount);
        Assert.Equal("Parabéns pela live", page.Messages[1].Text);
        Assert.True(page.Messages[1].IsPaid);
        Assert.True(page.Messages[2].IsPaid);
        Assert.False(page.Messages[0].IsPaid);
    }

    [Theory]
    [InlineData("liveChatEnded", "terminou")]
    [InlineData("quotaExceeded", "limite diário")]
    [InlineData("forbidden", "Abrir o chat")]
    [InlineData("liveChatNotFound", "não achou")]
    public void Erros_do_chat_do_youtube_viram_mensagens_claras(string reason, string expected)
    {
        var page = YouTubeChat.Parse("{\"error\":{\"errors\":[{\"reason\":\"" + reason + "\"}]}}");
        Assert.Contains(expected, page.Error);
    }

    [Fact]
    public void Codigo_do_chat_sai_dos_detalhes_da_live_e_o_endereco_nao_deixa_entrar_lixo()
    {
        Assert.Equal("Cg0KC2FiYw", YouTubeChat.ChatIdFromVideo("""{"items":[{"liveStreamingDetails":{"activeLiveChatId":"Cg0KC2FiYw"}}]}"""));
        Assert.Null(YouTubeChat.ChatIdFromVideo("""{"items":[{"liveStreamingDetails":{}}]}"""));
        Assert.Null(YouTubeChat.ChatIdFromVideo("<html>"));
        string url = YouTubeChat.MessagesUrl("a&b", "k", "p q");
        Assert.Contains("liveChatId=a%26b", url);
        Assert.Contains("pageToken=p%20q", url);
    }

    // ---- perguntas

    [Theory]
    [InlineData("Qual é a sua placa de vídeo?", true)]
    [InlineData("como faz para configurar", true)]
    [InlineData("isso aqui roda no meu pc?", true)]
    [InlineData("Que golaço!", false)]
    [InlineData("?", false)]
    [InlineData("", false)]
    public void Pergunta_e_reconhecida_por_interrogacao_ou_palavra_de_pergunta(string text, bool expected) => Assert.Equal(expected, ChatRules.IsQuestion(text));

    private static ChatMessage Msg(string id, string text, ChatKind kind = ChatKind.Message, string? amount = null) =>
        new(id, "YouTube", "Autor" + id, text, DateTime.Now, null, kind, amount);

    [Fact]
    public void Filtros_do_chat_separam_pagas_e_perguntas()
    {
        var all = new[] { Msg("1", "Que golaço!"), Msg("2", "Qual é o seu setup?"), Msg("3", "Parabéns", ChatKind.SuperChat, "R$ 10,00") };
        Assert.Equal(3, ChatRules.Filter(all, 0).Count());
        Assert.Equal(new[] { "3" }, ChatRules.Filter(all, 1).Select(m => m.Id));
        Assert.Equal(new[] { "2", "3" }, ChatRules.Filter(all, 2).Select(m => m.Id));
    }

    [Fact]
    public void Lista_de_perguntas_nao_repete_marca_respondida_e_exporta_texto()
    {
        var board = new QuestionBoard();
        Assert.True(board.Add(Msg("1", "Qual é o seu setup?")));
        Assert.False(board.Add(Msg("1", "Qual é o seu setup?")));
        Assert.True(board.Add(Msg("2", "Pode mostrar o PC?", ChatKind.SuperChat, "R$ 20,00")));
        Assert.Equal(2, board.Pending);

        board.SetAnswered("1", true);
        Assert.Equal(1, board.Pending);
        string text = board.ToText();
        Assert.Contains("[x] Autor1: Qual é o seu setup?", text);
        Assert.Contains("[ ] Autor2 (R$ 20,00): Pode mostrar o PC?", text);

        board.ClearAnswered();
        Assert.Single(board.Items);
        board.Remove("2");
        Assert.Empty(board.Items);
    }
}
