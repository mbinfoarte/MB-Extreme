using System.Net;

namespace MBExtreme.LicenseServer;

/// <summary>Páginas de mensagem e formulários simples (conta, ativação), dentro da mesma moldura preta e amarela do site.</summary>
public static class Pages
{
    private static SiteOptions _options = new("MB Extreme", "suporte@mbextreme.com.br", "", "", "Beta 01", "", 3, PlanCatalog.DefaultPrices, 1990, new AffiliateProgram());

    /// <summary>Opções do site, definidas na partida do servidor.</summary>
    public static SiteOptions Options { get => _options; set => _options = value; }

    public static string E(string? text) => WebUtility.HtmlEncode(text ?? "");

    public static string Page(string title, string body, string? userEmail = null) => SiteLayout.Narrow(_options, title, body, userEmail);

    public static string Message(string title, string text, string css = "muted") =>
        Page(title, $"<h1>{E(title)}</h1><p class=\"{css}\">{E(text)}</p><p><a class=\"go\" href=\"/\">Voltar</a></p>");
}
