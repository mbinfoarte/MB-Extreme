using System.Text;
using static MBExtreme.LicenseServer.SiteLayout;

namespace MBExtreme.LicenseServer;

/// <summary>Páginas públicas do site: página inicial, planos, download e afiliados.</summary>
public static class SitePages
{
    // ------------------------------------------------------------ maquetes (usadas enquanto não há prints reais em wwwroot/img)

    private static string Window(string side, string main) =>
        $"<div class=\"win\"><div class=\"bar2\"><i></i><i></i><i></i></div><div class=\"body\"><div class=\"side\">{side}</div><div class=\"main\">{main}</div></div></div>";

    private const string Side = "<div>Painel</div><div>Otimização</div><div class=\"on\">Jogos</div><div>Testes</div><div>Rede</div><div>Windows</div><div>Ferramentas</div>";

    public static string MockLibrary() => Window(Side, """
        <div style="display:grid;grid-template-columns:1fr 150px;gap:10px">
          <div><span class="cover"></span><span class="cover b"></span><span class="cover c"></span><span class="cover d"></span><span class="cover c"></span><span class="cover"></span></div>
          <div style="background:#0e0e11;border:1px solid #26262c;border-radius:10px;padding:8px">
            <b style="font-size:13px">Battlefield 6</b>
            <div style="margin:6px 0"><span class="chip">Jogar</span><span class="chip o">Otimizar</span></div>
            <div style="margin-bottom:6px"><span class="chip o">Resumo</span><span class="chip">Rede</span></div>
            <div class="mrow"><span>Energia</span><span class="a">Adaptativo</span></div>
            <div class="mrow"><span>Cache</span><span class="g">Sem limite</span></div>
          </div>
        </div>
        """);

    public static string MockFps() => Window(Side, """
        <b style="font-size:14px">Desempenho medido (FPS)</b>
        <div class="mrow" style="margin-top:8px"><span></span><span>Antes</span><span>Depois</span><span>Mudança</span></div>
        <div class="mrow"><span>FPS médio</span><span>92</span><span>108</span><span class="g">+17,4%</span></div>
        <div class="mrow"><span>1% pior</span><span>61</span><span>79</span><span class="g">+29,5%</span></div>
        <p class="muted" style="margin:10px 0 0">Ganho: FPS médio 92 → 108. Compare sempre o mesmo trecho do jogo.</p>
        """);

    public static string MockRoute() => Window(Side, """
        <b style="font-size:14px">200.150.10.4 · <span class="ok">Bom</span></b>
        <p class="muted" style="margin:4px 0 8px">média 38 ms · oscilação 2 ms · perda 0%</p>
        <div class="hop"><span>1</span><span>192.168.0.1</span><span>1 ms</span></div>
        <div class="hop"><span>2</span><span>200.150.8.1</span><span>9 ms</span></div>
        <div class="hop"><span>3</span><span>187.16.220.5</span><span>31 ms</span></div>
        <div class="hop"><span>4</span><span>13.89.178.27</span><span>38 ms</span></div>
        <p class="warn" style="margin:8px 0 0">▲ Mostra onde o atraso aparece: rede local, provedor ou caminho.</p>
        """);

    // ------------------------------------------------------------ preços

    private sealed record Row(PlanOption Option, PlanOffer One, PlanOffer Two);

    private static List<Row> Rows(SiteOptions o) => PlanCatalog.Options
        .Select(p => new Row(p, PlanCatalog.Offer(o.MonthlyCents, p, 1, o.Prices), PlanCatalog.Offer(o.MonthlyCents, p, 2, o.Prices))).ToList();

    public static string PlanCards(SiteOptions o, string cta = "/register")
    {
        var sb = new StringBuilder("<div class=\"plans\">");
        foreach (var r in Rows(o))
        {
            bool hot = r.Option.Key == "annual";
            string period = r.Option.Months == 1 ? "por mês" : $"por {r.Option.Months} meses";
            string perMonth = r.Option.Months == 1 ? "" : $"<div class=\"muted small\">equivale a {Money(r.One.PerMonthCents)}/mês</div>";
            sb.Append($$"""
                <div class="plan{{(hot ? " hot" : "")}}">{{(hot ? "<span class=\"tag\">Melhor valor</span>" : "")}}
                  <h3>{{E(r.Option.Title)}}{{(r.One.DiscountPercent > 0 ? $" <span class=\"ok small\">-{r.One.DiscountPercent}%</span>" : "")}}</h3>
                  <div class="price">{{Money(r.One.TotalCents)}} <small>{{period}}</small></div>{{perMonth}}
                  <ul><li>Todos os recursos do Pro</li><li>{{o.TrialDays}} dias grátis para testar</li><li>PC extra por {{Money(r.Two.TotalCents - r.One.TotalCents)}}</li><li>Cancele quando quiser</li></ul>
                  <a class="{{(hot ? "go" : "ghost")}}" href="{{cta}}">Começar teste grátis</a>
                </div>
                """);
        }
        return sb.Append("</div>").ToString();
    }

    private static string CompareTable() => """
        <table class="cmp"><tr><th>Recurso</th><th class="c">Gratuito</th><th class="c">Pro</th></tr>
        <tr><td>Painel, informações do sistema e monitor em tempo real</td><td class="c yes">✓</td><td class="c yes">✓</td></tr>
        <tr><td>Limpeza, inicialização, memória, privacidade e segurança</td><td class="c yes">✓</td><td class="c yes">✓</td></tr>
        <tr><td>Reparo do Windows (SFC, DISM, atualização, loja)</td><td class="c yes">✓</td><td class="c yes">✓</td></tr>
        <tr><td>Acesso remoto para suporte</td><td class="c yes">✓</td><td class="c yes">✓</td></tr>
        <tr><td>Biblioteca de jogos com otimização por jogo e perfis de driver</td><td class="c no">—</td><td class="c yes">✓</td></tr>
        <tr><td>FPS antes e depois, monitor de FPS e Modo Jogo</td><td class="c no">—</td><td class="c yes">✓</td></tr>
        <tr><td>Ajustes de desempenho, jogos e rede (com desfazer)</td><td class="c no">—</td><td class="c yes">✓</td></tr>
        <tr><td>Análise de rota, ping, perda e atraso sob carga</td><td class="c no">—</td><td class="c yes">✓</td></tr>
        <tr><td>Gravação e transmissão, benchmark e teste de estresse</td><td class="c no">—</td><td class="c yes">✓</td></tr>
        <tr><td>Hardware e BIOS, drivers e ferramentas de arquivos</td><td class="c no">—</td><td class="c yes">✓</td></tr></table>
        """;

    // ------------------------------------------------------------ página inicial

    public static string Home(SiteOptions o, string? userEmail, bool devMode)
    {
        string download = "/download";
        string body = $$"""
            <section class="hero"><div class="wrap grid">
              <div>
                <span class="pill">{{o.TrialDays}} dias de teste grátis do plano Pro</span>
                <h1>Seu PC no <em>máximo</em>, com segurança e tudo reversível</h1>
                <p class="lead">Otimize o Windows para jogos e trabalho: biblioteca de jogos com otimização por jogo, FPS antes e depois, rede, drivers e muito mais. Cada ajuste guarda o valor original e dá para desfazer.</p>
                <div class="cta"><a class="go" href="{{download}}">Baixar grátis</a><a class="ghost" href="#como-funciona">Ver como funciona</a></div>
                <p class="muted small">Windows 10 e 11 · versão {{E(o.Version)}} · sem cartão para testar</p>
              </div>
              <div>{{Shot("shot-library", MockLibrary(), "Biblioteca de jogos do MB Extreme")}}</div>
            </div></section>

            <section id="como-funciona"><div class="wrap center">
              <h2>Como funciona</h2><p class="lead">Do download ao primeiro ganho em poucos minutos.</p>
              <div class="steps" style="text-align:left">
                <div class="step"><h3>Baixe e instale</h3><p class="muted">O instalador é rápido e cria um ponto de restauração antes de ações grandes. O plano gratuito já limpa e organiza o PC.</p></div>
                <div class="step"><h3>Teste o Pro por {{o.TrialDays}} dias</h3><p class="muted">Entre com o Google ou com e-mail e aprove o PC no site. Todos os recursos Pro ficam liberados durante o teste.</p></div>
                <div class="step"><h3>Otimize e compare</h3><p class="muted">Escolha um jogo, clique em Otimizar e veja o FPS antes e depois. Não gostou? Desfaça com um clique.</p></div>
              </div>
            </div></section>

            <section id="recursos" class="alt"><div class="wrap center">
              <h2>Tudo o que seu PC precisa</h2><p class="lead">Ferramentas que normalmente exigem vários programas, num só lugar.</p>
              <div class="cards" style="text-align:left">
                <div class="card"><div class="ico">🎮</div><h3>Biblioteca de jogos</h3><p class="muted">Seus jogos da Steam, Epic e GOG em capas. Ao clicar, as configurações daquele jogo abrem ao lado: otimizar, jogar, medir e desfazer.</p></div>
                <div class="card"><div class="ico">📈</div><h3>FPS antes e depois</h3><p class="muted">Abra o jogo pelo programa, jogue, otimize e jogue de novo. O MB Extreme compara FPS médio e o "1% pior" para provar o ganho.</p></div>
                <div class="card"><div class="ico">🌐</div><h3>Rede e rota</h3><p class="muted">Ping, perda e oscilação até o servidor do jogo, caminho salto a salto e teste de atraso sob carga, para achar onde está o lag.</p></div>
                <div class="card"><div class="ico">🟩</div><h3>Perfis de driver NVIDIA</h3><p class="muted">Ajustes por jogo (Battlefield, CS2, Valorant, Apex e outros) no perfil de driver, com valores explicados e desfazer.</p></div>
                <div class="card"><div class="ico">🌀</div><h3>Ventoinhas, GPU e RGB</h3><p class="muted">Curva de ventoinha com travas de segurança, limites de potência da placa de vídeo e iluminação RGB.</p></div>
                <div class="card"><div class="ico">🧹</div><h3>Limpeza e inicialização</h3><p class="muted">Arquivos temporários, programas na inicialização, memória e privacidade, sem apagar o que importa.</p></div>
                <div class="card"><div class="ico">🛠</div><h3>Reparo e drivers</h3><p class="muted">SFC, DISM e correções do Windows, remoção de driver de vídeo (como o DDU) e atualização de drivers.</p></div>
                <div class="card"><div class="ico">🎥</div><h3>Gravação e transmissão</h3><p class="muted">Grave a tela ou o jogo, transmita para Twitch, YouTube ou RTMP e guarde o replay dos melhores momentos.</p></div>
                <div class="card"><div class="ico">🖥</div><h3>Acesso remoto</h3><p class="muted">Peça ajuda a um técnico ou ajude alguém: conexão por ID e código de segurança, com o seu aceite a cada sessão.</p></div>
              </div>
            </div></section>

            <section><div class="wrap">
              <div class="center"><h2>Veja o programa em ação</h2><p class="lead">Telas reais do MB Extreme.</p></div>
              <div class="cards" style="margin-top:34px">
                <div>{{Shot("shot-fps", MockFps(), "Comparação de FPS antes e depois")}}<h3 style="margin-top:12px">Prove o ganho</h3><p class="muted">Compare as suas próprias medições, sem promessas de número.</p></div>
                <div>{{Shot("shot-route", MockRoute(), "Análise de rota até o servidor do jogo")}}<h3 style="margin-top:12px">Ache o lag</h3><p class="muted">Veja se o atraso vem do Wi-Fi, do provedor ou do caminho.</p></div>
                <div>{{Shot("shot-library", MockLibrary(), "Painel do jogo ao lado da biblioteca")}}<h3 style="margin-top:12px">Tudo do jogo num painel</h3><p class="muted">Abas de resumo, otimização, desempenho e rede.</p></div>
              </div>
            </div></section>

            <section class="alt" id="teste"><div class="wrap center">
              <h2>{{o.TrialDays}} dias grátis, sem pegadinha</h2>
              <p class="lead">Durante o teste você usa todos os recursos Pro. Quando o teste termina, o MB Extreme <b>desfaz automaticamente as otimizações feitas</b> e elas só voltam a valer se você ativar o plano Pro. O plano gratuito continua com o que não é Pro.</p>
              <div class="cta" style="justify-content:center"><a class="go" href="{{download}}">Baixar e testar grátis</a></div>
            </div></section>

            <section id="planos"><div class="wrap center">
              <h2>Planos</h2><p class="lead">Escolha o período. Quanto maior, menor o valor por mês. Mais de um PC? Cada computador extra custa 30% menos.</p>
              {{PlanCards(o)}}
              <p class="muted small" style="margin-top:16px">Pagamento recorrente, cancele quando quiser. Direito de arrependimento de 7 dias na contratação online.</p>
              {{CompareTable()}}
            </div></section>

            <section class="alt"><div class="wrap center">
              <h2>Ganhe indicando</h2><p class="lead">Programa de afiliados com 4 níveis: Latão, Bronze, Prata e Ouro. De {{(int)(AffiliateProgram.Tiers[0].Rate * 100)}}% a {{(int)(AffiliateProgram.Tiers[^1].Rate * 100)}}% de comissão recorrente por até {{o.Affiliate.CommissionMonths}} meses.</p>
              <div class="cta" style="justify-content:center"><a class="go" href="/afiliados">Quero ser afiliado</a></div>
            </div></section>

            <section id="faq"><div class="wrap" style="max-width:820px">
              <h2 class="center">Perguntas frequentes</h2>
              <details><summary>O que acontece quando o teste grátis acaba?</summary><p>O MB Extreme desfaz sozinho as otimizações feitas com recursos Pro (cada ajuste volta ao valor original guardado). Se você assinar o Pro, é só otimizar de novo. O que é do plano gratuito continua funcionando.</p></details>
              <details><summary>É seguro? Posso desfazer?</summary><p>Cada ajuste guarda o valor original antes de mudar e tem botão de desfazer. Ações maiores criam um ponto de restauração do Windows. Mesmo assim, recomendamos manter backup dos seus dados.</p></details>
              <details><summary>Funciona em placas AMD e Intel?</summary><p>Sim. Os ajustes do Windows, a medição de FPS e a análise de rede funcionam com qualquer placa. Os perfis de driver por jogo são para placas NVIDIA GeForce.</p></details>
              <details><summary>Vai dar problema com antitrapaça?</summary><p>O MB Extreme não injeta nada nos jogos: ele lê dados do próprio Windows. Mesmo assim, cada jogo tem sua política; na dúvida, teste fora de partidas ranqueadas.</p></details>
              <details><summary>Quantos computadores posso usar?</summary><p>O plano inclui 1 computador; cada PC extra custa 30% menos. Você gerencia seus computadores em Minha conta.</p></details>
              <details><summary>Como cancelo e como funciona o reembolso?</summary><p>Cancele quando quiser no portal da assinatura; o plano vale até o fim do período pago. Na contratação online você tem 7 dias de arrependimento conforme o Código de Defesa do Consumidor.</p></details>
              <details><summary>O Windows mostra um aviso ao instalar. É normal?</summary><p>Enquanto o instalador não tiver certificado de assinatura, o Windows SmartScreen pode avisar sobre "editor desconhecido". Baixe só deste site, clique em "Mais informações" e depois em "Executar assim mesmo".</p></details>
              <details><summary>O MB Extreme ativa Windows ou Office?</summary><p>Não. O MB Extreme não faz ativação de software e nunca desliga o UAC, o Windows Update ou o Defender.</p></details>
            </div></section>

            <section class="alt"><div class="wrap center">
              <h2>Pronto para testar?</h2><p class="lead">Baixe, instale e use o Pro por {{o.TrialDays}} dias.</p>
              <div class="cta" style="justify-content:center"><a class="go" href="{{download}}">Baixar grátis</a><a class="ghost" href="/register">Criar conta</a></div>
              {{(devMode ? "<p class=\"warn\">Modo de teste ligado: não há cobrança real.</p>" : "")}}
            </div></section>
            """;
        return Layout(o, "Otimizador para Windows", body, userEmail, null, "home");
    }

    // ------------------------------------------------------------ planos

    public static string Plans(SiteOptions o, string? userEmail) => Layout(o, "Planos", $$"""
        <section><div class="wrap center">
          <h1 style="font-size:38px">Planos e preços</h1>
          <p class="lead">Use o Pro por {{o.TrialDays}} dias grátis. Depois, escolha o período que preferir.</p>
          {{PlanCards(o)}}
          <p class="muted small" style="margin-top:16px">Pagamento recorrente em reais, cancele quando quiser. Direito de arrependimento de 7 dias na contratação online. Mais de um PC: cada extra custa 30% menos.</p>
          {{CompareTable()}}
        </div></section>
        """, userEmail, "Planos e preços do MB Extreme.", "plans");

    // ------------------------------------------------------------ download

    public static string Download(SiteOptions o, string? userEmail)
    {
        string Button(string url, string label, bool primary) => url.Length == 0
            ? $"<span class=\"ghost\" style=\"opacity:.6\">{label} (em breve)</span>"
            : $"<a class=\"{(primary ? "go" : "ghost")}\" href=\"{E(url)}\" rel=\"noopener\">{label}</a>";
        return Layout(o, "Download", $$"""
            <section><div class="wrap" style="max-width:860px">
              <div class="center"><span class="pill">Grátis · {{o.TrialDays}} dias de Pro incluídos</span>
              <h1 style="font-size:38px">Baixar o MB Extreme</h1>
              <p class="lead">Windows 10 ou 11 (64 bits). Versão {{E(o.Version)}}.</p>
              <div class="cta" style="justify-content:center">{{Button(o.SetupUrl, "Baixar instalador", true)}}{{Button(o.PortableUrl, "Versão portátil (.zip)", false)}}</div></div>

              <div class="steps" style="margin-top:44px">
                <div class="step"><h3>Instale</h3><p class="muted">Execute o instalador e aceite o pedido de administrador (o programa altera configurações do Windows).</p></div>
                <div class="step"><h3>Ative o teste</h3><p class="muted">No programa, abra <b>Licença e conta</b> e clique em <b>Entrar e ativar este PC</b>. Entre com o Google ou e-mail e aprove no site.</p></div>
                <div class="step"><h3>Otimize</h3><p class="muted">Abra a biblioteca de jogos, escolha um jogo e clique em Otimizar. Dá para desfazer a qualquer momento.</p></div>
              </div>

              <div class="box" style="margin-top:30px"><h3>Requisitos</h3>
                <ul class="muted"><li>Windows 10 ou 11, 64 bits, com permissão de administrador.</li><li>Placa NVIDIA GeForce para os perfis de driver por jogo (o restante funciona com qualquer placa).</li><li>Internet para ativar o teste e renovar a licença.</li></ul></div>
              <div class="box"><h3>O Windows mostrou um aviso?</h3>
                <p class="muted">Enquanto o instalador não tem certificado de assinatura, o SmartScreen pode avisar sobre "editor desconhecido". Baixe somente desta página, clique em <b>Mais informações</b> e depois em <b>Executar assim mesmo</b>.</p></div>
              <div class="box"><h3>Quando o teste acabar</h3>
                <p class="muted">As otimizações feitas durante o teste são desfeitas automaticamente e só voltam se você ativar o plano Pro. <a href="/planos" style="color:var(--accent)">Ver planos</a>.</p></div>
            </div></section>
            """, userEmail, "Baixe o MB Extreme grátis para Windows e teste o plano Pro por alguns dias.", "download");
    }

    // ------------------------------------------------------------ afiliados (página pública)

    public static string AffiliatesLanding(SiteOptions o, string? userEmail, bool isAffiliate)
    {
        var p = o.Affiliate;
        var sb = new StringBuilder("<div class=\"tiers\">");
        foreach (var t in AffiliateProgram.Tiers)
            sb.Append($"<div class=\"card\"><span class=\"tier\" style=\"background:{t.Color}\">{E(t.Name)}</span><div class=\"rate\">{(int)Math.Round(t.Rate * 100)}%</div><p class=\"muted small\">{(t.MinCustomers == 0 ? "Ao entrar no programa" : $"A partir de {t.MinCustomers} clientes pagantes")}</p></div>");
        sb.Append("</div>");
        string cta = isAffiliate ? "<a class=\"go\" href=\"/affiliate\">Abrir meu painel</a>"
            : userEmail is null ? "<a class=\"go\" href=\"/register?next=/affiliate\">Criar conta e virar afiliado</a><a class=\"ghost\" href=\"/login?next=/affiliate\">Já tenho conta</a>"
            : "<a class=\"go\" href=\"/affiliate\">Virar afiliado agora</a>";
        return Layout(o, "Programa de afiliados", $$"""
            <section class="hero"><div class="wrap" style="max-width:900px">
              <div class="center"><span class="pill">Programa de afiliados</span>
              <h1>Ganhe indicando o <em>MB Extreme</em></h1>
              <p class="lead">Divulgue o seu link e receba uma porcentagem de cada pagamento dos clientes que você indicar, por até {{p.CommissionMonths}} meses. Quanto mais clientes, maior o seu nível.</p>
              <div class="cta" style="justify-content:center">{{cta}}</div></div>
              {{sb}}
            </div></section>
            <section class="alt"><div class="wrap" style="max-width:900px">
              <h2 class="center">Como funciona</h2>
              <div class="steps" style="margin-top:34px">
                <div class="step"><h3>Pegue o seu link</h3><p class="muted">Crie a conta, entre no painel de afiliado e copie o link com o seu código.</p></div>
                <div class="step"><h3>Divulgue</h3><p class="muted">Vídeos, lives, grupos e redes sociais. Quem clicar e assinar em até {{p.CookieDays}} dias é seu cliente.</p></div>
                <div class="step"><h3>Receba por Pix</h3><p class="muted">A comissão fica disponível após {{p.HoldDays}} dias. Com saldo a partir de {{Money(p.MinPayoutCents)}}, peça o saque no painel.</p></div>
              </div>
              <div class="box" style="margin-top:34px"><h3>Regras resumidas</h3><ul class="muted">
                <li>Comissão recorrente: você ganha a % do seu nível sobre cada pagamento do cliente por até {{p.CommissionMonths}} meses.</li>
                <li>O nível sobe pelo número de clientes pagantes confirmados (que já passaram da carência) e vale para as próximas comissões.</li>
                <li>Carência de {{p.HoldDays}} dias para cobrir o direito de arrependimento e estornos. Pagamentos estornados cancelam a comissão.</li>
                <li>Saque por Pix, com saldo mínimo de {{Money(p.MinPayoutCents)}}, conferido e pago manualmente.</li>
                <li>Não vale indicar a si mesmo, usar spam, anúncios enganosos ou prometer ganhos que o programa não garante. Contas com fraude perdem as comissões.</li>
                <li>As regras podem mudar com aviso prévio. Valores calculados sobre o que o cliente efetivamente pagou.</li></ul></div>
            </div></section>
            """, userEmail, "Programa de afiliados do MB Extreme: níveis Latão, Bronze, Prata e Ouro com comissão recorrente.", "aff");
    }

    // ------------------------------------------------------------ painel do afiliado

    public static string AffiliateDashboard(SiteOptions o, string userEmail, string csrfField, Db.Affiliate aff, Db.AffiliateStats stats,
        IReadOnlyList<Db.Commission> commissions, IReadOnlyList<Db.Payout> payouts, string baseUrl, string? message, bool messageIsError)
    {
        var p = o.Affiliate;
        var tier = AffiliateProgram.TierFor(stats.PaidCustomers);
        var next = AffiliateProgram.Next(stats.PaidCustomers);
        string link = $"{baseUrl}/r/{aff.Code}";
        int percent = next is null ? 100 : (int)Math.Clamp(100.0 * (stats.PaidCustomers - tier.MinCustomers) / Math.Max(1, next.Value.Tier.MinCustomers - tier.MinCustomers), 0, 100);
        string progress = next is null
            ? "<p class=\"ok\">Você está no nível máximo: Ouro!</p>"
            : $"<p class=\"muted\">Faltam <b>{next.Value.Missing}</b> cliente(s) pagante(s) para o nível <b>{E(next.Value.Tier.Name)}</b> ({(int)Math.Round(next.Value.Tier.Rate * 100)}%).</p>";
        string rows = commissions.Count == 0 ? "<tr><td class=\"muted\">Ainda não há comissões. Divulgue o seu link!</td></tr>"
            : string.Concat(commissions.Take(20).Select(c => $"<tr><td>{c.CreatedAt.LocalDateTime:dd/MM/yyyy}</td><td>{Money(c.AmountPaidCents)}</td><td>{(int)Math.Round(c.Rate * 100)}%</td><td><b>{Money(c.CommissionCents)}</b></td><td class=\"{(c.Status == "reversed" ? "bad" : p.Available(c.CreatedAt, DateTimeOffset.UtcNow) ? "ok" : "warn")}\">{(c.Status == "reversed" ? "estornada" : c.PayoutId is not null ? "em saque" : p.Available(c.CreatedAt, DateTimeOffset.UtcNow) ? "disponível" : "em carência")}</td></tr>"));
        string payoutRows = payouts.Count == 0 ? "" : "<h3 style=\"margin-top:18px\">Saques</h3><table>" + string.Concat(payouts.Take(10).Select(x =>
            $"<tr><td>{x.RequestedAt.LocalDateTime:dd/MM/yyyy}</td><td>{Money(x.AmountCents)}</td><td class=\"{(x.PaidAt is null ? "warn" : "ok")}\">{(x.PaidAt is null ? "aguardando pagamento" : $"pago em {x.PaidAt.Value.LocalDateTime:dd/MM/yyyy}")}</td></tr>")) + "</table>";
        string note = message is null ? "" : $"<p class=\"{(messageIsError ? "bad" : "ok")}\">{E(message)}</p>";

        string body = $$"""
            <h1>Painel do afiliado</h1>
            <p><span class="tier" style="background:{{tier.Color}}">{{E(tier.Name)}}</span> <span class="muted">· {{(int)Math.Round(tier.Rate * 100)}}% de comissão · {{E(userEmail)}}</span></p>
            {{note}}
            <div class="stats">
              <div class="stat"><b>{{stats.Signups}}</b><span>cadastros indicados</span></div>
              <div class="stat"><b>{{stats.PaidCustomers}}</b><span>clientes pagantes</span></div>
              <div class="stat"><b>{{Money(stats.AvailableCents)}}</b><span>disponível para saque</span></div>
              <div class="stat"><b>{{Money(stats.PendingCents)}}</b><span>em carência ({{p.HoldDays}} dias)</span></div>
            </div>
            <div class="box"><h3>Seu link de indicação</h3>
              <div class="copy"><input id="lnk" readonly value="{{E(link)}}"><button class="go" type="button" onclick="navigator.clipboard.writeText(document.getElementById('lnk').value);this.textContent='Copiado!'">Copiar</button></div>
              <p class="muted small">Vale por {{p.CookieDays}} dias depois do clique. Código: <b>{{E(aff.Code)}}</b></p></div>
            <div class="box"><h3>Seu nível</h3><div class="bar"><i style="width:{{percent}}%"></i></div>{{progress}}
              <p class="muted small">Latão {{(int)(AffiliateProgram.Tiers[0].Rate * 100)}}% · Bronze {{(int)(AffiliateProgram.Tiers[1].Rate * 100)}}% (5 clientes) · Prata {{(int)(AffiliateProgram.Tiers[2].Rate * 100)}}% (20) · Ouro {{(int)(AffiliateProgram.Tiers[3].Rate * 100)}}% (50)</p></div>
            <div class="box"><h3>Saque por Pix</h3>
              <form method="post" action="/affiliate/payout">{{csrfField}}
                <label>Sua chave Pix<input name="pix" maxlength="120" value="{{E(aff.PixKey)}}" placeholder="CPF, e-mail, telefone ou chave aleatória" required></label>
                <button class="go">{{(stats.AvailableCents >= p.MinPayoutCents ? $"Pedir saque de {Money(stats.AvailableCents)}" : "Salvar chave Pix")}}</button>
                <span class="muted small"> Saque a partir de {{Money(p.MinPayoutCents)}}. Em saque: {{Money(stats.RequestedCents)}} · Já pago: {{Money(stats.PaidCents)}}</span>
              </form>{{payoutRows}}</div>
            <div class="box"><h3>Comissões</h3><table><tr><th>Data</th><th>Pagamento</th><th>%</th><th>Comissão</th><th>Situação</th></tr>{{rows}}</table></div>
            """;
        return Narrow(o, "Painel do afiliado", body, userEmail, "aff");
    }

    // ------------------------------------------------------------ login e cadastro (com Google)

    public static string GoogleButton(SiteOptions o, string baseUrl) => o.GoogleClientId.Length == 0 ? "" : $$"""
        <div class="or">ou</div>
        <script src="https://accounts.google.com/gsi/client" async defer></script>
        <div id="g_id_onload" data-client_id="{{E(o.GoogleClientId)}}" data-login_uri="{{E(baseUrl)}}/auth/google" data-ux_mode="redirect" data-auto_prompt="false"></div>
        <div class="g_id_signin" data-type="standard" data-theme="filled_black" data-size="large" data-text="continue_with" data-shape="pill" data-width="320" data-locale="pt-BR"></div>
        """;

    public static string AuthForm(SiteOptions o, string title, string action, string button, bool register, string? error, string? next, string baseUrl, string? terms = null)
    {
        string hidden = next is null ? "" : $"<input type=\"hidden\" name=\"next\" value=\"{E(next)}\">";
        string switchText = register
            ? $"Já tem conta? <a href=\"/login{(next is null ? "" : "?next=" + Uri.EscapeDataString(next))}\" style=\"color:var(--accent)\">Entrar</a>"
            : $"Novo aqui? <a href=\"/register{(next is null ? "" : "?next=" + Uri.EscapeDataString(next))}\" style=\"color:var(--accent)\">Criar conta e testar {o.TrialDays} dias grátis</a>";
        string body = $$"""
            <div style="max-width:440px;margin:0 auto">
            <h1 style="font-size:32px">{{E(title)}}</h1>
            {{(error is null ? "" : $"<p class=\"bad\">{E(error)}</p>")}}
            <form method="post" action="{{E(action)}}" class="box">
              {{hidden}}
              {{(register ? "<label>Nome<input name=\"name\" maxlength=\"80\" autocomplete=\"name\"></label>" : "")}}
              <label>E-mail<input name="email" type="email" required autocomplete="email"></label>
              <label>Senha{{(register ? " (mínimo de 8 caracteres)" : "")}}<input name="password" type="password" required minlength="8" autocomplete="{{(register ? "new-password" : "current-password")}}"></label>
              <button class="go" style="width:100%">{{E(button)}}</button>
              {{GoogleButton(o, baseUrl)}}
            </form>
            <p class="muted small center">{{switchText}}</p>
            {{(register ? "<p class=\"muted small center\">Ao criar a conta você aceita os <a href=\"/termos\" style=\"color:var(--accent)\">termos de uso</a> e a <a href=\"/privacidade\" style=\"color:var(--accent)\">política de privacidade</a>.</p>" : "")}}
            </div>
            """;
        return Narrow(o, title, body, null);
    }
}
