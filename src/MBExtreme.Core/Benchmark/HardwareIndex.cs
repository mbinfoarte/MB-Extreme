using System.Text.Json;
using System.Text.RegularExpressions;

namespace MBExtreme.Core.Benchmark;

public sealed record IndexEntry(string Name, double Score, int Samples);

/// <summary>Índice de velocidade de CPUs e placas de vídeo a partir dos resultados públicos do Blender Open Data (licença CC0). Mediana dos dispositivos = 100.</summary>
public sealed record HardwareIndexData(DateTime GeneratedAt, string Source, IReadOnlyList<IndexEntry> Cpu, IReadOnlyList<IndexEntry> Gpu);

/// <summary>
/// Transforma o arquivo de resultados do Blender Open Data (um JSON por linha) num índice pequeno. Os formatos mudaram de versão em versão, então a leitura é tolerante:
/// aceita registros com a lista "scenes" (versões antigas) e com "scene" único (versões novas). O tempo de cada cena é normalizado pela mediana de todos os dispositivos
/// na mesma cena e versão do Blender, e a nota do dispositivo é a média geométrica dessas velocidades relativas.
/// </summary>
public static class HardwareIndexBuilder
{
    private sealed record Sample(bool IsGpu, string Device, string Scene, string Version, double Seconds);

    public const string SourceName = "Blender Open Data (CC0), opendata.blender.org";

    public static HardwareIndexData Build(IEnumerable<string> jsonLines, DateTime generatedAt, int minSamplesPerDevice = 3, int minGroupSize = 10)
    {
        var samples = new List<Sample>();
        foreach (var line in jsonLines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                Collect(doc.RootElement, samples);
            }
            catch (JsonException) { /* linha estragada: ignora */ }
        }

        var cpu = Score(samples.Where(s => !s.IsGpu), minSamplesPerDevice, minGroupSize);
        var gpu = Score(samples.Where(s => s.IsGpu), minSamplesPerDevice, minGroupSize);
        return new HardwareIndexData(generatedAt, SourceName, cpu, gpu);
    }

    private static void Collect(JsonElement root, List<Sample> samples)
    {
        if (root.ValueKind != JsonValueKind.Object) return;
        if (root.TryGetProperty("data", out var data))
        {
            if (data.ValueKind == JsonValueKind.Array) foreach (var item in data.EnumerateArray()) CollectEntry(item, samples);
            else CollectEntry(data, samples);
        }
        else CollectEntry(root, samples);
    }

    private static void CollectEntry(JsonElement d, List<Sample> samples)
    {
        if (d.ValueKind != JsonValueKind.Object) return;
        if (!d.TryGetProperty("device_info", out var info) || info.ValueKind != JsonValueKind.Object) return;
        string? name = null, type = null;
        if (info.TryGetProperty("compute_devices", out var devices) && devices.ValueKind == JsonValueKind.Array && devices.GetArrayLength() > 0)
        {
            var first = devices[0];
            if (first.ValueKind == JsonValueKind.String) name = first.GetString();
            else if (first.ValueKind == JsonValueKind.Object)
            {
                name = Str(first, "name");
                type = Str(first, "type");
            }
        }
        name ??= Str(info, "device_name") ?? Str(info, "name");
        type ??= Str(info, "device_type") ?? Str(info, "compute_framework");
        if (string.IsNullOrWhiteSpace(name)) return;

        string version = d.TryGetProperty("blender_version", out var bv) && bv.ValueKind == JsonValueKind.Object ? Str(bv, "version") ?? "?" : "?";
        bool gpu = IsGpu(type, name);

        if (d.TryGetProperty("scenes", out var scenes) && scenes.ValueKind == JsonValueKind.Array)
            foreach (var scene in scenes.EnumerateArray()) AddScene(samples, scene, scene, gpu, name, version);
        else if (d.TryGetProperty("scene", out var one) && one.ValueKind == JsonValueKind.Object)
            AddScene(samples, one, d, gpu, name, version);
    }

    private static void AddScene(List<Sample> samples, JsonElement scene, JsonElement statsHolder, bool gpu, string device, string version)
    {
        string? label = Str(scene, "label") ?? Str(scene, "name");
        if (label is null || !statsHolder.TryGetProperty("stats", out var stats) || stats.ValueKind != JsonValueKind.Object) return;
        if (Str(stats, "result") is { } result && !result.Equals("OK", StringComparison.OrdinalIgnoreCase)) return;
        if (!stats.TryGetProperty("total_render_time", out var time) || time.ValueKind != JsonValueKind.Number) return;
        double seconds = time.GetDouble();
        if (seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) return;
        samples.Add(new Sample(gpu, device, label, version, seconds));
    }

    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool IsGpu(string? type, string name)
    {
        if (type is not null)
        {
            string t = type.ToUpperInvariant();
            if (t == "CPU") return false;
            if (t is "CUDA" or "OPTIX" or "HIP" or "METAL" or "ONEAPI" or "OPENCL" or "GPU") return true;
        }
        return Regex.IsMatch(name, @"NVIDIA|GEFORCE|RTX|GTX|RADEON|\bARC\b|QUADRO|TESLA|APPLE M\d", RegexOptions.IgnoreCase) && !Regex.IsMatch(name, @"RYZEN|CORE\(|XEON|EPYC|THREADRIPPER", RegexOptions.IgnoreCase);
    }

    private static List<IndexEntry> Score(IEnumerable<Sample> samples, int minSamples, int minGroup)
    {
        var list = samples.ToList();
        var medians = list.GroupBy(s => (s.Scene, s.Version))
            .Where(g => g.Count() >= minGroup)
            .ToDictionary(g => g.Key, g => Median(g.Select(s => s.Seconds).ToList()));

        var perDevice = new Dictionary<string, (string Display, List<double> Logs)>();
        foreach (var s in list)
        {
            if (!medians.TryGetValue((s.Scene, s.Version), out double median)) continue;
            string key = HardwareNames.Key(s.Device);
            if (key.Length == 0) continue;
            if (!perDevice.TryGetValue(key, out var entry)) perDevice[key] = entry = (s.Device, new List<double>());
            entry.Logs.Add(Math.Log(median / s.Seconds));    // mais de 0: mais rápido que a mediana
        }

        var scored = perDevice.Values.Where(v => v.Logs.Count >= minSamples)
            .Select(v => (v.Display, Raw: Math.Exp(v.Logs.Average()), v.Logs.Count)).ToList();
        if (scored.Count == 0) return new List<IndexEntry>();
        double scale = 100.0 / Median(scored.Select(s => s.Raw).ToList());
        return scored.OrderByDescending(s => s.Raw).Select(s => new IndexEntry(s.Display, Math.Round(s.Raw * scale, 2), s.Count)).ToList();
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        int n = values.Count;
        return n % 2 == 1 ? values[n / 2] : (values[n / 2 - 1] + values[n / 2]) / 2;
    }
}

/// <summary>Nome de CPU ou placa reduzido ao que identifica o modelo, para achar o mesmo dispositivo escrito de jeitos diferentes.</summary>
public static class HardwareNames
{
    public static string Key(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        string u = name.ToUpperInvariant().Replace("(R)", " ").Replace("(TM)", " ").Replace("®", " ").Replace("™", " ");
        u = Regex.Replace(u, @"\(.*?\)", " ");                                  // "(OptiX)", "(8 cores)"
        u = Regex.Replace(u, @"@\s*[\d.,]+\s*GHZ", " ");
        u = Regex.Replace(u, @"\b\d+(ST|ND|RD|TH)\s*GEN\b", " ");
        u = Regex.Replace(u, @"\b\d+-CORE\b", " ");
        u = Regex.Replace(u, @"\bWITH\s+RADEON\b.*$|\bWITH\s+.*GRAPHICS\b", " ");
        u = Regex.Replace(u, @"\b(NVIDIA|GEFORCE|AMD|INTEL|RADEON|CPU|PROCESSOR|GRAPHICS|LAPTOP|GPU|MOBILE|CORPORATION)\b", " ");
        return Regex.Replace(u, @"\s+", " ").Trim();
    }
}

/// <summary>Consulta ao índice: em que posição o seu processador ou a sua placa fica entre os modelos do conjunto.</summary>
public sealed class HardwareIndex
{
    private readonly Dictionary<string, IndexEntry> _cpu, _gpu;

    public HardwareIndex(HardwareIndexData data)
    {
        Data = data;
        _cpu = ToMap(data.Cpu);
        _gpu = ToMap(data.Gpu);
    }

    public HardwareIndexData Data { get; }

    private static Dictionary<string, IndexEntry> ToMap(IReadOnlyList<IndexEntry> entries)
    {
        var map = new Dictionary<string, IndexEntry>(StringComparer.Ordinal);
        foreach (var e in entries)
        {
            string key = HardwareNames.Key(e.Name);
            if (key.Length > 0 && !map.ContainsKey(key)) map[key] = e;
        }
        return map;
    }

    /// <summary>Resultado: nota do modelo, % dos modelos do índice que ele supera, e quantos modelos o índice tem.</summary>
    public sealed record Standing(IndexEntry Entry, double BeatsPercent, int Models);

    public Standing? Cpu(string? name) => Find(name, _cpu, Data.Cpu);

    public Standing? Gpu(string? name) => Find(name, _gpu, Data.Gpu);

    private static Standing? Find(string? name, Dictionary<string, IndexEntry> map, IReadOnlyList<IndexEntry> all)
    {
        string key = HardwareNames.Key(name);
        if (key.Length == 0 || all.Count == 0) return null;
        // só vale o mesmo modelo: "RTX 3070" e "RTX 3070 Ti" são placas diferentes
        if (!map.TryGetValue(key, out var entry)) return null;
        int below = all.Count(e => e.Score < entry.Score);
        return new Standing(entry, below * 100.0 / all.Count, all.Count);
    }

    public static HardwareIndex? Load(params string[] paths)
    {
        foreach (var path in paths)
        {
            try
            {
                if (!File.Exists(path)) continue;
                var data = JsonSerializer.Deserialize<HardwareIndexData>(File.ReadAllText(path));
                if (data is { Cpu.Count: > 0 } or { Gpu.Count: > 0 }) return new HardwareIndex(data);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return null;
    }

    public static void Save(HardwareIndexData data, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = false }));
    }
}
