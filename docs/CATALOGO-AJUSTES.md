# Catálogo de ajustes e organização do menu

Consolida o que foi estudado no pacote "Lobo Alpha — Ultimate Performance" (veja `REFERENCIA-LOBO-ALPHA.md`), nas especificações e nas decisões do projeto. Tudo é reimplementado do zero, **reversível** e com aviso de risco.

Legenda de estado: **Feito** (está no app), **Planejado** (decidido, falta implementar), **Não faremos**.
Legenda de risco: **B** baixo, **M** médio, **A** alto. Ajustes M e A nunca entram em "Marcar recomendados".

> **Atualização (Etapa 8).** Implementados: TRIM, último acesso, 8.3, hibernação, RSS, RSC, TCP Nodelay, energia dos adaptadores, FSE, SystemResponsiveness, MPO, ULPS/telemetria/Crash Defender (AMD), telemetria (NVIDIA), timer e hipervisor (bcdedit, risco alto), teclado, políticas de Chrome/Edge/Brave, limpeza de cache de shaders, limpeza semanal e novos reparos (áudio, Bluetooth, Wi-Fi, Explorer, Xbox, antitrapaça, desligamento, logs de eventos, DNS). As tabelas abaixo mantêm o texto original; o que está nesta nota vale mais.

## Princípios

1. Antes de mudar um valor, o app **guarda o original** (`%AppData%\MBExtreme\tweak-backup.json`). "Restaurar originais" desfaz.
2. Ações grandes (remover aplicativos) criam um **ponto de restauração** do Windows antes.
3. Cada ajuste mostra descrição, efeito esperado, **atenção** (o que se perde) e se exige reiniciar.
4. **Medir antes e depois** sempre que possível; o ganho de vários ajustes varia por máquina.
5. Segurança: o app **não desativa o Defender, o UAC nem o Windows Update**. O **SmartScreen** do Explorador pode ser desativado por escolha do usuário (risco alto, com confirmação), porque a decisão é do dono do PC.

## Estrutura do menu

| Grupo | Item | Conteúdo |
|---|---|---|
| (topo) | **Início** | Análise rápida (somente leitura) |
| OTIMIZAR | **Desempenho** | Prioridade de CPU, plano de energia, serviços |
| | **Jogos e GPU** | Agendador multimídia, Game DVR, HAGS, drivers; por jogo (planejado) |
| | **Memória** | Status, limpeza, aplicativos protegidos |
| | **Armazenamento** | SSD e HD, limpeza, logs (planejado) |
| REDE | **Otimizações de rede** | TCP, QoS, throttling, IPv6 |
| | **DNS** | Teste, ranking, aplicar, restaurar |
| | **Analisador de rede** | MTR, Ping, TCPing, monitoramento (planejado) |
| WINDOWS | **Privacidade** | Anúncios, histórico, Copilot, Cortana, SmartScreen |
| | **Aplicativos** | Remover apps do Windows, um por um |
| | **Inicialização e boot** | Inicialização rápida; bcdedit e programas de inicialização (planejado) |
| | **Periféricos** | Mouse e teclado |
| FERRAMENTAS | **Hardware e benchmarks** | Informações, sensores, stress, benchmarks (planejado) |
| | **Drivers e programas** | Fontes oficiais e winget (planejado) |
| | **Central de ações** | Histórico e desfazer (planejado) |

## Ajustes

### Desempenho
| Ajuste | Risco | Estado | Observação |
|---|---|---|---|
| Prioridade de CPU para o primeiro plano (`Win32PrioritySeparation` = 38) | B | Feito | Recomendado |
| Plano de energia Desempenho Máximo | B | Feito | Cria o plano; ao restaurar volta ao anterior e apaga o criado |
| Impedir apps da Loja em segundo plano | B | Feito | Recomendado |
| Telemetria (DiagTrack) e Mapas (MapsBroker) sob demanda | B | Feito | Recomendado |
| Desativar SysMain (Superfetch) | M | Feito | Só em SSD |
| Desativar indexação (Windows Search) | M | Feito | Busca fica lenta |
| Desativar spooler de impressão | M | Feito | Sem impressora |
| Desbloquear atributos ocultos de energia, núcleos sempre ativos | M | Planejado | `powercfg` |
| Desativar hibernação (`powercfg -h off`) | M | Planejado | Remove o arquivo hiberfil |

### Jogos e GPU
| Ajuste | Risco | Estado | Observação |
|---|---|---|---|
| Game DVR / gravação em segundo plano | B | Feito | Recomendado |
| Agendador multimídia, perfil "Games" (CPU, GPU, disco com prioridade alta) | B | Feito | Recomendado |
| Desativar HAGS | M | Feito | Perde Frame Generation; reiniciar |
| Impedir Windows Update de trocar drivers | M | Feito | |
| Desativar MPO (Multiplane Overlay) | M | Planejado | Intel/NVIDIA/AMD |
| AMD: desativar ULPS, forçar shader cache | M | Planejado | Só em GPU AMD |
| NVIDIA: limpar shader cache, desativar ShadowPlay | B/M | Planejado | Só em GPU NVIDIA |
| Limpar cache de shaders (todas as marcas) | B | Planejado | Resolve travadas após trocar de driver |
| Prioridade de CPU por perfil Intel/AMD | M | Planejado | Detectar a CPU |
| Otimizações por jogo (Valorant, CS2, Fortnite, Minecraft, Call of Duty) | M | Planejado | Scripts grandes ainda não lidos; virarão perfis por jogo com backup |
| Fix: antitrapaça bloqueando jogo (reativar Defender e mitigações) | B | Planejado | Em "Reparo" |

### Rede
| Ajuste | Risco | Estado | Observação |
|---|---|---|---|
| Remover limite de pacotes do agendador multimídia | B | Feito | Ganho varia |
| Remover limite de banda do QoS | B | Feito | Recomendado |
| Desativar IPv6 | M | Feito | Reiniciar |
| TCP NoDelay / AckFrequency por interface | M | Planejado | Mexe em cada adaptador |
| Desativar TCP Coalescing (RSC), habilitar RSS | B/M | Planejado | `netsh int tcp` |
| TCP Auto-Tuning | M | Planejado | O pacote usa "highlyrestricted"; o padrão "normal" costuma ser melhor. Testar antes |
| MTU | **A** | Planejado | **Só por teste** (pacote sem fragmentação), nunca fixo em 1400 às cegas |
| Desativar economia de energia dos adaptadores | B/M | Planejado | |
| Reset de rede (winsock e pilha IP) | M | Planejado | Em "Reparo"; derruba a conexão por instantes |
| Limpar e renovar DNS | B | Planejado | Botão no módulo DNS |

### Memória
| Ajuste | Risco | Estado | Observação |
|---|---|---|---|
| Limpar working sets e lista de espera | B | Feito | |
| Esvaziar a lista de páginas modificadas | B | Planejado | Equivalente nativo do que o `EmptyStandbyList.exe` do pacote faz (não usaremos o .exe) |
| Ajustes de Memory Management e cache de arquivos por quantidade de RAM (2, 6, 8 GB) | M | Planejado | Ler os `.reg` antes |
| Limpeza automática (ao iniciar jogo, por status) | B | Planejado | |

### Armazenamento
| Ajuste | Risco | Estado | Observação |
|---|---|---|---|
| Verificar e habilitar TRIM (SSD) | B | Planejado | |
| Desativar último acesso do NTFS | B | Planejado | |
| HD: tempo de inatividade, desfragmentação automática | B | Planejado | Só em HD |
| Limpar logs de eventos | M | Planejado | Apaga rastros úteis para diagnóstico; com confirmação |
| Limpeza de temporários, cache de navegadores | B/M | Planejado | Com prévia e tamanho |
| Otimizar navegadores (Chrome, Brave, Opera, Edge) | B | Planejado | Pasta 14, ainda não lida |

### Privacidade
| Ajuste | Risco | Estado | Observação |
|---|---|---|---|
| Anúncios e sugestões do Windows | B | Feito | Recomendado |
| Histórico de programas e documentos recentes | B | Feito | |
| Copilot, Cortana | B | Feito | |
| SmartScreen do Explorador | **A** | Feito | Confirmação obrigatória; nunca recomendado |

### Aplicativos (Debloat)
Feito: lista de 25 aplicativos em 3 grupos (**Seguros**, **Cautela**, **Perigosos**: Xbox e Loja), seleção individual, ponto de restauração antes, confirmação reforçada para os perigosos.
Planejado: reinstalar apps removidos (winget/Loja), remover pacotes provisionados para novos usuários.

### Inicialização e boot
| Ajuste | Risco | Estado | Observação |
|---|---|---|---|
| Desativar inicialização rápida | B | Feito | Reiniciar |
| Programas de inicialização (listar, desativar, restaurar) | B | Planejado | |
| Timer do sistema (`bcdedit`: `useplatformtick`, `disabledynamictick`, `tscsyncpolicy`) | M/A | Planejado | Antes: `bcdedit /export` de backup; resultado depende da CPU |
| Tempo do menu de boot | B | Planejado | `bcdedit` |

### Periféricos
| Ajuste | Risco | Estado | Observação |
|---|---|---|---|
| Aceleração do mouse | B | Feito | Recomendado |
| Teclas de aderência, filtro e alternância | B | Feito | Recomendado; avisa quem usa acessibilidade |
| Tamanho da fila de dados de mouse e teclado (perfis "Fraco, Mediano, Bom") | M | Planejado | Ler os `.reg` antes |

### Reparo do Windows (ferramenta, não ajuste)
Planejado: agendar `chkdsk`, `DISM /RestoreHealth`, `sfc /scannow`, atualizar drivers do sistema, loja que não baixa, PC que não desliga, tela preta após o boot, notebook preso no modo avião. Cada um com a janela de progresso padrão.

## O que não faremos
- Desativar o Windows Defender, o UAC ou o Windows Update.
- Executar os `.exe` e `.lnk` do pacote (`EmptyStandbyList.exe`, "Criar Ponto de Restauração.exe", "LIMPAR SSD") nem os programas de terceiros da pasta "Bônus"; fazemos equivalentes nativos.
- Importar os planos de energia `.pow` do pacote; criamos o plano pelo próprio Windows.
- Remover a Loja ou o Xbox sem confirmação reforçada.

## O que ainda não foi lido do pacote
> Atualização: o pacote foi lido por inteiro. Veja `ANALISE-LOBO-ALPHA.md` (decisão por item) e a Etapa 8 do `PLANO-ETAPAS.md`. O texto abaixo é histórico.

Os nomes e tamanhos de todas as pastas foram mapeados, mas o conteúdo de vários arquivos ainda não foi lido: scripts por jogo, navegadores, periféricos (`.reg`), RAM por quantidade, `Aumentar Prioridade de jogos` (51 KB), drivers de vídeo e as páginas seguintes de algumas pastas. A tabela acima será completada à medida que forem lidos.

## Ajustes do plano MB Optimizer (Fase 5, 06/11/2026)

| Id | O que faz | Valor | Fonte | Versões |
|---|---|---|---|---|
| priv-recall | Desativa o Recall (salvamento de instantâneos) | `HKLM`/`HKCU\Software\Policies\Microsoft\Windows\WindowsAI` `DisableAIDataAnalysis=1` | Microsoft Learn, Policy CSP – WindowsAI › DisableAIDataAnalysis | Windows 11 24H2+ (build 26100) |
| priv-widgets | Desativa os Widgets | `HKLM\SOFTWARE\Policies\Microsoft\Dsh` `AllowNewsAndInterests=0` | Microsoft Learn, Policy CSP – NewsAndInterests › AllowNewsAndInterests | Windows 11 (22000+) |
| priv-news-interests | Desativa Notícias e interesses | `HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Feeds` `EnableFeeds=0` | Política "Habilitar notícias e interesses na barra de tarefas" (Modelos Administrativos) | Windows 10 |
| priv-lockscreen-tips | Sem dicas na tela de bloqueio | `HKCU\...\ContentDeliveryManager` `RotatingLockScreenOverlayEnabled=0`, `SubscribedContent-338387Enabled=0` | Valores gravados pela opção de Configurações › Personalização › Tela de bloqueio | Windows 10 e 11 |
| priv-start-promos | Sem recomendações e avisos de conta no Iniciar | `HKCU\...\Explorer\Advanced` `Start_IrisRecommendations=0`, `Start_AccountNotifications=0` | Valores gravados pelas opções de Configurações › Personalização › Iniciar | Windows 11 22H2+ |
| priv-explorer-ads | Sem notificações do provedor de sincronização | `HKCU\...\Explorer\Advanced` `ShowSyncProviderNotifications=0` | Opção de Opções de Pasta › Modo de exibição | Windows 10 e 11 |
| visual-taskbar-small | Barra de tarefas menor | `HKCU\...\Explorer\Advanced` `TaskbarSmallIcons=1` | Opção "Usar botões pequenos da barra de tarefas" das Configurações | Só Windows 10 (o 11 não tem a opção nativa) |

O programa confere a build do Windows (`Tweak.MinBuild`/`MaxBuild`) e esconde o ajuste onde ele não existe.
