using Xunit;
using MBExtreme.Core.Apps;
using MBExtreme.Core.Cleanup;
using MBExtreme.Core.Repair;
using MBExtreme.Core.Tweaks;

namespace MBExtreme.Core.Tests;

/// <summary>Recursos inspirados na comparação com o Hone: filtros por objetivo, selos, novos ajustes, limpezas e reparos.</summary>
public class RecursosHoneTests
{
    private sealed class T : Tweak
    {
        public T(string id, TweakCategory category, string title, string description = "d")
            : base(new TweakStore(Path.Combine(Path.GetTempPath(), $"mbx-h-{Guid.NewGuid():N}.json")), id, category, TweakRisk.Low, title, description) { }
        public override bool? IsApplied() => false;
        public override void Apply() { }
        public override void Revert() { }
    }

    [Fact]
    public void Objetivo_padrao_vem_da_categoria_e_pode_ser_trocado()
    {
        Assert.Equal(TweakGoal.FpsLatency, new T("a", TweakCategory.Gaming, "x").Goals);
        Assert.Equal(TweakGoal.Network, new T("b", TweakCategory.Network, "x").Goals);
        Assert.Equal(TweakGoal.Privacy, new T("c", TweakCategory.Privacy, "x").Goals);
        Assert.Equal(TweakGoal.QualityOfLife, new T("d", TweakCategory.Visual, "x").Goals);
        Assert.Equal(TweakGoal.QualityOfLife, new T("e", TweakCategory.Boot, "x").Goals);
        var both = new T("f", TweakCategory.Visual, "x") { Goals = TweakGoal.QualityOfLife | TweakGoal.Privacy };
        Assert.True(TweakFilter.Matches(both, TweakGoal.Privacy, null));
        Assert.True(TweakFilter.Matches(both, TweakGoal.QualityOfLife, null));
        Assert.False(TweakFilter.Matches(both, TweakGoal.Network, null));
    }

    [Fact]
    public void Busca_ignora_acentos_maiusculas_e_exige_todas_as_palavras()
    {
        var t = new T("g", TweakCategory.Visual, "Menu clássico do botão direito", "Volta o menu completo");
        Assert.True(TweakFilter.Matches(t, TweakGoal.None, "classico"));
        Assert.True(TweakFilter.Matches(t, TweakGoal.None, "BOTAO menu"));
        Assert.False(TweakFilter.Matches(t, TweakGoal.None, "menu rede"));
        Assert.True(TweakFilter.Matches(t, TweakGoal.None, "   "));
    }

    [Fact]
    public void Contador_de_ativas()
    {
        Assert.Equal("3 de 10 ativas", TweakFilter.Counter(3, 10));
        Assert.Equal("0 de 1 ativa", TweakFilter.Counter(0, 1));
        Assert.Equal(5, TweakFilter.Goals.Count);
    }

    [Fact]
    public void Novos_ajustes_estao_no_catalogo_com_selo_e_regras_de_risco()
    {
        if (!OperatingSystem.IsWindows()) return;
        var all = TweakCatalog.Create(new TweakStore(Path.Combine(Path.GetTempPath(), $"mbx-hc-{Guid.NewGuid():N}.json"))).ToDictionary(t => t.Id);
        string[] ids =
        {
            "qol-classic-context-menu", "qol-shortcut-suffix", "qol-explorer-compact", "qol-explorer-thispc", "qol-show-extensions",
            "visual-jpeg-quality", "visual-lockscreen-blur", "qol-lockscreen-off", "qol-notifications-off", "qol-storage-sense-off",
            "qol-insider-hide", "qol-tray-icons", "qol-photo-viewer", "boot-verbose-status", "boot-menu-timeout",
            "game-nvidia-dlss-indicator", "net-nic-buffers", "net-lso-off", "game-msi-mode",
        };
        foreach (string id in ids)
        {
            Assert.True(all.ContainsKey(id), id);
            Assert.Equal("Novo", all[id].Badge);
            Assert.False(string.IsNullOrWhiteSpace(all[id].Effect), id);
        }
        Assert.Equal(22000, all["qol-classic-context-menu"].MinBuild);
        Assert.Equal(21999, all["qol-tray-icons"].MaxBuild);
        Assert.NotNull(all["game-nvidia-dlss-indicator"].AppliesTo);
        foreach (string id in new[] { "net-nic-buffers", "net-lso-off", "game-msi-mode" })
        {
            Assert.Equal(TweakRisk.Medium, all[id].Risk);
            Assert.False(all[id].Recommended);
            Assert.True(all[id].RequiresRestart);
        }
    }

    [Fact]
    public void Limpezas_novas_nunca_tocam_em_jogos_saves_ou_mensagens()
    {
        string roaming = Path.Combine(Path.GetTempPath(), $"mbx-roam-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(roaming, "discord"));
        var discord = CleanupCatalog.DiscordCaches(roaming);
        Assert.Equal(3, discord.Count);
        Assert.All(discord, p => Assert.Contains(Path.GetFileName(p), new[] { "Cache", "Code Cache", "GPUCache" }));

        string steam = Path.Combine(Path.GetTempPath(), $"mbx-steam-{Guid.NewGuid():N}");
        Directory.CreateDirectory(steam);
        var steamRoots = CleanupCatalog.SteamCaches(Path.Combine(roaming, "local"), steam);
        Assert.Contains(Path.Combine(steam, "logs"), steamRoots);
        Assert.Contains(Path.Combine(steam, "appcache", "httpcache"), steamRoots);
        Assert.DoesNotContain(steamRoots, p => p.Contains("steamapps") || p.Contains("userdata"));

        var all = CleanupCatalog.Create();
        Assert.False(all.First(c => c.Id == "steam-cache").DefaultSelected);
        Assert.False(all.First(c => c.Id == "recent-docs").DefaultSelected);
        Assert.Contains(all, c => c.Id == "discord-cache");
        Assert.Equal(all.Count, all.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public void Reparo_do_cache_de_fontes_e_programas_sysinternals()
    {
        var fonts = Assert.Single(RepairCatalog.Create("C:", "pt"), t => t.Id == "fonts");
        Assert.Contains(fonts.Steps, s => s.Arguments.Contains("FontCache"));
        Assert.Contains(fonts.Steps, s => s.AlwaysRun && s.Arguments.Contains("start FontCache"));
        Assert.DoesNotContain(fonts.Steps, s => s.Arguments.Contains("\\\""));   // o script não pode quebrar as aspas do -Command

        var ids = AppCatalog.InKit("tecnico").Select(a => a.Id).ToList();
        Assert.Contains("Microsoft.Sysinternals.ProcessExplorer", ids);
        Assert.Contains("Microsoft.Sysinternals.Autoruns", ids);
    }
}
