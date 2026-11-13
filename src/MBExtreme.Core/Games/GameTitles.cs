using System.Text.Json;
using System.Text.Json.Serialization;

namespace MBExtreme.Core.Games;

/// <summary>
/// Perfil próprio de um título (EA FC é diferente de Call of Duty): tipo de otimização, Reflex, anti-cheat, o que nunca fechar,
/// ajustes dentro do jogo por nível de hardware, dicas e avisos. Vem embutido no programa e pode ser atualizado pelo servidor.
/// </summary>
public sealed class GameTitle
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public List<string> Exe { get; set; } = new();
    public List<string> Names { get; set; } = new();
    public string Profile { get; set; } = "openworld";
    public bool Reflex { get; set; }
    public string AntiCheat { get; set; } = "";
    public List<string> KeepRunning { get; set; } = new();
    /// <summary>Prioridade alta de CPU permanente (pelo próprio Windows) enquanto o perfil estiver aplicado.</summary>
    public bool Priority { get; set; } = true;
    /// <summary>Desligar as otimizações de tela cheia do Windows (bom em jogos DirectX 9/11 antigos).</summary>
    public bool Fso { get; set; }
    public List<string> Low { get; set; } = new();
    public List<string> Mid { get; set; } = new();
    public List<string> High { get; set; } = new();
    public List<string> Tips { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    /// <summary>Só no servidor: true esconde um título embutido.</summary>
    public bool Disabled { get; set; }
}

/// <summary>O que o perfil do título traz para este PC: ajustes do jogo para o nível do hardware, dicas, avisos e extras de driver.</summary>
public sealed record GameTitlePlan(GameTitle Title, string TierName, IReadOnlyList<string> InGame, IReadOnlyList<string> Tips, IReadOnlyList<string> Warnings, IReadOnlyList<DriverSetting> DriverExtras);

public static class GameTitles
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
    };

    private static readonly string[] Profiles = { "competitive", "openworld", "racing", "light", "app" };
    private static readonly Lazy<IReadOnlyList<GameTitle>> BuiltinLazy = new(LoadBuiltin);
    private static volatile IReadOnlyList<GameTitle> _current = Array.Empty<GameTitle>();
    private static volatile bool _merged;

    /// <summary>Banco embutido no programa (Games/Data/game-titles.json).</summary>
    public static IReadOnlyList<GameTitle> Builtin => BuiltinLazy.Value;

    /// <summary>Banco em uso: embutido + o que veio do servidor (o servidor manda quando tem a mesma chave).</summary>
    public static IReadOnlyList<GameTitle> All => _merged ? _current : Builtin;

    private static IReadOnlyList<GameTitle> LoadBuiltin()
    {
        using var stream = typeof(GameTitles).Assembly.GetManifestResourceStream("MBExtreme.Games.titles.json");
        if (stream is null) return Array.Empty<GameTitle>();
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>Lê e limpa uma lista de títulos (do programa ou do servidor). Itens inválidos são descartados; nunca lança.</summary>
    public static IReadOnlyList<GameTitle> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 2_000_000) return Array.Empty<GameTitle>();
        List<GameTitle>? raw;
        try { raw = JsonSerializer.Deserialize<List<GameTitle>>(json, Json); }
        catch (JsonException) { return Array.Empty<GameTitle>(); }
        if (raw is null) return Array.Empty<GameTitle>();
        var result = new List<GameTitle>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in raw.Take(500))
        {
            if (t is null) continue;
            t.Key = Clean(t.Key, 40).ToLowerInvariant();
            if (t.Key.Length == 0 || !t.Key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') || !seen.Add(t.Key)) continue;
            t.Title = Clean(t.Title, 80);
            if (t.Title.Length == 0 && !t.Disabled) continue;
            t.Exe = (t.Exe ?? new()).Select(e => Clean(e, 80)).Where(ExeGuesser.IsValidExeName).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList();
            t.Names = List(t.Names, 80, 8).Where(n => n.Length >= 3).ToList();
            t.Profile = Profiles.Contains(t.Profile?.Trim().ToLowerInvariant()) ? t.Profile!.Trim().ToLowerInvariant() : "openworld";
            t.AntiCheat = Clean(t.AntiCheat, 60);
            t.KeepRunning = List(t.KeepRunning, 60, 16).Select(p => p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p[..^4] : p)
                .Where(p => p.Length > 0 && p.IndexOfAny(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' }) < 0).ToList();
            t.Low = List(t.Low, 240, 10);
            t.Mid = List(t.Mid, 240, 10);
            t.High = List(t.High, 240, 10);
            t.Tips = List(t.Tips, 400, 8);
            t.Warnings = List(t.Warnings, 400, 6);
            if (!t.Disabled && t.Exe.Count == 0 && t.Names.Count == 0) continue;
            result.Add(t);
        }
        return result;
    }

    private static string Clean(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var chars = s.Trim().Where(c => !char.IsControl(c)).ToArray();
        string clean = new string(chars);
        return clean.Length > max ? clean[..max] : clean;
    }

    private static List<string> List(List<string>? items, int max, int count) =>
        (items ?? new()).Select(i => Clean(i, max)).Where(i => i.Length > 0).Take(count).ToList();

    public static string ToJson(IEnumerable<GameTitle> titles) => JsonSerializer.Serialize(titles, Json);

    /// <summary>Junta o embutido com o do servidor: mesma chave = o do servidor vale; Disabled esconde.</summary>
    public static IReadOnlyList<GameTitle> Merge(IReadOnlyList<GameTitle> builtin, IReadOnlyList<GameTitle> remote)
    {
        var map = new Dictionary<string, GameTitle>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var t in builtin.Concat(remote))
        {
            if (!map.ContainsKey(t.Key)) order.Add(t.Key);
            map[t.Key] = t;
        }
        return order.Select(k => map[k]).Where(t => !t.Disabled).ToList();
    }

    /// <summary>Usa o banco do servidor (já lido do cache ou baixado agora).</summary>
    public static void UseRemote(IReadOnlyList<GameTitle> remote)
    {
        _current = Merge(Builtin, remote);
        _merged = true;
    }

    /// <summary>Acha o título pelo executável; senão pelo nome do jogo na loja (palavra inteira no começo do nome).</summary>
    public static GameTitle? Find(string? gameName, string? exeName) => Find(All, gameName, exeName);

    public static GameTitle? Find(IReadOnlyList<GameTitle> titles, string? gameName, string? exeName)
    {
        if (!string.IsNullOrWhiteSpace(exeName))
        {
            string exe = Path.GetFileName(exeName.Trim());
            if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exe += ".exe";
            var byExe = titles.FirstOrDefault(t => t.Exe.Contains(exe, StringComparer.OrdinalIgnoreCase));
            if (byExe is not null) return byExe;
        }
        if (string.IsNullOrWhiteSpace(gameName)) return null;
        string name = Normalize(gameName);
        return titles
            .SelectMany(t => t.Names.Select(n => (Title: t, Name: Normalize(n))))
            .Where(x => x.Name.Length > 0 && (name == x.Name || name.StartsWith(x.Name + " ", StringComparison.Ordinal)))
            .OrderByDescending(x => x.Name.Length)
            .Select(x => x.Title)
            .FirstOrDefault();
    }

    private static string Normalize(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in s.ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Nível do hardware (0 entrada … 4 muito forte) para os três grupos de ajustes do jogo.</summary>
    public static (string Name, IReadOnlyList<string> Items) ForTier(GameTitle t, int tier) => tier switch
    {
        <= 1 => ("PC de entrada/básico", t.Low.Count > 0 ? t.Low : t.Mid),
        2 => ("PC intermediário", t.Mid.Count > 0 ? t.Mid : t.Low),
        _ => ("PC forte", t.High.Count > 0 ? t.High : t.Mid),
    };

    // configurações de driver NVIDIA (identificadores do CustomSettingNames.xml do NVIDIA Profile Inspector, MIT)
    public const uint UltraLowLatencyCpl = 0x0005F543;
    public const uint UltraLowLatencyEnabled = 0x10835000;
    public const uint BackgroundFpsLimit = 0x10835005;

    /// <summary>Monta o plano do título para este PC. Jogo competitivo sem Reflex: baixa latência do driver (NVIDIA).</summary>
    public static GameTitlePlan Plan(GameTitle t, GpuMaker maker, int tier)
    {
        var (tierName, inGame) = ForTier(t, tier);
        var tips = t.Tips.ToList();
        var warnings = t.Warnings.ToList();
        var extras = new List<DriverSetting>();
        if (maker == GpuMaker.Nvidia)
        {
            if (t.Profile == "competitive" && !t.Reflex)
            {
                extras.Add(new DriverSetting(UltraLowLatencyEnabled, 1, "Baixa latência ultra (driver)", "Ligada",
                    "Este jogo não tem NVIDIA Reflex: o driver segura a fila de quadros para o comando chegar mais rápido na tela.", false));
                extras.Add(new DriverSetting(UltraLowLatencyCpl, 2, "Modo de baixa latência (painel NVIDIA)", "Ultra",
                    "Mostra \"Ultra\" no Painel de Controle da NVIDIA para este jogo.", false));
            }
            else if (t.Reflex)
                tips.Insert(0, "Este jogo tem NVIDIA Reflex: ligue em \"Ativado + Boost\" no menu de vídeo do jogo (ele substitui a baixa latência do driver).");
            extras.Add(new DriverSetting(BackgroundFpsLimit, 20, "FPS em segundo plano (driver)", "20 FPS",
                "Quando você sai do jogo (Alt+Tab), a placa trabalha menos e esfria; volta ao normal ao voltar para o jogo. Atrapalha quem assiste o jogo num segundo monitor.", true));
        }
        else if (maker == GpuMaker.Amd && t.Profile == "competitive")
            tips.Insert(0, t.Reflex ? "AMD: ligue o AMD Anti-Lag 2 no jogo, se aparecer, ou o Anti-Lag no AMD Software para este jogo." : "AMD: ligue o Anti-Lag no AMD Software para este jogo.");
        if (t.AntiCheat.Length > 0)
            warnings.Add($"Anti-cheat {t.AntiCheat}: o MB Extreme só mexe no Windows e no driver da placa, nunca nos arquivos ou na memória do jogo.");
        return new GameTitlePlan(t, tierName, inGame, tips, warnings, extras);
    }
}

/// <summary>Atualização do banco de títulos pelo servidor (Firestore, documento público config/gameProfiles, campo "json").</summary>
public static class GameTitleSync
{
    public static string CachePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "game-titles.json");

    public static string Url(string projectId, string databaseId = "(default)") =>
        $"https://firestore.googleapis.com/v1/projects/{Uri.EscapeDataString(projectId)}/databases/{Uri.EscapeDataString(databaseId).Replace("%28", "(").Replace("%29", ")")}/documents/config/gameProfiles";

    /// <summary>Tira o texto JSON da resposta da API REST do Firestore (fields.json.stringValue).</summary>
    public static string? ExtractJson(string response)
    {
        try
        {
            using var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("fields", out var fields) && fields.TryGetProperty("json", out var json) &&
                json.TryGetProperty("stringValue", out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }
        catch (JsonException) { }
        return null;
    }

    /// <summary>Usa o cache do disco (rápido, sem internet).</summary>
    public static void LoadCache()
    {
        try
        {
            if (File.Exists(CachePath)) GameTitles.UseRemote(GameTitles.Parse(File.ReadAllText(CachePath)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Baixa o banco do servidor; se vier válido, guarda no cache e passa a usar. Devolve quantos títulos chegaram (-1 = falhou).</summary>
    public static async Task<int> RefreshAsync(HttpClient http, string projectId, string databaseId, CancellationToken stop = default)
    {
        try
        {
            using var response = await http.GetAsync(Url(projectId, databaseId), stop).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return 0;
            if (!response.IsSuccessStatusCode) return -1;
            string body = await response.Content.ReadAsStringAsync(stop).ConfigureAwait(false);
            if (body.Length > 3_000_000) return -1;
            string? json = ExtractJson(body);
            if (json is null) return -1;
            var remote = GameTitles.Parse(json);
            GameTitles.UseRemote(remote);
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            await File.WriteAllTextAsync(CachePath, GameTitles.ToJson(remote), stop).ConfigureAwait(false);
            return remote.Count;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException) { return -1; }
    }
}
