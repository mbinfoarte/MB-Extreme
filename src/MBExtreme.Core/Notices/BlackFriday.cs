namespace MBExtreme.Core.Notices;

/// <summary>Uma etapa da campanha: desconto, início e fim (UTC).</summary>
public sealed record PromoStep(int Percent, DateTimeOffset Start, DateTimeOffset End, string Label);

/// <summary>
/// Black Friday 2026 do MB Extreme, nas mesmas datas do site e do servidor (horário de Brasília): 40% no plano anual de 24/11 a 01/12
/// e 50% nas 48 horas de 27 e 28/11. Só o 1º ano. O desconto é aplicado no pagamento do site; aqui o programa só avisa.
/// </summary>
public static class BlackFriday
{
    private static readonly TimeSpan Brt = TimeSpan.FromHours(-3);

    public static IReadOnlyList<PromoStep> Steps { get; } = new PromoStep[]
    {
        new(50, new DateTimeOffset(2026, 11, 27, 0, 0, 0, Brt), new DateTimeOffset(2026, 11, 29, 0, 0, 0, Brt), "48 horas de Black Friday"),
        new(40, new DateTimeOffset(2026, 11, 24, 0, 0, 0, Brt), new DateTimeOffset(2026, 12, 2, 0, 0, 0, Brt), "Black Friday MB Extreme"),
    };

    /// <summary>Etapa que vale agora (a de 50% tem prioridade), ou null.</summary>
    public static PromoStep? Active(DateTimeOffset now) => Steps.FirstOrDefault(s => now >= s.Start && now < s.End);

    /// <summary>
    /// Quem vê a faixa: grátis, teste e plano mensal. Quem já paga o anual (período pago de mais de 40 dias pela frente) ou tem chave
    /// sem vencimento (cortesia) não vê.
    /// </summary>
    public static bool ShouldShow(bool paid, DateTimeOffset? periodEnd, bool noEndDate, DateTimeOffset now)
    {
        if (!paid) return true;
        if (noEndDate) return false;
        return periodEnd is not { } end || end - now <= TimeSpan.FromDays(40);
    }

    /// <summary>"2d 03:12:45" até o fim da etapa.</summary>
    public static string Countdown(PromoStep step, DateTimeOffset now)
    {
        var left = step.End - now;
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        return (left.Days > 0 ? left.Days + "d " : "") + left.ToString(@"hh\:mm\:ss");
    }

    /// <summary>Id do aviso no sino (um por etapa).</summary>
    public static string NoticeId(PromoStep step) => "black-friday-2026-" + step.Percent;

    public const string SitePath = "/?origem=programa#planos";
}
