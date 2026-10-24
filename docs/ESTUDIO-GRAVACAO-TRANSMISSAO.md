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

## Não verificado em hardware real

Câmera por DirectShow, janela de chat, teste de upload, atalhos globais e o visual da tela novos: testados só por testes automáticos de comando e regras, não em um PC com placa de vídeo.
