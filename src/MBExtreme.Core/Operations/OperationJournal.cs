using System.Text.Json;
using System.Text.Json.Serialization;

namespace MBExtreme.Core.Operations;

/// <summary>Uma linha do diário: o que foi feito, quando, como terminou e se ainda dá para desfazer.</summary>
public sealed record JournalEntry
{
    public required string EntryId { get; init; }
    public required string OperationId { get; init; }
    public required OperationKind Kind { get; init; }
    public required string Title { get; init; }
    public string? TargetId { get; init; }
    public bool Reversible { get; init; }
    public OperationState State { get; init; }
    public string Message { get; init; } = "";
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }

    /// <summary>Esta alteração já foi desfeita (pela Central Desfazer ou por "Restaurar" na página dela).</summary>
    public bool RolledBack { get; init; }

    /// <summary>Ficou em "Running" e o programa fechou (queda de energia, travamento): ninguém sabe como terminou.</summary>
    public bool Interrupted { get; init; }

    /// <summary>O Windows mudou de fato (para a Central Desfazer listar).</summary>
    [JsonIgnore]
    public bool Changed => State is OperationState.Succeeded or OperationState.RebootRequired || Interrupted;
}

/// <summary>
/// Diário de todas as alterações feitas pelo programa: um arquivo por dia (uma linha JSON por evento) em %AppData%\MBExtreme\diario.
/// Sobrevive a quedas: uma linha incompleta é ignorada e uma operação que ficou "Running" aparece como interrompida.
/// </summary>
public sealed class OperationJournal
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };
    private readonly object _lock = new();

    public OperationJournal(string? folder = null) => Folder = folder ?? DefaultFolder;

    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "diario");

    /// <summary>O diário do programa (os testes usam o seu próprio).</summary>
    public static OperationJournal Shared { get; set; } = new();

    public string Folder { get; }

    /// <summary>Quantos dias de diário são guardados.</summary>
    public int KeepDays { get; init; } = 90;

    public JournalEntry Begin(OperationInfo info, DateTimeOffset now)
    {
        var entry = new JournalEntry
        {
            EntryId = Guid.NewGuid().ToString("N"), OperationId = info.Id, Kind = info.Kind, Title = info.Title,
            TargetId = info.TargetId, Reversible = info.Reversible, State = OperationState.Running, StartedAt = now,
        };
        Append(entry, now);
        return entry;
    }

    public JournalEntry Finish(JournalEntry entry, OperationResult result, DateTimeOffset now)
    {
        var done = entry with { State = result.State, Message = result.Message, FinishedAt = now };
        Append(done, now);
        return done;
    }

    /// <summary>Registra de uma vez algo que já terminou (ex.: uma limpeza feita pela página dela).</summary>
    public JournalEntry Record(OperationInfo info, OperationResult result, DateTimeOffset now) => Finish(Begin(info, now), result, now);

    /// <summary>Marca que uma alteração foi desfeita.</summary>
    public void MarkRolledBack(JournalEntry entry, DateTimeOffset now) => Append(entry with { RolledBack = true }, now);

    private void Append(JournalEntry entry, DateTimeOffset now)
    {
        string line = JsonSerializer.Serialize(entry, Json);
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(FileFor(now), line + "\n");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }   // sem diário o programa segue; nunca derruba a operação
        }
    }

    private string FileFor(DateTimeOffset when) => Path.Combine(Folder, when.ToLocalTime().ToString("yyyy-MM-dd") + ".jsonl");

    /// <summary>
    /// O estado mais recente de cada entrada (a última linha de cada uma vale), mais recente primeiro.
    /// Uma entrada que ficou em "Running" sem fim aparece como interrompida.
    /// </summary>
    public IReadOnlyList<JournalEntry> Load(int days = 90)
    {
        var latest = new Dictionary<string, JournalEntry>();
        lock (_lock)
        {
            if (!Directory.Exists(Folder)) return Array.Empty<JournalEntry>();
            var files = Directory.GetFiles(Folder, "*.jsonl").OrderBy(f => f, StringComparer.Ordinal).TakeLast(Math.Max(1, days));
            foreach (string file in files)
            {
                string[] lines;
                try { lines = File.ReadAllLines(file); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        if (JsonSerializer.Deserialize<JournalEntry>(line, Json) is { } entry) latest[entry.EntryId] = entry;
                    }
                    catch (JsonException) { }   // linha cortada no meio (queda de energia): ignora só ela
                }
            }
        }
        return latest.Values
            .Select(e => e.State == OperationState.Running ? e with { Interrupted = true, Message = "Interrompida: o programa fechou antes de terminar." } : e)
            .OrderByDescending(e => e.StartedAt)
            .ToList();
    }

    /// <summary>Apaga os arquivos mais velhos que <see cref="KeepDays"/>.</summary>
    public void Prune(DateTimeOffset now)
    {
        lock (_lock)
        {
            if (!Directory.Exists(Folder)) return;
            string limit = now.ToLocalTime().AddDays(-KeepDays).ToString("yyyy-MM-dd");
            foreach (string file in Directory.GetFiles(Folder, "*.jsonl"))
                if (string.CompareOrdinal(Path.GetFileNameWithoutExtension(file), limit) < 0)
                {
                    try { File.Delete(file); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
        }
    }
}
