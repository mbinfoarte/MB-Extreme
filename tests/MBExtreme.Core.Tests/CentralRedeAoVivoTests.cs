using MBExtreme.Core.NetDiag;
using MBExtreme.Core.Network;
using MBExtreme.UI;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>Painel ao vivo da Central de Rede: ping contínuo, eventos, mapa da conexão, conexões por programa e o menu novo.</summary>
public class CentralRedeAoVivoTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 26, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Serie_de_ping_calcula_mediana_variacao_perda_e_picos()
    {
        var s = new PingSeries(10);
        foreach (double? v in new double?[] { 10, 12, 11, null, 10, 45, 11 }) s.Add(v);
        Assert.Equal(11, s.Median);
        Assert.Equal(14.3, s.LossPct);
        Assert.Equal(new[] { 5 }, s.Spikes());
        Assert.NotNull(s.Jitter);
    }

    [Fact]
    public void Serie_guarda_so_os_mais_recentes()
    {
        var s = new PingSeries(3);
        for (int i = 1; i <= 5; i++) s.Add(i);
        Assert.Equal(new double?[] { 3, 4, 5 }, s.Samples);
    }

    [Fact]
    public void Pico_precisa_de_2_5_vezes_e_25_ms_acima()
    {
        Assert.False(PingSeries.IsSpike(30, 10));   // 3x, mas só 20 ms acima
        Assert.True(PingSeries.IsSpike(40, 10));
        Assert.False(PingSeries.IsSpike(120, 60));  // 2x
        Assert.False(PingSeries.IsSpike(99, null));
    }

    [Fact]
    public void Queda_avisa_uma_vez_e_a_volta_tambem()
    {
        var w = new PingWatch("1.1.1.1");
        Assert.Null(w.Feed(T0, null, 10));
        Assert.Null(w.Feed(T0, null, 10));
        var down = w.Feed(T0, null, 10);
        Assert.Equal(FindingSeverity.Problema, down!.Level);
        Assert.Null(w.Feed(T0, null, 10));   // não repete
        var up = w.Feed(T0, 12, 10);
        Assert.Contains("voltou", up!.Text);
        Assert.Equal(FindingSeverity.Atencao, w.Feed(T0, 60, 10)!.Level);
        Assert.Null(w.Feed(T0, 11, 10));
    }

    [Fact]
    public void Registro_de_eventos_mostra_o_mais_novo_primeiro_e_tem_limite()
    {
        var log = new NetEventLog(2);
        log.Add(T0, "a"); log.Add(T0, "b"); log.Add(T0, "c");
        Assert.Equal(new[] { "c", "b" }, log.Items.Select(e => e.Text));
    }

    private static RttStats Rtt(double median, int lost = 0) =>
        RttStats.From(10, Enumerable.Repeat(median, 10 - lost).ToList());

    private static NetDiagReport Report(Action<NetDiagEvidence> fill)
    {
        var e = new NetDiagEvidence
        {
            Primary = new NetIface("Ethernet", "Placa", "Ethernet", true, "AA", 1_000_000_000, new[] { "192.168.0.10" }, new[] { 24 }, Array.Empty<string>(),
                new[] { "192.168.0.1" }, new[] { "1.1.1.1" }, true, "192.168.0.1", 1500, false),
            Loopback = Rtt(0.2), GatewayAddress = "192.168.0.1", Gateway = Rtt(1),
        };
        e.External["1.1.1.1"] = Rtt(11);
        e.External["8.8.8.8"] = Rtt(14);
        fill(e);
        return new NetDiagReport(1, T0, T0, "pc", e, Array.Empty<Finding>(), Array.Empty<string>());
    }

    [Fact]
    public void Mapa_sem_diagnostico_nao_inventa_nada()
    {
        var m = ConnectionMapRules.Build(null);
        Assert.False(m.Pc.Tested);
        Assert.False(m.Router.Tested);
        Assert.False(m.Isp.Tested);
        Assert.False(m.Internet.Tested);
        Assert.Empty(m.Targets);
        Assert.Empty(m.Devices);
    }

    [Fact]
    public void Perda_so_no_salto_do_provedor_com_destino_ok_e_aviso_e_marca_o_salto()
    {
        var r = Report(e =>
        {
            e.Hops.Add(new HopResult(1, "192.168.0.1", Rtt(1)));
            e.Hops.Add(new HopResult(2, "100.64.0.1", Rtt(6)));
            e.Hops.Add(new HopResult(3, "200.1.1.1", Rtt(7)));
            e.Hops.Add(new HopResult(4, "200.1.1.2", Rtt(8, lost: 1)));
            e.Hops.Add(new HopResult(5, "1.1.1.1", Rtt(12)));
        });
        var m = ConnectionMapRules.Build(r);
        Assert.Equal(FindingSeverity.Atencao, m.Isp.Level);
        Assert.Equal(4, m.IspHop);
        Assert.Contains("perda 10%", m.Isp.Value);
        Assert.Equal("Cloudflare", m.Targets[0].Name);
        Assert.Equal(FindingSeverity.Info, m.Router.Level);
    }

    [Fact]
    public void Aparelhos_com_conflito_vem_primeiro_e_roteador_e_este_pc_ficam_de_fora()
    {
        var lan = new[]
        {
            new LanDevice("192.168.0.1", "R", "TP-Link", "Roteador", 1, true, false),
            new LanDevice("192.168.0.10", "P", "", "Este PC", 0, false, true),
            new LanDevice("192.168.0.40", "A", "HP", "Impressora", 2, false, false),
            new LanDevice("192.168.0.23", "B", "Samsung", "Smart TV", 3, false, false),
        };
        var conflicts = new[] { new IpConflict("192.168.0.23", new[] { "B", "C" }, 0.9, "ARP + DHCP", "") };
        var m = ConnectionMapRules.Build(Report(_ => { }), lan, conflicts);
        Assert.Equal(2, m.Devices.Count);
        Assert.True(m.Devices[0].Conflict);
        Assert.Equal("Smart TV", m.Devices[0].Name);
    }

    [Fact]
    public void Conexoes_por_programa_contam_so_as_abertas()
    {
        var rows = new (uint, int)[] { (5, 100), (5, 100), (5, 200), (2, 200), (5, 4), (5, 300) };
        var names = new Dictionary<int, string?> { [100] = "steam", [200] = "chrome", [300] = null };
        var top = TrafficRules.ByProgram(rows, pid => names.GetValueOrDefault(pid));
        Assert.Equal(new[] { ("steam", 2), ("chrome", 1) }, top);
    }

    [Theory]
    [InlineData(5_250_000, "42 Mb/s")]
    [InlineData(1_000, "8 kb/s")]
    [InlineData(10, "80 b/s")]
    public void Velocidade_em_bits(double bytes, string text) => Assert.Equal(text, TrafficRules.Rate(bytes));

    [Fact]
    public void Menu_tem_os_modulos_novos_e_otimizar_ficou_enxuto()
    {
        var side = NavigationMap.Sidebar[0].Items.Select(i => i.Key).ToList();
        foreach (string k in new[] { "hub-disks", "hub-drivers", "hub-memoria", "netcenter" }) Assert.Contains(k, side);
        var titles = NavigationMap.Sidebar[0].Items.Skip(1).Select(i => i.Title).ToList();
        Assert.Equal(titles.OrderBy(t => t, StringComparer.Create(new System.Globalization.CultureInfo("pt-BR"), true)).ToList(), titles);
        Assert.Equal(new[] { "hub-startup", "hub-clean", "tweaks-all", "repair" }, NavigationMap.Hubs["hub-otimizar"].Select(t => t.Key));
        Assert.Equal(new[] { "hub-disks" }, NavigationMap.HubPath("diskmgr"));
        Assert.Equal(new[] { "hub-drivers" }, NavigationMap.HubPath("bios-tune"));
        Assert.Equal(new[] { "hub-memoria" }, NavigationMap.HubPath("memory"));
    }

    [Theory]
    [InlineData("dns")]
    [InlineData("net-tweaks")]
    [InlineData("netanalyzer")]
    [InlineData("speedtest")]
    [InlineData("wifi")]
    public void Ferramentas_de_rede_abrem_na_Central_de_Rede(string key)
    {
        Assert.Equal("netcenter", NavigationMap.SideKeyOf(key));
        Assert.Null(NavigationMap.HubOf(key));
    }
}
