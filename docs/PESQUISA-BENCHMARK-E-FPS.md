# Pesquisa: benchmark e medidor de FPS

Resumo do que foi pesquisado e do que sugiro implementar. Itens marcados com **(confirmar)** não foram verificados em fonte oficial e precisam de conferência antes de virar código ou promessa de venda.

## 1. Ferramentas de benchmark: o que dá para usar dentro do app

O app é comercial. Isso decide quase tudo: a maioria dos benchmarks famosos libera a linha de comando só em licença paga ou proíbe o uso comercial, então **não podem ser embutidos** sem contrato.

| Ferramenta | Mede | Automação (linha de comando) | Licença / risco | Sugestão |
|---|---|---|---|---|
| **Teste próprio do MB Extreme** (já existe: CPU 1 e N núcleos, memória, disco, GPU 3D em Direct3D 11) | Desempenho relativo do seu PC | Total, é nosso | Sem risco; a nota só compara PC com PC pela nossa escala | Base do menu Benchmark; ampliar |
| **3DMark** (Time Spy, Steel Nomad) | GPU/CPU em 3D | Só com licença Professional/Enterprise ([UL](https://support.benchmarks.ul.com/support/solutions/articles/44002145411-windows-run-3dmark-benchmarks-from-the-command-line)) | Uso comercial só na Enterprise | Não embutir. No máximo detectar se o usuário tem e abrir |
| **Unigine** (Superposition, Heaven, Valley) | GPU em 3D | Automação só na versão Pro ([manual](https://benchmark.unigine.com/docs/superposition/Superposition_Benchmark_User_Manual.pdf)) | Edição grátis só para uso pessoal; empresa exige Pro ([EULA](https://benchmark.unigine.com/policies/superposition-eula)) | Não embutir |
| **Cinebench 2024/2026** | CPU (render) | Sim: `g_CinebenchCpuXTest=true` e similares, resultado só no console ([Maxon](https://maxon.net/tech-info-cinebench)) | Redistribuição **(confirmar)** | Detectar se instalado e rodar, lendo o texto; sem distribuir |
| **Geekbench 6** | CPU/GPU compute | Linha de comando só na versão Pro paga ([edições](https://Geekbench.com/editions/)) | Pago | Não embutir |
| **FurMark 2** | Estresse/benchmark de GPU | Sim, é "linha de comando primeiro" (`--benchmark`, log de sensores) ([Geeks3D](https://geeks3d.com/furmark/command-line)) | Redistribuição **(confirmar)** | Opcional: instalar via winget e orquestrar |
| **OCCT** | Estabilidade e estresse | Edição de linha de comando; licença pessoal exclui uso comercial ([ocbase](https://ocbase.com/purchase)) | Risco para produto comercial | Só orientar o usuário |
| **y-cruncher** | CPU + memória (estresse e benchmark) | Sim | Redistribuição livre, mas uso comercial exige falar com o autor ([licença](https://numberworld.org/y-cruncher/license.html)) | Pedir permissão ao autor, ou deixar como opcional externo |
| **7-Zip** (`7z b`) | CPU (compressão), muito conhecido | Sim | LGPL: pode distribuir com o aviso de licença | **Embutir** como teste de CPU comparável |
| **DiskSpd** (Microsoft) | Disco: sequencial, aleatório, IOPS, latência | Sim, é ferramenta de linha de comando ([wiki](https://github.com/Microsoft/diskspd/wiki/Command-line-and-parameters)) | Código aberto; confirmar a licença no repositório **(confirmar)** | **Embutir**, no lugar do nosso teste simples de disco |
| **Blender Benchmark** | Render (Cycles) em CPU/GPU | Sim: `benchmark-launcher-cli` com saída em JSON ([fórum](https://devtalk.blender.org/t/blender-benchmark-launcher-cli-and-documentation/11225)) | GPL, cenas em domínio público (CC0), resultados públicos anônimos ([Open Data](https://opendata.blender.org/about/)) | **Baixar sob demanda** e rodar: dá nota comparável com o banco público do Blender |
| **Phoronix Test Suite** | Centenas de testes | Sim | GPLv3, mas cada perfil pode ter licença própria ([Phoronix](https://www.phoronix.com/vr.php?view=26396)) | Pesado demais; não vale |
| **PresentMon** (Intel) | Tempo de cada quadro de qualquer jogo | Sim, saída em CSV | **MIT** (confirmado no [LICENSE](https://raw.githubusercontent.com/GameTechDev/PresentMon/main/LICENSE.txt)) | **Embutir** como motor do medidor de FPS |

### Arquitetura sugerida para o módulo Benchmark (menu próprio, fora de Jogos)
1. **Testes próprios**: CPU, memória, GPU 3D, mais cenas novas (largura de banda de memória de vídeo, compute puro, preenchimento). Pontuação MB com histórico e "antes e depois".
2. **Motores abertos integrados**: DiskSpd (disco), 7-Zip (CPU), Blender (render, comparável com o mundo), PresentMon (jogos). Cada um aparece como um cartão com o resultado normalizado e exportável.
3. **Ferramentas de terceiros detectadas** (Cinebench, 3DMark, Unigine, FurMark, OCCT, y-cruncher): botão "Abrir/rodar" quando houver linha de comando, e "importar resultado". Nada disso é distribuído por nós.
4. **Teste de estabilidade**: carga combinada (CPU + GPU + memória) com registro de temperatura, clock e potência, e detecção de "throttling".
5. **Relatório**: página HTML/imagem e CSV com a configuração do PC, notas, gráficos e avisos.

## 2. Medidor de FPS: o que os melhores fazem

### Como o app mede hoje
`FpsMonitor` usa ETW com o provedor **DXGI** (evento de apresentação de quadro), sem injetar nada no jogo. Isso já é um diferencial (compatível com antitrapaça). Limites do que existe hoje:
- Só enxerga o que passa pelo DXGI, e mede o instante em que o jogo **apresenta** o quadro, não o que aparece na tela.
- Calcula FPS, tempo médio e "1% pior" numa janela de 5 s; não guarda série de tempos, não tem gráfico, 0,1%, nem detecção de engasgo.
- Não diz se o limite é a placa de vídeo ou o processador.

### O que o PresentMon traz
- Captura por vários provedores do Windows e métricas separadas: tempo entre apresentações (`MsBetweenPresents`) e **tempo entre mudanças na tela** (`MsBetweenDisplayChange`), que é o que o jogador realmente vê; também latência até a tela e flags de tearing ([versão 2.3.1](https://www.kitguru.net/gaming/joao-silva/intel-presentmon-2-3-1-brings-new-gpu-telemetry-features/), [README](https://raw.githubusercontent.com/GameTechDev/PresentMon/main/README.md)).
- Métricas de tempo da GPU (nome varia por versão, como `msGPUActive`); o próprio projeto avisa que ficam menos exatas com agendamento de GPU por hardware (HAGS) ligado.
- A 2.3.1 passou a rodar sem administrador ([notícia](https://www.comss.ru/page.php?id=16702)) **(confirmar o requisito na versão que formos usar)**.
- Os **nomes das colunas do CSV mudaram entre versões**: o leitor precisa aceitar apelidos.

### Como calcular "1% low" e "0,1% low"
Não há um número único: o CapFrameX tem dois métodos, a **média dos x% piores quadros** e o valor pelo qual se fica abaixo por x% do **tempo** ([blog](https://www.capframex.com/blog/post/Explanation%20of%20different%20performance%20metrics)). A NVIDIA (FrameView) usa a média dos 1% mais lentos. Sugestão: usar a **média dos x% piores** (igual à NVIDIA), mostrar o método no relatório e avisar quando a amostra for pequena (0,1% com poucos quadros muda de uma rodada para a outra).

### Gráficos e indicadores que valem a pena
- **Gráfico de tempo de quadro** (linha, últimos 10 s ao vivo e a sessão inteira depois): picos são engasgos, linha grossa é ritmo ruim de quadros ([Guru3D](https://www.guru3d.com/review/nvidia-geforce-rtx-4080-super-review/page-25/)).
- **Curva de percentis** (formato "taco de hóquei" mostra engasgo) ([NVIDIA](https://developer.nvidia.com/content/analysing-stutter-%E2%80%93-mining-more-percentiles-0)).
- **Detecção de engasgo**: comparar cada quadro com a média móvel dos 20 anteriores, não com o quadro anterior ([PCPer](https://pcper.com/2014/12/msi-geforce-gtx-970-4gb-gaming-graphics-card-review/2/)); contar engasgos por minuto.
- **P99 do tempo de quadro** como indicador único de fluidez; a média de FPS sozinha esconde quedas ([Android](https://developer.android.com/games/optimize/framerate)).
- **Limite: GPU ou CPU?** pela razão entre o tempo ocupado da GPU e o tempo do quadro; liga com o nosso `BottleneckAnalyzer`.
- **Linha do tempo com sensores** junto do gráfico (temperatura, clock, potência, uso, VRAM) e marcas de "throttling".
- **Modo de apresentação** do quadro (independente, composto, com tearing) e se o jogo está na placa certa (notebook com dois vídeos).

### Detecção de hardware e configuração (o que causa FPS ruim na prática)
Uma tela "Verificação de configuração" com avisos acionáveis:
- Placa de vídeo usada pelo jogo (integrada x dedicada), memória de vídeo, versão e idade do driver (DXGI/WMI).
- Largura e geração do barramento PCIe da placa (placa rodando em x4/x8 sem querer), pela NVML no caso da NVIDIA ([NVML](https://developer.nvidia.com/management-library-nvml)); AMD pela [ADLX](https://www.gpuopen.com/adlx) **(confirmar licença do SDK)**.
- Motivos de limitação de clock (térmico, potência) pela NVML; para outras marcas, só pelos sensores que já lemos.
- Memória RAM: XMP/EXPO desligado (velocidade configurada x nominal), canal único, pentes diferentes (WMI).
- Resizable BAR, HAGS, Modo de Jogo, plano de energia, taxa de atualização do monitor, VSync/VRR.

## 3. O que sugiro implementar, em ordem
**Fase 1 – menu Benchmark e medidor v2**
1. Módulo **Benchmark** no menu, juntando: Benchmark, Antes e depois, Teste de estresse, Monitor de FPS e uma página nova **"Jogo (FPS)"**. O "Medir FPS" sai da Biblioteca e chama essa página.
2. Motor de quadros v2 no Core (testável sem Windows): lê o CSV do PresentMon (ou nosso ETW ampliado), guarda a série de tempos, calcula média, 1% e 0,1% pelos dois métodos, P99, engasgos e variação; aparar os primeiros segundos de aquecimento.
3. Página de sessão com gráfico de tempo de quadro ao vivo e depois, comparação A x B com sobreposição e exportação em CSV.
4. DiskSpd e 7-Zip no teste de disco e de CPU.
5. Verificação de configuração (hardware e Windows).

**Fase 2**
6. Blender Benchmark sob demanda (nota comparável com o banco público).
7. Relatório HTML/imagem; overlay configurável (mínimo, padrão, completo; mini gráfico).
8. Detecção e orquestração de Cinebench, FurMark, y-cruncher e similares quando instalados.
9. **Ranking opcional e anônimo** ("seu PC está acima de X% dos PCs com a mesma placa"): precisa do servidor e de consentimento (LGPD), mas é um diferencial comercial forte.

## 4. Riscos e cuidados
- Medir FPS de tela cheia exclusiva pode falhar ou mudar de comportamento conforme o jogo; documentar "use janela sem borda".
- O driver de sensores usado por bibliotecas como a LibreHardwareMonitor pode ser bloqueado por Defender, HVCI ou antitrapaça **(confirmar na versão 0.9.6 que usamos)**: o app deve funcionar sem sensores e explicar por quê.
- Avisos de licença de terceiros precisam estar no app (7-Zip LGPL, Vosk, NAudio, TraceEvent, QRCoder, Vortice, FFmpeg).
- Não prometer comparação com resultados de 3DMark/Cinebench: a nossa pontuação é de outra escala.
