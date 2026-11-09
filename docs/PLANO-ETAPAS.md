# Plano de etapas (próximas melhorias)

## Situação em 22/10 (implementado localmente, ainda não publicado)

| Etapa | Estado | O que falta |
|---|---|---|
| 1 Painel limpo | Feita | Conferir visualmente no Windows |
| 2 Estúdio redesenhado | Feita (blocos, pré-visualização, abas, predefinições, Facebook, saúde da transmissão) | Cenas/webcam/sobreposições estilo OBS; câmera virtual (Discord); teste visual no Windows |
| 3 Biblioteca de jogos | Feita (Steam, Epic, GOG, manual, Modo Jogo ao abrir, tempo jogado, prioridade por jogo) | Banco curado de jogos e edição de configs do jogo (Fase B); Xbox/EA/Ubisoft/Battle.net/Riot |
| 4 Gargalos | Feita (regras, relatório, histórico local, linha no overlay) | Envio opcional ao servidor (cliente pronto, falta o endpoint); VRAM e falhas de página; teste em jogo real |
| 5 Acesso remoto | Núcleo feito e testado ponta a ponta (cripto, servidor, agente, console) | 5.4 especiais, 5.5 serviço do Windows (UAC/login), 5.6 conexão direta/H.264/arquivos, 5.7 teste externo e 2FA; teste em duas máquinas Windows. Ver `ACESSO-REMOTO.md` |
| 6 Licença/cobrança | Feita (teste 7 dias, planos, Stripe, chaves) | Testar o Stripe em modo de teste; fixar URL e chave pública; site do cliente |
| 7 Qualidade/lançamento | Parcial (CI assina se houver certificado, textos legais em rascunho, lista) | Certificado, revisão jurídica, revisão de código independente. Ver `LANCAMENTO.md` |
| 8 Catálogo expandido | Feita (≈30 ajustes e reparos novos, navegadores, limpeza semanal, shaders) | Itens "por teste" (fila de dados de mouse/teclado, RAM por quantidade) e teste em Windows real |

(A "Etapa 9" citada no pedido não existe: o plano termina na 8.)

Organização do pedido de 22/10 em etapas, na ordem sugerida de execução. Tamanhos: **P** (horas a 1 dia), **M** (alguns dias), **G** (semanas), **GG** (muitas semanas).

## Etapa 1 · Painel mais limpo (P)
1. Remover do painel os cartões **Uso da CPU** e **Limpeza do sistema** (a limpeza continua no menu Otimização e no botão do topo).
2. Manter os medidores redondos e **acoplar abaixo de cada um a linha animada** do histórico, como a do cartão removido:
   - CPU: medidor de temperatura + linha de uso (%).
   - Placa de vídeo: medidor de temperatura + linha de uso (%).
   - Memória: medidor de uso + linha do histórico.
   - Internet: medidor de ping + linha do ping (e a rede continua no cartão grande de download/envio).
3. Botão da limpeza automática da memória: **só o ícone de vassoura**, sem texto (tooltip explica). Corrige também o defeito visto na captura: o texto "Auto" aparecia como quadradinhos porque a fonte de ícones era aplicada ao texto todo.
4. Suporte: o AnyDesk deixa de ser a tela principal. A página passa a ter **Pedir suporte** em destaque (texto pronto com nome, versão e relatório do sistema) e o AnyDesk vira uma opção **secundária** ("Outras opções de acesso remoto"), sem abrir sozinho. Quando o módulo próprio da Etapa 5 existir, ele passa a ser o principal.

## Etapa 2 · Estúdio de gravação e transmissão redesenhado (G)
Referência: a tela do ShadowPlay/GeForce Experience (poucos botões grandes, o resto escondido em configurações).

**2.1 Estrutura da tela**
- Linha de **três blocos grandes**: *Replay instantâneo*, *Gravar*, *Transmitir ao vivo*. Cada um mostra o estado (desligado, gravando 00:12, ao vivo) e liga/desliga com um clique.
- À direita: **Galeria** (gravações e clipes), e três ícones: microfone, áudio do PC e **Configurações**.
- Tudo que hoje é lista longa (resolução, fps, taxa, codificador, áudio, transmissão, atalhos) vai para **Configurações**, em abas: *Vídeo*, *Áudio*, *Transmissão*, *Replay*, *Atalhos*. Os padrões já vêm bons (predefinição **Leve / Equilibrado / Qualidade**), para a maioria das pessoas nem abrir.
- "O que capturar" fica em uma faixa simples: *Tela* ou *Janela*, com a lista de telas/janelas.

**2.2 Pré-visualização ao escolher uma janela (ou tela)**
- Janela pequena no app mostrando em tempo real o que será capturado (5 a 10 quadros por segundo, reduzida, para quase não pesar).
- Técnica proposta: duplicação da tela pela placa de vídeo (DXGI, a mesma base da captura) recortada na janela; para janelas comuns, captura por GDI. Alternativa mais precisa para jogos: Windows Graphics Capture.
- Mostra também uma aviso se a janela sair preta (jogo em DirectX com o modo GDI) e sugere o outro modo.

**2.3 Transmissão estilo OBS, sem pesar**
- Predefinições por plataforma: Twitch, YouTube, Kick, Facebook e servidor RTMP próprio.
- Codificação sempre na placa de vídeo (NVENC, AMF, Quick Sync), taxa constante (CBR), intervalo de quadro-chave de 2 s, limite de taxa por plataforma.
- **Modo leve** (padrão): 720p ou 1080p a 30/60 fps, escolha automática pela potência da máquina (usa o Assistente de jogo).
- Painel de saúde da transmissão: taxa real, quadros perdidos, uso da placa pelo codificador; aviso e sugestão (baixar resolução) se perder quadros.
- Depois (opcional): cenas, webcam e sobreposições estilo OBS.
- **Discord:** o Discord não aceita transmissão RTMP externa; só pelo próprio aplicativo (Go Live). Para ele, a opção possível é uma **câmera virtual**, que exige driver; fica como item futuro.

## Etapa 3 · Biblioteca de jogos (G)
1. **Detecção** dos jogos instalados: Steam (pastas de biblioteca e manifestos), Epic Games, GOG, Xbox/Game Pass, EA, Ubisoft, Battle.net e Riot, mais "adicionar jogo manualmente".
2. **Tela de biblioteca** com capa/ícone, nome, tempo jogado e perfil aplicado. Clique no jogo abre o painel do jogo.
3. **Pré-configuração por hardware**: usa o Assistente de jogo (nível da máquina) para sugerir energia, prioridade, Modo Jogo, e as configurações gráficas recomendadas do jogo.
   - Fase A: perfis genéricos por faixa de hardware (já temos a base).
   - Fase B: banco curado dos jogos mais populares, com ajustes específicos. Edição de arquivo de configuração do jogo só com backup e só para jogos conhecidos.
4. **Iniciar o jogo de dentro do MB Extreme** (Steam/Epic/exe), ligando antes o Modo Jogo e o perfil, e desfazendo tudo quando o jogo fecha.

## Etapa 4 · Monitor de gargalos em tempo real (G)
Mede enquanto o jogo roda e entrega um relatório no fim da sessão.
- **Coleta**: FPS, tempo de quadro, 1% e 0,1% mínimos e quantidade de travadas (stutters); uso por núcleo da CPU; uso, clock, temperatura e limitação térmica/de energia da GPU; memória de vídeo; RAM, uso de arquivo de paginação e falhas de página; fila do disco; DPC/ISR (já existe).
- **Regras de diagnóstico** (exemplos):
  - GPU ≥ 95% de uso e nenhum núcleo da CPU saturado: **limite da placa de vídeo**.
  - Um núcleo ≥ 90% com a GPU abaixo de 85%: **limite do processador** (jogo dependente de um núcleo).
  - Memória de vídeo ≥ 95% ou queda de FPS junto de uso de RAM compartilhada: **limite de VRAM**.
  - RAM ≥ 90% ou muita paginação: **limite de memória RAM**.
  - Travadas coincidindo com atividade do disco: **armazenamento** (jogo em HD).
  - Temperatura alta com clock caindo: **limitação térmica**.
- **Relatório**: gráfico do tempo, percentual do tempo gasto em cada gargalo, FPS médio e mínimos, latência (DPC) e **recomendações** em português ("sua placa limita: reduza a qualidade de sombras ou a resolução; para 144 fps seria preciso uma GPU X% mais forte"). O usuário define a meta de FPS (60, 144...).
- **Overlay** durante o jogo (já existe o de FPS) com o gargalo atual.
- Limite honesto: o diagnóstico é uma estimativa por regras; quanto mais sessões, melhor a leitura.

## Etapa 5 · Acesso remoto próprio (GG)
Objetivo: o administrador (você) acessa o computador do cliente pelo próprio MB Extreme, com ID numérico que o cliente informa, **sem usar programa de terceiros**.

Decisões e arquitetura proposta:
- **Servidor de conexão** (o mesmo domínio do servidor de licenças): cadastro dos computadores, ID de 9 dígitos, mediação do pedido ("sinalização") e **retransmissão** (relay) dos dados quando a conexão direta não for possível.
- **Fluxo**: cliente clica em *Pedir suporte* e o programa recebe um ID e uma senha de uso único; o técnico (conta de administrador, com verificação em dois passos) digita o ID no console do técnico; o cliente vê "Técnico X quer acessar" e **aceita**; só então a sessão começa.
- **Captura e transmissão da tela**: DXGI + codificação por placa de vídeo (H.264), enviada por WebSocket com TLS pelo servidor (primeira versão). Depois, conexão direta (STUN/TURN) para reduzir custo e latência.
- **Controle remoto**: mouse e teclado injetados no computador do cliente; para operar janelas de administrador (UAC) e a tela de login é preciso um **serviço do Windows** assinado (componente separado do executável principal).
- **Criptografia ponta a ponta**: chave da sessão acordada entre os dois lados e assinada pelo servidor; o servidor de retransmissão não consegue ler a tela.
- **Segurança e confiança**: só o cliente inicia; aceite a cada sessão; faixa visível "Sessão remota ativa" com botão Encerrar; registro de auditoria (quem, quando, quanto tempo); acesso sem supervisão desligado por padrão; limite de tentativas.
- **Antivírus**: ferramentas de acesso remoto costumam ser marcadas como suspeitas. Por isso o componente remoto é um programa **separado, assinado**, baixado só quando o cliente pede suporte, nunca embutido no executável principal.
- **Bibliotecas possíveis** (para não começar do zero): uma pilha WebRTC para .NET (como SIPSorcery) e um servidor TURN aberto (coturn); verificar as licenças antes.
- Custo contínuo: servidor, banda do relay e manutenção; é responsabilidade de segurança sua. Em troca: independência e marca própria.

Sub-etapas:
- **5.1** Servidor: contas de técnico, IDs, sinalização, relay, auditoria.
- **5.2** Cliente: *Pedir suporte*, ID e senha, aceite, faixa de sessão ativa.
- **5.3** Console do técnico: lista de clientes disponíveis, conectar pelo ID, visualizar a tela.
- **5.4** Controle (mouse e teclado), teclado especial (Ctrl+Alt+Del) e área de transferência.
- **5.5** Serviço do Windows (UAC e tela de login), assinatura de código.
- **5.6** Conexão direta (STUN/TURN), qualidade adaptativa, transferência de arquivos e chat.
- **5.7** Teste de segurança externo antes de abrir ao público.

## Etapa 6 · Licença, teste e cobrança (G)
(Já combinado nas conversas anteriores.) Teste de 7 dias definido no servidor; planos mensal, trimestral, semestral e anual com descontos; quantidade de PCs com desconto; Stripe Checkout e Portal; chaves de ativação para vendas fora do Stripe; site (feito à parte) integrado à API.

## Etapa 7 · Qualidade e lançamento (M)
Assinatura de código do instalador e do executável, ofuscação, testes de desempenho (o estúdio e o monitor de gargalos não podem pesar), revisão independente, política de privacidade e termos de uso.

## Etapa 8 · Catálogo expandido a partir do estudo do Lobo Alpha (M)
Base: `ANALISE-LOBO-ALPHA.md` (pacote lido por inteiro, nada executado). Entram só os itens marcados ADICIONAR e OPCIONAL, todos com backup do valor original, desfazer e aviso.
1. **Armazenamento**: hibernação, TRIM, último acesso do NTFS, defrag só em HD, nomes 8.3 (opcional), limpar logs (opcional).
2. **Jogos e GPU**: limpar cache de shaders (todas as marcas), MPO, ULPS/telemetria AMD, telemetria/ShadowPlay NVIDIA, Game DVR em tela cheia, `SystemResponsiveness`. Itens específicos de marca só aparecem se a GPU for daquela marca.
3. **Rede**: RSS/RSC, TCP Nodelay/AckFrequency, energia do adaptador, limpar DNS/renovar IP. MTU e auto-tuning só por teste.
4. **Periféricos**: resposta do teclado (com valores corretos). Fila de dados de mouse/teclado só depois de medir latência de entrada.
5. **Navegadores** (página nova): políticas do Chrome, Brave, Opera e Edge, limpar cache, com reverter.
6. **Reparo do Windows**: novas tarefas (antitrapaça, Bluetooth, áudio, Wi-Fi/modo avião, tela preta, desligamento, Xbox/Game Pass).
7. **Rotina semanal** opcional (limpeza + SFC/DISM) por tarefa agendada.
8. **Por jogo** (junto da Etapa 3): prioridade de CPU por executável (IFEO `PerfOptions`), criada e removida pelo app.
9. **Fora do produto** (registrado): ativação de Windows/Office, desligar UAC, desligar Windows Update, MTU fixo, executáveis de terceiros.

## Etapa 9 · Assistente de ajuste de hardware: testar primeiro, ajustar depois (GG)
Pedido de 22/10. Regra central: **nada é alterado antes de um teste**; o app mede, diagnostica, propõe 2 ou 3 configurações e só então deixa o usuário aplicar, com aviso forte de risco e como voltar atrás. Público: quem já faz overclock e undervolt.

**9.1 Diagnóstico completo (base já existe)** — orquestra o que o app tem (estresse de CPU, por núcleo e memória; benchmark de CPU, memória, disco e GPU 3D; sensores) numa sequência guiada, gravando temperatura, clock, potência, tensão, limitação térmica/de energia e erros ao longo do teste. Falta: teste de estresse da **GPU** e do **SSD** com telemetria, o registro unificado e o relatório (margem térmica, margem de potência, núcleos instáveis, gargalos).
**9.2 BIOS** — versão e data atuais (já lê), comparação com a **versão mais nova**. Não existe API pública das fabricantes; a consulta automática será feita fabricante por fabricante (a pesquisar e conferir) e, onde não der, o app abre a página oficial do modelo exato e explica como conferir. O app nunca atualiza a BIOS sozinho.
**9.3 Perfis sugeridos** — a partir das medições: **Frio e silencioso** (undervolt: processador quente, cair a temperatura sem perder desempenho), **Equilibrado** e **Desempenho** (overclock leve quando há sobra de potência e de temperatura). Cada perfil vem com passo a passo para o hardware detectado (a Receita de BIOS atual vira a base: Curve Optimizer/PBO na AMD, Vcore e loadline na Intel, XMP/EXPO, limites de potência) e **teste de confirmação depois** (antes e depois).
**9.4 Aplicação direta pelo app (o que dá para fazer do Windows)** — curvas de ventoinha (já existe), limites de potência/clock (NVIDIA, já existe), e **ajuste de clock da placa de vídeo estilo MSI Afterburner**: NVIDIA pela NVAPI (offset de clock do núcleo e da memória, limite de potência) e AMD pela ADLX, com aviso obrigatório, limites conservadores, **reversão automática** se não houver confirmação em N segundos, nada gravado para depois do reinício, e só liberado depois de um teste de estresse bem-sucedido. Curva de tensão/frequência completa só depois, e só onde a API for pública.
**9.5 Processador** — ajustes do Windows (plano de energia, limites) já feitos; **undervolt/overclock pelo app** depende de driver assinado (PawnIO, que o app já usa para sensores) e do módulo certo por família de CPU (Ryzen: limites PPT/TDC/EDC e Curve Optimizer; Intel: em muitos modelos a tensão está bloqueada por BIOS/microcódigo). Será feito família por família, começando pela que o seu PC usar, e com teste em hardware real. Onde não der, fica o passo a passo da BIOS.
**9.6 Controlador de RGB próprio** — sem depender do OpenRGB. Arquitetura de "motor de efeitos" com um driver por família de dispositivo (HID USB, SMBus/I²C para memória e placa-mãe, SDKs oficiais quando existirem). Cada driver só entra com **protocolo documentado e testado em hardware real**; sem o seu hardware na mão não dá para garantir. Começa pelos dispositivos que o seu PC tem (precisa da lista) e cresce depois. Inclui efeitos próprios (cor por temperatura já existe), perfis e sincronização com as ventoinhas.

**Ordem sugerida:** 9.1 e 9.3 (o assistente e os perfis, só leitura e orientação: seguro e já útil) → 9.2 → 9.4 (GPU) → 9.6 (RGB, conforme o hardware) → 9.5 (CPU, família por família).

## Decisões tomadas (22/10)
- **AnyDesk**: sai da tela principal e fica como opção secundária.
- **Discord**: item futuro, voltado à comunidade (câmera virtual).
- **Jogos**: começar com perfis por faixa de hardware; buscar dados de testes da comunidade por API/site e **aprender com o que roda nas máquinas** dos próprios clientes (ver abaixo).
- **Execução**: nada é aplicado ainda; primeiro o plano é completado.

## Dados de jogos: comunidade e aprendizado local (acréscimo à Etapa 3)
1. **Metadados e requisitos**: fontes públicas candidatas (a verificar termos de uso e limites): Steam (requisitos e dados da loja), PCGamingWiki (ajustes e notas por jogo, licença aberta com atribuição), IGDB (metadados). Sites de benchmark sem API pública ou que proíbem coleta automática não entram.
2. **Aprendizado dentro do MB Extreme (com permissão do cliente)**: o monitor de gargalos (Etapa 4) grava, para cada jogo e configuração, o hardware (faixa, sem identificar a pessoa), as opções gráficas usadas e o resultado (FPS médio, mínimos, gargalo). Esses dados voltam ao servidor, onde viram recomendações do tipo "máquinas parecidas com a sua rodam este jogo a 90 fps com estas opções".
3. **Aprendizado local**: o programa guarda as últimas sessões do jogo no próprio PC e sugere a configuração que deu o melhor resultado ali.
4. **Privacidade**: envio opcional e desligado por padrão, dados anônimos e agregados, texto claro na política de privacidade (LGPD).

## Ordem sugerida
1. Etapa 1 (rápida, melhora o que você vê agora).
2. Etapa 2 (estúdio) começando pela estrutura nova e pela pré-visualização.
3. Etapas 3 e 4 juntas (biblioteca de jogos e monitor de gargalos compartilham a medição de FPS e o hardware).
4. Etapa 5 em paralelo com a 6, porque as duas dependem do servidor no ar.
5. Etapa 8 pode andar em paralelo com 2 a 4 (é trabalho de catálogo, independente da interface nova); o item 8 dela anda junto com a Etapa 3.
6. Etapa 7 antes de abrir para clientes.

## Decisões que preciso de você
1. **Acesso remoto**: começar com conexão pelo servidor (mais simples, gasta banda) e deixar a direta para depois? E onde o servidor vai ficar hospedado?
2. **AnyDesk**: removo já da tela (Etapa 1) e deixo só o *Pedir suporte* até o módulo próprio existir?
3. **Discord**: aceita que fique como item futuro (câmera virtual)?
4. **Jogos**: começamos com perfis por faixa de hardware (rápido) e vamos montando o banco de jogos específicos depois?
