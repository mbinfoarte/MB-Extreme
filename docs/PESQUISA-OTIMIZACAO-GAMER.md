# Levantamento: otimização para jogos, latência e hardware fraco

Pesquisa feita em 21/10/2026. Objetivo: separar o que **realmente ajuda** do que é **marketing**, e transformar isso em funções do MB Extreme.

Como ler a coluna **Evidência**:
- **Forte**: vários testes independentes ou documentação oficial concordam.
- **Mista**: ganho depende do jogo/hardware, ou as fontes discordam.
- **Fraca**: só blogs e relatos, sem medição. O app só oferece com "Experimental" e medição antes/depois.
- **Conhecimento técnico**: não confirmado nas buscas desta pesquisa; validar antes de implementar.

Regra do app: nada de "otimização milagrosa". Toda mudança tem risco, é reversível e, quando possível, o app **mede antes e depois** (benchmark e FPS/frametime), em vez de prometer percentual.

## 1. O que a pesquisa concluiu

### 1.1 Latência e fluidez
| Item | Evidência | Risco | No app hoje | Proposta |
|---|---|---|---|---|
| Plano de energia Alto/Ultimate | Forte (consenso) | Baixo (gasta bateria/calor) | Tem (Ultimate) | Manter; em notebook, só ligado na tomada |
| Modo de Jogo | Mista (um guia manda desligar) | Baixo | Tem (liga/desliga) | Manter, com explicação |
| HAGS (agendamento de GPU) | Mista; exigido por DLSS Frame Generation e recursos de baixa latência em RTX 50/RX 9000 | Médio | Tem (liga/desliga + ajuste "desligar") | Mostrar se a GPU suporta e avisar da dependência de DLSS FG |
| V-Sync / limite de FPS / VRR | Forte (config do jogo/driver) | Nenhum | Não | Guia dentro do app (não dá para aplicar por fora) |
| NVIDIA Reflex / AMD Anti-Lag 2 | Forte, mas é opção do jogo | Nenhum | Não | Detectar suporte da GPU/driver e orientar |
| Aceleração do mouse, polling 1000 Hz, Raw Input | Forte | Baixo | Tem (aceleração) | Guia para polling/raw input |
| Timer resolution e `bcdedit` (HPET, `useplatformclock`, `disabledynamictick`) | **Fraca e contraditória**: guias se contradizem sem medir | Médio | Não | **Não aplicar às cegas.** Só modo avançado com medição A/B |
| Diagnóstico de DPC/ISR (drivers que travam o sistema) | Forte (é como se acha a causa de micro-travadas; LatencyMon usa essa ideia) | Nenhum (só leitura) | Não | **Prioridade alta**: medir DPC/ISR por driver via ETW e apontar o culpado (ex.: ndis.sys, dxgkrnl.sys, HDAudBus.sys) |
| PCIe Link State Power Management, USB selective suspend, mínimo do processador a 100% | Mista | Baixo (consome mais energia) | Não | Entram no "perfil de jogo" via `powercfg`, reversível |
| Core parking | Não coberto pelas buscas | Baixo | Não | Conhecimento técnico: `powercfg` CPMINCORES; testar com medição |
| MSI Mode / afinidade de interrupção da GPU | Fraca | Médio | Não | Só modo avançado, depois |

### 1.2 Segurança x desempenho (VBS / Integridade da Memória)
- Tom's Hardware (teste original): perda média de cerca de **5%** em jogos com VBS/HVCI; testes mais novos entre **2% e 10%**, com **1% lows** caindo até 15% em CPU-limitado. É dependente da CPU (processadores com MBEC reduzem o custo).
- O Windows está passando a ligar isso por padrão em atualizações recentes.
- **Proposta:** oferecer como ajuste **Alto risco** (reduz proteção contra malware no kernel), nunca como "recomendado", com aviso claro e **medição A/B** pelo benchmark do app para o usuário ver se compensa no PC dele.

### 1.3 Memória e BIOS (onde estão os ganhos mais concretos)
| Item | Evidência | Ganho citado |
|---|---|---|
| **XMP/EXPO ligado** | Forte | 4–10% de FPS médio e até ~20% nos 1% lows (ex.: 7800X3D de DDR5-4800 para EXPO 6000: ~10% / ~20%) |
| **Memória em canal duplo** | Forte (conhecimento técnico) | Grande em iGPU e jogos CPU-limitados |
| Resizable BAR | **Mista** | Entre −2,5% e +5% médios; depende do jogo; no NVIDIA o driver liga só nos jogos com ganho |
| Microcode/BIOS Intel 13ª/14ª geração | Forte (comunicado da Intel) | Estabilidade: usar BIOS com microcode **0x12B ou mais novo (0x12F)** e o perfil "Intel Default Settings" |

**Detecção que o app consegue fazer sozinho (Windows/WMI):**
- Velocidade **configurada** x velocidade **nominal** de cada pente (`Win32_PhysicalMemory`: `ConfiguredClockSpeed` x `Speed`): se a RAM roda abaixo do que o kit suporta, avisar "XMP/EXPO desligado".
- Quantidade de pentes e canais: avisar "um pente só = canal único".
- Fabricante/modelo/versão/data da BIOS (`Win32_BIOS`, `Win32_BaseBoard`) e microcode da CPU (registro `HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0`, valor `Update Revision`; conhecimento técnico, validar).
- Resizable BAR: detectável, método a validar.

### 1.4 PBO / Curve Optimizer / undervolt
Fontes (guias da comunidade) concordam no método, não em valores únicos:
- Ponto de partida do **Curve Optimizer** (all-core): −10 a −15; por geração: Zen 3 (5000) ~−15 (menos margem), Zen 4 (7000) e Zen 5 (9000) ~−20; subir em passos de 5 e **voltar um passo** ao primeiro erro.
- Manter PPT/TDC/EDC/Scalar em padrão ao fazer undervolt.
- Cada chip é diferente; testar estabilidade **por núcleo** (ex.: ferramenta CoreCycler) depois de cada mudança.
- Qualquer PBO é overclock e **pode afetar a garantia**.

**Proposta de recurso "Receita de BIOS" (não aplica nada sozinho):**
1. O app lê CPU/placa-mãe/memória/BIOS.
2. Gera um passo a passo personalizado: onde fica a opção na BIOS daquela marca, valores iniciais conservadores, como testar com o **Teste de estresse por núcleo** do app, e como reverter.
3. Registra os resultados do usuário e compara com o estoque (temperatura, clock, pontuação do benchmark).
4. Por que não aplicar direto: o Windows não consegue ajustar Curve Optimizer sem o Ryzen Master, e errar valores trava o PC. O app orienta e mede.

### 1.5 BIOS e drivers: "está atualizado?"
O que a pesquisa achou:
- **Ler a versão** da BIOS no Windows é fácil e confiável (`Win32_BIOS`, `Win32_BaseBoard`).
- **Descobrir a versão mais nova** automaticamente **não tem API pública** das fabricantes (ASUS, MSI, Gigabyte, ASRock): nenhuma busca encontrou feed JSON oficial. Cada uma tem utilitário próprio (Armoury Crate/Live Update, MSI Center, Gigabyte Control Center, ASRock Live Update). Um relato antigo citou servidores de "Live Update" comprometidos: baixar só do site oficial.
- Notebooks e PCs de marca (Dell, HP, Lenovo) têm ferramentas de atualização oficiais próprias (Dell Command Update, HP CMSL, Lenovo Thin Installer); a integração precisa ser validada.

**Estratégia proposta (honesta):**
1. Mostrar BIOS atual (versão, data, fabricante, placa) e **idade** da BIOS, com alerta se for muito antiga.
2. Abrir a **página de suporte exata** da placa no site oficial (link gerado a partir de fabricante + modelo).
3. **Base de conhecimento curada pelo MB Extreme** (arquivo que atualizamos): versões mínimas recomendadas para problemas conhecidos (ex.: Intel 13ª/14ª geração: microcode ≥ 0x12B/0x12F; AMD: AGESA para as gerações novas). Assim o app consegue dizer "sua BIOS está abaixo da recomendada para este problema" sem depender de API de fabricante.
4. Para PCs de marca, usar a ferramenta oficial da marca quando existir.
5. **O app não atualiza BIOS sozinho.** Atualizar BIOS tem risco de deixar o PC inutilizável. O app só entrega um checklist (notebook na tomada, nada de desligar, baixar do site oficial, ler as notas da versão).
6. Drivers: GPU já tem atualização automática (NVIDIA direto; AMD/Intel via Windows Update). Chipset (AMD, Intel) e áudio/rede via Windows Update. Para AMD, o driver de chipset também instala planos de energia importantes.

### 1.6 Rede e ping
Resultado da pesquisa: **evidência fraca**. Guias falam de Nagle (`TcpAckFrequency`/`TCPNoDelay`), Interrupt Moderation (−1 a −3 ms sem fonte), Energy-Efficient Ethernet. Pontos honestos:
- Nagle só afeta **TCP**; a maioria dos jogos em tempo real usa **UDP** e muitos já desligam Nagle no próprio programa.
- Ganhos medidos: no máximo alguns milissegundos.
- Ping alto por distância, rota ou Wi-Fi **não se resolve com registro do Windows**.
- **Proposta:** manter o que já existe (DNS, MTR, ping/TCPing), adicionar **diagnóstico de Wi-Fi** (sinal, banda, canal) e "economia de energia da placa de rede" desligável por adaptador, todos marcados como **Experimental** e com medição de jitter/perda antes e depois.

### 1.7 PCs fracos
O que mais se repete e faz sentido:
- **Programas de inicialização** (gerenciador de startup) — **não existe no app ainda**.
- **Efeitos visuais** (animações, transparência, sombras) — não existe ainda.
- **Apps em segundo plano** e **Game Bar/captura** — já temos.
- **Plano de energia** — já temos.
- **Configuração dentro do jogo** (resolução, texturas, sombras) é onde está a maior parte do ganho. **Proposta:** "Assistente de jogo": lê GPU/VRAM/RAM e sugere preset (baixo/médio) por faixa de hardware.
- Cuidado com o **SysMain** (Superfetch): em **HD** ele ajuda; desabilitar pode piorar. O app já classifica como risco Médio; ajustar o texto para HD x SSD.
- Pagefile: manter gerenciado pelo Windows (sem dados convincentes de que mexer ajude).
- Não confiar em promessas de "20–60% de FPS": sem fonte.
- Defender: excluir pasta de jogos reduz verificação, mas **reduz segurança**: só como opção avançada.

### 1.8 Armazenamento
- Já temos: saúde, temperatura, desgaste, otimização (TRIM/defrag).
- Falta: limpar **cache de shaders** da GPU (NVIDIA/AMD/DirectX) quando causa stutter; recomendar **jogos no SSD**; manter 10–15% do disco livre.

## 2. Funções que você pediu: como fazer
### 2.1 Informações do sistema em tempo real (clock por núcleo, voltagens, núcleos)
- **Sem dependência nova**: o LibreHardwareMonitor que já usamos entrega, quando o hardware expõe: clock por núcleo, carga por núcleo, temperaturas (núcleo, pacote, Tctl), tensões (Vcore/VID, e as do Super I/O da placa, que dependem do modelo), potência, GPU (clock de núcleo e memória, hot spot, tensão, potência, ventoinha, VRAM usada), temperatura de discos.
- WMI (`Win32_Processor`): núcleos físicos, threads, clock base/máximo, caches L2/L3.
- Página **Monitor em tempo real**: tabela agrupada por componente com valor atual, mínimo e máximo, atualizada a cada 1 s; gráfico; exportar CSV; modo "só o que está acima do normal".
- Limite honesto: voltagens da placa e o Vcore dependem do chip Super I/O da placa e do driver **PawnIO**; quando indisponível o app mostra "sem leitura" em vez de inventar.

### 2.2 Benchmark estilo Futuremark (3D) e CPU/RAM
- **GPU 3D**: bibliotecas **Vortice.Windows** (Direct3D 11/12 para .NET; licença MIT segundo o repositório, a confirmar na hora; compatibilidade com .NET 8 a confirmar no NuGet). Cenas próprias (não copiamos o 3DMark): (1) fractal por raymarching (ALU), (2) muita geometria instanciada com iluminação (vértices/draw calls), (3) preenchimento/texturas, (4) compute shader (FLOPS). Resolução fixa em alvo fora da tela (independente do monitor), V-Sync desligado, FPS e frametime ao vivo, pontuação própria "MB Score".
- **CPU ampliado**: 1 núcleo, todos, SIMD (AVX2), criptografia (AES/SHA), compressão, simulação de física; **por núcleo** para achar o núcleo mais fraco.
- **Memória**: largura de banda (leitura/escrita/cópia) e **latência** (acesso aleatório em ns, com curva L1/L2/L3), útil para ver o efeito de XMP/EXPO e canal duplo.
- Ligado ao **A/B**: "rodar antes", aplicar uma otimização, "rodar depois", mostrar a diferença.

### 2.3 Gravação: Full HD, QHD, 4K e 30/60 FPS com a placa de vídeo
- Hoje: AVI próprio (Motion JPEG), sem aceleração, bom só para testes curtos.
- **Caminho recomendado**: usar **FFmpeg** (instalado pelo winget sob demanda; só chamamos o programa) com captura `ddagrab` (a captura roda na GPU) e codificadores da placa:
  - NVIDIA: `h264_nvenc`, `hevc_nvenc`, `av1_nvenc` (AV1 só RTX 40+).
  - AMD: `h264_amf`, `hevc_amf`, `av1_amf` (AV1 só RX 7000+).
  - Intel: `*_qsv` (o `ddagrab` não alimenta o QSV direto; precisa de caminho próprio).
- O app detecta os codificadores (`ffmpeg -encoders`), a marca/geração da GPU e oferece só o que funciona: **1080p / 1440p / 2160p** e **30 / 60 fps**, H.264/H.265/AV1, qualidade (CQ), MKV/MP4.
- Pontos que a pesquisa **não** confirmou e precisam de teste real: limite de 4K60 por GPU, comandos exatos de HEVC/AV1 com `ddagrab`, limites de sessões simultâneas.
- Alternativa futura, sem FFmpeg: Windows Graphics Capture + Media Foundation (os codificadores NVENC/AMF/QSV aparecem como MFTs). Dá mais trabalho, mas elimina a dependência externa.
- Áudio (sistema e microfone) não está no escopo da primeira versão.

## 2.4 Confirmação em fontes internacionais (testes publicados)
Segunda rodada de pesquisa, limitada a veículos e documentação técnica de referência (Tom's Hardware, TechPowerUp, ComputerBase, GamersNexus, Puget Systems, Guru3D, TweakTown, Blur Busters, Microsoft, NVIDIA, AMD, AV-Comparatives). O que mudou ou foi confirmado:

| Tema | O que os testes mostram | Efeito no app |
|---|---|---|
| **HAGS** | Tom's Hardware: desempenho praticamente igual ligado ou desligado. GamersNexus (Windows 10): 1% lows equivalentes num i9; num i3 ficou **pior** ligado. | Tratar como **neutro/contextual**. Só recomendar ligar quando o jogo exige (DLSS Frame Generation) |
| **Game Mode** | Pouquíssimo teste em Windows 11; no Windows 10 ajudou mínimos de FPS. | Manter ligado como padrão, sem prometer ganho |
| **Windows 11 24H2 + Ryzen** | Hardware Unboxed: ~+10% (7700X) e ~+11% (9700X) de 23H2 para 24H2; Hardwareluxx até +23,5%, com alguns jogos sem ganho ou piores. ComputerBase atribui à predição de desvios. | **Detectar a versão do Windows** e avisar para atualizar em PCs Ryzen: é um dos maiores ganhos comprovados e custa nada |
| **Windows 10 x 11** | ComputerBase: empate técnico hoje; o custo da VBS/HVCI é menor no Windows 11. | Não sugerir trocar de Windows só por FPS |
| **Plano de energia (Ryzen)** | Puget Systems usa "Alto desempenho" nos testes por causa da penalidade do "Equilibrado"; a AMD distribui o plano "Ryzen Balanced" no driver de chipset. | Detectar se o **driver de chipset AMD** está instalado e oferecer; só depois sugerir Alto desempenho |
| **Intel híbrida (P/E-cores)** | TechPowerUp: alguns jogos põem threads principais nos E-cores (Atlas Fallen ganhou até ~50% sem E-cores); a Intel desaconselha desligá-los de forma geral. | Não desligar E-cores por padrão. Opção avançada: **afinidade por jogo** (P-cores) em vez de mexer na BIOS |
| **Resizable BAR** | RTX 3090: +1,2% a +4,8% (média 3,2%); Dead Space chegou a +46% num relato; em outros jogos zero ou perda. NVIDIA liga por jogo; AMD é global. | Informar o estado e **não forçar** |
| **XMP/EXPO e canal duplo (DDR5)** | Tom's Hardware: **um único pente DDR5 custa 8–10%** em média (menos de 5% num 9800X3D); TechPowerUp: subtimings apertados rendem mais que kits caros com perfil frouxo; ponto ideal em Ryzen é 6000–6400 MT/s; números de "EXPO ULL" (11%/15% nos 1% lows) são **da AMD**. | Alertar **canal único** e **XMP/EXPO desligado**; não prometer ganho de "ULL" |
| **Undervolt/PBO (Ryzen)** | Tom's/TechPowerUp: Curve Optimizer em −10 a −20 reduz potência e temperatura com **pouca perda**; em jogos o ganho de desempenho é pequeno (maior em produtividade). Relatos de travamento em repouso com offsets agressivos. | Posicionar como **temperatura/ruído/consumo**, não como "mais FPS"; validar com o teste de estresse |
| **Antivírus/Defender** | Sem teste controlado de FPS com Defender ligado/desligado. Há um caso antigo de Defender prejudicando CPU Intel (TechPowerUp) e relatos com anti-cheat. AV-Comparatives mede impacto de sistema, não FPS. | **Não** oferecer desligar o Defender. Se oferecer exclusões, só como avançado e com A/B |
| **Reflex / modo de baixa latência** | Blur Busters: ajuda quando a **GPU está no limite** (fila de renderização); pouco efeito quando não está; sem efeito em DirectX 12/Vulkan no modo de baixa latência do painel. Limitador de FPS interno do jogo = menor latência; externo = menos stutter. Regra comum: limitar ~3 FPS abaixo da taxa máxima com VRR. | Guia dentro do app por tipo de jogo/API; detectar gargalo (GPU ≥ 95%) para saber quando vale |
| **Cache de shaders (GPU)** | NVIDIA: o limite padrão pode descartar shaders úteis e causar stutter; 10 GB é recomendado por vários; o driver novo **apaga o cache** (stutter na primeira execução é esperado). | **Não** limpar cache por padrão; orientar a subir o limite (NVIDIA) e avisar sobre o primeiro uso após atualizar driver |
| **SSD, NVMe, DirectStorage** | Demo Forspoken (Tom's Hardware): HDD 21,5 s, SATA 3,7 s, NVMe 1,9 s; DirectStorage melhora só frações de segundo no mesmo SSD. | Recomendar **HD → SSD**; NVMe vs SATA só como detalhe |
| **Codificadores de vídeo** | Tom's Hardware/EposVox (VMAF): em H.264, NVENC ≈ Quick Sync, AMF um pouco atrás (e o AMF com pré-análise empata, mas custa desempenho); em **bitrate baixo**, Quick Sync e AMD saíram à frente do NVENC num teste; AV1 da Intel Arc foi o melhor em qualidade. Testes de HEVC e de impacto no FPS do jogo são escassos. | Oferecer H.264/HEVC/AV1 conforme a GPU e **não prometer** "melhor codificador"; deixar o usuário escolher e mostrar o aviso de 4K60 |
| **Timer/EcoQoS (Microsoft)** | A documentação oficial descreve EcoQoS e "ignorar resolução de timer" como recursos de **economia de energia** e diz para não usar EcoQoS em primeiro plano. Resolução alta de timer aumenta consumo. | O Modo Jogo MB pode **tirar processos do jogo do modo eficiência** via API oficial, sem hack de registro |

**Conclusões que mudam o plano:**
1. O maior ganho **comprovado e barato** em Ryzen é estar no Windows 11 **24H2 ou mais novo** com chipset/BIOS atuais. O app deve detectar e avisar.
2. O maior ganho de hardware **sem custo** é **XMP/EXPO ligado + canal duplo**. O app deve detectar e avisar.
3. A maior parte dos "tweaks de registro" não tem teste independente. Ficam como Experimental, com A/B.
4. Itens a **remover da lista de recomendados**: desligar HAGS por padrão, desligar Defender, desligar E-cores, limpar cache de shaders.

## 3. Ordem sugerida
**Fase A (sem dependência nova, alto valor):**
1. Monitor em tempo real completo (2.1).
2. Hardware e BIOS: detecção, alerta de XMP/EXPO e canal único, microcode, idade da BIOS, base de versões recomendadas, **Receita de BIOS/PBO**.
3. Gerenciador de inicialização, efeitos visuais, cache de shaders.
4. Perfil de energia avançado (ASPM, USB suspend, core parking) e **Modo Jogo MB** (ao abrir um jogo: fecha apps pesados escolhidos, ajusta energia/prioridade e restaura depois).
5. Benchmarks de CPU/RAM ampliados e o **A/B antes/depois**.

**Fase B:**
6. Diagnóstico de latência por ETW (DPC/ISR).
7. Teste de estresse **por núcleo** (para Curve Optimizer).
8. Benchmark 3D de GPU (Vortice).
9. Gravação com codificação por GPU (FFmpeg).

**Fase C:** integração com ferramentas de OEM, áudio na gravação, Media Foundation nativo, assistente de configuração por jogo.

## 4. O que não vamos fazer
- Prometer percentual de FPS.
- Aplicar tweaks de registro "mágicos" de TCP/timer sem medir.
- Desligar Windows Update ou Defender de forma permanente.
- Atualizar BIOS automaticamente.
- Aplicar overclock/PBO sem o usuário entender e confirmar.

## 5. Decisões que dependem de você
1. **VBS/Integridade da Memória**: oferecer como opção de alto risco, com medição antes/depois?
2. **FFmpeg** para a gravação (instalado sob demanda pelo winget) ou só solução nativa (mais lenta de entregar)?
3. **Vortice.Windows** como nova dependência para o benchmark 3D?
4. **BIOS**: concorda em só orientar (nunca atualizar sozinho)?
5. Qual fase começar primeiro?

## Fontes
- Segunda rodada (internacional): [GamersNexus: HAGS](https://gamersnexus.net/guides/3599-windows-10-hardware-accelerated-gpu-scheduling-benchmarks), [Tom's Hardware: HAGS](https://www.tomshardware.com/news/gpu-hardware-scheduling-tested), [ComputerBase: Windows 10 x 11](https://www.computerbase.de/artikel/betriebssysteme/windows-10-vs-windows-11-gaming-leistung-test.90348/), [ComputerBase: Ryzen e 24H2](https://www.computerbase.de/artikel/prozessoren/amd-ryzen-intel-core-gaming-benchmarks-windows-11-24h2.90044/), [Tom's Hardware: Ryzen no 24H2](https://www.tomshardware.com/pc-components/cpus/ryzen-cpus-receive-a-performance-bump-with-the-windows-11-24h2-release-preview-version), [Puget Systems: 9800X3D](https://www.pugetsystems.com/labs/articles/amd-ryzen-7-9800x3d-content-creation-review/), [GamersNexus: plano Ryzen](https://gamersnexus.net/news-pc/2870-ryzen-power-plan-update-min-frequency-90-pct), [Tom's Hardware: DDR5 de um pente](https://www.tomshardware.com/pc-components/ddr5/single-dimm-ddr5-gaming-works-better-than-you-probably-think-amds-3d-v-cache-chips-drop-less-than-3-percent-one-ddr5-dimm-beats-dual-channel-ddr4-ram), [TechPowerUp: DDR5 em Zen 5](https://www.techpowerup.com/review/ddr5-memory-performance-scaling-with-amd-zen-5/22.html), [Guru3D: EXPO ULL](https://www.guru3d.com/review/gskill-6000-amd-expo-ull-ultra-low-latency-ddr5-ram-review/), [Tom's Hardware: Resizable BAR RTX 3090](https://www.tomshardware.com/news/nvidia-geforce-rtx-3090-3-percent-performance-boost-resizable-bar), [TechPowerUp: Dead Space e ReBAR](https://www.techpowerup.com/304346/dead-space-performance-jumps-46-with-resizable-bar-enabled), [TechPowerUp: E-cores](https://www.techpowerup.com/312242/atlas-fallen-optimization-fail-gain-50-additional-performance-by-turning-off-the-e-cores?cp=3), [TechPowerUp: Defender e CPU Intel](https://www.techpowerup.com/295877/windows-defender-can-significantly-impact-intel-cpu-performance-we-have-the-fix), [AV-Comparatives: desempenho](https://av-comparatives.org/tests/performance-test-september-2026/), [Tom's Hardware: Ryzen 9 9950X (PBO/CO −10)](https://www.tomshardware.com/pc-components/cpus/amd-ryzen-9-9950x-cpu-review/4), [Tom's Hardware: DirectStorage](https://www.tomshardware.com/news/directstorage-reduces-forspoken-load-times-to-less-than-two-seconds), [Tom's Hardware: AMF x NVENC](https://www.tomshardware.com/news/amd-amf-encoder-quality-boost), [Tom's Hardware: AV1 Intel Arc](https://www.tomshardware.com/news/intel-arc-av1-encoder-dominates-nvenc), [NVIDIA: tamanho do cache de shaders](https://www.nvidia.com/content/Control-Panel-Help/vLatest/en-us/mergedProjects/3D%20Settings/Manage_3D_Settings_(reference).htm), [Blur Busters: Reflex](https://forums.blurbusters.com/viewtopic.php?t=7522), [Microsoft: EcoQoS](https://devblogs.microsoft.com/performance-diagnostics/introducing-ecoqos/), [Microsoft: SetProcessInformation](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-setprocessinformation)
- Guias gerais de latência e otimização do Windows 11: [Kunal Chowdhury](https://www.kunal-chowdhury.com/2026/09/optimize-windows-11-peak-gaming-performance.html), [Ultimatemove](https://ultimatemove.eu/en/windows-gaming-optimization/), [Switchblade Gaming](https://www.switchbladegaming.com/game-settings/windows-11-gaming-optimization/), [SageTweaks](https://www.sagetweaks.com/reduce-input-lag-windows)
- DPC/LatencyMon: [LatencyMon](https://www.latencymon.com/), [Wccftech](https://wccftech.com/how-to/how-to-diagnose-and-fix-high-dpc-latency-in-windows/), [TechBloat](https://www.techbloat.com/how-to-fix-high-dpc-latency-on-windows-11-and-10.html)
- PC fraco: [LagoFast](https://www.lagofast.com/en/blog/optimize-low-end-pc-for-gaming/), [TechTimes](https://www.techtimes.com/articles/313747/20260105/how-optimize-your-pc-gaming-boost-fps-without-spending-hardware-upgrades.htm), [BSR Studios](https://bsr-studios.github.io/articles/how-to-boost-fps-low-end-pc.html)
- VBS/Memory Integrity: [Tom's Hardware](https://www.tomshardware.com/news/windows-11-gaming-benchmarks-performance-vbs-hvci-security), [Neowin](https://www.neowin.net/news/microsofts-vbshvci-still-hurts-windows-11-performance-even-on-latest-versions/), [Digital Trends](https://www.digitaltrends.com/computing/are-windows-11-security-features-killing-gaming-performance/)
- Rede: [Tier1Settings (Nagle)](https://tier1settings.com/disable-nagles-algorithm-to-reduce-game-lag/), [P. Belamri](https://pbelamri.com/tcpnodelay/), [FixGameLag](https://fixgamelag.com/network-adapter-settings-gaming/)
- Memória/ReBAR: [Club386](https://www.club386.com/resizable-bar-on-or-off/), [SmallFormFactor](https://smallformfactor.net/articles/resizable-bar-at-pcie-3-0-with-a-mid-range-gpu-benchmark/), [Tier1Settings (XMP/EXPO)](https://tier1settings.com/how-to-enable-xmp-or-expo-for-gaming/), [NZXT](https://nzxt.com/en-intl/blogs/news/how-to-enable-xmp-expo)
- PBO/Curve Optimizer: [TechFuelHQ](https://techfuelhq.com/articles/9800x3d-undervolt-guide-2026/), [MakeUseOf (CoreCycler)](https://www.makeuseof.com/undervolt-ryzen-cpus-cycler/), [WindowsForum](https://windowsforum.com/news/amd-ryzen-curve-optimizer-guide-undervolt-boost-clocks-stability-testing-and-real-world-gains.446809/), [Tech Insider](https://tech-insider.org/cpu-undervolting-guide-curve-optimizer-xtu-2026/)
- BIOS: [powershell.one (Win32_BIOS)](https://powershell.one/wmi/root/cimv2/win32_bios), [TechSpot (utilitários)](https://www.techspot.com/article/1824-update-motherboard-bios/), [Tier1Settings (atualizar BIOS)](https://tier1settings.com/how-to-update-your-bios-safely/)
- Intel 13ª/14ª geração: [Intel Community (0x12F)](https://community.intel.com/t5/Mobile-and-Desktop-Processors/Intel-Core-13th-and-14th-Gen-Vmin-Shift-Instabilty-Update-New/m-p/1751126), [Club386 (0x12B)](https://www.club386.com/intel-confirms-0x12b-microcode-fixes-13th-and-14th-gen-issues-once-and-for-all/), [Neowin](https://www.neowin.net/news/intel-says-latest-13th14th-gen-cpu-instability-bug-firmware-does-not-impact-performance/)
- Gravação/codificação por GPU: [FFmpeg ddagrab](https://ayosec.github.io/ffmpeg-filters-docs/8.0/Sources/Video/ddagrab.html), [FFmpeg Cookbook](https://ffmpeg-cookbook.com/en/articles/screen-recording/), [Comparativo NVENC/AMF/QSV](https://remio.net/blog/hardware-encoder-comparison)
- Benchmark 3D (Direct3D em .NET): [Vortice.Direct3D11 (NuGet)](https://www.nuget.org/packages/Vortice.Direct3D11/), [Vortice.Windows (GitHub)](https://github.com/amerkoleci/Vortice.Windows)
