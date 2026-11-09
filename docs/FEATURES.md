# Matriz de funcionalidades

Levantamento baseado **somente em informações públicas** dos fornecedores (sites, central de ajuda, blogs). As alegações de desempenho são do próprio fornecedor e não foram verificadas de forma independente. Veja `CLEAN-ROOM.md`.

## Referência 1: ExitLag

Fontes: [Tecnologia](https://www.exitlag.com/pt/tecnologia), [Central de ajuda: funcionalidades](https://www.exitlag.com/knowledge-base/203), [Guia](https://www.exitlag.com/blog/exitlag-guide/), [Como funciona](https://www.exitlag.com/en/how-it-works).

| Funcionalidade (descrição pública) | Ideia | Módulo MB Extreme | Ordem | Prioridade |
|---|---|---|---|---|
| Roteamento multipath: envia pacotes por várias rotas e troca se uma ficar instável | Rotas redundantes e failover | `core::route` | 2 | Alta |
| Escolha da melhor rota por monitoramento contínuo | Medição de latência/jitter/perda e seleção automática | `core::probe` + `core::route` | 1 e 2 | Alta |
| Seleção de jogo e região, botão aplicar rotas | Perfis por jogo | `apps/desktop` | 1 | Alta |
| Multi-Internet: usar até 4 conexões ao mesmo tempo | Agregação/failover entre interfaces de rede | `core::route` | 3 | Média |
| FPS Boost: ajustes do sistema (ex.: Game DVR, Windows Defender) | Ajustes reversíveis do Windows | `core::sysopt` | 1 | Alta |
| Traffic Shaper: prioriza o jogo e limita banda de outros apps | QoS por processo | `core::sysopt` / `core::route` | 2 | Média |
| PC Boost: limpeza de RAM e ajustes do sistema | Limpeza e perfis de energia | `core::sysopt` | 1 | Média |
| Não é VPN, somente Windows | Posicionamento e plataforma inicial | n/a | n/a | n/a |

## Diferenciais propostos para o MB Extreme

- **IA de decisão** que aprende o perfil do usuário e da rede e recomenda ou aplica ajustes (`crates/ai`).
- **Ajustes sempre reversíveis**, com snapshot e botão de restaurar.
- **Transparência**: mostrar medições antes/depois, sem prometer ganhos fixos.

## Próximas referências

- [ ] Programa 2 (a definir)
- [ ] Programa 3 (a definir)
