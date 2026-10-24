# Gravação e transmissão (estúdio)

Módulo próprio no menu lateral (antes ficava dentro de *Jogos*). Uma única captura e uma única codificação na placa de vídeo alimentam até três saídas ao mesmo tempo: gravar, transmitir e replay instantâneo.

## Tela

- Pré-visualização à esquerda, com os botões de **o que capturar** logo abaixo (tela inteira, janela, qual monitor). À direita, os três blocos com ícone: Replay instantâneo, Gravar, Transmitir ao vivo.
- Abas: Vídeo, Áudio, Câmera, Transmissão, Replay, Atalhos, Avançado. As opções ficam em linhas "nome + botões" para gastar pouca altura.

## Vídeo e codificador

- Resolução, quadros, taxa, formato (MKV/MP4).
- Codec por placa: H.264, H.265 e AV1 em NVENC, AMF ou Quick Sync, **só os que a placa realmente aceita** (cada um é testado). Os que faltam aparecem numa nota ("Sem suporte nesta placa...").
- Esforço do codificador: Rápido, Equilibrado, Qualidade (NVENC `p2/p4/p6`, AMF `speed/balanced/quality`, Quick Sync `veryfast/medium/slower`).

## Áudio

Som do PC e microfone lado a lado, cada um com volume e dispositivo. Qualidade do áudio AAC de 96 a 320 kbps.

## Câmera

Webcam por cima da tela (gravação, replay e transmissão), em um dos quatro cantos, 15 a 30% da largura, com moldura amarela opcional. A câmera é lida pelo DirectShow (`ffmpeg -f dshow`); câmeras virtuais (OBS, celular) aparecem na lista. A pré-visualização mostra um retângulo na posição escolhida (não mostra a imagem real da câmera).

## Transmissão

Ao escolher a plataforma, a tela mostra o padrão dela e o programa **aplica** esse padrão ao transmitir:

| Plataforma | Máx. resolução / fps | Máx. taxa de vídeo | Áudio (recomendado / máx.) |
|---|---|---|---|
| Twitch | 1080p / 60 | 6 Mbps | 160 / 160 kbps |
| YouTube | 2160p / 60 | 51 Mbps | 128 / 384 kbps |
| Facebook | 1080p / 30 | 6 Mbps | 128 / 256 kbps |
| Outro servidor (RTMP) | 2160p / 60 | 20 Mbps | 160 / 320 kbps |

A faixa de taxa por resolução (ex.: 1080p60 = 6 a 9 Mbps) segue a tabela de codificação do YouTube. **Estes números são de referência e as plataformas mudam as regras**: confira no painel da plataforma. Eles ficam em `Core/Recording/StreamSpecs.cs`.

- **Limite de banda**: padrão da plataforma, ou 3 a 20 Mbps. Nunca passa do máximo da plataforma.
- **Medir meu envio**: teste de 6 s de upload (servidor público da Cloudflare) e sugestão de taxa = 70% do envio menos o áudio.
- Mostra o uso da internet em tempo real (envio e recebimento, soma das placas de rede ativas) e, ao vivo, quanto a transmissão consome.
- Se a resolução ou os quadros passam do que a plataforma aceita, a transmissão reduz (e avisa). A gravação e o replay usam o mesmo vídeo, porque há uma codificação só.
- **Chat**: Twitch (nome do canal) e YouTube (endereço ou código da live) abrem numa janela própria do Edge (`--app`), sem abas. Facebook e outros servidores não têm chat aberto por aqui. O chat não fica embutido na tela do programa (isso exigiria o componente WebView2).

## Atalhos globais

Ctrl+Shift ou Ctrl+Alt com F5 a F12 (F1 a F4 ficam de fora por causa de Ajuda e Alt+F4). Padrão: salvar replay `Ctrl+Shift+F9`, gravar `Ctrl+Shift+F10`, transmitir `Ctrl+Shift+F11`. Combinações repetidas ou já usadas por outro programa são recusadas com aviso. Gravar e transmitir pelo atalho reaproveitam a origem, o codificador e o destino da última vez iniciada pela tela; se o estúdio está ligado, o atalho para.

## Mudar com o estúdio ligado

A tela não trava mais durante a transmissão. Tudo continua editável (câmeras, janela, saídas, áudio, Pix, qualidade). O que muda na hora e o que precisa reiniciar:

| Muda na hora (sem reiniciar) | Precisa reiniciar a captura |
|---|---|
| Mutar ou ligar o microfone e o som do PC (botões no painel "Ao vivo") | Ligar/desligar ou trocar câmera, trocar de janela ou de tela |
| Lances automáticos (liga, sensibilidade, tempo) e aviso na tela | Ligar/desligar Gravar, Transmitir ou Replay |
| Atalhos | Qualidade, codec, taxa, resolução, QR do Pix, dispositivos de áudio |

Quando algo que precisa reiniciar muda, aparece a barra **"Aplicar agora"** com o motivo. Ao aplicar: a transmissão sai do ar por alguns segundos e reconecta; a gravação continua em um arquivo novo (o anterior fica salvo) e o replay recomeça vazio. É assim porque a captura e a codificação são um único processo do FFmpeg: trocar uma entrada exige reiniciá-lo. Um mixer de cenas sem reinício (como o OBS) exigiria reescrever a captura e a composição dentro do programa.

## Várias câmeras

Até 3 câmeras ao mesmo tempo, cada uma com posição, tamanho e moldura próprios. Cada câmera física só pode aparecer uma vez (o DirectShow não a abre duas vezes). No FFmpeg cada câmera vira uma entrada e as imagens são empilhadas com `overlay` encadeado. Dois itens no mesmo canto aparecem em amarelo-escuro na pré-visualização e um cobre o outro.

## Pix (QR na transmissão)

Aba **Pix**: chave (CPF, CNPJ, e-mail, telefone com +55 ou chave aleatória, com conferência dos dígitos), nome, cidade, valor opcional e mensagem. O programa monta o "Pix copia e cola" (BR Code, CRC-16/CCITT-FALSE) e desenha o QR num cartão branco com a legenda. O QR vai por cima do vídeo no canto escolhido: sempre, ou por alguns segundos a cada tantos minutos (`overlay` com `enable`), sem reiniciar nada além do normal.

**O que isto não faz:** o Pix estático não informa ao programa que alguém pagou, quem pagou nem quanto. Não existe alerta automático na tela ("fulano doou R$ 10"). Para isso, como nos serviços de "live pix", é preciso:

1. um provedor de pagamentos (Mercado Pago, Efí, Asaas, Stripe com Pix e Stripe Connect...) que gere uma cobrança com identificador por doação e envie um aviso (webhook) quando for paga;
2. um servidor nosso para receber o webhook e repassar à transmissão (a página de doação do streamer, o aviso na tela e a mensagem lida);
3. contas e cadastro (KYC) do provedor para cada streamer, para o dinheiro cair na conta dele e não na da MB InfoArt.

Isto é um projeto à parte (e tem implicações fiscais e de taxas); o QR estático já entrega o recebimento direto, sem intermediário.

## Lances automáticos pelo som

No Replay: **Salvar os lances automaticamente**. A cada 0,1 s o programa mede o volume do som do PC (sem o microfone) e compara os últimos 2 s com o volume normal dos últimos 60 s. Se passar de 1,5x (alta), 1,9x (média) ou 2,4x (baixa) e de um volume mínimo, depois de 20 s de aquecimento, espera alguns segundos (3, 6 ou 10, para pegar a comemoração) e salva o clipe do replay, com pausa de 45 s entre clipes. Um aviso "Lance salvo" aparece por cima do jogo (janelas e sem bordas; em tela cheia exclusiva o Windows pode esconder). O atalho de salvar replay continua valendo para marcar um lance na mão.

É uma estimativa pelo som: não "vê" o gol. Em jogos de futebol a torcida costuma subir muito no gol, mas pode salvar um momento barulhento que não era gol e perder um lance silencioso. Precisa do Replay e do som do PC ligados.

## Não verificado em hardware real

Câmeras por DirectShow (inclusive várias), a barra "Aplicar agora", a janela de chat, o teste de upload, os atalhos globais, o QR do Pix na transmissão, o detector de lances e o aviso por cima do jogo: testados só por testes automáticos de comando e regras, não em um PC com placa de vídeo. A sintaxe do filtro de várias camadas do FFmpeg e o aviso em tela cheia são os pontos que mais merecem teste real.
