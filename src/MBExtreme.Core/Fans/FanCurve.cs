using System.Text.Json.Serialization;

namespace MBExtreme.Core.Fans;

/// <summary>Ponto da curva: a partir desta temperatura, a ventoinha vai a este percentual.</summary>
public sealed record CurvePoint(double TemperatureC, double Percent);

/// <summary>Curva temperatura → velocidade, com interpolação linear entre os pontos.</summary>
public sealed class FanCurve
{
    public FanCurve() { }

    public FanCurve(IEnumerable<CurvePoint> points) => Points = Normalize(points);

    [JsonInclude]
    public List<CurvePoint> Points { get; private set; } = new();

    /// <summary>Ordena por temperatura, limita 0 a 100% e 0 a 110 °C, e garante que a velocidade nunca diminui quando a temperatura sobe.</summary>
    public static List<CurvePoint> Normalize(IEnumerable<CurvePoint> points)
    {
        var sorted = points
            .Select(p => new CurvePoint(Math.Clamp(p.TemperatureC, 0, 110), Math.Clamp(p.Percent, 0, 100)))
            .GroupBy(p => p.TemperatureC).Select(g => g.Last())
            .OrderBy(p => p.TemperatureC).ToList();
        double floor = 0;
        for (int i = 0; i < sorted.Count; i++)
        {
            floor = Math.Max(floor, sorted[i].Percent);
            sorted[i] = sorted[i] with { Percent = floor };
        }
        return sorted;
    }

    public double Evaluate(double temperatureC)
    {
        if (Points.Count == 0) return 100;   // sem curva: não arrisca
        if (temperatureC <= Points[0].TemperatureC) return Points[0].Percent;
        if (temperatureC >= Points[^1].TemperatureC) return Points[^1].Percent;
        for (int i = 1; i < Points.Count; i++)
        {
            var a = Points[i - 1];
            var b = Points[i];
            if (temperatureC <= b.TemperatureC)
            {
                double t = (temperatureC - a.TemperatureC) / (b.TemperatureC - a.TemperatureC);
                return a.Percent + (b.Percent - a.Percent) * t;
            }
        }
        return Points[^1].Percent;
    }

    public static FanCurve Silent => new(new[] { new CurvePoint(30, 25), new CurvePoint(50, 30), new CurvePoint(65, 45), new CurvePoint(75, 65), new CurvePoint(85, 100) });
    public static FanCurve Balanced => new(new[] { new CurvePoint(30, 30), new CurvePoint(45, 40), new CurvePoint(60, 55), new CurvePoint(70, 75), new CurvePoint(80, 100) });
    public static FanCurve Performance => new(new[] { new CurvePoint(30, 40), new CurvePoint(45, 55), new CurvePoint(55, 70), new CurvePoint(65, 90), new CurvePoint(72, 100) });
}

public enum FanChannelKind { Case, Cpu, Gpu, Pump }

/// <summary>Regras de segurança que valem por cima de qualquer curva do usuário.</summary>
public static class FanSafety
{
    public const double PumpMinimum = 60;
    public const double CasingMinimum = 20;
    public const double CpuMinimum = 30;

    /// <summary>Mínimo permitido para o tipo de canal (bomba de líquido parada superaquece o processador).</summary>
    public static double Minimum(FanChannelKind kind, double sourceTemperatureC) => kind switch
    {
        FanChannelKind.Pump => PumpMinimum,
        FanChannelKind.Cpu => CpuMinimum,
        FanChannelKind.Gpu => sourceTemperatureC >= 55 ? 30 : 0,   // ventoinha de placa de vídeo pode ficar parada em repouso
        _ => CasingMinimum,
    };

    /// <summary>Aplica as regras: temperatura alta força velocidade alta, não importa a curva.</summary>
    public static double Clamp(double wanted, FanChannelKind kind, double sourceTemperatureC)
    {
        double value = Math.Clamp(wanted, Minimum(kind, sourceTemperatureC), 100);
        if (sourceTemperatureC >= 90) value = 100;
        else if (sourceTemperatureC >= 80) value = Math.Max(value, 80);
        return Math.Round(value);
    }

    /// <summary>Sem leitura da temperatura de referência, vai a 100% (melhor barulho do que calor).</summary>
    public static double FailSafe => 100;

    /// <summary>
    /// Suaviza a mudança: sobe na hora, mas desce devagar (no máximo <paramref name="maxDropPerStep"/> por passo) e só desce
    /// depois que a temperatura cair mais que a histerese, para a ventoinha não ficar oscilando.
    /// </summary>
    public static double Smooth(double current, double target, double temperatureC, double lastTemperatureC, double hysteresisC = 2, double maxDropPerStep = 5)
    {
        if (target >= current) return target;
        if (lastTemperatureC - temperatureC < hysteresisC && current - target < 10) return current;
        return Math.Max(target, current - maxDropPerStep);
    }
}

public sealed class FanChannelConfig
{
    public string ChannelId { get; set; } = "";
    public FanChannelKind Kind { get; set; } = FanChannelKind.Case;

    /// <summary>"auto" (BIOS/firmware manda), "manual" (percentual fixo) ou "curve".</summary>
    public string Mode { get; set; } = "auto";
    public double ManualPercent { get; set; } = 50;
    public string? SourceSensorId { get; set; }
    public List<CurvePoint> Curve { get; set; } = FanCurve.Balanced.Points;
}
