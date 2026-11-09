using MBExtreme.Core.Apps;
using MBExtreme.Core.Tweaks;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>Fase 5: ajustes novos (com fonte oficial), compatibilidade por versão, grupos de seleção, pós-formatação e erros do winget.</summary>
[Collection("Limpeza")]   // troca o Tweak.WindowsBuild (estático)
public class OtimizacoesFase5Tests
{
    private sealed class FakeTweak(string id, TweakRisk risk)
        : Tweak(new TweakStore(Path.Combine(Path.GetTempPath(), $"mbx-f5-{Guid.NewGuid():N}.json")), id, TweakCategory.Privacy, risk, id, "d")
    {
        public override bool? IsApplied() => false;
        public override void Apply() { }
        public override void Revert() { }

        public static FakeTweak Make(string id, TweakRisk risk, bool recommended = false, int min = 0, int max = 0) =>
            new(id, risk) { Recommended = recommended, MinBuild = min, MaxBuild = max };
    }

    [Fact]
    public void Ajuste_so_aparece_na_versao_do_windows_em_que_existe()
    {
        var before = Tweak.WindowsBuild;
        try
        {
            var recall = FakeTweak.Make("recall", TweakRisk.Low, min: 26100);
            var win10 = FakeTweak.Make("barra", TweakRisk.Low, max: 21999);
            Tweak.WindowsBuild = () => 19045;   // Windows 10 22H2
            Assert.False(recall.IsAvailable);
            Assert.True(win10.IsAvailable);
            Tweak.WindowsBuild = () => 26100;   // Windows 11 24H2
            Assert.True(recall.IsAvailable);
            Assert.False(win10.IsAvailable);
            Assert.Equal("Só no Windows 11 24H2 ou mais novo.", recall.CompatibilityText);
            Assert.Equal("Só no Windows 10.", win10.CompatibilityText);
        }
        finally { Tweak.WindowsBuild = before; }
    }

    [Fact]
    public void Catalogo_tem_os_ajustes_do_plano_com_a_versao_certa()
    {
        var all = TweakCatalog.Create(new TweakStore(Path.Combine(Path.GetTempPath(), $"mbx-cat-{Guid.NewGuid():N}.json"))).ToDictionary(t => t.Id);
        Assert.Equal(26100, all["priv-recall"].MinBuild);
        Assert.Equal(22000, all["priv-widgets"].MinBuild);
        Assert.Equal(21999, all["priv-news-interests"].MaxBuild);
        Assert.Equal(21999, all["visual-taskbar-small"].MaxBuild);
        Assert.Equal(22621, all["priv-start-promos"].MinBuild);
        foreach (string id in new[] { "priv-copilot", "priv-lockscreen-tips", "priv-explorer-ads", "priv-ads" }) Assert.True(all.ContainsKey(id), id);
        Assert.All(all.Values.Where(t => t.Risk == TweakRisk.High), t => Assert.False(t.Recommended));   // risco alto nunca é recomendado
        Assert.Equal(all.Count, all.Values.Select(t => t.Id).Distinct().Count());
    }

    [Fact]
    public void Grupos_de_selecao_por_risco()
    {
        var tweaks = new Tweak[]
        {
            FakeTweak.Make("a", TweakRisk.Low, recommended: true),
            FakeTweak.Make("b", TweakRisk.Low),
            FakeTweak.Make("c", TweakRisk.Medium),
            FakeTweak.Make("d", TweakRisk.High),
        };
        Assert.Equal(new[] { "a" }, TweakSelection.Select(tweaks, TweakGroup.Recommended).Select(t => t.Id));
        Assert.Equal(new[] { "a", "b" }, TweakSelection.Select(tweaks, TweakGroup.Safe).Select(t => t.Id));
        Assert.Equal(new[] { "c" }, TweakSelection.Select(tweaks, TweakGroup.Moderate).Select(t => t.Id));
        Assert.Equal(new[] { "d" }, TweakSelection.Select(tweaks, TweakGroup.Advanced).Select(t => t.Id));
        Assert.Equal(4, TweakSelection.Select(tweaks, TweakGroup.All).Count);
    }

    [Fact]
    public void Perfis_da_pos_formatacao_nunca_marcam_risco_alto_nem_app_perigoso()
    {
        var tweaks = new Tweak[] { FakeTweak.Make("rec", TweakRisk.Low, true), FakeTweak.Make("low", TweakRisk.Low), FakeTweak.Make("med", TweakRisk.Medium), FakeTweak.Make("high", TweakRisk.High) };
        Assert.Equal(new[] { "rec" }, PostInstallPlan.Tweaks(PostInstallProfile.Light, tweaks).Select(t => t.Id));
        Assert.Equal(new[] { "rec", "low" }, PostInstallPlan.Tweaks(PostInstallProfile.Balanced, tweaks).Select(t => t.Id));
        Assert.Equal(new[] { "rec", "low", "med" }, PostInstallPlan.Tweaks(PostInstallProfile.Extreme, tweaks).Select(t => t.Id));

        var apps = BloatCatalog.All;
        Assert.Empty(PostInstallPlan.Apps(PostInstallProfile.Light, apps));
        Assert.All(PostInstallPlan.Apps(PostInstallProfile.Balanced, apps), a => Assert.Equal(AppRemovalLevel.Safe, a.Level));
        Assert.DoesNotContain(PostInstallPlan.Apps(PostInstallProfile.Extreme, apps), a => a.Level == AppRemovalLevel.Dangerous);
        Assert.Contains(PostInstallPlan.Apps(PostInstallProfile.Extreme, apps), a => a.Level == AppRemovalLevel.Caution);
    }

    [Theory]
    [InlineData(0, true, "Atualizado.")]
    [InlineData(unchecked((int)0x8A15002B), true, "Já está na versão mais nova.")]
    [InlineData(unchecked((int)0x8A150101), false, "O programa está aberto. Feche-o e tente de novo.")]
    [InlineData(unchecked((int)0x8A150105), false, "Disco cheio: libere espaço e tente de novo.")]
    [InlineData(unchecked((int)0x8A150107), false, "Sem internet: confira a conexão.")]
    public void Codigos_do_winget_em_portugues(int code, bool ok, string message)
    {
        var (resultOk, text, _) = WingetErrors.Explain(code, upgrade: true);
        Assert.Equal(ok, resultOk);
        Assert.Equal(message, text);
    }

    [Fact]
    public void Codigo_desconhecido_e_reinicio_do_winget()
    {
        Assert.True(WingetErrors.Explain(unchecked((int)0x8A150109), false).Reboot);
        Assert.Contains("0x8A1500FF", WingetErrors.Explain(unchecked((int)0x8A1500FF), false).Message);
    }
}
