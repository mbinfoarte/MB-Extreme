using MBExtreme.Core.Games;
using MBExtreme.Core.Tweaks;
using Xunit;

namespace MBExtreme.Core.Tests;

public class PerfisPorTituloTests
{
    [Fact]
    public void Banco_embutido_tem_os_jogos_populares_e_todos_validos()
    {
        var all = GameTitles.Builtin;
        Assert.True(all.Count >= 20, $"só {all.Count} títulos");
        foreach (string key in new[] { "eafc", "cod", "valorant", "cs2", "fortnite", "lol", "gtav", "roblox", "efootball" })
            Assert.Contains(all, t => t.Key == key);
        Assert.Equal(all.Count, all.Select(t => t.Key).Distinct().Count());
        foreach (var t in all)
        {
            Assert.NotEmpty(t.Exe);
            Assert.All(t.Exe, e => Assert.True(ExeGuesser.IsValidExeName(e), e));
            Assert.NotEmpty(t.Low);
            Assert.NotEmpty(t.Mid);
            Assert.NotEmpty(t.High);
        }
        // o banco do site parte do mesmo arquivo
        Assert.Equal(all.Count, GameTitles.Parse(GameTitles.ToJson(all)).Count);
    }

    [Theory]
    [InlineData(null, "FC26.exe", "eafc")]
    [InlineData("EA SPORTS FC™ 27", null, "eafc")]
    [InlineData("EA SPORTS FC 26", "outro.exe", "eafc")]
    [InlineData("Call of Duty®", "cod.exe", "cod")]
    [InlineData(null, "valorant-win64-shipping.exe", "valorant")]
    [InlineData("Counter-Strike 2", null, "cs2")]
    [InlineData(null, "cs2", "cs2")]
    [InlineData("Grand Theft Auto V Enhanced", null, "gtav")]
    public void Acha_o_titulo_pelo_exe_ou_pelo_nome(string? name, string? exe, string expected) =>
        Assert.Equal(expected, GameTitles.Find(GameTitles.Builtin, name, exe)?.Key);

    [Theory]
    [InlineData("Paladins", null)]
    [InlineData("Valorantine Story", null)]
    [InlineData(null, "javaw.exe")]
    public void Nao_inventa_perfil_para_jogo_desconhecido(string? name, string? exe) =>
        Assert.Null(GameTitles.Find(GameTitles.Builtin, name, exe));

    [Fact]
    public void Leitura_descarta_o_que_e_invalido()
    {
        string json = """
        [
          { "key": "ok", "title": "Jogo", "exe": ["jogo.exe", "..\\mal.exe", "x.bat"], "profile": "ESTRANHO", "keepRunning": ["Launcher.exe", "c:\\x"], "low": ["a"] },
          { "key": "ok", "title": "Repetido", "exe": ["b.exe"] },
          { "key": "chave com espaço", "title": "X", "exe": ["c.exe"] },
          { "key": "semexe", "title": "Sem nada" },
          null
        ]
        """;
        var list = GameTitles.Parse(json);
        var t = Assert.Single(list);
        Assert.Equal(new[] { "jogo.exe" }, t.Exe);
        Assert.Equal("openworld", t.Profile);
        Assert.Equal(new[] { "Launcher" }, t.KeepRunning);
        Assert.Empty(GameTitles.Parse("{ não é json"));
        Assert.Empty(GameTitles.Parse(""));
    }

    [Fact]
    public void Servidor_substitui_e_esconde_titulos()
    {
        var builtin = GameTitles.Parse("""[{"key":"a","title":"A","exe":["a.exe"]},{"key":"b","title":"B","exe":["b.exe"]}]""");
        var remote = GameTitles.Parse("""[{"key":"a","title":"A nova","exe":["a2.exe"]},{"key":"b","disabled":true},{"key":"c","title":"C","exe":["c.exe"]}]""");
        var merged = GameTitles.Merge(builtin, remote);
        Assert.Equal(new[] { "a", "c" }, merged.Select(t => t.Key));
        Assert.Equal("A nova", merged[0].Title);
        Assert.Null(GameTitles.Find(merged, null, "b.exe"));
    }

    [Fact]
    public void Resposta_do_Firestore_e_lida()
    {
        string body = """{"name":"projects/p/databases/(default)/documents/config/gameProfiles","fields":{"json":{"stringValue":"[{\"key\":\"x\",\"title\":\"X\",\"exe\":[\"x.exe\"]}]"},"updatedAt":{"timestampValue":"2026-11-13T00:00:00Z"}}}""";
        string? json = GameTitleSync.ExtractJson(body);
        Assert.NotNull(json);
        Assert.Equal("x", Assert.Single(GameTitles.Parse(json)).Key);
        Assert.Null(GameTitleSync.ExtractJson("""{"fields":{}}"""));
        Assert.Null(GameTitleSync.ExtractJson("<html>"));
        Assert.Equal("https://firestore.googleapis.com/v1/projects/p/databases/(default)/documents/config/gameProfiles", GameTitleSync.Url("p"));
        Assert.Equal("https://firestore.googleapis.com/v1/projects/p/databases/ai-studio-x/documents/config/gameProfiles", GameTitleSync.Url("p", "ai-studio-x"));
    }

    [Fact]
    public void Plano_se_adapta_ao_jogo_a_placa_e_ao_PC()
    {
        var fc = GameTitles.Find(GameTitles.Builtin, null, "FC26.exe")!;
        var valorant = GameTitles.Find(GameTitles.Builtin, null, "VALORANT-Win64-Shipping.exe")!;

        // FC não tem Reflex: driver NVIDIA com baixa latência ultra
        var fcNvidia = GameTitles.Plan(fc, GpuMaker.Nvidia, 1);
        Assert.Contains(fcNvidia.DriverExtras, d => d.Id == GameTitles.UltraLowLatencyEnabled && d.Value == 1);
        Assert.Equal(fc.Low, fcNvidia.InGame);
        Assert.Contains(fcNvidia.Warnings, w => w.Contains("Javelin"));

        // Valorant tem Reflex: nada de baixa latência do driver, dica do Reflex
        var valNvidia = GameTitles.Plan(valorant, GpuMaker.Nvidia, 4);
        Assert.DoesNotContain(valNvidia.DriverExtras, d => d.Id == GameTitles.UltraLowLatencyEnabled);
        Assert.Contains(valNvidia.Tips, t => t.Contains("Reflex"));
        Assert.Equal(valorant.High, valNvidia.InGame);

        // AMD e Intel: sem configurações de driver NVIDIA
        Assert.Empty(GameTitles.Plan(fc, GpuMaker.Amd, 2).DriverExtras);
        Assert.Contains(GameTitles.Plan(fc, GpuMaker.Amd, 2).Tips, t => t.Contains("Anti-Lag"));
        Assert.Equal(fc.Mid, GameTitles.Plan(fc, GpuMaker.Intel, 2).InGame);
        Assert.Empty(GameTitles.Plan(fc, GpuMaker.Intel, 2).DriverExtras);
    }

    private sealed class FakeTweak : Tweak
    {
        public bool On;
        public FakeTweak(string id) : base(new TweakStore(Path.Combine(Path.GetTempPath(), $"mbx-f-{Guid.NewGuid():N}.json")), id, TweakCategory.Gaming, TweakRisk.Low, "T " + id, "d") { }
        public override bool? IsApplied() => On;
        public override void Apply() => On = true;
        public override void Revert() => On = false;
    }

    [Fact]
    public void Automatico_liga_ao_abrir_e_desfaz_ao_fechar()
    {
        var a = new FakeTweak("a");
        var b = new FakeTweak("b") { On = true };   // já estava ligado: não é desligado ao fechar
        var store = new GameLibraryStore(Path.Combine(Path.GetTempPath(), $"mbx-g-{Guid.NewGuid():N}.json"));
        var service = new GameOptimizationService(new Tweak[] { a, b }, store);
        var optimizedAt = new DateTime(2026, 11, 13, 10, 0, 0);
        store.Update("fc", s => { s.ExeName = "FC26.exe"; s.PlanTweaks = new() { "a", "b" }; s.ProfileKey = "competitive"; s.OptimizedAt = optimizedAt; });

        Assert.Null(service.StartSession("fc"));          // automático desligado: nada
        Assert.False(a.On);

        store.Update("fc", s => s.AutoProfile = true);
        Assert.Equal(new[] { "fc" }, store.IdsForExe("FC26"));
        service.StartSession("fc");
        Assert.True(a.On);
        Assert.True(store.Get("fc").SessionActive);
        Assert.Equal(new[] { "fc" }, store.ActiveSessions());
        Assert.Null(service.StartSession("fc"));          // já ativo: não repete

        service.EndSession("fc");
        Assert.False(a.On);
        Assert.True(b.On);
        Assert.False(store.Get("fc").SessionActive);
        Assert.NotNull(store.Get("fc").OptimizedAt);   // continua marcado como otimizado
        Assert.Empty(store.ActiveSessions());
        Assert.Null(service.EndSession("fc"));
    }
}
