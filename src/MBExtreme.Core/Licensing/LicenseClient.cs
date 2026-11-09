using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MBExtreme.Core.Licensing;

public sealed class LicenseNetworkException(string message, Exception? inner = null) : Exception(message, inner);

public sealed record DeviceCode(
    [property: JsonPropertyName("deviceCode")] string DeviceCodeValue,
    [property: JsonPropertyName("userCode")] string UserCode,
    [property: JsonPropertyName("verificationUri")] string VerificationUri,
    [property: JsonPropertyName("intervalSeconds")] int IntervalSeconds,
    [property: JsonPropertyName("expiresInSeconds")] int ExpiresInSeconds);

public enum PollState { Pending, SlowDown, Denied, Expired, Approved }

public sealed record PollResult(PollState State, string? License, string? Message);

public enum RefreshState { Renewed, Revoked, DeviceLimit, Failed }

public sealed record RefreshResult(RefreshState State, string? License, string? Message);

/// <summary>Conversa com o servidor de licenças (login por código de dispositivo, renovação e desativação).</summary>
public sealed class LicenseClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public LicenseClient(HttpClient http, string baseUrl)
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public async Task<DeviceCode> StartDeviceLoginAsync(string deviceId, string deviceName, CancellationToken token)
    {
        var response = await PostAsync("/v1/device/code", new { deviceId, deviceName }, token);
        if (!response.IsSuccessStatusCode) throw new LicenseNetworkException($"O servidor recusou o pedido de login ({(int)response.StatusCode}).");
        return await response.Content.ReadFromJsonAsync<DeviceCode>(cancellationToken: token)
               ?? throw new LicenseNetworkException("Resposta inválida do servidor.");
    }

    public async Task<PollResult> PollAsync(string deviceCode, string deviceId, CancellationToken token)
    {
        var response = await PostAsync("/v1/device/token", new { deviceCode, deviceId }, token);
        string state = "";
        string? license = null, message = null;
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            state = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
            license = doc.RootElement.TryGetProperty("license", out var l) ? l.GetString() : null;
            message = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null;
        }
        catch (JsonException)
        {
            throw new LicenseNetworkException("Resposta inválida do servidor.");
        }

        return state switch
        {
            "approved" when license is not null => new PollResult(PollState.Approved, license, message),
            "pending" => new PollResult(PollState.Pending, null, message),
            "slow_down" => new PollResult(PollState.SlowDown, null, message),
            "denied" => new PollResult(PollState.Denied, null, message ?? "O acesso foi negado."),
            "expired" => new PollResult(PollState.Expired, null, message ?? "O código expirou."),
            _ => throw new LicenseNetworkException("Resposta inesperada do servidor."),
        };
    }

    public async Task<RefreshResult> RefreshAsync(string currentLicense, string deviceId, CancellationToken token)
    {
        var response = await PostAsync("/v1/license/refresh", new { license = currentLicense, deviceId }, token);
        string? license = null, message = null;
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            license = doc.RootElement.TryGetProperty("license", out var l) ? l.GetString() : null;
            message = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null;
        }
        catch (JsonException) { }

        if (response.IsSuccessStatusCode && license is not null) return new RefreshResult(RefreshState.Renewed, license, message);
        return response.StatusCode switch
        {
            HttpStatusCode.PaymentRequired or HttpStatusCode.Forbidden => new RefreshResult(RefreshState.Revoked, null, message ?? "A assinatura não está ativa."),
            HttpStatusCode.Conflict => new RefreshResult(RefreshState.DeviceLimit, null, message ?? "Limite de computadores atingido."),
            _ => new RefreshResult(RefreshState.Failed, null, message ?? $"O servidor respondeu {(int)response.StatusCode}."),
        };
    }

    public async Task DeactivateAsync(string currentLicense, string deviceId, CancellationToken token)
    {
        var response = await PostAsync("/v1/license/deactivate", new { license = currentLicense, deviceId }, token);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
            throw new LicenseNetworkException($"Não foi possível desativar ({(int)response.StatusCode}).");
    }

    private async Task<HttpResponseMessage> PostAsync(string path, object body, CancellationToken token)
    {
        try
        {
            return await _http.PostAsJsonAsync(_baseUrl + path, body, token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !token.IsCancellationRequested)
        {
            throw new LicenseNetworkException("Sem conexão com o servidor de licenças.", ex);
        }
    }
}
