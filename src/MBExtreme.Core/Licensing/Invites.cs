using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MBExtreme.Core.Licensing;

public sealed record InviteFriend(string Name, string State, int Days, int DaysLeft, long? AtMs);

public sealed record InviteSummary(string Code, string Link, int Invited, int Testing, int Subscribed, int DaysEarned, int FriendTrialDays, int RewardDays, List<InviteFriend> List)
{
    /// <summary>Mensagem pronta para mandar no WhatsApp ou onde a pessoa quiser.</summary>
    public string ShareText => $"Tô usando o MB Extreme pra deixar o PC mais rápido e os jogos com mais FPS. Testa {FriendTrialDays} dias grátis pelo meu link: {Link}";
}

public sealed record AccountDevice(string Id, string Name, string Spec, long? LastSeenMs, long? FirstSeenMs, bool ThisPc);
public sealed record AccountSubscription(string Status, long? NextChargeMs, double? AmountBrl, string? Interval, bool CancelAtPeriodEnd);
public sealed record AccountOverview(long? MemberSinceMs, bool EmailVerified, string Provider, AccountSubscription? Subscription, int MaxDevices, List<AccountDevice> Devices, int DaysEarned);

public sealed record FeedbackData(int Stars, List<string> Likes, List<string> Issues, string Comment, string Version, string Plan);

/// <summary>"Indique um amigo" e a avaliação do programa (servidor de licenças, autenticado pela licença deste PC).</summary>
public sealed class InviteClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public InviteClient(HttpClient http, string baseUrl)
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public async Task<InviteSummary> MeAsync(string license, string deviceId, CancellationToken token = default)
    {
        using var r = await PostAsync("/v1/invite/me", new { license, deviceId }, token);
        return await r.Content.ReadFromJsonAsync<InviteSummary>(Json, token) ?? throw new LicenseNetworkException("Resposta vazia do servidor.");
    }

    /// <summary>Usa o código de um amigo. Devolve a mensagem para mostrar (sucesso ou o motivo da recusa).</summary>
    public async Task<(bool Ok, string Message)> ClaimAsync(string license, string deviceId, string code, CancellationToken token = default)
    {
        using var r = await _http.PostAsJsonAsync(_baseUrl + "/v1/invite/claim", new { license, deviceId, code = NormalizeCode(code) }, Json, token).ConfigureAwait(false);
        if (r.IsSuccessStatusCode)
        {
            var ok = await r.Content.ReadFromJsonAsync<ClaimReply>(Json, token);
            string who = ok?.InviterName is { Length: > 0 } n ? n : "seu amigo";
            return (true, $"Código aceito! Indicado por {who}: seu teste vai até {ok?.TrialDays ?? 7} dias.");
        }
        return (false, await MessageOf(r, token));
    }

    /// <summary>Resumo da conta: assinatura, computadores e dias ganhos indicando.</summary>
    public async Task<AccountOverview> OverviewAsync(string license, string deviceId, CancellationToken token = default)
    {
        using var r = await PostAsync("/v1/account/overview", new { license, deviceId }, token);
        return await r.Content.ReadFromJsonAsync<AccountOverview>(Json, token) ?? throw new LicenseNetworkException("Resposta vazia do servidor.");
    }

    /// <summary>Tira outro computador da conta.</summary>
    public async Task RemoveDeviceAsync(string license, string deviceId, string target, CancellationToken token = default)
    {
        using var r = await PostAsync("/v1/account/remove-device", new { license, deviceId, target }, token);
    }

    public async Task SaveBackupAsync(string license, string deviceId, string data, bool auto, CancellationToken token = default)
    {
        using var r = await PostAsync("/v1/backup/save", new { license, deviceId, data, auto }, token);
    }

    public async Task<IReadOnlyList<MBExtreme.Core.Settings.CloudBackupInfo>> ListBackupsAsync(string license, string deviceId, CancellationToken token = default)
    {
        using var r = await PostAsync("/v1/backup/list", new { license, deviceId }, token);
        return (await r.Content.ReadFromJsonAsync<BackupList>(Json, token))?.Items ?? new List<MBExtreme.Core.Settings.CloudBackupInfo>();
    }

    public async Task<string> GetBackupAsync(string license, string deviceId, string id, CancellationToken token = default)
    {
        using var r = await PostAsync("/v1/backup/get", new { license, deviceId, id }, token);
        return (await r.Content.ReadFromJsonAsync<BackupData>(Json, token))?.Data ?? throw new LicenseNetworkException("Backup vazio.");
    }

    private sealed record BackupList(List<MBExtreme.Core.Settings.CloudBackupInfo> Items);
    private sealed record BackupData(string Data);

    /// <summary>E-mail com o meio escondido (m•••••@gmail.com) para mostrar na tela.</summary>
    public static string MaskEmail(string email)
    {
        int at = email.IndexOf('@');
        if (at <= 1) return email;
        return email[0] + new string('•', Math.Min(6, at - 1)) + email[at..];
    }

    public async Task SendFeedbackAsync(string license, string deviceId, FeedbackData feedback, CancellationToken token = default)
    {
        using var r = await PostAsync("/v1/feedback", new { license, deviceId, feedback }, token);
    }

    /// <summary>Suporte rápido: avisa o painel do administrador que este PC está online (ID de acesso, nunca a senha).</summary>
    public async Task SupportHeartbeatAsync(string license, string deviceId, MBExtreme.Core.Remote.SupportBeat beat, CancellationToken token = default)
    {
        using var r = await PostAsync("/v1/support/heartbeat", new
        {
            license, deviceId,
            support = new { rustdeskId = beat.RustDeskId, pcName = beat.PcName, os = beat.Os, cpu = beat.Cpu, gpu = beat.Gpu, help = beat.Help, inSession = beat.InSession },
        }, token);
    }

    /// <summary>Tira este PC do painel de suporte (o cliente desligou o suporte rápido).</summary>
    public async Task SupportOptOutAsync(string license, string deviceId, CancellationToken token = default)
    {
        using var r = await PostAsync("/v1/support/optout", new { license, deviceId }, token);
    }

    /// <summary>Só letras e números, maiúsculas, até 12 (o mesmo que o site aceita).</summary>
    public static string NormalizeCode(string code)
    {
        string c = new(code.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        return c.Length > 12 ? c[..12] : c;
    }

    private sealed record ClaimReply(bool Ok, int TrialDays, string? InviterName);
    private sealed record ErrorReply(string? Message);

    private async Task<HttpResponseMessage> PostAsync(string path, object body, CancellationToken token)
    {
        HttpResponseMessage r;
        try { r = await _http.PostAsJsonAsync(_baseUrl + path, body, Json, token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !token.IsCancellationRequested)
        {
            throw new LicenseNetworkException("Sem conexão com o servidor.", ex);
        }
        if (r.IsSuccessStatusCode) return r;
        string message = await MessageOf(r, token);
        r.Dispose();
        throw new LicenseNetworkException(message);
    }

    private static async Task<string> MessageOf(HttpResponseMessage r, CancellationToken token)
    {
        try
        {
            if (await r.Content.ReadFromJsonAsync<ErrorReply>(Json, token) is { Message.Length: > 0 } e) return e.Message!;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or HttpRequestException) { }
        return r.StatusCode == HttpStatusCode.TooManyRequests ? "Muitas tentativas. Aguarde um pouco." : $"O servidor recusou ({(int)r.StatusCode}).";
    }
}

/// <summary>Quando perguntar a avaliação: uma vez, a partir do 2º dia de uso com a conta, depois de um momento bom.</summary>
public static class RatingPrompt
{
    public static bool ShouldShow(DateTime nowUtc, DateTime? firstUseUtc, DateTime? snoozedUntilUtc, bool done, bool signedIn) =>
        signedIn && !done && firstUseUtc is { } first && nowUtc - first >= TimeSpan.FromDays(1) && (snoozedUntilUtc is null || nowUtc >= snoozedUntilUtc);
}
