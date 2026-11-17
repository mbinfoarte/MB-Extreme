using MBExtreme.Core.Cleanup;
using MBExtreme.Core.Repair;
using MBExtreme.Core.Tweaks;
using Xunit;

namespace MBExtreme.Core.Tests;

public class CatalogoExpandidoTests
{
    private static TweakStore NewStore() => new(Path.Combine(Path.GetTempPath(), $"mbx-cmd-{Guid.NewGuid():N}.json"));

    [Fact]
    public void FsutilLeOValorSemDependerDoIdioma()
    {
        Assert.Equal("0", CommandParsers.FsutilValue("NTFS DisableDeleteNotify = 0  (Desativado)\r\nReFS DisableDeleteNotify = 1", "DisableDeleteNotify"));
        Assert.Equal("1", CommandParsers.FsutilValue("DisableDeleteNotify = 1 (Disabled)", "disabledeletenotify"));
        Assert.Null(CommandParsers.FsutilValue("sem valor aqui", "DisableDeleteNotify"));
    }

    [Fact]
    public void BcdeditLeOValorDoElemento()
    {
        string output = "identifier              {current}\r\nuseplatformtick         Yes\r\nhypervisorlaunchtype    Off\r\ndisabledynamictick      Yes";
        Assert.Equal("Yes", CommandParsers.BcdValue(output, "useplatformtick"));
        Assert.Equal("Off", CommandParsers.BcdValue(output, "hypervisorlaunchtype"));
        Assert.Null(CommandParsers.BcdValue(output, "tscsyncpolicy"));
        Assert.Null(CommandParsers.BcdValue("useplatformtickX Yes", "useplatformtick"));
    }

    [Fact]
    public void ComandoGuardaOEstadoOriginalEORestaura()
    {
        string state = "1";
        var written = new List<string>();
        var store = NewStore();
        var tweak = new CommandTweak(store, "t-cmd", TweakCategory.Storage, TweakRisk.Low, "t", "d",
            () => state, "0", v => { state = v; written.Add(v); }, "1");

        Assert.False(tweak.IsApplied());
        Assert.False(tweak.HasBackup);
        tweak.Apply();
        tweak.Apply();   // aplicar de novo não pode trocar o backup
        Assert.True(tweak.IsApplied());
        Assert.True(tweak.HasBackup);
        state = "0";     // simula o estado depois do ajuste

        tweak.Revert();
        Assert.Equal("1", state);
        Assert.False(tweak.HasBackup);
        Assert.False(tweak.IsApplied());
    }

    [Fact]
    public void ComandoSemBackupUsaOPadrao()
    {
        string state = "0";
        var tweak = new CommandTweak(NewStore(), "t-cmd2", TweakCategory.Boot, TweakRisk.Low, "t", "d", () => state, "0", v => state = v, "auto");
        tweak.Revert();
        Assert.Equal("auto", state);
    }

    [Fact]
    public void EstadoIlegivelNaoQuebra()
    {
        var tweak = new CommandTweak(NewStore(), "t-cmd3", TweakCategory.Boot, TweakRisk.Low, "t", "d", () => null, "0", _ => { }, "1");
        Assert.Null(tweak.IsApplied());
    }

    [Fact]
    public void ExportaOBcdAntesDeMudarSoNaPrimeiraVez()
    {
        int exported = 0;
        string state = "-";
        var tweak = new CommandTweak(NewStore(), "t-cmd4", TweakCategory.Boot, TweakRisk.High, "t", "d", () => state, "yes", v => state = v, "-", () => exported++);
        tweak.Apply();
        tweak.Apply();
        Assert.Equal(1, exported);
    }

    [Fact]
    public void AjusteEspecificoDeMarcaPodeFicarIndisponivel()
    {
        var hidden = new CommandTweak(NewStore(), "t-cmd5", TweakCategory.Gaming, TweakRisk.Low, "t", "d", () => "0", "0", _ => { }, "0") { AppliesTo = () => false };
        var shown = new CommandTweak(NewStore(), "t-cmd6", TweakCategory.Gaming, TweakRisk.Low, "t", "d", () => "0", "0", _ => { }, "0");
        var broken = new CommandTweak(NewStore(), "t-cmd7", TweakCategory.Gaming, TweakRisk.Low, "t", "d", () => "0", "0", _ => { }, "0") { AppliesTo = () => throw new InvalidOperationException() };
        Assert.False(hidden.IsAvailable);
        Assert.True(shown.IsAvailable);
        Assert.False(broken.IsAvailable);
    }

    [Fact]
    public void CatalogoNovoRespeitaAsRegrasDeRisco()
    {
        if (!OperatingSystem.IsWindows()) return;
        var all = TweakCatalog.Create(NewStore());
        Assert.Equal(all.Count, all.Select(t => t.Id).Distinct().Count());
        foreach (string id in new[] { "storage-trim", "storage-hibernate-off", "net-rss-on", "net-rsc-off", "boot-timer-bcd", "boot-hypervisor-off", "game-mpo-off", "game-amd-ulps-off", "browser-chrome", "net-adapter-power-off" })
            Assert.Contains(all, t => t.Id == id);
        Assert.All(all.Where(t => t.Risk != TweakRisk.Low), t =>
        {
            Assert.False(t.Recommended);
            Assert.False(string.IsNullOrWhiteSpace(t.Warning));
        });
        Assert.All(all.Where(t => t.Id.StartsWith("game-amd") || t.Id.StartsWith("game-nvidia")), t => Assert.NotNull(t.AppliesTo));
    }

    [Fact]
    public void LimpezaDeShadersExisteEFicaDesmarcada()
    {
        var shader = CleanupCatalog.Create().First(c => c.Id == "shader-cache");
        Assert.False(shader.DefaultSelected);
        Assert.NotEmpty(shader.Roots);
    }

    [Fact]
    public void NovasTarefasDeReparoTemComandosETextos()
    {
        var tasks = RepairCatalog.Create("C:", "pt");
        Assert.Equal(tasks.Count, tasks.Select(t => t.Id).Distinct().Count());
        foreach (string id in new[] { "audio", "bluetooth", "wifi", "explorer", "xbox", "anticheat", "shutdown-fix", "eventlogs", "dns-renew" })
        {
            var task = Assert.Single(tasks, t => t.Id == id);
            Assert.NotEmpty(task.Steps);
            Assert.False(string.IsNullOrWhiteSpace(task.Description));
        }
        Assert.NotNull(tasks.First(t => t.Id == "eventlogs").Caution);
        Assert.NotNull(tasks.First(t => t.Id == "anticheat").Caution);
    }

    [Fact]
    public void RotinaSemanalMontaOAgendamentoSeguro()
    {
        var args = WeeklyRoutine.CreateArguments(@"C:\Program Files\MB Extreme\MBExtreme.exe");
        Assert.Contains("/TN", args);
        Assert.Contains(args, a => a.Contains("--weekly") && a.StartsWith("\"C:\\Program Files"));
        Assert.Equal("SUN", args[Array.IndexOf(args, "/D") + 1]);
        Assert.Contains("LIMITED", args);   // sem privilégio elevado
        Assert.True(WeeklyRoutine.IsSafeExePath(@"C:\a\b.exe"));
        Assert.False(WeeklyRoutine.IsSafeExePath(@"C:\a\b.exe & calc"));
        Assert.False(WeeklyRoutine.IsSafeExePath("b.bat"));
        Assert.False(WeeklyRoutine.IsSafeExePath(null));
    }
}
