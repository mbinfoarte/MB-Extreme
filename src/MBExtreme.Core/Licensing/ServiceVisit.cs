using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MBExtreme.Core.Licensing;

/// <summary>Um PC já atendido de uma empresa.</summary>
public sealed record ServicePc(string PcName, string DeviceId, long LastVisit, int? HealthAfter, bool Sealed);

/// <summary>Empresa do atendimento técnico (manutenção sem assinatura).</summary>
public sealed record ServiceCompany(string Id, string Name, string Cnpj, string Contact, string Whatsapp, string Address, string Notes, double Price,
    long? LastVisit, double Due, int Visits, List<ServicePc> Pcs);

public sealed record ServiceTotals(int MonthCount, double MonthPaid, double Due, int Stale);

public sealed record ServiceCompanies(List<ServiceCompany> Items, ServiceTotals Totals);

/// <summary>Uma visita já finalizada.</summary>
public sealed record ServiceVisitInfo(string Id, string PcName, long? StartedAt, long? FinishedAt, double Price, string Payment, List<string> Items, string Notes);

/// <summary>Dados de uma empresa para salvar (Id vazio = nova).</summary>
public sealed record ServiceCompanyInput(string? Id, string Name, string Cnpj, string Contact, string Whatsapp, string Address, string Notes, double Price);

/// <summary>Atendimento em andamento neste PC (fica nas configurações até ser finalizado).</summary>
public sealed class ServiceVisitState
{
    public string VisitId { get; set; } = "";
    public string CompanyId { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string PcName { get; set; } = "";
    public double Price { get; set; }
    public DateTime StartedAt { get; set; }
    /// <summary>O que foi feito: anotado sozinho a cada operação concluída e à mão pelo técnico.</summary>
    public List<string> Items { get; set; } = new();
    public string Notes { get; set; } = "";
    /// <summary>Ajustes do Windows aplicados (são muitos: no relatório viram uma linha só).</summary>
    public int Tweaks { get; set; }

    /// <summary>A lista do relatório: os ajustes numa linha, depois o resto na ordem em que foi feito.</summary>
    public List<string> ReportItems()
    {
        var list = new List<string>();
        if (Tweaks > 0) list.Add(Tweaks == 1 ? "1 ajuste do Windows aplicado" : $"{Tweaks} ajustes do Windows aplicados");
        list.AddRange(Items);
        return list;
    }
}

/// <summary>
/// Atendimento técnico pela API do programa (só a conta de administrador; para as outras o servidor responde 403).
/// O "selo" que o servidor devolve ao finalizar é uma licença do plano "service" presa a este PC: não libera nada, só impede o
/// programa de desfazer as otimizações quando a conta do técnico sai.
/// </summary>
public sealed class ServiceClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _license;
    private readonly string _deviceId;

    public ServiceClient(HttpClient http, string baseUrl, string license, string deviceId)
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
        _license = license;
        _deviceId = deviceId;
    }

    /// <summary>Empresas e números do mês. Null quando a conta não é de administrador.</summary>
    public async Task<ServiceCompanies?> CompaniesAsync(CancellationToken token = default) =>
        await PostAsync<ServiceCompanies>(new { action = "companies" }, allowForbidden: true, token);

    public async Task<List<ServiceVisitInfo>> VisitsAsync(string companyId, CancellationToken token = default) =>
        (await PostAsync<VisitList>(new { action = "visits", companyId }, false, token))?.Items ?? new List<ServiceVisitInfo>();

    public async Task<string> SaveCompanyAsync(ServiceCompanyInput c, CancellationToken token = default) =>
        (await PostAsync<IdReply>(new { action = "saveCompany", company = c }, false, token))?.Id ?? throw new LicenseNetworkException("Resposta vazia do servidor.");

    public async Task SetPaymentAsync(string visitId, string payment, CancellationToken token = default) =>
        await PostAsync<JsonElement>(new { action = "setPayment", visitId, payment }, false, token);

    public async Task<string> StartAsync(string companyId, string pcName, double price, CancellationToken token = default) =>
        (await PostAsync<StartReply>(new { action = "startPc", companyId, pcName, price }, false, token))?.VisitId ?? throw new LicenseNetworkException("Resposta vazia do servidor.");

    /// <summary>Finaliza no servidor. Devolve o selo (ou null se não pediu).</summary>
    public async Task<string?> FinishAsync(ServiceVisitState visit, string payment, bool seal, CancellationToken token = default) =>
        (await PostAsync<FinishReply>(new { action = "finishPc", visitId = visit.VisitId, items = visit.ReportItems(), notes = visit.Notes, price = visit.Price, payment, seal }, false, token))?.Seal;

    private sealed record VisitList(List<ServiceVisitInfo> Items);
    private sealed record IdReply(string Id);
    private sealed record StartReply(string VisitId);
    private sealed record FinishReply(string? Seal);
    private sealed record ErrorReply(string? Message);

    private async Task<T?> PostAsync<T>(object request, bool allowForbidden, CancellationToken token)
    {
        var body = JsonSerializer.SerializeToElement(request, Json);
        var merged = new Dictionary<string, object?> { ["license"] = _license, ["deviceId"] = _deviceId };
        foreach (var p in body.EnumerateObject()) merged[p.Name] = p.Value;
        HttpResponseMessage r;
        try { r = await _http.PostAsJsonAsync(_baseUrl + "/v1/admin/service", merged, Json, token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !token.IsCancellationRequested)
        {
            throw new LicenseNetworkException("Sem conexão com o servidor.", ex);
        }
        using (r)
        {
            if (allowForbidden && r.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound) return default;
            if (!r.IsSuccessStatusCode)
            {
                string message = "Erro no servidor.";
                try { message = (await r.Content.ReadFromJsonAsync<ErrorReply>(Json, token))?.Message ?? message; }
                catch (JsonException) { }
                throw new LicenseNetworkException(message);
            }
            return await r.Content.ReadFromJsonAsync<T>(Json, token);
        }
    }

    /// <summary>Texto da situação do pagamento.</summary>
    public static string PaymentText(string payment) => payment switch { "pago" => "Pago", "pix_enviado" => "Pix enviado", _ => "A receber" };

    /// <summary>Resumo para mandar no WhatsApp da empresa.</summary>
    public static string Summary(ServiceVisitState v, DateTime end)
    {
        var lines = new List<string>
        {
            $"Relatório do atendimento técnico · {v.CompanyName}",
            $"Computador: {v.PcName}",
            $"Data: {v.StartedAt:dd/MM/yyyy}, das {v.StartedAt:HH:mm} às {end:HH:mm}",
            "",
            "O que foi feito:",
        };
        lines.AddRange(v.ReportItems().Select(i => "• " + i));
        if (v.Notes.Trim().Length > 0) { lines.Add(""); lines.Add("Observações: " + v.Notes.Trim()); }
        lines.Add("");
        lines.Add($"Valor: {v.Price.ToString("C", new System.Globalization.CultureInfo("pt-BR"))}");
        lines.Add("Atendimento feito com o MB Extreme · MB InfoArt");
        return string.Join("\n", lines);
    }
}

/// <summary>O selo de atendimento guardado neste PC.</summary>
public static class ServiceSeal
{
    /// <summary>Em ProgramData: vale para todos os usuários do PC e não some quando a conta do técnico sai.</summary>
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MBExtreme", "service-seal.txt");

    /// <summary>O selo é autêntico, é de atendimento e é deste PC?</summary>
    public static bool IsValid(string? token, string publicKey, string deviceId)
    {
        if (string.IsNullOrWhiteSpace(token) || publicKey.Length == 0) return false;
        var p = LicenseToken.Verify(token.Trim(), publicKey);
        return p is not null && p.Plan == "service" && p.Status == "service" && p.Device == deviceId && p.ExpiresAt > DateTimeOffset.UtcNow;
    }

    public static bool Present(string publicKey, string deviceId)
    {
        try { return File.Exists(FilePath) && IsValid(File.ReadAllText(FilePath), publicKey, deviceId); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    public static void Save(string token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, token.Trim());
    }

    /// <summary>Empresa e data do selo, para mostrar.</summary>
    public static (string Company, DateTimeOffset When)? Info(string publicKey, string deviceId)
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            string t = File.ReadAllText(FilePath);
            if (!IsValid(t, publicKey, deviceId)) return null;
            var p = LicenseToken.Peek(t)!;
            return (p.Name, p.IssuedAt);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
