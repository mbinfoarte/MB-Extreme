namespace MBExtreme.Core.Display;

/// <summary>Um modo de vídeo informado pelo driver: resolução e taxa de atualização.</summary>
public sealed record DisplayMode(int Width, int Height, int Hz);

/// <summary>Um monitor ligado, com o modo atual e as taxas que ele aceita na resolução de agora.</summary>
public sealed record MonitorRates(string DeviceName, string Name, bool Primary, int Width, int Height, int CurrentHz, IReadOnlyList<int> Rates)
{
    public int MaxHz => Rates.Count == 0 ? CurrentHz : Rates.Max();
    public bool BelowMax => CurrentHz > 0 && MaxHz > CurrentHz + 1;
    public bool Vertical => Height > Width;
}

/// <summary>Contas das taxas de atualização (sem Windows, para testar).</summary>
public static class RefreshRates
{
    /// <summary>Taxas aceitas na resolução atual, sem repetir e sem as de "compatibilidade" abaixo de 50 Hz (1 Hz, 23/24/25/29/30 de TV).</summary>
    public static IReadOnlyList<int> For(IEnumerable<DisplayMode> modes, int width, int height) =>
        modes.Where(m => m.Width == width && m.Height == height && m.Hz >= 50)
             .Select(m => m.Hz).Distinct().OrderBy(h => h).ToList();

    /// <summary>Taxa da prévia/animações: a do monitor, entre 30 e 120 (acima de 120 só pesa sem diferença visível na prévia).</summary>
    public static int PreviewFps(int monitorHz, bool economy) => economy ? 30 : Math.Clamp(monitorHz <= 0 ? 60 : monitorHz, 30, 120);
}
