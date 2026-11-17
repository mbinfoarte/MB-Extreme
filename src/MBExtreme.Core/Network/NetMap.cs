using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace MBExtreme.Core.Network;

/// <summary>Um ponto no mapa (cidade, servidor ou salto).</summary>
public sealed record GeoPoint(string Name, double Lat, double Lon);

/// <summary>Um servidor para medir o ping (região de nuvem onde muitos jogos online ficam hospedados).</summary>
/// <summary>Servidor para medir o ping. <paramref name="Icmp"/> = ping comum (servidores sem HTTPS, como os da Valve); senão, conexão TCP na porta 443.</summary>
public sealed record PingTarget(string Key, string Name, string Group, string Host, double Lat, double Lon, string Note = "", string Provider = "", bool Icmp = false);

/// <summary>Onde a pessoa está, segundo o endereço de IP público (aproximado: cidade do provedor).</summary>
public sealed record IpLocation(string Ip, string City, string Region, string Country, double Lat, double Lon, string Isp);

/// <summary>Resultado do ping para um servidor: mediana, variação e perdas.</summary>
public sealed record PingResult(PingTarget Target, double? Median, double? Jitter, int Lost, int Sent);

/// <summary>Limites do mapa em graus.</summary>
public readonly record struct MapBounds(double MinLon, double MaxLon, double MinLat, double MaxLat);

/// <summary>Cidades, servidores, localização pelo IP e as contas do mapa do teste de internet.</summary>
public static class NetMap
{
    /// <summary>Onde fica cada servidor da Cloudflare pelo código do aeroporto (os mais comuns para quem está no Brasil).</summary>
    public static readonly IReadOnlyDictionary<string, GeoPoint> Colos = new Dictionary<string, GeoPoint>(StringComparer.OrdinalIgnoreCase)
    {
        ["GRU"] = new("São Paulo", -23.43, -46.47), ["GIG"] = new("Rio de Janeiro", -22.81, -43.25), ["FOR"] = new("Fortaleza", -3.78, -38.53),
        ["POA"] = new("Porto Alegre", -29.99, -51.17), ["CWB"] = new("Curitiba", -25.53, -49.18), ["BSB"] = new("Brasília", -15.87, -47.92),
        ["SSA"] = new("Salvador", -12.91, -38.33), ["REC"] = new("Recife", -8.13, -34.92), ["BEL"] = new("Belém", -1.38, -48.48),
        ["BNU"] = new("Blumenau", -26.92, -49.07), ["CNF"] = new("Belo Horizonte", -19.63, -43.97), ["FLN"] = new("Florianópolis", -27.67, -48.55),
        ["GYN"] = new("Goiânia", -16.63, -49.22), ["MAO"] = new("Manaus", -3.04, -60.05), ["CGB"] = new("Cuiabá", -15.65, -56.12),
        ["VCP"] = new("Campinas", -23.01, -47.13), ["SJP"] = new("São José do Rio Preto", -20.82, -49.40), ["ITJ"] = new("Itajaí", -26.91, -48.66),
        ["JOI"] = new("Joinville", -26.22, -48.80), ["QWJ"] = new("Americana", -22.74, -47.33), ["UDI"] = new("Uberlândia", -18.88, -48.23),
        ["SJK"] = new("São José dos Campos", -23.23, -45.86), ["NVT"] = new("Navegantes", -26.88, -48.65), ["EZE"] = new("Buenos Aires", -34.82, -58.54),
        ["SCL"] = new("Santiago", -33.39, -70.79), ["LIM"] = new("Lima", -12.02, -77.11), ["BOG"] = new("Bogotá", 4.70, -74.15),
        ["MIA"] = new("Miami", 25.79, -80.29), ["IAD"] = new("Washington", 38.95, -77.46), ["ATL"] = new("Atlanta", 33.64, -84.43),
        ["DFW"] = new("Dallas", 32.90, -97.04), ["LAX"] = new("Los Angeles", 33.94, -118.41), ["EWR"] = new("Nova York", 40.69, -74.17),
        ["LIS"] = new("Lisboa", 38.77, -9.13), ["MAD"] = new("Madri", 40.49, -3.57), ["FRA"] = new("Frankfurt", 50.03, 8.57),
        ["LHR"] = new("Londres", 51.47, -0.45), ["AMS"] = new("Amsterdã", 52.31, 4.76), ["CDG"] = new("Paris", 49.01, 2.55),
    };

    /// <summary>
    /// Regiões de nuvem para medir o ping de jogo. São os endereços públicos da AWS por região (os mesmos que os sites de "ping da nuvem" usam);
    /// muitos jogos online hospedam servidores nessas regiões. Cada jogo tem os próprios servidores: isto mede a rota até a região.
    /// </summary>
    public static readonly IReadOnlyList<PingTarget> GameTargets = new PingTarget[]
    {
        new("aws-sa-east-1", "São Paulo", "Brasil", "dynamodb.sa-east-1.amazonaws.com", -23.55, -46.63, "A região mais usada pelos jogos para o Brasil"),
        new("cf-anycast", "Cloudflare (mais perto)", "Brasil", "1.1.1.1", double.NaN, double.NaN, "Referência: o servidor da Cloudflare mais perto de você"),
        new("google-anycast", "Google (mais perto)", "Brasil", "8.8.8.8", double.NaN, double.NaN, "Referência: o servidor do Google mais perto de você"),
        new("aws-us-east-1", "Virgínia (EUA)", "América do Norte", "dynamodb.us-east-1.amazonaws.com", 38.9, -77.4),
        new("aws-us-east-2", "Ohio (EUA)", "América do Norte", "dynamodb.us-east-2.amazonaws.com", 40.0, -83.0),
        new("aws-us-west-1", "Califórnia (EUA)", "América do Norte", "dynamodb.us-west-1.amazonaws.com", 37.4, -121.9),
        new("aws-us-west-2", "Oregon (EUA)", "América do Norte", "dynamodb.us-west-2.amazonaws.com", 45.6, -121.2),
        new("aws-ca-central-1", "Montreal (Canadá)", "América do Norte", "dynamodb.ca-central-1.amazonaws.com", 45.5, -73.6),
        new("aws-mx-central-1", "Querétaro (México)", "América do Norte", "dynamodb.mx-central-1.amazonaws.com", 20.6, -100.4),
        new("aws-eu-west-1", "Irlanda", "Europa", "dynamodb.eu-west-1.amazonaws.com", 53.3, -6.3),
        new("aws-eu-west-2", "Londres", "Europa", "dynamodb.eu-west-2.amazonaws.com", 51.5, -0.1),
        new("aws-eu-central-1", "Frankfurt", "Europa", "dynamodb.eu-central-1.amazonaws.com", 50.1, 8.7),
        new("aws-eu-south-2", "Espanha", "Europa", "dynamodb.eu-south-2.amazonaws.com", 41.6, -0.9),
        new("aws-ap-northeast-1", "Tóquio", "Ásia e outros", "dynamodb.ap-northeast-1.amazonaws.com", 35.7, 139.7),
        new("aws-ap-southeast-1", "Singapura", "Ásia e outros", "dynamodb.ap-southeast-1.amazonaws.com", 1.35, 103.8),
        new("aws-af-south-1", "Cidade do Cabo", "Ásia e outros", "dynamodb.af-south-1.amazonaws.com", -33.9, 18.4),
        new("aws-me-central-1", "Emirados Árabes", "Ásia e outros", "dynamodb.me-central-1.amazonaws.com", 25.2, 55.3),
    };

    public static IReadOnlyList<string> Groups { get; } = new[] { "Brasil", "América do Norte", "Europa", "Ásia e outros" };

    /// <summary>Lê a resposta do ipwho.is ou do ipapi.co (os dois serviços gratuitos de localização por IP usados aqui).</summary>
    public static IpLocation? ParseLocation(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False) return null;
            if (r.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.True) return null;
            string S(params string[] names) { foreach (var n in names) if (r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String) return v.GetString() ?? ""; return ""; }
            double? D(params string[] names) { foreach (var n in names) if (r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number) return v.GetDouble(); return null; }
            string isp = r.TryGetProperty("connection", out var c) && c.TryGetProperty("isp", out var i) ? i.GetString() ?? "" : S("org", "isp");
            if (D("latitude", "lat") is not double lat || D("longitude", "lon") is not double lon) return null;
            return new IpLocation(S("ip", "query"), S("city"), S("region", "regionName"), S("country", "country_name"), lat, lon, isp);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Pergunta onde está o IP público (primeiro ipwho.is, depois ipapi.co). Só a cidade aproximada do provedor; nada é guardado.</summary>
    public static async Task<IpLocation?> LocateAsync(HttpClient http, CancellationToken ct)
    {
        foreach (var url in new[] { "https://ipwho.is/?lang=pt-BR", "https://ipapi.co/json/" })
        {
            try
            {
                string json = await http.GetStringAsync(url, ct);
                if (ParseLocation(json) is { } location) return location;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { }
        }
        return null;
    }

    /// <summary>Área do mapa que mostra todos os pontos, com folga; no mínimo a América do Sul inteira (o mapa não fica "zoom demais").</summary>
    public static MapBounds Fit(IEnumerable<(double Lat, double Lon)> points, double aspect)
    {
        var list = points.Where(p => !double.IsNaN(p.Lat) && !double.IsNaN(p.Lon)).ToList();
        double minLon = -85, maxLon = -32, minLat = -56, maxLat = 13;   // América do Sul
        foreach (var (lat, lon) in list)
        {
            minLon = Math.Min(minLon, lon - 8); maxLon = Math.Max(maxLon, lon + 8);
            minLat = Math.Min(minLat, lat - 6); maxLat = Math.Max(maxLat, lat + 6);
        }
        return FitArea(minLon, maxLon, minLat, maxLat, aspect);
    }

    /// <summary>Brasil inteiro, com uma folga para os nomes.</summary>
    public static MapBounds BrazilArea(double aspect) => FitArea(-76, -32, -35, 7, aspect);

    /// <summary>O mundo, sem a Antártida.</summary>
    public static MapBounds WorldArea(double aspect) => FitArea(-170, 180, -56, 76, aspect);

    /// <summary>Acerta a área à proporção da janela (largura/altura), crescendo o lado que falta.</summary>
    public static MapBounds FitArea(double minLon, double maxLon, double minLat, double maxLat, double aspect)
    {
        minLon = Math.Max(-180, minLon); maxLon = Math.Min(180, maxLon);
        minLat = Math.Max(-60, minLat); maxLat = Math.Min(80, maxLat);
        double midLat = (minLat + maxLat) / 2;
        double kx = Math.Cos(midLat * Math.PI / 180);
        double w = (maxLon - minLon) * kx, h = maxLat - minLat;
        if (w / h < aspect)
        {
            double needW = h * aspect / kx, extra = (needW - (maxLon - minLon)) / 2;
            minLon -= extra; maxLon += extra;
        }
        else
        {
            double needH = w / aspect, extra = (needH - h) / 2;
            minLat -= extra; maxLat += extra;
        }
        return new MapBounds(minLon, maxLon, minLat, maxLat);
    }

    /// <summary>O ponto cabe na área (com folga em graus)?</summary>
    public static bool Contains(MapBounds b, double lat, double lon, double margin = 0) =>
        lon >= b.MinLon - margin && lon <= b.MaxLon + margin && lat >= b.MinLat - margin && lat <= b.MaxLat + margin;

    /// <summary>Longitude/latitude para a posição na tela (projeção simples, norte para cima).</summary>
    public static (double X, double Y) Project(double lat, double lon, MapBounds b, double width, double height) =>
        ((lon - b.MinLon) / (b.MaxLon - b.MinLon) * width, (b.MaxLat - lat) / (b.MaxLat - b.MinLat) * height);

    /// <summary>Lê os anéis do <see cref="WorldMapData"/> ("lon,lat lon,lat|...").</summary>
    public static IEnumerable<(double Lon, double Lat)[]> Rings(string data)
    {
        foreach (var ring in data.Split('|', StringSplitOptions.RemoveEmptyEntries))
            yield return ring.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(p =>
            {
                int comma = p.IndexOf(',');
                return (double.Parse(p[..comma], CultureInfo.InvariantCulture), double.Parse(p[(comma + 1)..], CultureInfo.InvariantCulture));
            }).ToArray();
    }

    /// <summary>
    /// Onde cada salto do traceroute fica na linha entre você e o servidor (0 = você, 1 = servidor), pelo tempo de resposta de cada um:
    /// não é a posição geográfica real do roteador, é quanto do caminho ele representa. Sempre crescente.
    /// </summary>
    public static IReadOnlyList<double> HopPositions(IReadOnlyList<double?> hopMs)
    {
        double final = hopMs.LastOrDefault(v => v is > 0) ?? 0;
        var result = new List<double>();
        double last = 0;
        for (int i = 0; i < hopMs.Count; i++)
        {
            double t = final > 0 && hopMs[i] is double ms ? ms / final : (double)(i + 1) / (hopMs.Count + 1);
            t = Math.Clamp(Math.Max(t, last + 0.02), 0.04, 0.96);
            result.Add(t);
            last = t;
        }
        return result;
    }

    /// <summary>Mede o ping por conexão TCP (porta 443): é o que o jogo sente para abrir a conexão; funciona mesmo onde o ping comum é bloqueado.</summary>
    public static async Task<PingResult> PingAsync(PingTarget target, int tries, CancellationToken ct)
    {
        var times = new List<double>();
        int lost = 0;
        for (int i = 0; i < tries; i++)
        {
            ct.ThrowIfCancellationRequested();
            var ms = target.Icmp ? await NetAnalyzer.PingAsync(target.Host, 2000) : await NetAnalyzer.TcpPingAsync(target.Host, 443, 2000, ct);
            if (ms is double v) times.Add(v); else lost++;
            await Task.Delay(150, ct);
        }
        if (times.Count == 0) return new PingResult(target, null, null, lost, tries);
        var useful = times.Count > 2 && !target.Icmp ? times.Skip(1).ToList() : times;   // a primeira conexão TCP inclui o DNS
        return new PingResult(target, SpeedMath.Median(useful), SpeedMath.Jitter(useful), lost, tries);
    }

    /// <summary>Frase para o ping de jogo.</summary>
    public static string GameVerdict(double? ms) => ms switch
    {
        null => "Sem resposta",
        < 20 => "Excelente para jogos competitivos",
        < 50 => "Muito bom para jogar",
        < 90 => "Bom; em jogos de tiro pode sentir um pouco",
        < 150 => "Atrasado: jogável em casual",
        _ => "Alto demais para jogar online",
    };
}
