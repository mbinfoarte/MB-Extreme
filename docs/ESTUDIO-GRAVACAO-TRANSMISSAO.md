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

## Estatísticas da transmissão

Aba **Estatísticas** e cartão no alto da tela com a live no ar: espectadores agora, visualizações, curtidas, comentários, pico e média da sessão, gráfico dos espectadores, título e categoria. Atualiza a cada 15 s.

| Plataforma | Fonte | O que traz | O que o usuário precisa |
|---|---|---|---|
| YouTube | YouTube Data API v3 (`videos.list`: snippet, statistics, liveStreamingDetails) | espectadores simultâneos, visualizações, curtidas, comentários, título, categoria, início | chave de API do próprio projeto no Google Cloud (grátis; 1 unidade por consulta, cota de 10.000/dia) e o código da live |
| Twitch | Helix `GET /streams` com token de aplicativo (client credentials) | espectadores, título, jogo, início | Client ID e segredo de um aplicativo próprio (dev.twitch.tv/console/apps) e o nome do canal |
| Facebook / outros | nenhuma | — | a API só libera para aplicativos aprovados; o botão abre o painel da plataforma |

As chaves ficam protegidas pelo Windows (DPAPI), só para o usuário, e nunca em log. Quando a cota ou a chave falham, a tela explica o motivo.

**Configurações da live (título, categoria, privacidade, latência, atraso, DVR):** o programa mostra título e categoria atuais, mas **não os altera**. Mudar isso pela API exige que o dono entre com a conta do Google ou da Twitch (OAuth) em um aplicativo aprovado pela plataforma (o escopo do YouTube que edita transmissões é "sensível" e passa por verificação do Google, que leva semanas). Por isso o botão **Abrir o painel da plataforma** leva direto ao YouTube Studio da live, ao Stream Manager da Twitch ou ao Produtor do Facebook. Fazer isso de dentro do programa é um projeto à parte (aplicativo OAuth verificado, e idealmente o servidor do MB Extreme guardando as credenciais do aplicativo).

## Lances automáticos pelo som

No Replay: **Salvar os lances automaticamente**. A cada 0,1 s o programa mede o volume do som do PC (sem o microfone) e compara os últimos 2 s com o volume normal dos últimos 60 s. Se passar de 1,5x (alta), 1,9x (média) ou 2,4x (baixa) e de um volume mínimo, depois de 20 s de aquecimento, espera alguns segundos (3, 6 ou 10, para pegar a comemoração) e salva o clipe do replay, com pausa de 45 s entre clipes. Um aviso "Lance salvo" aparece por cima do jogo (janelas e sem bordas; em tela cheia exclusiva o Windows pode esconder). O atalho de salvar replay continua valendo para marcar um lance na mão.

É uma estimativa pelo som: não "vê" o gol. Em jogos de futebol a torcida costuma subir muito no gol, mas pode salvar um momento barulhento que não era gol e perder um lance silencioso. Precisa do Replay e do som do PC ligados.

## Não verificado em hardware real

Câmeras por DirectShow (inclusive várias), a barra "Aplicar agora", a janela de chat, o teste de upload, os atalhos globais, o QR do Pix na transmissão, o detector de lances e o aviso por cima do jogo: testados só por testes automáticos de comando e regras, não em um PC com placa de vídeo. A sintaxe do filtro de várias camadas do FFmpeg e o aviso em tela cheia são os pontos que mais merecem teste real.

## Estúdio profissional (fase 1)

- **Mixer de áudio**: som do PC e microfone com medidor de nível, volume e mudo; abaixo, uma linha por aplicativo que está tocando (jogo, Discord, Spotify, navegador são reconhecidos pelo nome). O volume por aplicativo usa as sessões de áudio do Windows, ou seja, muda também o que você ouve. Volume independente só para a transmissão exige captura por processo (Windows 10 build 20348 ou mais novo) e ainda não foi feito.
- **Imagem**: brilho, contraste, saturação, gama, nitidez, redução de ruído e espelho, com predefinições (Original, Natural, Vívida, Cinema, Nítida). Cada câmera tem a sua própria qualidade de imagem.
- **Canal**: nome, apresentador, frase e logo; gera três camadas (entrada do canal, faixa com o nome que desliza de tempos em tempos e logo no canto) com temas de cores e pré-visualização no próprio app.
- **Pix com QR do seu banco**: envie a imagem do QR e ela aparece dentro de uma moldura organizada com o nome do canal. O Pix é estático: o app não recebe aviso de pagamento.
- **Chat e perguntas**: Twitch (anônimo, sem login) e YouTube (chave de API e vídeo ao vivo); mensagens pagas (bits, Super Chat) em destaque, filtros (tudo, só pagas, perguntas e pagas) e uma lista de perguntas para marcar como respondida. Pode conectar sozinho ao iniciar a transmissão.

### Ainda não feito (roadmap, a partir do que as comunidades do OBS/Twitch pedem)

1. Cenas e Modo Estúdio (prévia do que vai ao ar antes de trocar), com prévia composta real do que está no ar.
2. Overlays dinâmicos (alertas de seguidor/doação) e botão "mostrar agora".
3. Volume independente por aplicativo na transmissão (captura por processo do Windows).
4. Desfoque de fundo da webcam (precisa de modelo de segmentação), supressão de ruído do microfone, chroma key e câmera virtual.
5. Alterar título e categoria ao vivo (exige aplicativo OAuth no YouTube/Twitch).
6. Aviso de Pix em tempo real (exige provedor de pagamento, servidor e cadastro de cada streamer).

## Vídeo vertical e legendas ao vivo (Shorts, Reels, TikTok)

Aba **Vertical** do estúdio. Vale para gravar, transmitir e salvar o replay: tudo é montado em tempo real, num único FFmpeg, sem edição depois.

- **Formatos (altos)**: 1080×1920 (9:16, padrão das redes), 1440×2560 (2K), 2160×3840 (4K, só para gravar), 720×1280 (econômico), 1080×1350 (4:5, feed do Instagram) e 1080×1080. A resolução da aba Vídeo não vale no modo vertical. Na transmissão, o limite da plataforma é aplicado à **largura** (ex.: Twitch 1080) e o quadro é reduzido mantendo a proporção.
- **Tela dividida**: uma parte em cima e outra embaixo (25% a 75% da altura), com linha amarela opcional. Cada parte pode ser a **tela/janela**, uma **câmera** ou um **arquivo** (imagem, GIF ou vídeo que repete sem parar, como um gameplay de fundo). Duas partes da mesma tela também funcionam (ex.: jogo inteiro em cima e um recorte aproximado, como o minimapa, embaixo).
- **Enquadramento**: "preencher" (corta as bordas, escolhendo lado, altura e aproximação) ou "inteira com fundo desfocado" (jogo em 16:9 no meio, borrado atrás, como nos Shorts).
- **Tudo o que já existia continua valendo** por cima do quadro vertical: filtros de imagem, câmeras pequenas nos cantos, Pix, entrada/faixa/logo do canal, áudio, replay e clipes automáticos.
- **Legendas automáticas**: reconhecimento de voz em português **offline** (Vosk, modelo pequeno de cerca de 32 MB baixado uma vez). A fonte pode ser o microfone ou o som do PC. O texto é escrito em dois arquivos a cada 100 ms e o FFmpeg os relê a cada quadro (`drawtext` com `reload=1`), então a legenda sai gravada no vídeo, no estilo Shorts (letras grandes com contorno, maiúsculas opcionais, cor, tamanho e posição: embaixo, meio, em cima ou na divisória). A legenda some depois de 3 s de silêncio. Também funciona no vídeo horizontal.

### Limites e cuidados

- O modelo pequeno erra mais que serviços na nuvem, principalmente com ruído, gírias e nomes de jogos. Um modelo maior pode ser adicionado depois.
- Palavras ainda não confirmadas podem mudar na tela por uma fração de segundo (é o texto parcial do reconhecedor).
- A câmera usada numa parte do vídeo vertical não pode ser usada também como câmera pequena no canto (o DirectShow não abre a mesma câmera duas vezes).
- Arquivos de fundo em vídeo usam `-re` (tempo real); vídeos muito pesados (4K) gastam processador.
- Ainda sem teste em PC real: câmeras DirectShow, gráfico de filtros com `drawtext` de arquivo recarregado, biblioteca nativa do Vosk empacotada ao lado do executável e microfone WASAPI alimentando o reconhecedor.

### Próximos passos sugeridos

1. Animações das legendas (palavra atual em destaque, estilo karaokê) com legendas ASS.
2. Rastreamento do rosto/ação para o corte vertical seguir o jogador.
3. Cenas verticais salvas (troca com atalho) e modelo de voz maior opcional.
4. Exportar a legenda também em arquivo SRT junto da gravação.

## Voz e imagem inspiradas no NVIDIA App / Broadcast

Feito sem placa RTX e sem inteligência artificial:

- **Voz do microfone** (aba Áudio, muda ao vivo): *Reduzir ruído* (filtro de graves, porta de ruído que abaixa o som entre as falas) e *Voz de estúdio* (também realça presença e corpo da voz, compressor e limitador).
- **Vinheta** (escurece os cantos) e predefinições **Luz de estúdio** e **Retrato** na aba Imagem e em cada câmera.
- **Câmera virtual do NVIDIA Broadcast**: aparece na lista de câmeras e traz fundo virtual, enquadramento automático e contato visual sem esforço do nosso app.

Não feito (exige IA/RTX): fundo virtual próprio, enquadramento automático, contato visual, super resolução, geração de quadros, remoção de eco da sala. Ideias futuras: teste do microfone com medidor e gravação curta, luz-chave com gradiente (máscara radial), zoom e posição manuais por câmera.

## Cópia vertical automática e vídeos curtos

### Cópia vertical ao gravar (aba Vertical)
Marque **"Gravar também uma cópia vertical"**: enquanto grava o vídeo normal (horizontal), o mesmo FFmpeg recorta e monta, em tempo real, um segundo arquivo `gravacao-…-vertical` em 9:16 (ou o formato escolhido), com o mesmo áudio. O enquadramento é o da aba (tela inteira com fundo desfocado, preencher cortando, ou dividida com recorte aproximado, como a câmera do canto). Se a câmera usada embaixo é a mesma câmera pequena do canto, ela é aberta uma vez só e dividida entre as duas saídas. Cuidados: usa dois codificadores da placa ao mesmo tempo (placas antigas podem perder quadros), só vale com a gravação ligada, e o replay e a transmissão continuam no vídeo normal. O recorte é **fixo** (não segue o mouse nem o jogador).

### Vídeos curtos (página "Vídeos curtos", dentro de Gravação e transmissão)
Escolha uma gravação longa (ou uma das recentes) e gere vários vídeos verticais em MP4:
- **Cortes na mão** (início e fim, de 3 a 180 s), **divididos por tempo** (30, 45, 60 ou 90 s) ou **sugeridos pelos momentos mais fortes**: o app mede o volume do áudio a cada segundo, compara com o volume normal do vídeo e escolhe os picos (gritos, comemorações, lances), com 10 s antes e 15 s depois, sem sobreposição.
- **Montagem**: inteira com fundo desfocado, preencher cortando ou dividida (vídeo em cima e um canto aproximado embaixo, útil para câmera do apresentador ou minimapa).
- Tamanho (de 720×1280 a 2160×3840), 30 ou 60 quadros e taxa; usa o codificador da placa (ou o processador se não houver). Os arquivos saem em `Vídeos\MB Extreme\Curtos\<nome do vídeo>`.

Limites: sem legenda nos cortes ainda, sem rastreio de rosto, a sugestão por som só enxerga volume (não entende o conteúdo), e nada disso foi testado em PC real.

### O que a pesquisa mostrou (e ideias para as próximas versões)
- O plugin **Aitum Vertical** do OBS adiciona uma tela vertical paralela à horizontal, com cenas separadas e gravação/transmissão simultâneas; relatos dizem que dois codificadores dobram o trabalho da placa ([guia](https://obs-versions.com/blog/obs-vertical-streaming-guide), [fórum do OBS](https://obsproject.com/forum/threads/aitum-vertical.166504/page-11)). Nossa cópia vertical faz o equivalente dentro do mesmo FFmpeg.
- Ferramentas como o **OpusClip** transformam vídeo longo em curtos com enquadramento automático que segue quem fala, legendas animadas e nota de "potencial viral" ([resumo](https://www.castmagic.io/software-reviews/opus-clip)). Próximos passos viáveis: legenda automática nos cortes (Vosk no áudio do arquivo e queimar no vídeo), legendas animadas (palavra em destaque, arquivo ASS), rastreio de rosto para o recorte seguir o apresentador e uma nota por corte (volume + densidade de fala).
- O recorte que **segue o mouse ou o jogo** ao vivo é possível enviando comandos ao filtro `crop` por ZeroMQ (`zmq`) ou `sendcmd`; as discussões do FFmpeg indicam enviar posições absolutas de x/y a cada quadro, e exige um FFmpeg com `libzmq` ([discussão](https://ffmpeg.org/pipermail/ffmpeg-user/2018-June/040076.html), [limitações do crop](https://trac.ffmpeg.org/ticket/11438)). Fica como fase futura.
- Outras ideias para o app: exportar legenda em SRT junto do vídeo, título e hashtags sugeridos para cada corte, miniatura (capa) automática, publicar direto no YouTube/Instagram/TikTok (exige apps OAuth de cada rede), modelo de voz maior opcional e cenas verticais salvas com atalho.
