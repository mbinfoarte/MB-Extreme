namespace MBExtreme.Core.Recording;

/// <summary>
/// Detecta lances marcantes (gol, jogada decisiva) pelo som do jogo: a torcida e o locutor sobem de repente em relação ao volume normal da partida.
/// É uma estimativa: pode salvar um clipe de um momento barulhento que não era um gol, e pode deixar passar um lance silencioso.
/// </summary>
public sealed class HighlightDetector
{
    public enum Sensitivity { Low, Medium, High }

    private readonly Queue<(double Seconds, double Peak)> _samples = new();
    private double _lastTrigger = double.NegativeInfinity;
    private double? _first;

    public HighlightDetector(Sensitivity sensitivity = Sensitivity.Medium, double cooldownSeconds = 45)
    {
        Factor = sensitivity switch { Sensitivity.Low => 2.4, Sensitivity.High => 1.5, _ => 1.9 };
        CooldownSeconds = cooldownSeconds;
    }

    /// <summary>Quantas vezes o volume dos últimos segundos precisa passar do volume normal.</summary>
    public double Factor { get; }

    public double CooldownSeconds { get; }

    /// <summary>Janela do "volume agora" (segundos).</summary>
    public double ShortWindow { get; init; } = 2.0;

    /// <summary>Janela do "volume normal" (segundos).</summary>
    public double LongWindow { get; init; } = 60.0;

    /// <summary>Nos primeiros segundos não há o que comparar.</summary>
    public double WarmUpSeconds { get; init; } = 20.0;

    /// <summary>Abaixo disso é silêncio ou ruído de fundo, nunca um lance.</summary>
    public double MinimumLevel { get; init; } = 0.12;

    /// <summary>Alimente com o volume (0 a 1) e o relógio (em segundos). Devolve true quando um lance foi detectado.</summary>
    public bool Feed(double peak, double nowSeconds)
    {
        _first ??= nowSeconds;
        _samples.Enqueue((nowSeconds, Math.Clamp(peak, 0, 1)));
        while (_samples.Count > 0 && nowSeconds - _samples.Peek().Seconds > LongWindow) _samples.Dequeue();

        if (nowSeconds - _first < WarmUpSeconds) return false;
        if (nowSeconds - _lastTrigger < CooldownSeconds) return false;

        double shortSum = 0, longSum = 0;
        int shortCount = 0, longCount = 0;
        foreach (var (t, p) in _samples)
        {
            longSum += p;
            longCount++;
            if (nowSeconds - t <= ShortWindow) { shortSum += p; shortCount++; }
        }

        if (shortCount < 5 || longCount < 20) return false;
        double now = shortSum / shortCount;
        // o volume normal é medido SEM os últimos segundos, senão o próprio lance o puxaria para cima
        double rest = longCount - shortCount;
        if (rest < 10) return false;
        double normal = (longSum - shortSum) / rest;
        if (now < MinimumLevel || now < normal * Factor) return false;

        _lastTrigger = nowSeconds;
        return true;
    }
}
