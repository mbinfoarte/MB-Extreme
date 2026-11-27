namespace MBExtreme.Core.Recording;

/// <summary>
/// Codificador sobrecarregado: a velocidade do FFmpeg ("speed") fica abaixo de 0,93x por 10 segundos seguidos (ele não dá conta de
/// codificar em tempo real e a live perde quadros). Avisa uma vez e só volta a avisar depois de 2 minutos.
/// </summary>
public sealed class EncoderLoad
{
    public const double Limit = 0.93;
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(2);

    private DateTime? _slowSince;
    private DateTime _lastAlert = DateTime.MinValue;

    /// <summary>Lê uma medida de velocidade (0 = ainda sem medida). Devolve true quando é hora de aliviar o codificador.</summary>
    public bool Feed(double speed, DateTime now)
    {
        if (speed <= 0) return false;
        if (speed >= Limit) { _slowSince = null; return false; }
        _slowSince ??= now;
        if (now - _slowSince.Value < Window || now - _lastAlert < Cooldown) return false;
        _lastAlert = now;
        _slowSince = null;
        return true;
    }

    public void Reset() { _slowSince = null; _lastAlert = DateTime.MinValue; }

    /// <summary>Um degrau a menos de esforço: Qualidade → Equilibrado → Rápido. Null = já está no mais leve.</summary>
    public static EncoderQuality? StepDown(EncoderQuality q) => q switch
    {
        EncoderQuality.Quality => EncoderQuality.Balanced,
        EncoderQuality.Balanced => EncoderQuality.Fast,
        _ => null,
    };
}
