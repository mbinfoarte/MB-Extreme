using System.Net;
using MBExtreme.Core.Network;
using Xunit;

namespace MBExtreme.Core.Tests;

public class RotaJogoTests
{
    private static HopSummary H(int hop, string addr, double avg, double loss = 0, double jitter = 1, int got = 10) => new(hop, addr, avg, loss, jitter, got);

    [Theory]
    [InlineData(20, 2, 0, "Excelente")]
    [InlineData(45, 4, 0.2, "Bom")]
    [InlineData(110, 4, 0, "Regular")]
    [InlineData(40, 12, 0, "Regular")]
    [InlineData(40, 4, 1.5, "Regular")]
    [InlineData(40, 30, 0, "Ruim")]
    [InlineData(40, 4, 4, "Ruim")]
    [InlineData(200, 4, 0, "Ruim")]
    public void Veredito(double media, double jitter, double perda, string esperado) => Assert.Equal(esperado, RouteAnalyzer.Verdict(media, jitter, perda));

    [Fact]
    public void Analise_PerdaEOscilacaoAltas()
    {
        var f = RouteAnalyzer.Analyze(new[] { H(1, "192.168.0.1", 1), H(2, "10.0.0.1", 30) }, 30, 4, 30);
        Assert.Contains(f, x => x.Level == FindingLevel.Bad && x.Title.Contains("Perda"));
        Assert.Contains(f, x => x.Level == FindingLevel.Bad && x.Title.Contains("Oscilação"));
    }

    [Fact]
    public void Analise_RoteadorLento_ApontaRedeLocal()
    {
        var f = RouteAnalyzer.Analyze(new[] { H(1, "192.168.0.1", 22), H(2, "x", 40) }, 40, 0, 2);
        Assert.Contains(f, x => x.Title.Contains("rede local"));
    }

    [Fact]
    public void Analise_SaltoGrandeQueSeMantem_EProvavelRotaLonga()
    {
        var hops = new[] { H(1, "192.168.0.1", 1), H(2, "200.1.1.1", 8), H(3, "200.1.1.2", 95), H(4, "200.1.1.3", 98) };
        var f = RouteAnalyzer.Analyze(hops, 98, 0, 2);
        var jump = Assert.Single(f, x => x.Title.StartsWith("Salto de"));
        Assert.Equal(FindingLevel.Bad, jump.Level);
        Assert.Contains("200.1.1.2", jump.Detail);
    }

    [Fact]
    public void Analise_SaltoQueNaoSeMantem_EIgnorado()
    {
        // um roteador do meio demora a responder ao ping mas o destino é rápido
        var hops = new[] { H(1, "192.168.0.1", 1), H(2, "200.1.1.1", 90), H(3, "200.1.1.9", 15) };
        Assert.DoesNotContain(RouteAnalyzer.Analyze(hops, 15, 0, 1), x => x.Title.StartsWith("Salto de"));
    }

    [Fact]
    public void Analise_SaltoComPerdaMasDestinoSemPerda_ENormal()
    {
        var hops = new[] { H(1, "192.168.0.1", 1), H(2, "200.1.1.1", 10, loss: 60), H(3, "200.1.1.9", 15) };
        var f = RouteAnalyzer.Analyze(hops, 15, 0, 1);
        Assert.Contains(f, x => x.Level == FindingLevel.Info && x.Title.Contains("não responde"));
    }

    [Fact]
    public void Analise_TudoBem_DizNadaDeErrado()
    {
        var f = RouteAnalyzer.Analyze(new[] { H(1, "192.168.0.1", 1), H(2, "x", 20) }, 20, 0, 1);
        Assert.Single(f);
        Assert.Contains("Nada de errado", f[0].Title);
    }

    [Theory]
    [InlineData(2, "A")]
    [InlineData(4.9, "A")]
    [InlineData(10, "B")]
    [InlineData(45, "C")]
    [InlineData(100, "D")]
    [InlineData(250, "F")]
    public void Bufferbloat_Nota(double aumento, string nota) => Assert.Equal(nota, Bufferbloat.Grade(aumento));

    [Fact]
    public void Bufferbloat_UsaOPiorDownloadOuUpload()
    {
        var r = Bufferbloat.Evaluate(20, 25, 160);
        Assert.Equal("D", r.Grade);
        Assert.Contains("roteador", r.Advice);
        Assert.Equal("A", Bufferbloat.Evaluate(20, null, null).Grade);
    }

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("200.150.10.4", true)]
    [InlineData("192.168.0.10", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.20.0.1", false)]
    [InlineData("172.32.0.1", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("100.64.1.1", false)]
    [InlineData("224.0.0.1", false)]
    public void EnderecoPublico(string ip, bool publico) => Assert.Equal(publico, GameEndpoints.IsPublic(IPAddress.Parse(ip)));

    [Fact]
    public void EscolheOsServidoresMaisUsados_SemRedeLocal()
    {
        var all = new[]
        {
            new RemoteEndpoint(IPAddress.Parse("13.1.1.1"), 443, "TCP", 1),
            new RemoteEndpoint(IPAddress.Parse("13.1.1.1"), 7777, "UDP", 5000),
            new RemoteEndpoint(IPAddress.Parse("52.2.2.2"), 7777, "UDP", 200),
            new RemoteEndpoint(IPAddress.Parse("192.168.0.20"), 7777, "UDP", 99999),
            new RemoteEndpoint(IPAddress.Parse("34.3.3.3"), 27015, "UDP", 10),
        };
        var top = GameEndpoints.Top(all, 2);
        Assert.Equal(new[] { "13.1.1.1", "52.2.2.2" }, top.Select(t => t.Address.ToString()));
        Assert.Equal(7777, top[0].Port); // a conexão mais usada daquele servidor
    }

    [Fact]
    public void TabelaTcp_LeLinhasDoWindows()
    {
        // 1 linha: estado 5 (estabelecida), local 192.168.0.5:49152, remoto 13.89.178.27:443, pid 4242
        var buf = new byte[4 + 24];
        BitConverter.GetBytes(1).CopyTo(buf, 0);
        BitConverter.GetBytes(5u).CopyTo(buf, 4);
        new byte[] { 192, 168, 0, 5 }.CopyTo(buf, 8);
        new byte[] { 0xC0, 0x00 }.CopyTo(buf, 12);          // 49152 em ordem de rede
        new byte[] { 13, 89, 178, 27 }.CopyTo(buf, 16);
        new byte[] { 0x01, 0xBB }.CopyTo(buf, 20);          // 443
        BitConverter.GetBytes(4242).CopyTo(buf, 24);
        var rows = GameConnections.ParseTcpTable(buf);
        var r = Assert.Single(rows);
        Assert.Equal(5u, r.State);
        Assert.Equal("13.89.178.27", r.Remote.ToString());
        Assert.Equal(443, r.RemotePort);
        Assert.Equal(49152, r.LocalPort);
        Assert.Equal(4242, r.Pid);
    }

    [Fact]
    public void TabelaTcp_BufferCurtoOuVazio_NaoQuebra()
    {
        Assert.Empty(GameConnections.ParseTcpTable(Array.Empty<byte>()));
        var buf = new byte[8];
        BitConverter.GetBytes(5).CopyTo(buf, 0); // diz ter 5 linhas mas não cabe nenhuma
        Assert.Empty(GameConnections.ParseTcpTable(buf));
    }

    [Fact]
    public void UdpEventos_DecodificaEnderecoEPortaEmOrdemDeRede()
    {
        // 13.89.178.27 guardado como bytes 13,89,178,27 lidos como inteiro; porta 7777 = 0x1E61 -> bytes trocados 0x611E
        uint raw = BitConverter.ToUInt32(new byte[] { 13, 89, 178, 27 }, 0);
        var (ip, port) = UdpEndpointWatcher.Decode(raw, 0x611E);
        Assert.Equal("13.89.178.27", ip.ToString());
        Assert.Equal(7777, port);
    }

    private static NetReport R(bool opt, double avg, int day, string host = "13.1.1.1", double jitter = 2, double loss = 0) =>
        new() { At = new DateTime(2026, 10, day), Optimized = opt, AvgMs = avg, Host = host, JitterMs = jitter, LossPercent = loss };

    [Fact]
    public void ComparacaoDePing_AntesDepois()
    {
        var c = NetCompare.Build(new[] { R(false, 60, 1), R(true, 45, 2) });
        Assert.Contains("Ping menor", c.Message);
        Assert.Contains("60 → 45", c.Message);
        Assert.Empty(c.Warnings);
    }

    [Fact]
    public void ComparacaoDePing_ServidoresDiferentesEDiferencaPequena_Avisa()
    {
        var c = NetCompare.Build(new[] { R(false, 50, 1, "1.1.1.1"), R(true, 49, 2, "2.2.2.2") });
        Assert.Contains("Sem diferença relevante", c.Message);
        Assert.Equal(2, c.Warnings.Count);
    }

    [Fact]
    public void ComparacaoDePing_SemDados()
    {
        Assert.Contains("Analise a rota", NetCompare.Build(Array.Empty<NetReport>()).Message);
        Assert.Contains("Otimize", NetCompare.Build(new[] { R(false, 50, 1) }).Message);
    }

    [Fact]
    public void RelatoriosGuardamAs20Ultimas()
    {
        var store = new NetReportStore(Path.Combine(Path.GetTempPath(), $"mbx-net-{Guid.NewGuid():N}.json"));
        for (int i = 0; i < 25; i++) store.Add("g", R(i % 2 == 0, 50 + i, 1 + i % 28));
        Assert.Equal(20, store.Get("g").Count);
        store.Clear("g");
        Assert.Empty(store.Get("g"));
    }

    [Fact]
    public async Task Sonda_EnderecoLocal_MedePingEMonta()
    {
        // usa 127.0.0.1: funciona sem internet e sem ICMP externo; só confere que não quebra e produz um diagnóstico
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var d = await RouteProbe.RunAsync("127.0.0.1", 0, 3, 1, null, cts.Token);
        Assert.Equal("127.0.0.1", d.Host);
        Assert.Equal(3, d.Samples);
        Assert.NotEmpty(d.Findings);
    }
}
