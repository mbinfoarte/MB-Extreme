# Site, pagamento, login com Google e afiliados

O site é servido pelo mesmo servidor do licenciamento (`src/MBExtreme.LicenseServer`, ASP.NET + SQLite). Páginas: início (landing), planos, download, afiliados, login/cadastro, minha conta, painel do afiliado, termos e privacidade. Visual preto e amarelo, igual ao aplicativo.

## O que já está pronto
- **Landing completa:** como funciona, recursos, prévia das telas (maquetes em HTML; para usar prints reais, coloque `shot-library.png`, `shot-fps.png` e `shot-route.png` em `src/MBExtreme.LicenseServer/wwwroot/img/` e elas substituem as maquetes), teste de 3 dias, planos, comparação Gratuito × Pro, FAQ e chamada para afiliados.
- **Conta:** cadastro e login por e-mail e senha (PBKDF2) e **Entrar com o Google** (o token é conferido no servidor: assinatura RS256 com as chaves do Google, emissor, público, validade e e-mail verificado).
- **Download:** botões configuráveis (`Download:SetupUrl` e `Download:PortableUrl`).
- **Teste grátis de 3 dias** (`Billing:TrialDays`), começa quando o primeiro PC é ativado, uma vez por computador. **No aplicativo**, quando o teste ou o Pro acaba, as otimizações feitas com recursos Pro são **desfeitas sozinhas** (só com "Exigir licença" ligado e o servidor configurado) e só voltam se a pessoa assinar.
- **Pagamento:** assinatura recorrente pelo Stripe (Checkout + portal do cliente + webhook assinado), chaves de ativação para venda fora do Stripe (Pix, revenda).
- **Afiliados** (Latão, Bronze, Prata, Ouro): link `/r/CÓDIGO`, cookie de 60 dias, comissão recorrente por até 12 meses, carência de 30 dias, saque por Pix a partir de R$ 50, painel com estatísticas e nível, estorno cancela a comissão. Detalhes em `docs/PRECOS-E-AFILIADOS.md`.

## O que VOCÊ precisa fazer (eu não tenho acesso às suas contas)
1. **Domínio e hospedagem com HTTPS.** Opção pronta: um VPS com Docker e os arquivos de `deploy/` (Caddy gera o HTTPS sozinho). Passos: apontar o domínio para o IP do servidor; copiar `deploy/.env.example` para `deploy/.env` e preencher; `docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d --build`. O banco fica no volume `appdata` (**faça backup**).
2. **Google (login):** em console.cloud.google.com > APIs e serviços > Credenciais > Criar credenciais > ID do cliente OAuth > Aplicativo da Web. Em "Origens JavaScript autorizadas" ponha `https://SEU-DOMINIO`; em "URIs de redirecionamento autorizados" ponha `https://SEU-DOMINIO/auth/google`. Copie o *Client ID* para `GOOGLE_CLIENT_ID`. Configure também a tela de consentimento (nome, e-mail de suporte, link de termos e privacidade). Sem o Client ID o botão do Google simplesmente não aparece.
3. **Stripe:** crie 4 preços de assinatura em reais, 1 por período, com o valor de 1 PC (padrão sugerido: R$ 19,90 mensal, R$ 49,90 trimestral, R$ 89,90 semestral, R$ 149,90 anual; a quantidade do item vira o número de PCs). Copie os IDs (`price_...`) para `STRIPE_PRICE_*`, a chave secreta para `STRIPE_SECRET_KEY`, e crie o webhook em `https://SEU-DOMINIO/v1/webhooks/stripe` com os eventos `checkout.session.completed`, `customer.subscription.created`, `customer.subscription.updated`, `customer.subscription.deleted`, `invoice.payment_failed`, `invoice.paid` e `charge.refunded`; o segredo dele vai em `STRIPE_WEBHOOK_SECRET`. Ative Pix/cartões e o portal do cliente. **Confirme que sua conta Stripe aceita cobrar em reais e as taxas atuais**; se preferir Mercado Pago, Asaas ou Paddle, o webhook genérico `/v1/webhooks/payments` e as chaves de ativação já permitem ligar outro gateway.
4. **Download público:** os arquivos precisam estar num lugar sem login. O repositório atual do GitHub é o do código; se ele for **privado**, o link de release não abre para o público. Crie um repositório público só de *releases* (ou outro armazenamento) e ponha os links em `DOWNLOAD_SETUP_URL` e `DOWNLOAD_PORTABLE_URL`. Hoje o padrão aponta para a página de releases.
5. **Administrador:** coloque seu e-mail em `ADMIN_EMAILS` (gera chaves de ativação e vê/paga saques de afiliados).
6. **Aplicativo:** em `src/MBExtreme.Core/AppInfo.cs` preencha `DefaultServerUrl` (seu domínio) e `DefaultPublicKey` (de `https://SEU-DOMINIO/v1/public-key`) e, na versão de venda, ligue "Exigir licença" por padrão. Sem isso o app continua em beta aberto e nada é bloqueado nem desfeito.
7. **Textos legais:** `docs/TERMOS-DE-USO.md` e `docs/POLITICA-PRIVACIDADE.md` são rascunhos; revise com um advogado (o site os publica em `/termos` e `/privacidade`). Inclua as regras do programa de afiliados e do reembolso.
8. **Nota fiscal e imposto** (MEI/Simples) e certificado de assinatura de código (evita o aviso do SmartScreen) ficam por sua conta.

## Operação diária
- **Saques de afiliados:** `GET /v1/admin/payouts` lista os pedidos em aberto (e-mail, valor e chave Pix); depois de pagar por Pix, `POST /v1/admin/payouts/{id}/paid` com `{"note":"pago"}`. A autenticação é a licença assinada de uma conta listada em `ADMIN_EMAILS`.
- **Chaves de ativação:** `POST /v1/admin/keys` (veja `docs/LICENCIAMENTO.md`).
- **Ajustes sem recompilar:** `Billing:Prices:*` (preços de vitrine), `Billing:TrialDays`, `Affiliates:CommissionMonths|HoldDays|MinPayoutCents|CookieDays`, `Site:*`, `Download:*`.

## Limites conhecidos
- O envio de e-mails (confirmação de conta, recuperação de senha) **não existe** ainda.
- O painel administrativo é por API; não há tela.
- O visual foi conferido em navegador (desktop e celular), mas nada foi testado com o Stripe nem o Google reais.
- As maquetes das telas são desenhos em HTML, não prints do programa.
