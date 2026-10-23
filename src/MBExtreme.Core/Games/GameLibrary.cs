using System.Text;
using System.Text.Json;

namespace MBExtreme.Core.Games;

public enum GameLauncher { Steam, Epic, Gog, Manual }

/// <summary>Um jogo encontrado no PC. <see cref="ExeName"/> é o nome do processo (ex.: cs2.exe), usado para vigiar e priorizar.</summary>
public sealed record InstalledGame(string Id, string Name, GameLauncher Launcher, string InstallPath, string? ExeName, string? LaunchTarget, string? SteamAppId = null)
{
    public string LauncherName => Launcher switch
    {
        GameLauncher.Steam => "Steam", GameLauncher.Epic => "Epic Games", GameLauncher.Gog => "GOG", _ => "Adicionado por você",
    };
}

/// <summary>Leitor do formato de texto da Valve (.vdf e .acf): pares "chave" "valor" e blocos { }.</summary>
public static class Vdf
{
    public static Dictionary<string, object> Parse(string text)
    {
        int i = 0;
        return ReadBlock(text, ref i, top: true);
    }

    private static Dictionary<string, object> ReadBlock(string s, ref int i, bool top)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            string? key = NextToken(s, ref i, out bool close);
            if (close) return result;
            if (key is null) return result;
            string? value = NextToken(s, ref i, out bool open, allowOpen: true);
            if (open) result[key] = ReadBlock(s, ref i, top: false);
            else if (value is not null) result[key] = value;
            else return result;
        }
    }

    // "texto" | { | }. Devolve null no fim; close = encontrou "}"; em allowOpen, close vira "encontrou {".
    private static string? NextToken(string s, ref int i, out bool flag, bool allowOpen = false)
    {
        flag = false;
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                while (i < s.Length && s[i] != '\n') i++;
                continue;
            }
            if (c == '{') { i++; flag = allowOpen; return null; }
            if (c == '}') { i++; flag = !allowOpen; return null; }
            if (c == '"')
            {
                var sb = new StringBuilder();
                i++;
                while (i < s.Length && s[i] != '"')
                {
                    if (s[i] == '\\' && i + 1 < s.Length)
                    {
                        i++;
                        sb.Append(s[i] switch { 'n' => '\n', 't' => '\t', _ => s[i] });
                    }
                    else sb.Append(s[i]);
                    i++;
                }
                i++;
                return sb.ToString();
            }
            i++;
        }
        return null;
    }

    public static string? GetString(Dictionary<string, object> block, string key) =>
        block.TryGetValue(key, out var v) ? v as string : null;

    public static Dictionary<string, object>? GetBlock(Dictionary<string, object> block, string key) =>
        block.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;
}

public static class GameParsers
{
    /// <summary>Pastas de biblioteca da Steam a partir do conteúdo de libraryfolders.vdf.</summary>
    public static IReadOnlyList<string> SteamLibraryFolders(string vdf)
    {
        var root = Vdf.Parse(vdf);
        var folders = Vdf.GetBlock(root, "libraryfolders");
        var result = new List<string>();
        if (folders is null) return result;
        foreach (var entry in folders.Values.OfType<Dictionary<string, object>>())
            if (Vdf.GetString(entry, "path") is { Length: > 0 } path) result.Add(path);
        return result;
    }

    private static readonly HashSet<string> SteamNonGames = new(StringComparer.OrdinalIgnoreCase)
    {
        "228980", "1007", "1070560", "1391110", "1493710", "1628350", "2180100", "250820",
    };

    /// <summary>Jogo a partir de um appmanifest_*.acf; ignora ferramentas da Steam.</summary>
    public static InstalledGame? SteamManifest(string acf, string libraryPath)
    {
        var state = Vdf.GetBlock(Vdf.Parse(acf), "AppState");
        if (state is null) return null;
        string? id = Vdf.GetString(state, "appid");
        string? name = Vdf.GetString(state, "name");
        string? dir = Vdf.GetString(state, "installdir");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(dir)) return null;
        if (SteamNonGames.Contains(id) || name.StartsWith("Steamworks", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Steam Linux Runtime", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Proton", StringComparison.OrdinalIgnoreCase)) return null;
        return new InstalledGame("steam:" + id, name, GameLauncher.Steam, Path.Combine(libraryPath, "steamapps", "common", dir), null, "steam://rungameid/" + id, id);
    }

    /// <summary>Jogo a partir de um manifesto .item da Epic Games (JSON).</summary>
    public static InstalledGame? EpicManifest(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            string? name = r.TryGetProperty("DisplayName", out var n) ? n.GetString() : null;
            string? app = r.TryGetProperty("AppName", out var a) ? a.GetString() : null;
            string? dir = r.TryGetProperty("InstallLocation", out var d) ? d.GetString() : null;
            string? exe = r.TryGetProperty("LaunchExecutable", out var e) ? e.GetString() : null;
            bool isApp = !r.TryGetProperty("bIsApplication", out var ia) || ia.ValueKind != JsonValueKind.False;
            if (!isApp || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(app) || string.IsNullOrWhiteSpace(dir)) return null;
            string? exeName = string.IsNullOrWhiteSpace(exe) ? null : ExeGuesser.FileNameOf(exe);
            return new InstalledGame("epic:" + app, name, GameLauncher.Epic, dir, exeName,
                $"com.epicgames.launcher://apps/{Uri.EscapeDataString(app)}?action=launch&silent=true");
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public static class ExeGuesser
{
    private static readonly string[] Ignored =
    {
        "unins", "setup", "install", "crash", "report", "redist", "vcredist", "dxsetup", "dotnet", "launcher_helper",
        "eac", "easyanticheat", "battleye", "beclient", "cefprocess", "helper", "updater", "uninstall", "webhelper", "ue4prereq", "prereq", "dxwebsetup",
    };

    /// <summary>Nome do arquivo de um caminho com "\\" ou "/" (independe do sistema onde roda).</summary>
    public static string FileNameOf(string path) => path[(path.LastIndexOfAny(new[] { '\\', '/' }) + 1)..];

    public static bool IsValidExeName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        && name.IndexOfAny(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' }) < 0 && name.Length <= 120 && !name.Contains("..", StringComparison.Ordinal);

    /// <summary>Escolhe o executável mais provável do jogo: o maior que não seja instalador, atualizador ou antitrapaça.</summary>
    public static string? Pick(IEnumerable<(string Path, long Size)> candidates)
    {
        var best = candidates
            .Where(c => IsValidExeName(FileNameOf(c.Path)))
            .Where(c => !Ignored.Any(w => c.Path.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(c => c.Size)
            .FirstOrDefault();
        return best.Path is null ? null : FileNameOf(best.Path);
    }
}

/// <summary>Dados que o MB Extreme guarda de cada jogo (não mexe nos arquivos do jogo).</summary>
public sealed class GameSettings
{
    public bool HighPriority { get; set; }
    public bool BoostOnLaunch { get; set; } = true;
    public double MinutesPlayed { get; set; }
    public DateTime? LastPlayed { get; set; }
    public string? ExeName { get; set; }

    /// <summary>Tipo escolhido pelo usuário ou sugerido (competitive, openworld, racing, light, app).</summary>
    public string? ProfileKey { get; set; }

    /// <summary>Ajustes que a otimização deste jogo ligou (só os que não estavam ligados antes), para desfazer depois.</summary>
    public List<string> AppliedTweaks { get; set; } = new();

    public DateTime? OptimizedAt { get; set; }

    /// <summary>A otimização de rede criou uma política de QoS para este jogo (removida ao desfazer).</summary>
    public bool QosApplied { get; set; }
}

public sealed class ManualGame
{
    public string Name { get; set; } = "";
    public string ExePath { get; set; } = "";
}

/// <summary>Guarda perfis por jogo e jogos adicionados à mão em %AppData%\MBExtreme\games.json.</summary>
public sealed class GameLibraryStore
{
    private readonly string _path;
    private readonly object _lock = new();
    private Data _data;

    private sealed class Data
    {
        public Dictionary<string, GameSettings> Settings { get; set; } = new();
        public List<ManualGame> Manual { get; set; } = new();
    }

    public GameLibraryStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "games.json");
        _data = Load();
    }

    private Data Load()
    {
        try
        {
            if (File.Exists(_path)) return JsonSerializer.Deserialize<Data>(File.ReadAllText(_path)) ?? new Data();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new Data();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public GameSettings Get(string id)
    {
        lock (_lock) return _data.Settings.TryGetValue(id, out var s) ? s : new GameSettings();
    }

    public void Update(string id, Action<GameSettings> change)
    {
        lock (_lock)
        {
            if (!_data.Settings.TryGetValue(id, out var s)) _data.Settings[id] = s = new GameSettings();
            change(s);
            Save();
        }
    }

    /// <summary>Soma o tempo jogado ao jogo cujo executável (com ou sem .exe) é o informado.</summary>
    public bool AddPlaytime(string processName, double minutes, DateTime now)
    {
        string exe = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? processName : processName + ".exe";
        lock (_lock)
        {
            var match = _data.Settings.Where(kv => string.Equals(kv.Value.ExeName, exe, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Value).ToList();
            if (match.Count == 0 || minutes <= 0) return false;
            foreach (var s in match) { s.MinutesPlayed += minutes; s.LastPlayed = now; }
            Save();
            return true;
        }
    }

    /// <summary>Quais outros jogos também dependem de um ajuste (para não desfazer o que outro jogo ainda usa).</summary>
    public bool UsedByOthers(string tweakId, string exceptGameId)
    {
        lock (_lock) return _data.Settings.Any(kv => kv.Key != exceptGameId && kv.Value.AppliedTweaks.Contains(tweakId));
    }

    public IReadOnlyList<ManualGame> ManualGames()
    {
        lock (_lock) return _data.Manual.ToList();
    }

    public void AddManual(string name, string exePath)
    {
        lock (_lock)
        {
            if (_data.Manual.Any(m => m.ExePath.Equals(exePath, StringComparison.OrdinalIgnoreCase))) return;
            _data.Manual.Add(new ManualGame { Name = name, ExePath = exePath });
            Save();
        }
    }

    public void RemoveManual(string exePath)
    {
        lock (_lock)
        {
            if (_data.Manual.RemoveAll(m => m.ExePath.Equals(exePath, StringComparison.OrdinalIgnoreCase)) > 0) Save();
        }
    }

    public static string ManualId(string exePath) => "manual:" + exePath.ToLowerInvariant();
}
