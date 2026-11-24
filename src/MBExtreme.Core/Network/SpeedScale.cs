namespace MBExtreme.Core.Network;

/// <summary>
/// Escala do velocímetro do teste de internet (como a dos testes de velocidade conhecidos): as marcas ficam com o mesmo espaço entre si,
/// então 0 a 10 Mbps ocupa tanto quanto 500 a 1000 e as internets lentas e rápidas aparecem bem.
/// </summary>
public static class SpeedScale
{
    public static readonly double[] Normal = { 0, 5, 10, 50, 100, 250, 500, 750, 1000 };
    public static readonly double[] Fast = { 0, 10, 50, 100, 250, 500, 1000, 2500, 5000 };

    /// <summary>Marcas para a velocidade: acima de 1 Gbps usa a escala até 5 Gbps.</summary>
    public static double[] TicksFor(double maxSeen) => maxSeen > 1000 ? Fast : Normal;

    /// <summary>Posição de 0 a 1 no arco: entre duas marcas, a posição anda proporcional ao valor.</summary>
    public static double Fraction(double mbps, IReadOnlyList<double> ticks)
    {
        if (ticks.Count < 2 || mbps <= ticks[0]) return 0;
        if (mbps >= ticks[^1]) return 1;
        for (int i = 1; i < ticks.Count; i++)
        {
            if (mbps > ticks[i]) continue;
            double within = (mbps - ticks[i - 1]) / (ticks[i] - ticks[i - 1]);
            return (i - 1 + within) / (ticks.Count - 1);
        }
        return 1;
    }

    /// <summary>Texto da marca (1000 vira "1G" na escala rápida).</summary>
    public static string Label(double tick) => tick >= 1000 && tick % 1000 == 0 && tick > 1000 ? $"{tick / 1000:0}G" : tick.ToString("0");
}
