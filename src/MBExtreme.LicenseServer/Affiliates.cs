using System.Security.Cryptography;

namespace MBExtreme.LicenseServer;

/// <summary>Nível do afiliado. MinCustomers = clientes pagantes indicados (com pagamento já confirmado) para chegar ao nível.</summary>
public sealed record AffiliateTier(string Key, string Name, int MinCustomers, double Rate, string Color);

/// <summary>
/// Regras do programa de afiliados (ajustáveis por configuração em Affiliates:*). O afiliado indica pelo link /r/CÓDIGO; quando a pessoa
/// indicada assina, ele ganha uma porcentagem de cada pagamento por até <see cref="CommissionMonths"/> meses. A comissão só fica disponível
/// depois da carência (prazo de arrependimento e estornos) e é paga por Pix quando o saldo passa do valor mínimo.
/// </summary>
public sealed class AffiliateProgram
{
    public static readonly IReadOnlyList<AffiliateTier> Tiers = new[]
    {
        new AffiliateTier("latao", "Latão", 0, 0.20, "#b5a642"),
        new AffiliateTier("bronze", "Bronze", 5, 0.25, "#cd7f32"),
        new AffiliateTier("prata", "Prata", 20, 0.30, "#c0c0c8"),
        new AffiliateTier("ouro", "Ouro", 50, 0.35, "#ffc400"),
    };

    /// <summary>Quantos meses de pagamento do cliente geram comissão (0 = só o primeiro pagamento).</summary>
    public int CommissionMonths { get; init; } = 12;

    /// <summary>Dias até a comissão ficar disponível para saque (cobre o direito de arrependimento de 7 dias e estornos).</summary>
    public int HoldDays { get; init; } = 30;

    public long MinPayoutCents { get; init; } = 5000;

    /// <summary>Quanto tempo o link de indicação vale depois do clique.</summary>
    public int CookieDays { get; init; } = 60;

    public static AffiliateTier TierFor(int paidCustomers) =>
        Tiers.Where(t => paidCustomers >= t.MinCustomers).MaxBy(t => t.MinCustomers) ?? Tiers[0];

    /// <summary>Próximo nível e quantos clientes faltam; nulo se já é Ouro.</summary>
    public static (AffiliateTier Tier, int Missing)? Next(int paidCustomers)
    {
        var current = TierFor(paidCustomers);
        var next = Tiers.Where(t => t.MinCustomers > current.MinCustomers).MinBy(t => t.MinCustomers);
        return next is null ? null : (next, next.MinCustomers - paidCustomers);
    }

    public static long Commission(long amountCents, double rate) => amountCents <= 0 ? 0 : (long)Math.Floor(amountCents * rate);

    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string NewCode()
    {
        var chars = new char[8];
        for (int i = 0; i < chars.Length; i++) chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }

    /// <summary>Código do link: 4 a 24 letras, números ou hífen.</summary>
    public static bool ValidCode(string? code) =>
        code is { Length: >= 4 and <= 24 } && code.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    public static string NormalizeCode(string? code) => (code ?? "").Trim().ToUpperInvariant();

    /// <summary>A comissão deste pagamento ainda vale? (nº do mês de pagamento do cliente indicado, a partir de 0)</summary>
    public bool PaysCommission(int paymentIndex) => paymentIndex <= Math.Max(0, CommissionMonths - 1);

    public bool Available(DateTimeOffset createdAt, DateTimeOffset now) => now >= createdAt.AddDays(HoldDays);
}
