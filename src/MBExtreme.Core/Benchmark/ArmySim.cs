namespace MBExtreme.Core.Benchmark;

/// <summary>
/// "IA" das unidades da simulação de jogo, calculada no processador a cada quadro (como num jogo de estratégia):
/// cada unidade anda na direção do exército inimigo, desvia das vizinhas (grade espacial) e, quando encosta num
/// inimigo, os dois recuam e nasce uma explosão de partículas no ponto do choque. Usa todos os núcleos.
/// </summary>
public sealed class ArmySim
{
    public const int MaxSparks = 256;
    private const float Cell = 1.2f;
    private const int GridSize = 128; // células por lado (cobre -76..76)
    private const float Half = GridSize * Cell / 2;

    private readonly float[] _x, _z, _vx, _vz, _heading;
    private readonly byte[] _team;
    private readonly int[] _cellStart = new int[GridSize * GridSize + 1];
    private readonly int[] _cellItems;
    private readonly int[] _cellOf;
    private readonly float[] _sparks = new float[MaxSparks * 4];
    private int _sparkNext;
    private readonly object _sparkLock = new();

    public int PerArmy { get; }
    public int Count => _x.Length;
    public long Collisions { get; private set; }

    public ArmySim(int perArmy, int seed = 7)
    {
        PerArmy = perArmy;
        int n = perArmy * 2;
        _x = new float[n]; _z = new float[n]; _vx = new float[n]; _vz = new float[n]; _heading = new float[n];
        _team = new byte[n];
        _cellItems = new int[n];
        _cellOf = new int[n];
        var rng = new Random(seed);
        int cols = (int)Math.Ceiling(Math.Sqrt(perArmy));
        for (int i = 0; i < n; i++)
        {
            bool blue = i >= perArmy;
            int k = blue ? i - perArmy : i;
            float sx = (k % cols) / (float)Math.Max(1, cols - 1) - 0.5f, sz = (k / cols) / (float)Math.Max(1, cols - 1) - 0.5f;
            _team[i] = blue ? (byte)1 : (byte)0;
            _x[i] = (blue ? 1 : -1) * (10f + (sx + 0.5f) * 28f) + (float)(rng.NextDouble() - 0.5) * 0.3f;
            _z[i] = sz * 52f;
            _heading[i] = blue ? MathF.PI : 0;
        }
        for (int s = 0; s < MaxSparks; s++) _sparks[s * 4 + 3] = -100;
    }

    private static int CellIndex(float x, float z)
    {
        int cx = Math.Clamp((int)((x + Half) / Cell), 0, GridSize - 1);
        int cz = Math.Clamp((int)((z + Half) / Cell), 0, GridSize - 1);
        return cz * GridSize + cx;
    }

    /// <summary>Avança a simulação. <paramref name="time"/> é o relógio da cena (para as explosões).</summary>
    public void Step(float dt, float time)
    {
        dt = Math.Clamp(dt, 0.001f, 0.05f);
        int n = Count;
        // grade espacial (contagem e prefixo)
        Array.Clear(_cellStart);
        for (int i = 0; i < n; i++) { int c = CellIndex(_x[i], _z[i]); _cellOf[i] = c; _cellStart[c + 1]++; }
        for (int c = 0; c < GridSize * GridSize; c++) _cellStart[c + 1] += _cellStart[c];
        var fill = new int[GridSize * GridSize];
        for (int i = 0; i < n; i++) { int c = _cellOf[i]; _cellItems[_cellStart[c] + fill[c]++] = i; }

        long collisions = 0;
        Parallel.For(0, n, () => 0L, (i, _, local) =>
        {
            float x = _x[i], z = _z[i];
            byte team = _team[i];
            float goalX = team == 0 ? 40f : -40f;
            float ax = MathF.Sign(goalX - x) * 2.5f + MathF.Sin(time * 0.7f + i * 0.37f) * 0.6f;
            float az = -z * 0.004f + MathF.Cos(time * 0.5f + i * 0.21f) * 0.3f;
            int cx = Math.Clamp((int)((x + Half) / Cell), 0, GridSize - 1), cz = Math.Clamp((int)((z + Half) / Cell), 0, GridSize - 1);
            bool hit = false;
            for (int gz = Math.Max(0, cz - 1); gz <= Math.Min(GridSize - 1, cz + 1); gz++)
            for (int gx = Math.Max(0, cx - 1); gx <= Math.Min(GridSize - 1, cx + 1); gx++)
            {
                int cell = gz * GridSize + gx;
                for (int k = _cellStart[cell]; k < _cellStart[cell + 1]; k++)
                {
                    int j = _cellItems[k];
                    if (j == i) continue;
                    float dx = x - _x[j], dz = z - _z[j];
                    float d2 = dx * dx + dz * dz;
                    if (d2 > 1.0f || d2 < 1e-6f) continue;
                    float inv = 1f / MathF.Sqrt(d2);
                    float push = _team[j] == team ? 2.5f : 9f;
                    ax += dx * inv * push * (1f - MathF.Sqrt(d2));
                    az += dz * inv * push * (1f - MathF.Sqrt(d2));
                    if (_team[j] != team && d2 < 0.81f) hit = true; // encostou num inimigo
                }
            }
            float damping = 1f - 1.2f * dt; // atrito que não depende do FPS
            float vx = (_vx[i] + ax * dt) * damping, vz = (_vz[i] + az * dt) * damping;
            float speed = MathF.Sqrt(vx * vx + vz * vz);
            if (speed > 4f) { vx *= 4f / speed; vz *= 4f / speed; }
            _vx[i] = vx; _vz[i] = vz;
            if (speed > 0.05f) _heading[i] = MathF.Atan2(vz, vx);
            if (hit)
            {
                local++;
                if ((i & 7) == 0) AddSpark(x, z, time);
            }
            return local;
        }, local => Interlocked.Add(ref collisions, local));

        for (int i = 0; i < n; i++)
        {
            _x[i] = Math.Clamp(_x[i] + _vx[i] * dt, -60f, 60f);
            _z[i] = Math.Clamp(_z[i] + _vz[i] * dt, -60f, 60f);
        }
        Collisions += collisions;
    }

    private void AddSpark(float x, float z, float time)
    {
        lock (_sparkLock)
        {
            int s = _sparkNext;
            if (time - _sparks[s * 4 + 3] < 1.1f) return; // a explosão mais antiga ainda está no ar
            _sparks[s * 4] = x; _sparks[s * 4 + 1] = 0; _sparks[s * 4 + 2] = z; _sparks[s * 4 + 3] = time;
            _sparkNext = (s + 1) % MaxSparks;
        }
    }

    /// <summary>Copia as unidades no formato do sombreador: x, z, direção, time (0 amarelo, 1 azul).</summary>
    public void WriteUnits(Span<float> destination)
    {
        for (int i = 0; i < Count; i++)
        {
            destination[i * 4] = _x[i];
            destination[i * 4 + 1] = _z[i];
            destination[i * 4 + 2] = _heading[i];
            destination[i * 4 + 3] = _team[i];
        }
    }

    /// <summary>Explosões no formato do sombreador: x, y, z, início. A altura (y) é calculada no sombreador pelo terreno.</summary>
    public ReadOnlySpan<float> Sparks => _sparks;

    public (float X, float Z) Position(int i) => (_x[i], _z[i]);
}
