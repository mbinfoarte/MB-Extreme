using MBExtreme.Core.Games;
using MBExtreme.Core.Tweaks;
using Xunit;

namespace MBExtreme.Core.Tests;

public class OtimizacaoPorJogoTests
{
    private sealed class FakeTweak : Tweak
    {
        public bool On;
        public int Applies, Reverts;
        public FakeTweak(string id, TweakRisk risk = TweakRisk.Low, bool on = false) : base(new TweakStore(Path.Combine(Path.GetTempPath(), $"mbx-f-{Guid.NewGuid():N}.json")), id, TweakCategory.Gaming, risk, "Título " + id, "d") => On = on;
        public override bool? IsApplied() => On;
        public override void Apply() { On = true; Applies++; }
        public override void Revert() { On = false; Reverts++; }
    }

    private static (GameOptimizationService Service, GameLibraryStore Store, Dictionary<string, FakeTweak> Tweaks) Setup(params FakeTweak[] tweaks)
    {
        var store = new GameLibraryStore(Path.Combine(Path.GetTempPath(), $"mbx-g-{Guid.NewGuid():N}.json"));
        return (new GameOptimizationService(tweaks, store), store, tweaks.ToDictionary(t => t.Id));
    }

    [Theory]
    [InlineData("cs2.exe", null, null, "competitive")]
    [InlineData("obs64.exe", null, null, "app")]
    [InlineData("jogo.exe", new[] { "Ação", "Aventura", "RPG" }, new[] { "Jogador único" }, "openworld")]
    [InlineData("jogo.exe", new[] { "Corrida", "Simulação" }, new string[0], "racing")]
    [InlineData("jogo.exe", new[] { "Indie", "Casual" }, new string[0], "light")]
    [InlineData("jogo.exe", new[] { "Ação", "Gratuito para jogar" }, new[] { "Multijogador", "PvP online" }, "competitive")]
    [InlineData("jogo.exe", new[] { "Action", "Sports" }, new[] { "Multi-player" }, "competitive")]
    [InlineData("x.exe", null, null, "openworld")]
    public void ClassificaPorExecutavelEGenero(string exe, string[]? genres, string[]? categories, string expected) =>
        Assert.Equal(expected, GameProfiles.Classify(exe, genres, categories).Key);

    [Fact]
    public void TodosOsPerfisSoUsamAjustesQueExistemNoCatalogo()
    {
        if (!OperatingSystem.IsWindows()) return;
        var ids = TweakCatalog.Create(new TweakStore(Path.Combine(Path.GetTempPath(), $"mbx-c-{Guid.NewGuid():N}.json"))).ToDictionary(t => t.Id);
        foreach (var p in GameProfiles.All)
            foreach (string id in p.TweakIds.Concat(new[] { "game-amd-telemetry-off" }))
            {
                Assert.True(ids.ContainsKey(id), $"{p.Key}: {id} não existe");
                Assert.Equal(TweakRisk.Low, ids[id].Risk);
            }
    }

    [Fact]
    public void PlanoTrazDicasDaMarcaEDoNivel()
    {
        var nvidia = GameOptimizer.Plan(GameProfiles.Competitive, GpuMaker.Nvidia, 3, false);
        Assert.Contains(nvidia.Advice, a => a.Contains("Reflex"));
        Assert.Contains(nvidia.Advice, a => a.Contains("1440p"));
        var amd = GameOptimizer.Plan(GameProfiles.OpenWorld, GpuMaker.Amd, 1, true);
        Assert.Contains(amd.Advice, a => a.Contains("FSR"));
        Assert.Contains("game-amd-telemetry-off", amd.TweakIds);
        Assert.Contains(amd.Advice, a => a.Contains("HD"));
        Assert.Contains(GameOptimizer.Plan(GameProfiles.Light, GpuMaker.Intel, 0, false).Advice, a => a.Contains("XeSS"));
        Assert.DoesNotContain("game-amd-telemetry-off", nvidia.TweakIds);
    }

    [Fact]
    public void AplicaSoOQueFaltaESoLigaRiscoBaixo()
    {
        var (service, store, t) = Setup(new FakeTweak("a"), new FakeTweak("b", on: true), new FakeTweak("c", TweakRisk.Medium));
        var plan = new OptimizationPlan(GameProfiles.Light, new[] { "a", "b", "c", "inexistente" }, Array.Empty<string>());
        var result = service.Apply("steam:1", "g.exe", plan);

        Assert.Equal(new[] { "Título a" }, result.Applied);
        Assert.Equal(new[] { "Título b" }, result.AlreadyOn);
        Assert.Equal(2, result.Skipped.Count);
        Assert.True(t["a"].On);
        Assert.False(t["c"].On);
        Assert.Equal(new[] { "a" }, store.Get("steam:1").AppliedTweaks);
        Assert.Equal("light", store.Get("steam:1").ProfileKey);
    }

    [Fact]
    public void DesfazerRevertSoOQueEleLigouEPoupaOQueOutroJogoUsa()
    {
        var (service, store, t) = Setup(new FakeTweak("a"), new FakeTweak("b", on: true), new FakeTweak("c"));
        service.Apply("g1", null, new OptimizationPlan(GameProfiles.Light, new[] { "a", "b", "c" }, Array.Empty<string>()));
        service.Apply("g2", null, new OptimizationPlan(GameProfiles.Light, new[] { "c" }, Array.Empty<string>()));   // c já estava ligado pelo g1

        // o g2 não ligou nada (c já estava ligado), então desfazê-lo não mexe em c
        service.Undo("g2");
        Assert.True(t["c"].On);

        var undo = service.Undo("g1");
        Assert.False(t["a"].On);
        Assert.False(t["c"].On);
        Assert.True(t["b"].On);                 // já estava ligado antes: continua
        Assert.Equal(0, t["b"].Reverts);
        Assert.Equal(2, undo.Applied.Count);
        Assert.Empty(store.Get("g1").AppliedTweaks);
    }

    [Fact]
    public void AjusteCompartilhadoSoVoltaQuandoNenhumJogoMaisUsa()
    {
        var a = new FakeTweak("a");
        var (service, store, _) = Setup(a);
        // dois jogos registraram o mesmo ajuste como seu
        store.Update("g1", s => s.AppliedTweaks = new List<string> { "a" });
        store.Update("g2", s => s.AppliedTweaks = new List<string> { "a" });
        a.On = true;
        var first = service.Undo("g1");
        Assert.True(a.On);
        Assert.Contains("Título a", first.AlreadyOn);
        service.Undo("g2");
        Assert.False(a.On);
    }

    [Fact]
    public void LeLojaDaSteamEImagemDeRequisitos()
    {
        string json = """
        {"730":{"success":true,"data":{"name":"Counter-Strike 2","short_description":"Jogo <b>de tiro</b>.",
        "genres":[{"id":"1","description":"Ação"},{"id":"37","description":"Gratuito para jogar"}],
        "categories":[{"id":1,"description":"Multijogador"},{"id":36,"description":"PvP online"}],
        "pc_requirements":{"minimum":"<strong>Mínimo:</strong><br><ul class=\"bb_ul\"><li>Placa de vídeo: 1 GB<br></li><li>RAM: 8&nbsp;GB</li></ul>","recommended":"<b>Recomendado</b>"}}}}
        """;
        var info = SteamStore.Parse(json, "730");
        Assert.NotNull(info);
        Assert.Equal("Counter-Strike 2", info!.Name);
        Assert.Equal("Jogo de tiro.", info.ShortDescription);
        Assert.Contains("Ação", info.Genres);
        Assert.Contains("PvP online", info.Categories);
        Assert.Contains("RAM: 8", info.MinimumRequirements);
        Assert.DoesNotContain("<", info.MinimumRequirements);
        Assert.Equal("competitive", GameProfiles.Classify("x.exe", info.Genres, info.Categories).Key);
        Assert.Null(SteamStore.Parse("""{"730":{"success":false}}""", "730"));
        Assert.Null(SteamStore.Parse("não é json", "730"));
    }

    [Fact]
    public void AcaCapaLocalDaSteamNosDoisFormatos()
    {
        string steam = Path.Combine(Path.GetTempPath(), "mbx-steam-" + Guid.NewGuid().ToString("N"));
        try
        {
            string cache = Path.Combine(steam, "appcache", "librarycache");
            Directory.CreateDirectory(cache);
            File.WriteAllBytes(Path.Combine(cache, "730_library_600x900.jpg"), new byte[10]);
            Assert.EndsWith("730_library_600x900.jpg", GameCovers.FindSteamLocal(steam, "730"));

            string folder = Path.Combine(cache, "440");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "abc123.jpg"), new byte[100]);
            File.WriteAllBytes(Path.Combine(folder, "library_hero.jpg"), new byte[900]);
            Assert.EndsWith("abc123.jpg", GameCovers.FindSteamLocal(steam, "440"));   // ignora hero/logo
            Assert.Null(GameCovers.FindSteamLocal(steam, "999"));
        }
        finally { Directory.Delete(steam, true); }
    }
}
