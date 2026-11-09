using System.Text.Json;

namespace MBExtreme.Core.Bottleneck;

public sealed class SessionRecord
{
    public string Game { get; set; } = "";
    public DateTime Date { get; set; }
    public int DurationSeconds { get; set; }
    public double AverageFps { get; set; }
    public double Low1PercentFps { get; set; }
    public string Primary { get; set; } = "";
    public int Tier { get; set; }
    /// <summary>Anotação do usuário sobre as opções gráficas usadas (ex.: "Alto, DLSS qualidade").</summary>
    public string? Settings { get; set; }
}

/// <summary>Sessões medidas, guardadas só neste PC (%AppData%\MBExtreme\sessions.json). Base do aprendizado local.</summary>
public sealed class SessionHistory
{
    private readonly string _path;
    private readonly object _lock = new();
    private List<SessionRecord> _items;

    public SessionHistory(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "sessions.json");
        _items = Load();
    }

    private List<SessionRecord> Load()
    {
        try
        {
            if (File.Exists(_path)) return JsonSerializer.Deserialize<List<SessionRecord>>(File.ReadAllText(_path)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new();
    }

    public void Add(SessionRecord record)
    {
        lock (_lock)
        {
            _items.Add(record);
            if (_items.Count > 500) _items = _items.OrderByDescending(r => r.Date).Take(500).ToList();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(_items, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    public IReadOnlyList<SessionRecord> For(string game)
    {
        lock (_lock) return _items.Where(r => r.Game.Equals(game, StringComparison.OrdinalIgnoreCase)).OrderByDescending(r => r.Date).ToList();
    }

    public IReadOnlyList<SessionRecord> Recent(int count = 20)
    {
        lock (_lock) return _items.OrderByDescending(r => r.Date).Take(count).ToList();
    }

    /// <summary>A sessão com melhor equilíbrio (FPS médio e o "1% pior" pesam juntos) de um jogo, com ao menos 2 minutos.</summary>
    public SessionRecord? Best(string game) =>
        For(game).Where(r => r.DurationSeconds >= 120).OrderByDescending(r => r.AverageFps * 0.5 + r.Low1PercentFps * 0.5).FirstOrDefault();

    /// <summary>Texto de sugestão local: repete a melhor configuração anotada.</summary>
    public string? Suggestion(string game)
    {
        var best = Best(game);
        if (best is null) return null;
        string settings = string.IsNullOrWhiteSpace(best.Settings) ? "sem anotação das opções gráficas" : $"opções: {best.Settings}";
        return $"Sua melhor sessão em {game}: {best.AverageFps:0} fps de média e {best.Low1PercentFps:0} fps no 1% pior ({best.Date:dd/MM/yyyy}), {settings}.";
    }
}

/// <summary>Resumo anônimo de uma sessão, no formato que seria enviado se o usuário autorizar (desligado por padrão).</summary>
public sealed record AnonymousSessionStats(string GameExe, int HardwareTier, int RamGb, double AverageFps, double Low1PercentFps, string Primary, int DurationSeconds)
{
    public static AnonymousSessionStats From(string gameExe, int tier, long totalMemoryBytes, BottleneckReport r) =>
        new(gameExe.ToLowerInvariant(), tier, Bucket((int)Math.Round(totalMemoryBytes / 1024.0 / 1024 / 1024)), Math.Round(r.AverageFps), Math.Round(r.Low1PercentFps),
            r.Primary.ToString(), (int)r.Duration.TotalSeconds);

    // 4, 8, 16, 32, 64: faixas, não o valor exato
    private static int Bucket(int gb) => gb <= 0 ? 0 : new[] { 4, 8, 12, 16, 24, 32, 48, 64, 128 }.FirstOrDefault(b => gb <= b, 128);
}
