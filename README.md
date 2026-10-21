# MB Extreme

Software de otimização completa para Windows, da MB InfoArt.

## Objetivo

Melhorar a experiência em jogos e no uso diário do PC com:

1. **Otimização de ping e rota**: mede latência e perda de pacotes por caminho e escolhe a melhor rota.
2. **Otimização de desempenho do Windows**: serviços, plano de energia, processos em segundo plano, startup e ajustes de rede (TCP/DNS).
3. **IA de decisão**: aprende o perfil de uso e de rede do usuário e recomenda ou aplica ajustes.
4. **Instalador Windows** (`.msi`/`.exe`) com atualização automática.

## Estrutura

```
├── apps/desktop/      Interface (React + TypeScript) empacotada com Tauri
├── crates/core/       Núcleo em Rust: medição de rede, rotas, ajustes do sistema
├── crates/ai/         Motor de decisão (regras + modelo de aprendizado)
├── installer/         Empacotamento e assinatura do instalador Windows
├── docs/              Arquitetura, roadmap e política de desenvolvimento
└── scripts/           Automação de build e testes
```

## Stack proposta

- **Tauri 2** (Rust + WebView): instalador pequeno e acesso ao sistema.
- **React + TypeScript + Tailwind**.
- **Rust** no núcleo: desempenho e segurança em operações de baixo nível.
- **WinDivert** ou túnel UDP/WireGuard para roteamento (decisão em `docs/ARCHITECTURE.md`).

## Status

Estrutura criada. Nenhum código funcional ainda. Veja `docs/ROADMAP.md`.

## Política de desenvolvimento

Veja `docs/CLEAN-ROOM.md`. Resumo: o MB Extreme implementa funcionalidades próprias, sem copiar código, binários ou recursos de outros produtos.
