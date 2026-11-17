using System.Globalization;
using Microsoft.Win32;

namespace MBExtreme.Core.Licensing;

/// <summary>Situação do teste grátis do Pro neste PC.</summary>
public sealed record TrialState(DateTime StartedUtc, int DaysTotal, double DaysLeft)
{
    public bool Active => DaysLeft > 0;
    public bool Expired => !Active;

    /// <summary>"faltam 3 dias", "último dia", "acabou".</summary>
    public string LeftText => DaysLeft <= 0 ? "acabou" : DaysLeft < 1 ? "último dia" : $"faltam {Math.Ceiling(DaysLeft):0} dias";
}

/// <summary>
/// Teste grátis do Pro por PC (7 dias por padrão), contado a partir do primeiro uso, sem pedir conta.
/// A data de início fica em dois lugares (configurações e registro do usuário): apagar um não reinicia o teste. Atrasar o relógio do PC
/// não devolve dias: vale a data mais adiantada já vista. O servidor, quando estiver no ar, guarda o mesmo PC para reinstalar não renovar.
/// </summary>
public static class TrialClock
{
    private const string Key = @"Software\MBExtreme";

    /// <summary>Conta pura (testável): dias que faltam a partir do início, sem deixar o relógio "voltar".</summary>
    public static TrialState Evaluate(DateTime startedUtc, DateTime lastSeenUtc, DateTime nowUtc, int days)
    {
        var effectiveNow = nowUtc < lastSeenUtc ? lastSeenUtc : nowUtc;   // relógio atrasado: usa o mais adiantado já visto
        double left = days - (effectiveNow - startedUtc).TotalDays;
        return new TrialState(startedUtc, days, Math.Clamp(left, 0, days));
    }

    /// <summary>A data de início mais antiga entre as guardadas (ou agora, na primeira vez).</summary>
    public static DateTime EarliestStart(DateTime? fromSettings, DateTime? fromRegistry, DateTime nowUtc)
    {
        var known = new[] { fromSettings, fromRegistry }.Where(d => d is not null && d <= nowUtc.AddDays(1)).Select(d => d!.Value).ToList();
        return known.Count == 0 ? nowUtc : known.Min();
    }

    public static DateTime? ParseDate(string? text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : null;

    /// <summary>Lê, começa (se for a primeira vez) e guarda o teste deste PC.</summary>
    public static TrialState Current(Settings.AppSettings settings, int days, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        DateTime? registry = null, registrySeen = null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);
            registry = ParseDate(key?.GetValue("TrialStart") as string);
            registrySeen = ParseDate(key?.GetValue("TrialSeen") as string);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or PlatformNotSupportedException) { }

        var start = EarliestStart(ParseDate(settings.TrialStartedUtc), registry, now);
        var seen = new[] { ParseDate(settings.TrialLastSeenUtc), registrySeen, (DateTime?)now }.Where(d => d is not null).Max()!.Value;
        string startText = start.ToString("O", CultureInfo.InvariantCulture), seenText = seen.ToString("O", CultureInfo.InvariantCulture);
        if (settings.TrialStartedUtc != startText || settings.TrialLastSeenUtc != seenText)
        {
            settings.TrialStartedUtc = startText;
            settings.TrialLastSeenUtc = seenText;
            settings.Save();
        }
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(Key);
            key.SetValue("TrialStart", startText);
            key.SetValue("TrialSeen", seenText);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or PlatformNotSupportedException) { }
        return Evaluate(start, seen, now, days);
    }
}
