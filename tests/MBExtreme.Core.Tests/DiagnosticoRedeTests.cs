using MBExtreme.Core.NetDiag;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>Cenários da especificação (seção 11) com um coletor falso: nada aqui usa rede de verdade.</summary>
public class DiagnosticoRedeTests
{
    private static NetIface Iface(string kind = "Ethernet", string ip = "192.168.0.10", int prefix = 24, string gw = "192.168.0.1",
        string[]? dns = null, long speed = 1_000_000_000, string name = "Ethernet") =>
        new(name, name, kind, true, "AA-BB-CC-DD-EE-FF", speed, new[] { ip }, new[] { prefix }, Array.Empty<string>(),
            gw.Length > 0 ? new[] { gw } : Array.Empty<string>(), dns ?? new[] { "192.168.0.1" }, true, "192.168.0.1", 1500, false);

    private static RttStats Ok(double ms = 2) => RttStats.From(10, Enumerable.Repeat(ms, 10).ToList());
    private static RttStats Dead() => RttStats.From(10, Array.Empty<double>());

    private sealed class FakeProbe : INetProbe
    {
        public List<NetIface> Ifaces = new() { Iface() };
        public Func<string, RttStats> Ping = _ => Ok();
        public bool SystemDns = true, AltDns = true;
        public HttpCheck Http = new("u", true, 200, false, "abriu");
        public List<HopResult> Hops = new() { new(1, "192.168.0.1", Ok()), new(2, "1.1.1.1", Ok(10)) };
        public int TraceCalls;

        public IReadOnlyList<NetIface> Interfaces() => Ifaces;
        public Task<RttStats> PingAsync(string a, int c, int t, CancellationToken ct) => Task.FromResult(Ping(a));
        public Task<DnsCheck> ResolveSystemAsync(string n, CancellationToken ct) => Task.FromResult(new DnsCheck("DNS do PC", n, SystemDns, SystemDns ? 20 : null, SystemDns ? "" : "sem resposta"));
        public Task<DnsCheck> ResolveViaAsync(string r, string n, CancellationToken ct) => Task.FromResult(new DnsCheck(r, n, AltDns, AltDns ? 15 : null, AltDns ? "" : "sem resposta"));
        public Task<HttpCheck> HttpAsync(string u, string b, CancellationToken ct) => Task.FromResult(Http);
        public Task<IReadOnlyList<HopResult>> TraceAsync(string a, int m, int p, CancellationToken ct) { TraceCalls++; return Task.FromResult<IReadOnlyList<HopResult>>(Hops); }
    }

    private static Task<NetDiagReport> Run(FakeProbe p) => new NetDiagRunner(p).RunAsync(new NetDiagOptions(), null, CancellationToken.None);

    [Fact]
    public async Task Tudo_ok_nao_inventa_problema()
    {
        var r = await Run(new FakeProbe());
        Assert.Equal("NET-OK-000", r.Findings[0].Code);
        Assert.DoesNotContain(r.Findings, f => f.Severity != FindingSeverity.Info);
    }

    [Fact]
    public async Task Dns_falhando_e_sinalizado_com_evidencia_sem_mexer()
    {
        var r = await Run(new FakeProbe { SystemDns = false });
        var f = Assert.Single(r.Findings, x => x.Code == "DR-001");
        Assert.Equal(FindingStatus.Provavel, f.Status);
        Assert.Contains(f.Evidence, e => e.Contains("DNS alternativo"));
        Assert.NotEmpty(r.Evidence.DnsAlternate);
    }

    [Fact]
    public async Task Com_vpn_o_dns_tem_confianca_menor_e_aviso_de_dns_dividido()
    {
        var p = new FakeProbe { SystemDns = false };
        p.Ifaces.Add(Iface(kind: "VPN", ip: "10.8.0.2", gw: "", name: "VPN"));
        var f = Assert.Single((await Run(p)).Findings, x => x.Code == "DR-001");
        Assert.True(f.Confidence <= 0.5);
        Assert.Contains(f.Counter, c => c.Contains("VPN"));
    }

    [Fact]
    public async Task Roteador_sem_resposta_e_internet_fora()
    {
        var p = new FakeProbe { Ping = a => a == "127.0.0.1" ? Ok(0.1) : Dead() };
        var r = await Run(p);
        Assert.Contains(r.Findings, f => f.Code == "NET-GW-005" && f.Confidence >= 0.8);
        Assert.Equal(0, p.TraceCalls);   // sem internet por IP não traça caminho
    }

    [Fact]
    public async Task Roteador_ok_e_internet_fora_aponta_depois_do_roteador()
    {
        var r = await Run(new FakeProbe { Ping = a => a is "1.1.1.1" or "8.8.8.8" ? Dead() : Ok() });
        Assert.Contains(r.Findings, f => f.Code == "NET-WAN-006");
    }

    [Fact]
    public async Task Perda_no_roteador_pelo_wifi_e_instabilidade_local()
    {
        var p = new FakeProbe { Ping = a => a == "192.168.0.1" ? RttStats.From(10, new double[] { 3, 80, 4, 90, 5, 70, 3 }) : Ok() };
        p.Ifaces[0] = Iface(kind: "Wi-Fi", name: "Wi-Fi");
        var f = Assert.Single((await Run(p)).Findings, x => x.Code == "DR-002");
        Assert.Contains(f.Evidence, e => e.Contains("Perda"));
        Assert.Contains("cabo", f.Next);
    }

    [Fact]
    public async Task Salto_do_meio_sem_resposta_nao_e_falha()
    {
        var p = new FakeProbe { Hops = new() { new(1, "192.168.0.1", Ok()), new(2, null, Dead()), new(3, "1.1.1.1", Ok(12)) } };
        var f = Assert.Single((await Run(p)).Findings, x => x.Code == "DR-005");
        Assert.Equal(FindingSeverity.Info, f.Severity);
    }

    [Fact]
    public async Task Apipa_sem_dns_e_cabo_100_mbps()
    {
        var p = new FakeProbe();
        p.Ifaces[0] = Iface(ip: "169.254.10.4", prefix: 16, dns: Array.Empty<string>(), speed: 100_000_000);
        var r = await Run(p);
        Assert.Contains(r.Findings, f => f.Code == "NET-IP-002" && f.Status == FindingStatus.Confirmado);
        Assert.Contains(r.Findings, f => f.Code == "NET-DNS-003");
        Assert.Contains(r.Findings, f => f.Code == "DR-003");
    }

    [Fact]
    public async Task Portal_cativo_nao_vira_problema_de_dns()
    {
        var r = await Run(new FakeProbe { Http = new("u", false, 302, true, "redirecionado para login.hotel.com") });
        Assert.Contains(r.Findings, f => f.Code == "NET-HTTP-009");
        Assert.DoesNotContain(r.Findings, f => f.Code == "DR-001");
    }

    [Fact]
    public async Task Sem_placa_conectada()
    {
        var r = await Run(new FakeProbe { Ifaces = new() });
        Assert.Equal("NET-IF-001", r.Findings[0].Code);
        Assert.NotEmpty(r.NotTested);
    }

    [Fact]
    public void Placa_principal_prefere_cabo_e_deixa_vpn_por_ultimo()
    {
        var list = new[] { Iface(kind: "VPN", name: "VPN"), Iface(kind: "Wi-Fi", name: "Wi-Fi"), Iface(name: "Cabo") };
        Assert.Equal("Cabo", NetDiagRunner.PickPrimary(list)!.Name);
    }

    [Fact]
    public void Estatistica_de_latencia()
    {
        var s = RttStats.From(5, new double[] { 10, 20, 30, 40 });
        Assert.Equal(20, s.LossPct);
        Assert.Equal(10, s.Min);
        Assert.Equal(40, s.Max);
        Assert.Equal(10, s.Jitter);
        Assert.True(RttStats.From(3, Array.Empty<double>()).AllLost);
    }

    [Theory]
    [InlineData("192.168.0.0", 24, true)]
    [InlineData("10.0.0.0", 22, true)]
    [InlineData("10.0.0.0", 16, false)]
    [InlineData("8.8.8.0", 24, false)]
    [InlineData("lixo", 24, false)]
    public void Varredura_so_em_rede_privada_e_pequena(string net, int prefix, bool ok) =>
        Assert.Equal(ok, NetScope.CanScan(net, prefix).Allowed);

    [Fact]
    public async Task Relatorio_json_volta_igual_e_html_esconde_dados()
    {
        var r = await Run(new FakeProbe { SystemDns = false });
        var back = NetDiagReportWriter.FromJson(NetDiagReportWriter.ToJson(r))!;
        Assert.Equal(r.Findings.Count, back.Findings.Count);
        Assert.Equal(NetDiagReport.CurrentSchema, back.SchemaVersion);
        string masked = NetDiagReportWriter.ToHtml(r, masked: true);
        Assert.DoesNotContain("192.168.0.10", masked);
        Assert.Contains("192.168.x.x", masked);
        Assert.Contains("192.168.0.10", NetDiagReportWriter.ToHtml(r, masked: false));
        Assert.Contains("nada foi alterado", masked);
    }
}
