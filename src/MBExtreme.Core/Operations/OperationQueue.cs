namespace MBExtreme.Core.Operations;

/// <summary>Como ficou uma operação da fila.</summary>
public sealed record QueueItem(IOperation Operation, OperationState State, string Message, JournalEntry? Entry);

/// <summary>
/// Fila única de operações: roda uma de cada vez (nunca duas atualizações ou dois reparos juntos), registra tudo no diário,
/// segue em frente se uma falhar e para de forma controlada ao cancelar (a que está rodando termina ou observa o cancelamento;
/// as que faltam ficam "Cancelled").
/// </summary>
public sealed class OperationQueue
{
    private readonly OperationJournal _journal;
    private readonly Func<DateTimeOffset> _clock;
    private int _running;

    public OperationQueue(OperationJournal? journal = null, Func<DateTimeOffset>? clock = null)
    {
        _journal = journal ?? OperationJournal.Shared;
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    /// <summary>A fila do programa: todas as páginas usam a mesma, então duas operações nunca rodam ao mesmo tempo.</summary>
    public static OperationQueue Shared { get; } = new();

    public bool IsBusy => Volatile.Read(ref _running) == 1;

    /// <summary>Uma operação mudou de estado (chamado na thread da fila).</summary>
    public event Action<QueueItem>? Changed;

    /// <summary>
    /// Roda as operações em ordem. Se a fila já estiver ocupada (outra página rodando), recusa na hora em vez de rodar junto.
    /// </summary>
    public IReadOnlyList<QueueItem> Run(IReadOnlyList<IOperation> operations, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            throw new InvalidOperationException("Já existe uma operação em andamento. Espere terminar para começar outra.");
        try
        {
            var items = operations.Select(o => new QueueItem(o, OperationState.Pending, "Na fila.", null)).ToList();
            foreach (var item in items) Changed?.Invoke(item);
            for (int i = 0; i < items.Count; i++)
            {
                var operation = items[i].Operation;
                if (ct.IsCancellationRequested)
                {
                    items[i] = items[i] with { State = OperationState.Cancelled, Message = "Cancelada antes de começar." };
                    Changed?.Invoke(items[i]);
                    continue;
                }

                var entry = _journal.Begin(operation.Info, _clock());
                items[i] = items[i] with { State = OperationState.Running, Message = "Em andamento...", Entry = entry };
                Changed?.Invoke(items[i]);
                progress?.Report(operation.Info.Title);

                OperationResult result;
                try { result = operation.Execute(progress, ct); }
                catch (OperationCanceledException) { result = new OperationResult(OperationState.Cancelled, "Cancelada."); }
                catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException
                                               or IOException or System.ComponentModel.Win32Exception or ArgumentException or TimeoutException)
                {
                    result = OperationResult.Fail(Friendly(ex));
                }

                entry = _journal.Finish(entry, result, _clock());
                items[i] = items[i] with { State = result.State, Message = result.Message, Entry = entry };
                Changed?.Invoke(items[i]);
            }
            return items;
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    /// <summary>Desfaz as operações escolhidas (as que sabem desfazer) e marca no diário.</summary>
    public IReadOnlyList<(JournalEntry Entry, OperationResult Result)> Rollback(IReadOnlyList<(JournalEntry Entry, IReversibleOperation Operation)> targets)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            throw new InvalidOperationException("Já existe uma operação em andamento. Espere terminar para desfazer.");
        try
        {
            var results = new List<(JournalEntry, OperationResult)>();
            foreach (var (entry, operation) in targets)
            {
                OperationResult result;
                try { result = operation.Rollback(); }
                catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException
                                               or IOException or System.ComponentModel.Win32Exception or ArgumentException)
                {
                    result = OperationResult.Fail(Friendly(ex));
                }
                if (result.IsSuccess) _journal.MarkRolledBack(entry, _clock());
                results.Add((entry, result));
            }
            return results;
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    /// <summary>Mensagem em português para os erros mais comuns.</summary>
    public static string Friendly(Exception ex) => ex switch
    {
        UnauthorizedAccessException or System.Security.SecurityException => "Sem permissão: abra o MB Extreme como administrador e tente de novo.",
        TimeoutException => "Demorou mais que o limite e foi interrompida.",
        IOException io when io.HResult == unchecked((int)0x80070020) => "Um arquivo está em uso por outro programa. Feche-o e tente de novo.",
        _ => ex.Message,
    };
}
