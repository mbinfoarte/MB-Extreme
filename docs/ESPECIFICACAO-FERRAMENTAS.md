# Hardware, jogos, testes, dispositivos, drivers e programas

Funções inspiradas em CPU-Z/GPU-Z/AIDA, OCCT, MSI Afterburner, LatencyMon e recursos do próprio Windows. Implementação própria (clean-room), tudo com risco indicado e reversível quando altera algo. A base das recomendações está em `PESQUISA-OTIMIZACAO-GAMER.md` (fontes e testes publicados).

## Organização do menu
| Módulo | Páginas |
|---|---|
| Otimização | Perfis, Limpeza, Inicialização, Desempenho, Efeitos visuais, Memória, Armazenamento |
| Jogos | Modo Jogo, Jogos e energia, Ajustes para jogos, Diagnóstico de latência, Assistente de jogo, Monitor de FPS, Gravação e transmissão |
| Testes | Informações do sistema, Monitor em tempo real, Benchmark, Antes e depois, Teste de estresse |
| Hardware e BIOS | Meu hardware, BIOS e atualizações, Receita de BIOS |
| Dispositivos | Dispositivos conectados, Cache de portas USB |
| Rede | DNS, Otimizações de rede, Analisador de rede, Diagnóstico de Wi-Fi |
| Windows | Privacidade, Aplicativos, Inicialização e boot, Periféricos |
| Drivers e programas | Drivers, Programas |

## Informações e sensores
- **Informações do sistema**: WMI (CPU, placa-mãe/BIOS, memória, GPU, discos, rede, Windows). Copiar e salvar relatório.
- **Monitor em tempo real**: LibreHardwareMonitor com todos os grupos habilitados (CPU, GPU, memória, placa-mãe/Super I/O, controladores, discos, rede, bateria). Clock e uso por núcleo, temperaturas, tensões, potência, ventoinhas, mínimo e máximo, exportar CSV. Tensões e Vcore dependem da placa e do driver PawnIO; sem leitura, o sensor não aparece.

## Hardware e BIOS
- **Meu hardware** (`HardwareService` + `HardwareAdvisor`): lê CPU (núcleos, threads, cache, microcódigo), placa-mãe, BIOS, memória por pente (velocidade nominal x em uso, tipo), GPU/VRAM, Windows (versão e compilação), notebook x desktop, driver de chipset AMD e Resizable BAR (NVIDIA, via `nvidia-smi`). As regras geram avisos com nível (OK, Info, Atenção, Problema) e ação sugerida.
- **BIOS e atualizações**: versão, data e idade da BIOS, microcódigo; busca no site oficial restrita ao domínio da fabricante (placa-mãe em PC montado, modelo do equipamento em PC de marca); lista de verificação antes de atualizar. Não existe API pública das fabricantes para a versão mais nova. O app **nunca atualiza BIOS sozinho**.
- **Receita de BIOS**: passo a passo para o hardware lido: perfil de memória (XMP/EXPO e velocidade-alvo), Resizable BAR, PBO/Curve Optimizer por geração do Ryzen (início em −10, passos de 5, voltar um passo ao primeiro erro, PPT/TDC/EDC no padrão), Intel 13ª/14ª geração (microcódigo 0x12B/0x12F e Intel Default Settings), como testar com o app e como voltar atrás. Copiar e salvar.

## Benchmark e testes
- **Benchmark** (`BenchmarkService`): CPU em 1 núcleo e em todos; vetorial (SIMD, GFLOPS); criptografia (AES-256); compressão; memória (leitura, escrita, cópia e latência de acesso aleatório com curva de cache); disco (256 MB, gravação com `WriteThrough`); GPU 3D. Completo ou rápido. Pontuação relativa e histórico (20 últimos) em `%AppData%\MBExtreme\bench-history.json`. **CPU por núcleo** testa cada núcleo lógico fixado por afinidade.
- **Benchmark 3D** (`Gpu3DBenchmark`): Direct3D 11 (Vortice.Windows, MIT), fora da tela em 1920x1080, escolhe a GPU com mais memória dedicada. Cenas: fractal por raymarching (cálculo), 98 mil cubos instanciados (geometria), camadas de preenchimento (pixels) e compute (GFLOPS). A página mostra a imagem renderizada ao vivo.
- **Antes e depois**: medição rápida (CPU, memória, latência) com nome, escolha de duas medições e comparação com veredito; até 3% é variação normal.
- **Teste de estresse**: CPU (cálculo determinístico conferido) e memória (padrões gravados e verificados); duração, limite de temperatura, parada automática. **Por núcleo**: um núcleo físico de cada vez com cálculos escalares e vetoriais conferidos, para achar o núcleo instável no Curve Optimizer.

## Jogos
- **Modo Jogo** (`GameBoostService` + `GameWatcher`): lista de jogos, vigia a abertura; troca o plano de energia para alto desempenho/Desempenho Máximo, pede para fechar (sem forçar) os programas escolhidos, sobe a prioridade e tira o jogo do modo de eficiência (EcoQoS, `SetProcessInformation`), libera RAM opcionalmente; ao fechar o jogo restaura o plano de energia.
- **Jogos e energia**: Modo de Jogo do Windows, HAGS (risco médio, reinício) e planos de energia, com backup.
- **Ajustes para jogos**: prioridade, Game DVR, MMCSS, HAGS, bloqueio de drivers no Windows Update e opções de energia na tomada (processador em 100%, núcleos sem estacionar, PCI Express sem ASPM, USB sem suspensão seletiva).
- **Diagnóstico de latência** (`LatencyMonitor`): sessão ETW do kernel (DPC, ISR, imagens carregadas) agrupada por driver, com picos, médias e dica por tipo de driver; mostra a resolução do temporizador do Windows (somente leitura). No topo da página lista as **configurações que afetam a latência** (plano de energia, Modo de Jogo, agendamento de GPU, estacionamento de núcleos, economia do PCIe e do USB, Game DVR, MMCSS, separação de prioridade, limitação de rede, aceleração do mouse e Integridade de memória) com o estado atual e aplicar/desfazer.
- **Assistente de jogo** (`GameAdvisor`): nível do PC pela VRAM e RAM, alvo de resolução/predefinição, upscaler (DLSS/FSR/XeSS), limite de quadros conforme a taxa da tela, Reflex/Anti-Lag.
- **Monitor de FPS**: sessão ETW com o provedor Microsoft-Windows-DXGI (Present), por processo. FPS, tempo de quadro e "1% pior". Overlay transparente, sempre no topo e sem captura de mouse; atalho global Ctrl+Shift+F10. Limite: jogo em tela cheia exclusiva pode esconder o overlay.
- **Gravação e transmissão** (`StudioSession`, `StudioCommand`): uma captura e uma codificação pelo FFmpeg (instalado sob demanda pelo winget) enviadas por `tee` para até três saídas ao mesmo tempo: arquivo (MKV, ou MP4 fragmentado), transmissão RTMP/RTMPS (Twitch, YouTube ou servidor próprio; H.264, no máximo 8 Mbps, `onfail=ignore` para uma queda da rede não derrubar a gravação) e replay (pedaços de 4 s em anel). Origem: tela inteira (`ddagrab`), área da janela (`ddagrab` recortado, quadros ficam na GPU) ou conteúdo da janela (`gdigrab`). Janelas listadas por `EnumWindows` (sem janelas minimizadas, ocultas, de ferramenta ou da barra de tarefas). Áudio: som do PC (WASAPI loopback) e microfone, mixados no app (`AudioMixer`, 48 kHz estéreo, volume por fonte, medidor) e entregues ao FFmpeg por um canal nomeado. Replay: Ctrl + Shift + F9 junta os últimos 30 s, 1 min ou 2 min em um MP4 sem recodificar (`concat`), na pasta `Clipes`. A chave de transmissão só é guardada se o usuário pedir, protegida por DPAPI, e é mascarada em qualquer mensagem. Modo compatível: AVI Motion JPEG próprio, sem programas externos nem áudio.
- **Painel**: `LiveChart` (gráfico animado, linha que desliza a cada medição) com download e envio; `NetworkStatus` (adaptador ativo, roteador, DNS com o nome do provedor), `PingWindow` (média, oscilação, perda das últimas 20 medições) e `RouteSummary` (lê o MTR e aponta o maior salto de latência e a perda até o destino; o app não altera a rota da internet). `AttentionAdvisor` lista o que precisa de atenção (segurança, disco cheio, memória, CPU quente, inicialização).
- **Processador / Placa de vídeo / Memória / Discos**: gráficos de utilização, temperatura, velocidade e energia (sensores LibreHardwareMonitor), uso por núcleo, memória de vídeo, pentes de memória (tipo, velocidade, modelo), velocidade de leitura e gravação por disco e espaço por unidade em barra única (verde livre, laranja/vermelho usado). **Limpeza automática de memória** (`MemoryAutoPolicy`): acima do limite escolhido e respeitando 3 minutos entre limpezas, reduz a memória de programas em segundo plano sem tocar no programa em uso, nos jogos da lista nem na lista de permissões.
- **Pendrives e HDs externos** (`DriveMap`): une letra, modelo e barramento do disco; remoção segura trava e desmonta o volume e pede a ejeção ao dispositivo; só unidades externas, nunca a do Windows.
- **Segurança** (`SecurityService`): Defender (proteção em tempo real, idade das definições, última verificação rápida), outros antivírus (Central de Segurança), firewall por perfil e UAC; botões de verificação rápida e atualização de definições.
- **Ferramentas**: processos (`ProcessList`, com os essenciais do Windows protegidos), arquivos grandes, duplicados (tamanho, trecho inicial e SHA-256), pastas vazias, atalhos inválidos, destruir arquivos (`Shredder`) e desligamento programado (`shutdown.exe`). Tudo que apaga mostra antes o que vai apagar, envia à Lixeira (exceto destruir arquivos) e recusa pastas do Windows e dos programas (`PathGuard`). Não há limpeza de registro: o ganho é nulo e o risco é real.
- **Reparo do Windows** (`RepairCatalog`, `RepairRunner`): SFC (reparar e só verificar), DISM (CheckHealth, ScanHealth, RestoreHealth, StartComponentCleanup), CHKDSK (verificação online e reparo profundo agendado para o reinício), reinício do Windows Update, reparo da loja, WMI, pilha de rede e cache de ícones, mais o reparo completo recomendado (DISM, SFC, CHKDSK). A saída aparece ao vivo (UTF-16 do SFC e página de código do console tratadas), com barra de progresso, cancelamento (encerra a árvore de processos), ponto de restauração opcional, tradução dos códigos de saída em frases, os comandos equivalentes para copiar e relatórios em `%AppData%\MBExtreme\logs`.

## Otimização
- **Perfis**: Jogo, Competitivo (na tomada) e PC fraco; só ajustes de risco baixo, restaurar tudo.
- **Inicialização**: Run (usuário, máquina, 32 bits) e pastas Inicializar; liga/desliga pela chave `StartupApproved` (igual ao Gerenciador de Tarefas), sem apagar.
- **Efeitos visuais**: transparência, animações, atraso de menus e Aero Peek (ajustes reversíveis).
- **Desempenho**: inclui "Desligar a Integridade da Memória/VBS" (risco **alto**, com aviso de segurança) e "Desligar a limitação por processo (EcoQoS)" (experimental).
- **Armazenamento**: volumes, saúde dos discos (temperatura, desgaste, erros) e otimização (`defrag /O`).

## Dispositivos, rede, drivers e programas
- **Dispositivos**: lista (WMI), filtro USB, log de conectar/remover; **cache de portas USB** remove registros desconectados (`pnputil /remove-device`).
- **Analisador de rede**: Ping, TCPing e MTR com perda, latência e **jitter**. **Diagnóstico de Wi-Fi**: sinal, banda, canal e velocidade (`netsh wlan`, português e inglês) com dicas.
- **Drivers**: NVIDIA direto do servidor oficial (catálogo da NVIDIA, download só de `*.nvidia.com` em HTTPS, conferência da assinatura digital, ponto de restauração, instalação silenciosa); AMD e Intel pelo Windows Update filtrado por vídeo; Intel DSA via winget; drivers pendentes do Windows Update.
- **Programas**: `winget upgrade` e catálogo de instalação (IDs validados).

## Limites conhecidos
- Sem busca automática da "BIOS mais nova" por fabricante (não há API pública); o app orienta e abre a busca no site oficial.
- Se a rede de transmissão travar (em vez de cair), o FFmpeg pode atrasar também a gravação até o tempo limite da conexão; o `onfail=ignore` cobre quedas, não travamentos.
- Ao transmitir, a gravação e o replay usam a mesma imagem enviada ao vivo (no máximo 8 Mbps). A janela em modo recorte grava a área da tela onde ela está: se outra janela ficar por cima, aparece no vídeo.
- A linha de comando do FFmpeg, que contém a chave de transmissão, fica visível para outros programas do mesmo PC enquanto transmite (limitação do próprio FFmpeg, que não aceita a chave por outro caminho).
- A consulta ao catálogo da NVIDIA, o benchmark 3D, o ETW e a gravação com FFmpeg dependem do hardware e do Windows; falhas aparecem como mensagem na página e o restante do app segue funcionando.
- Temperatura de CPU e algumas tensões dependem do driver PawnIO.
