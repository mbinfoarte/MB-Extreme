using System.Text.Json;

namespace MBExtreme.Core.Network;

/// <summary>Resumo de uma análise de rota guardada, para comparar antes e depois de otimizar.</summary>
public sealed class NetReport
{
    public DateTime At { get; set; }
    public string Host { get; set; } = "";
    public bool Optimized { get; set; }
    public double AvgMs { get; set; }
    public double JitterMs { get; set; }
    public double LossPercent { get; set; }
    public int Samples { get; set; }
}

public sealed record NetComparison(NetReport? Before, NetReport? After, string Message, IReadOnlyList<string> Warnings);

public static class NetCompare
{
    public static NetComparison Build(IReadOnlyList<NetReport> reports)
    {
        var ordered = reports.OrderBy(r => r.At).ToList();
        var after = ordered.LastOrDefault(r => r.Optimized);
        var before = after is null ? ordered.LastOrDefault(r => !r.Optimized) : ordered.LastOrDefault(r => !r.Optimized && r.At < after.At);
        if (before is null || after is null)
            return new NetComparison(before, after, before is null && after is null
                ? "Analise a rota do jogo para guardar o ping atual (antes)."
                : after is null ? "Medida de antes guardada. Otimize e analise de novo para ver o depois."
                : "Falta uma medida de antes (sem otimização).", Array.Empty<string>());

        var warnings = new List<string>();
        if (!string.Equals(before.Host, after.Host, StringComparison.OrdinalIgnoreCase))
            warnings.Add("Os dois testes foram em servidores diferentes; a comparação é menos confiável.");
        double change = before.AvgMs <= 0 ? 0 : Math.Round((after.AvgMs - before.AvgMs) / before.AvgMs * 100, 1);
        string verdict = Math.Abs(after.AvgMs - before.AvgMs) < 3 ? "Sem diferença relevante" : after.AvgMs < before.AvgMs ? "Ping menor" : "Ping maior";
        string message = $"{verdict}: {before.AvgMs:0} → {after.AvgMs:0} ms ({change:+0.#;-0.#;0}%), oscilação {before.JitterMs:0.#} → {after.JitterMs:0.#} ms, perda {before.LossPercent:0.#}% → {after.LossPercent:0.#}%.";
        if (Math.Abs(change) < 5) warnings.Add("Variações pequenas de ping são normais entre um teste e outro (hora do dia, tráfego).");
        return new NetComparison(before, after, message, warnings);
    }
}

public sealed class NetReportStore
{
    private const int Keep = 20;
    private readonly string _path;
    private readonly object _lock = new();

    public NetReportStore(string? path = null) =>
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "net-reports.json");

    public IReadOnlyList<NetReport> Get(string gameId)
    {
        lock (_lock) return Load().GetValueOrDefault(gameId) ?? new List<NetReport>();
    }

    public void Add(string gameId, NetReport report)
    {
        lock (_lock)
        {
            var all = Load();
            var list = all.GetValueOrDefault(gameId) ?? new List<NetReport>();
            list.Add(report);
            all[gameId] = list.OrderBy(r => r.At).TakeLast(Keep).ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.WriteAllText(_path, JsonSerializer.Serialize(all));
        }
    }

    public void Clear(string gameId)
    {
        lock (_lock)
        {
            var all = Load();
            if (all.Remove(gameId)) File.WriteAllText(_path, JsonSerializer.Serialize(all));
        }
    }

    private Dictionary<string, List<NetReport>> Load()
    {
        try { return File.Exists(_path) ? JsonSerializer.Deserialize<Dictionary<string, List<NetReport>>>(File.ReadAllText(_path)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or JsonException) { return new(); }
    }
}
