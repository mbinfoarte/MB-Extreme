using System.Diagnostics;
using System.Numerics;

namespace MBExtreme.Core.Benchmark;

/// <summary>Resultado da etapa de inteligência artificial.</summary>
public sealed record AiResult(double CpuFp32Gflops, double CpuInt8Gops, double GpuFp32Gflops, int Score);

/// <summary>
/// Inteligência artificial: o que uma rede neural faz por dentro é multiplicar matrizes. Mede essa conta em
/// números de 32 bits (FP32, como no treino) e em inteiros de 8 bits (INT8, como nos modelos "quantizados" que
/// rodam no PC), usando todos os núcleos e as instruções vetoriais. A placa de vídeo entra pela medição da janela 3D.
/// </summary>
public static class AiBenchmark
{
    public const string PartFp32 = "Rede neural FP32 (processador)";
    public const string PartInt8 = "Modelo quantizado INT8 (processador)";
    public const string PartGpu = "Rede neural na placa de vídeo";

    public static AiResult Run(int msPerPart, CancellationToken token, Action<string, double>? progress = null, Func<CancellationToken, double>? gpuGemm = null)
    {
        double fp32 = Gemm(msPerPart, token, f => progress?.Invoke(PartFp32, f));
        double int8 = Int8(msPerPart, token, f => progress?.Invoke(PartInt8, f));
        double gpu = 0;
        if (gpuGemm is not null)
        {
            progress?.Invoke(PartGpu, 0);
            try { gpu = gpuGemm(token); }
            catch (Exception ex) when (ex is not OperationCanceledException) { gpu = 0; }
            progress?.Invoke(PartGpu, 1);
        }
        return new AiResult(fp32, int8, gpu, Score(fp32, int8, gpu));
    }

    public static int Score(double fp32Gflops, double int8Gops, double gpuGflops) =>
        (int)Math.Round(fp32Gflops * 4 + int8Gops * 2 + gpuGflops * 0.1);

    /// <summary>C = A x B (matrizes 256x256) repetido em todas as linhas de execução. Devolve GFLOPS (2 operações por multiplicação-soma).</summary>
    private static double Gemm(int ms, CancellationToken token, Action<double> progress)
    {
        const int n = 256;
        int threads = Environment.ProcessorCount;
        var counts = new long[threads];
        var watch = Stopwatch.StartNew();
        var workers = Enumerable.Range(0, threads).Select(t => new Thread(() =>
        {
            var rng = new Random(t + 1);
            var a = new float[n * n];
            var b = new float[n * n];
            var c = new float[n * n];
            for (int i = 0; i < a.Length; i++) { a[i] = (float)rng.NextDouble() - 0.5f; b[i] = (float)rng.NextDouble() - 0.5f; }
            int w = Vector<float>.Count;
            long done = 0;
            while (watch.ElapsedMilliseconds < ms && !token.IsCancellationRequested)
            {
                Array.Clear(c);
                for (int i = 0; i < n; i++)
                {
                    var row = c.AsSpan(i * n, n);
                    for (int k = 0; k < n; k++)
                    {
                        var aik = new Vector<float>(a[i * n + k]);
                        var bk = b.AsSpan(k * n, n);
                        int j = 0;
                        for (; j <= n - w; j += w)
                            (new Vector<float>(row.Slice(j)) + aik * new Vector<float>(bk.Slice(j))).CopyTo(row.Slice(j));
                        for (; j < n; j++) row[j] += a[i * n + k] * bk[j];
                    }
                }
                done++;
                if (t == 0) progress(Math.Min(1, watch.ElapsedMilliseconds / (double)ms));
            }
            counts[t] = done;
            _sink += c[n + 1];
        }) { IsBackground = true }).ToList();
        workers.ForEach(x => x.Start());
        workers.ForEach(x => x.Join());
        token.ThrowIfCancellationRequested();
        return counts.Sum() * 2.0 * n * n * n / Math.Max(watch.Elapsed.TotalSeconds, 0.001) / 1e9;
    }

    /// <summary>Camada quantizada: vetor x matriz em inteiros de 8 bits, somando em 32 bits. Devolve GOPS.</summary>
    private static double Int8(int ms, CancellationToken token, Action<double> progress)
    {
        const int inputs = 4096, outputs = 256;
        int threads = Environment.ProcessorCount;
        var counts = new long[threads];
        var watch = Stopwatch.StartNew();
        var workers = Enumerable.Range(0, threads).Select(t => new Thread(() =>
        {
            var rng = new Random(100 + t);
            var weights = new sbyte[inputs * outputs];
            var x = new sbyte[inputs];
            for (int i = 0; i < weights.Length; i++) weights[i] = (sbyte)rng.Next(-127, 128);
            for (int i = 0; i < x.Length; i++) x[i] = (sbyte)rng.Next(-127, 128);
            var y = new int[outputs];
            int w = Vector<sbyte>.Count;
            long done = 0;
            while (watch.ElapsedMilliseconds < ms && !token.IsCancellationRequested)
            {
                for (int o = 0; o < outputs; o++)
                {
                    var row = weights.AsSpan(o * inputs, inputs);
                    var acc = Vector<int>.Zero;
                    for (int i = 0; i <= inputs - w; i += w)
                    {
                        Vector.Widen(new Vector<sbyte>(row.Slice(i)), out Vector<short> wl, out Vector<short> wh);
                        Vector.Widen(new Vector<sbyte>(x.AsSpan(i)), out Vector<short> xl, out Vector<short> xh);
                        Vector.Widen(wl * xl, out Vector<int> p0, out Vector<int> p1);
                        Vector.Widen(wh * xh, out Vector<int> p2, out Vector<int> p3);
                        acc += p0 + p1 + p2 + p3;
                    }
                    y[o] = Vector.Sum(acc);
                }
                done++;
                if (t == 0) progress(Math.Min(1, watch.ElapsedMilliseconds / (double)ms));
            }
            counts[t] = done;
            _sink += y[3];
        }) { IsBackground = true }).ToList();
        workers.ForEach(v => v.Start());
        workers.ForEach(v => v.Join());
        token.ThrowIfCancellationRequested();
        return counts.Sum() * 2.0 * inputs * outputs / Math.Max(watch.Elapsed.TotalSeconds, 0.001) / 1e9;
    }

    private static double _sink;
}
