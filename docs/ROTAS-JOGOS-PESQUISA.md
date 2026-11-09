# Rotas e redução de ping para jogos (pesquisa)

Pesquisa de 23/10/2026 sobre ExitLag e programas parecidos, para decidir o que o MB Extreme pode ter. **Quase tudo vem de páginas dos próprios fabricantes e de comparativos com interesse comercial; não há teste independente.** O site do ExitLag não pôde ser aberto a partir do ambiente de pesquisa, então o detalhe dele vem de resumos de busca.

## 1. Os programas
| Programa | Como se descreve | Diferencial citado |
|---|---|---|
| **ExitLag** | Rede privada para jogos: desvia o tráfego do jogo pelos servidores deles (cerca de 200+ servidores). | **Multipath:** manda o mesmo tráfego por vários caminhos ao mesmo tempo; escolha de região do jogo; escolhe quantas rotas TCP/UDP/ICMP; mede ping, perda e oscilação. |
| **NoPing** | Parecido, mais barato na América Latina (segundo comparativos). | **Multi Connection** (cópias dos pacotes por 3 a 5 caminhos, descarta duplicados), **Multi Internet** (usar até 6 conexões/provedores juntos), monitora as ~10 melhores rotas e troca em ~3 s se houver perda/oscilação (afirmação do fabricante). |
| **WTFast** | Rede privada de rota única. | Mais antigo; poucos recursos avançados. |
| **Mudfish** | Otimização de rede, cobrança por tráfego. | Modo multipath e encadeamento de servidores; resultado muda muito de nó para nó. Um usuário brasileiro relatou ~200 ms contra ~130 ms do ExitLag no mesmo teste. |
| **GearUP, LagoFast** | Roteamento "adaptativo/IA" com escolha automática do caminho. | Escolha automática de rota; detalhes técnicos não publicados. |
| **Haste** | Quase sem informação encontrada. | — |
Não encontrei nenhum programa chamado "VPN Lag"; o termo pode se referir a VPNs gamer em geral.

## 2. O que todos têm em comum
1. **Servidores próprios** perto do jogo e com boa conexão com a internet (é isso que muda o caminho; software no PC sozinho não muda a rota do provedor).
2. **Escolha do jogo e da região** na interface, com lista de jogos.
3. **Medição contínua** de ping, perda de pacotes e oscilação (jitter), mostrada ao usuário, com mapa de rota e "ping estimado".
4. **Só o tráfego do jogo** passa pelo túnel (o resto da internet vai direto).
5. **Teste grátis** e cobrança mensal.

## 3. Como funcionam por dentro (mecanismos conhecidos)
- **Desviar só o tráfego do jogo:** por driver de filtro do Windows (WFP, filtros por aplicativo), por biblioteca de captura de pacotes (WinDivert, licença LGPL-3/GPL-2, driver assinado), ou por rotas do sistema só para os IPs do jogo. Exemplo aberto: o projeto NetRoute usa o driver de split tunneling da Mullvad (MPL-2.0) sem injetar nada no jogo.
- **Proxy (SOCKS/HTTP)** é simples, mas muitos jogos ignoram proxy e UDP é difícil; modo TUN pode "falsear" o ICMP (ping 1 ms), o que atrapalha jogos que escolhem servidor pelo ping.
- **Multipath:** o cliente copia o pacote por 2+ caminhos até o servidor de saída, que descarta o duplicado. Reduz perda e picos. **Exige software dos dois lados** (cliente e servidor da rede deles).
- **Troca de rota sem cair:** só é possível porque o jogo sempre vê o mesmo IP de saída; o que muda é o caminho **dentro** da rede do serviço. Trocar entre "direto" e "pelo túnel" muda o IP visto pelo jogo e derruba muitas conexões.
- **Escolha de rota:** medições periódicas dos candidatos (ping, perda, oscilação) e troca quando um piora.

## 4. Quando NÃO ajuda
- Jogos **peer-to-peer** (o caminho é entre jogadores, não até um servidor).
- Quando o atraso vem do **Wi-Fi, do roteador ou do uso da internet em paralelo** (bufferbloat).
- Quando a rota do provedor já é boa; o túnel adiciona um salto e pode piorar.
- Resultado varia por jogo, horário e provedor (relatos mistos, inclusive ping maior em alguns casos).

## 5. Contexto do Brasil (interior da Bahia)
- Existe IX.br em Salvador; peering local reduz latência, mas **um IX não garante rota curta**: a rota depende de o provedor ter peering direto com o destino. Há estudos mostrando que rotas por IX nem sempre são as mais rápidas e que a densidade de peering do IX.br é menor que a de IX europeus.
- Cabos como o EllaLink chegam em Fortaleza; alguns serviços passam por lá (por isso o resultado muda de nó para nó).
- Sem dados do seu provedor não dá para prever o ganho: **é preciso medir** (traceroute até o servidor do jogo e comparação com a rota por um servidor de teste).

## 6. O melhor de cada um, e o que o MB Extreme pode ter
| Ideia | Origem | Dá para fazer? | Precisa de servidor? |
|---|---|---|---|
| Ver conexões do jogo (IP, porta, TCP/UDP) e ping/perda/jitter em tempo real | todos | **Sim, já** (tabelas do Windows + medição) | Não |
| Mapa de rota salto a salto com onde o atraso aparece | ExitLag | **Sim** | Não |
| Teste de atraso sob carga (bufferbloat) e dicas de roteador/Wi-Fi | — | **Sim** | Não |
| Lista de jogos da biblioteca com "região do jogo" e IPs conhecidos | todos | **Sim**, mantendo uma lista | Não |
| Túnel só para os IPs do jogo (WireGuard) e comparação "direto × túnel" com escolha automática | WTFast/Mudfish | **Sim**, sem driver próprio | **Sim (1 servidor em São Paulo para teste)** |
| Multipath (cópia por 2+ caminhos, descarte no servidor) | ExitLag/NoPing | Possível, trabalhoso (protocolo próprio) | **Sim, 2+ servidores** |
| Troca de caminho ao vivo sem derrubar o jogo | NoPing | Só dentro da nossa rede, com IP de saída fixo | **Sim** |
| Multi Internet (usar 2 provedores juntos) | NoPing | Possível se você tiver 2 conexões | Sim |
| Verificação "provou que funcionou" (medir antes/depois com o jogo) | NetRoute | **Sim** (já temos antes/depois de FPS; falta ping) | Não |
| Recuperação de emergência (remover todas as rotas/filtros com um clique) | NetRoute | **Sim, obrigatório** | Não |

## 7. Proposta por etapas
1. **Etapa 1 (sem servidor):** diagnóstico de rota do jogo na biblioteca: conexões ativas do jogo, ping/perda/jitter, mapa de saltos, teste de bufferbloat, relatório "onde está o atraso" e comparação antes/depois.
2. **Etapa 2 (1 servidor de teste em São Paulo):** túnel WireGuard só para os IPs do jogo, com botão de emergência, e comparação direto × túnel; o app só usa o túnel quando for melhor.
3. **Etapa 3 (rede própria):** vários servidores, multipath e troca automática de caminho, com cuidado de custo, privacidade (LGPD) e abuso.

## 8. Riscos e cuidados
- Custo mensal de servidores e banda; responsabilidade sobre o tráfego dos usuários (privacidade, abuso).
- Antitrapaça: redirecionar tráfego sem injetar no jogo costuma ser aceito, mas cada jogo tem a sua política.
- Licenças de componentes (WinDivert LGPL/GPL, driver da Mullvad MPL-2.0, WireGuard): revisar antes de distribuir.
- Honestidade: o app deve mostrar o ping medido, nunca prometer ganho.
