# Diagnóstico de rede (MB Network Diagnostics dentro do MB Extreme)

Especificação oficial: [NETWORK-DIAGNOSTICS-SPEC.md](NETWORK-DIAGNOSTICS-SPEC.md) (versão 1.0, 23/11/2026).

## ADR-001 · Stack: .NET 8 dentro do MB Extreme, não Python/PySide6 separado

- **Contexto:** a especificação sugere Python 3.12 + PySide6 como opção de referência e aceita .NET 8 (WPF/WinUI) como "alternativa robusta" (seção 5.1).
- **Decisão:** implementar em .NET 8 dentro do MB Extreme, como uma aba da tela **Otimizar › Rede**.
- **Motivos:**
  - o dono pediu a ferramenta dentro da parte de redes do programa, sem complicar a aba;
  - o programa já é .NET 8 + WPF, com instalador, licença, testes e CI;
  - um segundo programa em Python traria outro instalador, a licença do Qt e o empacotamento, sem ganho para o usuário;
  - a especificação pede para não misturar duas stacks sem necessidade.
- **Consequências:**
  - o motor fica em `src/MBExtreme.Core/NetDiag` (sem interface e testável no Linux);
  - a tela fica em `src/MBExtreme.App`;
  - SQLite, Pydantic e pytest são trocados por JSON versionado, records do C# e xUnit, que o projeto já usa.

## Arquitetura (hexagonal)

```
App (WPF)  →  NetDiagRunner (casos de uso)  →  INetProbe (contrato)  →  SystemNetProbe (APIs .NET/Windows)
                    ↓                                                   FakeProbe (testes, sem rede)
              NetDiagRules (regras determinísticas)  →  Finding (evidência, contra-evidência, confiança, próximo teste)
                    ↓
              NetDiagReportWriter (JSON esquema 1, HTML com dados escondidos)
              NetScope (política: o que pode ser testado e varrido; mascaramento)
```

- **Coleta:** `NetworkInterface`, `Ping`, `Dns`, socket UDP 53 e `HttpClient`. Nada de ler a saída de `ipconfig` e companhia, porque ela muda conforme o idioma do Windows.
- **Inferência:** a coleta só mede e as regras só decidem. Toda regra é testada com cenários falsos.
- **Correção:** não existe nesta fase. Fica para a Fase 2, com plano, confirmação, foto do estado anterior, registro e verificação.

## Política de segurança (RNF-01, seção 9)

- **Diagnóstico só de leitura:** nenhum teste muda a rede.
- **Destinos externos fixos:** 1.1.1.1 e 8.8.8.8 (ping), 1.1.1.1:53 (DNS alternativo, só para comparar) e a página NCSI da Microsoft.
- **Varredura da rede local:** só IPv4 privado e no máximo /22 (`NetScope.CanScan`).
- **Relatório do cliente:** esconde IPs e MACs por padrão. O completo é uma opção à parte.
- **Sem binários de terceiros:** Npcap, TShark, Nmap e iPerf3 não são embutidos sem revisão de licença.
- **Administrador:** não precisa nesta fase.

## Situação dos recursos

| Recurso | Situação |
|---|---|
| RF-01 placas, IP, gateway, DNS, DHCP, MTU, velocidade | **Implementado** (leitura) |
| RF-04 pilha → roteador → internet → DNS → página, mediana/p95/variação/perda | **Implementado** |
| RF-04 portal cativo (página de teste redirecionada) | **Implementado** |
| RF-05 traceroute básico (ping com TTL, 3 amostras por salto) | **Implementado** |
| RF-06 DNS do PC × DNS alternativo, tempo e erros | **Implementado** (benchmark completo já existe na aba DNS) |
| RF-14 regras DR-001, DR-002, DR-003, DR-005 e mais 6 | **Implementado** |
| RF-16 relatório JSON (esquema 1) e HTML com dados escondidos | **Implementado**; PDF **Proposto** |
| RF-02 inventário da rede local | **Parcial** (aba Rede local existente; a política `NetScope` vale para as próximas varreduras) |
| RF-13 correção assistida (A/B) e RF-15 monitoramento | **Proposto** (Fase 2) |
| RF-07 Wi-Fi detalhado, RF-08 iPerf3 | **Proposto** (Fase 3); iPerf3 **dependente de licença** |
| RF-03, RF-09, RF-10, RF-11 | **Proposto** (Fase 4); captura **dependente de licença/hardware** |

## Como testar

- **Testes:** `dotnet test tests/MBExtreme.Core.Tests --filter DiagnosticoRedeTests` cobre os cenários da seção 11 que não precisam de laboratório.
- **No Windows:** em Otimizar › Rede › Diagnóstico, clique em **Diagnosticar**.

## Backlog

- **Próximo (Fase 2):**
  - limpar o cache de DNS e renovar o DHCP, com aviso, foto do estado anterior e registro;
  - monitorar um destino em intervalos;
  - comparar antes e depois.
- **Depois:**
  - WLAN API, com redes vizinhas e canais;
  - conexões por processo;
  - correlação IP/MAC.
