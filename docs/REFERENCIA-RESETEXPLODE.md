# Estudo de referência: ResetExplode

Projeto: https://github.com/GustavoReset/ResetExplode (licença MIT, WPF/.NET 8).

**Uso neste projeto:** somente estudo das técnicas e da organização. O MB Extreme é implementado do zero, com código e interface próprios, sobre a base do MB Extreme. Nada é copiado. Se algum dia um trecho for reaproveitado, ele entra em `THIRD-PARTY.md` com o aviso MIT.

## Técnicas aprendidas (descritas com nossas palavras)

### Ajustes do Windows reversíveis (`sysopt`)
- Cada ajuste tem: leitura do estado atual, escrita, **valor original guardado antes da primeira alteração** e restauração. É o padrão que vamos seguir (snapshot + "restaurar originais").
- Ajustes usados no mercado, todos sem reduzir a segurança: plano de energia Alto desempenho (via `powercfg`), Modo de Jogo, desativar gravação em segundo plano (Game DVR), agendamento de GPU por hardware (HAGS, exige reiniciar), otimizações para jogos em janela e desligar a aceleração do mouse.
- Boa prática: não tocar em Defender, VBS/HVCI, UAC nem Windows Update. Marcar quais ajustes exigem reiniciar.

### Limpeza de memória e processos (`sysopt`)
- Esvaziar a lista de espera (standby) da RAM exige privilégio de administrador e uma chamada de sistema específica; reduzir o working set dos processos libera RAM física.
- Fechar programas de forma segura: janela aberta recebe pedido de fechar (sem forçar); só processos de segundo plano conhecidos são encerrados. Manter **lista de processos protegidos** (sistema, segurança, drivers, o próprio app) e uma lista de sugeridos seguros.

### Diagnóstico de rede (`probe`)
- Medir latência, jitter (variação entre pings) e perda até o roteador e até a internet separa problema local do problema do provedor.
- Também medir tempo de DNS, velocidade e **latência sob carga (bufferbloat)**.
- Emitir um veredito em linguagem simples.

### Inicialização
- Ler as chaves Run (usuário e máquina, 32 e 64 bits) e as pastas Inicializar; ativar/desativar pelo mesmo mecanismo do Gerenciador de Tarefas, e guardar cópia para desfazer.

### FPS sem injetar no jogo
- Escutar os eventos de apresentação de quadros do Windows (ETW, DXGI/D3D9) permite medir FPS, frametime e 1% low sem tocar no jogo e sem risco com anti-cheat.

### Perfis por jogo
- Detectar jogos instalados (Steam, Epic, GOG, Ubisoft) e aplicar um perfil só enquanto o jogo roda (prioridade, energia, GPU dedicada), desfazendo ao fechar.

### Organização e entrega
- Menu lateral com categorias, tema claro/escuro, configurações salvas por usuário, relatório exportável e publicação automatizada por GitHub Actions.

## O que não faz parte do nosso escopo
- Drivers seriais, Arduino, RGB, overclock e benchmarks pesados.

## O que o MB Extreme acrescenta
- Otimização de **rota** (multipath, relays) e **IA** de decisão, que o projeto de referência não tem.

## Próximo passo
Cruzar este estudo com a base atual do software do dono do projeto (a ser enviada) e decidir o que implementar primeiro.
