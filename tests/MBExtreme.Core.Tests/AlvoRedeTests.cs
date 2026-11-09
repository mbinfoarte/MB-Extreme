using MBExtreme.Core.Network;
using Xunit;

namespace MBExtreme.Core.Tests;

public class AlvoRedeTests
{
    [Theory]
    [InlineData("1.1.1.1", "1.1.1.1", null)]
    [InlineData("google.com", "google.com", null)]
    [InlineData("https://www.exemplo.com.br/pagina?x=1#topo", "www.exemplo.com.br", 443)]
    [InlineData("http://exemplo.com:8080/a", "exemplo.com", 8080)]
    [InlineData("  \"https://user:senha@servidor.net/\"  ", "servidor.net", 443)]
    [InlineData("jogo.exemplo.com:27015", "jogo.exemplo.com", 27015)]
    [InlineData("exemplo.com.", "exemplo.com", null)]
    [InlineData("https://[2001:db8::1]:8443/x", "2001:db8::1", 8443)]
    public void Entende_ip_site_e_endereco_copiado_do_navegador(string input, string host, int? port)
    {
        var t = NetTarget.Parse(input);
        Assert.NotNull(t);
        Assert.Equal(host, t!.Host);
        Assert.Equal(port, t.Port);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("site com espaço")]
    [InlineData("exemplo.com:99999")]
    [InlineData("exemplo.com:abc")]
    public void Recusa_o_que_nao_e_endereco(string input) => Assert.Null(NetTarget.Parse(input));

    [Fact]
    public async Task Ip_literal_nao_consulta_o_dns()
    {
        var found = await NetTarget.ResolveAsync("8.8.8.8");
        Assert.Single(found);
        Assert.Equal("8.8.8.8", found[0].ToString());
    }
}
