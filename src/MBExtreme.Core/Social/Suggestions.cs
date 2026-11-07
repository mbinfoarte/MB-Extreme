using System.Net.Http;
using System.Net.Http.Json;

namespace MBExtreme.Core.Social;

/// <summary>Uma sugestão, problema ou elogio enviado pelo programa.</summary>
public sealed record Suggestion(string Kind, string Text, string Contact, string AppVersion, string Windows, string Report = "", string Name = "")
{
    public static readonly IReadOnlyList<string> Kinds = new[] { "Sugestão de função", "Encontrei um problema", "Elogio", "Outro" };

    public const int MaxText = 4000;
    public const int MinText = 15;

    /// <summary>Dica dentro da caixa de texto para cada tipo.</summary>
    public static string Hint(string kind) => kind switch
    {
        "Encontrei um problema" => "O que aconteceu? Em qual tela? Dá para repetir o problema?",
        "Elogio" => "O que você mais gostou no MB Extreme?",
        "Outro" => "Escreva o que quiser contar para a equipe.",
        _ => "Qual função você queria ver no MB Extreme? Como ela ajudaria no seu dia a dia?",
    };

    /// <summary>Texto vazio, curto demais ou longo demais não vai.</summary>
    public string? Problem => Text.Trim().Length < MinText ? $"Escreva um pouco mais (pelo menos {MinText} letras)." : Text.Length > MaxText ? $"Texto longo demais (até {MaxText} letras)." : null;

    /// <summary>Corpo de e-mail/WhatsApp (quando o servidor não está no ar).</summary>
    public string AsMessage() =>
        $"[{Kind}] MB Extreme {AppVersion} · {Windows}\n\n{Text.Trim()}" + (Name.Length > 0 ? $"\n\nNome: {Name}" : "") + (Contact.Length > 0 ? $"\nContato: {Contact}" : "") + (Report.Length > 0 ? "\n\n--- Relatório do PC ---\n" + Report : "");

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
