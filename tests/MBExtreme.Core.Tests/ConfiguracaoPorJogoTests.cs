using MBExtreme.Core.Games;
using Xunit;

namespace MBExtreme.Core.Tests;

public class ConfiguracaoPorJogoTests
{
    private sealed class FakeDriver : IDriverProfileStore
    {
        public bool Available { get; set; } = true;
        public Dictionary<(string, uint), uint> Own = new();
        public Dictionary<uint, uint> Global = new();
        public string? FailWrite;
        public DriverValue? Read(string exe, uint id) => Own.TryGetValue((exe, id), out var v) ? new DriverValue(v, true) : Global.TryGetValue(id, out var g) ? new DriverValue(g, false) : null;
        public string? Write(string exe, uint id, uint value) { if (FailWrite is not null) return FailWrite; Own[(exe, id)] = value; return null; }
        public string? Remove(string exe, uint id) { Own.Remove((exe, id)); return null; }
    }

    private sealed class FakeGpu : IGpuPreferenceStore
    {
        public Dictionary<string, GpuPreferenceMode> Map = new();
        public GpuPreferenceMode Get(string p) => Map.GetValueOrDefault(p);
        public void Set(string p, GpuPreferenceMode m) { if (m == GpuPreferenceMode.Default) Map.Remove(p); else Map[p] = m; }
    }

    private static GameDriverService Service(FakeDriver d, FakeGpu g) => new(d, g, Path.Combine(Path.GetTempPath(), $"mbx-drv-{Guid.NewGuid():N}.json"));

    [Theory]
    [InlineData("GpuPreference=2;", GpuPreferenceMode.HighPerformance)]
    [InlineData("GpuPreference=1;", GpuPreferenceMode.PowerSaving)]
    [InlineData("GpuPreference=9;", GpuPreferenceMode.Default)]
    [InlineData("", GpuPreferenceMode.Default)]
    [InlineData(null, GpuPreferenceMode.Default)]
    [InlineData("AutoHDREnable=1;GpuPreference=2;", GpuPreferenceMode.HighPerformance)]
    public void PreferenciaDeGpu_Interpreta(string? raw, GpuPreferenceMode esperado) => Assert.Equal(esperado, GpuPreference.Parse(raw));

    [Fact]
    public void PreferenciaDeGpu_FormataERecompoe() => Assert.Equal(GpuPreferenceMode.HighPerformance, GpuPreference.Parse(GpuPreference.Format(GpuPreferenceMode.HighPerformance)));

    [Fact]
    public void PlanoDeDriver_CompetitivoNvidia_EnergiaELatencia()
    {
        var plan = DriverPlan.For(GameProfiles.Competitive, GpuMaker.Nvidia);
        Assert.Equal(1u, plan.PowerMode);
        Assert.Equal(1u, plan.PreRenderLimit);
        Assert.Equal(GpuPreferenceMode.HighPerformance, plan.GpuPreference);
    }

    [Fact]
    public void PlanoDeDriver_Amd_NaoMexeNoDriverNvidia()
    {
        var plan = DriverPlan.For(GameProfiles.Competitive, GpuMaker.Amd);
        Assert.Null(plan.PowerMode);
        Assert.Null(plan.PreRenderLimit);
    }

    [Fact]
    public void PlanoDeDriver_JogoLeve_NaoMexeEmNada() => Assert.True(DriverPlan.For(GameProfiles.Light, GpuMaker.Nvidia).IsEmpty);

    private static GameSnapshot Snap(DriverValue? power = null, DriverValue? pre = null, GpuPreferenceMode gpu = GpuPreferenceMode.Default, bool driver = true, string? dlss = null, bool? high = false) =>
        new(new Dictionary<string, bool?> { ["game-dvr"] = true, ["game-mmcss"] = false },
            new Dictionary<string, string> { ["game-dvr"] = "Sem gravação", ["game-mmcss"] = "Prioridade de jogos" },
            high, gpu, driver, power, pre, dlss);

    [Fact]
    public void Tabela_MarcaOQueEstaDiferente()
    {
        var rows = GameCompare.Build(GameProfiles.Light, GpuMaker.Nvidia, Snap());
        var dvr = rows.Single(r => r.Title == "Sem gravação");
        Assert.Equal(RowState.Ok, dvr.State);
        Assert.False(dvr.CanFix);
        var mm = rows.Single(r => r.Title == "Prioridade de jogos");
        Assert.Equal(RowState.Different, mm.State);
        Assert.True(mm.CanFix);
    }

    [Fact]
    public void Tabela_DriverNvidia_ComparaAtualERecomendado()
    {
        var rows = GameCompare.Build(GameProfiles.Competitive, GpuMaker.Nvidia, Snap(power: new DriverValue(0, false), pre: new DriverValue(1, true), gpu: GpuPreferenceMode.HighPerformance));
        var power = rows.Single(r => r.Title.StartsWith("Modo de gerenciamento"));
        Assert.Equal("Adaptativo (padrão global)", power.Current);
        Assert.Equal("Preferir desempenho máximo", power.Recommended);
        Assert.True(power.CanFix);
        Assert.Equal(RowState.Ok, rows.Single(r => r.Title.StartsWith("Quadros pré-renderizados")).State);
        Assert.Equal(RowState.Ok, rows.Single(r => r.Title.StartsWith("Placa de vídeo usada")).State);
    }

    [Fact]
    public void Tabela_SemDriverNvidia_AvisaEmVezDeOferecerCorrecao()
    {
        var rows = GameCompare.Build(GameProfiles.Competitive, GpuMaker.Nvidia, Snap(driver: false));
        var info = rows.Single(r => r.Group == "Driver NVIDIA");
        Assert.Equal(RowState.Info, info.State);
        Assert.False(info.CanFix);
    }

    [Fact]
    public void Tabela_PlacaAmd_NaoMostraLinhasNvidia()
    {
        var rows = GameCompare.Build(GameProfiles.Competitive, GpuMaker.Amd, Snap());
        Assert.DoesNotContain(rows, r => r.Group == "Driver NVIDIA");
    }

    [Theory]
    [InlineData("310.2.1.0", RowState.Ok)]
    [InlineData("3.8.10.0", RowState.Info)]
    [InlineData("3.5.0.0", RowState.Different)]
    [InlineData("2.5.1.0", RowState.Different)]
    [InlineData("abc", RowState.Info)]
    public void Dlss_AvaliaAVersao(string versao, RowState esperado) => Assert.Equal(esperado, DlssInfo.Evaluate(versao).State);

    [Fact]
    public void Dlss_ProcuraArquivoNaPastaDoJogo()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mbx-dlss-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "bin"));
        Assert.Null(DlssInfo.Find(dir));
        File.WriteAllText(Path.Combine(dir, "bin", "nvngx_dlss.dll"), "x");
        Assert.NotNull(DlssInfo.Find(dir)); // arquivo sem versão: "desconhecida"
        Assert.Null(DlssInfo.Find(Path.Combine(dir, "nao-existe")));
    }

    [Fact]
    public void Aplicar_GuardaOriginalEDesfazDevolve()
    {
        var d = new FakeDriver();
        d.Own[("cs2.exe", DriverSettingIds.PowerMode)] = 4;
        var g = new FakeGpu();
        g.Map["C:\\jogo\\cs2.exe"] = GpuPreferenceMode.PowerSaving;
        var s = Service(d, g);
        var plan = DriverPlan.For(GameProfiles.Competitive, GpuMaker.Nvidia);

        var r = s.Apply("g1", "cs2.exe", "C:\\jogo\\cs2.exe", plan);
        Assert.Equal(3, r.Applied.Count);
        Assert.Equal(1u, d.Own[("cs2.exe", DriverSettingIds.PowerMode)]);
        Assert.Equal(1u, d.Own[("cs2.exe", DriverSettingIds.PreRenderLimit)]);
        Assert.Equal(GpuPreferenceMode.HighPerformance, g.Map["C:\\jogo\\cs2.exe"]);

        // reaplicar não perde o original
        s.Apply("g1", "cs2.exe", "C:\\jogo\\cs2.exe", plan);
        var u = s.Undo("g1");
        Assert.Empty(u.Failed);
        Assert.Equal(4u, d.Own[("cs2.exe", DriverSettingIds.PowerMode)]);          // tinha valor próprio: volta
        Assert.False(d.Own.ContainsKey(("cs2.exe", DriverSettingIds.PreRenderLimit))); // não tinha: removido
        Assert.Equal(GpuPreferenceMode.PowerSaving, g.Map["C:\\jogo\\cs2.exe"]);
        Assert.False(s.HasBackup("g1"));
    }

    [Fact]
    public void Aplicar_FalhaDoDriver_NaoGuardaNemQuebra()
    {
        var d = new FakeDriver { FailWrite = "código NVAPI -5" };
        var s = Service(d, new FakeGpu());
        var r = s.Apply("g1", "cs2.exe", null, DriverPlan.For(GameProfiles.Competitive, GpuMaker.Nvidia));
        Assert.Empty(r.Applied);
        Assert.Equal(2, r.Failed.Count);
        Assert.Contains("NVAPI", r.Failed[0]);
    }

    [Fact]
    public void Aplicar_DriverIndisponivel_AvisaSemEscrever()
    {
        var d = new FakeDriver { Available = false };
        var r = Service(d, new FakeGpu()).Apply("g1", "cs2.exe", null, DriverPlan.For(GameProfiles.OpenWorld, GpuMaker.Nvidia));
        Assert.Empty(d.Own);
        Assert.Single(r.Failed);
    }

    [Fact]
    public void Aplicar_JaNoValor_NaoGrava()
    {
        var d = new FakeDriver();
        d.Own[("cs2.exe", DriverSettingIds.PowerMode)] = 1;
        var r = Service(d, new FakeGpu()).Apply("g1", "cs2.exe", null, new DriverPlan(1, null, null));
        Assert.Empty(r.Applied);
        Assert.Empty(r.Failed);
    }

    [Fact]
    public void NvApi_VersoesDasEstruturasBatemComOTamanho()
    {
        Assert.Equal(0x00013020u, NvApiDrs.Version(NvApiDrs.SettingSize));
        Assert.Equal(0x00011014u, NvApiDrs.Version(NvApiDrs.ProfileSize));
        Assert.Equal(0x00013008u, NvApiDrs.Version(NvApiDrs.AppSize));
    }
}
