using MBExtreme.Core.Network;
using MBExtreme.Core.Social;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>Redes da MB InfoArt, vídeo novo do canal, sugestões e servidores LibreSpeed.</summary>
public class CanalSuporteTests
{
    [Fact]
    public void Links_padrao_usam_o_nome_mbinfoarte()
    {
        var c = ChannelConfig.Defaults();
        Assert.Equal("mbinfoarte", c.Handle);
        Assert.Contains(c.Links, l => l.Url == "https://www.youtube.com/@mbinfoarte");
        Assert.Contains(c.Links, l => l.Url == "https://www.instagram.com/mbinfoarte");
        Assert.All(c.Links, l => Assert.Contains("mbinfoarte", l.Url));
        Assert.Equal("https://www.youtube.com/@mbinfoarte?sub_confirmation=1", c.Subscribe);
        Assert.Equal("https://www.youtube.com/@mbinfoarte/join", c.Membership);
    }

    [Fact]
    public void Arquivo_do_repositorio_e_valido()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "config", "canal.json");
        var c = ChannelConfig.Parse(File.ReadAllText(path));
        Assert.NotNull(c);
        Assert.Equal("mbinfoarte", c!.Handle);
        Assert.Equal(5, c.Links.Count);
    }

    [Fact]
    public void Arquivo_online_recusa_links_que_nao_sao_https()
    {
        var c = ChannelConfig.Parse("""{"handle":"mbinfoarte","links":[{"name":"ok","url":"https://a.com"},{"name":"ruim","url":"file:///C:/x.exe"}],"partners":[{"name":"P","url":"http://inseguro"}]}""");
        Assert.Single(c!.Links);
        Assert.Empty(c.Partners);
        Assert.Null(ChannelConfig.Parse("{quebrado"));
    }

    [Theory]
    [InlineData("(74) 99999-8888", "https://wa.me/5574999998888")]
    [InlineData("5574999998888", "https://wa.me/5574999998888")]
    [InlineData("", null)]
    public void Link_do_WhatsApp_do_suporte(string number, string? link) =>
        Assert.Equal(link, (ChannelConfig.Defaults() with { SupportWhatsApp = number }).WhatsAppLink());

    [Fact]
    public void Codigo_do_canal_pela_pagina_do_YouTube()
    {
        Assert.Equal("UCabcdefghijklmnopqrstuv", YouTubeFeed.ExtractChannelId("""<link rel="canonical" href="https://www.youtube.com/channel/UCabcdefghijklmnopqrstuv">"""));
        Assert.Equal("UC1234567890123456789012", YouTubeFeed.ExtractChannelId("""{"externalId":"UC1234567890123456789012","x":1}"""));
        Assert.Null(YouTubeFeed.ExtractChannelId("<html>nada</html>"));
    }

    private const string Feed = """
        <?xml version="1.0" encoding="UTF-8"?>
        <feed xmlns:yt="http://www.youtube.com/xml/schemas/2015" xmlns="http://www.w3.org/2005/Atom">
          <entry><yt:videoId>AAAAAAAAAA1</yt:videoId><title>Vídeo antigo</title><published>2026-10-01T12:00:00+00:00</published></entry>
          <entry><yt:videoId>BBBBBBBBBB2</yt:videoId><title>Seu PC está LENTO?</title><published>2026-11-06T15:00:00+00:00</published></entry>
        </feed>
        """;

    [Fact]
    public void Le_o_feed_do_canal_do_mais_novo_para_o_mais_velho()
    {
        var videos = YouTubeFeed.ParseFeed(Feed.Trim());
        Assert.Equal(2, videos.Count);
        Assert.Equal("BBBBBBBBBB2", videos[0].Id);
        Assert.Equal("https://www.youtube.com/watch?v=BBBBBBBBBB2", videos[0].Url);
        Assert.Equal("https://i.ytimg.com/vi/BBBBBBBBBB2/mqdefault.jpg", videos[0].Thumbnail);
        Assert.Empty(YouTubeFeed.ParseFeed("não é xml"));
    }

    [Fact]
    public void Avisa_uma_vez_e_so_de_video_recente()
    {
        var now = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
        var video = new YouTubeVideo("BBBBBBBBBB2", "x", now.AddDays(-1));
        Assert.True(YouTubeFeed.ShouldNotify(video, "", now));
        Assert.False(YouTubeFeed.ShouldNotify(video, "BBBBBBBBBB2", now));        // já avisado
        Assert.False(YouTubeFeed.ShouldNotify(video with { Published = now.AddDays(-10) }, "", now));   // antigo
        Assert.Equal("Publicado hoje", YouTubeFeed.Ago(now.AddHours(-1), now));
    }

    [Fact]
    public void Sugestao_curta_demais_nao_vai_e_mensagem_leva_versao()
    {
        Assert.NotNull(new Suggestion("Ideia", "oi", "", "0.9.0", "Windows 11").Problem);
        var s = new Suggestion("Problema", "O teste de internet travou no upload.", "74999998888", "0.9.0", "Windows 11 Pro");
        Assert.Null(s.Problem);
        Assert.Contains("[Problema] MB Extreme 0.9.0 · Windows 11 Pro", s.AsMessage());
        Assert.Contains("Contato: 74999998888", s.AsMessage());
    }

    [Fact]
    public void Lista_do_LibreSpeed_so_com_https()
    {
        const string json = """
            [{"id":1,"name":"Sao Paulo, Brazil (Provedor)","server":"//sp.exemplo.com.br/backend","dlURL":"garbage.php","ulURL":"empty.php","pingURL":"empty.php","sponsorName":"Provedor"},
             {"id":2,"name":"Insecure","server":"http://x.com/","dlURL":"garbage.php"},
             {"id":3,"name":"Amsterdam, Netherlands","server":"https://ams.exemplo.net/","dlURL":"","ulURL":"","pingURL":""}]
            """;
        var list = LibreSpeed.ParseList(json);
        Assert.Equal(2, list.Count);
        Assert.Equal("https://sp.exemplo.com.br/backend", list[0].BaseUrl);
        Assert.Equal("https://sp.exemplo.com.br/backend/garbage.php", list[0].Url(list[0].DownloadUrl));
        Assert.Equal("empty.php", list[1].PingUrl);   // padrão quando vem vazio
        Assert.Equal(0, LibreSpeed.RegionRank(list[0]));
        Assert.Equal(3, LibreSpeed.RegionRank(list[1]));
        Assert.Empty(LibreSpeed.ParseList("{}"));
    }
}
