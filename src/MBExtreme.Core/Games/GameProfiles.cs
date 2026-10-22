using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MBExtreme.Core.Games;

/// <summary>Tipo de jogo (ou programa) que define o pacote de otimização.</summary>
public sealed record GameProfile(string Key, string Title, string Description, IReadOnlyList<string> TweakIds);

public enum GpuMaker { Unknown, Nvidia, Amd, Intel }

public sealed record OptimizationPlan(GameProfile Profile, IReadOnlyList<string> TweakIds, IReadOnlyList<string> Advice);

/// <summary>Pacotes de otimização por tipo de jogo. Só entram ajustes de risco baixo, reversíveis; os outros ficam como dica.</summary>
public static class GameProfiles
{
    public static readonly GameProfile Competitive = new("competitive", "Competitivo (FPS, battle royale, eSports)",
        "Resposta mínima: prioridade de CPU e GPU, sem gravação em segundo plano, energia sem economia, USB sem suspensão e mouse sem aceleração.",
        new[] { "game-dvr", "game-mmcss", "perf-priority-separation", "power-min-cpu", "power-usb-suspend", "periph-mouse-accel", "game-sysresponsiveness" });

    public static readonly GameProfile OpenWorld = new("openworld", "Mundo aberto e AAA",
        "Estabilidade de quadros em jogos pesados: prioridade de CPU/GPU, plano de energia de desempenho máximo e sem gravação em segundo plano.",
        new[] { "game-dvr", "game-mmcss", "perf-priority-separation", "perf-power-ultimate", "game-sysresponsiveness" });

    public static readonly GameProfile Racing = new("racing", "Corrida e simulação",
        "Tempo de quadro consistente e periféricos sem atraso: prioridade, PCI Express e USB sem economia de energia.",
        new[] { "game-dvr", "game-mmcss", "perf-priority-separation", "power-pcie-aspm", "power-usb-suspend", "game-fse" });

    public static readonly GameProfile Light = new("light", "Jogo leve (indie, MOBA, casual)",
        "O mínimo necessário: prioridade para o jogo e sem gravação em segundo plano. Não mexe em energia nem em periféricos.",
        new[] { "game-dvr", "game-mmcss" });

    public static readonly GameProfile App = new("app", "Programa (edição, criação, transmissão)",
        "Mais CPU para o programa em uso e plano de energia de desempenho máximo. Não altera nada específico de jogos.",
        new[] { "perf-priority-separation", "perf-power-ultimate" });

    public static IReadOnlyList<GameProfile> All { get; } = new[] { Competitive, OpenWorld, Racing, Light, App };

    public static GameProfile Find(string? key) => All.FirstOrDefault(p => p.Key == key) ?? OpenWorld;

    private static readonly HashSet<string> KnownCompetitive = new(StringComparer.OrdinalIgnoreCase)
    {
        "cs2", "valorant", "valorant-win64-shipping", "r5apex", "fortniteclient-win64-shipping", "overwatch", "rainbowsix", "rainbowsix_vulkan",
        "tslgame", "leagueclient", "league of legends", "marvelrivals", "pointblank", "bf4", "bf2042", "bf6", "cod", "codbo6", "blackopscoldwar", "warzone",
    };

    private static readonly HashSet<string> KnownApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "obs64", "premiere", "afterfx", "resolve", "blender", "davinci", "photoshop", "ableton", "fl64", "studio one", "handbrake", "vegas",
    };

    /// <summary>Escolhe o tipo de jogo pelo nome do executável e, se houver, pelos gêneros e categorias da loja (em português ou inglês).</summary>
    public static GameProfile Classify(string? exeName, IEnumerable<string>? genres, IEnumerable<string>? categories)
    {
        string name = Path.GetFileNameWithoutExtension(exeName ?? "");
        if (name.Length > 0 && KnownCompetitive.Contains(name)) return Competitive;
        if (name.Length > 0 && KnownApps.Contains(name)) return App;

        var g = (genres ?? Array.Empty<string>()).Select(x => x.ToLowerInvariant()).ToList();
        var c = (categories ?? Array.Empty<string>()).Select(x => x.ToLowerInvariant()).ToList();
        bool Has(IEnumerable<string> list, params string[] words) => list.Any(x => words.Any(x.Contains));

        bool pvp = Has(c, "pvp", "multijogador", "multi-player", "multiplayer", "online co-op") && Has(g, "ação", "acao", "action", "esportes", "sports");
        if (pvp && !Has(g, "rpg", "aventura", "adventure", "simulação", "simulation")) return Competitive;
        if (Has(g, "corrida", "racing", "simulação", "simulation", "esportes", "sports")) return Racing;
        if (Has(g, "utilitários", "utilities", "design", "edição de vídeo", "video production", "produção de áudio", "audio production", "animação", "animation")) return App;
        bool indieLike = Has(g, "indie", "casual", "estratégia", "strategy", "quebra-cabeça", "puzzle", "cartas", "card");
        if (indieLike && !Has(g, "rpg")) return Light;
        if (Has(g, "ação", "action", "aventura", "adventure", "rpg")) return OpenWorld;
        return OpenWorld;
    }
}

/// <summary>Monta o plano de otimização: ajustes do Windows do pacote + dicas de configuração do jogo para a marca da placa de vídeo.</summary>
public static class GameOptimizer
{
    public static OptimizationPlan Plan(GameProfile profile, GpuMaker maker, int tier, bool onHdd)
    {
        var tweaks = profile.TweakIds.ToList();
        var advice = new List<string>();

        // dicas por marca (configurações do jogo, que o app não altera sozinho)
        switch (maker)
        {
            case GpuMaker.Nvidia:
                advice.Add(profile.Key == "competitive"
                    ? "NVIDIA: ligue o NVIDIA Reflex (se o jogo tiver) em \"Ativado + Boost\" e use DLSS no modo Desempenho só se precisar de FPS."
                    : "NVIDIA: use DLSS em \"Qualidade\" (ou \"Equilibrado\" em 4K) e ligue o Reflex se existir. No Painel de Controle da NVIDIA, modo de gerenciamento de energia: \"Preferir desempenho máximo\" para este jogo.");
                break;
            case GpuMaker.Amd:
                advice.Add(profile.Key == "competitive"
                    ? "AMD: ligue o AMD Anti-Lag no AMD Software para este jogo e use FSR em \"Qualidade\" só se precisar de FPS."
                    : "AMD: use FSR em \"Qualidade\" (ou \"Equilibrado\" em 4K). Se houver quedas de clock ou piscadas, veja \"AMD: desativar o ULPS\" em Jogos e GPU.");
                tweaks.Add("game-amd-telemetry-off");
                break;
            case GpuMaker.Intel:
                advice.Add("Intel: use XeSS no modo Qualidade se o jogo tiver. Em gráficos integrados, abaixe sombras e distância de visão antes da resolução, e mantenha a memória em dois pentes (dual channel).");
                break;
        }

        advice.Add(tier switch
        {
            <= 0 => "Seu hardware é de entrada: jogue em 720p ou 900p, texturas médias, sombras e efeitos no mínimo.",
            1 => "Hardware básico: 1080p em Médio, sombras e efeitos em Baixo; limite o FPS em 60 para evitar picos de calor.",
            2 => "Hardware intermediário: 1080p em Alto é o ponto ideal; reflexos e sombras em Médio se o FPS cair.",
            3 => "Hardware forte: 1440p em Alto, ou 1080p em Ultra com FPS alto.",
            _ => "Hardware muito forte: 4K em Alto ou 1440p em Ultra; ligue ray tracing se o jogo tiver e o FPS ficar acima da sua meta.",
        });

        if (profile.Key == "competitive") advice.Add("Competitivo: desligue V-Sync, limite o FPS um pouco abaixo da taxa do monitor se houver rasgos, e prefira \"tela cheia\" (ou sem borda com Reflex/Anti-Lag).");
        if (profile.Key == "racing") advice.Add("Simulação: mantenha o FPS estável (limite) em vez do máximo; volante e pedais pedem USB sem economia de energia, que já está no pacote.");
        if (profile.Key == "app") advice.Add("Para edição e criação, feche o navegador e o launcher dos jogos; use o disco mais rápido para o cache e os arquivos de projeto.");
        if (onHdd && profile.Key != "app") advice.Add("O jogo está num HD: mova para um SSD para reduzir travadas ao carregar o mundo.");

        return new OptimizationPlan(profile, tweaks.Distinct().ToList(), advice);
    }
}

/// <summary>Informações do jogo na loja da Steam (gêneros, categorias e requisitos), com cache em disco.</summary>
public sealed record SteamStoreInfo(string AppId, string Name, IReadOnlyList<string> Genres, IReadOnlyList<string> Categories, string ShortDescription, string MinimumRequirements, string RecommendedRequirements);

public static class SteamStore
{
    public static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        string text = Regex.Replace(html, "<br\\s*/?>|</li>|</p>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", "");
        text = System.Net.WebUtility.HtmlDecode(text);
        return Regex.Replace(text, "[ \\t]+", " ").Replace("\n ", "\n").Trim();
    }

    public static SteamStoreInfo? Parse(string json, string appId)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(appId, out var entry) || !entry.TryGetProperty("success", out var ok) || !ok.GetBoolean()) return null;
            var data = entry.GetProperty("data");
            string Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            IReadOnlyList<string> List(string n) => data.TryGetProperty(n, out var arr) && arr.ValueKind == JsonValueKind.Array
                ? arr.EnumerateArray().Select(x => Str(x, "description")).Where(x => x.Length > 0).ToList()
                : Array.Empty<string>();
            string Req(string n) => data.TryGetProperty("pc_requirements", out var r) && r.ValueKind == JsonValueKind.Object ? StripHtml(Str(r, n)) : "";
            return new SteamStoreInfo(appId, Str(data, "name"), List("genres"), List("categories"), StripHtml(Str(data, "short_description")), Req("minimum"), Req("recommended"));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return null;
        }
    }

    public static async Task<SteamStoreInfo?> GetAsync(HttpClient http, string appId, string cacheFolder, CancellationToken stop = default)
    {
        if (!appId.All(char.IsDigit) || appId.Length is 0 or > 12) return null;
        string cache = Path.Combine(cacheFolder, appId + ".json");
        try
        {
            if (File.Exists(cache) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cache) < TimeSpan.FromDays(30))
                return Parse(await File.ReadAllTextAsync(cache, stop), appId);
            string json = await http.GetStringAsync($"https://store.steampowered.com/api/appdetails?appids={appId}&l=brazilian", stop);
            var info = Parse(json, appId);
            if (info is not null)
            {
                Directory.CreateDirectory(cacheFolder);
                await File.WriteAllTextAsync(cache, json, stop);
            }
            return info;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>Capa vertical (600x900) dos jogos da Steam: usa a que a Steam já guardou no PC e só baixa se faltar.</summary>
public static class GameCovers
{
    public static string? FindSteamLocal(string steamPath, string appId)
    {
        try
        {
            string cache = Path.Combine(steamPath, "appcache", "librarycache");
            if (!Directory.Exists(cache)) return null;
            foreach (string name in new[] { $"{appId}_library_600x900.jpg", $"{appId}_library_600x900.png" })
            {
                string old = Path.Combine(cache, name);
                if (File.Exists(old)) return old;
            }
            string folder = Path.Combine(cache, appId);   // formato novo: uma pasta por jogo, com arquivos de nome aleatório
            if (Directory.Exists(folder))
            {
                var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).ToList();
                return files.FirstOrDefault(f => Path.GetFileName(f).Contains("library_600x900", StringComparison.OrdinalIgnoreCase)) ??
                       files.Where(f => !Path.GetFileName(f).Contains("hero", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(f).Contains("logo", StringComparison.OrdinalIgnoreCase))
                            .OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return null;
    }

    public static async Task<string?> DownloadSteamAsync(HttpClient http, string appId, string cacheFolder, CancellationToken stop = default)
    {
        if (!appId.All(char.IsDigit) || appId.Length is 0 or > 12) return null;
        string file = Path.Combine(cacheFolder, appId + ".jpg");
        try
        {
            if (File.Exists(file)) return file;
            Directory.CreateDirectory(cacheFolder);
            byte[] data = await http.GetByteArrayAsync($"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_600x900.jpg", stop);
            if (data.Length < 1000) return null;
            await File.WriteAllBytesAsync(file, data, stop);
            return file;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
