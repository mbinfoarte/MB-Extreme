using System.Diagnostics;
using System.Numerics;

namespace MBExtreme.Core.Benchmark;

/// <summary>Resultado da etapa de inteligência artificial.</summary>
public sealed record AiResult(double CpuFp32Gflops, double CpuInt8Gops, double GpuFp32Gflops, int Score,
    double GenerationTokensPerSec = 0, double PromptTokensPerSec = 0);

/// <summary>
/// Inteligência artificial: o que uma rede neural faz por dentro é multiplicar matrizes. Mede essa conta em
/// números de 32 bits (FP32, como no treino) e em inteiros de 8 bits (INT8, como nos modelos "quantizados" que
/// rodam no PC), usando todos os núcleos e as instruções vetoriais. A placa de vídeo entra pela medição da janela 3D.
/// </summary>
public static class AiBenchmark
{
    public const string PartGenerate = "Escrevendo a resposta";
    public const string PartPrompt = "Lendo o seu pedido";
    public const string PartFp32 = "Rede neural FP32 (processador)";
    public const string PartInt8 = "Modelo quantizado INT8 (processador)";
    public const string PartGpu = "Rede neural na placa de vídeo";

    public static AiResult Run(int msPerPart, CancellationToken token, Action<string, double>? progress = null, Func<CancellationToken, double>? gpuGemm = null)
    {
        var model = TinyModel.Create();
        double generate = model.Generate(msPerPart, token, f => progress?.Invoke(PartGenerate, f));
        double prompt = model.ReadPrompt(msPerPart, token, f => progress?.Invoke(PartPrompt, f));
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
        return new AiResult(fp32, int8, gpu, Score(fp32, int8, gpu), generate, prompt);
    }

    /// <summary>
    /// Rede de teste no formato dos modelos de linguagem: 4 camadas (projeções de atenção e a rede de cada camada),
    /// largura 768, 28 milhões de parâmetros em FP32 (113 MB). Gerar um token passa por todos os pesos uma vez
    /// (depende da memória); ler o pedido processa 64 tokens de uma vez (depende do cálculo).
    /// </summary>
    public sealed class TinyModel
    {
        public const int Width = 768, Hidden = 3072, Layers = 4, Batch = 64;
        private readonly float[][] _weights; // por matriz: [saídas x entradas]
        private readonly (int Out, int In)[] _shapes;

        private TinyModel(float[][] weights, (int, int)[] shapes) { _weights = weights; _shapes = shapes; }

        public long Parameters => _weights.Sum(w => (long)w.Length);

        public static TinyModel Create(int seed = 5)
        {
            var shapes = new List<(int, int)>();
            for (int l = 0; l < Layers; l++)
            {
                shapes.Add((3 * Width, Width)); // q, k, v
                shapes.Add((Width, Width));     // saída da atenção
                shapes.Add((Hidden, Width));    // sobe
                shapes.Add((Width, Hidden));    // desce
            }
            var rng = new Random(seed);
            var weights = shapes.Select(sh =>
            {
                var w = new float[sh.Item1 * sh.Item2];
                for (int i = 0; i < w.Length; i++) w[i] = (float)(rng.NextDouble() - 0.5) * 0.04f;
                return w;
            }).ToArray();
            return new TinyModel(weights, shapes.ToArray());
        }

        /// <summary>y = W x, dividindo as linhas de saída entre os núcleos.</summary>
        private void MatVec(int m, float[] x, float[] y)
        {
            var (outs, ins) = _shapes[m];
            var w = _weights[m];
            int vw = Vector<float>.Count;
            Parallel.For(0, (outs + 63) / 64, block =>
            {
                int end = Math.Min(outs, block * 64 + 64);
                var xs = x.AsSpan(0, ins);
                for (int o = block * 64; o < end; o++)
                {
                    var row = w.AsSpan(o * ins, ins);
                    var acc = Vector<float>.Zero;
                    int i = 0;
                    for (; i <= ins - vw; i += vw) acc += new Vector<float>(row.Slice(i)) * new Vector<float>(xs.Slice(i));
                    float sum = Vector.Dot(acc, Vector<float>.One);
                    for (; i < ins; i++) sum += row[i] * xs[i];
                    y[o] = sum;
                }
            });
        }

        /// <summary>Y[b] = W X[b] para os 64 tokens: cada linha de pesos é lida uma vez e usada 64 vezes.</summary>
        private void MatMat(int m, float[] x, float[] y)
        {
            var (outs, ins) = _shapes[m];
            var w = _weights[m];
            int vw = Vector<float>.Count;
            Parallel.For(0, (outs + 31) / 32, block =>
            {
                int end = Math.Min(outs, block * 32 + 32);
                for (int o = block * 32; o < end; o++)
                {
                    var row = w.AsSpan(o * ins, ins);
                    for (int b = 0; b < Batch; b++)
                    {
                        var xs = x.AsSpan(b * ins, ins);
                        var acc = Vector<float>.Zero;
                        int i = 0;
                        for (; i <= ins - vw; i += vw) acc += new Vector<float>(row.Slice(i)) * new Vector<float>(xs.Slice(i));
                        y[b * outs + o] = Vector.Dot(acc, Vector<float>.One);
                    }
                }
            });
        }

        private static void Activate(float[] v, int count)
        {
            for (int i = 0; i < count; i++) { float a = v[i]; v[i] = a / (1f + MathF.Exp(-1.702f * a)); }
        }

        /// <summary>Tokens por segundo gerando um de cada vez (como o texto aparecendo no chat).</summary>
        public double Generate(int ms, CancellationToken token, Action<double>? progress = null)
        {
            var x = new float[Hidden];
            var y = new float[3 * Width + Hidden];
            var rng = new Random(1);
            for (int i = 0; i < Width; i++) x[i] = (float)rng.NextDouble();
            long tokens = 0;
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < ms && !token.IsCancellationRequested)
            {
                for (int l = 0; l < Layers; l++)
                {
                    MatVec(l * 4, x, y);
                    Array.Copy(y, 0, x, 0, Width);
                    MatVec(l * 4 + 1, x, y);
                    Array.Copy(y, 0, x, 0, Width);
                    MatVec(l * 4 + 2, x, y);
                    Activate(y, Hidden);
                    Array.Copy(y, 0, x, 0, Hidden);
                    MatVec(l * 4 + 3, x, y);
                    for (int i = 0; i < Width; i++) x[i] = y[i] * 0.5f + 0.1f;
                }
                tokens++;
                progress?.Invoke(Math.Min(1, watch.ElapsedMilliseconds / (double)ms));
            }
            token.ThrowIfCancellationRequested();
            return tokens / Math.Max(watch.Elapsed.TotalSeconds, 0.001);
        }

        /// <summary>Tokens por segundo lendo um pedido longo (64 tokens de uma vez).</summary>
        public double ReadPrompt(int ms, CancellationToken token, Action<double>? progress = null)
        {
            var x = new float[Batch * Hidden];
            var y = new float[Batch * Hidden * 1];
            var y3 = new float[Batch * 3 * Width];
            var rng = new Random(2);
            for (int i = 0; i < Batch * Width; i++) x[i] = (float)rng.NextDouble();
            long tokens = 0;
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < ms && !token.IsCancellationRequested)
            {
                for (int l = 0; l < Layers; l++)
                {
                    MatMat(l * 4, x, y3);
                    for (int b = 0; b < Batch; b++) Array.Copy(y3, b * 3 * Width, x, b * Width, Width);
                    MatMat(l * 4 + 1, x, y);
                    Array.Copy(y, x, Batch * Width);
                    MatMat(l * 4 + 2, x, y);
                    Activate(y, Batch * Hidden);
                    Array.Copy(y, x, Batch * Hidden);
                    MatMat(l * 4 + 3, x, y);
                    for (int i = 0; i < Batch * Width; i++) x[i] = y[i] * 0.5f + 0.1f;
                }
                tokens += Batch;
                progress?.Invoke(Math.Min(1, watch.ElapsedMilliseconds / (double)ms));
            }
            token.ThrowIfCancellationRequested();
            return tokens / Math.Max(watch.Elapsed.TotalSeconds, 0.001);
        }
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
