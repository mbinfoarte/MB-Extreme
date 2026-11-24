using MBExtreme.Core.Network;
using Xunit;

namespace MBExtreme.Core.Tests;

public class RedeLocalTests
{
    [Fact]
    public void Le_a_tabela_arp_e_o_fabricante()
    {
        const string arp = "Interface: 192.168.0.10 --- 0x7\r\n  Endereço IP           Endereço físico       Tipo\r\n  192.168.0.1           50-c7-bf-11-22-33     dinâmico\r\n" +
                           "  192.168.0.23          fa-12-34-56-78-9a     dinâmico\r\n  192.168.0.255         ff-ff-ff-ff-ff-ff     estático\r\n  224.0.0.22            01-00-5e-00-00-16     estático\r\n";
        var list = LocalNetwork.ParseArp(arp);
        Assert.Equal(2, list.Count);
        Assert.Equal("TP-Link", LocalNetwork.VendorOf(list[0].Mac));
        Assert.Contains("privado", LocalNetwork.VendorOf(list[1].Mac));
        Assert.Equal("Videogame", new LanDevice("192.168.0.5", "", "Sony (PlayStation)", "", 3, false, false).Kind);
        Assert.Equal("Roteador", new LanDevice("192.168.0.1", "", "TP-Link", "", 1, true, false).Kind);
        Assert.Equal(253, LocalNetwork.Range("192.168.0.10").Count);
        Assert.Empty(LocalNetwork.Range("fe80::1"));
    }

    [Fact]
    public void Mede_lentidao_ate_o_roteador()
    {
        var good = LocalNetwork.Quality(new double?[] { 1, 1, 2, 1, 1 }, wifi: false);
        Assert.False(good.Slow);
        var bad = LocalNetwork.Quality(new double?[] { 5, 40, null, 30, 8 }, wifi: true);
        Assert.True(bad.Slow);
        Assert.Equal(20, bad.LossPercent);
        Assert.Contains("5 GHz", bad.Text);
    }

    [Fact]
    public void Recomenda_so_o_que_o_driver_aceita()
    {
        var props = new[]
        {
            new NicProperty("Ethernet", "Energy Efficient Ethernet", "Enabled", "*EEE", "1", new[] { "0", "1" }),
            new NicProperty("Ethernet", "Receive Buffers", "512", "*ReceiveBuffers", "512", new[] { "256", "512", "1024", "2048" }),
            new NicProperty("Ethernet", "Interrupt Moderation", "Disabled", "*InterruptModeration", "0", new[] { "0", "1" }),
            new NicProperty("Wi-Fi", "Roaming Aggressiveness", "3. Medium", "RoamAggressiveness", "2", new[] { "0", "1", "2", "3", "4" }),
            new NicProperty("Wi-Fi", "Preferred Band", "No Preference", "RoamingPreferredBandType", "0", new[] { "0", "1" }),
            new NicProperty("Ethernet", "Speed & Duplex", "Auto", "*SpeedDuplex", "0", new[] { "0", "1" }),
        };
        var changes = LocalNetwork.Recommend(props);
        Assert.Equal(new[] { "*EEE", "*ReceiveBuffers", "RoamAggressiveness" }, changes.Select(c => c.Keyword));
        Assert.Equal("2048", changes[1].To);
        Assert.Equal("0", changes[2].To);
    }

    [Fact]
    public void Le_as_propriedades_do_powershell()
    {
        const string json = "[{\"Name\":\"Ethernet\",\"DisplayName\":\"Energy Efficient Ethernet\",\"DisplayValue\":\"Enabled\",\"RegistryKeyword\":\"*EEE\",\"RegistryValue\":[\"1\"],\"ValidRegistryValues\":[\"0\",\"1\"]}]";
        var p = Assert.Single(LocalNetwork.ParseProperties(json));
        Assert.Equal("1", p.Value);
        Assert.Equal(2, p.ValidValues.Count);
    }
}
