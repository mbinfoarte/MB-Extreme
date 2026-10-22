using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MBExtreme.LicenseServer;

/// <summary>Período de cobrança: quantos meses e o desconto em relação ao mensal.</summary>
public sealed record PlanOption(string Key, string Title, int Months, int DiscountPercent);

public sealed record PlanOffer(string Key, string Title, int Months, int Devices, long TotalCents, long PerMonthCents, int DiscountPercent);

/// <summary>Preços de vitrine. Os valores reais de cobrança vêm dos preços cadastrados no Stripe; aqui é só para mostrar e conferir.</summary>
public static class PlanCatalog
{
    public static readonly IReadOnlyList<PlanOption> Options = new[]
    {
        new PlanOption("monthly", "Mensal", 1, 0),
        new PlanOption("quarterly", "Trimestral", 3, 10),
        new PlanOption("semiannual", "Semestral", 6, 15),
        new PlanOption("annual", "Anual", 12, 25),
    };

    /// <summary>Cada computador a mais custa 70% do primeiro (desconto de 30% para contas compartilhadas).</summary>
    public const double ExtraDeviceFactor = 0.7;

    public static PlanOption? Find(string? key) => Options.FirstOrDefault(o => o.Key == key);

    public static PlanOffer Offer(long monthlyCents, PlanOption option, int devices)
    {
        devices = Math.Clamp(devices, 1, 10);
        double deviceFactor = 1 + (devices - 1) * ExtraDeviceFactor;
        long total = (long)Math.Round(monthlyCents * option.Months * (1 - option.DiscountPercent / 100.0) * deviceFactor);
        return new PlanOffer(option.Key, option.Title, option.Months, devices, total, total / option.Months, option.DiscountPercent);
    }

    public static string Money(long cents) => $"R$ {cents / 100m:N2}".Replace(',', '#').Replace('.', ',').Replace('#', '.');
}

/// <summary>Chaves de ativação para vendas fora do Stripe (revenda, Pix, balcão). Guardadas só como hash.</summary>
public static class ActivationKeys
{
    private const string Alphabet = "BCDFGHJKLMNPQRSTVWXZ23456789";

    public static string Generate()
    {
        var chars = new char[19];
        for (int i = 0; i < chars.Length; i++)
            chars[i] = (i + 1) % 5 == 0 ? '-' : Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return "MBX-" + new string(chars);
    }

    public static string Normalize(string? input)
    {
        string letters = new string((input ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return letters.StartsWith("MBX", StringComparison.Ordinal) ? letters[3..] : letters;
    }

    public static bool LooksValid(string? input) => Normalize(input).Length == 16 && Normalize(input).All(Alphabet.Contains);

    public static string Hash(string? input) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("mbx-key|" + Normalize(input))));
}

/// <summary>Estado de uma assinatura vindo do Stripe, já traduzido.</summary>
public sealed record StripeUpdate(string? UserId, string? CustomerId, string? SubscriptionId, string Status, DateTimeOffset? PeriodEnd, int Devices);

public sealed class StripeGateway
{
    private readonly HttpClient _http;
    private readonly string _secretKey;
    private readonly Dictionary<string, string> _prices;

    public StripeGateway(HttpClient http, string secretKey, Dictionary<string, string> prices)
    {
        _http = http;
        _secretKey = secretKey;
        _prices = prices;
    }

    public bool Configured => _secretKey.Length > 0 && _prices.Count > 0;

    public bool HasPrice(string key) => _prices.ContainsKey(key);

    private async Task<JsonElement> PostAsync(string path, Dictionary<string, string> form, CancellationToken stop)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.stripe.com" + path) { Content = new FormUrlEncodedContent(form) };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _secretKey);
        using var response = await _http.SendAsync(request, stop);
        string body = await response.Content.ReadAsStringAsync(stop);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Stripe respondeu {(int)response.StatusCode}.");
        return JsonSerializer.Deserialize<JsonElement>(body);
    }

    /// <summary>Página de pagamento hospedada pelo Stripe. A assinatura nasce com o id do usuário nos metadados.</summary>
    public async Task<string> CheckoutUrlAsync(long userId, string email, string period, int devices, string successUrl, string cancelUrl, CancellationToken stop)
    {
        if (!_prices.TryGetValue(period, out var price)) throw new InvalidOperationException("Período sem preço cadastrado.");
        devices = Math.Clamp(devices, 1, 10);
        var form = new Dictionary<string, string>
        {
            ["mode"] = "subscription",
            ["line_items[0][price]"] = price,
            ["line_items[0][quantity]"] = devices.ToString(),
            ["customer_email"] = email,
            ["client_reference_id"] = userId.ToString(),
            ["success_url"] = successUrl,
            ["cancel_url"] = cancelUrl,
            ["subscription_data[metadata][user_id]"] = userId.ToString(),
            ["subscription_data[metadata][devices]"] = devices.ToString(),
            ["allow_promotion_codes"] = "true",
        };
        var result = await PostAsync("/v1/checkout/sessions", form, stop);
        return result.GetProperty("url").GetString() ?? throw new InvalidOperationException("Stripe não devolveu o endereço.");
    }

    /// <summary>Portal do cliente (trocar cartão, cancelar, ver faturas).</summary>
    public async Task<string> PortalUrlAsync(string customerId, string returnUrl, CancellationToken stop)
    {
        var result = await PostAsync("/v1/billing_portal/sessions", new() { ["customer"] = customerId, ["return_url"] = returnUrl }, stop);
        return result.GetProperty("url").GetString() ?? throw new InvalidOperationException("Stripe não devolveu o endereço.");
    }

    /// <summary>Confere o cabeçalho Stripe-Signature (t=...,v1=HMAC-SHA256 de "t.corpo"), com tolerância de 5 minutos.</summary>
    public static bool VerifySignature(string payload, string header, string secret, DateTimeOffset now, TimeSpan? tolerance = null)
    {
        if (secret.Length == 0 || string.IsNullOrEmpty(header)) return false;
        string? timestamp = null;
        var signatures = new List<string>();
        foreach (string part in header.Split(','))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;
            string key = part[..eq].Trim(), value = part[(eq + 1)..].Trim();
            if (key == "t") timestamp = value;
            else if (key == "v1") signatures.Add(value);
        }
        if (timestamp is null || !long.TryParse(timestamp, out long seconds) || signatures.Count == 0) return false;
        if (Math.Abs((now - DateTimeOffset.FromUnixTimeSeconds(seconds)).TotalSeconds) > (tolerance ?? TimeSpan.FromMinutes(5)).TotalSeconds) return false;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        string expected = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(timestamp + "." + payload))).ToLowerInvariant();
        return signatures.Any(s => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(s.ToLowerInvariant()), Encoding.UTF8.GetBytes(expected)));
    }

    public static string Sign(string payload, string secret, DateTimeOffset now)
    {
        string t = now.ToUnixTimeSeconds().ToString();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return $"t={t},v1={Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(t + "." + payload))).ToLowerInvariant()}";
    }

    /// <summary>Traduz o evento do Stripe. Devolve nulo para eventos que não mudam a assinatura.</summary>
    public static StripeUpdate? Translate(JsonElement stripeEvent)
    {
        string type = stripeEvent.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
        if (!stripeEvent.TryGetProperty("data", out var data) || !data.TryGetProperty("object", out var obj)) return null;

        string? Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        switch (type)
        {
            case "checkout.session.completed":
                return new StripeUpdate(Str(obj, "client_reference_id"), Str(obj, "customer"), Str(obj, "subscription"), "active", null, 0);

            case "customer.subscription.created":
            case "customer.subscription.updated":
            case "customer.subscription.deleted":
                string status = type.EndsWith("deleted") ? "canceled" : Str(obj, "status") switch
                {
                    "active" => "active",
                    "trialing" => "trialing",
                    "past_due" => "past_due",
                    "unpaid" => "past_due",
                    "incomplete" => "past_due",
                    _ => "canceled",
                };
                DateTimeOffset? end = null;
                if (obj.TryGetProperty("current_period_end", out var cpe) && cpe.TryGetInt64(out long unix)) end = DateTimeOffset.FromUnixTimeSeconds(unix);
                else if (obj.TryGetProperty("items", out var items) && items.TryGetProperty("data", out var arr) && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0
                         && arr[0].TryGetProperty("current_period_end", out var ipe) && ipe.TryGetInt64(out long unix2)) end = DateTimeOffset.FromUnixTimeSeconds(unix2);
                int devices = 0;
                if (obj.TryGetProperty("items", out var its) && its.TryGetProperty("data", out var list) && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0
                    && list[0].TryGetProperty("quantity", out var q) && q.TryGetInt32(out int qty)) devices = qty;
                string? userId = obj.TryGetProperty("metadata", out var meta) ? Str(meta, "user_id") : null;
                return new StripeUpdate(userId, Str(obj, "customer"), Str(obj, "id"), status, end, devices);

            case "invoice.payment_failed":
                return new StripeUpdate(null, Str(obj, "customer"), Str(obj, "subscription"), "past_due", null, 0);

            default:
                return null;
        }
    }
}
