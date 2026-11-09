# Especificação funcional (rascunho v1)

Base: lista de funcionalidades fornecida pelo dono do projeto. Cada item será implementado do zero, de forma **reversível**, com **snapshot antes de aplicar** e botão de restaurar.

Legenda de risco: **B** baixo, **M** médio (depende do hardware/uso), **A** alto (efeito colateral relevante).

## 1. Ajustes de Sistema

**Perfis:** `Padrão` (nada alterado), `Alto desempenho` (conjunto recomendado, risco B/M) e `Personalizado` (usuário escolhe cada item). Cada ajuste mostra descrição, efeito esperado, nota de quando não usar, se exige reiniciar e se exige administrador.

| # | Ajuste | Mecanismo previsto | Risco | Observação |
|---|---|---|---|---|
| 1 | Potenciador de Unreal Engine (Beta) | Perfil por jogo que ajusta arquivos de configuração do jogo (com backup) | M | Beta. Abrir o jogo ao menos uma vez para gerar a configuração. Não tocar em jogos com anti-cheat que verifica arquivos |
| 2 | Alto desempenho da GPU (Beta) | Preferência de GPU por aplicativo do Windows + plano de energia + (em versão futura) perfil do driver | M | Mais consumo e calor. Notebooks: avisar |
| 3 | Reduzir input lag | Desativar otimizações de tela cheia por jogo, Modo de Jogo, HAGS quando compatível | B/M | HAGS exige reiniciar e GPU/driver compatíveis |
| 4 | Reduzir tempo de entrada do teclado | Atraso e velocidade de repetição de teclas nas configurações do usuário | B | Efeito perceptível em digitação, não em FPS |
| 5 | Desligar contadores de desempenho do Windows | Não implementar no MVP | A | Pouco ganho e quebra ferramentas de monitoramento (inclusive as nossas). Reavaliar depois |
| 6 | Desempenho máximo para jogos | Ajustes de prioridade de CPU/GPU do agendador multimídia para jogos | M | Valores documentados e reversíveis |
| 7 | Prioridade mínima para processos de fundo | Baixar a prioridade de processos de fundo conhecidos **enquanto o jogo roda** | B | Lista de processos protegidos; desfaz ao fechar o jogo |
| 8 | Desligar economia de energia da rede | Desativar economia de energia nos adaptadores de rede | B/M | Notebook: mais bateria. Pode exigir reiniciar o adaptador |
| 9 | Desligar gravações de fundo da Game Bar | Chaves de captura do Game DVR | B | Perde o "gravar o que aconteceu" |
| 10 | Manter todos os núcleos ativos | Desativar o estacionamento de núcleos no plano de energia ativo | B/M | Mais consumo em ociosidade |
| 11 | Desligar registro de último acesso do NTFS | Comando do sistema de arquivos | B | Ganho pequeno; alguns backups/auditorias usam essa data |
| 12 | Desligar SuperFetch (SysMain) | Parar e desativar o serviço | M | Só oferecer em SSD. Em HDD tende a piorar |
| 13 | Desligar indexação de arquivos | Parar e desativar o serviço de busca | M | Buscas no Windows ficam mais lentas |
| 14 | Modo de desempenho máximo | Ativar plano de energia de desempenho máximo (criar se não existir) | B/M | Mais consumo |

Observação honesta: ganhos de FPS de vários desses ajustes são pequenos e variam por máquina. O app deve **medir antes e depois** (FPS/frametime/latência) e mostrar o resultado real, em vez de prometer números.

## 2. Limpeza de RAM

- **Status:** Boa / Precisa de limpeza / Crítica, calculado pela memória livre e pela lista de espera. Mostra usada, livre e total.
- **Ação Limpar:** esvazia a lista de espera e reduz o working set dos processos; relata quanto foi liberado.
- **Automação** (desativada por padrão). Executar quando: (a) uma conexão de jogo começa, e/ou (b) o status muda para "Precisa de limpeza", com intervalo mínimo configurável (padrão: 1 vez a cada 12 horas).
- **Lista de permissões:** aplicativos que nunca são tocados; botão "Adicionar aplicativo".
- Aviso: esvaziar a lista de espera tira cache útil; só vale a pena com a RAM quase cheia. Por isso a automação por status usa limiar, não roda toda hora.

## 3. DNS Optimizer

- **Teste:** mede o tempo de resposta médio de vários provedores (AdGuard, NextDNS, Control D, Google e outros), com várias consultas por provedor, e compara com a configuração atual do Windows. Mostra data/hora da última medição.
- **Ações:** `Escolher o mais rápido`, `Escolher` por provedor e `Restaurar padrão do Windows` (volta ao DNS automático/DHCP salvo antes).
- Aplicação: nos adaptadores ativos, com o DNS anterior guardado (IPv4 e IPv6).
- **Privacidade:** alguns provedores filtram anúncios ou conteúdo. Mostrar o tipo (com ou sem filtro) e deixar o usuário escolher. Opção futura: DNS criptografado (DoH).
- Cuidados: o melhor DNS varia com horário e região; repetir o teste periodicamente. Conferir resposta válida e não só tempo.

## Pontos em aberto

- A lista recebida terminou no provedor Google (4º). Confirmar se havia mais itens ou seções.
- Confirmar a stack do MB Extreme para definir como cada mecanismo será codificado.
- Definir quais ajustes entram no MVP: sugestão 3, 4, 7, 8, 9, 10, 14 e as seções 2 e 3.
