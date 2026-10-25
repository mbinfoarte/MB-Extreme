# Pesquisa: estúdio estilo OBS (modo estúdio, áudio, peso da live)

Pesquisa de 25/10/2026. A busca na web trouxe pouco material oficial; o que segue separa o que foi **visto nas fontes** do que é **conhecimento do projeto OBS** (não conferido nesta sessão) e do que é **decisão nossa**.

## 1. Modo estúdio (visto em fórum do OBS)
- Existem dois painéis: **Pré-visualização** (à esquerda: a cena que você prepara) e **Programa** (à direita: o que quem assiste vê). Uma **transição** leva a pré-visualização para o programa. Fonte: [fórum OBS](https://obsproject.com/forum/threads/how-do-you-stress-your-cpu-the-least-in-preview-mode.52298).
- Um usuário relata que **a janela de pré-visualização consumia CPU**; fechá-la derrubava o uso. Lição: a prévia dentro do app deve ser leve (imagem pequena, poucos quadros por segundo), como já fazemos.

## 2. Por que o MB Extreme ainda não tem "modo estúdio" de verdade
O OBS compõe a cena **dentro do próprio programa** e só envia o resultado ao codificador; por isso troca de cena sem cortar a live. O MB Extreme hoje monta a cena **dentro de um único processo FFmpeg** (captura + câmeras + filtros + codificação). Mudar a cena exige reiniciar a captura (a live reconecta por alguns segundos).

Caminhos possíveis:
1. **Cenas salvas + "Transmitir agora"** (curto prazo, já compatível): você monta a cena na pré-visualização (janela, câmeras, posição, filtros) e, ao clicar, o app aplica e reinicia a captura. Não é transição suave, mas é o mesmo fluxo de trabalho: preparar sem afetar a live, depois enviar.
2. **Compositor próprio** (grande): o app captura, desenha a cena (WPF/Direct3D) e entrega quadros prontos ao FFmpeg por pipe. Aí a troca é instantânea e o modo estúdio é real. Custo: reescrever a captura e a composição, risco de pesar mais; exige testes longos em PCs reais.

Recomendação: fazer o caminho 1 e só abrir o caminho 2 depois de medir o peso real no seu PC.

## 3. Peso da live (fóruns do OBS)
- No OBS 28.1 as opções do NVENC passaram a ser **Preset (P1–P7), Tuning e Multipass**; presets altos e multipass gastam mais GPU. Fonte: [GamingOnLinux sobre o OBS 28.1](https://www.gamingonlinux.com/2022/11/video-recording-app-obs-studio-v281-out-with-nvenc-changes/). Por isso o "Modo leve" usa esforço **Rápido** (equivale a preset baixo, sem multipass).
- Relatos de "encoder sobrecarregado" melhoraram com: encoder NVENC mais simples, resolução menor, preset menor, drivers atualizados e jogo em tela cheia em vez de janela sem borda. Fontes: [fórum OBS 1](https://obsproject.com/forum/threads/obs-23-0-0-encoder-overloaded.100997/post-395005), [fórum OBS 2](https://obsproject.com/forum/threads/how-can-my-encoding-overload.110441/). São relatos individuais, não medições.
- Resolução de tela muito alta (DSR/escala) estoura a GPU, que divide o trabalho entre desenhar o jogo e codificar. Regra do MB Extreme: nunca transmitir acima do que a plataforma aceita.

## 4. Áudio (valores do projeto OBS, de memória; confira na documentação)
Padrões dos filtros do OBS, para comparação: porta de ruído fecha em −32 dB e abre em −26 dB; compressor 10:1 com limiar −18 dB; limitador em −6 dB. No MB Extreme os padrões são mais suaves (compressor 3:1, limitador em −1 dB, porta abrindo em −45 dB), porque o objetivo é soar natural sem ajuste manual. Fórum: usuários combinam supressão de ruído + compressor + limitador no microfone, ajustando cada um ([exemplo](https://forums.evga.com/FindPost/3257723/?go=last)).

## 5. O que o MB Extreme já tem a partir desta pesquisa
- Limitador de volume em dB (teto ajustável), porta de ruído, compressor com ganho e filtro de graves, tudo ao vivo; atalhos "Padrão", "Voz limpa (live)" e "Podcast".
- Indicador **REC / AO VIVO** piscando só na pré-visualização do programa (não vai para o vídeo).
- Faixa com nome do apresentador e do canal (aba Canal), "Modo leve" e "Otimizar a live".

## 6. Ainda não pesquisado/implementado
- Supressão de ruído por inteligência artificial (RNNoise), que o OBS usa; entra como licença BSD, mas exige cuidado de empacotamento.
- Cenas salvas com "Transmitir agora" (item 1 da seção 2).
- Comparação visual com Streamlabs, Medal e ShadowPlay: a busca não trouxe material utilizável.
