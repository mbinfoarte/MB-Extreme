# Hardware, jogos, testes, dispositivos, drivers e programas

Funções inspiradas em CPU-Z/GPU-Z/AIDA, OCCT, MSI Afterburner, LatencyMon e recursos do próprio Windows. Implementação própria (clean-room), tudo com risco indicado e reversível quando altera algo. A base das recomendações está em `PESQUISA-OTIMIZACAO-GAMER.md` (fontes e testes publicados).

## Organização do menu
| Módulo | Páginas |
|---|---|
| Otimização | Perfis, Limpeza, Inicialização, Desempenho, Efeitos visuais, Memória, Armazenamento |
| Jogos | Modo Jogo, Jogos e energia, Ajustes para jogos, Diagnóstico de latência, Assistente de jogo, Monitor de FPS, Gravação de tela |
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
- **Diagnóstico de latência** (`LatencyMonitor`): sessão ETW do kernel (DPC, ISR, imagens carregadas) agrupada por driver, com picos, médias e dica por tipo de driver; mostra a resolução do temporizador do Windows (somente leitura).
- **Assistente de jogo** (`GameAdvisor`): nível do PC pela VRAM e RAM, alvo de resolução/predefinição, upscaler (DLSS/FSR/XeSS), limite de quadros conforme a taxa da tela, Reflex/Anti-Lag.
- **Monitor de FPS**: sessão ETW com o provedor Microsoft-Windows-DXGI (Present), por processo. FPS, tempo de quadro e "1% pior". Overlay transparente, sempre no topo e sem captura de mouse; atalho global Ctrl+Shift+F10. Limite: jogo em tela cheia exclusiva pode esconder o overlay.
- **Gravação de tela**: pela placa de vídeo com FFmpeg (instalado sob demanda pelo winget): captura `ddagrab` e codificadores NVENC/AMF/Quick Sync em H.264, H.265 e AV1, 1080p/1440p/2160p ou resolução da tela, 30 ou 60 fps, MKV ou MP4. O app testa quais codificadores realmente funcionam na placa. Resolução diferente da tela redimensiona pela CPU. Modo compatível: AVI Motion JPEG próprio, sem programas externos. Sem som.

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
- Ainda não há áudio na gravação de tela.
- A consulta ao catálogo da NVIDIA, o benchmark 3D, o ETW e a gravação com FFmpeg dependem do hardware e do Windows; falhas aparecem como mensagem na página e o restante do app segue funcionando.
- Temperatura de CPU e algumas tensões dependem do driver PawnIO.
