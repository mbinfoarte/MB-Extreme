# Como fazer a rede própria de rotas para jogos (parte que precisa de servidor)

Guia técnico de como montar o que o ExitLag/NoPing fazem. **Nada disto está implementado no MB Extreme**: exige servidor, custo e decisões jurídicas. A parte sem servidor (diagnóstico de rota, ping, perda, jitter, atraso sob carga, antes × depois) já está na aba **Rede** da biblioteca. Ver também `docs/ROTAS-JOGOS-PESQUISA.md`.

## 1. Princípio
O seu provedor escolhe a rota padrão até o jogo. Para usar outra rota, o tráfego precisa **passar por um servidor seu** (relay) que tenha caminho melhor até o servidor do jogo:

```
PC (Miguel Calmon) ──[internet do provedor]──► Relay em São Paulo ──[boa rota]──► Servidor do jogo
```
Só vale a pena se `ping(PC→relay) + ping(relay→jogo)` for **menor** que o ping direto, ou se a perda/oscilação forem menores. Por isso **meça antes** (seção 3).

## 2. Peças necessárias
| Peça | O que faz | Observação |
|---|---|---|
| **Relay (Linux)** | Recebe o túnel do cliente e repassa para o servidor do jogo. | VPS em São Paulo com boa conectividade; começar com 1. |
| **Túnel** | Leva o tráfego do PC ao relay. | WireGuard (UDP, leve, kernel do Linux). |
| **Cliente no Windows** | Cria o túnel e manda **só** o tráfego do jogo por ele. | Ver seção 5. |
| **Medidor** | Compara direto × túnel e decide. | Já temos ping/perda/jitter/rota no app. |
| **Botão de emergência** | Remove túnel e rotas com um clique. | Obrigatório antes de qualquer teste. |

## 3. Teste manual hoje, sem programar (para saber se compensa)
1. No Windows, com o jogo aberto, anote o IP do servidor (aba **Rede** da biblioteca → *Analisar rota do jogo*) e rode no PowerShell:
   - `ping -n 50 IP_DO_SERVIDOR` (média, perda)
   - `pathping -n -q 50 IP_DO_SERVIDOR` (perda e atraso por salto)
2. Alugue um VPS pequeno em São Paulo (escolha provedores com datacenter em SP e confira preço atual). Nele rode:
   - `ping -c 50 IP_DO_SERVIDOR` e `mtr -n -c 100 -r IP_DO_SERVIDOR` (ping do relay até o jogo)
3. Do seu PC: `ping -n 50 IP_DO_VPS`.
4. **Regra de decisão:** some (PC→VPS) + (VPS→jogo). Se for pelo menos ~5 ms menor que o direto, ou se a perda/jitter forem claramente menores, vale seguir. Se for parecido ou pior, o provedor já tem boa rota e o relay não ajuda.
5. Repita em horários diferentes (noite costuma piorar).

## 4. Montando o relay de teste (WireGuard)
No VPS (Ubuntu/Debian, como administrador):
```
apt install wireguard nftables
sysctl -w net.ipv4.ip_forward=1        # (e deixe fixo em /etc/sysctl.d/)
wg genkey | tee server.key | wg pubkey > server.pub
```
`/etc/wireguard/wg0.conf` (servidor):
```
[Interface]
Address = 10.50.0.1/24
ListenPort = 51820
PrivateKey = <server.key>
MTU = 1380

[Peer]            # um por usuário
PublicKey = <chave pública do cliente>
AllowedIPs = 10.50.0.2/32
```
NAT de saída, **permitindo só os IPs dos jogos** (reduz abuso e risco jurídico):
```
nft add table ip nat
nft add chain ip nat post '{ type nat hook postrouting priority 100 ; }'
nft add rule ip nat post ip daddr { <faixas dos jogos> } oifname "eth0" masquerade
nft add table ip filter
nft add chain ip filter forward '{ type filter hook forward priority 0 ; policy drop ; }'
nft add rule ip filter forward iifname "wg0" ip daddr { <faixas dos jogos> } accept
nft add rule ip filter forward oifname "wg0" ct state established,related accept
```
No cliente (Windows) a configuração tem **AllowedIPs = só os IPs do jogo** (não `0.0.0.0/0`), assim o resto da internet continua direto:
```
[Interface]
PrivateKey = <chave do cliente>
Address = 10.50.0.2/32
MTU = 1380

[Peer]
PublicKey = <chave pública do servidor>
Endpoint = IP_DO_VPS:51820
AllowedIPs = 13.89.178.0/24, 35.0.0.0/24      # faixas do servidor do jogo
PersistentKeepalive = 25
```
Com isso: `ping -n 50 IP_DO_JOGO` agora vai pelo túnel; compare com o direto.

**Importante:** o IP que o jogo vê passa a ser o do VPS. Conexões já abertas **caem** ao ligar o túnel; ligue **antes** de abrir o jogo.

## 5. Cliente dentro do MB Extreme
Opções para criar o túnel no Windows (verificar licença de cada uma antes de distribuir):
1. **WireGuard para Windows instalado pelo usuário** e controlado pelo MB Extreme (instalar/remover o serviço de túnel por linha de comando). Mais simples e seguro.
2. **Biblioteca embutida do WireGuard (wireguard-nt)**: sem instalar o app do WireGuard, mas exige cuidar da licença dos binários e do driver assinado.
3. **Driver de divisão de tráfego por aplicativo** (usado por VPNs e por projetos como o NetRoute, licença MPL-2.0) ou captura de pacotes (WinDivert, LGPL/GPL): permite escolher **por programa**, mas é driver de kernel, com conflitos (só um programa pode usá-lo) e responsabilidade de assinatura.

**Recomendação:** começar com a opção 1 e rotas por IP (AllowedIPs). Para saber os IPs do jogo antes de ele abrir, manter um catálogo por jogo (faixas de IP das empresas, via dados públicos de BGP/ASN) e completar com o que o detector da aba **Rede** observa. Nunca mexer em rota sem o botão de emergência (`wg-quick`/serviço de túnel removido + rotas apagadas).

## 6. Multipath (o diferencial do ExitLag/NoPing)
Ideia: cada pacote do jogo é enviado por **2 ou mais caminhos** até o servidor de saída, que descarta o duplicado.
- **Formato:** o cliente coloca um número de sequência (32 bits) em cada pacote; o servidor guarda uma janela deslizante (bitmap) dos números já vistos, repassa o primeiro que chegar e descarta os repetidos. O mesmo vale no sentido contrário (servidor → cliente).
- **Caminhos:** (a) dois relays diferentes que desembocam no **mesmo** servidor de saída; (b) duas conexões de internet do usuário (ex.: fibra + 4G) para o mesmo relay.
- **Custo:** dobra a banda (jogos usam pouco) e quase nenhum atraso extra; reduz perda e picos.
- **Complexidade:** protocolo próprio (UDP), testes de rede ruim e cuidado com o MTU. É a parte mais trabalhosa; só depois de a etapa 2 (túnel simples) provar ganho.

## 7. Troca de rota ao vivo sem derrubar o jogo
- **Possível:** trocar o **caminho interno** entre cliente e servidor de saída, desde que o **IP de saída** (o que o jogo vê) continue o mesmo. Regras: medir cada caminho a cada ~1 s (ping, perda, jitter); só trocar se o outro for melhor por X ms durante Y segundos (histerese, como no controle de ventoinhas), para não ficar alternando.
- **Impossível sem derrubar:** alternar entre "conexão direta" e "túnel" (o IP muda) ou entre relays com **IPs de saída diferentes**.
- **DNS:** trocar o DNS durante a partida não derruba nada, mas também não muda o ping do jogo que já conectou.
- **ICMP:** só serve para medir; o jogo usa TCP/UDP.
- **TCP dentro do túnel:** em caso de perda, o TCP trava pacotes atrás do perdido; multipath ajuda.

## 8. Custos, segurança e jurídico (consultar advogado)
- **Custo:** servidor mensal + banda por usuário; cresce com o número de usuários e com o DDoS contra o relay. Comece com 1 servidor e poucos usuários de teste.
- **Abuso:** limitar a saída às faixas dos jogos (seção 4), limitar taxa por usuário, chave por usuário revogável, sem acesso à rede local do servidor.
- **Privacidade (LGPD):** não inspecionar nem guardar o conteúdo; guardar o mínimo de registros; política de privacidade e termos de uso claros.
- **Marco Civil da Internet:** provedores de aplicação podem ter obrigação de guardar registros de acesso por prazo definido; confirmar com advogado se o serviço se enquadra.
- **Jogos e antitrapaça:** redirecionar tráfego sem injetar nada no jogo costuma ser aceito, mas cada jogo tem política própria; testar em cada um e avisar o usuário.
- **Licenças:** conferir WireGuard, wireguard-nt, WinDivert (LGPL/GPL) e o driver de split tunnel (MPL-2.0) antes de distribuir.
- **Honestidade:** o app mostra o ping medido, não promete ganho.

## 9. Roteiro sugerido
| Fase | Entrega | Critério para seguir |
|---|---|---|
| A | Teste manual (seção 3) | Ganho de pelo menos ~5 ms ou perda/jitter menores |
| B | 1 relay + WireGuard, ligado à mão (seção 4) | Ganho confirmado em 2+ jogos e em horários diferentes |
| C | MB Extreme controla o túnel (ligar/desligar, rotas por jogo, emergência, medir direto × túnel) | Funciona sem derrubar o PC nem a internet |
| D | Vários relays, escolha automática, multipath | Menos perda/picos que a fase C |
| E | Abrir a mais usuários (cobrança, termos, LGPD) | Custos e jurídico resolvidos |
