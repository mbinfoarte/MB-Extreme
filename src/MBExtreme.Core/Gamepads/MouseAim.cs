namespace MBExtreme.Core.Gamepads;

/// <summary>Jogo de tiro com sensibilidade linear: graus girados por contagem do mouse com sensibilidade 1 (o "yaw").</summary>
public sealed record AimGame(string Name, double Yaw, string Hint);

/// <summary>Estilo de mira: distância de mouse para uma volta completa (cm/360).</summary>
public sealed record AimStyle(string Name, double CmPer360, string Hint);

/// <summary>Contas da mira do mouse: cm/360, eDPI, conversão entre jogos e taxa de atualização medida.</summary>
public static class MouseAim
{
    public static IReadOnlyList<AimGame> Games { get; } = new[]
    {
        new AimGame("Call of Duty (2019 em diante)", 0.0066, "Opções › Mouse › Sensibilidade do mouse (multiplicador da mira 1,00)."),
        new AimGame("Overwatch 2", 0.0066, "Opções › Controles › Sensibilidade."),
        new AimGame("Counter-Strike 2", 0.022, "Opções › Teclado e mouse › Sensibilidade."),
        new AimGame("Apex Legends", 0.022, "Opções › Mouse/Teclado › Sensibilidade do mouse."),
        new AimGame("Titanfall 2", 0.022, "Opções › Mouse › Sensibilidade."),
        new AimGame("Valorant", 0.07, "Configurações › Geral › Sensibilidade › Mira."),
    };

    public static IReadOnlyList<AimStyle> Styles { get; } = new[]
    {
        new AimStyle("Precisão (fuzil, sniper)", 45, "Braço inteiro: mira muito estável, giros mais lentos."),
        new AimStyle("Equilibrado", 32, "Braço e pulso: o mais usado em COD e Battlefield."),
        new AimStyle("Rápido (SMG, perto)", 22, "Pulso: giros rápidos, exige mais controle."),
    };

    /// <summary>Quantos centímetros de mouse para uma volta de 360°.</summary>
    public static double CmPer360(double yaw, double sensitivity, int dpi) =>
        yaw <= 0 || sensitivity <= 0 || dpi <= 0 ? 0 : 360.0 / (yaw * sensitivity * dpi) * 2.54;

    /// <summary>Sensibilidade do jogo que dá <paramref name="cm"/> cm por volta com este DPI.</summary>
    public static double SensitivityFor(double yaw, double cm, int dpi) =>
        yaw <= 0 || cm <= 0 || dpi <= 0 ? 0 : 360.0 * 2.54 / (yaw * cm * dpi);

    /// <summary>Mesma sensação de mira em outro jogo (e, se quiser, com outro DPI).</summary>
    public static double Convert(AimGame from, double sensitivity, int fromDpi, AimGame to, int toDpi) =>
        SensitivityFor(to.Yaw, CmPer360(from.Yaw, sensitivity, fromDpi), toDpi);

    public static double EDpi(double sensitivity, int dpi) => sensitivity * dpi;

    /// <summary>
    /// Taxa de atualização do mouse pelos instantes (ms) em que chegaram os movimentos: usa a mediana dos intervalos
    /// (ignora pausas) e arredonda para a taxa comercial mais próxima (125, 250, 500, 1000, 2000, 4000, 8000 Hz).
    /// </summary>
    public static (int Hz, double Measured)? PollingRate(IReadOnlyList<double> timesMs)
    {
        if (timesMs.Count < 50) return null;
        var gaps = new List<double>(timesMs.Count);
        for (int i = 1; i < timesMs.Count; i++)
        {
            double g = timesMs[i] - timesMs[i - 1];
            if (g > 0.01 && g < 20) gaps.Add(g);
        }
        if (gaps.Count < 40) return null;
        gaps.Sort();
        double median = gaps[gaps.Count / 2];
        double measured = 1000.0 / median;
        int[] standard = { 125, 250, 500, 1000, 2000, 4000, 8000 };
        int hz = standard.OrderBy(s => Math.Abs(Math.Log(s / measured))).First();
        return (hz, measured);
    }

    /// <summary>O que acertar dentro do Call of Duty para a mira do mouse ficar igual sempre.</summary>
    public static IReadOnlyList<string> CodTips { get; } = new[]
    {
        "Aceleração do mouse: 0 · Filtragem do mouse: 0 · Suavização: desligada.",
        "Multiplicador de sensibilidade na mira (ADS): 1,00 e transição da sensibilidade: Instantânea.",
        "Coeficiente de distância do monitor: 1,33 (padrão) — a mira com zoom fica com a mesma sensação da mira sem zoom.",
        "Campo de visão entre 100 e 110 e campo de visão na mira: Afetado.",
        "No Windows: aceleração do ponteiro desligada (botão abaixo). O COD lê o mouse direto, então a velocidade do ponteiro não importa.",
    };
}
