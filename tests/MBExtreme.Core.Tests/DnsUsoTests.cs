using MBExtreme.Core.Dns;
using Xunit;

namespace MBExtreme.Core.Tests;

public class DnsUsoTests
{
    [Fact]
    public void Indica_para_que_cada_dns_e_melhor()
    {
        var cf = new DnsResult(new DnsProvider("Cloudflare", "1.1.1.1", "1.0.0.1"), 8, 10, 10);
        var g = new DnsResult(new DnsProvider("Google", "8.8.8.8", "8.8.4.4"), 11, 10, 10);
        var ad = new DnsResult(new DnsProvider("AdGuard", "94.140.14.14", "94.140.15.15"), 40, 9, 10);
        var off = new DnsResult(new DnsProvider("Control D", "76.76.2.0", "76.76.10.0"), null, 0, 10);
        var all = new[] { cf, g, ad, off };
        Assert.Equal(new[] { "Jogos", "Navegação" }, DnsAdvice.Uses(cf, all));
        Assert.Equal(new[] { "Navegação", "Streaming" }, DnsAdvice.Uses(g, all));
        Assert.Equal(new[] { "Proteção" }, DnsAdvice.Uses(ad, all));
        Assert.Empty(DnsAdvice.Uses(off, all));
    }
}
