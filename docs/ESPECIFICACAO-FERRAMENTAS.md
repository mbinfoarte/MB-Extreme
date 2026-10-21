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

## Próximas etapas
2. **Benchmark** (CPU, memória, disco, com histórico) e **teste de estresse** (CPU/RAM, limite de temperatura, parada automática, detecção de erros).
3. **Monitor de FPS** por ETW (eventos de apresentação do DXGI/D3D) e overlay transparente com FPS, frametime, 1% low, temperaturas e uso.
4. **Gravação de tela** em `Vídeos\MB Extreme`.

## Limites conhecidos
- Sem busca automática do "driver mais novo" de cada fabricante (sem API oficial estável); usamos página oficial + Windows Update + winget.
- Temperatura de CPU depende do driver PawnIO.
