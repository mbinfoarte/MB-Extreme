using MBExtreme.Core.Recording;
using Xunit;

namespace MBExtreme.Core.Tests;

public class OtimizarLiveTests
{
    private static LiveProbe Probe(double? up, double ping = 20, double jitter = 3, double loss = 0, bool ok = true, bool wifi = false, int tier = 3) =>
        new(up, ping, jitter, loss, ok, wifi, tier);

    [Fact]
    public void Envio_bom_e_conexao_limpa_permite_1080p60_dentro_do_limite_da_plataforma()
    {
        var a = LiveOptimizer.Recommend(Probe(40), StreamService.Twitch, 160);
        Assert.Equal(1080, a.Height);
        Assert.Equal(60, a.Fps);
        Assert.Equal(6, a.CapMbps);          // Twitch: máximo de 6 Mbps
        Assert.Equal(AdviceLevel.Good, a.Level);
    }

    [Fact]
    public void Envio_pequeno_baixa_a_resolucao_e_nunca_passa_do_envio()
    {
        var a = LiveOptimizer.Recommend(Probe(4), StreamService.Twitch, 160);
        Assert.True(a.CapMbps <= 4 * 0.7);
        Assert.True(a.Height <= 720);
        Assert.True(a.VideoMbps <= a.CapMbps);
    }

    [Fact]
    public void Perda_e_jitter_deixam_mais_folga_e_avisam()
    {
        var clean = LiveOptimizer.Recommend(Probe(20), StreamService.YouTube, 128);
        var bad = LiveOptimizer.Recommend(Probe(20, ping: 80, jitter: 35, loss: 4), StreamService.YouTube, 128);
        Assert.True(bad.CapMbps < clean.CapMbps);
        Assert.Equal(AdviceLevel.Bad, bad.Level);
        Assert.Contains(bad.Tips, t => t.Contains("perda de pacotes"));
        Assert.Contains(bad.Tips, t => t.Contains("oscilação"));
    }

    [Fact]
    public void Wifi_pede_cabo_e_limita_a_taxa()
    {
        var wired = LiveOptimizer.Recommend(Probe(20), StreamService.YouTube, 128);
        var wifi = LiveOptimizer.Recommend(Probe(20, wifi: true), StreamService.YouTube, 128);
        Assert.True(wifi.CapMbps < wired.CapMbps);
        Assert.Contains(wifi.Tips, t => t.Contains("cabo"));
    }

    [Fact]
    public void Pc_fraco_fica_em_720p_e_sem_medida_usa_o_conjunto_seguro()
    {
        var weak = LiveOptimizer.Recommend(Probe(50, tier: 1), StreamService.YouTube, 128);
        Assert.True(weak.Height <= 720);
        var vweak = LiveOptimizer.Recommend(Probe(50, tier: 0), StreamService.YouTube, 128);
        Assert.Equal(30, vweak.Fps);

        var none = LiveOptimizer.Recommend(Probe(null), StreamService.Twitch, 160);
        Assert.Equal((720, 30, 3), (none.Height, none.Fps, none.CapMbps));
        Assert.Equal(AdviceLevel.Warning, none.Level);
    }

    [Fact]
    public void Servidor_que_nao_responde_ao_ping_e_explicado_sem_castigar_a_taxa()
    {
        var a = LiveOptimizer.Recommend(Probe(20, ok: false), StreamService.Facebook, 128);
        Assert.Contains(a.Reasons, r => r.Contains("não respondeu"));
        Assert.Equal(AdviceLevel.Good, a.Level);
    }
}
