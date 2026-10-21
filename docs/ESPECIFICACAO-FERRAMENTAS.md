# Ferramentas de hardware, dispositivos, drivers e jogos

Funções inspiradas em CPU-Z/GPU-Z/AIDA, OCCT, MSI Afterburner e recursos do próprio Windows. Implementação própria (clean-room), tudo com risco indicado e reversível quando altera algo.

## Organização do menu
| Módulo | Páginas | Etapa |
|---|---|---|
| Otimização | Limpeza, Desempenho, Memória, Armazenamento | 1 (Armazenamento: em breve) |
| Jogos | Jogos e energia, Ajustes para jogos, Monitor de FPS, Gravação de tela | 1 / FPS e gravação: 3 e 4 |
| Testes | Informações do sistema, Benchmark, Teste de estresse | 1 / 2 |
| Dispositivos | Dispositivos conectados, Cache de portas USB | 1 |
| Rede | DNS, Otimizações de rede, Analisador | 1 (analisador: em breve) |
| Windows | Privacidade, Aplicativos, Boot, Periféricos | 1 |
| Drivers e programas | Drivers, Programas | 1 |

## Etapa 1 (esta)
- **Informações do sistema**: WMI (CPU, placa-mãe/BIOS, memória, GPU, discos, rede, Windows). Somente leitura; copiar/salvar relatório.
- **Dispositivos**: Win32_PnPEntity; filtro USB; aviso de conectar/remover por verificação a cada 3 s.
- **Cache de portas USB**: lista dispositivos desconectados (USB, USBSTOR, WPD, HID) via `Get-PnpDevice` e remove com `pnputil /remove-device`. Risco baixo; o Windows recria o registro ao plugar de novo.
- **Jogos e energia**: Modo de Jogo (HKCU\Software\Microsoft\GameBar), HAGS (`HwSchMode`, risco médio, reinício), planos de energia (`powercfg`). Backup/restauração pelo TweakStore.
- **Drivers**: detecta GPU (NVIDIA/AMD/Intel), mostra versão, abre a página oficial do fabricante; drivers do Windows Update via COM `Microsoft.Update.Session`, com ponto de restauração antes de instalar.
- **Programas**: `winget upgrade` e catálogo de instalação (IDs validados por expressão regular).

## Etapa 2 a 4 (implementadas)
- **Benchmark**: CPU em 1 núcleo e em todos, largura de banda de memória e disco (256 MB, gravação com WriteThrough). Pontuação relativa e histórico (20 últimas) em `%AppData%\MBExtreme\bench-history.json`.
- **Teste de estresse**: CPU (cálculo determinístico conferido contra um valor de referência) e memória (padrões gravados e verificados). Para por duração, por limite de temperatura (80/90/95 °C) ou por você. Erros contam como sinal de instabilidade.
- **Monitor de FPS**: sessão ETW com o provedor Microsoft-Windows-DXGI (evento Present, id 42), por processo, sem injetar nada no jogo. FPS, tempo de quadro e "1% pior". Overlay transparente, topmost e sem captura de mouse; atalho global Ctrl+Shift+F10. Limite: jogo em tela cheia exclusiva pode esconder o overlay; Vulkan/OpenGL puros podem não gerar eventos DXGI.
- **Gravação de tela**: captura GDI da tela principal, JPEG por quadro, AVI Motion JPEG escrito pelo próprio app (`MjpegAviWriter`), em `Vídeos\MB Extreme`. Sem som. Limite de ~1,9 GB por arquivo. Pode ser ligada junto do benchmark e do teste de estresse.
- **Armazenamento**: volumes, saúde (Get-PhysicalDisk/StorageReliabilityCounter) e otimização com `defrag /O`.
- **Analisador de rede**: Ping, TCPing e MTR (TTL crescente, perda e latência por salto).

## Pendências
- Central de ações (histórico geral e desfazer tudo), assinatura de código do instalador, gravação de áudio e de uma janela específica.
