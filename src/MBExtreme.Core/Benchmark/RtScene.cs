using System.Runtime.InteropServices;

namespace MBExtreme.Core.Benchmark;

/// <summary>Instância da cena de ray tracing no formato do DirectX 12 (D3D12_RAYTRACING_INSTANCE_DESC, 64 bytes).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct RtInstance
{
    // matriz 3x4 por linhas: [rotação·escala | posição]
    public float M00, M01, M02, M03, M10, M11, M12, M13, M20, M21, M22, M23;
    public uint IdAndMask;            // 24 bits de ID (malha | material << 8) + 8 bits de máscara
    public uint HitGroupAndFlags;     // não usado (ray tracing em compute): 0
    public ulong Blas;                // endereço na placa da malha (BLAS)

    public readonly uint Mesh => IdAndMask & 0xFF;
    public readonly uint Material => (IdAndMask >> 8) & 0xFF;
}

/// <summary>
/// Arena futurista do teste de ray tracing: piso espelhado, esferas cromadas e douradas, anéis girando, caixas e pilares com
/// neon. As malhas são montadas uma vez; a cada quadro as instâncias se mexem (a estrutura de aceleração é refeita, como num jogo).
/// </summary>
public static class RtScene
{
    public const int Floor = 0, Sphere = 1, Torus = 2, Box = 3;
    public const int MatFloor = 0, MatChrome = 1, MatGold = 2, MatYellow = 3, MatBlue = 4, MatPillar = 5;

    /// <summary>Hash da fonte (Common + RayTracing) usada para gerar RayTracing.dxil. Atualizado por tools/shaders/build-raytracing.sh.</summary>
    public const string RayTracingSourceSha256 = "92c89893092b3f42344c1b38343375ff0fb74f6d3403dfedf616100f188ba357";

    /// <summary>Malhas: vértices (posição xyzw e normal xyzw, 8 floats), índices locais e, por malha, (início dos índices, início dos vértices, nº de índices, nº de vértices).</summary>
    public sealed record Meshes(float[] Vertices, uint[] Indices, uint[] Info)
    {
        public int Count => Info.Length / 4;
        public int VertexCount => Vertices.Length / 8;
        public int TriangleCount => Indices.Length / 3;
    }

    public static Meshes Build()
    {
        var v = new List<float>();
        var idx = new List<uint>();
        var info = new List<uint>();
        void Begin() { info.Add((uint)idx.Count); info.Add((uint)(v.Count / 8)); info.Add(0); info.Add(0); }
        void End() { int m = info.Count - 4; info[m + 2] = (uint)idx.Count - info[m]; info[m + 3] = (uint)(v.Count / 8) - info[m + 1]; }
        void Vert(float x, float y, float z, float nx, float ny, float nz) { v.AddRange(new[] { x, y, z, 1f, nx, ny, nz, 0f }); }

        // 0: piso (quadrado de -1 a 1, normal para cima)
        Begin();
        Vert(-1, 0, -1, 0, 1, 0); Vert(1, 0, -1, 0, 1, 0); Vert(1, 0, 1, 0, 1, 0); Vert(-1, 0, 1, 0, 1, 0);
        idx.AddRange(new uint[] { 0, 2, 1, 0, 3, 2 });
        End();

        // 1: esfera lisa (raio 1)
        Begin();
        const int sl = 48, st = 24;
        for (int j = 0; j <= st; j++)
            for (int i = 0; i <= sl; i++)
            {
                double th = Math.PI * j / st, ph = 2 * Math.PI * i / sl;
                float x = (float)(Math.Sin(th) * Math.Cos(ph)), y = (float)Math.Cos(th), z = (float)(Math.Sin(th) * Math.Sin(ph));
                Vert(x, y, z, x, y, z);
            }
        for (int j = 0; j < st; j++)
            for (int i = 0; i < sl; i++)
            {
                uint a = (uint)(j * (sl + 1) + i), b = a + 1, c = a + sl + 1, d = c + 1;
                idx.AddRange(new[] { a, c, b, b, c, d });
            }
        End();

        // 2: anel (toro, raio 1 e espessura 0,28)
        Begin();
        const int ru = 64, rv = 24;
        const double R = 1.0, r = 0.28;
        for (int j = 0; j <= rv; j++)
            for (int i = 0; i <= ru; i++)
            {
                double u = 2 * Math.PI * i / ru, w = 2 * Math.PI * j / rv;
                double cx = Math.Cos(u), cz = Math.Sin(u);
                float nx = (float)(Math.Cos(w) * cx), ny = (float)Math.Sin(w), nz = (float)(Math.Cos(w) * cz);
                Vert((float)((R + r * Math.Cos(w)) * cx), (float)(r * Math.Sin(w)), (float)((R + r * Math.Cos(w)) * cz), nx, ny, nz);
            }
        for (int j = 0; j < rv; j++)
            for (int i = 0; i < ru; i++)
            {
                uint a = (uint)(j * (ru + 1) + i), b = a + 1, c = a + ru + 1, d = c + 1;
                idx.AddRange(new[] { a, c, b, b, c, d });
            }
        End();

        // 3: caixa (meia-medida 1, uma normal por face)
        Begin();
        float[,] n = { { 1, 0, 0 }, { -1, 0, 0 }, { 0, 1, 0 }, { 0, -1, 0 }, { 0, 0, 1 }, { 0, 0, -1 } };
        for (int f = 0; f < 6; f++)
        {
            float nx = n[f, 0], ny = n[f, 1], nz = n[f, 2];
            // dois eixos da face: a (perpendicular à normal) e b = n × a
            float ax = ny != 0 ? 1 : 0, ay = ny != 0 ? 0 : 1, az = 0;
            float bx = ny * az - nz * ay, by = nz * ax - nx * az, bz = nx * ay - ny * ax;
            uint start = (uint)(v.Count / 8) - info[^3];
            foreach (var (s, t) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
                Vert(nx + ax * s + bx * t, ny + ay * s + by * t, nz + az * s + bz * t, nx, ny, nz);
            idx.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }
        End();

        return new Meshes(v.ToArray(), idx.ToArray(), info.ToArray());
    }

    private record struct Item(int Mesh, int Material, float X, float Y, float Z, float Scale, float Spin, float Bob, float Seed);

    private static readonly Item[] Items = Layout();

    /// <summary>Quantas instâncias a cena tem.</summary>
    public static int InstanceCount => Items.Length;

    private static Item[] Layout()
    {
        var list = new List<Item> { new(Floor, MatFloor, 0, 0, 0, 120, 0, 0, 0) };
        var rng = new Random(2026);
        // grade de objetos, livre perto do caminho da câmera (raio 30)
        for (int gx = -7; gx <= 7; gx++)
            for (int gz = -7; gz <= 7; gz++)
            {
                float x = gx * 4.6f, z = gz * 4.6f;
                double d = Math.Sqrt(x * x + z * z);
                if (d < 7 || (d > 25 && d < 35)) continue;
                int kind = rng.Next(10);
                float seed = (float)rng.NextDouble();
                if (kind < 5)
                {
                    int mat = new[] { MatChrome, MatGold, MatYellow, MatBlue, MatChrome }[rng.Next(5)];
                    float s = 0.7f + (float)rng.NextDouble() * 0.8f;
                    list.Add(new(Sphere, mat, x, s, z, s, 0, 0.6f, seed));
                }
                else if (kind < 8)
                {
                    float s = 0.9f + (float)rng.NextDouble() * 0.6f;
                    list.Add(new(Torus, rng.Next(2) == 0 ? MatGold : MatChrome, x, 1.6f + s, z, s, 1.2f + (float)rng.NextDouble(), 0.3f, seed));
                }
                else
                {
                    float s = 0.6f + (float)rng.NextDouble() * 0.5f;
                    list.Add(new(Box, rng.Next(2) == 0 ? MatYellow : MatBlue, x, s, z, s, 0.5f, 0, seed));
                }
            }
        // pilares com neon em volta da arena
        for (int i = 0; i < 24; i++)
        {
            double a = i * Math.PI * 2 / 24;
            list.Add(new(Box, MatPillar, (float)(Math.Cos(a) * 44), 6, (float)(Math.Sin(a) * 44), -6, 0, 0, i));
        }
        // centro: esfera cromada grande com três anéis dourados girando
        list.Add(new(Sphere, MatChrome, 0, 3.2f, 0, 2.6f, 0, 0.4f, 0.5f));
        for (int k = 0; k < 3; k++) list.Add(new(Torus, MatGold, 0, 3.2f, 0, 3.6f + k * 0.9f, 0.7f + k * 0.35f, 0.4f, k * 0.33f));
        return list.ToArray();
    }

    /// <summary>Monta as instâncias no instante <paramref name="time"/>. <paramref name="blas"/>: endereço de cada malha na placa.</summary>
    public static void Animate(Span<RtInstance> target, double time, ReadOnlySpan<ulong> blas)
    {
        for (int i = 0; i < Items.Length && i < target.Length; i++)
        {
            var it = Items[i];
            float t = (float)time;
            float y = it.Y + (it.Bob > 0 ? MathF.Sin(t * 1.3f + it.Seed * 6.28f) * it.Bob : 0);
            float yaw = it.Spin * t + it.Seed * 6.28f;
            float tilt = it.Mesh == Torus ? 0.9f + 0.5f * MathF.Sin(t * 0.7f + it.Seed * 9f) : 0;
            // escala: pilar = caixa fina e alta (Scale negativo = pilar)
            float sx = it.Scale, sy = it.Scale, sz = it.Scale;
            if (it.Scale < 0) { sx = 0.7f; sy = -it.Scale; sz = 0.7f; }
            float cy = MathF.Cos(yaw), syw = MathF.Sin(yaw), cx = MathF.Cos(tilt), sxw = MathF.Sin(tilt);
            // R = RotY(yaw) · RotX(tilt)
            ref var o = ref target[i];
            o.M00 = cy * sx; o.M01 = syw * sxw * sy; o.M02 = syw * cx * sz; o.M03 = it.X;
            o.M10 = 0; o.M11 = cx * sy; o.M12 = -sxw * sz; o.M13 = y;
            o.M20 = -syw * sx; o.M21 = cy * sxw * sy; o.M22 = cy * cx * sz; o.M23 = it.Z;
            o.IdAndMask = (uint)it.Mesh | ((uint)it.Material << 8) | (0xFFu << 24);
            o.HitGroupAndFlags = 0;
            o.Blas = blas.Length > it.Mesh ? blas[it.Mesh] : 0;
        }
    }
}

/// <summary>Resultado do teste de ray tracing (DirectX 12): FPS com RT desligado e ligado e o custo do RT.</summary>
public sealed record RayTracingResult(string GpuName, GpuSceneStat? Off, GpuSceneStat? On, string? Unsupported = null)
{
    public double OffFps => Off?.AvgFps ?? 0;
    public double OnFps => On?.AvgFps ?? 0;

    /// <summary>Quanto do FPS o ray tracing custa (0 a 100%).</summary>
    public double CostPercent => OffFps > 0 && OnFps > 0 ? Math.Clamp((1 - OnFps / OffFps) * 100, 0, 100) : 0;
}
