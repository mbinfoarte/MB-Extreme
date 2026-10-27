using System.Globalization;

namespace MBExtreme.Core.Recording;

/// <summary>Um comando para um filtro do FFmpeg que está rodando (enviado pela tecla "c" na entrada do FFmpeg).</summary>
public sealed record FilterCommand(string Target, string Command, string Argument, int DelayMs = 0)
{
    /// <summary>A linha que o FFmpeg lê depois da tecla "c": alvo, tempo (-1 = agora), comando e argumento.</summary>
    public string Line => $"{Target} -1 {Command} {Argument}";
}

/// <summary>
/// Troca de cena com o estúdio no ar, sem reiniciar: muda o seletor (streamselect) e, no esmaecer, os pesos da mistura (mix) aos poucos.
/// O FFmpeg lê um comando a cada ~100 ms, então o esmaecer usa poucos passos (cerca de meio segundo).
/// </summary>
public static class SceneSwitch
{
    // nomes das instâncias no grafo; o FFmpeg aceita o alvo "tipo@nome" (testado no FFmpeg 6.1)
    public const string Select = "cena";
    public const string SelectOut = "cena_sai";
    public const string SelectIn = "cena_entra";
    public const string Mix = "cena_mistura";
    public const int FadeSteps = 5;
    public const int StepMs = 110;

    private static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string I(int v) => Math.Max(0, v).ToString(CultureInfo.InvariantCulture);

    /// <param name="graph">Como o FFmpeg foi montado (com ou sem a mistura do esmaecer).</param>
    /// <param name="wanted">Como a pessoa quer trocar agora.</param>
    public static IReadOnlyList<FilterCommand> Plan(SceneTransition graph, SceneTransition wanted, int to)
    {
        string scene = I(to);
        if (graph == SceneTransition.Cut)
            return new[] { new FilterCommand("streamselect@" + Select, "map", scene) };

        if (wanted == SceneTransition.Cut)
        {
            return new[]
            {
                new FilterCommand("streamselect@" + SelectOut, "map", scene),
                new FilterCommand("streamselect@" + SelectIn, "map", scene),
                new FilterCommand("mix@" + Mix, "weights", "1 0"),
            };
        }

        // esmaecer: o seletor "entra" recebe a cena nova, os pesos passam de "1 0" para "0 1" e, no fim, o "sai" também fica na cena nova
        var plan = new List<FilterCommand> { new("streamselect@" + SelectIn, "map", scene) };
        for (int k = 1; k <= FadeSteps; k++)
        {
            double weight = k / (double)FadeSteps;
            plan.Add(new FilterCommand("mix@" + Mix, "weights", $"{N(1 - weight)} {N(weight)}", StepMs));
        }
        plan.Add(new FilterCommand("streamselect@" + SelectOut, "map", scene, StepMs));
        plan.Add(new FilterCommand("mix@" + Mix, "weights", "1 0"));
        return plan;
    }

    /// <summary>Linhas que o FFmpeg escreve quando recebe um comando: não são erro e não devem aparecer como aviso.</summary>
    public static bool IsCommandEcho(string line) =>
        line.StartsWith("Enter command", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("Command reply", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("Processing command", StringComparison.OrdinalIgnoreCase);
}
