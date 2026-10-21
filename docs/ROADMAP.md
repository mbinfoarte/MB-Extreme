# Roadmap

## Fase 0: Base
- [x] Estrutura de pastas e documentação
- [x] Especificações (funcional, rede, interface)
- [x] Stack definida: .NET 8 + WPF
- [ ] Importar a base atual do software (a ser enviada)

## Fase 1: MVP Windows
- [x] Tema preto e amarelo, menu lateral e janela de progresso padrão
- [x] Memória: status, limpeza e lista de protegidos
- [x] DNS: teste, ranking, aplicar e restaurar
- [x] CI no GitHub Actions (build Windows)
- [x] Primeiro build validado no GitHub Actions
- [ ] Testar em um PC Windows (visual, limpeza de RAM, DNS)
- [x] Motor de ajustes reversíveis e 23 ajustes (energia, jogos, rede, privacidade, boot, periféricos)
- [x] Remoção de aplicativos do Windows, um por um, com ponto de restauração
- [ ] Ajustes planejados do `CATALOGO-AJUSTES.md` (TCP, MTU por teste, bcdedit, armazenamento)
- [ ] Ferramenta de Reparo do Windows (chkdsk, DISM, sfc)
- [ ] Limpeza automática da RAM (ao iniciar jogo / por status)
- [ ] Analisador de rede: Ping, TCPing e MTR
- [ ] Inicialização do Windows
- [ ] Instalador e assinatura de código

## Fase 2: Desempenho e ferramentas
- [ ] Informações de hardware e sensores
- [ ] Stress test e benchmarks próprios
- [ ] Drivers (fontes oficiais) e instalação de programas (winget)
- [ ] Central de ações (histórico e desfazer)
- [ ] Perfis por jogo e medição de FPS

## Fase 3: Rotas
- [ ] Ping por região de jogo e catálogo de servidores
- [ ] Relays próprios, multipath e seleção automática de rota

## Fase 4: IA
- [ ] Coleta de métricas (opt-in) e motor de recomendação

## Fase 5: Produto
- [ ] Auto-update, licenciamento, site e suporte
