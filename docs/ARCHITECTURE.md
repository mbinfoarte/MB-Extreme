# Arquitetura

## Visão geral

```
[MBExtreme.App (WPF)]  -->  [MBExtreme.Core]  -->  [Windows: memória, rede, serviços, registro]
   Pages / Controls            Memory, Dns,
   ProgressPanel               Progress, Settings
```

## Decisão de stack

**.NET 8 + WPF.** Motivos: interface escura com anéis, brilho e animações é direta em WPF; o acesso a registro, serviços, rede e APIs do Windows é nativo; o instalador e a publicação são simples. Componentes de alto desempenho para rotas (fase 2) podem ser separados depois, se necessário.

## Projetos

| Projeto | Responsabilidade |
|---|---|
| `MBExtreme.Core` | Lógica sem interface. `Memory` (status e limpeza), `Dns` (benchmark, aplicar, restaurar), `Progress` (eventos), `Settings` (JSON em %AppData%) |
| `MBExtreme.App` | Interface: tema, janela principal com menu lateral, páginas e a janela de progresso padrão |
| `MBExtreme.Core.Tests` | Testes de unidade do que não depende de Windows |

## Janela de progresso padrão

Toda tarefa longa publica `ProgressEvent(etapa, porcentagem, mensagem)` por `IProgress<T>` e aceita `CancellationToken`. O componente `ProgressPanel` exibe etapas, anel de porcentagem, status e o botão Parar. Novas funções só precisam publicar eventos.

## Princípios

- Todo ajuste do sistema é **reversível**, com valor original guardado antes de aplicar.
- Administrador só quando necessário. Hoje o app inteiro pede elevação (simplificação do MVP); o plano é separar um serviço auxiliar elevado.
- Telemetria **opt-in**, sem dados pessoais.
- Sem alterar arquivos de jogos nem interferir em anti-cheat.

## Decisões em aberto

- Roteamento (fase 2): WinDivert vs túnel UDP/WireGuard para relays.
- Infraestrutura de relays e regiões.
- Modelo de IA: começar com regras e estatística; modelo treinado só após coletar dados com consentimento.
- Instalador: MSIX, WiX (MSI) ou Inno Setup.
