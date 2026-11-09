using MBExtreme.Core.Network;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>Catálogo de servidores de jogos da tela Internet: regiões, distância, Valve, LibreSpeed e mapa.</summary>
public class ServidoresJogosTests
{
    [Fact]
    public void Catalogo_fixo_tem_sao_paulo_e_enderecos_no_padrao_de_cada_nuvem()
    {
        var all = GameServers.Static;
        Assert.Equal(all.Count, all.Select(t => t.Key).Distinct().Count());
        Assert.Contains(all, t => t.Key == "aws-sa-east-1" && t.Group == GameServers.Brazil);
        Assert.Contains(all, t => t.Key == "oci-sa-saopaulo-1" && t.Group == GameServers.Brazil);
        Assert.All(all.Where(t => t.Provider == "AWS"), t => Assert.Matches(@"^dynamodb\.[a-z]{2}-[a-z]+-\d\.amazonaws\.com$", t.Host));
        Assert.All(all.Where(t => t.Provider == "Oracle"), t => Assert.Matches(@"^objectstorage\.[a-z]{2}-[a-z]+-\d\.oraclecloud\.com$", t.Host));
        Assert.All(all, t => Assert.Contains(t.Group, GameServers.Regions));
        Assert.All(all.Where(t => !double.IsNaN(t.Lat)), t => Assert.Equal(t.Group, GameServers.RegionOf(t.Lat, t.Lon)));
    }

    [Theory]
    [InlineData(-23.55, -46.63, "Brasil")]
    [InlineData(-3.73, -38.52, "Brasil")]
    [InlineData(-3.12, -60.02, "Brasil")]
    [InlineData(-34.6, -58.38, "América do Sul")]
    [InlineData(-33.45, -70.67, "América do Sul")]
    [InlineData(25.76, -80.19, "América do Norte")]
    [InlineData(50.1, 8.7, "Europa")]
    [InlineData(35.7, 139.7, "Ásia e outros")]
    public void Regiao_pela_posicao(double lat, double lon, string region) => Assert.Equal(region, GameServers.RegionOf(lat, lon));

    [Fact]
    public void Distancia_e_ping_minimo()
    {
        double km = GameServers.DistanceKm(-3.73, -38.52, -23.55, -46.63);   // Fortaleza → São Paulo
        Assert.InRange(km, 2300, 2450);
        Assert.InRange(GameServers.MinimumPingMs(km), 23, 25);
        Assert.Equal(0, GameServers.DistanceKm(1, 2, 1, 2), 6);
    }

    [Fact]
    public void Mais_perto_poe_as_referencias_primeiro_e_ordena_por_distancia()
    {
        var near = GameServers.Nearest(GameServers.Static, -3.73, -38.52, 5);   // Fortaleza
        Assert.Equal(5, near.Count);
        Assert.True(double.IsNaN(near[0].Lat) && double.IsNaN(near[1].Lat));
        Assert.Equal(GameServers.Brazil, near[2].Group);
        var far = GameServers.Nearest(GameServers.Static, 50.1, 8.7, 4);       // Frankfurt
        Assert.Equal("aws-eu-central-1", far[2].Key);
    }

    [Theory]
    [InlineData(10.0, 0)]
    [InlineData(45.0, 1)]
    [InlineData(80.0, 2)]
    [InlineData(150.0, 3)]
    [InlineData(null, 3)]
    public void Cor_do_ping(double? ms, int level) => Assert.Equal(level, GameServers.Level(ms));

    [Fact]
    public void Explicacao_compara_com_o_minimo_da_distancia()
    {
        Assert.Contains("o resto é o caminho", GameServers.Explain("São Paulo", 90, 2370));
        Assert.Contains("rota do provedor está boa", GameServers.Explain("São Paulo", 30, 2370));
        Assert.DoesNotContain("distância", GameServers.Explain("Fortaleza", 6, 5));
    }

    [Fact]
    public void Le_os_relays_da_valve()
    {
        const string json = """
            {"revision":1,"pops":{
              "gru":{"desc":"Sao Paulo (Brazil)","geo":[-46.6,-23.6],"relays":[{"ipv4":"155.133.227.10","port_range":[27015,27060]}]},
              "eze":{"desc":"Buenos Aires (Argentina)","geo":[-58.4,-34.6],"relays":[{"ipv4":"155.133.249.1"}]},
              "sem":{"desc":"Sem relay","geo":[0,0],"relays":[]},
              "ruim":{"desc":"IP ruim","geo":[1,1],"relays":[{"ipv4":"abc"}]}}}
            """;
        var list = GameServers.ParseValveSdr(json);
        Assert.Equal(2, list.Count);
        var gru = list.Single(t => t.Key == "valve-gru");
        Assert.Equal("São Paulo", gru.Name);
        Assert.Equal(GameServers.Brazil, gru.Group);
        Assert.True(gru.Icmp);
        Assert.Equal("155.133.227.10", gru.Host);
        Assert.Equal(GameServers.SouthAmerica, list.Single(t => t.Key == "valve-eze").Group);
        Assert.Empty(GameServers.ParseValveSdr("não é json"));
    }

    [Fact]
    public void LibreSpeed_com_cidade_conhecida_vira_alvo()
    {
        var servers = new[]
        {
            new LibreServer(1, "São Paulo, Brazil (Provedor X)", "//speed.exemplo.com.br/", "garbage.php", "empty.php", "empty.php", "Provedor X"),
            new LibreServer(2, "Fortaleza, Brazil", "https://ft.exemplo.net", "garbage.php", "empty.php", "empty.php", ""),
            new LibreServer(3, "Cidade Desconhecida", "https://x.exemplo.net", "garbage.php", "empty.php", "empty.php", ""),
            new LibreServer(4, "Recife, Brazil", "http://sem-https.exemplo.net", "garbage.php", "empty.php", "empty.php", ""),
        };
        var list = GameServers.FromLibreSpeed(servers);
        Assert.Equal(2, list.Count);
        var sp = list.Single(t => t.Key == "libre-1");
        Assert.Equal("speed.exemplo.com.br", sp.Host);
        Assert.Equal(GameServers.Brazil, sp.Group);
        Assert.InRange(sp.Lat, -24, -23);
        Assert.Equal("LibreSpeed", sp.Provider);
        Assert.NotNull(GameServers.Locate("SAO PAULO"));
        Assert.Null(GameServers.Locate("Lugar nenhum"));
    }

    [Fact]
    public void Areas_do_mapa()
    {
        var br = NetMap.BrazilArea(1.0);
        Assert.True(NetMap.Contains(br, -23.55, -46.63));
        Assert.False(NetMap.Contains(br, 50.1, 8.7));
        var world = NetMap.WorldArea(2.0);
        Assert.True(NetMap.Contains(world, 50.1, 8.7) && NetMap.Contains(world, 35.7, 139.7));
    }
}
