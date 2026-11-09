namespace MBExtreme.Core.Licensing;

public enum ProfileLevel { Neutral, Ok, Warn, Bad }

/// <summary>O que o cartão do cliente, no topo do menu, mostra.</summary>
public sealed record ProfileInfo(string Name, string Initials, string PlanText, string ActionText, bool ShowAction, ProfileLevel Level);

public static class LicenseProfile
{
    public const int RenewWarningDays = 7;

    public static ProfileInfo Describe(LicenseStatus status, LicenseConfig config, DateTimeOffset now, string windowsUser)
    {
        string name = NameOf(status.Payload, windowsUser);
        string initials = Initials(name);

        switch (status.State)
        {
            case LicenseState.Valid when status.Plan == "pro":
            {
                int days = status.DaysLeft(now) ?? 0;
                string text = days switch { 0 => "Pro · vence hoje", 1 => "Pro · 1 dia restante", _ => $"Pro · {days} dias restantes" };
                return days <= RenewWarningDays
                    ? new ProfileInfo(name, initials, text, "Renovar assinatura", true, ProfileLevel.Warn)
                    : new ProfileInfo(name, initials, text, "", false, ProfileLevel.Ok);
            }
            case LicenseState.Valid:
                return new ProfileInfo(name, initials, "Plano gratuito", "Ativar o Pro", true, ProfileLevel.Neutral);
            case LicenseState.Expired:
                return new ProfileInfo(name, initials, "Licença vencida", "Renovar", true, ProfileLevel.Bad);
            case LicenseState.WrongDevice:
                return new ProfileInfo(name, initials, "Licença de outro PC", "Ver licença", true, ProfileLevel.Bad);
            case LicenseState.ClockTampered:
                return new ProfileInfo(name, initials, "Corrija a data e a hora", "Ver licença", true, ProfileLevel.Warn);
            case LicenseState.Invalid:
                return new ProfileInfo(name, initials, "Licença inválida", "Ver licença", true, ProfileLevel.Bad);
            default:
                return new ProfileInfo(name, initials, config.Configured ? "Plano gratuito" : "Beta aberto", "Ativar o Pro", true, ProfileLevel.Neutral);
        }
    }

    public static string NameOf(LicensePayload? payload, string windowsUser)
    {
        if (payload is not null)
        {
            if (!string.IsNullOrWhiteSpace(payload.Name)) return payload.Name.Trim();
            int at = payload.Email.IndexOf('@');
            if (at > 0) return payload.Email[..at];
        }

        return string.IsNullOrWhiteSpace(windowsUser) ? "Visitante" : windowsUser;
    }

    /// <summary>Até duas letras (primeira de cada uma das duas primeiras palavras), em maiúsculas.</summary>
    public static string Initials(string name)
    {
        var words = name.Split(new[] { ' ', '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        string letters = string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0])));
        return letters.Length == 0 ? "MB" : letters;
    }
}
