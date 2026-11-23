using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MBExtreme.Core.NetDiag;

/// <summary>Relatório do diagnóstico (RF-16): JSON com esquema versionado e HTML para o cliente, com a opção de esconder IPs e MACs.</summary>
public static class NetDiagReportWriter
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ToJson(NetDiagReport report) => JsonSerializer.Serialize(report, Json);

    public static NetDiagReport? FromJson(string json) => JsonSerializer.Deserialize<NetDiagReport>(json, Json);

    private static string StatusText(FindingStatus s) => s switch
    {
        FindingStatus.Confirmado => "Confirmado", FindingStatus.Provavel => "Provável", FindingStatus.Indeterminado => "Indeterminado", _ => "Não testado",
    };

    public static string ToHtml(NetDiagReport r, bool masked)
    {
        string M(string? v) => v is null ? "—" : WebUtility.HtmlEncode(masked ? NetScope.Mask(v) : v);
        string E(string v) => WebUtility.HtmlEncode(v);
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\"><title>Diagnóstico de rede · MB Extreme</title>");
        sb.Append("<style>body{font-family:Segoe UI,Arial,sans-serif;max-width:900px;margin:24px auto;padding:0 16px;color:#111}h1{font-size:22px}h2{font-size:16px;margin-top:24px}");
        sb.Append(".f{border:1px solid #ddd;border-radius:10px;padding:12px;margin:8px 0}.Problema{border-left:5px solid #d33}.Atencao{border-left:5px solid #e9a000}.Info{border-left:5px solid #2a9d5c}");
        sb.Append("table{border-collapse:collapse;width:100%;font-size:13px}td,th{border-bottom:1px solid #eee;padding:5px;text-align:left}small{color:#666}</style></head><body>");
        sb.Append($"<h1>Diagnóstico de rede</h1><p><small>{r.StartedUtc.ToLocalTime():dd/MM/yyyy HH:mm} · {(masked ? "dados sensíveis escondidos" : E(r.Machine))} · MB Extreme · esquema {r.SchemaVersion}</small></p>");

        sb.Append("<h2>Resultado</h2>");
        foreach (var f in r.Findings)
        {
            sb.Append($"<div class=\"f {f.Severity}\"><b>{E(f.Title)}</b> <small>· {StatusText(f.Status)} · confiança {f.Confidence:P0} · {E(f.Code)}</small><ul>");
            foreach (var ev in f.Evidence) sb.Append($"<li>{E(masked ? MaskInText(ev) : ev)}</li>");
            sb.Append("</ul>");
            if (f.Counter.Count > 0) sb.Append("<p><small>Pode não ser isso: " + E(string.Join(" ", f.Counter)) + "</small></p>");
            sb.Append($"<p><b>Próximo passo:</b> {E(f.Next)}</p></div>");
        }

        var e = r.Evidence;
        sb.Append("<h2>Medições</h2><table><tr><th>Teste</th><th>Resultado</th></tr>");
        if (e.Loopback is { } lo) sb.Append($"<tr><td>Pilha de rede do PC</td><td>{E(lo.Text)}</td></tr>");
        if (e.Gateway is { } gw) sb.Append($"<tr><td>Roteador {M(e.GatewayAddress)}</td><td>{E(gw.Text)}</td></tr>");
        foreach (var (target, st) in e.External) sb.Append($"<tr><td>Internet {E(target)}</td><td>{E(st.Text)}</td></tr>");
        foreach (var d in e.DnsSystem.Concat(e.DnsAlternate)) sb.Append($"<tr><td>DNS {M(d.Resolver)} · {E(d.Name)}</td><td>{(d.Ok ? $"ok em {d.Ms:0} ms" : E(d.Detail))}</td></tr>");
        if (e.Http is { } h) sb.Append($"<tr><td>Página de teste</td><td>{(h.Ok ? "abriu" : E(h.Detail))}</td></tr>");
        sb.Append("</table>");

        if (e.Hops.Count > 0)
        {
            sb.Append($"<h2>Caminho até {E(e.TraceTarget ?? "")}</h2><table><tr><th>Salto</th><th>Endereço</th><th>Latência</th></tr>");
            foreach (var hop in e.Hops) sb.Append($"<tr><td>{hop.Ttl}</td><td>{M(hop.Address)}</td><td>{E(hop.Rtt.Text)}</td></tr>");
            sb.Append("</table>");
        }

        sb.Append("<h2>Placas de rede</h2><table><tr><th>Nome</th><th>Tipo</th><th>IP</th><th>Gateway</th><th>DNS</th><th>Velocidade</th></tr>");
        foreach (var i in e.Interfaces.Where(i => i.Up))
            sb.Append($"<tr><td>{E(i.Name)}</td><td>{E(i.Kind)}</td><td>{string.Join(", ", i.Ipv4.Select(M))}</td><td>{string.Join(", ", i.Gateways.Select(M))}</td><td>{string.Join(", ", i.Dns.Select(M))}</td><td>{(i.SpeedBps > 0 ? $"{i.SpeedBps / 1_000_000} Mbps" : "—")}</td></tr>");
        sb.Append("</table>");

        if (r.NotTested.Count > 0) sb.Append("<h2>Não testado</h2><ul>" + string.Concat(r.NotTested.Select(n => $"<li>{E(n)}</li>")) + "</ul>");
        sb.Append("<p><small>Diagnóstico só de leitura: nada foi alterado na rede. Hipóteses são prováveis, não causas confirmadas, até um teste que as separe.</small></p></body></html>");
        return sb.ToString();
    }

    /// <summary>Esconde IPs que aparecem dentro de um texto de evidência.</summary>
    public static string MaskInText(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b", m => NetScope.Mask(m.Value));
}
