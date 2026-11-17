using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace MBExtreme.Core.Network;

/// <summary>
/// Catálogo de servidores para o ping de jogos. Inclui as regiões de nuvem onde os jogos ficam (AWS e Oracle, endereços
/// públicos de cada região) e as listas públicas que mudam com o tempo: os relays da Valve (CS2, Dota 2), lidos da própria
/// Steam, e os servidores do LibreSpeed. Também calcula a distância até você e o mínimo de ping que a distância permite.
/// </summary>
public static class GameServers
{
    public const string Brazil = "Brasil";
    public const string SouthAmerica = "América do Sul";
    public const string NorthAmerica = "América do Norte";
    public const string Europe = "Europa";
    public const string Other = "Ásia e outros";

    public static IReadOnlyList<string> Regions { get; } = new[] { Brazil, SouthAmerica, NorthAmerica, Europe, Other };

    /// <summary>Endereço público da Steam com os relays da Valve (Steam Datagram Relay) por cidade.</summary>
    public const string ValveSdrUrl = "https://api.steampowered.com/ISteamApps/GetSDRConfig/v1/?appid=730";

    private static PingTarget Aws(string region, string name, string group, double lat, double lon, string note = "") =>
        new("aws-" + region, name, group, $"dynamodb.{region}.amazonaws.com", lat, lon, note, "AWS");

    private static PingTarget Oracle(string region, string name, string group, double lat, double lon) =>
        new("oci-" + region, name, group, $"objectstorage.{region}.oraclecloud.com", lat, lon, "", "Oracle");

    /// <summary>Servidores fixos (regiões de nuvem e referências). Os da Valve e do LibreSpeed entram pela internet.</summary>
    public static IReadOnlyList<PingTarget> Static { get; } = new[]
    {
        Aws("sa-east-1", "São Paulo", Brazil, -23.55, -46.63, "Fortnite, LoL, Valorant, Free Fire e muitos outros"),
        Oracle("sa-saopaulo-1", "São Paulo", Brazil, -23.55, -46.63),
        Oracle("sa-vinhedo-1", "Vinhedo (SP)", Brazil, -23.03, -46.98),
        new PingTarget("cf-anycast", "Cloudflare (mais perto)", Brazil, "1.1.1.1", double.NaN, double.NaN, "Referência: o servidor da Cloudflare mais perto de você", "Referência"),
        new PingTarget("google-anycast", "Google (mais perto)", Brazil, "8.8.8.8", double.NaN, double.NaN, "Referência: o servidor do Google mais perto de você", "Referência"),
        Oracle("sa-santiago-1", "Santiago (Chile)", SouthAmerica, -33.45, -70.67),
        Oracle("sa-valparaiso-1", "Valparaíso (Chile)", SouthAmerica, -33.05, -71.62),
        Oracle("sa-bogota-1", "Bogotá (Colômbia)", SouthAmerica, 4.71, -74.07),
        Aws("us-east-1", "Virgínia (EUA)", NorthAmerica, 38.9, -77.4),
        Aws("us-east-2", "Ohio (EUA)", NorthAmerica, 40.0, -83.0),
        Aws("us-west-1", "Califórnia (EUA)", NorthAmerica, 37.4, -121.9),
        Aws("us-west-2", "Oregon (EUA)", NorthAmerica, 45.6, -121.2),
        Aws("ca-central-1", "Montreal (Canadá)", NorthAmerica, 45.5, -73.6),
        Aws("mx-central-1", "Querétaro (México)", NorthAmerica, 20.6, -100.4),
        Oracle("us-ashburn-1", "Ashburn (EUA)", NorthAmerica, 39.04, -77.49),
        Aws("eu-west-1", "Irlanda", Europe, 53.3, -6.3),
        Aws("eu-west-2", "Londres", Europe, 51.5, -0.1),
        Aws("eu-central-1", "Frankfurt", Europe, 50.1, 8.7),
        Aws("eu-south-2", "Espanha", Europe, 41.6, -0.9),
        Aws("ap-northeast-1", "Tóquio", Other, 35.7, 139.7),
        Aws("ap-southeast-1", "Singapura", Other, 1.35, 103.8),
        Aws("af-south-1", "Cidade do Cabo", Other, -33.9, 18.4),
        Aws("me-central-1", "Emirados Árabes", Other, 25.2, 55.3),
    };

    /// <summary>Provedores, na ordem dos filtros da tela.</summary>
    public static IReadOnlyList<string> Providers { get; } = new[] { "Valve", "AWS", "Oracle", "LibreSpeed", "Referência" };

    // ------------------------------------------------------------------ região e distância

    private static readonly List<(double Lon, double Lat)[]> BrazilRings = NetMap.Rings(WorldMapData.Brazil).ToList();

    /// <summary>O ponto está dentro do contorno do Brasil?</summary>
    public static bool InBrazil(double lat, double lon) => BrazilRings.Any(r => Inside(r, lat, lon));

    private static bool Inside((double Lon, double Lat)[] ring, double lat, double lon)
    {
        bool inside = false;
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
        {
            var (xi, yi) = ring[i];
            var (xj, yj) = ring[j];
            if ((yi > lat) != (yj > lat) && lon < (xj - xi) * (lat - yi) / (yj - yi) + xi) inside = !inside;
        }
        return inside;
    }

    /// <summary>Região do filtro pela posição.</summary>
    public static string RegionOf(double lat, double lon)
    {
        if (double.IsNaN(lat) || double.IsNaN(lon)) return Brazil;
        if (InBrazil(lat, lon)) return Brazil;
        if (lat < 13 && lon is > -92 and < -30) return SouthAmerica;
        if (lat >= 13 && lon < -50) return NorthAmerica;
        if (lat > 34 && lon is > -25 and < 45) return Europe;
        return Other;
    }

    /// <summary>Distância em km entre dois pontos (fórmula de haversine).</summary>
    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double r = 6371;
        double dLat = (lat2 - lat1) * Math.PI / 180, dLon = (lon2 - lon1) * Math.PI / 180;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * r * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    /// <summary>Menor ping possível pela distância: a luz na fibra anda ~200 km por ms, ida e volta = 1 ms a cada 100 km.</summary>
    public static double MinimumPingMs(double km) => km / 100.0;

    /// <summary>Os servidores mais perto de você: as referências (sem posição) primeiro, depois por distância.</summary>
    public static IReadOnlyList<PingTarget> Nearest(IEnumerable<PingTarget> targets, double lat, double lon, int count)
    {
        var list = targets.ToList();
        var anycast = list.Where(t => double.IsNaN(t.Lat)).ToList();
        var placed = list.Where(t => !double.IsNaN(t.Lat))
            .GroupBy(t => t.Key).Select(g => g.First())
            .OrderBy(t => DistanceKm(lat, lon, t.Lat, t.Lon)).Take(Math.Max(0, count - anycast.Count));
        return anycast.Concat(placed).ToList();
    }

    /// <summary>Nível de cor do ping: 0 verde (até 30 ms), 1 amarelo (até 60), 2 laranja (até 100), 3 vermelho (mais ou sem resposta).</summary>
    public static int Level(double? ms) => ms switch
    {
        null => 3,
        <= 30 => 0,
        <= 60 => 1,
        <= 100 => 2,
        _ => 3,
    };

    /// <summary>Frase curta sobre o ping comparado com o mínimo pela distância.</summary>
    public static string Explain(string name, double ms, double km)
    {
        double min = MinimumPingMs(km);
        string verdict = NetMap.GameVerdict(ms).ToLowerInvariant();
        if (km < 150) return $"{name} está a {ms:0} ms: {verdict}.";
        double extra = ms - min;
        return extra > min && extra > 15
            ? $"{name} está a {ms:0} ms: {verdict}. Pela distância ({km:N0} km), o mínimo seria uns {min:0} ms; o resto é o caminho que o provedor usa."
            : $"{name} está a {ms:0} ms: {verdict}. Perto do mínimo possível pela distância ({min:0} ms): a rota do provedor está boa.";
    }

    // ------------------------------------------------------------------ Valve (relays da Steam)

    /// <summary>Lê o JSON da Steam: um alvo por cidade (o primeiro relay com IPv4), com a posição que a própria Steam informa.</summary>
    public static IReadOnlyList<PingTarget> ParseValveSdr(string json)
    {
        var result = new List<PingTarget>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("pops", out var pops) || pops.ValueKind != JsonValueKind.Object) return result;
            foreach (var pop in pops.EnumerateObject())
            {
                var v = pop.Value;
                if (!v.TryGetProperty("relays", out var relays) || relays.ValueKind != JsonValueKind.Array) continue;
                string? ip = relays.EnumerateArray()
                    .Select(r => r.TryGetProperty("ipv4", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null)
                    .FirstOrDefault(a => System.Net.IPAddress.TryParse(a, out var parsed) && parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                if (ip is null) continue;
                double lat = double.NaN, lon = double.NaN;
                if (v.TryGetProperty("geo", out var geo) && geo.ValueKind == JsonValueKind.Array && geo.GetArrayLength() >= 2)
                {
                    lon = geo[0].GetDouble();
                    lat = geo[1].GetDouble();
                }
                if (double.IsNaN(lat)) continue;
                string desc = v.TryGetProperty("desc", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() ?? pop.Name : pop.Name;
                result.Add(new PingTarget("valve-" + pop.Name, ValveName(desc), RegionOf(lat, lon), ip, lat, lon, "CS2, Dota 2 e outros jogos da Valve", "Valve", Icmp: true));
            }
        }
        catch (JsonException) { }
        catch (InvalidOperationException) { }
        catch (FormatException) { }
        return result;
    }

    /// <summary>"Sao Paulo (Brazil)" → "São Paulo (Brazil)" com os acentos das cidades brasileiras mais comuns.</summary>
    private static string ValveName(string desc) => desc
        .Replace("Sao Paulo", "São Paulo", StringComparison.OrdinalIgnoreCase)
        .Replace("(Brazil)", "", StringComparison.OrdinalIgnoreCase).Trim();

    public static async Task<IReadOnlyList<PingTarget>> ValveAsync(HttpClient http, CancellationToken ct)
    {
        try { return ParseValveSdr(await http.GetStringAsync(ValveSdrUrl, ct)); }
        catch (HttpRequestException) { return Array.Empty<PingTarget>(); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return Array.Empty<PingTarget>(); }
    }

    // ------------------------------------------------------------------ LibreSpeed (servidores de provedores e voluntários)

    /// <summary>Cidades conhecidas para posicionar no mapa os servidores do LibreSpeed (o nome da lista traz a cidade).</summary>
    public static readonly IReadOnlyDictionary<string, (double Lat, double Lon)> Cities = BuildCities();

    private static Dictionary<string, (double, double)> BuildCities()
    {
        var d = new Dictionary<string, (double, double)>(StringComparer.Ordinal);
        foreach (var c in NetMap.Colos.Values) d[Fold(c.Name)] = (c.Lat, c.Lon);
        foreach (var (name, lat, lon) in new[]
        {
            ("Sao Paulo", -23.55, -46.63), ("Rio de Janeiro", -22.91, -43.17), ("Belo Horizonte", -19.92, -43.94), ("Brasilia", -15.79, -47.88),
            ("Curitiba", -25.43, -49.27), ("Porto Alegre", -30.03, -51.23), ("Florianopolis", -27.6, -48.55), ("Salvador", -12.97, -38.5),
            ("Recife", -8.05, -34.88), ("Fortaleza", -3.73, -38.52), ("Natal", -5.79, -35.21), ("Joao Pessoa", -7.12, -34.86),
            ("Maceio", -9.67, -35.74), ("Aracaju", -10.91, -37.07), ("Teresina", -5.09, -42.8), ("Sao Luis", -2.53, -44.3),
            ("Belem", -1.46, -48.5), ("Manaus", -3.12, -60.02), ("Porto Velho", -8.76, -63.9), ("Rio Branco", -9.97, -67.81),
            ("Boa Vista", 2.82, -60.67), ("Macapa", 0.03, -51.07), ("Palmas", -10.18, -48.33), ("Goiania", -16.68, -49.25),
            ("Cuiaba", -15.6, -56.1), ("Campo Grande", -20.47, -54.62), ("Vitoria", -20.32, -40.34), ("Campinas", -22.91, -47.06),
            ("Santos", -23.96, -46.33), ("Ribeirao Preto", -21.18, -47.81), ("Sorocaba", -23.5, -47.46), ("Londrina", -23.31, -51.16),
            ("Maringa", -23.42, -51.94), ("Joinville", -26.3, -48.85), ("Blumenau", -26.92, -49.07), ("Caxias do Sul", -29.17, -51.18),
            ("Uberlandia", -18.92, -48.28), ("Juiz de Fora", -21.76, -43.35), ("Feira de Santana", -12.27, -38.97), ("Campina Grande", -7.23, -35.88),
            ("Buenos Aires", -34.6, -58.38), ("Santiago", -33.45, -70.67), ("Montevideo", -34.9, -56.16), ("Asuncion", -25.26, -57.58),
            ("Lima", -12.05, -77.04), ("Bogota", 4.71, -74.07), ("Miami", 25.76, -80.19),
        })
            d[Fold(name)] = (lat, lon);
        return d;
    }

    /// <summary>Minúsculas sem acento, para comparar nomes de cidade.</summary>
    public static string Fold(string text)
    {
        var normalized = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (char c in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString();
    }

    /// <summary>Posição pela cidade escrita no nome do servidor (o nome mais longo que bater, para "São Paulo" ganhar de "Paulo").</summary>
    public static (double Lat, double Lon)? Locate(string name)
    {
        string folded = Fold(name);
        var hit = Cities.Where(c => folded.Contains(c.Key, StringComparison.Ordinal)).OrderByDescending(c => c.Key.Length).FirstOrDefault();
        return hit.Key is null ? null : hit.Value;
    }

    /// <summary>Servidores do LibreSpeed com cidade conhecida viram alvos de ping (conexão HTTPS ao próprio servidor).</summary>
    public static IReadOnlyList<PingTarget> FromLibreSpeed(IEnumerable<LibreServer> servers)
    {
        var result = new List<PingTarget>();
        foreach (var s in servers)
        {
            if (Locate(s.Name) is not { } at) continue;
            if (!Uri.TryCreate(s.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) continue;
            string name = s.Name.Replace(", Brazil", "", StringComparison.OrdinalIgnoreCase).Replace("Brazil", "", StringComparison.OrdinalIgnoreCase).Trim(' ', ',', '-');
            result.Add(new PingTarget("libre-" + s.Id, name, RegionOf(at.Lat, at.Lon), uri.Host, at.Lat, at.Lon,
                s.Sponsor.Length > 0 ? "Servidor de " + s.Sponsor : "Servidor público do LibreSpeed", "LibreSpeed"));
        }
        return result;
    }
}
