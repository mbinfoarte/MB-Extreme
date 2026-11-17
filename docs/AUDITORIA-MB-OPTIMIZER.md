# Auditoria para o MB Optimizer (Fase 0)

Data: 06/11/2026. Base auditada: repositório `mbinfoarte/MB-Extreme`, branch `claude/elegant-bohr-w9chcw`, commit `6653797`.
O repositório `mbinfoarte/otmz` foi conferido de novo nesta data e **continua vazio** (nenhum commit). Portanto, o código que existe de verdade
para um otimizador de Windows da MB Infoarte é o do MB Extreme, e é ele que esta auditoria descreve.

Nada foi alterado no código para esta auditoria.

## 1. Arquitetura atual

| Item | Situação |
|---|---|
| Linguagem e plataforma | C# 12, .NET 8, Windows 10/11 x64 |
| Interface | WPF; páginas montadas por código (`ToolPageBase` com helpers `Heading`, `CardOf`, `Chips`, `Field`...) e algumas em XAML. Tema preto com amarelo `#FFC400` |
| Projetos | `MBExtreme.Core` (regras e acesso ao Windows, ~25 mil linhas em 33 áreas), `MBExtreme.App` (interface, ~55 páginas), `MBExtreme.Installer` (instalador próprio), `MBExtreme.LicenseServer` (servidor de licenças), `tools/MBExtreme.IndexTool` |
| Acesso ao Windows | Registro (`Microsoft.Win32`), serviços (`sc`), `powercfg`, `bcdedit`, PowerShell, WMI/CIM (`System.Management`), contadores de desempenho, LibreHardwareMonitor (sensores), DXGI/Direct3D (Vortice), NAudio, winget |
| Testes | `tests/MBExtreme.Core.Tests`: 55 arquivos, **1116 testes passando**. Cobrem as regras puras do Core; as chamadas reais ao Windows não são testadas automaticamente |
| Entrega | GitHub Actions compila, testa, gera instalador e portátil e publica um *pre-release* `beta-N` a cada push |
| Licença | Plano grátis e Pro (servidor de licenças, teste de 3 dias, afiliados) |

**Padrões já existentes que o plano pede:**
- **Ajustes reversíveis:** `Tweak` (ler estado → aplicar → restaurar). Os valores originais ficam guardados em `%AppData%\MBExtreme\tweak-backup.json` (`TweakStore`). Há tipos para registro, serviço, comando, plano de energia e opção de energia.
- **Risco declarado:** `TweakRisk` (baixo, médio, alto), categoria e um texto "quando não usar".
- **Execução em fila:** `TweakRunner` aplica um a um com progresso, e a falha de um não interrompe os outros. `RepairRunner` mostra a saída ao vivo e permite cancelar.
- **Ponto de restauração:** `RestorePointService`.
- **Comandos seguros:** os processos são abertos com `ArgumentList`, sem concatenar texto vindo do usuário.

**O que não existe:** um núcleo único de operações. Cada módulo tem o seu próprio executor. Não há estados padronizados nem histórico geral, e não há como retomar operações interrompidas.

## 2. Inventário: o plano frente ao que já existe

Legenda: **Feito** = existe e funciona (dentro do testado); **Parcial** = existe, mas falta parte do que o plano pede; **Ausente** = não existe.

| Módulo do plano | Situação | O que já existe | O que falta |
|---|---|---|---|
| 1. Painel inteligente | Parcial | `DashboardPage` com "itens que precisam de atenção" (`AttentionAdvisor`), segurança, rota de rede | Análise rápida e profunda com etapas, tempo decorrido e cancelamento; seleção individual de problemas; "Corrigir selecionados/tudo" em fila; relatório antes/depois; histórico; exportação |
| 2. Reparação Windows | Feito / parcial | `RepairCatalog` + `RepairRunner`: DISM CheckHealth/ScanHealth/RestoreHealth, SFC (verificar ou reparar), CHKDSK/Repair-Volume por disco, saída ao vivo, cancelamento, interpretação dos códigos | Diagnóstico de data/hora e de Windows Update; limites de tempo configuráveis; tratamento explícito de reinício pendente |
| 3. Limpeza | Parcial | `CleanupCatalog`/`CleanupService` (temporários, caches de navegador sem cookies/senhas, ignora junções), rotina semanal agendada, Lixeira | Pré-visualização arquivo a arquivo; exclusões configuráveis; separar "em uso" de erro; limpeza de componentes do Windows Update e de "Windows.old" pelo método oficial; relatório do espaço recuperado de fato |
| 4. Saúde dos discos | Parcial | `StorageService` (saúde, otimização com `defrag /O`, que escolhe TRIM ou desfragmentação), `DiskManager` completo (partições, formatar, letra, reduzir/estender), `DriveMap` | Leitura SMART detalhada (temperatura, horas, desgaste); verificação explícita do TRIM e da otimização agendada; estado da Proteção do Sistema por unidade; histórico por unidade |
| 5. Otimizações | Feito / parcial | 49 ajustes declarativos em `TweakCatalog` (inclui Copilot por política), perfis prontos (`TweakProfiles`), reversão individual | Faltam alguns dos itens pedidos: Recall, Widgets, propaganda do Iniciar, da tela de bloqueio e do Explorador, barra de tarefas menor. Faltam também a verificação de compatibilidade por versão/build do Windows e os grupos Recomendados/Seguros/Moderados/Avançados/Todos (hoje são 3 níveis de risco) |
| 6. Pós-formatação | Ausente | As peças existem soltas: remoção de apps (`BloatCatalog`), ajustes, programas recomendados (winget), ponto de restauração | O assistente em 8 passos com os perfis Leve/Equilibrado/Extremo e o relatório final |
| 7. Programas | Parcial | `WingetService` (atualizar/instalar), `AppRemovalService` (Store/Appx), `ProgramsPage`, catálogo de recomendados | Inventário unificado Win32 + MSIX com ícones; atualização em fila com bloqueio entre instâncias; detecção do programa aberto antes de atualizar; mensagens de erro do winget em português; histórico |
| 8. Hardware | Feito | `SensorService` (LibreHardwareMonitor), monitor de componentes, gráficos, informações do sistema, drivers, BIOS, ventoinhas, teste de estresse, benchmark | Exportação de diagnóstico completo em um arquivo |
| 9. Periféricos | Ausente | Só a latência do sistema (`LatencyPage`) | Testes de teclado, mouse e controles (botões, gatilhos, analógicos, drift, zona morta, vibração) |
| 10. Internet | Parcial | Monitor de rede, análise de rota, bufferbloat, teste de envio (upload), DNS, Wi-Fi | Teste de download com várias conexões e aquecimento; servidor e cidade; histórico comparativo; relatório |
| 11. Interface | Parcial | Navegação com Voltar/Início, menu agrupado, layout responsivo no estúdio | **Acessibilidade: nenhum `AutomationProperties` no app** (leitores de tela não leem os botões); Alt+← e o botão lateral do mouse; escala de interface; estados de foco visíveis em todos os controles |
| 12. Central Desfazer | Parcial | Cada ajuste se desfaz individualmente; o backup sobrevive ao reinício; a Lixeira na limpeza | Histórico único de **todas** as alterações (ajustes, serviços, apps removidos, limpezas) com data, seleção múltipla para restaurar e lista do que não tem volta |
| Núcleo de operações | Ausente | Executores separados por módulo | Contrato `analyze/plan/execute/cancel/rollback`, estados PENDING…REBOOT_REQUIRED, log de auditoria, retomada após falha |

## 3. Problemas técnicos encontrados

1. **Sem núcleo comum.** Ajustes, reparos, limpeza e winget têm cada um o seu executor, com formatos de progresso e resultado diferentes. Isso dificulta o "Corrigir tudo", o histórico e a Central Desfazer.
2. **Acessibilidade zero.** Nenhum controle tem nome acessível, e os botões desenhados com ícones vetoriais ficam mudos para o Narrador.
3. **Sem bloqueio de segunda instância.** Não há Mutex: dá para abrir o programa duas vezes e rodar dois winget ou dois reparos ao mesmo tempo.
4. **Compatibilidade por versão do Windows não é verificada** antes de aplicar um ajuste. Ajustes que só existem no Windows 11 podem ser gravados no Windows 10 sem efeito.
5. **Testes só das regras.** As chamadas reais ao Windows (registro, serviços, DISM, winget) não têm teste automático. O plano pede testes em máquina virtual, que ainda não existem.
6. **Duas linhas de produto no mesmo código.** O MB Extreme é Pro/pago e inclui estúdio, jogos e ventoinhas. O plano pede o selo "Licença: Grátis". É preciso decidir se o MB Optimizer é um produto separado ou um modo do MB Extreme (ver seção 7).

## 4. Plano de alterações (resumo por fase)

| Fase | Entrega | Reaproveita |
|---|---|---|
| 0 | Esta auditoria | — |
| 1 | Núcleo de operações + Central Desfazer + bloqueio de instância | `Tweak`, `TweakStore`, `TweakRunner`, `RepairRunner`, `RestorePointService` |
| 2 | Painel: análise rápida/profunda, seleção, "Corrigir selecionados/tudo", relatório e histórico | `AttentionAdvisor`, `SecurityService`, `StorageService`, `RepairCatalog` (só os passos de verificação) |
| 3 | Reparação: data/hora, Windows Update, tempos limite, reinício pendente | `RepairCatalog`, `RepairRunner` |
| 4 | Limpeza com pré-visualização e exclusões; discos com SMART, TRIM, otimização agendada e Proteção do Sistema | `CleanupService`, `StorageService` |
| 5 | Ajustes que faltam + grupos de seleção + compatibilidade por versão; assistente de pós-formatação | `TweakCatalog`, `TweakProfiles`, `BloatCatalog`, `RecommendedApps` |
| 6 | Programas: inventário unificado e atualização em fila | `WingetService`, `AppRemovalService` |
| 7 | Periféricos (novo) e internet (download, histórico) | `UploadTest`, `NetworkMonitor` |
| 8 | Acessibilidade, Alt+←, botão do mouse, escala | `ToolPageBase` (os helpers recebem o nome acessível uma vez e valem para todas as páginas) |
| 9 | Testes, empacotamento e beta | CI atual |

## 5. Dependências

- **Já usadas:** LibreHardwareMonitorLib, System.Management, System.Diagnostics.PerformanceCounter, Vortice (Direct3D/DXGI), NAudio, QRCoder e o winget do Windows.
- **Novas previstas (só se a fase precisar):** XInput ou Windows.Gaming.Input para os controles (Fase 7, já fazem parte do Windows). Nenhuma biblioteca paga.
- **Ferramentas do Windows:** DISM, SFC, CHKDSK/Repair-Volume, `defrag`, `vssadmin`/WMI para a Proteção do Sistema, `cleanmgr`/DISM StartComponentCleanup, `w32tm` (horário), `Get-PhysicalDisk`/`Get-StorageReliabilityCounter` (SMART).

## 6. Riscos

| Risco | Como reduzir |
|---|---|
| Ajuste de registro errado ou inexistente na versão do Windows | Cada ajuste novo com a fonte oficial (documentação Microsoft) anotada; checar a build antes de aplicar; nada de chaves "da internet" sem fonte |
| Reparos demorados (DISM até 30 min) parecendo travados | Saída ao vivo (já existe) + tempo decorrido + aviso de "pode demorar" |
| Limpeza apagar algo do usuário | Só as pastas do catálogo; pré-visualização; Lixeira quando possível; nunca Prefetch inteiro, cookies, senhas ou Documentos |
| Desfazer que não desfaz | A Central só oferece "restaurar" quando há backup verificável; o resto aparece como "sem volta" antes de aplicar |
| Quebrar o que já funciona no MB Extreme | Mudanças pequenas por fase; os 1116 testes rodando no CI a cada push; o núcleo novo envolve os executores atuais em vez de trocá-los |
| Testes sem Windows real | Daqui eu só compilo e testo as regras; a validação real precisa ser feita no seu PC ou numa máquina virtual com snapshot |

## 7. Decisões que dependem de você

1. **Onde o MB Optimizer vai morar:**
   - **(a) Recomendado:** como **novo aplicativo no mesmo repositório**, reaproveitando o `MBExtreme.Core`. As correções valem para os dois produtos, e o MB Optimizer sai grátis, sem estúdio, jogos nem Pro.
   - **(b)** Como um módulo "Otimizador" dentro do próprio MB Extreme.
   - **(c)** Copiar o Core para o repositório `otmz`. Não recomendo: duplica o código e as correções.
2. **Selo "Licença: Grátis":** vale só para o MB Optimizer (opção a) ou o MB Extreme também passa a ter um modo grátis?
3. **Os 24 ajustes do fórum:** preciso da lista com nomes. Hoje existem 49 ajustes no catálogo, e alguns dos citados (Recall, Widgets, propaganda do Iniciar e da tela de bloqueio) ainda faltam.

## 7.1 Decisões tomadas (06/11/2026)

- O MB Optimizer é o **próprio MB Extreme** (opção b): nada de produto separado.
- Selo **"Licença: Grátis"** no cartão do cliente enquanto não houver assinatura Pro; os recursos Pro continuam liberando só com a assinatura.
- Fase 1 aprovada.

## 8. Fase 1 — feita (06/11/2026)

- `src/MBExtreme.Core/Operations/`: `Operation.cs` (contrato, estados, risco), `OperationQueue.cs` (fila única), `OperationJournal.cs` (diário em `%AppData%\MBExtreme\diario`, um arquivo por dia, 90 dias), `TweakOperation.cs` (ajuste como operação reversível com verificação), `UndoCenter.cs` (o que dá e o que não dá para desfazer).
- `TweakRunner` passou a registrar cada ajuste no diário (as páginas atuais não mudaram); restaurar pela página conta como desfeito.
- Limpeza, remoção de aplicativos e reparos ficam registrados como "sem volta", com o motivo.
- Nova página **Central Desfazer** (Windows › Sistema e segurança).
- Uma janela só: abrir de novo traz a janela aberta para frente.
- Voltar com Alt+← e com o botão lateral do mouse.
- Selo "Licença: Grátis".
- Testes: `NucleoOperacoesTests` (9 testes) — fila em ordem e seguindo após falha, cancelamento, duas filas ao mesmo tempo, diário com linha cortada e operação interrompida, limpeza dos dias antigos, desfazer com verificação, desfazer que falha, regras da Central e o registro pelo `TweakRunner`.

Concluída depois: Jogos e energia, Diagnóstico de latência e Inicialização também entram no diário (como configuração, com a dica de como desfazer). Nada foi testado num Windows real ainda.

## 9. Fase 2 — Painel inteligente (06/11/2026)

- `src/MBExtreme.Core/Diagnostics/`: contrato de verificação (só leitura), `DiagnosticRunner` (etapa X de Y e tempo decorrido, sem porcentagem inventada; parar; "não verificável" separado de erro; a profunda reaproveita a rápida de até 10 minutos), histórico das 30 últimas análises, relatório técnico em texto, regras testáveis (`DiagnosticRules`).
- Verificações rápidas: espaço livre, temporários e cache, proteção do Windows, programas na inicialização, ajustes recomendados (só os do plano do usuário), TRIM, otimização automática dos discos, Proteção do Sistema, saúde SMART e reinício pendente.
- Verificações profundas, todas em modo "só verificar": DISM CheckHealth/ScanHealth, SFC /verifyonly e CHKDSK /scan do disco do Windows.
- Correções pela fila: limpeza, ajustes recomendados (com volta), ligar o TRIM, ligar a otimização agendada, ligar a Proteção do Sistema, reparos DISM RestoreHealth e SFC (sem volta, avisado antes). Ponto de restauração antes; acompanhamento item a item; no fim, nova análise rápida com "antes → depois".
- Página Painel inteligente (Otimização › Diagnóstico) e botão "Analisar o PC" no Início.
- Testes: `PainelInteligenteTests` (17).

Limitações: o resultado do DISM/SFC em modo verificação depende do `RepairInterpreter` atual; não foi conferido num Windows com a imagem realmente corrompida.

## 8.1 Proposta original da Fase 1

Objetivo: criar o núcleo comum **sem mudar o comportamento** de nada que já existe.

1. **Contrato de operação no Core:**
   - Interface `IOperation` com `Analyze`, `Plan`, `Execute`, `Cancel` e `Rollback` (opcional).
   - Metadados: id, risco, se precisa de administrador, se é reversível, tempo estimado e pré-condições.
   - Estados: `Pending`, `Running`, `Succeeded`, `Failed`, `Cancelled`, `Skipped`, `RebootRequired`.
2. **Fila de operações:** roda uma de cada vez, com progresso e cancelamento. Oferece ponto de restauração antes de alterar algo e não interrompe a fila se uma operação falhar.
3. **Adaptadores** para os executores atuais: ajustes (`Tweak`), reparos (`RepairStep`) e limpeza. As páginas atuais continuam iguais.
4. **Diário de operações:** um arquivo JSON por dia em `%AppData%`, com quem, o quê, quando, o resultado e o valor anterior. Ao abrir o programa, uma operação que ficou "Running" é marcada como interrompida.
5. **Central Desfazer (primeira versão):**
   - Lista as alterações do diário.
   - Permite restaurar várias de uma vez.
   - Verifica o estado depois de restaurar.
   - Mostra separado o que não tem volta.
6. **Bloqueio de segunda instância** (Mutex): a segunda janela só traz a primeira para frente.
7. **Testes:** estados e transições da fila, cancelamento, falha no meio, retomada após interrupção, restauração e diário corrompido.

Critério de pronto da Fase 1: todos os testes atuais e os novos passando no CI, as páginas atuais funcionando igual, e a Central Desfazer listando e restaurando os ajustes aplicados.


## 10. Fases 3 a 9 (06/11/2026)

| Fase | O que entrou | Testes |
|---|---|---|
| 3. Reparação | Relógio (w32tm, em qualquer idioma) e ano impossível; Windows Update (serviços desativados e dias sem atualização); tempo limite configurável dos comandos comuns — DISM, SFC e CHKDSK nunca são cortados, só avisados; aviso de reinício pendente | `ReparacaoFase3Tests` (com processo real) |
| 4. Limpeza e discos | "Ver arquivos" antes de limpar; pastas que nunca são limpas (inclusive na semanal); Prefetch fora do padrão; Windows.old só medido (apagar pela Limpeza de Disco do Windows); TRIM, otimização agendada, Proteção do Sistema por unidade e histórico SMART | `LimpezaDiscosFase4Tests` (com arquivos reais) |
| 5. Otimizações e pós-formatação | Recall, Widgets, Notícias e interesses, tela de bloqueio, Iniciar, Explorador, barra menor — com fonte em `CATALOGO-AJUSTES.md`; versão mínima/máxima do Windows por ajuste; grupos Recomendados/Seguros/Moderados/Avançados/Todos; assistente de pós-formatação | `OtimizacoesFase5Tests` |
| 6. Programas | Inventário (registro + Loja, com ícone e busca); desinstalação acompanhada; atualizações e instalações uma de cada vez pela fila; aviso de programa aberto; erros do winget em português (conferidos na documentação oficial) | `ProgramasFase6Tests` |
| 7. Periféricos e internet | Teclado, mouse (taxa pela entrada bruta, clique duplicado) e controle XInput (deriva, zona morta, circularidade, vibração); teste de internet com várias conexões, aquecimento, servidor e cidade, histórico | `PerifericosInternetFase7Tests` (servidor de mentira) |
| 8. Interface | Nome acessível automático em todos os controles; foco visível; escala 90–150% (Ctrl +/−/0) | `InterfaceFase8Tests` |
| 9. Revisão | Uma janela só espera 3 s a outra fechar (atualização/reinício); roteiro de testes no Windows (`ROTEIRO-TESTES-MB-OPTIMIZER.md`) | 1196 testes no total |

### O que NÃO foi possível verificar daqui
Tudo foi compilado e as regras foram testadas automaticamente, mas o ambiente de desenvolvimento não é Windows. Precisam de teste num Windows real ou numa máquina virtual com snapshot: as chamadas ao registro, serviços, DISM/SFC/CHKDSK, winget, contadores, XInput, entrada bruta do mouse, Narrador e a escala. O roteiro está em `docs/ROTEIRO-TESTES-MB-OPTIMIZER.md`.

### Limitações conhecidas
- Controles de PlayStation/Switch só aparecem no teste com um tradutor (Steam, DS4Windows): o teste usa XInput.
- O teste de internet usa a Cloudflare, não a Ookla: os números podem diferir do Speedtest.
- Desinstaladores que abrem outro processo e saem na hora podem ser dados como "desinstalado" antes de terminar.
- A "última vez que o programa foi aberto" não é mostrada (o Windows não guarda isso de forma confiável).
- A lista dos 24 ajustes do fórum não foi recebida: os ajustes citados por nome foram feitos; os demais dependem da lista.
