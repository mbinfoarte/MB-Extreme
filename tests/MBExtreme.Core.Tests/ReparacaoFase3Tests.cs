using MBExtreme.Core.Diagnostics;
using MBExtreme.Core.Repair;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>Fase 3: relógio, Windows Update e tempo limite dos reparos (sem cortar DISM/SFC/CHKDSK no meio).</summary>
public class ReparacaoFase3Tests
{
    [Theory]
    [InlineData("Rastreando time.windows.com [20.101.57.9:123].\nColetando 1 amostras.\nA hora atual é 06/11/2026 10:00:00.\n10:00:00, +00.0123456s", 0.0123456)]
    [InlineData("Tracking time.windows.com [20.101.57.9:123].\n10:00:00, -425.5000000s", -425.5)]
    [InlineData("10:00:00, +12,5000000s", 12.5)]
    [InlineData("10:00:00, error: 0x800705B4", null)]
    [InlineData("", null)]
    public void Le_a_diferenca_do_relogio_em_qualquer_idioma(string output, double? expected)
    {
        var value = ClockCheck.ParseOffset(output);
        if (expected is null) Assert.Null(value);
        else Assert.Equal(expected.Value, value!.Value, 6);
    }

    [Fact]
    public void Gravidade_e_descricao_do_relogio()
    {
        Assert.Equal(Severity.Ok, ClockCheck.Severity(2));
        Assert.Equal(Severity.Warning, ClockCheck.Severity(-90));
        Assert.Equal(Severity.Problem, ClockCheck.Severity(600));
        Assert.Equal("10 min atrasado", ClockCheck.Describe(600));
        Assert.Equal("2 h adiantado", ClockCheck.Describe(-7200));
        Assert.True(ClockCheck.YearLooksWrong(new DateTime(2009, 1, 1)));
        Assert.False(ClockCheck.YearLooksWrong(new DateTime(2026, 11, 6)));
    }

    [Fact]
    public void Windows_update_por_idade_e_data_do_powershell()
    {
        Assert.Equal(Severity.Ok, UpdateCheck.Age(10));
        Assert.Equal(Severity.Warning, UpdateCheck.Age(50));
        Assert.Equal(Severity.Problem, UpdateCheck.Age(120));
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 3, 0, 0, TimeSpan.Zero), UpdateCheck.ParseDate("2026-10-20T03:00:00.0000000Z\r\n"));
        Assert.Null(UpdateCheck.ParseDate(""));
        Assert.Contains(UpdateCheck.Services, s => s.Name == "wuauserv");
    }

    [Fact]
    public void DISM_SFC_e_CHKDSK_nunca_sao_cortados_os_comuns_sim()
    {
        Assert.False(RepairTimeouts.For(StepKind.Dism, 15).Stop);
        Assert.False(RepairTimeouts.For(StepKind.Sfc, 15).Stop);
        Assert.False(RepairTimeouts.For(StepKind.Chkdsk, 15).Stop);
        var generic = RepairTimeouts.For(StepKind.Generic, 15);
        Assert.True(generic.Stop);
        Assert.Equal(TimeSpan.FromMinutes(15), generic.Limit);
        Assert.Equal(TimeSpan.FromMinutes(120), RepairTimeouts.For(StepKind.Generic, 999).Limit);
    }

    /// <summary>Com um processo de verdade: o comum que passa do limite é encerrado; o "crítico" só ganha o aviso e termina sozinho.</summary>
    [Fact]
    public async Task Tempo_limite_encerra_o_comum_e_so_avisa_no_critico()
    {
        if (OperatingSystem.IsWindows() || !File.Exists("/bin/sleep")) return;
        var runner = new RepairRunner { TimeoutPolicy = kind => new StepTimeout(TimeSpan.FromSeconds(1), kind == StepKind.Generic) };
        var messages = new List<string>();
        var progress = new SyncProgress(u => { lock (messages) messages.Add(u.Message); });

        var stuck = new RepairTask("t1", "g", "Comando travado", "d", "1 s", new[] { new RepairStep("Dormindo", "/bin/sleep", "20") });
        var started = DateTime.UtcNow;
        var r1 = await runner.RunAsync(stuck, progress, CancellationToken.None);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(15));
        Assert.False(r1.Success);
        Assert.Contains("Passou do limite", r1.Steps[0].Verdict);

        var slow = new RepairTask("t2", "g", "Reparo demorado", "d", "3 s", new[] { new RepairStep("Dormindo", "/bin/sleep", "3", StepKind.Sfc) });
        var r2 = await runner.RunAsync(slow, progress, CancellationToken.None);
        Assert.Equal(0, r2.Steps[0].ExitCode);   // terminou sozinho, não foi morto
        lock (messages) Assert.Contains(messages, m => m.Contains("demorando mais que o normal"));
    }

    private sealed class SyncProgress(Action<RepairUpdate> report) : IProgress<RepairUpdate>
    {
        public void Report(RepairUpdate value) => report(value);
    }
}
