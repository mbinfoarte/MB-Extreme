using System.Net;
using System.Text;

namespace MBExtreme.LicenseServer;

/// <summary>Páginas do site em HTML simples (preto e amarelo, como o aplicativo). Todo texto vindo do usuário passa por HtmlEncode.</summary>
public static class Pages
{
    public static string E(string? text) => WebUtility.HtmlEncode(text ?? "");

    public static string Page(string title, string body, string? userEmail = null)
    {
        var nav = new StringBuilder("<a href=\"/\">Início</a>");
        if (userEmail is null) nav.Append("<a href=\"/login\">Entrar</a><a href=\"/register\">Criar conta</a>");
        else nav.Append("<a href=\"/account\">Minha conta</a><form method=\"post\" action=\"/logout\" style=\"display:inline\"><button class=\"link\">Sair</button></form>");

        return $$"""
            <!doctype html><html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>{{E(title)}} · MB Extreme</title>
            <style>
              body{background:#050505;color:#f2f2f4;font:16px/1.5 "Segoe UI",system-ui,sans-serif;margin:0}
              header{display:flex;gap:18px;align-items:center;padding:16px 28px;border-bottom:1px solid #26262c}
              header b{color:#ffc400;font-size:18px;margin-right:auto} header a,.link{color:#c9c9d1;text-decoration:none;background:none;border:0;font:inherit;cursor:pointer}
              main{max-width:760px;margin:36px auto;padding:0 20px}
              .card{background:#111114;border:1px solid #26262c;border-radius:16px;padding:20px 22px;margin:0 0 16px}
              h1{font-size:28px;margin:0 0 6px} .muted{color:#8e8e96}
              input{width:100%;box-sizing:border-box;background:#17171b;color:#f2f2f4;border:1px solid #2c2c33;border-radius:10px;padding:10px 12px;margin:6px 0 14px;font:inherit}
              button.go,a.go{display:inline-block;background:linear-gradient(#ffdd33,#ffc400);color:#111;border:0;border-radius:12px;padding:10px 20px;font-weight:600;font:inherit;cursor:pointer;text-decoration:none}
              .ok{color:#3ddc97}.warn{color:#ffa94d}.bad{color:#ff5a5f} table{width:100%;border-collapse:collapse} td,th{text-align:left;padding:8px 6px;border-bottom:1px solid #26262c}
              .code{font:700 34px/1.2 Consolas,monospace;letter-spacing:4px;color:#ffc400}
            </style></head><body><header><b>MB EXTREME</b>{{nav}}</header><main>{{body}}</main></body></html>
            """;
    }

    public static string Home(bool devMode) => Page("Planos", $$"""
        <h1>MB Extreme</h1><p class="muted">Otimização e diagnóstico para Windows, feito por MB InfoArt.</p>
        <div class="card"><h2>Gratuito</h2><p>Painel, limpeza, inicialização, segurança, reparo do Windows e mais. Para sempre.</p></div>
        <div class="card"><h2>Pro</h2><p>Gravação e transmissão, benchmark 3D, teste de estresse, diagnóstico de latência, hardware e BIOS, ajustes avançados e ferramentas de arquivos.</p>
        <p class="muted">Assinatura mensal, cancele quando quiser. Até 2 computadores.</p>
        <a class="go" href="/account">Assinar</a></div>
        {{(devMode ? "<p class=\"warn\">Modo de teste ligado: não há cobrança real.</p>" : "")}}
        """);

    public static string Message(string title, string text, string css = "muted") =>
        Page(title, $"<h1>{E(title)}</h1><p class=\"{css}\">{E(text)}</p><p><a class=\"go\" href=\"/\">Voltar</a></p>");

    public static string Form(string title, string action, string button, bool register, string? error, string? next = null)
    {
        string hidden = next is null ? "" : $"<input type=\"hidden\" name=\"next\" value=\"{E(next)}\">";
        return Page(title, $$"""
            <h1>{{E(title)}}</h1>
            {{(error is null ? "" : $"<p class=\"bad\">{E(error)}</p>")}}
            <form method="post" action="{{E(action)}}" class="card">
              {{hidden}}
              <label>E-mail<input name="email" type="email" required autocomplete="email"></label>
              <label>Senha{{(register ? " (mínimo de 8 caracteres)" : "")}}<input name="password" type="password" required minlength="8" autocomplete="{{(register ? "new-password" : "current-password")}}"></label>
              <button class="go">{{E(button)}}</button>
            </form>
            """);
    }
}
