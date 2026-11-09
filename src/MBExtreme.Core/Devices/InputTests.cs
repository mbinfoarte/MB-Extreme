namespace MBExtreme.Core.Devices;

/// <summary>Uma leitura de um analógico, de -1 a 1 em cada eixo.</summary>
public readonly record struct StickSample(double X, double Y)
{
    public double Radius => Math.Sqrt(X * X + Y * Y);

    /// <summary>XInput entrega de -32768 a 32767.</summary>
    public static StickSample FromXInput(short x, short y) => new(Normalize(x), Normalize(y));

    public static double Normalize(short value) => value < 0 ? value / 32768.0 : value / 32767.0;
}

/// <summary>
/// Contas dos testes de controle: deriva (o analógico "andando sozinho" solto), circularidade (o quanto o curso faz um círculo)
/// e a zona morta sugerida.
/// </summary>
public static class StickMath
{
    /// <summary>Deriva: o maior afastamento do centro com o analógico solto (0 = perfeito).</summary>
    public static double Drift(IReadOnlyList<StickSample> atRest) => atRest.Count == 0 ? 0 : atRest.Max(s => s.Radius);

    /// <summary>Zona morta sugerida: a deriva com uma folga, nunca menos que 5% nem mais que 30%.</summary>
    public static double SuggestedDeadzone(double drift) => Math.Clamp(Math.Ceiling((drift * 1.25 + 0.02) * 100) / 100, 0.05, 0.30);

    /// <summary>Deriva acima de 10% com o analógico solto já atrapalha o jogo (o personagem ou a mira andam sozinhos).</summary>
    public static bool HasDrift(double drift) => drift >= 0.10;

    /// <summary>
    /// Erro de circularidade: divide o círculo em 36 fatias, pega o maior alcance em cada uma (girando o analógico encostado na borda)
    /// e mede o quanto se afasta de 1. 0% = círculo perfeito; acima de ~10% o movimento na diagonal fica desigual.
    /// Fatias sem leitura não contam (é preciso girar todo o círculo para o número valer).
    /// </summary>
    public static (double ErrorPercent, int Coverage) Circularity(IReadOnlyList<StickSample> samples, int slices = 36)
    {
        var max = new double[slices];
        foreach (var s in samples)
        {
            if (s.Radius < 0.5) continue;
            double angle = Math.Atan2(s.Y, s.X);
            int slice = (int)((angle + Math.PI) / (2 * Math.PI) * slices) % slices;
            max[slice] = Math.Max(max[slice], s.Radius);
        }
        var covered = max.Where(m => m > 0).ToList();
        if (covered.Count == 0) return (0, 0);
        double error = covered.Average(m => Math.Abs(Math.Min(m, 1.5) - 1.0)) * 100;
        return (error, covered.Count * 100 / slices);
    }
}

/// <summary>Contas do teste de mouse.</summary>
public static class MouseMath
{
    /// <summary>
    /// Taxa de atualização (Hz) a partir dos horários (ms) dos movimentos recebidos pela entrada bruta do Windows:
    /// usa a mediana dos intervalos só dentro de movimentos contínuos (pausas acima de 50 ms são ignoradas).
    /// </summary>
    public static double? PollingRate(IReadOnlyList<double> timestampsMs)
    {
        if (timestampsMs.Count < 20) return null;
        var intervals = timestampsMs.Zip(timestampsMs.Skip(1), (a, b) => b - a).Where(d => d > 0.05 && d < 50).ToList();
        if (intervals.Count < 10) return null;
        var sorted = intervals.OrderBy(v => v).ToList();
        double median = sorted[sorted.Count / 2];
        return 1000.0 / median;
    }

    /// <summary>Arredonda para a taxa de fábrica mais próxima (125, 250, 500, 1000, 2000, 4000, 8000 Hz).</summary>
    public static int NearestStandard(double hz)
    {
        int[] standard = { 125, 250, 500, 1000, 2000, 4000, 8000 };
        return standard.OrderBy(s => Math.Abs(Math.Log(s / Math.Max(1, hz)))).First();
    }

    /// <summary>Dois cliques em menos de 80 ms quase sempre são o botão "duplicando" (defeito comum de micro-switch).</summary>
    public static bool IsChatter(double msBetweenClicks) => msBetweenClicks is > 0 and < 80;
}
