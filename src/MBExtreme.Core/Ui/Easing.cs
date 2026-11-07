namespace MBExtreme.Core.Ui;

/// <summary>Contas das animações da interface (separadas para testar): curvas de suavização e a contagem de números.</summary>
public static class Easing
{
    /// <summary>Começa rápido e termina devagar (cúbica), de 0 a 1.</summary>
    public static double OutCubic(double t)
    {
        t = Math.Clamp(t, 0, 1);
        double u = 1 - t;
        return 1 - u * u * u;
    }

    /// <summary>Valor no meio de uma contagem animada (ex.: a nota de saúde subindo de 0 a 87).</summary>
    public static double Count(double from, double to, TimeSpan elapsed, TimeSpan duration) =>
        duration <= TimeSpan.Zero ? to : from + (to - from) * OutCubic(elapsed.TotalMilliseconds / duration.TotalMilliseconds);

    /// <summary>Atraso de cada item numa entrada em cascata (para a lista não demorar demais quando é grande).</summary>
    public static TimeSpan Stagger(int index, int stepMs = 35, int maxMs = 420) => TimeSpan.FromMilliseconds(Math.Min(index * stepMs, maxMs));
}
