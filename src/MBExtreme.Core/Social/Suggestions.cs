using System.Net.Http;
using System.Net.Http.Json;

namespace MBExtreme.Core.Social;

/// <summary>Uma sugestão, problema ou elogio enviado pelo programa.</summary>
public sealed record Suggestion(string Kind, string Text, string Contact, string AppVersion, string Windows, string Report = "")
{
    public static readonly IReadOnlyList<string> Kinds = new[] { "Ideia", "Problema", "Elogio" };

    public const int MaxText = 4000;

    /// <summary>Texto vazio, curto demais ou longo demais não vai.</summary>
    public string? Problem => Text.Trim().Length < 10 ? "Escreva um pouco mais (pelo menos 10 letras)." : Text.Length > MaxText ? $"Texto longo demais (até {MaxText} letras)." : null;

    /// <summary>Corpo de e-mail/WhatsApp (quando o servidor não está no ar).</summary>
    public string AsMessage() =>
        $"[{Kind}] MB Extreme {AppVersion} · {Windows}\n\n{Text.Trim()}" + (Contact.Length > 0 ? $"\n\nContato: {Contact}" : "") + (Report.Length > 0 ? "\n\n--- Relatório do PC ---\n" + Report : "");

    /// <summary>Envia para o servidor da MB InfoArt. Falso sem servidor ou sem internet (aí o programa oferece WhatsApp ou e-mail).</summary>
    public async Task<bool> SendAsync(HttpClient http, string serverUrl, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(serverUrl.TrimEnd('/') + "/v1/suggestions", UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return false;
        try
        {
            using var response = await http.PostAsJsonAsync(uri, this, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return false; }
    }
}
