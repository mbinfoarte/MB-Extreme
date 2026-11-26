using MBExtreme.Core.NetDiag;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>Central de Rede: Wi-Fi, conflitos de IP, DNS, rótulos da rota, visão geral, reparação e histórico. Nada aqui usa rede de verdade.</summary>
public class CentralRedeTests
{
    private static WifiBss N(string ssid, int ch, int rssi, bool me = false, string sec = "WPA2", int? mhz = null) =>
        new(ssid, $"AA:BB:CC:00:{ch:X2}:{Math.Abs(rssi):X2}", mhz ?? (ch == 14 ? 2484 : ch <= 13 ? 2407 + ch * 5 : 5000 + ch * 5), rssi, sec, me);

    [Theory]
    [InlineData(2412, 1)]
    [InlineData(2437, 6)]
    [InlineData(2462, 11)]
    [InlineData(5180, 36)]
    [InlineData(5745, 149)]
    [InlineData(5955, 1)]
    public void Canal_pela_frequencia(int mhz, int ch) => Assert.Equal(ch, WifiRules.Channel(mhz));

    [Fact]
    public void Banda_e_qualidade_do_sinal()
    {
        Assert.Equal(WifiRules.B24, WifiRules.Band(2437));
        Assert.Equal(WifiRules.B5, WifiRules.Band(5220));
        Assert.Equal(WifiRules.B6, WifiRules.Band(6115));
        Assert.Equal("Excelente", WifiRules.Quality(-48));
        Assert.Equal("Fraco", WifiRules.Quality(-74));
    }

    [Fact]
    public void Canal_6_lotado_sugere_1_ou_11()
    {
        var scan = new WifiScan(new[]
        {
            N("Minha", 6, -45, me: true), N("Viz1", 6, -50), N("Viz2", 6, -55), N("Viz3", 7, -60), N("Viz4", 5, -58), N("Longe", 11, -88),
        });
        var a = WifiRules.Analyze(scan);
        Assert.Contains(a.Findings, f => f.Code == "WIFI-002");
        Assert.Contains(a.BestChannel[WifiRules.B24], new[] { 1, 11 });
        Assert.True(a.Score < 100);
    }

    [Fact]
    public void Wifi_bom_nao_inventa_problema()
    {
        var a = WifiRules.Analyze(new WifiScan(new[] { N("Minha", 44, -48, me: true), N("Viz", 149, -80) }));
        Assert.Equal("WIFI-OK", Assert.Single(a.Findings).Code);
        Assert.Equal(100, a.Score);
    }

    [Fact]
    public void Sinal_fraco_e_rede_aberta()
    {
        var a = WifiRules.Analyze(new WifiScan(new[] { N("Bar", 1, -83, me: true, sec: "Aberta") }));
        Assert.Contains(a.Findings, f => f.Code == "WIFI-001" && f.Severity == FindingSeverity.Problema);
        Assert.Contains(a.Findings, f => f.Code == "WIFI-004");
    }

    [Fact]
    public void Sugere_5GHz_quando_a_mesma_rede_existe()
    {
        var a = WifiRules.Analyze(new WifiScan(new[] { N("Casa", 6, -55, me: true), N("Casa", 44, -60) }));
        Assert.Contains(a.Findings, f => f.Code == "WIFI-003");
    }

    [Fact]
    public void Sem_wifi_conectado_nao_da_nota()
    {
        var a = WifiRules.Analyze(new WifiScan(new[] { N("Viz", 6, -60) }));
        Assert.Equal(0, a.Score);
        Assert.Equal("WIFI-000", a.Findings[0].Code);
    }

    [Fact]
    public void Estrutura_do_Windows_tem_o_tamanho_certo() => Assert.Equal(360, NetCenterProbe.BssEntrySize);

    [Fact]
    public void Conflito_de_IP_aparece_quando_o_MAC_muda_entre_varreduras()
    {
        var seen = new[]
        {
            new ArpSeen("192.168.0.23", "aa-bb-cc-dd-ee-01", 1), new ArpSeen("192.168.0.23", "AA:BB:CC:DD:EE:02", 2),
            new ArpSeen("192.168.0.1", "00-11-22-33-44-55", 1), new ArpSeen("192.168.0.1", "00:11:22:33:44:55", 2),
            new ArpSeen("192.168.0.255", "ff-ff-ff-ff-ff-ff", 1),
        };
        var c = Assert.Single(IpConflictRules.Find(seen));
        Assert.Equal("192.168.0.23", c.Ip);
        Assert.Equal(2, c.Macs.Count);
        Assert.True(c.Confidence >= 0.7);
        Assert.Contains("MAC aleatório", c.Counter);
    }

    [Fact]
    public void Aviso_do_Windows_confirma_conflito_do_proprio_PC()
    {
        var c = Assert.Single(IpConflictRules.Find(Array.Empty<ArpSeen>(), "192.168.0.10", ownConflictEvents: 2));
        Assert.Equal("192.168.0.10", c.Ip);
        Assert.True(c.Confidence >= 0.9);
    }

    [Fact]
    public void Mesmo_MAC_em_varios_IPs()
    {
        var multi = IpConflictRules.MultiIp(new[] { new ArpSeen("192.168.0.5", "AA-AA-AA-AA-AA-AA", 1), new ArpSeen("192.168.0.6", "AA-AA-AA-AA-AA-AA", 1) });
        Assert.Equal(2, Assert.Single(multi).Ips.Count);
    }

    private sealed class DnsProbe : INetProbe
    {
        public double First = 30, Second = 1, Nic = 60;
        public bool Hijack;
        private int _calls;
        public IReadOnlyList<NetIface> Interfaces() => Array.Empty<NetIface>();
        public Task<RttStats> PingAsync(string a, int c, int t, CancellationToken ct) => Task.FromResult(RttStats.From(1, new double[] { 1 }));
        public Task<DnsCheck> ResolveSystemAsync(string n, CancellationToken ct) => Task.FromResult(n.EndsWith(".invalid")
            ? new DnsCheck("DNS do PC", n, Hijack, Hijack ? 5 : null, Hijack ? "" : "não existe")
            : new DnsCheck("DNS do PC", n, true, _calls++ == 0 ? First : Second, ""));
        public Task<DnsCheck> ResolveViaAsync(string r, string n, CancellationToken ct) => Task.FromResult(new DnsCheck(r, n, true, r == "192.168.0.1" ? Nic : r == "1.1.1.1" ? 9 : 20, ""));
        public Task<HttpCheck> HttpAsync(string u, string b, CancellationToken ct) => Task.FromResult(new HttpCheck(u, true, 200, false, ""));
        public Task<IReadOnlyList<HopResult>> TraceAsync(string a, int m, int p, CancellationToken ct) => Task.FromResult<IReadOnlyList<HopResult>>(Array.Empty<HopResult>());
    }

    [Fact]
    public async Task Dns_mais_rapido_e_desvio_de_nome_inexistente()
    {
        var r = await DnsSuite.RunAsync(new DnsProbe { Hijack = true }, new[] { "192.168.0.1" }, 37, CancellationToken.None);
        Assert.Equal("Cloudflare (1.1.1.1)", r.Fastest!.Resolver);
        Assert.Contains(r.Resolvers, x => x.Resolver == "Roteador (192.168.0.1)");
        Assert.Contains(r.Findings, f => f.Code == "DNS-103");   // desvio
        Assert.Contains(r.Findings, f => f.Code == "DNS-104");   // há DNS mais rápido
        Assert.Equal(37, r.CacheEntries);
    }

    [Fact]
    public async Task Dns_bom_nao_inventa_problema()
    {
        var r = await DnsSuite.RunAsync(new DnsProbe { Nic = 10 }, new[] { "1.1.1.1" }, null, CancellationToken.None);
        Assert.Equal("DNS-OK", Assert.Single(r.Findings).Code);
    }

    [Fact]
    public void Cache_que_nao_acelera_vira_alerta()
    {
        var r = DnsSuite.Evaluate(Array.Empty<DnsCheck>(), new DnsCheck("PC", "x", true, 40, ""), new DnsCheck("PC", "x", true, 38, ""), false, null);
        Assert.Contains(r.Findings, f => f.Code == "DNS-102");
    }

    [Fact]
    public void Rotulos_da_rota()
    {
        var labels = RouteLabels.Label(new string?[] { "192.168.1.1", "100.64.0.1", "10.0.0.1", "186.192.88.65", null, "72.14.212.120", "142.250.227.11", "209.85.252.54", "142.250.78.100" });
        Assert.Equal("Rede local (roteador)", labels[0]);
        Assert.Equal("Provedor", labels[1]);
        Assert.Equal("Provedor", labels[2]);
        Assert.Equal("Provedor", labels[3]);
        Assert.Equal("Sem resposta", labels[4]);
        Assert.Equal("Internet", labels[7]);
        Assert.Equal("Destino", labels[8]);
    }

    private static NetIface Iface(string kind = "Ethernet") => new("Eth", "Placa", kind, true, "AA-BB-CC-DD-EE-FF", 1_000_000_000, new[] { "192.168.1.105" }, new[] { 24 },
        Array.Empty<string>(), new[] { "192.168.1.1" }, new[] { "1.1.1.1", "8.8.8.8" }, true, "192.168.1.1", 1500, false);

    private static NetDiagReport Report(double extMs = 12, double loss = 0, string kind = "Ethernet", params Finding[] findings)
    {
        var e = new NetDiagEvidence { Primary = Iface(kind), GatewayAddress = "192.168.1.1", Gateway = RttStats.From(10, Enumerable.Repeat(1.0, 10).ToList()) };
        int recv = (int)Math.Round(10 * (1 - loss / 100));
        e.External["1.1.1.1"] = RttStats.From(10, Enumerable.Repeat(extMs, recv).ToList());
        e.DnsSystem.Add(new DnsCheck("DNS do PC", "www.google.com", true, 9, ""));
        e.Hops.Add(new HopResult(1, "192.168.1.1", RttStats.From(3, new double[] { 1, 1, 1 })));
        e.Hops.Add(new HopResult(2, "142.250.78.100", RttStats.From(3, new double[] { 12, 12, 13 })));
        return new NetDiagReport(1, DateTimeOffset.Now, DateTimeOffset.Now, "PC", e, findings, Array.Empty<string>());
    }

    [Fact]
    public void Visao_geral_de_rede_boa_por_cabo()
    {
        var o = NetOverviewRules.From(Report());
        Assert.Equal("Conexão estável", o.Headline);
        Assert.True(o.Quality >= 90);
        Assert.Equal("Excelente", o.QualityText);
        Assert.Contains(o.Rows, r => r.Area == "Wi-Fi / Interferência" && r.State == "Não aplicável");
        Assert.Contains(o.Rows, r => r.Area == "Rota" && r.Text.StartsWith("2 saltos"));
        Assert.All(o.Rows.Where(r => r.Area != "Wi-Fi / Interferência"), r => Assert.Contains(r.State, new[] { "OK", "Bom" }));
    }

    [Fact]
    public void Visao_geral_com_problema_baixa_a_nota_e_recomenda()
    {
        var f = new Finding("NET-WAN-006", FindingSeverity.Problema, FindingStatus.Provavel, 0.8, "Internet fora", new[] { "x" }, Array.Empty<string>(), "Reinicie o modem.");
        var o = NetOverviewRules.From(Report(extMs: 150, loss: 30, findings: f));
        Assert.Equal(FindingSeverity.Problema, o.Level);
        Assert.True(o.Quality < 60);
        Assert.Contains("Reinicie o modem.", o.Recommendations);
    }

    [Fact]
    public void Visao_geral_no_wifi_usa_a_analise_do_wifi()
    {
        var wifi = WifiRules.Analyze(new WifiScan(new[] { N("Casa", 44, -48, me: true) }));
        var o = NetOverviewRules.From(Report(kind: "Wi-Fi"), wifi);
        Assert.Contains(o.Rows, r => r.Area == "Wi-Fi / Interferência" && r.Text.Contains("100/100"));
    }

    [Fact]
    public void Catalogo_de_reparacao_tem_impacto_e_reinicio()
    {
        Assert.Equal(4, NetRepairCatalog.All.Count);
        Assert.Equal("Baixo", NetRepairCatalog.Get(RepairKind.FlushDns).Impact);
        Assert.Equal("Alto", NetRepairCatalog.Get(RepairKind.ResetTcpIp).Impact);
        Assert.True(NetRepairCatalog.Get(RepairKind.ResetWinsock).NeedsRestart);
        Assert.False(NetRepairCatalog.Get(RepairKind.RenewDhcp).NeedsRestart);
    }

    [Fact]
    public void Historico_salva_e_lista_do_mais_novo_para_o_mais_velho()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mbx-hist-" + Guid.NewGuid().ToString("N"));
        try
        {
            var a = Report() with { StartedUtc = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero) };
            var b = Report() with { StartedUtc = new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.Zero) };
            Assert.NotNull(NetHistory.Save(a, dir));
            Assert.NotNull(NetHistory.Save(b, dir));
            var list = NetHistory.List(10, dir);
            Assert.Equal(2, list.Count);
            Assert.True(list[0].Report.StartedUtc > list[1].Report.StartedUtc);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
