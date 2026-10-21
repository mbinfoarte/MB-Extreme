# Arquitetura

## Visão geral

```
[UI React/Tauri] <-> [Core Rust] <-> [Windows: rede, serviços, registro]
                         |
                         +-> [AI: decisão de rota/ajustes]
                         +-> [Relays MB (servidores próprios) - fase 2]
```

## Módulos

| Módulo | Responsabilidade | Fase |
|---|---|---|
| `core::probe` | Medir latência, jitter e perda (ICMP/UDP) para servidores de jogos e relays | 1 |
| `core::sysopt` | Ajustes reversíveis do Windows (plano de energia, serviços, startup, DNS) | 1 |
| `core::route` | Escolha de rota: direta vs relay, multi-caminho | 2 |
| `ai` | Recomendação/aplicação de ajustes com base em histórico | 3 |
| `apps/desktop` | Dashboard, perfis por jogo, logs, configurações | 1 |
| `installer` | MSI/EXE, assinatura de código, auto-update | 1 |

## Decisões em aberto

- Roteamento: **WinDivert** (captura em modo usuário, exige driver assinado) vs **túnel UDP/WireGuard** para relays.
- Infraestrutura de relays: provedores de nuvem e regiões prioritárias (Brasil, EUA, Europa).
- Modelo de IA: começar com regras + estatística; modelo treinado só após coletar dados com consentimento.

## Princípios

- Todo ajuste do sistema é **reversível** (snapshot antes de aplicar, botão de restaurar).
- Privilégios de administrador solicitados só quando necessário.
- Telemetria **opt-in**, sem dados pessoais.
- Sem alterar arquivos de jogos nem interferir em anti-cheat.
