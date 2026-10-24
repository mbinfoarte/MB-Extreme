using System.Net.Http;
using System.Text.Json;
using MBExtreme.Core;
using MBExtreme.Core.Benchmark;
using MBExtreme.Core.Fps;
using MBExtreme.Core.Games;
using MBExtreme.Core.Hardware;
using MBExtreme.Core.Licensing;
using MBExtreme.Core.Ranking;

namespace MBExtreme.UI;

/// <summary>Liga o app ao ranking anônimo. Só envia se a pessoa aceitou (<c>RankingOptIn</c>); qualquer falha é silenciosa para o usuário.</summary>
internal static class RankingService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static bool OptedIn => AppServices.Settings.RankingOptIn;

    public static string ServerUrl => LicenseConfig.From(AppServices.Settings).ServerUrl;

    public static bool Configured => ServerUrl.Length > 0;

    private static RankingClient Client => new(Http, ServerUrl);

    /// <summary>Código desta instalação (criado só quando precisa).</summary>
    public static string InstallId()
    {
        var s = AppServices.Settings;
        if (!RankingRules.ValidInstallId(s.RankingInstallId))
        {
            s.RankingInstallId = Guid.NewGuid().ToString("N");
            s.Save();
        }
        return s.RankingInstallId;
    }

    public static BenchSubmission? BuildBench(BenchmarkResult r, HardwareInventory hw)
    {
        if (r.Score <= 0) return null;   // medição só de render da CPU não tem nota geral
        var gpu = hw.Gpus.OrderByDescending(g => g.VramBytes).FirstOrDefault();
        return new BenchSubmission(InstallId(), hw.Cpu.Name.Trim(), gpu?.Name ?? "Sem placa de vídeo dedicada", (int)Math.Round(hw.TotalMemoryBytes / 1024.0 / 1024 / 1024), hw.Cpu.Cores, hw.Cpu.Threads,
            r.Score, r.CpuSingleMops, r.CpuMultiMops, r.MemoryGBps, r.DiskWriteMBps, r.Gpu3DScore, r.RenderSingleScore, r.RenderMultiScore, AppInfo.Version);
    }

    public static GameSubmission? BuildGame(FpsSession s, HardwareInventory hw)
    {
        if (s.Metrics.DurationSeconds < 30) return null;
        var gpu = hw.Gpus.OrderByDescending(g => g.VramBytes).FirstOrDefault();
        return new GameSubmission(InstallId(), s.Game, hw.Cpu.Name.Trim(), gpu?.Name ?? "Sem placa de vídeo dedicada", (int)Math.Round(hw.TotalMemoryBytes / 1024.0 / 1024 / 1024),
            Math.Round(s.Metrics.AverageFps, 1), Math.Round(Math.Min(s.Metrics.Low1Fps, s.Metrics.AverageFps), 1), Math.Round(Math.Min(s.Metrics.Low01Fps, s.Metrics.AverageFps), 1), (int)s.Metrics.DurationSeconds, AppInfo.Version);
    }

    /// <summary>O texto exato que seria enviado, para a pessoa conferir antes de aceitar.</summary>
    public static string Preview(object submission) => JsonSerializer.Serialize(submission, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });

    public static async Task<BenchStanding?> SubmitBenchAsync(BenchmarkResult result)
    {
        if (!OptedIn || !Configured) return null;
        try
        {
            var hw = await AppServices.Hardware();
            return BuildBench(result, hw) is { } submission ? await Client.SubmitBenchAsync(submission) : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Management.ManagementException or AggregateException) { return null; }
    }

    public static async Task<GameStanding?> SubmitGameAsync(FpsSession session)
    {
        if (!OptedIn || !Configured) return null;
        try
        {
            var hw = await AppServices.Hardware();
            return BuildGame(session, hw) is { } submission ? await Client.SubmitGameAsync(submission) : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Management.ManagementException or AggregateException) { return null; }
    }

    /// <summary>Apaga os dados no servidor e troca o código da instalação. Verdadeiro se o servidor confirmou.</summary>
    public static async Task<bool> ForgetAsync()
    {
        var s = AppServices.Settings;
        if (!Configured || !RankingRules.ValidInstallId(s.RankingInstallId)) return false;
        bool ok = await Client.ForgetAsync(s.RankingInstallId);
        if (ok)
        {
            s.RankingInstallId = "";
            s.Save();
        }
        return ok;
    }

    public static string Describe(BenchStanding? standing, string gpuName)
    {
        if (standing is null) return "Não foi possível enviar ao ranking agora (sem internet ou o servidor não respondeu). Sua medição continua guardada aqui.";
        if (standing.SameGpu.Enough)
            return $"Entre {standing.SameGpu.Samples} PCs com {gpuName} que participam, o seu fica acima de {standing.SameGpu.BeatsPercent:0}% (mediana {standing.SameGpu.Median:N0} pontos).";
        if (standing.All.Enough)
            return $"Ainda há poucos PCs com a sua placa ({standing.SameGpu.Samples}) para comparar. Entre os {standing.All.Samples} PCs do ranking, o seu fica acima de {standing.All.BeatsPercent:0}%.";
        return $"Medição enviada. O ranking ainda tem poucos participantes ({standing.All.Samples}) para mostrar uma posição.";
    }

    public static string Describe(GameStanding? standing, double myFps)
    {
        if (standing is null) return "Não foi possível enviar ao ranking agora.";
        if (!standing.Enough) return $"Medição enviada. Ainda há poucos PCs com a sua placa neste jogo ({standing.Samples}) para comparar.";
        return $"Entre {standing.Samples} PCs com a sua placa neste jogo, a mediana é {standing.MedianFps:0} FPS (metade fica entre {standing.P25Fps:0} e {standing.P75Fps:0}); o seu teve {myFps:0} FPS, acima de {standing.BeatsPercent:0}%.";
    }
}
