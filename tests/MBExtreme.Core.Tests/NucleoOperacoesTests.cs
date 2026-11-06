using MBExtreme.Core.Operations;
using MBExtreme.Core.Tweaks;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>Núcleo de operações (Fase 1): fila única, estados, diário que sobrevive a quedas e a Central Desfazer.</summary>
public class NucleoOperacoesTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "mbx-diario-" + Guid.NewGuid().ToString("N"));

    /// <summary>Ajuste de mentira com backup de verdade no TweakStore (como o RegistryTweak faz).</summary>
    private sealed class FakeTweak : Tweak
    {
        public bool On;
        public bool FailRevert;
        public FakeTweak(TweakStore store, string id) : base(store, id, TweakCategory.Privacy, TweakRisk.Low, "Ajuste " + id, "d") { }
        public override bool? IsApplied() => On;
        public override void Apply() { if (!Store.Has(Id + "|v")) Store.Set(Id + "|v", "original"); On = true; }
        public override void Revert() { if (FailRevert) return; Store.Remove(Id + "|v"); On = false; }
    }

    private sealed class FakeOperation(string id, Func<CancellationToken, OperationResult> run) : IOperation
    {
        public OperationInfo Info { get; } = new(id, OperationKind.Other, "Operação " + id);
        public int Runs;
        public OperationResult Execute(IProgress<string>? progress, CancellationToken ct) { Runs++; return run(ct); }
    }

    [Fact]
    public void Fila_roda_em_ordem_segue_depois_de_uma_falha_e_registra_tudo()
    {
        var journal = new OperationJournal(TempFolder());
        var queue = new OperationQueue(journal);
        var ops = new IOperation[]
        {
            new FakeOperation("a", _ => OperationResult.Ok()),
            new FakeOperation("b", _ => throw new UnauthorizedAccessException("negado")),
            new FakeOperation("c", _ => OperationResult.Reboot()),
        };
        var states = new List<(string, OperationState)>();
        queue.Changed += item => states.Add((item.Operation.Info.Id, item.State));

        var result = queue.Run(ops);

        Assert.Equal(new[] { OperationState.Succeeded, OperationState.Failed, OperationState.RebootRequired }, result.Select(r => r.State));
        Assert.Contains("administrador", result[1].Message);   // mensagem em português, não a exceção crua
        Assert.Equal(new[] { ("a", OperationState.Running), ("a", OperationState.Succeeded) },
            states.Where(s => s.Item1 == "a" && s.Item2 != OperationState.Pending));
        var saved = journal.Load();
        Assert.Equal(3, saved.Count);
        Assert.All(saved, e => Assert.NotNull(e.FinishedAt));
        Assert.False(queue.IsBusy);
    }

    [Fact]
    public void Cancelar_para_a_atual_e_as_que_faltam_ficam_canceladas()
    {
        var journal = new OperationJournal(TempFolder());
        using var cts = new CancellationTokenSource();
        var second = new FakeOperation("b", ct => { cts.Cancel(); ct.ThrowIfCancellationRequested(); return OperationResult.Ok(); });
        var third = new FakeOperation("c", _ => OperationResult.Ok());
        var result = new OperationQueue(journal).Run(new IOperation[] { new FakeOperation("a", _ => OperationResult.Ok()), second, third }, ct: cts.Token);

        Assert.Equal(new[] { OperationState.Succeeded, OperationState.Cancelled, OperationState.Cancelled }, result.Select(r => r.State));
        Assert.Equal(0, third.Runs);   // nem começou
        Assert.Equal(2, journal.Load().Count);   // a cancelada antes de começar não mexeu em nada: não entra no diário
    }

    [Fact]
    public void Duas_filas_ao_mesmo_tempo_nao_rodam_juntas()
    {
        var journal = new OperationJournal(TempFolder());
        var queue = new OperationQueue(journal);
        using var inside = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var slow = new FakeOperation("lenta", _ => { inside.Set(); release.Wait(5000); return OperationResult.Ok(); });
        var running = Task.Run(() => queue.Run(new IOperation[] { slow }));
        Assert.True(inside.Wait(5000));
        Assert.True(queue.IsBusy);
        Assert.Throws<InvalidOperationException>(() => queue.Run(new IOperation[] { new FakeOperation("outra", _ => OperationResult.Ok()) }));
        release.Set();
        running.Wait(5000);
        Assert.False(queue.IsBusy);
    }

    [Fact]
    public void Diario_aguenta_linha_cortada_e_mostra_a_operacao_interrompida()
    {
        string folder = TempFolder();
        var journal = new OperationJournal(folder);
        var now = new DateTimeOffset(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);
        journal.Record(new OperationInfo("ok", OperationKind.Cleanup, "Limpeza"), OperationResult.Ok(), now);
        journal.Begin(new OperationInfo("caiu", OperationKind.Repair, "Reparo"), now.AddMinutes(1));   // o programa "caiu" aqui
        File.AppendAllText(Directory.GetFiles(folder).Single(), "{\"EntryId\":\"meia-linh");          // linha cortada pela queda de energia

        var entries = new OperationJournal(folder).Load();
        Assert.Equal(2, entries.Count);
        var repair = entries.Single(e => e.OperationId == "caiu");
        Assert.True(repair.Interrupted);
        Assert.True(repair.Changed);   // pode ter mexido: aparece na Central
        Assert.Contains("Interrompida", repair.Message);
    }

    [Fact]
    public void Diario_apaga_os_dias_mais_antigos()
    {
        string folder = TempFolder();
        var journal = new OperationJournal(folder) { KeepDays = 30 };
        var today = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
        journal.Record(new OperationInfo("velho", OperationKind.Other, "x"), OperationResult.Ok(), today.AddDays(-60));
        journal.Record(new OperationInfo("novo", OperationKind.Other, "y"), OperationResult.Ok(), today);
        journal.Prune(today);
        Assert.Equal("novo", Assert.Single(journal.Load()).OperationId);
    }

    [Fact]
    public void Ajuste_pela_fila_pode_ser_desfeito_e_confere_que_voltou()
    {
        var store = new TweakStore(Path.Combine(Path.GetTempPath(), $"mbx-t-{Guid.NewGuid():N}.json"));
        var journal = new OperationJournal(TempFolder());
        var queue = new OperationQueue(journal);
        var tweak = new FakeTweak(store, "anuncios");

        var run = queue.Run(new IOperation[] { new TweakOperation(tweak, apply: true) });
        Assert.Equal(OperationState.Succeeded, run[0].State);
        Assert.True(tweak.On);

        var center = UndoCenter.Build(journal.Load(), new Tweak[] { tweak });
        var item = Assert.Single(center);
        Assert.True(item.CanUndo);

        var undo = queue.Rollback(new[] { (item.Entry, (IReversibleOperation)new TweakOperation(tweak, apply: true)) });
        Assert.True(undo[0].Result.IsSuccess);
        Assert.False(tweak.On);

        var after = Assert.Single(UndoCenter.Build(journal.Load(), new Tweak[] { tweak }));
        Assert.False(after.CanUndo);
        Assert.Equal("Já desfeito.", after.Note);
    }

    [Fact]
    public void Desfazer_que_nao_voltou_e_avisado_e_nao_marca_como_desfeito()
    {
        var store = new TweakStore(Path.Combine(Path.GetTempPath(), $"mbx-t-{Guid.NewGuid():N}.json"));
        var journal = new OperationJournal(TempFolder());
        var queue = new OperationQueue(journal);
        var tweak = new FakeTweak(store, "teimoso");
        queue.Run(new IOperation[] { new TweakOperation(tweak, apply: true) });
        tweak.FailRevert = true;   // o Windows não aceitou voltar

        var item = Assert.Single(UndoCenter.Build(journal.Load(), new Tweak[] { tweak }));
        var undo = queue.Rollback(new[] { (item.Entry, (IReversibleOperation)new TweakOperation(tweak, apply: true)) });
        Assert.Equal(OperationState.Failed, undo[0].Result.State);
        Assert.True(Assert.Single(UndoCenter.Build(journal.Load(), new Tweak[] { tweak })).CanUndo);   // continua na lista para tentar de novo
    }

    [Fact]
    public void Central_so_oferece_o_mais_recente_e_separa_o_que_nao_tem_volta()
    {
        var store = new TweakStore(Path.Combine(Path.GetTempPath(), $"mbx-t-{Guid.NewGuid():N}.json"));
        var journal = new OperationJournal(TempFolder());
        var tweak = new FakeTweak(store, "efeitos");
        var t0 = new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
        tweak.Apply();
        journal.Record(new TweakOperation(tweak, true).Info, OperationResult.Ok(), t0);
        journal.Record(new TweakOperation(tweak, true).Info, OperationResult.Ok(), t0.AddHours(1));
        journal.Record(new OperationInfo("cleanup.temp", OperationKind.Cleanup, "Limpeza: temporários"), OperationResult.Ok(), t0.AddHours(2));
        journal.Record(new OperationInfo("app.remove.x", OperationKind.AppRemoval, "Remover aplicativo: X"), OperationResult.Ok(), t0.AddHours(3));
        journal.Record(new OperationInfo("fail", OperationKind.Tweak, "Falhou", TargetId: "efeitos"), OperationResult.Fail("não deu"), t0.AddHours(4));
        journal.Record(new OperationInfo("sumiu", OperationKind.Tweak, "Ajuste antigo", TargetId: "nao-existe-mais"), OperationResult.Ok(), t0.AddHours(5));

        var items = UndoCenter.Build(journal.Load(), new Tweak[] { tweak });
        Assert.Equal(5, items.Count);                                     // a que falhou não mudou nada: fora da lista
        Assert.Single(items, i => i.CanUndo);                             // só a aplicação mais recente do mesmo ajuste
        Assert.Contains(items, i => !i.CanUndo && i.Note.StartsWith("Aplicado de novo depois", StringComparison.Ordinal));
        Assert.Contains(items, i => i.Entry.Kind == OperationKind.Cleanup && i.Note.StartsWith("Sem volta", StringComparison.Ordinal));
        Assert.Contains(items, i => i.Entry.Kind == OperationKind.AppRemoval && i.Note.Contains("Microsoft Store"));
        Assert.Contains(items, i => i.Note.Contains("não existe mais"));
    }

    [Fact]
    public void Runner_antigo_dos_ajustes_tambem_registra_no_diario()
    {
        var store = new TweakStore(Path.Combine(Path.GetTempPath(), $"mbx-t-{Guid.NewGuid():N}.json"));
        var journal = new OperationJournal(TempFolder());
        var tweak = new FakeTweak(store, "visual");
        TweakRunner.Run(new[] { new TweakWork(tweak, true) }, journal: journal);
        Assert.True(Assert.Single(UndoCenter.Build(journal.Load(), new Tweak[] { tweak })).CanUndo);

        // restaurar pela própria página conta como desfeito na Central
        TweakRunner.Run(new[] { new TweakWork(tweak, false) }, journal: journal);
        var item = Assert.Single(UndoCenter.Build(journal.Load(), new Tweak[] { tweak }));
        Assert.False(item.CanUndo);
        Assert.Equal("Já desfeito.", item.Note);
    }
}
