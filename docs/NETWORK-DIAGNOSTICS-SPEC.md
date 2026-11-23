# MB Network Diagnostics
## Documento de requisitos, arquitetura e plano de implementação para Claude Code

**Versão:** 1.0 | **Data-base:** 23/11/2026 | **Plataforma-alvo:** Windows 10/11 x64 | **Idioma:** Português do Brasil  
**Natureza:** Especificação de produto e engenharia. Não constitui comprovação de que todas as funcionalidades de terceiros são integráveis.  
**Objetivo:** Construir um aplicativo desktop para diagnóstico, análise, monitoramento e reparação controlada de redes locais, Wi-Fi, DNS, DHCP, IP, conectividade e rotas.

> **Diretriz de produto:** medir antes de recomendar, produzir evidências antes de corrigir, pedir consentimento antes de mudanças e medir novamente após toda intervenção. O software deve atuar somente em equipamentos e redes próprios ou expressamente autorizados.

# 1. Visão executiva

O MB Network Diagnostics deverá reunir em uma interface única capacidades inspiradas em Wireshark, PingPlotter, Nmap, NetSpot, Acrylic Wi-Fi Analyzer, PRTG, ManageEngine OpUtils, Angry IP Scanner, Fing, WinMTR, iPerf3, DNS Benchmark, TCPView, WifiInfoView, Zabbix e ntopng. **Recriar as funções não significa copiar código, marca, layout proprietário ou contornar licenças.**

**Públicos:** técnicos de informática, equipes de suporte, administradores de redes de pequenas e médias empresas e usuários avançados. **Casos:** internet lenta, Wi-Fi instável, impressora fora da rede, perda de conexão VPN, conflito IP, servidor inacessível, gargalos LAN, DNS defeituoso, interferência/congestionamento sem fio e equipamentos não identificados.

**Modo de trabalho:** (a) diagnóstico rápido em 1 clique; (b) análise avançada por módulo; (c) monitoramento contínuo opt-in; (d) reparação assistida; (e) relatório técnico antes/depois.

**Restrições:** análise de sinal do próprio adaptador Wi-Fi não substitui levantamento de cobertura com múltiplos pontos; análise de espectro RF exige hardware específico; determinação de gargalo na rede de terceiros é probabilística; testes de throughput precisam de servidor de teste ou endpoint autorizado; detecção de duplicação IP exige correlação de fontes, não apenas ping.

# 2. Inventário completo de ferramentas de referência

| Ferramenta | Capacidades a estudar | Tradução para funções do nosso app | Limite/integração |
|---|---|---|---|
| Wireshark / TShark | Captura e dissecação; filtros; ARP, DHCP, DNS, TCP, UDP, ICMP, TLS; retransmissão e handshake | Captura opt-in, resumo por protocolo, diagnóstico de timeout DNS, retransmissões TCP, exportação PCAPNG | TShark como processo externo opcional; GPL e Npcap exigem revisão de licença |
| PingPlotter | Ping e traceroute periódicos; histórico de jitter, perdas e latência; visualização por salto | Linha do tempo por destino, salto, alertas, comparação com gateway e WAN | Produto comercial: referência funcional, não embutir software proprietário |
| Nmap / Zenmap | Descoberta ARP/ICMP/TCP; identificação de hosts e serviços; relatórios XML | Scanner autorizado, IP/MAC, portas permitidas, perfil de rede e histórico | NPSL: revisar licença para distribuição/comercialização; preferir scanner próprio no MVP |
| NetSpot | Descoberta Wi-Fi; canais; RSSI; levantamentos de cobertura; heatmaps | Inventário de BSSID/SSID, canal, banda, potência e survey com posições informadas | Heatmap depende de planta/medições físicas; produto comercial como referência |
| Acrylic Wi-Fi Analyzer | Lista de BSSIDs; largura e ocupação de canal; características de segurança | Visualização 2,4/5/6 GHz, redes sobrepostas, recomendações de canal | API Windows limitada pelo driver e permissões; não garante detecção de ruído RF não Wi-Fi |
| PRTG | Sensores Ping, SNMP, WMI, fluxos; disponibilidade, largura de banda, alertas | Monitoramento e dashboards de servidores, switches, roteadores e enlaces | Implementar coleta própria; recursos avançados no produto original são comerciais |
| ManageEngine OpUtils | IPAM, DHCP, ARP, switch-port mapping, identificação de conflitos | Inventário e histórico IP↔MAC; detecção de possível duplicidade; DHCP autorizado | Leitura de DHCP/switch remoto exige credenciais e suporte do equipamento |
| Angry IP Scanner | Descoberta rápida em sub-redes, resolução hostnames, portas | Inventário local rápido com timeout e taxa limitados | Examinar licença da versão/bibliotecas antes de empacotar |
| Fing Desktop | Identidade e histórico de dispositivos, alertas de novos participantes | Alertas de novo IP/MAC, fabricante OUI (base licenciada), identificação manual | Fabricante inferido por OUI não prova identidade do dispositivo |
| WinMTR | Combinação de ping e traceroute repetitivos; perda por salto | Monitor de rota e janela histórica de hop/latência | Intermediários podem limitar ICMP; evitar atribuir culpa só pelo salto |
| iPerf3 | Throughput TCP/UDP; jitter e perdas UDP; múltiplos fluxos e JSON | Benchmark LAN com cliente e agente servidor, gráficos e relatórios | BSD-3-Clause; endpoint remoto autorizado e compatibilidade Windows devem ser testados |
| GRC DNS Benchmark | Tempo de resposta de resolvedores, disponibilidade e comparação | Benchmarks de DNS: cache/no-cache, erros e mediana/p95 | Aplicativo fechado: reproduzir métodos gerais, não copiar implementação |
| TCPView (Sysinternals) | Mapeamento PID→TCP/UDP; endereços e estados de conexão | Monitor por processo usando APIs de tabelas de conexão do Windows | API própria; não incorporar TCPView no instalador |
| WifiInfoView | Scan e detalhamento de APs vizinhos | Redes, canais, PHY, RSSI, BSSID e fabricante quando disponível | Funcionalidade depende de Windows WLAN API e do hardware |
| Zabbix | Monitoramento distribuído, agentes, SNMP, templates e alertas | Módulo empresarial opcional: SNMP/ICMP, histórico, regras | Implementação própria simples antes de oferecer integração externa |
| ntopng | Tráfego por host, protocolo, aplicação e fluxo | Relatório dos principais fluxos observáveis e consumo por interface | Ver fluxo de terceiros requer captura no ponto correto (SPAN/TAP/roteador), não basta estar em um PC |

**Outras referências úteis:** Microsoft `ipconfig`, `ping`, `tracert`, `pathping`, `nslookup`, `Resolve-DnsName`, `Test-NetConnection`, `Get-NetAdapter`, `Get-NetIPConfiguration`, `Get-NetRoute`, `Get-DnsClientCache`, `Get-WinEvent`, `netsh wlan`, Windows WLAN API, IP Helper API, ETW, WMI/CIM, SNMP, LLDP (quando disponível), ARP e IPv6 Neighbor Discovery.

# 3. Catálogo funcional detalhado

## RF-01 — Detecção de interface e configuração

Identificar interface Ethernet/Wi-Fi/VPN/virtual; status, nome, GUID, MAC, driver e versão, velocidade negociada, duplex quando exposto, MTU, DNS, IP v4/v6, máscara/prefixo, gateway, DHCP habilitado, servidor DHCP, lease, métricas de rota, proxy e perfil público/privado. Exibir múltiplos adaptadores e identificar efetivamente qual rota é usada até cada destino. Detectar APIPA 169.254/16, gateway fora da sub-rede, DNS vazio, endereços sobrepostos e interface desabilitada. **Coleta:** APIs Windows + PowerShell com output estruturado; nunca depender só de parsing textual localizado. **Critério:** resultados coerentes com `Get-NetIPConfiguration` e `Get-NetAdapter`.

## RF-02 — Descoberta da rede LAN

Descobrir sub-redes a partir das interfaces; permitir escopo manual CIDR; realizar ARP para o domínio L2, ICMP e descoberta TCP apenas em hosts/rede autorizados; incluir IPv6 ND quando suportado; identificar nome via DNS reverso e hostname quando viável; registrar MAC, IP, OUI, first_seen, last_seen, latência e confiança da classificação. Rate limit, cancelamento, barra de progresso, prevenção de grandes scans acidentais e identificação de VLANs somente quando há visibilidade/credencial. **Saída:** tabela pesquisável e exportável em CSV/JSON. **Restrição:** máquinas com firewall podem não responder a ping; não inferir ausência absoluta.

## RF-03 — Conflitos de endereço IP e DHCP

Cruzar cache ARP, observações ARP repetidas, registros DHCP autorizados, associações IP/MAC históricas e eventos de Windows. Sinalizar: (1) mesmo IPv4 associado a MACs diferentes no mesmo segmento em janela curta; (2) alternância de MAC por endereço suspeita; (3) IP estático dentro do pool DHCP conhecido; (4) oferta de DHCP inesperada observada passivamente. Diferenciar proxy ARP, VRRP/HSRP, mudanças legítimas de MAC, MAC aleatório e roteadores redundantes; apresentar **suspeita**, evidência e confiança, não acusação automática. Confirmar em switch/DHCP ou com captura quando disponível. Jamais desconectar clientes automaticamente.

## RF-04 — Conectividade e estado da internet

Sequência de testes: loopback/stack → IP próprio → gateway → IP externo autorizado → DNS → serviço HTTP(S) escolhido → serviço aplicativo. Medir RTT min/mediana/p95/max, jitter operacional definido explicitamente (ex.: variação absoluta entre RTTs sucessivos), perda e disponibilidade. Detectar possível portal cativo sem interpretá-lo como problema de DNS. Comparar Ethernet e Wi-Fi se ambos disponíveis e nunca forçar troca de rota.

## RF-05 — Traceroute, PathPing e rotas

Rastrear destinos usando ICMP/TCP/UDP conforme disponibilidade e permissões; obter TTL/hop, RTT e resolução reversa opcional; reexecutar amostras em intervalos; mostrar histórico em gráfico e diferenças de caminho. Identificar primeiro salto de degradação **persistente até o destino**, distinguindo limites de ICMP, assimetria de rotas e ECMP. Disponibilizar modo WinMTR e comparação gateway ↔ ISP ↔ destino. Não alegar localização geográfica exata ou responsabilidade de um salto sem evidência.

## RF-06 — DNS completo

Mostrar DNS configurados por adaptador, cache e ordem efetiva de resolvedores; testar A, AAAA, CNAME, MX, TXT e SRV conforme necessidade; medir latência, erros SERVFAIL/NXDOMAIN/timeouts, resolvedor alternativo escolhido, consistência de respostas, possíveis falhas em IPv6, DNS-over-HTTPS quando detectável e split DNS de VPN. Benchmark sob política de testes comparáveis (warm/cold cache, mesmas consultas, mediana/p95). Recursos de reparação: flush do cache local; re-registro DNS apenas quando justificado; mudança de resolvedor com backup, consentimento e verificação de DNS corporativo/VPN; rollback garantido sempre que possível. DNS mais rápido não implica internet mais rápida.

## RF-07 — Diagnóstico Wi-Fi

Via WLAN API, obter SSID/BSSID (quando permitido), banda, canal, frequência, RSSI, link rate, PHY/security capabilities, estado de associação, histórico de desconexões e redes vizinhas. Gráficos de potência, canais e largura; comparar sinal e estabilidade em janelas de tempo. Recomendar 2,4/5/6 GHz conforme capacidade do cliente/AP, densidade, interferência observável, canais regulatórios, DFS e largura. **Survey/heatmap** requer que o técnico registre pontos na planta; não deduzir a planta do imóvel. Para ruído não Wi-Fi, sugerir hardware de espectro (não fingir medir). Alterar canal/AP somente através de integração autorizada com roteador; caso contrário gerar instruções manuais.

## RF-08 — Throughput e gargalos

Teste LAN cliente-servidor via iPerf3 ou implementação equivalente: TCP upload/download, concorrência, duração, UDP com jitter/perda e limites para não saturar uma rede em produção. Selecionar endereço do servidor previamente autorizado; monitorar interface/CPU/disco durante teste; mostrar Mbps, percentis, retransmissões quando fornecidas. Speed test de internet é módulo distinto, sujeito a endpoint e franquia. Comparar taxa negociada, throughput e requisitos de aplicação; evitar usar teste de internet como prova de performance LAN.

## RF-09 — Pacotes e análise de protocolos

Modo avançado com captura **desativada por padrão**, escolha de interface, duração, tamanho máximo, filtros e consentimento. Uso opcional de `dumpcap`/`tshark` com Npcap licenciado e presente. Extrair contagens DNS, DHCP, ARP, ICMP, TCP SYN, retransmissões/dup ACK (heurísticas), resets, TTL, top talkers e fluxos sem capturar payload sempre que possível. Armazenar PCAPNG protegido e exportar somente com consentimento; mascarar domínios, IPs e dados de payload em relatórios compartilháveis.

## RF-10 — Conexões e aplicativos

Listar conexões TCP/UDP locais, endpoints, estados, PID, processo, caminho do executável quando autorizado, volume de tráfego quando exposto por API ou captura. Exibir alertas de conexões excessivas, portas locais ocupadas, erros de handshake e processos com uso incomum. Não classificar uma conexão como maliciosa sem evidência; não encerrar processos automaticamente.

## RF-11 — Roteadores, switches, servidores e infraestrutura

Inventário manual de nós gerenciáveis; SNMPv3 preferencial (não armazenar senhas em claro), ICMP e WMI/WinRM autorizados; coletar status de interface, utilização, erros/discards, CPU/memória quando MIB disponível, uptime, ARP/FDB, DHCP leases e estado de portas quando exposto. Suportar múltiplas VLANs por agentes locais ou acesso L3 autorizado; não presumir visibilidade de todas as VLANs apenas pelo PC. Alarmes com histerese e deduplicação; histórico em SQLite; integração empresarial opcional com Zabbix/PRTG via APIs documentadas.

## RF-12 — Diagnóstico de configurações complementares

Verificar proxy WinHTTP/WinINET, VPN, interface virtual, DNS de túnel, hosts file (leitura), firewall e políticas sem desativá-los, rotas estáticas, IPv6 vs IPv4, MTU/PMTUD, NTP e certificados TLS de serviços testados, horários de lease DHCP, driver e energia do adaptador. Incluir logs WLAN-AutoConfig, DNS Client, DHCP Client e System via Event Log conforme permissão.

## RF-13 — Mecanismo de correção assistida

Ações organizadas por risco: **A/baixo** (flush DNS, refresh diagnóstico), **B/médio** (renovação DHCP, restabelecer adaptador, limpar cache ARP quando aplicável), **C/alto** (Winsock reset, TCP/IP reset, trocar DNS, alterar MTU, desativar IPv6, ajustar rotas ou rede Wi-Fi). Ações C inicialmente **apenas assistidas, não automáticas**. Cada ação precisa de pré-requisitos, escopo exato, confirmação, privilégios elevados quando necessários, snapshot prévio, log de comando, timeout, verificação e instrução de rollback. Algumas redefinições não têm rollback perfeito; informar claramente e sugerir ponto de restauração quando apropriado, sem apresentá-lo como garantia.

## RF-14 — Assistente de causa provável

Engine determinística de regras (não depender inicialmente de IA externa), cada hipótese com: código, evidências a favor, evidências contra, severidade, confiança, recomendação e próximo teste discriminante. Exemplo: gateway com perda e DNS funcionando → priorizar análise LAN/Wi-Fi; gateway normal e DNS falho → investigar DNS; throughput LAN baixo e negociação a 100 Mbps → investigar cabo/porta/negociação. Exibir claramente diferença entre sintoma, hipótese e causa confirmada.

## RF-15 — Monitoramento contínuo

Cadastro de destinos com intervalos 5–300 s configuráveis, limites por destino, retenção, notificação local, cronologia de eventos, disponibilidade, SLA calculado somente com amostragem declarada, comparação com baseline e exportação. Não executar testes intensivos continuamente e não iniciar varredura sem autorização.

## RF-16 — Relatórios e atendimento técnico

Gerar diagnóstico resumido e detalhado em HTML/PDF/JSON: identificação anonimizada do ambiente; topologia observada (não inventada); testes, parâmetros, horários, evidências, hipóteses, intervenção, resultados antes/depois, limites e assinatura opcional. Exportar dados brutos CSV e captura apenas separadamente. Modelo de relatório para atendimento ao cliente, com versão para técnico e versão leiga.

# 4. Requisitos não funcionais

**RNF-01 Segurança:** todas as verificações são passivas ou de baixo impacto por padrão; escaneamento de porta/pacotes apenas em redes autorizadas; allowlist de subnet, limite de taxa e timeouts.  
**RNF-02 Privacidade:** dados locais, retenção configurável, criptografia quando apropriado, segredos no Windows Credential Manager/DPAPI, mascaramento padrão no PDF de cliente.  
**RNF-03 Resiliência:** travamento de comando nunca bloqueia GUI; cancelar operação; degradação elegante se falta Npcap, SNMP ou privilégio.  
**RNF-04 Compatibilidade:** Win10/11 x64; interface PT-BR; suporte IPv4/IPv6; execução portátil para funções básicas.  
**RNF-05 Performance:** interface responsiva; concorrência limitada; memória do histórico com retenção definida; testes com até centenas de hosts por sub-rede sem congelar UI.  
**RNF-06 Auditoria:** eventos append-only para ações corretivas, identidade do operador local, timestamp, configuração antes/depois e resultado.  
**RNF-07 Licenças:** registro SBOM, versão, licença, origem, checksum, atribuições e autorização de redistribuição de cada componente.  
**RNF-08 Acessibilidade:** contraste, navegação por teclado, escala de fonte, estado textual além de cores.

# 5. Arquitetura recomendada para implementação

## 5.1 Stack sugerida

**Opção de referência:** desktop Windows com **Python 3.12+ + PySide6/Qt** para GUI, Python tipado para orquestração e APIs nativas via `ctypes`/`pywin32` quando adequadas; SQLite para histórico; Pydantic para modelos de dados; `asyncio`/workers para execução não bloqueante; `pytest` para testes. Empacotar em modo executável/instalador após validação de licenças. Esta opção maximiza velocidade de prototipagem no Claude Code. Revisar licença da distribuição Qt/PySide6 e obrigações de empacotamento (LGPL/GPL/comercial). **Alternativa robusta:** .NET 8+ (WPF/WinUI) com serviços Windows e P/Invoke. Escolher uma stack antes de codificar UI; não misturar duas sem necessidade.

**Não escolher Electron + backend Python + Rust simultaneamente no MVP.** Componentização deve permitir troca de motor sem refazer a interface.

## 5.2 Componentes

```
MB Network Diagnostics
├── ui/                  dashboard, abas, gráficos, relatórios, confirmações
├── application/         casos de uso, execução de diagnóstico, scheduler
├── domain/              modelos, regras, classificações, níveis de risco
├── collectors/
│   ├── windows/         adaptadores, rotas, DNS, Wi-Fi, eventos, PID/conexões
│   ├── probes/          ping, TCP connect, DNS, traceroute, HTTP
│   ├── discovery/       ARP/IPv6 ND, inventário e correlação
│   ├── throughput/      integração opcional iPerf3
│   ├── packet/          integração opcional TShark/Npcap
│   └── infrastructure/  SNMP, equipamentos e integrações
├── diagnostics/         correlação, regras, hipóteses, evidências
├── remediation/         plano, aprovação, execução, checagem, rollback
├── persistence/         SQLite, migrações, retenção, logs
├── reporting/           HTML/PDF/JSON, redaction e templates
├── security/            privilégios, allowlist, DPAPI, política de testes
└── tests/               unit, integration, fake adapters, scenario fixtures
```

## 5.3 Padrões

Arquitetura hexagonal: UI → casos de uso → interfaces dos coletores → adaptadores reais. Interface comum `Collector.collect(context) -> Observations`; `DiagnosticRule.evaluate(evidence) -> Findings`; `RemediationAction.plan()`, `preflight()`, `execute()`, `verify()`, `rollback()`; toda exceção convertida em erro tipado. Separar coleta, inferência e correção. Não disparar `cmd.exe` com strings interpoladas; usar APIs/PowerShell parametrizado ou subprocess com lista fixa de argumentos e validação. Aplicar princípio de menor privilégio e elevar apenas ação específica.

## 5.4 Esquema de dados inicial

- `Device(id, ip_addresses, mac, hostname, vendor, first_seen, last_seen, source, confidence)`.
- `Interface(id, name, type, ipv4, ipv6, gateways, dns, dhcp, link_speed, wifi_details)`.
- `Measurement(id, test_type, target, interface_id, started_at, duration, samples, status)`.
- `Hop(index, address, hostname, rtt_samples, loss_samples, note)`.
- `Finding(code, severity, confidence, title, observations, counterevidence, next_steps)`.
- `Remediation(id, action, risk, requested_by, approved_at, before, after, verify_status)`.
- `AuditEvent(id, event_time_utc, actor, action, params_redacted, outcome)`.
- `MonitoringTarget(id, kind, destination, interval_seconds, policy, active)`.

A versão 1 deve armazenar valores e unidades explícitas (ms, Mbps, %, dBm), timestamps UTC e fuso local na apresentação. Definir schema versionado para resultados exportados.

# 6. Matriz de execução: APIs, comandos e privilégios

| Função | API/comando de referência | Tipo | Condição |
|---|---|---|---|
| IPv4/IPv6, rotas e adaptador | IP Helper API; `Get-NetIPConfiguration`; `Get-NetRoute` | Leitura | Sem admin em muitos cenários |
| Detalhes NIC e link | `Get-NetAdapter`; WMI/CIM | Leitura | Alguns campos dependem de driver |
| Wi-Fi, BSSID e RSSI | Windows WLAN API; `netsh wlan show interfaces` | Leitura | NIC wireless + permissão; dados podem ser restringidos |
| Redes próximas | `netsh wlan show networks mode=bssid` | Leitura | Scan pode ser limitado por OS/driver |
| Relatório Wi-Fi | `netsh wlan show wlanreport` | Leitura/gera arquivo | Privilégios e retenção conforme OS |
| Diagnóstico DNS | `Resolve-DnsName`; consultas DNS programáticas | Leitura | Comportamento variável com VPN/DoH |
| Limpar cache DNS | `ipconfig /flushdns`; API apropriada | Correção A | Não altera DNS remoto |
| DHCP release/renew | `ipconfig /release` + `/renew` | Correção B | Apenas adaptador DHCP; pode desconectar usuário |
| TCP/IP reset | `netsh int ip reset` | Correção C | Admin; possível reinício; configurações especiais |
| Winsock reset | `netsh winsock reset` | Correção C | Admin; provável reinício |
| ARP cache | `arp -a` / APIs IP Helper | Leitura | Cache não é prova de todos os hosts |
| Rastreamento | `tracert`; `pathping` ou probe próprio | Leitura ativa | ICMP pode ser limitado |
| Portas/processos | IP Helper API GetExtendedTcpTable/GetExtendedUdpTable | Leitura | Permissões/limites de processo |
| Eventos | Windows Event Log/`Get-WinEvent` | Leitura | Acesso varia por log |
| LAN throughput | iPerf3 como binário aprovado | Leitura ativa intensa | Dois endpoints e autorização |
| Captura pacotes | `dumpcap`/`tshark` com Npcap | Captura sensível | Driver/licença/permissões |
| SNMPv3 | Cliente SNMP (biblioteca auditada) | Leitura remota | Credenciais e dispositivos compatíveis |

**Atenção:** `ipconfig /renew` não corrige IP estático. Um reset TCP/IP não deve preceder diagnóstico. Fornecer estimativa do impacto de mudança de rede: sessão remota pode ser encerrada.

# 7. Regras diagnósticas exemplares

**DR-001: possível falha DNS.** Pré-condição: ping/TCP para endereço IP externo válido; consultas ao resolvedor falham em múltiplas tentativas; consulta de mesmo nome ao resolvedor alternativo autorizado funciona. Saída: suspeita de DNS configurado ou caminho até DNS. Checar split DNS/VPN antes de recomendar mudança.

**DR-002: possível falha local.** Múltiplos pings ao gateway mostram perda e aumento persistente de RTT, enquanto loopback é normal. Próximos testes: link NIC, cabo/switch, Wi-Fi RSSI/canal, erros de interface. Não concluir que gateway é a causa.

**DR-003: possível gargalo de negociação.** Interface Ethernet link 100 Mbps com hardware esperado 1 Gbps; throughput LAN próximo ao limite do link; diagnóstico: verificar porta, cabo e autonegociação, sem forçar configuração automaticamente.

**DR-004: possível conflito IP.** ARP/L2 mostra dois MACs alternando pelo mesmo IPv4 em intervalo curto; subir confiança com DHCP lease, logs e captura. Caso VRRP/roteamento redundante reconhecido → reduzir confiança e sinalizar exceção.

**DR-005: perda somente em salto intermediário.** Hop perde ICMP mas sucessores e destino não perdem → classificar como limitação de resposta ICMP, não falha de roteamento comprovada.

**DR-006: saturação Wi-Fi provável.** Bom RSSI mas throughput ruim, perda sob carga, canais sobrepostos e múltiplas redes vizinhas; sugerir teste comparativo por cabo e revisão do canal/largura; não afirmar interferência RF não Wi-Fi sem instrumento.

**DR-007: rota/VPN.** DNS interno resolve só com VPN; rota preferencial aponta túnel; teste público funciona sem VPN e serviço corporativo falha → investigar túnel, DNS corporativo e política, sem trocar servidor DNS de toda a máquina.

# 8. UX / telas detalhadas

1. **Painel inicial:** indicador de conexão, interface ativa, IP/gateway/DNS, RTT gateway, DNS e internet; score apenas transparente e explicado; botões Diagnosticar, Monitorar e Reparar.
2. **Diagnóstico guiado:** seleção da rede e problema; etapas com tempo limite e progresso; resultados classificados: confirmado, provável, indeterminado, não testado.
3. **Rede local:** lista de dispositivos, filtros IP/MAC, status, novos dispositivos, mapa lógico observado (topologia não presumida).
4. **Rotas:** destinos favoritos, hops, linha de tempo RTT/jitter/perda, tabelas e anotações de ICMP.
5. **Wi-Fi:** adaptador, sinal dBm, BSSIDs, canais, bandas, histórico, assistente de survey com importação de planta.
6. **DNS:** resolvers, latência comparativa, consultas, cache, possíveis falhas e opção de flush controlado.
7. **Performance:** iPerf3 LAN, tráfego de interface, processos/conexões, limites de teste.
8. **Reparação:** snapshot, descrição e risco, confirmação, execução e verificação, histórico e rollback quando suportado.
9. **Empresarial:** dispositivos SNMP, disponibilidade, alertas, histórico, dashboard de interfaces.
10. **Relatórios:** resumo de cliente e técnico, edição de identificação, ocultação de dados sensíveis, exportação.

**Estética sugerida:** estilo Windows 11 com dashboard técnico claro/escuro, ícones discretos, gráficos legíveis e estados definidos por texto+ícone; manter branding configurável. Priorizar densidade de dados sem poluição visual.

# 9. Segurança, conformidade e licenciamento

- **Autorização:** confirmar propriedade/autorização antes de scan de faixas, análise de portas, captura ou monitoramento de terceiros. Definir IPs/faixas alvo; bloquear alvos públicos amplos por padrão.
- **Privilégios:** aplicativo base sem elevação; helper elevado isolado só para mudanças específicas e com confirmação explícita. Não elevar permanentemente a GUI.
- **Captura de tráfego:** capturas podem conter credenciais, tokens, domínios e dados privados. Desativar captura até opt-in, limitar tamanho/duração e tratar logs como sensíveis.
- **Comercialização:** Wireshark/TShark têm termos GPL; Nmap possui NPSL; Npcap tem condições próprias, inclusive para redistribuição; iPerf3 usa licença BSD-3-Clause; PySide6/Qt e bibliotecas devem ser avaliados. **Não redistribuir esses binários por suposição.** Usar instalador de terceiros oficial, instalação pelo usuário, ou componentes próprios quando juridicamente adequado, após revisão de licença.
- **Dados:** retenção curta por padrão; recursos de exportar/limpar dados; criptografar segredos; não enviar telemetria ou pacotes à nuvem sem consentimento.
- **Rollback:** fazer backup de DNS estático, rotas, estado NIC e demais configurações pertinentes antes da alteração. Ações disruptivas exigem alerta e podem não ser reversíveis totalmente.
- **Qualidade:** tratar falsos positivos e eventos especiais (VPN, múltiplas NIC, NAT, AP isolation, ICMP rate-limit, IPv6, VLANs e DHCP relay).

# 10. Roadmap em entregas para Claude Code

## Fase 0 — Fundação

Repositório, licença do produto, arquitetura, tipagem, CI, logging seguro, SQLite/migrações, configuração, empacotamento de desenvolvimento, fixtures, testes, `CLAUDE.md` e documentação técnica. Definir contrato de plugins/coletor e política de segurança.

## Fase 1 — MVP diagnóstico seguro

Interface; adaptadores/IP/DNS/rotas; gateway/Internet; ping, jitter e perda; traceroute básico; DNS testing; inventário LAN básico autorizado; diagnóstico por regras; PDF/HTML/JSON. Todos os diagnósticos funcionam sem administrador quando suportado.

## Fase 2 — Reparação controlada e monitoramento

Flush DNS, DHCP renew com aviso, snapshots e log; monitor periódico de destinos e gráficos; baseline; diagnóstico antes/depois; riscos A/B. **Não lançar reset geral automático.**

## Fase 3 — Wi-Fi e testes de desempenho

WLAN API, BSSIDs/RSSI/canais, histórico, heurísticas, survey manual; iPerf3 cliente/servidor opcional, taxas e comparação com link.

## Fase 4 — Análise profunda e IPAM

Correlação IP/MAC/DHCP, eventos, painel de conflito; conexões por PID; integração opcional TShark/Npcap; análises de tráfego e protocolo; SNMP básico.

## Fase 5 — Empresarial e acabamento

SNMPv3/alertas, topologia observada, agendamento, comparativo de locais, relatórios customizáveis, internacionalização, instalador assinado, testes de carga, modelo comercial e revisão final de licenças.

# 11. Plano de testes e aceitação

| Cenário | Preparação/teste | Aceitação |
|---|---|---|
| DNS falhando | Resolver de teste indisponível | Sinaliza DNS com evidências; sem mexer configuração |
| Gateway inacessível | Desconectar cabo ou desativar AP em lab | Diferencia interface down de falha WAN |
| ICMP intermediário limitado | Rota com salto que não responde | Não aponta salto como falha confirmada |
| Duplicação IP | Dois hosts controlados com mesmo IPv4 no lab | Suspeita com evidência de MAC/ARP e alertas de falso positivo |
| Rede corporativa com VPN | DNS split e rota de túnel | Não recomenda DNS público indiscriminadamente |
| IPv6 preferencial | Rede dual stack | Seleciona família corretamente e reporta ambas |
| Firewall bloqueando ping | Host responde TCP, não ICMP | Não marca host como offline absoluto |
| Wi-Fi saturado | Clientes e carga controlados | Exibe indicadores sem alegar ruído RF invisível |
| Benchmark iPerf3 | 2 endpoints locais autorizados | Reporta Mbps/jitter/erro e encerra teste no timeout |
| Ação corretiva falha | Simulação de acesso negado | Sem travar; log e status de rollback |
| Sessão remota | Usuário acessa via RDP/VPN | Alerta antes de ação que desconecta |
| Sem Npcap ou SNMP | Máquina limpa | Aplicativo e diagnóstico básico continuam funcionando |
| Dados sensíveis | Captura/relatório com IPs internos | Exportação anonimizada e opt-in para dados brutos |
| Escopo não autorizado | Solicitar scan público amplo | Bloqueio/solicitação de autorização e limitação |

**Metas iniciais sugeridas (não métricas já alcançadas):** abrir interface em menos de 5 s em máquina de teste moderna; não travar GUI durante ping/scan; cancelar teste em até 2 s quando a operação permitir; registrar 100% das ações corretivas; cobertura unitária alta nas regras críticas; teste E2E em Windows 10 e 11 x64.

# 12. Backlog com prioridades e dependências

**P0 / bloqueadores:** arquitetura, modelos, adapters Windows, IP/DNS/route, scheduler assíncrono, logger, autorização de escopo, testes e exportação.  
**P1 / MVP:** ping/traceroute, DNS benchmark simples, descoberta LAN, regras, dashboard, HTML/PDF.  
**P2 / próxima entrega:** flush DNS, DHCP, snapshots, monitoramento e gráficos.  
**P3 / avançado:** Wi-Fi, iPerf3, IP conflict correlation, PID/conexões, captura opcional, SNMP.  
**P4 / premium:** heatmaps, templates corporativos, integrações, dashboards e perfis de atendimento.

**Dependências cruzadas:** scan exige escopo/autorização; remediação exige snapshot/auditoria; iPerf3 exige servidor; captura exige driver/licença; análise empresarial exige credenciais; heatmap exige planta e survey.

# 13. Instruções de trabalho para Claude Code

Criar o arquivo `CLAUDE.md` na raiz do repositório com objetivos, stack, convenções, comandos de testes, política de segurança e links para esta especificação. Entregar cada fase em commits pequenos, com código testável e sem prometer features não implementadas. Usar fake adapters para testes automatizados sem necessitar rede real; apresentar plano e alterações antes de inserir novas dependências, principalmente binários externos. Documentar cada recurso como **Implementado**, **Parcial**, **Proposto** ou **Dependente de licença/hardware**, sem confundir protótipo visual com motor funcional.

## Prompt mestre de inicialização (copiar para Claude Code)

```text
Você é o arquiteto e desenvolvedor principal do projeto MB Network Diagnostics,
um aplicativo de diagnóstico de redes para Windows 10/11 x64.
Leia integralmente ESPECIFICACAO_MB_NETWORK.md antes de gerar código.

OBJETIVO: implementar o produto real e funcional por fases, começando pela
Fase 0 (fundação) e Fase 1 (MVP seguro), sem simular dados como se fossem reais.

REGRAS OBRIGATÓRIAS:
1. Não alterar rede sem consentimento explícito; nenhuma correção automática.
2. Nunca executar varredura fora de IPs/faixas autorizadas; rate limiting.
3. Separar interface, coletores, motor de regras, reparação e relatórios.
4. Preferir APIs nativas do Windows; tratar falhas de permissão e drivers.
5. Não incluir binários de terceiros até confirmar licença de redistribuição.
6. Criar testes unitários com mocks e cenários de integração seguros.
7. Não inventar medições, dispositivos, saltos ou causas confirmadas.
8. Documentar comandos de instalação, execução, testes e empacotamento.
9. Manter registro de decisões arquiteturais (ADR) e backlog atualizado.
10. Propor os arquivos e a arquitetura antes de implementar código extenso.

STACK PREFERENCIAL: Python 3.12+, PySide6, SQLite, Pydantic, pytest,
com adaptadores Windows e serviços assíncronos. Se houver objeção técnica
importante, documente a alternativa antes de migrar a stack.

ENTREGA INICIAL:
- árvore de diretórios e contratos principais;
- projeto executável com dashboard funcional;
- leitura real de interfaces, IP, gateway, DNS e rotas;
- testes reais de gateway, DNS, ping e traceroute;
- regras básicas de diagnóstico com evidências;
- relatório HTML e JSON; exportação PDF na sequência;
- testes e instruções reproduzíveis.

A cada etapa informe: o que foi implementado, como testar, limitações,
permissões exigidas, riscos e próximo item do backlog.
```

# 14. Fontes e documentação oficial

Consultas e documentação técnica verificadas em 23/11/2026. Verificar novamente termos de licença e disponibilidade antes de empacotar o produto.

- Nmap — documentação, discovery e referência: https://nmap.org/docs.html ; https://nmap.org/book/man-host-discovery.html
- Wireshark / TShark — documentação e repositório: https://www.wireshark.org/docs/ ; https://github.com/wireshark/wireshark
- iPerf3 (ESnet) — documentação e repositório: https://software.es.net/iperf/ ; https://github.com/esnet/iperf
- Microsoft WLAN `netsh`: https://learn.microsoft.com/pt-br/windows-server/administration/windows-commands/netsh-wlan
- Microsoft solução de problemas Wi-Fi: https://learn.microsoft.com/pt-br/troubleshoot/windows-client/networking/wireless-network-connectivity-issues-troubleshooting
- Microsoft restauração e reset: https://support.microsoft.com/en-us/windows/experience/connectivity-networking/fix-wi-fi-connection-issues-in-windows
- PingPlotter produtos e capacidades: https://www.pingplotter.com/products/
- NetSpot recursos: https://www.netspotapp.com/pt/features.html
- Acrylic Wi-Fi Analyzer: https://www.acrylicwifi.com/en/wifi-analyzer/
- PRTG: https://www.paessler.com/
- ManageEngine OpUtils: https://www.manageengine.com/products/oputils/
- Angry IP Scanner: https://angryip.org/
- Fing: https://www.fing.com/
- Zabbix: https://www.zabbix.com/
- ntopng: https://www.ntop.org/products/traffic-analysis/ntop/
- Microsoft Sysinternals TCPView: https://learn.microsoft.com/en-us/sysinternals/downloads/tcpview
- NirSoft WifiInfoView: https://www.nirsoft.net/utils/wifi_information_view.html
- Claude Code e contexto de projeto: https://support.claude.com/en/articles/14553240-give-claude-context-claude-md-and-better-prompts

# 15. Decisões pendentes antes da codificação comercial

1. Produto exclusivo da MB Infoarte ou comercialização multiempresa? Isto muda licenciamento, assinatura, usuários e suporte.
2. Stack definitiva Python/PySide6 ou .NET; escolha recomendada é prototipar em Python/PySide6 antes de investir em instalador.
3. Política de captura de pacotes e Npcap, além de redistribuição de Nmap e TShark.
4. Quem operará o agente em infraestrutura empresarial e como será feita autenticação SNMPv3.
5. Quais funções avançadas dependem de hardware externo, agentes remotos ou licenças comerciais.

**Resultado esperado:** produto técnico real, com testes reproduzíveis, evidências, permissões explícitas, relatórios de atendimento e evolução por fases — não apenas uma interface que imita ferramentas conhecidas.
