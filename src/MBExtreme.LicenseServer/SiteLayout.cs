using System.Net;
using System.Text;

namespace MBExtreme.LicenseServer;

/// <summary>Configuração visível do site (links, valores e textos que mudam sem recompilar).</summary>
public sealed record SiteOptions(
    string Brand, string SupportEmail, string SetupUrl, string PortableUrl, string Version, string GoogleClientId,
    int TrialDays, IReadOnlyDictionary<string, long> Prices, long MonthlyCents, AffiliateProgram Affiliate);

/// <summary>Moldura do site (cabeçalho, rodapé, tema preto e amarelo igual ao aplicativo) e pequenos componentes.</summary>
public static class SiteLayout
{
    public static string E(string? text) => WebUtility.HtmlEncode(text ?? "");

    public const string Css = """
        :root{--bg:#050505;--surface:#0e0e11;--surface2:#141418;--line:#26262c;--text:#f2f2f4;--muted:#9a9aa3;--accent:#ffc400;--accent2:#ffdd33;--ok:#3ddc97;--warn:#ffa94d;--bad:#ff5a5f}
        *{box-sizing:border-box}
        html{scroll-behavior:smooth}
        body{background:var(--bg);color:var(--text);font:16px/1.6 "Segoe UI",system-ui,-apple-system,sans-serif;margin:0}
        a{color:inherit}
        .wrap{max-width:1120px;margin:0 auto;padding:0 22px}
        .narrow{max-width:780px;margin:0 auto;padding:36px 22px 60px}
        header.top{position:sticky;top:0;z-index:20;background:rgba(5,5,5,.86);backdrop-filter:blur(10px);border-bottom:1px solid var(--line)}
        header.top .wrap{display:flex;align-items:center;gap:22px;height:64px}
        .brand{display:flex;align-items:center;gap:10px;text-decoration:none;font-weight:800;font-size:19px;letter-spacing:.3px;margin-right:auto}
        .brand img{width:34px;height:34px;border-radius:9px}
        .brand b{color:var(--accent)}
        nav.main{display:flex;gap:20px}
        nav.main a,.link{color:#c9c9d1;text-decoration:none;font-size:15px;background:none;border:0;font-family:inherit;cursor:pointer;padding:0}
        nav.main a:hover,.link:hover{color:var(--accent)}
        .go{display:inline-block;background:linear-gradient(#ffdd33,#ffc400);color:#111;border:0;border-radius:12px;padding:11px 22px;font-weight:700;font:inherit;font-weight:700;cursor:pointer;text-decoration:none;box-shadow:0 0 24px rgba(255,196,0,.22)}
        .go:hover{filter:brightness(1.06)}
        .go.small{padding:7px 16px;font-size:14px;border-radius:10px}
        .ghost{display:inline-block;background:var(--surface2);color:var(--text);border:1px solid var(--line);border-radius:12px;padding:11px 22px;font:inherit;font-weight:600;cursor:pointer;text-decoration:none}
        .ghost:hover{border-color:var(--accent)}
        .hero{padding:78px 0 40px;position:relative;overflow:hidden}
        .hero:before{content:"";position:absolute;inset:-200px 0 auto 0;height:520px;background:radial-gradient(60% 60% at 50% 0,rgba(255,196,0,.16),transparent 70%);pointer-events:none}
        .hero .grid{display:grid;grid-template-columns:1.05fr .95fr;gap:44px;align-items:center;position:relative}
        .pill{display:inline-block;border:1px solid #4a3c00;background:#1a1500;color:var(--accent);border-radius:99px;padding:4px 14px;font-size:13px;font-weight:600}
        h1{font-size:46px;line-height:1.12;margin:16px 0 14px;letter-spacing:-.5px}
        h1 em{font-style:normal;color:var(--accent);text-shadow:0 0 28px rgba(255,196,0,.35)}
        h2{font-size:32px;line-height:1.2;margin:0 0 10px;letter-spacing:-.3px}
        h3{font-size:19px;margin:0 0 6px}
        .lead{color:#c3c3cb;font-size:18px;max-width:560px}
        .muted{color:var(--muted)}
        .cta{display:flex;gap:14px;flex-wrap:wrap;margin:26px 0 12px}
        .small{font-size:14px}
        section{padding:64px 0}
        section.alt{background:linear-gradient(#08080a,#0b0b0e);border-top:1px solid var(--line);border-bottom:1px solid var(--line)}
        .center{text-align:center}.center .lead{margin-left:auto;margin-right:auto}
        .cards{display:grid;grid-template-columns:repeat(3,1fr);gap:16px;margin-top:30px}
        .card{background:var(--surface);border:1px solid var(--line);border-radius:16px;padding:20px 22px}
        .card .ico{font-size:24px;width:44px;height:44px;border-radius:12px;background:#1a1500;border:1px solid #4a3c00;display:flex;align-items:center;justify-content:center;margin-bottom:12px}
        .steps{display:grid;grid-template-columns:repeat(3,1fr);gap:16px;margin-top:30px;counter-reset:s}
        .step{background:var(--surface);border:1px solid var(--line);border-radius:16px;padding:22px;position:relative}
        .step:before{counter-increment:s;content:counter(s);position:absolute;top:-14px;left:20px;background:var(--accent);color:#111;font-weight:800;width:30px;height:30px;border-radius:50%;display:flex;align-items:center;justify-content:center}
        .plans{display:grid;grid-template-columns:repeat(4,1fr);gap:16px;margin-top:30px}
        .plan{background:var(--surface);border:1px solid var(--line);border-radius:18px;padding:22px;display:flex;flex-direction:column;position:relative}
        .plan.hot{border-color:var(--accent);box-shadow:0 0 34px rgba(255,196,0,.16)}
        .plan .tag{position:absolute;top:-12px;right:16px;background:var(--accent);color:#111;font-size:12px;font-weight:800;border-radius:99px;padding:2px 12px}
        .plan .price{font-size:34px;font-weight:800;margin:8px 0 0}
        .plan .price small{font-size:14px;font-weight:500;color:var(--muted)}
        .plan ul{list-style:none;padding:0;margin:14px 0 18px;color:#c3c3cb;font-size:14.5px;flex:1}
        .plan li{padding:3px 0}.plan li:before{content:"✓ ";color:var(--ok);font-weight:700}
        table.cmp{width:100%;border-collapse:collapse;margin-top:26px;background:var(--surface);border:1px solid var(--line);border-radius:16px;overflow:hidden}
        table.cmp th,table.cmp td{padding:11px 16px;border-bottom:1px solid var(--line);text-align:left;font-size:15px}
        table.cmp th{background:var(--surface2);color:var(--muted);font-weight:600}
        table.cmp td.c,table.cmp th.c{text-align:center}
        .yes{color:var(--ok);font-weight:700}.no{color:#55555e}
        details{background:var(--surface);border:1px solid var(--line);border-radius:14px;padding:14px 18px;margin:10px 0}
        details summary{cursor:pointer;font-weight:600}
        details p{color:#c3c3cb;margin:10px 0 0}
        footer.bottom{border-top:1px solid var(--line);padding:34px 0;color:var(--muted);font-size:14px}
        footer.bottom .wrap{display:flex;gap:26px;flex-wrap:wrap;align-items:center}
        footer.bottom a{color:#b5b5bd;text-decoration:none}footer.bottom a:hover{color:var(--accent)}
        footer .sp{margin-left:auto}
        /* formulários e painéis */
        .box{background:var(--surface);border:1px solid var(--line);border-radius:16px;padding:20px 22px;margin:0 0 16px}
        label{display:block;color:#c9c9d1;font-size:14px}
        input,select{width:100%;background:#17171b;color:var(--text);border:1px solid #2c2c33;border-radius:10px;padding:10px 12px;margin:6px 0 14px;font:inherit}
        input:focus,select:focus{outline:2px solid rgba(255,196,0,.45);border-color:var(--accent)}
        select{width:auto}
        .ok{color:var(--ok)}.warn{color:var(--warn)}.bad{color:var(--bad)}
        table{width:100%;border-collapse:collapse}td,th{text-align:left;padding:8px 6px;border-bottom:1px solid var(--line)}
        .code{font:700 34px/1.2 Consolas,monospace;letter-spacing:4px;color:var(--accent)}
        .or{display:flex;align-items:center;gap:12px;color:var(--muted);font-size:13px;margin:16px 0}
        .or:before,.or:after{content:"";height:1px;background:var(--line);flex:1}
        .stats{display:grid;grid-template-columns:repeat(4,1fr);gap:12px;margin:0 0 16px}
        .stat{background:var(--surface);border:1px solid var(--line);border-radius:14px;padding:14px 16px}
        .stat b{display:block;font-size:24px;color:var(--accent)}.stat span{color:var(--muted);font-size:13px}
        .bar{height:10px;background:#17171b;border:1px solid var(--line);border-radius:99px;overflow:hidden;margin:8px 0}
        .bar i{display:block;height:100%;background:linear-gradient(90deg,#ffb800,#ffe23d);box-shadow:0 0 14px rgba(255,196,0,.6)}
        .tier{display:inline-block;border-radius:99px;padding:2px 12px;font-weight:700;font-size:13px;color:#111}
        .tiers{display:grid;grid-template-columns:repeat(4,1fr);gap:14px;margin-top:40px}
        .tiers .card{text-align:center}.tiers .rate{font-size:34px;font-weight:800;color:var(--accent)}
        .copy{display:flex;gap:10px}.copy input{margin:0}
        /* maquetes do aplicativo */
        .win{background:#0a0a0c;border:1px solid #2a2a31;border-radius:16px;box-shadow:0 30px 80px rgba(0,0,0,.6),0 0 60px rgba(255,196,0,.08);overflow:hidden;font-size:12px}
        .win .bar2{display:flex;gap:6px;padding:9px 12px;border-bottom:1px solid var(--line);background:#0e0e11}.win .bar2 i{width:10px;height:10px;border-radius:50%;background:#2c2c33;display:block}
        .win .body{display:grid;grid-template-columns:120px 1fr;min-height:250px}
        .win .side{background:#0d0d10;border-right:1px solid var(--line);padding:12px 10px}
        .win .side div{padding:5px 8px;border-radius:7px;color:#9a9aa3;margin-bottom:3px}.win .side div.on{background:#1b1b20;color:#fff}
        .win .main{padding:14px}
        .cover{display:inline-block;width:54px;height:78px;border-radius:8px;margin:0 6px 6px 0;background:linear-gradient(160deg,#25304a,#0d1120)}
        .cover.b{background:linear-gradient(160deg,#4a2530,#1a0d11)}.cover.c{background:linear-gradient(160deg,#1f4a3a,#0b1a14)}.cover.d{background:linear-gradient(160deg,#4a4125,#1a160b)}
        .mrow{display:flex;justify-content:space-between;padding:5px 8px;background:#121216;border-radius:7px;margin:4px 0}.mrow .a{color:var(--warn)}.mrow .g{color:var(--ok)}
        .chip{display:inline-block;background:var(--accent);color:#111;border-radius:99px;padding:1px 9px;font-weight:700;font-size:11px;margin:0 4px 4px 0}.chip.o{background:#1b1b20;color:#c9c9d1}
        .hop{display:flex;gap:8px;padding:3px 0;color:#c3c3cb;font-family:Consolas,monospace;font-size:11px}
        @media(max-width:900px){.hero .grid,.cards,.steps,.plans,.tiers,.stats{grid-template-columns:1fr 1fr}h1{font-size:36px}nav.main{display:none}}
        @media(max-width:600px){.hero .grid,.cards,.steps,.plans,.tiers,.stats{grid-template-columns:1fr}h1{font-size:31px}.hero{padding-top:48px}}
        """;

    public static string Layout(SiteOptions o, string title, string body, string? userEmail, string? description = null, string active = "")
    {
        string A(string key, string href, string text) => $"<a href=\"{href}\"{(active == key ? " style=\"color:var(--accent)\"" : "")}>{text}</a>";
        var account = userEmail is null
            ? "<a class=\"link\" href=\"/login\">Entrar</a><a class=\"go small\" href=\"/register\">Teste grátis</a>"
            : "<a class=\"link\" href=\"/account\">Minha conta</a><form method=\"post\" action=\"/logout\" style=\"display:inline\"><button class=\"link\">Sair</button></form>";
        string desc = E(description ?? "MB Extreme: otimização para Windows com biblioteca de jogos, FPS antes e depois, rede, drivers e muito mais. Teste grátis.");
        return $$"""
            <!doctype html><html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>{{E(title)}} · {{E(o.Brand)}}</title>
            <meta name="description" content="{{desc}}"><meta name="theme-color" content="#050505">
            <meta property="og:title" content="{{E(title)}} · {{E(o.Brand)}}"><meta property="og:description" content="{{desc}}"><meta property="og:type" content="website">
            <link rel="icon" href="/logo.png"><style>{{Css}}</style></head><body>
            <header class="top"><div class="wrap">
              <a class="brand" href="/"><img src="/logo.png" alt="" onerror="this.style.display='none'">MB <b>EXTREME</b></a>
              <nav class="main">{{A("features", "/#recursos", "Recursos")}}{{A("how", "/#como-funciona", "Como funciona")}}{{A("plans", "/planos", "Planos")}}{{A("download", "/download", "Download")}}{{A("aff", "/afiliados", "Afiliados")}}</nav>
              {{account}}
            </div></header>
            {{body}}
            <footer class="bottom"><div class="wrap">
              <span>© {{DateTime.UtcNow.Year}} MB InfoArt · {{E(o.Brand)}}</span>
              <a href="/termos">Termos de uso</a><a href="/privacidade">Privacidade</a><a href="/afiliados">Afiliados</a>
              <a href="mailto:{{E(o.SupportEmail)}}">{{E(o.SupportEmail)}}</a>
              <span class="sp">Todo ajuste guarda o valor original e pode ser desfeito.</span>
            </div></footer></body></html>
            """;
    }

    /// <summary>Página estreita (formulários, mensagens, conta).</summary>
    public static string Narrow(SiteOptions o, string title, string body, string? userEmail, string active = "") =>
        Layout(o, title, $"<main class=\"narrow\">{body}</main>", userEmail, null, active);

    /// <summary>Se existir wwwroot/img/{name}.png (print real do aplicativo), usa a imagem; senão, mostra a maquete em HTML.</summary>
    public static string Shot(string name, string mock, string alt)
    {
        string file = Path.Combine(AppContext.BaseDirectory, "wwwroot", "img", name + ".png");
        return File.Exists(file) ? $"<img src=\"/img/{E(name)}.png\" alt=\"{E(alt)}\" style=\"width:100%;border-radius:16px;border:1px solid var(--line)\">" : mock;
    }

    public static string Money(long cents) => PlanCatalog.Money(cents);

    /// <summary>Texto simples em Markdown (títulos, listas, negrito, tabelas ficam como texto) para as páginas legais.</summary>
    public static string Markdown(string md)
    {
        var sb = new StringBuilder();
        bool list = false;
        string Inline(string s)
        {
            string t = E(s);
            var parts = t.Split("**");
            var o = new StringBuilder();
            for (int i = 0; i < parts.Length; i++) o.Append(i % 2 == 1 ? $"<b>{parts[i]}</b>" : parts[i]);
            return o.ToString();
        }
        foreach (string raw in md.Replace("\r", "").Split('\n'))
        {
            string line = raw.TrimEnd();
            bool item = line.StartsWith("- ") || (line.Length > 2 && char.IsDigit(line[0]) && line.Contains(". ") && line.IndexOf(". ") <= 2);
            if (!item && list) { sb.Append("</ul>"); list = false; }
            if (line.Length == 0) continue;
            if (line.StartsWith("# ")) sb.Append($"<h1 style=\"font-size:32px\">{Inline(line[2..])}</h1>");
            else if (line.StartsWith("## ")) sb.Append($"<h2 style=\"font-size:22px;margin-top:26px\">{Inline(line[3..])}</h2>");
            else if (line.StartsWith("> ")) sb.Append($"<p class=\"warn\">{Inline(line[2..])}</p>");
            else if (item)
            {
                if (!list) { sb.Append("<ul>"); list = true; }
                string text = line.StartsWith("- ") ? line[2..] : line[(line.IndexOf(". ") + 2)..];
                sb.Append($"<li>{Inline(text)}</li>");
            }
            else if (line.StartsWith("|")) sb.Append($"<p class=\"muted small\">{Inline(line.Trim('|').Replace("|", " · "))}</p>");
            else sb.Append($"<p>{Inline(line)}</p>");
        }
        if (list) sb.Append("</ul>");
        return sb.ToString();
    }
}
