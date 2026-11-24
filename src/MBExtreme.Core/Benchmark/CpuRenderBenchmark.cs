using System.Diagnostics;

namespace MBExtreme.Core.Benchmark;

/// <summary>Um pedaço (bloco) já desenhado da imagem, em BGRA, para mostrar o desenho aparecendo na tela.</summary>
public sealed record RenderTile(int X, int Y, int Width, int Height, byte[] Bgra);

public sealed record CpuRenderResult(int Threads, double Seconds, int Passes, long Rays, double MegaRaysPerSecond, int Score);

/// <summary>
/// Teste de renderização da CPU (no espírito do Cinebench, mas com cena e código próprios): um traçador de raios com esferas espelhadas, chão xadrez, sombras e reflexos.
/// A cena é desenhada em blocos, por todas as linhas de execução, várias vezes, até passar do tempo mínimo. A nota é a quantidade de raios por segundo.
/// Serve para comparar este PC com ele mesmo; não é comparável com as notas do Cinebench.
/// </summary>
public static class CpuRenderBenchmark
{
    public const int DefaultWidth = 640;
    public const int DefaultHeight = 360;
    public const int TileSize = 40;

    private readonly record struct V(double X, double Y, double Z)
    {
        public static V operator +(V a, V b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static V operator -(V a, V b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static V operator *(V a, double s) => new(a.X * s, a.Y * s, a.Z * s);
        public static V operator *(V a, V b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
        public double Dot(V b) => X * b.X + Y * b.Y + Z * b.Z;
        public double Length => Math.Sqrt(Dot(this));
        public V Unit() { double l = Length; return l > 0 ? this * (1 / l) : this; }
    }

    private readonly record struct Sphere(V Center, double Radius, V Color, double Reflect, double Shine);

    private static readonly Sphere[] Spheres =
    {
        new(new V(0, 1, 3), 1.0, new V(0.9, 0.9, 0.95), 0.7, 80),
        new(new V(-2.2, 0.7, 2.2), 0.7, new V(0.9, 0.2, 0.2), 0.25, 40),
        new(new V(2.1, 0.8, 2.6), 0.8, new V(0.2, 0.5, 0.95), 0.35, 60),
        new(new V(-0.9, 0.35, 0.9), 0.35, new V(0.95, 0.8, 0.2), 0.2, 30),
        new(new V(1.1, 0.4, 0.6), 0.4, new V(0.3, 0.85, 0.35), 0.3, 50),
        new(new V(-3.4, 1.1, 4.8), 1.1, new V(0.7, 0.4, 0.9), 0.5, 70),
        new(new V(3.6, 1.0, 5.2), 1.0, new V(0.95, 0.55, 0.2), 0.4, 50),
        new(new V(0.2, 0.5, -1.2), 0.5, new V(0.85, 0.85, 0.85), 0.85, 120),
    };

    private static readonly V[] Lights = { new(-5, 8, -4), new(6, 6, -2) };
    private static readonly V Camera = new(0, 1.7, -6.5);

    private static (double T, int Index)? Hit(V origin, V dir, double max)
    {
        double best = max;
        int index = -1;
        for (int i = 0; i < Spheres.Length; i++)
        {
            V oc = origin - Spheres[i].Center;
            double b = oc.Dot(dir);
            double c = oc.Dot(oc) - Spheres[i].Radius * Spheres[i].Radius;
            double disc = b * b - c;
            if (disc < 0) continue;
            double t = -b - Math.Sqrt(disc);
            if (t > 1e-4 && t < best) { best = t; index = i; }
        }
        if (dir.Y < -1e-6)
        {
            double t = -origin.Y / dir.Y;
            if (t > 1e-4 && t < best) { best = t; index = Spheres.Length; }
        }
        return index < 0 ? null : (best, index);
    }

    private static V Trace(V origin, V dir, int depth, ref long rays)
    {
        rays++;
        var hit = Hit(origin, dir, 1e9);
        if (hit is null)
        {
            double k = 0.5 * (dir.Y + 1);
            return new V(1, 1, 1) * (1 - k) + new V(0.45, 0.65, 1.0) * k;
        }

        var (t, index) = hit.Value;
        V point = origin + dir * t;
        V normal, color;
        double reflect, shine;
        if (index == Spheres.Length)
        {
            normal = new V(0, 1, 0);
            bool dark = ((int)Math.Floor(point.X) + (int)Math.Floor(point.Z)) % 2 == 0;
            color = dark ? new V(0.12, 0.12, 0.14) : new V(0.85, 0.85, 0.8);
            reflect = 0.2;
            shine = 10;
        }
        else
        {
            var s = Spheres[index];
            normal = (point - s.Center).Unit();
            color = s.Color;
            reflect = s.Reflect;
            shine = s.Shine;
        }

        double diffuse = 0.12, specular = 0;
        foreach (var light in Lights)
        {
            V toLight = (light - point).Unit();
            rays++;
            if (Hit(point, toLight, (light - point).Length) is not null) continue;
            diffuse += Math.Max(0, normal.Dot(toLight)) * 0.6;
            V half = (toLight - dir).Unit();
            specular += Math.Pow(Math.Max(0, normal.Dot(half)), shine) * 0.5;
        }

        V result = color * diffuse + new V(specular, specular, specular);
        if (depth < 3 && reflect > 0)
        {
            V reflected = (dir - normal * (2 * dir.Dot(normal))).Unit();
            result = result * (1 - reflect) + Trace(point + normal * 1e-4, reflected, depth + 1, ref rays) * reflect;
        }
        return result;
    }

    /// <summary>Desenha um bloco da imagem. <paramref name="samples"/> é o lado da grade de amostras por pixel (2 = 4 raios por pixel). Devolve os raios usados.</summary>
    public static (RenderTile Tile, long Rays) RenderTileAt(int x0, int y0, int tileW, int tileH, int width, int height, int samples)
    {
        samples = Math.Clamp(samples, 1, 4);
        var bgra = new byte[tileW * tileH * 4];
        long rays = 0;
        double aspect = width / (double)height;
        double scale = Math.Tan(25 * Math.PI / 180);
        for (int py = 0; py < tileH; py++)
        {
            for (int px = 0; px < tileW; px++)
            {
                V sum = default;
                for (int sy = 0; sy < samples; sy++)
                {
                    for (int sx = 0; sx < samples; sx++)
                    {
                        double u = ((x0 + px + (sx + 0.5) / samples) / width * 2 - 1) * aspect * scale;
                        double v = (1 - (y0 + py + (sy + 0.5) / samples) / height * 2) * scale;
                        V dir = new V(u, v - 0.12, 1).Unit();
                        sum += Trace(Camera, dir, 0, ref rays);
                    }
                }

                sum *= 1.0 / (samples * samples);
                int i = (py * tileW + px) * 4;
                bgra[i] = ToByte(sum.Z);
                bgra[i + 1] = ToByte(sum.Y);
                bgra[i + 2] = ToByte(sum.X);
                bgra[i + 3] = 255;
            }
        }

        return (new RenderTile(x0, y0, tileW, tileH, bgra), rays);
    }

    private static byte ToByte(double linear) => (byte)Math.Clamp((int)(Math.Sqrt(Math.Clamp(linear, 0, 1)) * 255 + 0.5), 0, 255);

    /// <summary>
    /// Renderiza a cena repetidas vezes com <paramref name="threads"/> linhas de execução até passar de <paramref name="minSeconds"/>.
    /// <paramref name="onTile"/> recebe cada bloco pronto (de qualquer linha de execução); <paramref name="onProgress"/> recebe 0 a 100.
    /// </summary>
    public static CpuRenderResult Run(int threads, double minSeconds, CancellationToken token, Action<RenderTile>? onTile = null, Action<double>? onProgress = null,
        int width = DefaultWidth, int height = DefaultHeight, int samples = 2)
    {
        threads = Math.Clamp(threads, 1, 256);
        var tiles = new List<(int X, int Y, int W, int H)>();
        for (int y = 0; y < height; y += TileSize)
            for (int x = 0; x < width; x += TileSize)
                tiles.Add((x, y, Math.Min(TileSize, width - x), Math.Min(TileSize, height - y)));

        var watch = Stopwatch.StartNew();
        long totalRays = 0;
        int passes = 0;
        while (true)
        {
            int next = -1;
            var workers = Enumerable.Range(0, threads).Select(_ => new Thread(() =>
            {
                long local = 0;
                while (!token.IsCancellationRequested)
                {
                    int i = Interlocked.Increment(ref next);
                    if (i >= tiles.Count) break;
                    var (x, y, w, h) = tiles[i];
                    var (tile, rays) = RenderTileAt(x, y, w, h, width, height, samples);
                    local += rays;
                    onTile?.Invoke(tile);
                }
                Interlocked.Add(ref totalRays, local);
            }) { IsBackground = true }).ToList();
            workers.ForEach(t => t.Start());
            workers.ForEach(t => t.Join());
            token.ThrowIfCancellationRequested();
            passes++;
            onProgress?.Invoke(Math.Min(100, watch.Elapsed.TotalSeconds / minSeconds * 100));
            if (watch.Elapsed.TotalSeconds >= minSeconds) break;
        }

        double seconds = Math.Max(0.001, watch.Elapsed.TotalSeconds);
        double mrays = totalRays / seconds / 1e6;
        return new CpuRenderResult(threads, seconds, passes, totalRays, mrays, (int)Math.Round(mrays * 100));
    }
}
