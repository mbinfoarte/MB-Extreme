using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MBExtreme.Core.Benchmark;
using MBExtreme.Core.Ranking;
using Microsoft.Data.Sqlite;

namespace MBExtreme.LicenseServer;

/// <summary>
/// Guarda o ranking anônimo em um banco separado do das licenças. O código de instalação nunca é gravado como veio: vira um hash com um segredo do servidor.
/// Não há tabela de usuários, e-mails nem endereços de IP aqui. Cada instalação tem no máximo uma linha por tipo (a mais recente).
/// </summary>
public sealed class RankingStore
{
    private readonly string _connectionString;
    private readonly string _pepper;

    public RankingStore(string path, string pepper)
    {
        _pepper = pepper;
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Shared }.ToString();
        using var c = Open();
        Exec(c, """
            CREATE TABLE IF NOT EXISTS rank_bench (install_hash TEXT PRIMARY KEY, cpu_key TEXT NOT NULL, gpu_key TEXT NOT NULL, cpu TEXT NOT NULL, gpu TEXT NOT NULL, ram_gb INTEGER NOT NULL, cores INTEGER NOT NULL, threads INTEGER NOT NULL,
                score INTEGER NOT NULL, cpu_single REAL NOT NULL, cpu_multi REAL NOT NULL, mem_gbps REAL NOT NULL, disk_write REAL NOT NULL, gpu3d INTEGER NOT NULL, render_single INTEGER NOT NULL, render_multi INTEGER NOT NULL, app_version TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_rank_bench_gpu ON rank_bench(gpu_key);
            CREATE TABLE IF NOT EXISTS rank_game (install_hash TEXT NOT NULL, game_key TEXT NOT NULL, game TEXT NOT NULL, cpu_key TEXT NOT NULL, gpu_key TEXT NOT NULL, gpu TEXT NOT NULL, ram_gb INTEGER NOT NULL,
                avg_fps REAL NOT NULL, low1 REAL NOT NULL, low01 REAL NOT NULL, seconds INTEGER NOT NULL, app_version TEXT NOT NULL, updated_at TEXT NOT NULL, PRIMARY KEY (install_hash, game_key));
            CREATE INDEX IF NOT EXISTS ix_rank_game ON rank_game(game_key, gpu_key);
            """);
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }

    private static void Exec(SqliteConnection c, string sql, params (string, object?)[] args)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private List<double> Column(string sql, params (string, object?)[] args)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        using var r = cmd.ExecuteReader();
        var list = new List<double>();
        while (r.Read()) list.Add(r.GetDouble(0));
        return list;
    }

    public string Hash(string installId)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_pepper));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes("rank|" + installId)));
    }

    public BenchStanding SaveBench(BenchSubmission s)
    {
        string cpu = RankingRules.Clean(s.Cpu), gpu = RankingRules.Clean(s.Gpu);
        string gpuKey = HardwareNames.Key(gpu), cpuKey = HardwareNames.Key(cpu);
        using (var c = Open())
        {
            Exec(c, """
                INSERT INTO rank_bench(install_hash, cpu_key, gpu_key, cpu, gpu, ram_gb, cores, threads, score, cpu_single, cpu_multi, mem_gbps, disk_write, gpu3d, render_single, render_multi, app_version, updated_at)
                VALUES($h,$ck,$gk,$c,$g,$r,$co,$t,$s,$cs,$cm,$m,$d,$g3,$rs,$rm,$v,$u)
                ON CONFLICT(install_hash) DO UPDATE SET cpu_key=$ck, gpu_key=$gk, cpu=$c, gpu=$g, ram_gb=$r, cores=$co, threads=$t, score=$s, cpu_single=$cs, cpu_multi=$cm, mem_gbps=$m, disk_write=$d, gpu3d=$g3, render_single=$rs, render_multi=$rm, app_version=$v, updated_at=$u
                """,
                ("$h", Hash(s.InstallId)), ("$ck", cpuKey), ("$gk", gpuKey), ("$c", cpu), ("$g", gpu), ("$r", s.RamGb), ("$co", s.Cores), ("$t", s.Threads), ("$s", s.Score),
                ("$cs", s.CpuSingleMops), ("$cm", s.CpuMultiMops), ("$m", s.MemoryGBps), ("$d", s.DiskWriteMBps), ("$g3", s.Gpu3DScore), ("$rs", s.RenderSingleScore), ("$rm", s.RenderMultiScore),
                ("$v", Truncate(RankingRules.Clean(s.AppVersion), 20)), ("$u", DateTime.UtcNow.ToString("O")));
        }

        var same = Column("SELECT score FROM rank_bench WHERE gpu_key = $k", ("$k", gpuKey));
        var all = Column("SELECT score FROM rank_bench");
        return new BenchStanding(RankingMath.Stand(same, s.Score, RankingRules.MinSamples), RankingMath.Stand(all, s.Score, RankingRules.MinSamples));
    }

    public GameStanding SaveGame(GameSubmission s)
    {
        string gpu = RankingRules.Clean(s.Gpu), cpu = RankingRules.Clean(s.Cpu), game = RankingRules.Clean(s.Game);
        string gpuKey = HardwareNames.Key(gpu), gameKey = RankingRules.GameKey(game);
        using (var c = Open())
        {
            Exec(c, """
                INSERT INTO rank_game(install_hash, game_key, game, cpu_key, gpu_key, gpu, ram_gb, avg_fps, low1, low01, seconds, app_version, updated_at)
                VALUES($h,$gk,$g,$ck,$pk,$p,$r,$a,$l1,$l01,$s,$v,$u)
                ON CONFLICT(install_hash, game_key) DO UPDATE SET game=$g, cpu_key=$ck, gpu_key=$pk, gpu=$p, ram_gb=$r, avg_fps=$a, low1=$l1, low01=$l01, seconds=$s, app_version=$v, updated_at=$u
                """,
                ("$h", Hash(s.InstallId)), ("$gk", gameKey), ("$g", game), ("$ck", HardwareNames.Key(cpu)), ("$pk", gpuKey), ("$p", gpu), ("$r", s.RamGb), ("$a", s.AvgFps), ("$l1", s.Low1Fps),
                ("$l01", s.Low01Fps), ("$s", s.Seconds), ("$v", Truncate(RankingRules.Clean(s.AppVersion), 20)), ("$u", DateTime.UtcNow.ToString("O")));
        }

        var values = Column("SELECT avg_fps FROM rank_game WHERE game_key = $g AND gpu_key = $k", ("$g", gameKey), ("$k", gpuKey));
        bool enough = values.Count >= RankingRules.MinGameSamples;
        return new GameStanding(values.Count, enough, enough ? RankingMath.Median(values) : 0, enough ? RankingMath.Percentile(values, 25) : 0, enough ? RankingMath.Percentile(values, 75) : 0,
            enough ? RankingMath.BeatsPercent(values, s.AvgFps) : 0);
    }

    /// <summary>Apaga tudo que veio deste código de instalação. Devolve quantas linhas saíram.</summary>
    public int Forget(string installId)
    {
        string hash = Hash(installId);
        using var c = Open();
        int removed = 0;
        foreach (string table in new[] { "rank_bench", "rank_game" })
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = $"DELETE FROM {table} WHERE install_hash = $h";
            cmd.Parameters.AddWithValue("$h", hash);
            removed += cmd.ExecuteNonQuery();
        }
        return removed;
    }

    /// <summary>Tira medições com mais de <paramref name="months"/> meses (o ranking só vale com PCs e drivers recentes).</summary>
    public void Purge(int months = 18)
    {
        string limit = DateTime.UtcNow.AddMonths(-months).ToString("O");
        using var c = Open();
        Exec(c, "DELETE FROM rank_bench WHERE updated_at < $l", ("$l", limit));
        Exec(c, "DELETE FROM rank_game WHERE updated_at < $l", ("$l", limit));
    }

    public (int Bench, int Games) Counts()
    {
        using var c = Open();
        int Count(string table)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        return (Count("rank_bench"), Count("rank_game"));
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];
}

public static class RankingEndpoints
{
    public static void Map(WebApplication app, RankingStore store, RateLimiter limiter, int limitScale, Func<HttpContext, string> ip)
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        bool Allowed(HttpContext c, string installId, string kind) =>
            limiter.Allow("rank-ip|" + ip(c), 60 * limitScale, TimeSpan.FromHours(1)) &&
            limiter.Allow($"rank|{kind}|{installId}", 40 * limitScale, TimeSpan.FromDays(1));

        app.MapPost("/v1/rank/bench", async (HttpContext c) =>
        {
            var body = await ReadAsync<BenchSubmission>(c, json);
            if (body is null) return Results.BadRequest(new { message = "Corpo inválido." });
            if (RankingRules.Validate(body) is { } problem) return Results.BadRequest(new { message = problem });
            if (!Allowed(c, body.InstallId, "bench")) return Results.StatusCode(429);
            return Results.Json(store.SaveBench(body), json);
        });

        app.MapPost("/v1/rank/game", async (HttpContext c) =>
        {
            var body = await ReadAsync<GameSubmission>(c, json);
            if (body is null) return Results.BadRequest(new { message = "Corpo inválido." });
            if (RankingRules.Validate(body) is { } problem) return Results.BadRequest(new { message = problem });
            if (!Allowed(c, body.InstallId, "game")) return Results.StatusCode(429);
            return Results.Json(store.SaveGame(body), json);
        });

        app.MapPost("/v1/rank/forget", async (HttpContext c) =>
        {
            var body = await ReadAsync<ForgetRequest>(c, json);
            if (body is null || !RankingRules.ValidInstallId(body.InstallId)) return Results.BadRequest(new { message = "Código de instalação inválido." });
            if (!Allowed(c, body.InstallId, "forget")) return Results.StatusCode(429);
            return Results.Json(new { deleted = store.Forget(body.InstallId) }, json);
        });
    }

    private static async Task<T?> ReadAsync<T>(HttpContext c, JsonSerializerOptions options) where T : class
    {
        try
        {
            if (c.Request.ContentLength is > 16_384) return null;
            return await JsonSerializer.DeserializeAsync<T>(c.Request.Body, options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
