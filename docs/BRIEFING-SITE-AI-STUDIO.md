# Briefing do site MB Extreme (para colar no Google AI Studio)

> Como usar: copie **a seção 0** (prompt curto) como instrução principal e depois cole as seções que quiser detalhar (1 a 9), uma por vez. Tudo abaixo é a estrutura real que o servidor já tem; onde algo ainda não existe, está marcado **[A CONSTRUIR]**.

---

## 0. Prompt principal (cole primeiro)

Crie o site de lançamento do **MB Extreme**, um programa para Windows 10/11 (64 bits) da **MB InfoArt** que otimiza o PC para jogos: limpeza, desempenho, biblioteca de jogos com otimização por jogo, FPS antes e depois, análise de rede e rota, gravação e transmissão ao vivo, benchmark, drivers e acesso remoto para suporte. Público: gamers e streamers brasileiros. Idioma: **português do Brasil**. Tom: direto, confiável, sem promessas exageradas de FPS (o programa mede e mostra o antes e depois).

Visual: **tema escuro, preto e amarelo**, igual ao aplicativo. Fundo `#050505`, cartões `#0E0E11` e `#141418`, linhas `#26262C`, texto `#F2F2F4`, texto secundário `#9A9AA3`, **amarelo da marca `#FFC400`** (botões principais e destaques, texto escuro sobre o amarelo), verde `#3DDC97` (ok), laranja `#FFA94D` (aviso), vermelho `#FF5A5F` (erro). Cantos arredondados (12 a 18 px), brilho amarelo suave nos destaques, tipografia Segoe UI/Inter, layout responsivo (mobile primeiro), acessível (contraste, foco visível, rótulos nos campos) e rápido (sem bibliotecas pesadas).

Páginas: Início (landing), Planos, Download, Afiliados, Entrar, Criar conta, Minha conta, Painel do afiliado, Termos, Privacidade. Siga as seções abaixo para o conteúdo e o comportamento de cada uma. Não invente números, depoimentos ou selos que não estejam neste briefing.

---

## 1. Decisão de arquitetura (leia antes de começar)

O servidor do MB Extreme (ASP.NET) **já tem** login, cadastro, conta, pagamento (Stripe), afiliados e a ativação do programa, tudo em páginas próprias com sessão por cookie segura, proteção CSRF e limite de tentativas. **Recomendação (Opção A, mais simples e segura):**

- O AI Studio gera **só o site público** (landing, planos, download, afiliados, termos, privacidade) como site estático em `https://SEU-DOMINIO`.
- Os botões **Entrar**, **Criar conta**, **Minha conta**, **Assinar** e **Painel do afiliado** apontam para o servidor, num subdomínio como `https://conta.SEU-DOMINIO` (rotas `/login`, `/register`, `/account`, `/affiliate`). Assim o login/pagamento continuam no código já testado e você não expõe senhas nem pagamentos num front-end gerado.
- Depois, se quiser, o servidor pode ganhar uma **API JSON** para o site fazer login/conta por dentro (Opção B, seção 8). Isso exige CORS, tokens e novos testes; só vale a pena depois do lançamento.

Domínio: você citou "mb.mbinfoart"; o texto não ficou claro. Sugestão: `extreme.mbinfoarte.com.br` para o site e `conta.extreme.mbinfoarte.com.br` (ou `conta.mbinfoarte.com.br`) para o servidor. Troque `SEU-DOMINIO` abaixo pelo definitivo.

---

## 2. Marca e componentes

- Nome: **MB Extreme**; "por MB InfoArt". Logo: arquivo `logo.png` do projeto (`src/MBExtreme.LicenseServer/wwwroot/logo.png`, o mesmo do programa; fundo escuro, ícone amarelo "MX").
- Botão principal: amarelo `#FFC400`, texto `#111`, negrito, raio 12 px. Botão secundário: fundo `#141418`, borda `#26262C`, borda amarela ao passar o mouse.
- Cartões: fundo `#0E0E11`, borda `#26262C`, raio 16 px. "Pílula": borda `#4A3C00`, fundo `#1A1500`, texto amarelo.
- Passos numerados: bolinha amarela com número escuro.
- Ícones: os do programa usam **Segoe MDL2 Assets** (Windows). No site use um conjunto aberto equivalente (por exemplo Lucide ou Material Symbols), em amarelo ou branco.
- Cabeçalho fixo: logo à esquerda; links Início, Recursos, Planos, Download, Afiliados; à direita **Entrar** (secundário) e **Baixar grátis** (principal). Rodapé: Termos, Privacidade, Suporte (e-mail), links sociais, "© MB InfoArt".

---

## 3. Mapa de páginas e comportamento

| Rota | Quem vê | O que é |
|---|---|---|
| `/` | todos | Landing |
| `/planos` | todos | Preços e comparação Gratuito × Pro |
| `/download` | todos | Baixar, instalar, requisitos, aviso do Windows |
| `/afiliados` | todos | Programa de afiliados (como funciona e níveis) |
| `/termos`, `/privacidade` | todos | Textos legais (vêm de `docs/TERMOS-DE-USO.md` e `docs/POLITICA-PRIVACIDADE.md`) |
| `/login`, `/register` | visitantes | Entrar e criar conta (servidor) |
| `/account` | logado | Minha conta (servidor) |
| `/affiliate` | logado | Painel do afiliado (servidor) |
| `/activate?code=XXXX` | logado | Aprovar um computador (servidor; abre a partir do programa) |
| `/r/CODIGO` | todos | Link de indicação: grava cookie de 60 dias e redireciona para `/` |

---

## 4. Landing (`/`): seções, na ordem

1. **Herói.** Pílula: "3 dias de teste grátis do plano Pro". Título: "Seu PC no **máximo**, com segurança e tudo reversível" (palavra "máximo" em amarelo com brilho). Subtítulo: "Otimize jogos, meça o FPS antes e depois, ache o lag da sua rede e grave ou transmita, tudo num programa só." Botões: **Baixar grátis** (`/download`) e **Ver planos** (`/planos`). Selos pequenos: "Cada ajuste tem botão de desfazer", "Cria ponto de restauração antes de ações grandes", "Funciona com NVIDIA, AMD e Intel". Imagem: print do programa (a tela do **Painel** com medidores de CPU, placa de vídeo, memória, internet e energia).
2. **Como funciona (3 passos).** (1) Baixe e instale: instalador rápido, cria ponto de restauração antes de ações grandes; o plano gratuito já limpa e organiza o PC. (2) Teste o Pro por 3 dias: entre com o Google ou e-mail e aprove o PC no site. (3) Otimize e compare: escolha um jogo, clique em Otimizar, veja o FPS antes e depois; não gostou, desfaça com um clique.
3. **Recursos (cartões com ícone).** Biblioteca de jogos (Steam, Epic, GOG em capas; ao clicar, as configurações do jogo abrem ao lado) · FPS antes e depois (FPS médio e "1% pior") · Rede e rota (ping, perda e oscilação até o servidor do jogo, caminho salto a salto, atraso sob carga) · Perfis de driver NVIDIA por jogo · Ventoinhas, GPU e RGB · Limpeza e inicialização · Reparo e drivers (SFC, DISM, remoção de driver de vídeo) · Gravação e transmissão (Twitch, YouTube, RTMP, replay, vertical para Shorts/Reels, legendas ao vivo) · Acesso remoto para suporte.
4. **Prévia das telas (3 imagens).** "Prove o ganho" (comparação de FPS), "Ache o lag" (análise de rota), "Tudo do jogo num painel" (biblioteca com painel lateral). Use prints reais do programa.
5. **Teste grátis.** "3 dias grátis, sem pegadinha": começa quando o primeiro PC é ativado, uma vez por computador; ao acabar, as otimizações feitas com recursos Pro são desfeitas sozinhas (cada ajuste volta ao valor original guardado) e só voltam se assinar.
6. **Planos (resumo, 4 cartões)** com os preços da seção 5 e botão "Começar teste grátis".
7. **Comparação Gratuito × Pro (tabela).** Gratuito: Painel, informações do sistema, monitor em tempo real; limpeza, inicialização, memória, privacidade e segurança; reparo do Windows (SFC, DISM, atualização, loja); acesso remoto para suporte. Só no Pro: biblioteca de jogos com otimização por jogo e perfis de driver; FPS antes e depois, monitor de FPS e Modo Jogo; ajustes de desempenho, jogos e rede (com desfazer); análise de rota, ping, perda e atraso sob carga; gravação e transmissão, benchmark e teste de estresse; hardware e BIOS, drivers e ferramentas de arquivos.
8. **Afiliados (faixa).** "Ganhe indicando": 4 níveis (Latão 20%, Bronze 25%, Prata 30%, Ouro 35%), comissão recorrente por 12 meses, saque por Pix. Botão "Quero ser afiliado" (`/afiliados`).
9. **FAQ (acordeão).** O que acontece quando o teste acaba? · É seguro? Posso desfazer? · Funciona em placas AMD e Intel? (Sim; os perfis de driver por jogo são só NVIDIA) · Vai dar problema com antitrapaça? (O programa não injeta nada nos jogos, só lê dados do Windows; cada jogo tem sua política) · Quantos computadores posso usar? (1 no plano; cada extra custa 30% menos) · Como cancelo e como funciona o reembolso? · O Windows mostrou um aviso ao instalar? (Enquanto o instalador não tiver certificado de assinatura, o SmartScreen pode avisar "editor desconhecido": Mais informações > Executar assim mesmo) · O MB Extreme ativa Windows ou Office? (Não; nunca desliga UAC, Windows Update nem Defender).
10. **Chamada final.** "Pronto para testar? Baixe, instale e use o Pro por 3 dias."

---

## 5. Planos e preços (valores reais para 1 computador)

| Plano | Total | Equivale a | Desconto |
|---|---|---|---|
| Mensal | R$ 19,90 | R$ 19,90/mês | — |
| Trimestral | R$ 49,90 | R$ 16,63/mês | 16% |
| Semestral | R$ 89,90 | R$ 14,98/mês | 25% |
| Anual | R$ 149,90 | R$ 12,49/mês | 37% |

- Computador extra: **30% mais barato** que o primeiro (até 10 por conta). O cartão mostra "PC extra por R$ X".
- Todos incluem: todos os recursos Pro, 3 dias grátis para testar, cancele quando quiser.
- Destaque o **Anual** com a etiqueta "Melhor preço".
- Os preços vêm da API `GET /v1/plans` (seção 7). Preferir ler da API a deixar fixo no front-end.
- Botão "Assinar": leva a `/account` (logado) ou `/login?next=/account` (visitante). O pagamento é a página do **Stripe Checkout** (cartão e Pix, conforme ativado); a assinatura é recorrente.

---

## 6. Login, cadastro e conta (servidor; o site só aponta para eles)

**Criar conta** (`/register`): campos **Nome** (opcional, até 80), **E-mail**, **Senha** (8 a 200 caracteres). Botão "Entrar com o Google" (aparece só se o Client ID do Google estiver configurado). Mensagens: "E-mail inválido.", "A senha precisa ter de 8 a 200 caracteres.", "Este e-mail já tem conta. Entre com a senha.", "Muitas tentativas. Aguarde alguns minutos." Depois de criar, abre a sessão e vai para a página pedida (`next`) ou `/account`.

**Entrar** (`/login`): E-mail e senha, link "Criar conta", botão do Google. Erro único para e-mail ou senha errados ("E-mail ou senha incorretos."). Limite: 10 tentativas por IP a cada 5 minutos.

**Recuperar senha [A CONSTRUIR]:** ainda não existe envio de e-mail. Sugestão: fluxo "Esqueci minha senha" com link de uso único por e-mail (provedor SMTP/Resend), validade de 30 minutos.

**Confirmação de e-mail [A CONSTRUIR]:** ainda não existe.

**Minha conta** (`/account`), blocos nesta ordem:
1. **Plano:** "Gratuito" ou "Pro · renova em dd/mm/aaaa" (aviso "pagamento pendente" se falhou); "Teste grátis até dd/mm/aaaa" quando em teste; botão "Gerenciar assinatura e faturas" (portal do Stripe) se a assinatura foi pelo cartão; botão "Assinar o plano Pro" se gratuito.
2. **Baixar o programa:** passos (instale, abra "Licença e conta", clique "Entrar e ativar este PC").
3. **Planos:** tabela Mensal/Trimestral/Semestral/Anual com preço por mês para 1 e 2 PCs e botão Assinar (com escolha de 1 a 3 PCs).
4. **Programa de afiliados:** botão "Virar afiliado" ou "Abrir painel".
5. **Tenho uma chave de ativação:** campo `MBX-XXXX-XXXX-XXXX-XXXX` (para vendas por Pix/revenda).
6. **Computadores (N de M):** tabela Nome, Último uso, "Remover".
Todos os formulários têm proteção CSRF.

**Painel do afiliado** (`/affiliate`): link de indicação `https://SEU-DOMINIO/r/CODIGO` com botão copiar; nível atual e barra de progresso até o próximo; saldo disponível e em carência; pedido de saque por Pix (mínimo R$ 50, informa a chave Pix); tabela de comissões (data, pagamento, %, comissão, situação: em carência, disponível, paga, estornada); lista de saques.

**Aprovar computador** (`/activate`): o programa mostra um código (ex.: `ABCD-1234`); a pessoa abre o link, entra na conta, confere que o código é o mesmo e clica **Aprovar** (ou Negar). Mensagens: "Código inválido", "Limite de computadores" (remova um em Minha conta), "Computador ativado ... Você ganhou 3 dias grátis do plano Pro neste computador."

---

## 7. APIs que já existem (JSON) e o que o site pode usar

| Método e rota | Para quê | Resposta |
|---|---|---|
| `GET /v1/plans` | Planos e preços | `{currency:"BRL", trialDays:3, plans:[{key,title,months,discountPercent, offers:[{devices, totalCents, perMonthCents, ...}]}]}` |
| `GET /v1/public-key` | Chave pública de licença | texto |
| `GET /v1/app/latest` | Compilação mais nova do programa (usada pelo aviso de atualização e útil para a página Download) | `{build, version, url, sha256, size, notes, minimumBuild}` ou 404 |
| `GET /health` | Saúde do servidor | `ok` |
| `POST /v1/device/code`, `POST /v1/device/token`, `POST /v1/license/refresh`, `POST /v1/license/deactivate` | Ativação do programa (uso interno do app) | licença assinada |
| `POST /v1/webhooks/stripe` | Eventos do Stripe | uso interno |

Na página **Download**, use `GET /v1/app/latest` para mostrar "Versão atual: compilação N" e o **SHA-256** do instalador (para quem quer conferir). Os links de download vêm de `Download:SetupUrl` e `Download:PortableUrl` do servidor (precisam ser **públicos**).

---

## 8. Opção B (futuro): API JSON para o site fazer login/conta por dentro **[A CONSTRUIR]**

Só se você quiser conta e pagamento embutidos no site gerado. O servidor precisaria de:
- `POST /api/auth/register`, `/api/auth/login`, `/api/auth/google`, `/api/auth/logout`, `GET /api/me` (cookie `HttpOnly; Secure; SameSite=None` ou tokens curtos) e **CORS** liberado só para `https://SEU-DOMINIO`.
- `GET /api/account` (plano, computadores, afiliado), `POST /api/account/checkout` (devolve a URL do Stripe), `POST /api/account/billing` (URL do portal), `POST /api/account/device/remove`, `POST /api/account/redeem`.
- `GET /api/affiliate`, `POST /api/affiliate/join`, `POST /api/affiliate/payout`.
- Recuperação de senha e confirmação de e-mail (e-mails transacionais).
- Testes de segurança (CSRF por token, limite de tentativas, validação de origem).
Posso construir isso depois, quando o visual estiver pronto.

---

## 9. Requisitos não visuais

- **SEO:** títulos e descrições por página, Open Graph, `sitemap.xml` (`/`, `/planos`, `/download`, `/afiliados`, `/termos`, `/privacidade`), `robots.txt` bloqueando `/account`, `/affiliate`, `/v1/`. Dados estruturados `SoftwareApplication` (Windows, oferta em BRL).
- **Desempenho:** imagens em WebP/AVIF com `loading="lazy"`, fontes do sistema, sem animações pesadas; meta de LCP abaixo de 2,5 s.
- **Acessibilidade:** navegação por teclado, contraste mínimo 4,5:1, `alt` em todas as imagens, FAQ com `<details>`.
- **Privacidade e LGPD:** link para a Política de Privacidade em todo formulário; aviso de cookies só para cookies não essenciais (o site usa apenas cookie de sessão e o cookie de indicação de 60 dias, ambos essenciais ao funcionamento; descreva-os na política).
- **Rastreamento de indicação:** o link `/r/CODIGO` do servidor grava o cookie; o site estático não precisa tratá-lo, só manter o link como está.
- **Segurança:** HTTPS obrigatório, nenhum segredo (chaves do Stripe/Google) no código do site, `Content-Security-Policy` restritiva, sem scripts de terceiros além do necessário.
- **Textos legais:** os rascunhos em `docs/` precisam de revisão de advogado antes de publicar (reembolso de 7 dias na compra online, regras de afiliados).

---

## 10. O que ainda depende de você (para entrar no ar)
1. Confirmar o domínio e criar o subdomínio do servidor; apontar o DNS.
2. Colocar o servidor no ar com HTTPS (`deploy/`) e preencher o `.env` (Google Client ID, Stripe, administradores, download público).
3. Stripe: criar os 4 preços de assinatura e ligar os códigos `price_...` ao servidor (a conexão do assistente com o Stripe é só de leitura, então isso precisa ser feito no painel do Stripe).
4. Hospedar os instaladores em endereço **público** (o repositório atual é privado).
5. Prints reais do programa para a landing e o logo em alta resolução.
6. Revisão jurídica dos termos e da privacidade; nota fiscal e certificado de assinatura do instalador.
