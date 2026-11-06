using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MBExtreme.Core.Operations;

namespace MBExtreme.Core.Diagnostics;

/// <summary>Gravidade de um achado. "Ok" e "Info" não são problemas: aparecem só para o usuário saber.</summary>
public enum Severity { Ok, Info, Warning, Problem, Critical }

public enum CheckDepth { Quick, Deep }

/// <summary>Como terminou uma verificação. "Unavailable" não é erro: é algo que este PC não deixa verificar (ou não tem).</summary>
public enum CheckStatus { Done, Unavailable, Error, Cancelled, Reused }

/// <summary>
/// Como corrigir um achado: operações para a fila (o programa corrige, depois de você confirmar) e/ou a página onde o usuário resolve.
/// <paramref name="Operations"/> é chamada só na hora de corrigir (as operações leem o estado atual).
/// </summary>
public sealed record FixPlan(string Text, Func<IReadOnlyList<IOperation>>? Operations = null, string? Page = null, bool ChangesSystem = true)
{
    public bool IsAutomatic => Operations is not null;
}

/// <summary>Um item encontrado na análise.</summary>
public sealed record Finding(string Id, string Title, string Detail, Severity Severity, FixPlan? Fix = null)
{
    public bool IsProblem => Severity >= Severity.Warning;
}

/// <summary>Uma verificação. Só lê: diagnóstico nunca altera o Windows.</summary>
public interface IDiagnosticCheck
{
    string Id { get; }
    string Title { get; }
    CheckDepth Depth { get; }

    IReadOnlyList<Finding> Run(CancellationToken ct);
}

/// <summary>Este PC não deixa (ou não precisa) fazer esta verificação: aparece como "não verificável", não como erro.</summary>
public sealed class CheckUnavailableException(string message) : Exception(message);

public sealed record CheckRun(string CheckId, string Title, CheckDepth Depth, CheckStatus Status, string Note, TimeSpan Took, IReadOnlyList<Finding> Findings);

public sealed record DiagnosticReport(DateTimeOffset Started, DateTimeOffset Finished, bool Deep, bool Cancelled, IReadOnlyList<CheckRun> Checks)
{
    public TimeSpan Took => Finished - Started;

    /// <summary>Do mais grave para o menos grave.</summary>
    public IReadOnlyList<Finding> Findings => Checks.SelectMany(c => c.Findings).OrderByDescending(f => f.Severity).ThenBy(f => f.Title, StringComparer.CurrentCulture).ToList();

    public int Problems => Findings.Count(f => f.IsProblem);

    public int Count(Severity severity) => Findings.Count(f => f.Severity == severity);
}

/// <summary>Andamento da análise: etapa atual, quantas são e o tempo que passou (sem porcentagem inventada).</summary>
public sealed record DiagnosticProgress(int Step, int Total, string Title, TimeSpan Elapsed);

/// <summary>
/// Roda as verificações uma de cada vez, com etapa e tempo decorrido, e para quando cancelado (a verificação em andamento termina antes).
/// Na análise profunda, as verificações rápidas de uma análise recente (até <see cref="ReuseFor"/>) são reaproveitadas.
/// </summary>
public sealed class DiagnosticRunner
{
    private readonly Func<DateTimeOffset> _clock;

    public DiagnosticRunner(Func<DateTimeOffset>? clock = null) => _clock = clock ?? (() => DateTimeOffset.Now);

    public TimeSpan ReuseFor { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>A última análise (para reaproveitar e para o "antes e depois").</summary>
    public DiagnosticReport? Last { get; private set; }

    public DiagnosticReport Run(IReadOnlyList<IDiagnosticCheck> checks, bool deep, IProgress<DiagnosticProgress>? progress = null, CancellationToken ct = default, bool forceFresh = false,
        Action<CheckRun>? onCheck = null)
    {
        var started = _clock();
        var wanted = checks.Where(c => deep || c.Depth == CheckDepth.Quick).ToList();
        var reusable = !forceFresh && deep && Last is { } last && started - last.Finished <= ReuseFor
            ? last.Checks.Where(c => c.Depth == CheckDepth.Quick && c.Status == CheckStatus.Done).ToDictionary(c => c.CheckId)
            : new Dictionary<string, CheckRun>();
        var runs = new ReportingList(onCheck);
        bool cancelled = false;
        for (int i = 0; i < wanted.Count; i++)
        {
            var check = wanted[i];
            if (ct.IsCancellationRequested)
            {
                cancelled = true;
                runs.Add(new CheckRun(check.Id, check.Title, check.Depth, CheckStatus.Cancelled, "Não verificado (análise parada).", TimeSpan.Zero, Array.Empty<Finding>()));
                continue;
            }
            if (reusable.TryGetValue(check.Id, out var previous))
            {
                runs.Add(previous with { Status = CheckStatus.Reused, Note = "Resultado da análise rápida de agora há pouco." });
                continue;
            }
            progress?.Report(new DiagnosticProgress(i + 1, wanted.Count, check.Title, _clock() - started));
            var t0 = _clock();
            try
            {
                var findings = check.Run(ct);
                runs.Add(new CheckRun(check.Id, check.Title, check.Depth, CheckStatus.Done, "", _clock() - t0, findings));
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                runs.Add(new CheckRun(check.Id, check.Title, check.Depth, CheckStatus.Cancelled, "Não verificado (análise parada).", _clock() - t0, Array.Empty<Finding>()));
            }
            catch (CheckUnavailableException ex)
            {
                runs.Add(new CheckRun(check.Id, check.Title, check.Depth, CheckStatus.Unavailable, ex.Message, _clock() - t0, Array.Empty<Finding>()));
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception
                                           or JsonException or FormatException or System.Security.SecurityException or AggregateException)
            {
                runs.Add(new CheckRun(check.Id, check.Title, check.Depth, CheckStatus.Error, "Não foi possível verificar: " + ex.Message, _clock() - t0, Array.Empty<Finding>()));
            }
        }
        var report = new DiagnosticReport(started, _clock(), deep, cancelled, runs);
        if (!cancelled) Last = report;
        return report;
    }
}

/// <summary>Lista que avisa cada verificação concluída (o Painel mostra os itens aparecendo um a um).</summary>
internal sealed class ReportingList(Action<CheckRun>? onAdd) : List<CheckRun>
{
    public new void Add(CheckRun run)
    {
        base.Add(run);
        onAdd?.Invoke(run);
    }
}

/// <summary>
/// Nota de saúde do PC, de 0 a 100, a partir do que a análise achou (itens ignorados não contam):
/// crítico tira 25, problema tira 12, atenção tira 5 e informação não tira nada. Verificações que não rodaram não mudam a nota.
/// </summary>
public static class HealthScore
{
    public static int Of(IEnumerable<Finding> findings, ISet<string>? ignored = null)
    {
        int score = 100;
        foreach (var f in findings)
        {
            if (ignored is not null && ignored.Contains(f.Id)) continue;
            score -= f.Severity switch { Severity.Critical => 25, Severity.Problem => 12, Severity.Warning => 5, _ => 0 };
        }
        return Math.Clamp(score, 0, 100);
    }

    /// <summary>Frase do título do Painel para a nota.</summary>
    public static string Headline(int score) => score switch
    {
        >= 90 => "Seu PC está em dia",
        >= 70 => "Seu PC está bem, com alguns pontos de atenção",
        >= 40 => "Seu PC precisa de atenção",
        _ => "Seu PC precisa de cuidados",
    };

    /// <summary>Faixa de cor: 2 = boa, 1 = atenção, 0 = ruim.</summary>
    public static int Level(int score) => score >= 80 ? 2 : score >= 50 ? 1 : 0;
}

/// <summary>Resumo de uma análise guardado no histórico (sem as correções, que dependem do momento).</summary>
public sealed record DiagnosticSummary(DateTimeOffset Started, TimeSpan Took, bool Deep, int Critical, int Problems, int Warnings, int Infos, IReadOnlyList<string> Titles)
{
    public static DiagnosticSummary From(DiagnosticReport r) => new(r.Started, r.Took, r.Deep,
        r.Count(Severity.Critical), r.Count(Severity.Problem), r.Count(Severity.Warning), r.Count(Severity.Info),
        r.Findings.Where(f => f.IsProblem).Select(f => f.Title).ToList());
}

/// <summary>Histórico das análises (as 30 mais recentes) em %AppData%\MBExtreme\analises.json.</summary>
public sealed class DiagnosticHistory
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false, Converters = { new JsonStringEnumConverter() } };

    public DiagnosticHistory(string? path = null) => Path = path ?? System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "analises.json");

    public string Path { get; }

    public const int Keep = 30;

    public IReadOnlyList<DiagnosticSummary> Load()
    {
        try { return File.Exists(Path) ? JsonSerializer.Deserialize<List<DiagnosticSummary>>(File.ReadAllText(Path), Json) ?? new() : new List<DiagnosticSummary>(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new List<DiagnosticSummary>(); }
    }

    public void Add(DiagnosticReport report)
    {
        var list = Load().Prepend(DiagnosticSummary.From(report)).Take(Keep).ToList();
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(list, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>Relatório técnico em texto (para salvar e mandar ao suporte).</summary>
public static class DiagnosticText
{
    public static string SeverityText(Severity s) => s switch
    {
        Severity.Critical => "CRÍTICO",
        Severity.Problem => "Problema",
        Severity.Warning => "Atenção",
        Severity.Info => "Informação",
        _ => "OK",
    };

    public static string StatusText(CheckStatus s) => s switch
    {
        CheckStatus.Done => "verificado",
        CheckStatus.Reused => "reaproveitado",
        CheckStatus.Unavailable => "não verificável",
        CheckStatus.Cancelled => "não verificado",
        _ => "falhou",
    };

    public static string Report(DiagnosticReport r, string machine, string windows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("MB Extreme — relatório da análise");
        sb.AppendLine($"Computador: {machine} · {windows}");
        sb.AppendLine($"Análise {(r.Deep ? "profunda" : "rápida")} em {r.Started.ToLocalTime():dd/MM/yyyy HH:mm}, duração {Duration(r.Took)}{(r.Cancelled ? " (parada antes do fim)" : "")}.");
        sb.AppendLine($"Resumo: {r.Count(Severity.Critical)} crítico(s), {r.Count(Severity.Problem)} problema(s), {r.Count(Severity.Warning)} atenção, {r.Count(Severity.Info)} informação.");
        sb.AppendLine();
        sb.AppendLine("Achados:");
        foreach (var f in r.Findings) sb.AppendLine($"- [{SeverityText(f.Severity)}] {f.Title}: {f.Detail}");
        if (r.Findings.Count == 0) sb.AppendLine("- Nada encontrado.");
        sb.AppendLine();
        sb.AppendLine("Verificações:");
        foreach (var c in r.Checks) sb.AppendLine($"- {c.Title}: {StatusText(c.Status)}{(c.Note.Length > 0 ? " — " + c.Note : "")} ({Duration(c.Took)})");
        return sb.ToString();
    }

    public static string Duration(TimeSpan t) => t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} min {t.Seconds:00} s" : $"{Math.Max(0, t.TotalSeconds):0.0} s";
}
