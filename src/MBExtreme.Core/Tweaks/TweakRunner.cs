using System.ComponentModel;
using MBExtreme.Core.Operations;
using MBExtreme.Core.Progress;

namespace MBExtreme.Core.Tweaks;

public sealed record TweakWork(Tweak Tweak, bool Apply);

public sealed record TweakOutcome(Tweak Tweak, bool Applied, bool Success, string? Error);

public static class TweakRunner
{
    /// <summary>
    /// Aplica ou restaura os ajustes um a um, relatando o progresso. Uma falha não interrompe os demais.
    /// Cada um entra no diário de operações (<paramref name="journal"/>; sem ele, o do programa), para a Central Desfazer.
    /// </summary>
    public static IReadOnlyList<TweakOutcome> Run(
        IReadOnlyList<TweakWork> work,
        IProgress<ProgressEvent>? progress = null,
        CancellationToken ct = default,
        OperationJournal? journal = null)
    {
        journal ??= OperationJournal.Shared;
        var outcomes = new List<TweakOutcome>();
        for (int i = 0; i < work.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (tweak, apply) = work[i];
            progress?.Report(new ProgressEvent("Ajustes", 100.0 * i / work.Count,
                (apply ? "Aplicando: " : "Restaurando: ") + tweak.Title));

            var operation = new TweakOperation(tweak, apply);
            var entry = journal.Begin(operation.Info, DateTimeOffset.Now);
            try
            {
                var result = operation.Execute(null, CancellationToken.None);
                journal.Finish(entry, result, DateTimeOffset.Now);
                if (!apply) MarkEarlierApplyUndone(journal, tweak.Id);
                outcomes.Add(new TweakOutcome(tweak, apply, true, null));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException
                                           or InvalidOperationException or IOException or Win32Exception or ArgumentException)
            {
                journal.Finish(entry, OperationResult.Fail(OperationQueue.Friendly(ex)), DateTimeOffset.Now);
                outcomes.Add(new TweakOutcome(tweak, apply, false, ex.Message));
            }
        }

        progress?.Report(new ProgressEvent("Ajustes", 100, "Concluído"));
        return outcomes;
    }

    /// <summary>Restaurar pela página também conta como desfeito na Central Desfazer.</summary>
    private static void MarkEarlierApplyUndone(OperationJournal journal, string tweakId)
    {
        foreach (var earlier in journal.Load().Where(e => e.Kind == OperationKind.Tweak && e.TargetId == tweakId && e.Changed && !e.RolledBack))
            journal.MarkRolledBack(earlier, DateTimeOffset.Now);
    }
}
