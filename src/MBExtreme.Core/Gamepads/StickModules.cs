namespace MBExtreme.Core.Gamepads;

/// <summary>Tipo de módulo de analógico instalado no controle.</summary>
public enum StickModule { Original, HallEffect, Tmr }

/// <summary>Um item da conferência depois da calibração.</summary>
public sealed record StickCheck(string Item, bool Ok, string Detail);

/// <summary>
/// Conferência de analógico depois da troca/calibração. Módulos magnéticos (Hall Effect e TMR) não gastam,
/// então o padrão de aprovação é mais rígido: centro quase perfeito, alcance completo em todas as direções e círculo redondo.
/// </summary>
public static class StickModules
{
    public static string Name(StickModule m) => m switch
    {
        StickModule.HallEffect => "Hall Effect (magnético)",
        StickModule.Tmr => "TMR (magnético de alta precisão)",
        _ => "Original (potenciômetro)",
    };

    /// <summary>Limites: drift máximo no centro, erro de circularidade máximo (%), alcance mínimo da borda.</summary>
    public static (double Drift, double Circle, double Reach) Limits(StickModule m) => m switch
    {
        StickModule.Tmr => (0.015, 6, 0.97),
        StickModule.HallEffect => (0.02, 8, 0.96),
        _ => (0.025, 10, 0.93),
    };

    public static IReadOnlyList<StickCheck> Evaluate(StickModule module, StickMeasure after, IReadOnlyList<double> edge)
    {
        var (maxDrift, maxCircle, minReach) = Limits(module);
        var valid = edge.Where(v => v > 0.2).ToList();
        double reached = valid.Count == 0 ? 0 : valid.Count(v => v >= minReach) / (double)edge.Count;
        double over = valid.Count == 0 ? 0 : valid.Max();
        var list = new List<StickCheck>
        {
            new("Centro (sem drift)", after.DriftPeak <= maxDrift, $"{after.DriftPeak * 100:0.0}% (máximo {maxDrift * 100:0.0}%)"),
            new("Alcance até a borda", reached >= 0.9, $"{reached * 100:0}% das direções chegam a {minReach * 100:0}%"),
            new("Círculo redondo", after.CircularityError <= maxCircle, $"erro {after.CircularityError:0.0}% (máximo {maxCircle:0}%)"),
            new("Sem passar do limite", over <= 1.15, over <= 1.15 ? "ok" : $"chega a {over * 100:0}%: refaça a calibração do alcance"),
        };
        return list;
    }

    /// <summary>Ao empurrar para cima, o Y tem que ficar negativo (para cima na tela); positivo = ímã ou sensor montado invertido.</summary>
    public static bool LooksInverted(double yWhenPushingUp) => yWhenPushingUp > 0.5;
}
