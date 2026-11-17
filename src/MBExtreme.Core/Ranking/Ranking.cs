using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MBExtreme.Core.Ranking;

/// <summary>
/// Ranking anônimo e opcional: o app só envia quando o usuário aceita. Não vai nome, e-mail, endereço de IP guardado nem número de licença:
/// só o modelo da CPU e da placa de vídeo, a memória e as notas medidas, ligados a um código aleatório que nasce no PC e pode ser apagado a qualquer hora.
/// </summary>
public sealed record BenchSubmission(string InstallId, string Cpu, string Gpu, int RamGb, int Cores, int Threads, int Score, double CpuSingleMops, double CpuMultiMops,
    double MemoryGBps, double DiskWriteMBps, int Gpu3DScore, int RenderSingleScore, int RenderMultiScore, string AppVersion);

public sealed record GameSubmission(string InstallId, string Game, string Cpu, string Gpu, int RamGb, double AvgFps, double Low1Fps, double Low01Fps, int Seconds, string AppVersion);

public sealed record ForgetRequest(string InstallId);

/// <summary>Posição entre os PCs que participam: só aparece quando há amostras suficientes (<see cref="Enough"/>).</summary>
public sealed record Standing(int Samples, bool Enough, double BeatsPercent, double Median);

public sealed record BenchStanding(Standing SameGpu, Standing All);

public sealed record GameStanding(int Samples, bool Enough, double MedianFps, double P25Fps, double P75Fps, double BeatsPercent);

public static class RankingRules
{
    public const int MinSamples = 30;
    public const int MinGameSamples = 10;
    public const int MaxName = 120;

    private static readonly Regex NameRx = new(@"^[\p{L}\p{N} .,()\-_+/®™@#:'&]+$", RegexOptions.Compiled);

    public static bool ValidInstallId(string? id) => id is { Length: >= 32 and <= 64 } && id.All(char.IsLetterOrDigit);

    public static string Clean(string? text) => Regex.Replace(text ?? "", @"\s+", " ").Trim();

    private static bool ValidName(string name) => name.Length is > 0 and <= MaxName && NameRx.IsMatch(name);

    public static string? Validate(BenchSubmission s)
    {
        if (!ValidInstallId(s.InstallId)) return "Código de instalação inválido.";
        if (!ValidName(Clean(s.Cpu)) || !ValidName(Clean(s.Gpu))) return "Nome de processador ou placa inválido.";
        if (s.RamGb is < 1 or > 4096 || s.Cores is < 1 or > 512 || s.Threads is < 1 or > 1024) return "Dados do PC fora do esperado.";
        if (s.Score is < 1 or > 10_000_000) return "Nota fora do esperado.";
        if (Negative(s.CpuSingleMops, s.CpuMultiMops, s.MemoryGBps, s.DiskWriteMBps) || s.Gpu3DScore is < 0 or > 10_000_000 || s.RenderSingleScore is < 0 or > 10_000_000 || s.RenderMultiScore is < 0 or > 10_000_000)
            return "Medidas fora do esperado.";
        return null;
    }

    public static string? Validate(GameSubmission s)
    {
        if (!ValidInstallId(s.InstallId)) return "Código de instalação inválido.";
        if (!ValidName(Clean(s.Cpu)) || !ValidName(Clean(s.Gpu)) || !ValidName(Clean(s.Game))) return "Nome inválido.";
        if (s.RamGb is < 1 or > 4096) return "Dados do PC fora do esperado.";
        if (s.AvgFps is < 1 or > 2000 || s.Low1Fps is < 0 or > 2000 || s.Low01Fps is < 0 or > 2000 || s.Low1Fps > s.AvgFps * 1.01) return "FPS fora do esperado.";
        if (s.Seconds is < 30 or > 86_400) return "Duração fora do esperado (mínimo de 30 segundos).";
        return null;
    }

    private static bool Negative(params double[] values) => values.Any(v => v < 0 || v > 1e9 || double.IsNaN(v) || double.IsInfinity(v));

    /// <summary>Chave de agrupamento do jogo: letras e números em minúsculas.</summary>
    public static string GameKey(string name) => new(Clean(name).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}

public static class RankingMath
{
    public static double Median(IReadOnlyList<double> values) => Percentile(values, 50);

    /// <summary>Percentil por interpolação (0 a 100). Lista vazia devolve 0.</summary>
    public static double Percentile(IReadOnlyList<double> values, double percent)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToArray();
        double position = Math.Clamp(percent, 0, 100) / 100 * (sorted.Length - 1);
        int low = (int)Math.Floor(position), high = (int)Math.Ceiling(position);
        return sorted[low] + (sorted[high] - sorted[low]) * (position - low);
    }

    /// <summary>% dos valores que ficam abaixo do seu (empate vale meio ponto).</summary>
    public static double BeatsPercent(IReadOnlyList<double> values, double mine)
    {
        if (values.Count == 0) return 0;
        double below = values.Count(v => v < mine) + values.Count(v => v == mine) * 0.5;
        return below * 100.0 / values.Count;
    }

    public static Standing Stand(IReadOnlyList<double> values, double mine, int minSamples) =>
        new(values.Count, values.Count >= minSamples, values.Count >= minSamples ? BeatsPercent(values, mine) : 0, values.Count >= minSamples ? Median(values) : 0);
}

/// <summary>Fala com o servidor de ranking. Qualquer falha de rede devolve nulo: o ranking nunca atrapalha o uso do app.</summary>
public sealed class RankingClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public RankingClient(HttpClient http, string baseUrl)
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public bool Configured => _baseUrl.Length > 0;

    public async Task<BenchStanding?> SubmitBenchAsync(BenchSubmission submission, CancellationToken token = default) =>
        await PostAsync<BenchSubmission, BenchStanding>("/v1/rank/bench", submission, token);

    public async Task<GameStanding?> SubmitGameAsync(GameSubmission submission, CancellationToken token = default) =>
        await PostAsync<GameSubmission, GameStanding>("/v1/rank/game", submission, token);

    /// <summary>Apaga tudo que este código enviou. Verdadeiro quando o servidor confirmou.</summary>
    public async Task<bool> ForgetAsync(string installId, CancellationToken token = default) =>
        await PostAsync<ForgetRequest, JsonElement?>("/v1/rank/forget", new ForgetRequest(installId), token) is not null;

    private async Task<TOut?> PostAsync<TIn, TOut>(string path, TIn body, CancellationToken token)
    {
        if (!Configured) return default;
        try
        {
            using var response = await _http.PostAsJsonAsync(_baseUrl + path, body, Json, token);
            if (!response.IsSuccessStatusCode) return default;
            return await response.Content.ReadFromJsonAsync<TOut>(Json, token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            return default;
        }
    }
}
