# Estudo de referência: pacote "Lobo Alpha — Ultimate Performance"

Origem: pasta do Google Drive do dono do projeto (scripts `.bat` e `.reg` de otimização do Windows).
**Uso:** estudo das técnicas e da organização. O MB Extreme reimplementa tudo do zero, com código próprio, **reversível** e com confirmação (veja `CLEAN-ROOM.md`). Nenhum script é copiado nem executado pelo app.

## Estrutura do pacote

| Pasta | Conteúdo observado |
|---|---|
| `.1. Criar Ponto de Restauração` | Backup do registro (`Backup Regedit.bat`) e criador de ponto de restauração |
| `1 - .Bats` | Ajustes por componente: Windows, CPU, Memória RAM, SSD, HDD |
| `2 - Otimização do Hardware` | Prioridade de CPU (perfis Intel e AMD) via registro |
| `3 - Otimização Desempenho Windows` | Agendar `chkdsk`, `DISM` restaurar saúde, `sfc /scannow`, atualizar drivers do sistema, plano "Super Desempenho" |
| `4 - Regedits` | Desativar Game DVR, SmartScreen, SysMain (Superfetch), acelerar abertura de pastas; subpasta de RAM |
| `5 - Debloater` | Remover apps do Windows e **reverter** (ótimo: tem desfazer), desativar anúncios e sugestões |
| `6 - Periféricos`, `7 - Driver de Vídeo` | Ajustes de periféricos e de driver de vídeo (AMD, NVIDIA, Intel) |
| `9 - Redes` | MTU, desativar IPv6, índice de estrangulamento de rede, DNS (Google), remover limitador QoS |
| `10 - Planos de Energia` | Vários planos (Gaming, Highest Performance, Revision, OS) |
| `12 a 16` | Personalização, correção de erros, navegadores, otimizações por jogo, rotina semanal (limpeza completa, "arrumar Windows") |
| `Bônus - Programas` | Programas de terceiros (DDU, NetLimiter, DNS Jumper, OpenHardwareMonitor, Optimizer). **Não usamos nem redistribuímos** |

## Técnicas identificadas e como o MB Extreme trata cada uma

| Técnica no pacote | O que faz | Risco | Tratamento no MB Extreme |
|---|---|---|---|
| Prioridade de jogos no agendador multimídia (perfil `Games`: prioridade de CPU e GPU, categoria de agendamento alta) | Dá mais prioridade ao processo do jogo | Baixo | **Sim**, como ajuste "Desempenho máximo para jogos", com restauração do valor original |
| Índice de estrangulamento de rede desativado | Remove limite de pacotes multimídia | Baixo/Médio (benefício discutível) | Opcional, desligado no perfil padrão; medir antes e depois |
| Desativar SysMain (Superfetch) | Libera RAM e disco em alguns casos | Médio | Só oferecer em SSD, com aviso |
| Desativar Game DVR / Game Bar | Recupera recursos | Baixo | **Sim** |
| Desativar anúncios e sugestões do Windows | Privacidade e menos ruído | Baixo | **Sim**, em "Privacidade" |
| Desativar Copilot e Cortana por política | Menos processos | Baixo | Opcional |
| Limpeza de temporários (pastas Temp, Prefetch, histórico, cookies, recentes) | Libera espaço | **Médio**: apagar Prefetch/cookies afeta o início de programas e deslogar sites | Limpar com **prévia do que será apagado e do tamanho**; cookies e histórico só com confirmação; Prefetch fora do padrão |
| Ajustar MTU para 1400 em todas as interfaces | Reduz fragmentação em algumas conexões | **Alto/Médio**: pode piorar conexões que usam MTU maior | **Não aplicar às cegas.** Descobrir o MTU ideal por teste (pacote com "não fragmentar") e só então sugerir |
| Desativar IPv6 | Resolve problemas pontuais | Médio (quebra serviços que dependem de IPv6) | Opcional e reversível |
| DNS fixo (Google) em todas as interfaces | Troca de DNS | Baixo | Já coberto pelo módulo **DNS** (teste, escolha e restaurar) |
| `chkdsk` agendado, `DISM /RestoreHealth`, `sfc /scannow` | Reparo do sistema | Baixo (demorado) | Ferramenta de **Reparo do Windows**, com a janela de progresso padrão |
| Criar ponto de restauração e backup do registro antes de alterar | Segurança | n/a | **Obrigatório** antes de qualquer ajuste (princípio do projeto) |
| Remover apps do Windows (Loja, Xbox, Fotos, Câmera, OneDrive, Calculadora...) de uma vez | "Debloat" | **Alto**: sem a Loja e o Xbox, jogos do Game Pass e muitos apps deixam de funcionar; difícil de reverter | **Lista de apps com escolha individual**, sem remover Loja e Xbox por padrão, com backup e aviso do que depende de cada um |
| Plano de energia "Super Desempenho" | Máximo desempenho | Baixo | **Sim**, criando o plano se não existir |
| Prioridade da CPU por perfil Intel/AMD | Ajuste de agendador | Baixo/Médio | Detectar a CPU e aplicar só o que se aplica |

## Observações

- O pacote tem **desfazer** para o debloat. Esse padrão (todo ajuste com reverso) é o que o MB Extreme adota para tudo.
- Vários ajustes são "dicas populares" cujo ganho varia por máquina. O MB Extreme deve **medir antes e depois** e mostrar o resultado real, em vez de prometer ganho.
- Os scripts desativam recursos de segurança em alguns casos (ex.: SmartScreen). O MB Extreme **não** desativa Defender, SmartScreen, UAC nem Windows Update.
- Programas de terceiros que acompanham o pacote têm licenças próprias (alguns pagos). Não os incluímos no produto.

## Próximos passos

- ~~Ler o restante dos scripts~~ Feito. A análise completa, com decisão por item, está em `ANALISE-LOBO-ALPHA.md`.
- Transformar a tabela em itens da `ESPECIFICACAO-FUNCIONAL.md` (ajustes de sistema) e no roadmap.
