using MBExtreme.Core.Licensing;
using MBExtreme.Core.Social;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>Teste grátis de 7 dias do Pro e a liberação das telas durante o teste.</summary>
public class TesteGratisTests
{
    private static readonly DateTime Start = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Sete_dias_contados_do_primeiro_uso()
    {
        var day1 = TrialClock.Evaluate(Start, Start, Start.AddHours(2), 7);
        Assert.True(day1.Active);
        Assert.Equal("faltam 7 dias", day1.LeftText);
        var day7 = TrialClock.Evaluate(Start, Start, Start.AddDays(6.5), 7);
        Assert.Equal("último dia", day7.LeftText);
        var after = TrialClock.Evaluate(Start, Start, Start.AddDays(7.01), 7);
        Assert.True(after.Expired);
        Assert.Equal("acabou", after.LeftText);
    }

    [Fact]
    public void Atrasar_o_relogio_nao_devolve_dias()
    {
        var seen = Start.AddDays(8);
        var cheated = TrialClock.Evaluate(Start, seen, Start.AddDays(1), 7);   // relógio voltou para o dia 2
        Assert.True(cheated.Expired);
    }

    [Fact]
    public void Apagar_uma_das_copias_nao_reinicia_o_teste()
    {
        var now = Start.AddDays(5);
        Assert.Equal(Start, TrialClock.EarliestStart(null, Start, now));            // configurações apagadas
        Assert.Equal(Start, TrialClock.EarliestStart(Start, null, now));            // registro apagado
        Assert.Equal(Start, TrialClock.EarliestStart(Start.AddDays(3), Start, now));
        Assert.Equal(now, TrialClock.EarliestStart(null, null, now));               // primeira vez
        Assert.Equal(now, TrialClock.EarliestStart(now.AddYears(1), null, now));    // data no futuro (forjada) é ignorada
    }

    [Fact]
    public void Durante_o_teste_as_telas_Pro_abrem()
    {
        var none = new LicenseStatus(LicenseState.None, null, "", null);
        Assert.False(FeaturePolicy.IsAllowed("bench", none, enforce: true, trialActive: false));
        Assert.True(FeaturePolicy.IsAllowed("bench", none, enforce: true, trialActive: true));
        Assert.True(FeaturePolicy.IsAllowed("cleanup", none, enforce: true, trialActive: false));   // grátis sempre
        Assert.True(FeaturePolicy.IsAllowed("bench", none, enforce: false));                        // beta aberto
        Assert.False(LicenseExpiry.ShouldRevert(true, true, none, trialActive: true));               // no teste não desfaz nada
        Assert.True(LicenseExpiry.ShouldRevert(true, true, none, trialActive: false));
    }

    [Fact]
    public void Icones_das_redes_validos()
    {
        foreach (var key in new[] { "youtube", "instagram", "tiktok", "facebook", "threads", "whatsapp", "telegram" })
        {
            Assert.True(SocialIcons.Known.TryGetValue(key, out var icon), key);
            Assert.Matches("^#[0-9A-Fa-f]{6}$", icon.Color);
            Assert.True(icon.Path[0] is 'M' or 'm');
        }
        Assert.All(ChannelConfig.Defaults().Links, l => Assert.True(SocialIcons.Known.ContainsKey(l.Icon), l.Icon));
    }
}
