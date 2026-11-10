using MBExtreme.Core.Social;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>Links de afiliados em Nossas redes: vêm do canal.json online, agrupados em lojas, games e IA.</summary>
public class AfiliadosTests
{
    private const string Json = """
    {
      "handle": "mbinfoarte",
      "affiliates": [
        { "name": "Mercado Livre", "category": "lojas", "description": "Peças e periféricos", "url": "https://mercadolivre.com/sec/abc", "color": "#FFE600" },
        { "name": "KaBuM!", "category": "LOJAS", "description": "Hardware", "url": "https://www.kabum.com.br/?x=1", "coupon": "MBX10" },
        { "name": "ExitLag", "category": "games", "description": "Menos ping", "url": "https://www.exitlag.com/refer/abc" },
        { "name": "ChatGPT", "category": "ia", "description": "", "url": "https://chatgpt.com/" },
        { "name": "Parceiro novo", "category": "cursos", "description": "", "url": "https://exemplo.com/" },
        { "name": "Golpe", "category": "lojas", "description": "", "url": "http://inseguro.com/" },
        { "name": "", "category": "lojas", "description": "", "url": "https://semnome.com/" }
      ]
    }
    """;

    [Fact]
    public void Agrupa_por_lojas_games_ia_e_outros()
    {
        var c = ChannelConfig.Parse(Json)!;
        Assert.Equal(new[] { "Mercado Livre", "KaBuM!" }, c.AffiliatesIn("lojas").Select(a => a.Name));   // sem ligar para maiúsculas
        Assert.Equal("ExitLag", Assert.Single(c.AffiliatesIn("games")).Name);
        Assert.Equal("ChatGPT", Assert.Single(c.AffiliatesIn("ia")).Name);
        Assert.Equal("Parceiro novo", Assert.Single(c.AffiliatesIn("")).Name);                          // grupo desconhecido: Outros parceiros
        Assert.Equal("MBX10", c.Affiliates.Single(a => a.Name == "KaBuM!").Coupon);
    }

    [Fact]
    public void Link_sem_https_ou_sem_nome_fica_de_fora()
    {
        var c = ChannelConfig.Parse(Json)!;
        Assert.DoesNotContain(c.Affiliates, a => a.Name == "Golpe");
        Assert.DoesNotContain(c.Affiliates, a => a.Url.Contains("semnome"));
        Assert.Empty(ChannelConfig.Defaults().Affiliates);   // sem o arquivo online, a seção não aparece
    }
}
