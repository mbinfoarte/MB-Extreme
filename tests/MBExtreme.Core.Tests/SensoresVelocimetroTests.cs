using MBExtreme.Core.Network;
using MBExtreme.Core.Sensors;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>Leituras iguais às do Gerenciador de Tarefas (placa certa, motor mais ocupado) e a escala do velocímetro.</summary>
public class SensoresVelocimetroTests
{
    [Fact]
    public void Com_integrada_e_dedicada_mostra_a_dedicada()
    {
        var gpus = new[]
        {
            new GpuReading("GpuAmd", "AMD Radeon(TM) Graphics", 45, 2, 5, 400),
            new GpuReading("GpuNvidia", "NVIDIA GeForce RTX 4070", 38, 1, 20, 210),
        };
        Assert.Equal("NVIDIA GeForce RTX 4070", UsageRules.PickGpu(gpus)!.Name);
        Assert.Equal("NVIDIA GeForce RTX 4070", UsageRules.PickGpu(gpus.Reverse().ToList())!.Name);
    }

    [Theory]
    [InlineData("GpuAmd", "AMD Radeon(TM) Graphics", true)]
    [InlineData("GpuAmd", "AMD Radeon RX 7800 XT", false)]
    [InlineData("GpuIntel", "Intel(R) UHD Graphics 770", true)]
    [InlineData("GpuIntel", "Intel(R) Arc(TM) A770", false)]
    [InlineData("GpuNvidia", "NVIDIA GeForce GTX 1650", false)]
    public void Placa_integrada_reconhecida(string kind, string name, bool integrated) => Assert.Equal(integrated, UsageRules.LooksIntegrated(kind, name));

    [Fact]
    public void Uma_placa_so_e_ela_mesma()
    {
        Assert.Equal("Intel(R) UHD Graphics", UsageRules.PickGpu(new[] { new GpuReading("GpuIntel", "Intel(R) UHD Graphics", 50, 10, null, null) })!.Name);
        Assert.Null(UsageRules.PickGpu(Array.Empty<GpuReading>()));
    }

    [Fact]
    public void Uso_da_placa_e_o_motor_mais_ocupado_de_cada_placa()
    {
        const string dgpu = "0x00000000_0x0000D1E2", igpu = "0x00000000_0x0000A001";
        var samples = new (string, double)[]
        {
            ($"pid_100_luid_{dgpu}_phys_0_eng_0_engtype_3D", 30),
            ($"pid_200_luid_{dgpu}_phys_0_eng_0_engtype_3D", 25),        // dois programas no 3D somam
            ($"pid_100_luid_{dgpu}_phys_0_eng_3_engtype_VideoDecode", 40),
            ($"pid_300_luid_{igpu}_phys_0_eng_0_engtype_3D", 12),
            ("lixo", 99),
        };
        var byAdapter = UsageRules.GpuByAdapter(samples);
        Assert.Equal(55, byAdapter[dgpu]);
        Assert.Equal(12, byAdapter[igpu]);
        Assert.Equal(("0x00000000_0x0000D1E2", "VideoDecode"), UsageRules.ParseEngine($"pid_1_luid_{dgpu}_phys_0_eng_3_engtype_VideoDecode"));
    }

    [Fact]
    public void Placa_dedicada_e_a_que_usa_mais_memoria_propria()
    {
        var memory = new (string, double)[] { ("luid_0x00000000_0x0000A001_phys_0", 512e6), ("luid_0x00000000_0x0000D1E2_phys_0", 3.2e9) };
        Assert.Equal("0x00000000_0x0000D1E2", UsageRules.DedicatedAdapter(memory));
        Assert.Null(UsageRules.DedicatedAdapter(Array.Empty<(string, double)>()));
    }

    [Fact]
    public void Temperatura_do_processador_pelo_sensor_principal()
    {
        Assert.Equal(52, UsageRules.CpuTemperature(new[] { ("CCD1 (Tdie)", 61.0), ("Core (Tctl/Tdie)", 52.0) }));
        Assert.Equal(48, UsageRules.CpuTemperature(new[] { ("CPU Core #1", 55.0), ("CPU Package", 48.0) }));
        Assert.Equal(55, UsageRules.CpuTemperature(new[] { ("Core #1", 50.0), ("Core #2", 55.0) }));
        Assert.Null(UsageRules.CpuTemperature(Array.Empty<(string, double)>()));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 0.125)]
    [InlineData(100, 0.5)]
    [InlineData(175, 0.5625)]
    [InlineData(1000, 1)]
    [InlineData(2000, 1)]
    [InlineData(-3, 0)]
    public void Escala_do_velocimetro(double mbps, double fraction) => Assert.Equal(fraction, SpeedScale.Fraction(mbps, SpeedScale.Normal), 4);

    [Fact]
    public void Internet_acima_de_1_giga_usa_a_escala_rapida()
    {
        Assert.Same(SpeedScale.Normal, SpeedScale.TicksFor(485));
        Assert.Same(SpeedScale.Fast, SpeedScale.TicksFor(1800));
        Assert.Equal("5G", SpeedScale.Label(5000));
        Assert.Equal("1000", SpeedScale.Label(1000));
    }
}

/// <summary>Mapa do teste de internet, localização pelo IP e ping de jogo.</summary>
public class MapaInternetTests
{
    [Fact]
    public void Mapa_tem_os_paises_e_o_Brasil()
    {
        var countries = NetMap.Rings(WorldMapData.Countries).ToList();
        var brazil = NetMap.Rings(WorldMapData.Brazil).ToList();
        Assert.True(countries.Count > 150);
        Assert.NotEmpty(brazil);
        Assert.All(brazil.SelectMany(r => r), p => Assert.InRange(p.Lon, -75, -28));
        Assert.All(countries.SelectMany(r => r), p => { Assert.InRange(p.Lon, -180, 180); Assert.InRange(p.Lat, -90, 90); });
    }

    [Fact]
    public void Enquadramento_mostra_no_minimo_a_America_do_Sul_e_inclui_servidores_longe()
    {
        var sa = NetMap.Fit(new[] { (-11.18, -40.51), (-23.43, -46.47) }, 560.0 / 300);
        Assert.True(sa.MinLon <= -85 && sa.MaxLon >= -32 && sa.MinLat <= -56 && sa.MaxLat >= 13);
        var far = NetMap.Fit(new[] { (-11.18, -40.51), (50.1, 8.7) }, 560.0 / 300);
        Assert.True(far.MaxLat >= 56 && far.MaxLon >= 16);
        var (x, y) = NetMap.Project(-11.18, -40.51, sa, 560, 300);
        Assert.InRange(x, 0, 560);
        Assert.InRange(y, 0, 300);
    }

    [Fact]
    public void Saltos_ficam_na_linha_em_ordem_pelo_tempo()
    {
        var pos = NetMap.HopPositions(new double?[] { 1, 3, null, 12, 10, 20 });
        Assert.Equal(6, pos.Count);
        for (int i = 1; i < pos.Count; i++) Assert.True(pos[i] > pos[i - 1]);
        Assert.All(pos, t => Assert.InRange(t, 0.04, 0.96));
    }

    [Fact]
    public void Localizacao_pelo_ipwho_e_pelo_ipapi()
    {
        var a = NetMap.ParseLocation("""{"ip":"177.154.91.14","success":true,"city":"Jacobina","region":"Bahia","country":"Brasil","latitude":-11.18,"longitude":-40.51,"connection":{"isp":"Heptanet"}}""");
        Assert.Equal(("Jacobina", "Heptanet", -11.18), (a!.City, a.Isp, a.Lat));
        var b = NetMap.ParseLocation("""{"ip":"1.2.3.4","city":"Salvador","region":"Bahia","country_name":"Brazil","latitude":-12.97,"longitude":-38.5,"org":"Provedor X"}""");
        Assert.Equal(("Salvador", "Provedor X", "Brazil"), (b!.City, b.Isp, b.Country));
        Assert.Null(NetMap.ParseLocation("""{"success":false,"message":"limite"}"""));
        Assert.Null(NetMap.ParseLocation("quebrado"));
    }

    [Fact]
    public void Servidores_de_jogo_tem_Sao_Paulo_e_regioes_validas()
    {
        Assert.Contains(NetMap.GameTargets, t => t.Key == "aws-sa-east-1" && t.Group == "Brasil");
        Assert.All(NetMap.GameTargets, t => Assert.Contains(t.Group, NetMap.Groups));
        Assert.All(NetMap.GameTargets.Where(t => t.Key.StartsWith("aws-")), t => Assert.Matches(@"^dynamodb\.[a-z]{2}-[a-z]+-\d\.amazonaws\.com$", t.Host));
        Assert.Equal(NetMap.GameTargets.Count, NetMap.GameTargets.Select(t => t.Key).Distinct().Count());
        Assert.True(NetMap.Colos.ContainsKey("GRU") && NetMap.Colos.ContainsKey("SSA"));
    }

    [Theory]
    [InlineData(12.0, "Excelente")]
    [InlineData(40.0, "Muito bom")]
    [InlineData(200.0, "Alto demais")]
    public void Frase_do_ping_de_jogo(double ms, string part) => Assert.Contains(part, NetMap.GameVerdict(ms));
}
