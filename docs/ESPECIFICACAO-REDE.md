# Especificação funcional: Analisador de rede (rascunho v1)

Complementa `ESPECIFICACAO-FUNCIONAL.md`. Base: tela enviada pelo dono do projeto, com quatro abas. Implementação própria, a partir das técnicas gerais de diagnóstico de rede.

## Abas

1. **Monitoramento em tempo real**
2. **Scanner**
3. **Ping para jogos**
4. **Ferramentas** (MTR, Ping, TCPing)

> Somente a aba Ferramentas apareceu na imagem. As outras três estão descritas pelo nome e precisam de confirmação.

## 4. Ferramentas

Barra superior: escolha do teste (**MTR**, **Ping**, **TCPing**), campo "IP do servidor ou domínio" (exemplo: `www.google.com`), botão de configurações, botão de atualizar/limpar e botão **Iniciar**. Estado vazio: "Nenhum teste executado ainda. Este recurso mostra o caminho que seus pacotes percorreram pela rede."

| Teste | O que faz | Mecanismo previsto | Saída |
|---|---|---|---|
| **MTR** | Mostra cada salto (roteador) até o destino, com perda e latência por salto, repetindo várias rodadas | Pacotes ICMP com TTL crescente (como traceroute) repetidos, calculando perda, média, melhor, pior e jitter por salto | Tabela por salto, com destaque do salto onde começa a perda ou a latência |
| **Ping** | Latência, jitter e perda até o destino | ICMP echo com intervalo configurável | Resumo e gráfico ao vivo |
| **TCPing** | Latência de conexão TCP numa porta (útil quando o servidor bloqueia ICMP) | Tempo de handshake TCP | Resumo e gráfico ao vivo |

**Configurações:** quantidade de pacotes/rodadas, intervalo, tamanho do pacote, porta (TCPing), limite de saltos (MTR), resolver nomes dos saltos (DNS reverso). **Histórico:** guardar os últimos testes e permitir exportar.

**Cuidados:**
- Saltos que não respondem (`*`) não significam falha; muitos roteadores limitam ICMP. Só indicar problema quando a perda continua nos saltos seguintes.
- Validar a entrada (domínio/IP) e limitar a taxa de pacotes para não parecer ataque.
- ICMP pode precisar de privilégios dependendo da API usada.

## 1. Monitoramento em tempo real (proposto)

Latência, jitter e perda ao vivo para destinos escolhidos, com gráfico, uso de banda por aplicativo (opcional) e alerta quando ultrapassar limites.

## 2. Scanner (proposto)

Descobrir dispositivos na rede local (IP, nome, fabricante) e identificar quem consome banda, para achar o que atrapalha o jogo. Somente na rede do próprio usuário, sem varredura de portas em terceiros.

## 3. Ping para jogos (proposto)

Lista de jogos e regiões de servidor, com ping por região e recomendação da melhor. Depende de uma base de endereços de servidores de cada jogo (catálogo próprio, mantido por nós). Alimenta a otimização de rota.

## Ligação com o resto do produto

- `core::probe` fornece medição de latência/jitter/perda para todas as abas.
- O MTR identifica **onde** está o problema e justifica a troca de rota (fase 2).
- O diagnóstico pode alimentar a IA com padrões do usuário.

## Pontos em aberto

- Confirmar o conteúdo das abas 1, 2 e 3 (enviar prints).
- Definir o catálogo inicial de jogos/servidores do "Ping para jogos".
