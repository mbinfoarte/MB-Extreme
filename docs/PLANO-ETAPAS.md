# Plano de etapas (próximas melhorias)

Organização do pedido de 22/10 em etapas, na ordem sugerida de execução. Tamanhos: **P** (horas a 1 dia), **M** (alguns dias), **G** (semanas), **GG** (muitas semanas).

## Etapa 1 · Painel mais limpo (P)
1. Remover do painel os cartões **Uso da CPU** e **Limpeza do sistema** (a limpeza continua no menu Otimização e no botão do topo).
2. Manter os medidores redondos e **acoplar abaixo de cada um a linha animada** do histórico, como a do cartão removido:
   - CPU: medidor de temperatura + linha de uso (%).
   - Placa de vídeo: medidor de temperatura + linha de uso (%).
   - Memória: medidor de uso + linha do histórico.
   - Internet: medidor de ping + linha do ping (e a rede continua no cartão grande de download/envio).
3. Botão da limpeza automática da memória: **só o ícone de vassoura**, sem texto (tooltip explica). Corrige também o defeito visto na captura: o texto "Auto" aparecia como quadradinhos porque a fonte de ícones era aplicada ao texto todo.
4. Suporte: tirar a abertura do AnyDesk da tela (ver Etapa 4). Até o acesso remoto próprio existir, a página fica só com **Pedir suporte** (texto pronto com nome, versão e relatório do sistema para enviar ao suporte).

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

## Ordem sugerida
1. Etapa 1 (rápida, melhora o que você vê agora).
2. Etapa 2 (estúdio) começando pela estrutura nova e pela pré-visualização.
3. Etapas 3 e 4 juntas (biblioteca de jogos e monitor de gargalos compartilham a medição de FPS e o hardware).
4. Etapa 5 em paralelo com a 6, porque as duas dependem do servidor no ar.
5. Etapa 7 antes de abrir para clientes.

## Decisões que preciso de você
1. **Acesso remoto**: começar com conexão pelo servidor (mais simples, gasta banda) e deixar a direta para depois? E onde o servidor vai ficar hospedado?
2. **AnyDesk**: removo já da tela (Etapa 1) e deixo só o *Pedir suporte* até o módulo próprio existir?
3. **Discord**: aceita que fique como item futuro (câmera virtual)?
4. **Jogos**: começamos com perfis por faixa de hardware (rápido) e vamos montando o banco de jogos específicos depois?
