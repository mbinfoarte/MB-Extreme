# MB Extreme — plano da próxima fase

Organizado a partir do pedido de 07/11/2026. Nada aqui foi implementado ainda: primeiro validar, depois aplicar por etapas.

## O que já existe (e só precisa ser ligado)

| Parte | Situação hoje |
|---|---|
| Servidor de licenças (`src/MBExtreme.LicenseServer`) | Pronto: site, login Google/e-mail, planos, checkout Stripe, webhooks, chaves de ativação, afiliados, teste grátis por PC (hoje 3 dias, configurável), acesso remoto (relay). **Não está no ar** e o app não tem o endereço dele (`AppInfo.DefaultServerUrl` vazio). |
| Plano grátis × Pro no app | Pronto: `FeaturePolicy` (lista de telas Pro), selo "Licença: Grátis", tela de upgrade, `ProLock` desfaz os ajustes Pro quando o plano acaba. |
| Atualização no app | Pronto pela metade: o app pergunta ao servidor (`/v1/app/latest`), mostra o aviso, baixa, confere o SHA-256 e instala; aceita atualização **obrigatória** (compilação mínima). Falta: o CI publicar o manifesto e uma alternativa sem servidor (GitHub Releases). |
| "O que há de novo" | Pronto: janela que abre uma vez depois de cada atualização. Os textos estão fixos no código. |

## Etapas propostas

### Etapa 1 — Atualização automática (pelo GitHub, quando o repositório ficar público) — FEITA

Como usar:
- Cada compilação do CI vira uma Release com o instalador, o arquivo `.sha256` e, como texto, a descrição do commit.
- Com o repositório **público**, o programa acha a Release sozinho (enquanto for privado, o GitHub responde "não encontrado" e nada acontece).
- Para **obrigar** a atualização: escreva `[obrigatória]` no texto da Release (editando no GitHub) ou no commit; ou `Compilação mínima: N` para barrar só quem está abaixo de N.
- No programa (Sobre → Atualizações): "Atualizar sozinho" (padrão) ou "Só avisar", e "Receber também as versões de teste (beta)".

- O app procura atualização em dois lugares: no servidor (quando estiver no ar) **e** nas Releases públicas do GitHub (`api.github.com/repos/<dono>/<repo>/releases/latest`). Grátis, sem servidor.
- Confere o SHA-256 do instalador (o GitHub informa o "digest" de cada arquivo) antes de rodar.
- Opções em Configurações: "Atualizar sozinho" (baixa em segundo plano e instala ao fechar) ou "Só avisar".
- **Atualização obrigatória**: a Release marcada com `[obrigatória]` (ou a compilação mínima no manifesto) bloqueia o uso até atualizar — é como "forçar" quem está na versão antiga.
- "O que há de novo" passa a vir das notas da Release (o texto que escrevermos no GitHub aparece no app).
- O CI já gera `beta-N`; para o público usamos tags `v1.0.0`, `v1.0.1`… (pré-lançamentos ficam de fora para o cliente).

### Etapa 2 — Grátis por 7 dias, depois Pro ou Grátis com limites
- **Teste de 7 dias do Pro começa sozinho na instalação**, sem pedir conta. O servidor guarda só um código do PC (hash), para reinstalar não renovar o teste. Sem internet, o app conta os 7 dias localmente.
- No fim dos 7 dias: tela "Seu teste acabou" com duas opções — **Assinar o Pro** (abre o checkout) ou **Continuar no Grátis** (os ajustes Pro são desfeitos pela Central Desfazer, como já funciona).
- Contagem regressiva no selo do menu ("Pro: faltam 3 dias").
- Clientes que já têm a versão grátis instalada: a atualização obrigatória leva todos para a versão nova, que começa o teste de 7 dias.
- **A decidir:** o que fica no Grátis depois do teste (sugestão abaixo).

| Grátis (sempre) | Pro |
|---|---|
| Painel e análise, Limpeza, Programas, Desinstalador, Inicialização, Saúde dos discos, Meu PC, Windows 11, Rede (velocidade, mapa, DNS), Periféricos, Pontos de restauração, Central Desfazer, Backup para formatar, Suporte | Otimizações de desempenho/jogos/rede, Benchmark, Jogo (FPS), Teste de estresse, Modo Jogo, Gargalos, Gravação e live, Vídeos curtos, Drivers (atualização automática), Ping de jogo com 2 servidores, Arquivos duplicados e grandes |

### Etapa 3 — Pagamento e ativação do Pro
- Já existe checkout pela **Stripe** (cartão; a Stripe no Brasil também aceita **Pix**). Alternativa: **Mercado Pago** (Pix e boleto, mais conhecido aqui) — o servidor já tem um webhook genérico de pagamentos.
- Fluxo no app: botão **Assinar o Pro** → abre o site já logado → paga → o app libera sozinho em segundos (o app consulta a licença).
- Ativação por **chave** (para quem compra com você no balcão): já existe; deixar a tela de chave mais simples.
- **Precisa** do servidor no ar com domínio (ex.: `app.mbinfoarte.com.br`). Opções baratas/grátis para começar: Render, Railway, Fly.io ou um VPS pequeno.

### Etapa 4 — Vídeo novo do canal (YouTube) — FEITA
- Usa o feed público do canal (`youtube.com/feeds/videos.xml?channel_id=…`): grátis, sem chave de API.
- No Painel: faixa "Vídeo novo no canal" com a miniatura, título, "Assistir" e "X" para fechar.
- Aviso do Windows quando sair vídeo novo (mesmo com o programa minimizado na bandeja), no máximo um aviso por vídeo; dá para desligar.
- **Preciso:** o link do canal.

### Etapa 5 — Nossas redes, ofertas e parceiros — FEITA (links, cupons e parceiros em `config/canal.json`)
No estilo da tela que você mostrou, com as cores da MB:
- Topo com o logo e o nome da MB InfoArt, botões **Inscrever-se** e **Seja membro**.
- Cartões das redes (YouTube, Instagram, TikTok, Facebook, WhatsApp, site).
- **Ofertas**: grupos de promoções (WhatsApp/Telegram).
- **Parceiros com cupom**: logo, descrição, cupom com botão "Copiar" e "Ver oferta".
- **Tudo vem de um arquivo online** (no GitHub ou no servidor): você troca links, cupons e parceiros sem lançar versão nova.
- **Preciso:** os links, os cupons e os parceiros.

### Etapa 6 — Sugestões e "O que há de novo" — FEITA
- Aba **Sugestões**: tipo (ideia, problema, elogio), texto e, se quiser, contato. Envia para o servidor (com o servidor fora do ar, abre o WhatsApp/e-mail com o texto pronto). Opção de anexar o relatório do PC.
- **O que há de novo**: lista das versões com as novidades de cada uma, alimentada pelas notas das Releases.

### Etapa 7 — Suporte e acesso remoto (tela nova, mais simples) — FEITA
- Uma tela só, com 3 cartões grandes:
  1. **Falar com o suporte** (WhatsApp, horário de atendimento).
  2. **Deixar o técnico acessar** — seu ID e a senha em letras grandes, botão Copiar, status ("Aguardando o técnico"), e cada acesso continua pedindo a sua permissão.
  3. **Enviar relatório do PC** com um clique.
- Modo técnico separado (para a equipe MB), escondido do cliente comum.
- Perguntas frequentes.

### Etapa 8 — Servidores LibreSpeed (código aberto) no teste de internet — FEITA
- Lista pública de servidores LibreSpeed: escolher um perto de você para download e upload (além da Cloudflare), aparecendo no mapa.
- Observação: a lista é mantida por voluntários e pode não ter servidor perto de Jacobina; mostramos a distância e o ping de cada um.

## Ordem sugerida
1. Etapa 1 (atualização pelo GitHub) — não depende de servidor.
2. Etapas 4, 5 e 6 (YouTube, redes, sugestões) — dependem só dos seus links.
3. Etapa 7 (suporte) e 8 (LibreSpeed).
4. Etapas 2 e 3 (7 dias + pagamento) — dependem de pôr o servidor no ar e escolher Stripe ou Mercado Pago.

## O que eu preciso de você
1. Link do canal do YouTube.
2. Links das redes, grupos de oferta, parceiros e cupons.
3. Stripe ou Mercado Pago (ou os dois).
4. Onde hospedar o servidor e qual domínio usar.
5. Se a divisão Grátis/Pro da tabela está boa.
6. WhatsApp e horário do suporte.
