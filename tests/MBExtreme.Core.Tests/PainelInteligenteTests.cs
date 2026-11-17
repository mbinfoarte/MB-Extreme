using MBExtreme.Core.Diagnostics;
using MBExtreme.Core.Operations;
using MBExtreme.Core.Storage;
using MBExtreme.Core.Tweaks;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>Painel inteligente (Fase 2): análise que só lê, etapas, reaproveitamento, "não verificável", regras, histórico e relatório.</summary>
public class PainelInteligenteTests
{
    private static IDiagnosticCheck Check(string id, CheckDepth depth, Func<CancellationToken, IReadOnlyList<Finding>> run) => new DelegateCheck(id, "Verificação " + id, depth, run);

    private static IReadOnlyList<Finding> One(string id, Severity s) => new[] { new Finding(id, "Achado " + id, "detalhe", s) };

    [Fact]
    public void Analise_rapida_so_roda_as_rapidas_e_informa_a_etapa()
    {
        int deepRuns = 0;
        var checks = new[]
        {
            Check("a", CheckDepth.Quick, _ => One("a", Severity.Warning)),
            Check("b", CheckDepth.Quick, _ => Array.Empty<Finding>()),
            Check("p", CheckDepth.Deep, _ => { deepRuns++; return One("p", Severity.Problem); }),
        };
        var steps = new List<DiagnosticProgress>();
        var report = new DiagnosticRunner().Run(checks, deep: false, new SyncProgress<DiagnosticProgress>(steps.Add));
        Assert.Equal(0, deepRuns);
        Assert.Equal(2, report.Checks.Count);
        Assert.Equal(new[] { 1, 2 }, steps.Select(s => s.Step));
        Assert.All(steps, s => Assert.Equal(2, s.Total));
        Assert.Equal(1, report.Problems);
    }

    [Fact]
    public void Profunda_reaproveita_a_rapida_recente_e_refaz_se_for_antiga()
    {
        var now = new DateTimeOffset(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);
        var runner = new DiagnosticRunner(() => now);
        int quickRuns = 0;
        var checks = new[]
        {
            Check("q", CheckDepth.Quick, _ => { quickRuns++; return One("q", Severity.Info); }),
            Check("d", CheckDepth.Deep, _ => Array.Empty<Finding>()),
        };
        runner.Run(checks, deep: false);
        now = now.AddMinutes(5);
        var deep = runner.Run(checks, deep: true);
        Assert.Equal(1, quickRuns);                                   // reaproveitou
        Assert.Equal(CheckStatus.Reused, deep.Checks[0].Status);
        Assert.Single(deep.Findings);                                 // o achado da rápida continua no relatório

        now = now.AddMinutes(30);
        runner.Run(checks, deep: true);
        Assert.Equal(2, quickRuns);                                   // velha demais: refez
        runner.Run(checks, deep: true, forceFresh: true);
        Assert.Equal(3, quickRuns);                                   // "Analisar novamente" sempre refaz
    }

    [Fact]
    public void Nao_verificavel_nao_e_erro_e_erro_nao_derruba_a_analise()
    {
        var checks = new[]
        {
            Check("u", CheckDepth.Quick, _ => throw new CheckUnavailableException("Este PC não tem.")),
            Check("e", CheckDepth.Quick, _ => throw new InvalidOperationException("quebrou")),
            Check("ok", CheckDepth.Quick, _ => One("ok", Severity.Warning)),
        };
        var report = new DiagnosticRunner().Run(checks, deep: false);
        Assert.Equal(new[] { CheckStatus.Unavailable, CheckStatus.Error, CheckStatus.Done }, report.Checks.Select(c => c.Status));
        Assert.Equal(1, report.Problems);   // o "não verificável" e o erro não viram achado
        Assert.Contains("quebrou", report.Checks[1].Note);
    }

    [Fact]
    public void Parar_marca_o_resto_como_nao_verificado_e_nao_guarda_para_reaproveitar()
    {
        using var cts = new CancellationTokenSource();
        var checks = new[]
        {
            Check("a", CheckDepth.Quick, _ => { cts.Cancel(); return One("a", Severity.Info); }),
            Check("b", CheckDepth.Quick, _ => One("b", Severity.Info)),
        };
        var runner = new DiagnosticRunner();
        var report = runner.Run(checks, deep: false, ct: cts.Token);
        Assert.True(report.Cancelled);
        Assert.Equal(CheckStatus.Cancelled, report.Checks[1].Status);
        Assert.Null(runner.Last);
    }

    [Fact]
    public void Achados_saem_do_mais_grave_para_o_menos_grave()
    {
        var report = new DiagnosticRunner().Run(new[]
        {
            Check("i", CheckDepth.Quick, _ => One("i", Severity.Info)),
            Check("c", CheckDepth.Quick, _ => One("c", Severity.Critical)),
            Check("w", CheckDepth.Quick, _ => One("w", Severity.Warning)),
        }, deep: false);
        Assert.Equal(new[] { Severity.Critical, Severity.Warning, Severity.Info }, report.Findings.Select(f => f.Severity));
        Assert.Equal(2, report.Problems);
    }

    [Theory]
    [InlineData(4, true, Severity.Critical)]
    [InlineData(4, false, Severity.Problem)]    // disco de dados: nunca crítico
    [InlineData(8, true, Severity.Problem)]
    [InlineData(12, true, Severity.Warning)]
    [InlineData(40, true, null)]
    public void Espaco_livre_por_faixa(int freePercent, bool system, Severity? expected)
    {
        var v = new VolumeInfo(system ? @"C:\" : @"D:\", "", "NTFS", 1000L << 30, (1000L << 30) * freePercent / 100);
        var f = DiagnosticRules.DiskSpace(v, "C:");
        Assert.Equal(expected, f?.Severity);
        if (f is not null) Assert.Equal("cleanup", f.Fix!.Page);
    }

    [Fact]
    public void Regras_de_lixo_inicializacao_e_saude_do_disco()
    {
        Assert.Equal(Severity.Ok, DiagnosticRules.Junk(50L << 20));
        Assert.Equal(Severity.Info, DiagnosticRules.Junk(300L << 20));
        Assert.Equal(Severity.Warning, DiagnosticRules.Junk(2L << 30));
        Assert.Equal(Severity.Ok, DiagnosticRules.Startup(5));
        Assert.Equal(Severity.Warning, DiagnosticRules.Startup(9));
        Assert.Equal(Severity.Problem, DiagnosticRules.Startup(20));

        DiskInfo Disk(string health, double? wear = null, double? temp = null) => new("SSD X", "SSD", "NVMe", health, 1L << 40, temp, wear, null, null, null);
        Assert.Null(DiagnosticRules.DiskHealth(Disk("Healthy")));
        Assert.Equal(Severity.Critical, DiagnosticRules.DiskHealth(Disk("Warning"))!.Severity);
        Assert.Equal(Severity.Problem, DiagnosticRules.DiskHealth(Disk("Healthy", wear: 95))!.Severity);
        Assert.Equal(Severity.Warning, DiagnosticRules.DiskHealth(Disk("Healthy", temp: 70))!.Severity);
        Assert.Null(DiagnosticRules.DiskHealth(Disk("Healthy", wear: null, temp: null)));   // sem dado, não inventa
    }

    [Theory]
    [InlineData("NTFS DisableDeleteNotify = 0  (Allowed)\nReFS DisableDeleteNotify = 0", true)]
    [InlineData("NTFS DisableDeleteNotify = 1  (Disabled)", false)]
    [InlineData("qualquer outra coisa", null)]
    public void Le_o_estado_do_trim(string output, bool? expected) => Assert.Equal(expected, DiagnosticRules.TrimEnabled(output));

    [Fact]
    public void Historico_guarda_as_30_mais_recentes_e_aguenta_arquivo_estragado()
    {
        string path = Path.Combine(Path.GetTempPath(), $"mbx-analises-{Guid.NewGuid():N}.json");
        var history = new DiagnosticHistory(path);
        var runner = new DiagnosticRunner();
        for (int i = 0; i < 35; i++) history.Add(runner.Run(new[] { Check("a", CheckDepth.Quick, _ => One("a", Severity.Warning)) }, deep: false));
        var saved = history.Load();
        Assert.Equal(DiagnosticHistory.Keep, saved.Count);
        Assert.Equal(1, saved[0].Warnings);
        Assert.Equal(new[] { "Achado a" }, saved[0].Titles);

        File.WriteAllText(path, "{ estragado");
        Assert.Empty(history.Load());
    }

    [Fact]
    public void Relatorio_tecnico_tem_achados_e_o_estado_de_cada_verificacao()
    {
        var report = new DiagnosticRunner().Run(new[]
        {
            Check("a", CheckDepth.Quick, _ => One("a", Severity.Problem)),
            Check("u", CheckDepth.Quick, _ => throw new CheckUnavailableException("sem suporte")),
        }, deep: false);
        string text = DiagnosticText.Report(report, "PC-TESTE", "Windows 11");
        Assert.Contains("PC-TESTE", text);
        Assert.Contains("[Problema] Achado a", text);
        Assert.Contains("não verificável — sem suporte", text);
        Assert.Contains("análise rápida", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Configuracao_mostra_como_desfazer_e_restaurar_pela_pagina_marca_como_desfeito()
    {
        var journal = new OperationJournal(Path.Combine(Path.GetTempPath(), "mbx-diario-" + Guid.NewGuid().ToString("N")));
        journal.RecordSetting("gamesettings.hags", "Ligar o HAGS", true, "Para desfazer: \"Restaurar originais\" em Jogos e energia.");
        var item = Assert.Single(UndoCenter.Build(journal.Load(), Array.Empty<Tweak>()));
        Assert.False(item.CanUndo);
        Assert.Contains("Restaurar originais", item.Note);

        journal.MarkRolledBackWhere(e => e.OperationId.StartsWith("gamesettings.", StringComparison.Ordinal), DateTimeOffset.Now);
        Assert.Equal("Já desfeito.", Assert.Single(UndoCenter.Build(journal.Load(), Array.Empty<Tweak>())).Note);
    }

    /// <summary>Progress que chama na hora (o Progress padrão adia para outra thread).</summary>
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
