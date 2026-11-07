using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace MBExtreme.Core.Benchmark;

/// <summary>Resultado de uma tarefa do processador: quanto foi feito por segundo, na unidade da tarefa.</summary>
public sealed record CpuTaskResult(string Name, double Rate, string Unit);

/// <summary>
/// Tarefas reais do processador, cada uma medida pelo mesmo tempo: cálculo, ray tracing, física, compressão,
/// criptografia e ordenação. Com 1 linha de execução mede o núcleo; com todas, o processador inteiro.
/// A nota é a média geométrica de quanto cada tarefa rendeu em relação a uma referência fixa (x1000).
/// </summary>
public static class CpuTasks
{
    public static readonly IReadOnlyList<(string Name, string Unit, double Reference)> Tasks = new[]
    {
        ("Cálculo", "Mop/s", 200.0),
        ("Ray tracing", "M raios/s", 4.0),
        ("Física", "M interações/s", 300.0),
        ("Compressão", "MB/s", 120.0),
        ("Criptografia", "MB/s", 700.0),
        ("Ordenação", "M itens/s", 12.0),
    };

    /// <summary>Roda as seis tarefas, uma depois da outra, cada uma por <paramref name="msPerTask"/> em <paramref name="threads"/> linhas.</summary>
    public static IReadOnlyList<CpuTaskResult> Run(int threads, int msPerTask, CancellationToken token, Action<int, double>? progress = null)
    {
        threads = Math.Max(1, threads);
        var results = new List<CpuTaskResult>();
        for (int task = 0; task < Tasks.Count; task++)
        {
            token.ThrowIfCancellationRequested();
            int index = task;
            double rate = Measure(threads, msPerTask, token, () => CreateWorker(index), f => progress?.Invoke(index, f));
            results.Add(new CpuTaskResult(Tasks[task].Name, rate, Tasks[task].Unit));
        }
        return results;
    }

    public static int Score(IReadOnlyList<CpuTaskResult> results)
    {
        var ratios = results.Select(r => (r, Tasks.FirstOrDefault(t => t.Name == r.Name)))
            .Where(x => x.Item2.Reference > 0 && x.r.Rate > 0).Select(x => x.r.Rate / x.Item2.Reference).ToList();
        if (ratios.Count == 0) return 0;
        return (int)Math.Round(1000 * Math.Exp(ratios.Average(Math.Log)));
    }

    /// <summary>Cada linha de execução repete o trabalho até acabar o tempo; o total é somado.</summary>
    private static double Measure(int threads, int ms, CancellationToken token, Func<Func<double>> createWorker, Action<double> progress)
    {
        var totals = new double[threads];
        var watch = Stopwatch.StartNew();
        var workers = Enumerable.Range(0, threads).Select(i => new Thread(() =>
        {
            var work = createWorker();
            double done = 0;
            while (watch.ElapsedMilliseconds < ms && !token.IsCancellationRequested)
            {
                done += work();
                if (i == 0) progress(Math.Min(1, watch.ElapsedMilliseconds / (double)ms));
            }
            totals[i] = done;
        }) { IsBackground = true }).ToList();
        workers.ForEach(t => t.Start());
        workers.ForEach(t => t.Join());
        token.ThrowIfCancellationRequested();
        return totals.Sum() / Math.Max(watch.Elapsed.TotalSeconds, 0.001);
    }

    /// <summary>Cria o trabalho de uma tarefa (com os próprios dados, para as linhas não disputarem memória). Devolve a quantidade feita na unidade da tarefa.</summary>
    private static Func<double> CreateWorker(int task)
    {
        var rng = new Random(1234 + task);
        switch (task)
        {
            case 0:
                return () => Calc(200_000) * 0.2;
            case 1:
                return () => RayTrace(48, 48) / 1e6;
            case 2:
            {
                var bodies = NBody.Create(192, rng);
                return () => bodies.Step() / 1e6;
            }
            case 3:
            {
                var data = Compressible(256 * 1024, rng);
                return () =>
                {
                    using var sink = new MemoryStream(data.Length);
                    using (var gz = new GZipStream(sink, CompressionLevel.Fastest, true)) gz.Write(data, 0, data.Length);
                    return data.Length / 1e6;
                };
            }
            case 4:
            {
                var data = new byte[512 * 1024];
                rng.NextBytes(data);
                var aes = Aes.Create();
                aes.Key = SHA256.HashData(data.AsSpan(0, 64));
                var iv = new byte[16];
                var output = new byte[data.Length + 16];
                return () =>
                {
                    aes.TryEncryptCbc(data, iv, output, out _, PaddingMode.PKCS7);
                    SHA256.HashData(data);
                    return 2.0 * data.Length / 1e6;
                };
            }
            default:
            {
                var template = new int[65536];
                for (int i = 0; i < template.Length; i++) template[i] = rng.Next();
                var work = new int[template.Length];
                return () =>
                {
                    template.CopyTo(work, 0);
                    Array.Sort(work);
                    return work.Length / 1e6;
                };
            }
        }
    }

    private static double _sink;

    private static double Calc(long iterations)
    {
        double x = 1.0001;
        ulong h = 1469598103934665603UL;
        for (long i = 0; i < iterations; i++)
        {
            x = x * 1.0000001 + 0.5 / (i + 1);
            h = (h ^ (ulong)i) * 1099511628211UL;
            if ((i & 0xFFFF) == 0) x = Math.Sqrt(x * x + 1);
        }
        _sink += x + (h & 1);
        return 1;
    }

    private static byte[] Compressible(int size, Random rng)
    {
        var data = new byte[size];
        var words = "processador memória disco placa vídeo jogo render benchmark tempo quadro núcleo arquivo"u8.ToArray();
        for (int i = 0; i < size; i++) data[i] = rng.Next(8) == 0 ? (byte)rng.Next(256) : words[(i * 7 + rng.Next(3)) % words.Length];
        return data;
    }

    /// <summary>Ray tracing pequeno: esferas, chão, sombra e um reflexo. Devolve quantos raios foram lançados.</summary>
    private static double RayTrace(int width, int height)
    {
        Span<float> sx = stackalloc float[] { -1.2f, 0.0f, 1.2f, -0.6f, 0.6f };
        Span<float> sy = stackalloc float[] { 0.5f, 0.8f, 0.5f, 0.25f, 0.25f };
        Span<float> sz = stackalloc float[] { -3.0f, -4.0f, -3.0f, -2.2f, -2.2f };
        Span<float> sr = stackalloc float[] { 0.5f, 0.8f, 0.5f, 0.25f, 0.25f };
        long rays = 0;
        float total = 0;
        for (int py = 0; py < height; py++)
        for (int px = 0; px < width; px++)
        {
            float dx = (px + 0.5f) / width * 2 - 1, dy = 1 - (py + 0.5f) / height * 2, dz = -1.5f;
            float inv = 1 / MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            dx *= inv; dy *= inv; dz *= inv;
            float ox = 0, oy = 0.6f, oz = 0, weight = 1;
            for (int bounce = 0; bounce < 2; bounce++)
            {
                rays++;
                float best = 1e9f; int hit = -1;
                for (int s = 0; s < sx.Length; s++)
                {
                    float lx = ox - sx[s], ly = oy - sy[s], lz = oz - sz[s];
                    float b = lx * dx + ly * dy + lz * dz, c = lx * lx + ly * ly + lz * lz - sr[s] * sr[s], d = b * b - c;
                    if (d < 0) continue;
                    float t = -b - MathF.Sqrt(d);
                    if (t > 1e-3f && t < best) { best = t; hit = s; }
                }
                float floorT = dy < 0 ? -oy / dy : 1e9f;
                if (hit < 0 && floorT >= 1e9f) { total += weight * 0.4f; break; }
                if (hit < 0 || floorT < best)
                {
                    float fx = ox + dx * floorT, fz = oz + dz * floorT;
                    total += weight * ((((int)MathF.Floor(fx) + (int)MathF.Floor(fz)) & 1) == 0 ? 0.8f : 0.3f);
                    break;
                }
                float hx = ox + dx * best, hy = oy + dy * best, hz = oz + dz * best;
                float nx = (hx - sx[hit]) / sr[hit], ny = (hy - sy[hit]) / sr[hit], nz = (hz - sz[hit]) / sr[hit];
                float lambert = MathF.Max(0, nx * 0.5f + ny * 0.8f + nz * 0.3f);
                total += weight * 0.6f * lambert;
                float dn = dx * nx + dy * ny + dz * nz;
                dx -= 2 * dn * nx; dy -= 2 * dn * ny; dz -= 2 * dn * nz;
                ox = hx + nx * 1e-3f; oy = hy + ny * 1e-3f; oz = hz + nz * 1e-3f;
                weight *= 0.4f;
            }
        }
        _sink += total;
        return rays;
    }

    /// <summary>Simulação de gravidade entre corpos (cada passo compara todos com todos).</summary>
    private sealed class NBody
    {
        private readonly float[] _x, _y, _z, _vx, _vy, _vz;

        private NBody(int n)
        {
            _x = new float[n]; _y = new float[n]; _z = new float[n];
            _vx = new float[n]; _vy = new float[n]; _vz = new float[n];
        }

        public static NBody Create(int n, Random rng)
        {
            var b = new NBody(n);
            for (int i = 0; i < n; i++) { b._x[i] = (float)rng.NextDouble() * 10; b._y[i] = (float)rng.NextDouble() * 10; b._z[i] = (float)rng.NextDouble() * 10; }
            return b;
        }

        public double Step()
        {
            int n = _x.Length;
            for (int i = 0; i < n; i++)
            {
                float ax = 0, ay = 0, az = 0;
                for (int j = 0; j < n; j++)
                {
                    float dx = _x[j] - _x[i], dy = _y[j] - _y[i], dz = _z[j] - _z[i];
                    float d2 = dx * dx + dy * dy + dz * dz + 0.01f;
                    float inv = 1 / (d2 * MathF.Sqrt(d2));
                    ax += dx * inv; ay += dy * inv; az += dz * inv;
                }
                _vx[i] += ax * 1e-4f; _vy[i] += ay * 1e-4f; _vz[i] += az * 1e-4f;
            }
            for (int i = 0; i < n; i++) { _x[i] += _vx[i]; _y[i] += _vy[i]; _z[i] += _vz[i]; }
            return (double)n * n;
        }
    }
}
