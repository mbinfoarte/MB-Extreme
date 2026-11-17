# Análise de lacunas: pacote "Lobo Alpha — Ultimate Performance" × MB Extreme

Leitura completa do pacote (369 arquivos: 163 `.bat`, 55 `.reg`, 33 planos de energia `.pow`, cursores, 15 `.exe` de terceiros). Nada foi executado; só lido. Este documento diz **o que o pacote faz, o que o app já tem, o que falta e o que não vamos adotar**. A implementação só começa depois da sua aprovação (veja "Etapa 8" em `PLANO-ETAPAS.md`).

Política: `CLEAN-ROOM.md`. Estudamos a técnica e reescrevemos do zero, com backup, desfazer e aviso. Não copiamos texto, arte ASCII, menus nem binários.

Legenda de decisão: **TEMOS** (já no app), **ADICIONAR** (baixo risco, entra), **OPCIONAL** (entra desligado, com aviso e confirmação), **POR TESTE** (só depois de medir na máquina), **NÃO** (não adotamos).

## 1. Já temos (confirmado no catálogo atual)

Prioridade de CPU em primeiro plano (`Win32PrioritySeparation`), plano Desempenho Máximo, Power Throttling, perfil `Games` do agendador multimídia, Game DVR, HAGS, VBS, bloqueio de driver pelo Windows Update, apps da Loja em segundo plano, DiagTrack, Mapas, Search, SysMain, Spooler, QoS, NetworkThrottling, IPv6, mouse/teclas de aderência, animações/transparência/Aero Peek, Copilot/Cortana/anúncios/SmartScreen, inicialização rápida, núcleos estacionados, USB suspend, PCIe ASPM, limpeza de memória, reparo (SFC, DISM, CHKDSK, Windows Update, Loja, WMI, rede, ícones), DNS (módulo próprio), limpeza de temporários, debloat com 25 apps em 3 níveis.

## 2. Falta e vale adicionar

| # | Técnica do pacote (resumo) | Risco | Decisão | Como fica no app |
|---|---|---|---|---|
| 1 | Hibernação desligada (`powercfg -h off`) | B | ADICIONAR | Ajuste em Armazenamento; some o `hiberfil.sys`; avisa que desliga a inicialização rápida |
| 2 | Atributos ocultos de energia liberados (núcleos mínimos, boost) | B | ADICIONAR | Já planejado; liga a opção de núcleos/boost no plano do MB |
| 3 | TRIM do SSD (`DisableDeleteNotify 0`) e último acesso do NTFS (`disableLastAccess`) | B | ADICIONAR | Página Armazenamento: mostra estado e corrige |
| 4 | Nomes 8.3 desligados (`disable8dot3`) | M | OPCIONAL | Só em volume NTFS de dados, com aviso (instaladores antigos) |
| 5 | HD: tempo de inatividade, desfragmentação (`defrag /O`) | B | ADICIONAR | Somente em HD; em SSD o app oferece só TRIM |
| 6 | Resolver cache de shaders (NVIDIA `DXCache`/`GLCache`, AMD `DxCache`, `D3DSCache`) | B | ADICIONAR | Botão em Jogos e GPU, com tamanho liberado |
| 7 | MPO desligado (`Dwm\OverlayTestMode=5`) | M | OPCIONAL | Para tela piscando/travadas em NVIDIA/AMD/Intel; reversível |
| 8 | AMD: ULPS desligado, shader cache sempre ativo, overlay/telemetria | M | OPCIONAL | Só se a GPU for AMD (detectada) |
| 9 | NVIDIA: telemetria (`NvTelemetryContainer`), ShadowPlay | M | OPCIONAL | Só se NVIDIA; não mexer em `NVDisplay.ContainerLocalSystem` |
| 10 | Intel: prioridade e `bcdedit` de timer (`useplatformtick`, `disabledynamictick`, `tscsyncpolicy`) | M/A | POR TESTE | Faz `bcdedit /export` antes; medir latência antes/depois (já temos o diagnóstico DPC) |
| 11 | Game DVR em tela cheia (`GameDVR_FSEBehavior*`, `AppCaptureEnabled`) | B | ADICIONAR | Estende o ajuste Game DVR que já existe |
| 12 | `SystemResponsiveness` (0 a 10) | B | ADICIONAR | Estende o agendador multimídia; padrão do app = valor medido, não fixo |
| 13 | Rede: RSS ligado, RSC desligado, TCP Nodelay/AckFrequency por interface, economia de energia do adaptador | B/M | ADICIONAR | Página de rede; cada um com o estado atual e desfazer |
| 14 | Limpar DNS e renovar IP | B | ADICIONAR | Botão no módulo DNS |
| 15 | Limpar logs de eventos (`wevtutil`) | M | OPCIONAL | Com confirmação; avisa que apaga rastro de diagnóstico |
| 16 | `EmptyStandbyList` (workingsets, modifiedpagelist, standbylist) | B | TEMOS em parte | Falta a lista de páginas modificadas (nativa, sem o `.exe`) |
| 17 | `SvcHostSplitThresholdInKB` e `LargeSystemCache` por quantidade de RAM | M | POR TESTE | Só em perfis medidos; benefício discutível |
| 18 | Tamanho da fila de dados de mouse/teclado (perfis Fraco/Mediano/Bom: 50/26/16) | M | POR TESTE | Sem evidência técnica sólida; só com medição de latência de entrada |
| 19 | Resposta do teclado (`KeyboardDelay`, `KeyboardSpeed`) | B | ADICIONAR | Em Periféricos. Atenção: `AutoRepeatRate=500` do pacote deixa a repetição lenta; usaremos o valor correto, não o do pacote |
| 20 | Otimização de navegadores por política (Chrome/Brave/Opera/Edge: modo em segundo plano, predição de rede, sugestões, telemetria) + limpar cache | B | ADICIONAR | Página nova "Navegadores", com **reverter** (apagar a política). O pacote tem o reverso, adotamos o mesmo padrão |
| 21 | Prioridade de CPU por executável do jogo (IFEO `PerfOptions\CpuPriorityClass=3`) | B | ADICIONAR | **Peça central da Etapa 3**: cada jogo da biblioteca ganha "Prioridade alta" com chave criada e removida pelo app. Lista de ~90 executáveis conhecidos do pacote serve só como lista de referência (fatos), não copiada em bloco |
| 22 | Serviços extras por jogo: `wisvc` (Insider), `DPS` | M | NÃO | Ganho desprezível; `DPS` quebra diagnósticos do Windows |
| 23 | Plano de energia vendor-específico (AMD/Intel/Gaming) | B | TEMOS | Criamos o plano nativo pelo Windows; os `.pow` ficam de fora |
| 24 | Reparos ("Fix" 13): reativar Defender e mitigações para antitrapaça, Bluetooth, áudio (reiniciar serviços), Wi-Fi/modo avião, tela preta, menu iniciar, PC que não desliga, Xbox/Game Pass, serviços da Loja (BITS) | B | ADICIONAR | Viram tarefas do **Reparo do Windows** com a janela de progresso padrão. Faltam hoje: antitrapaça, Bluetooth, áudio, Wi-Fi/avião, tela preta/Explorer, desligamento, Xbox |
| 25 | Rotina semanal (limpeza completa + SFC/DISM) | B | ADICIONAR | Agendamento semanal opcional no app (tarefa agendada do Windows) |
| 26 | Personalização: cursores (`.cur/.ani`) | B | NÃO por ora | Estética, não desempenho; direitos de arte incertos. Se um dia entrar, com arte própria |
| 27 | Comando `winget upgrade --all`, `pnputil /scan-devices`, `UsoClient StartScan` | B | TEMOS em parte | Winget já integrado em Drivers e programas |

## 3. Opcional com aviso forte (nunca em "recomendados")

- Desligar o hipervisor (`bcdedit /set hypervisorlaunchtype off`): quebra WSL2, Hyper-V, Sandbox, Docker e algumas proteções. Só em modo "Jogos puro", com aviso.
- Remover Loja, Xbox, Fotos, Câmera, Calculadora e afins: já temos em 3 níveis; o pacote remove tudo de uma vez (`*store*`, `*xbox*`). Mantemos a seleção individual.
- Desativar serviços como `WSearch`, `PcaSvc`, `TermService`, `WbioSrvc` (Hello), `bthserv`, `W32Time`, `DoSvc`, `Spooler`: oferecer um a um, cada um com "o que você perde". `WbioSrvc` e `bthserv` só se o usuário não usa biometria/Bluetooth.
- Apagar Prefetch: não é padrão (piora o próximo início); só dentro de "limpeza completa", desmarcado.

## 4. Não adotamos

| Item do pacote | Motivo |
|---|---|
| Scripts "Ativar Windows" e "Ativar Pacote Office" (pasta `Segredo`) | Ativação pirata. Ilegal e incompatível com um produto licenciado. **Fora do produto, sem exceção.** |
| Desativar UAC (`EnableLUA=0`, `ConsentPromptBehaviorAdmin=0`) | Remove a barreira entre aplicativo e administrador; quebra apps da Loja. Princípio do projeto: nunca |
| Desativar Windows Update por política (`NoAutoUpdate`) e `WaaSMedicSvc` | Deixa o PC sem correções de segurança. Em vez disso: pausar atualização por tempo definido (reversível) |
| Desativar SmartScreen como "recomendado" | Já existe, só com confirmação reforçada e nunca recomendado |
| MTU fixo 1400 em todas as placas | Pode piorar a rede. Só o teste de MTU (pacote sem fragmentação) |
| TCP auto-tuning "highlyrestricted" | Reduz vazão em conexões rápidas. Padrão `normal`; só testar |
| `chimney` / `netsh int tcp` obsoletos | Removidos nas versões atuais do Windows; sem efeito |
| Executáveis de terceiros (Process Lasso, Optimizer, Firemin, ISLC, memreduct, X Mouse, NVIDIA Profile Inspector, DDU, Autoruns, LatencyMon, DNS Jumper, CPU-Z, HWMonitor, EmptyStandbyList, sPowers) | Licenças próprias, alguns pagos; não executamos nem redistribuímos. Temos equivalentes nativos: limpeza de memória, inicialização, diagnóstico DPC, DNS, hardware e sensores |
| Arquivos `.pow` binários | Não se sabe o que contêm; criamos o plano pelo próprio Windows |
| Menus ASCII, nomes de marca "iGust", textos | Material de terceiros |

## 5. Riscos que o pacote ignora e o app deve tratar

1. **Sem desfazer na maioria** (`reg add` sem guardar o original). O app sempre guarda o valor original (`tweak-backup.json`) e oferece "Restaurar originais" e o histórico na Central de ações.
2. **Sem detecção de hardware**: aplica ajuste AMD em máquina Intel. O app detecta CPU/GPU e esconde o que não se aplica.
3. **Sem checar HD x SSD** nem versão do Windows (Windows 10 x 11).
4. **Sem medir**. O app roda "Antes e depois" e mostra o resultado real; ajuste sem ganho medido pode ser desfeito com um clique.
5. **Dependem de executar como administrador e reiniciar**; o app avisa o que precisa de reinício.
6. **Ponto de restauração**: o pacote cria; o app cria antes de qualquer lote grande, e valida que a proteção do sistema está ligada (senão avisa).

## 6. Como isso se encaixa nas etapas

- **Etapa 3 (Biblioteca de jogos)** recebe o item 21: prioridade por executável e perfis por jogo (limpeza de shader, Game DVR, plano de energia ao abrir e desfazer ao fechar).
- **Etapa 4 (Gargalos)** reaproveita o diagnóstico DPC já pronto e dá base de medição para os itens "POR TESTE" (10, 17, 18).
- **Etapa 8 (nova): Catálogo expandido** reúne os itens ADICIONAR/OPCIONAL das seções 2 e 3, a página Navegadores, os novos reparos e o agendamento semanal.
- Itens da seção 4 ficam registrados aqui para não voltarem a ser discutidos.

## 7. Estimativa

Catálogo expandido: cerca de 30 ajustes novos entre registro, serviços e `netsh`/`bcdedit`/`powercfg`; reaproveita as classes `RegistryTweak`, `ServiceTweak`, `PowerSettingTweak` e o `RepairCatalog`. Precisa de novas classes só para `bcdedit` (com backup), `netsh` (estado atual) e IFEO por executável. Testes em Core cobrem estado/aplicar/desfazer com registro simulado; o efeito real no Windows só se confirma na sua máquina.
