# MB Extreme · guia para o Claude Code

Programa de otimização e ferramentas para Windows 10/11 x64, da MB InfoArt. Interface e textos em **português do Brasil**, tema **preto e amarelo**.

## Stack e projetos

- **.NET 8 + WPF:**
  - `src/MBExtreme.App`: telas;
  - `src/MBExtreme.Core`: lógica sem interface, testável no Linux;
  - `src/MBExtreme.Installer`: o instalador `MBExtreme-Setup.exe`;
  - `src/MBExtreme.Bootstrap`: o instalador pequeno.
- **Testes:** xUnit em `tests/MBExtreme.Core.Tests`.
- **Build:** o WPF não compila fora do Windows. Aqui roda `dotnet build src/MBExtreme.Core` e `dotnet test tests/MBExtreme.Core.Tests`. A tela é conferida no CI (`.github/workflows/build.yml`).
- **Instalador:** o csproj do instalador inclui o `Themes/Theme.xaml` do App. Código que o tema usa precisa ser incluído lá também.

## Regras do projeto

- **Fluxo:** prévia, aprovação, implementação. Só enviar ao GitHub quando o dono disser "pode subir".
- **Textos:** frases curtas e claras para quem não é técnico.
- **Listas:** em ordem alfabética. Pedidos e chamados ficam na ordem em que chegaram.
- **Ajustes reversíveis:** todo ajuste do Windows guarda o valor original e se desfaz pela Central Desfazer.
- **Segredos:** nunca em arquivo ou no chat (chaves do Stripe, JSON de conta de serviço).

## Diagnóstico de rede (`src/MBExtreme.Core/NetDiag`)

Especificação: `docs/NETWORK-DIAGNOSTICS-SPEC.md`. Decisões, situação e backlog: `docs/NETWORK-DIAGNOSTICS.md`.

- **Medir antes de recomendar.** O diagnóstico é só de leitura. Correção só com plano, confirmação, foto do estado anterior, registro e verificação, nunca automática.
- **Nunca inventar medição, dispositivo, salto ou causa confirmada.** Toda conclusão é um `Finding` com evidência, contra-evidência, confiança e próximo teste.
- **Separação:** coleta em `INetProbe` (real: `SystemNetProbe`; testes: coletor falso), regras em `NetDiagRules`, relatório em `NetDiagReportWriter`.
- **Escopo:** varredura só em rede privada até /22 (`NetScope.CanScan`). Destinos externos fixos.
- **Comandos e terceiros:** nada de `cmd.exe` com texto montado. Sem binários de terceiros (Npcap, TShark, Nmap, iPerf3) sem revisão de licença.
- **Documentação:** marcar cada recurso como Implementado, Parcial, Proposto ou Dependente de licença/hardware.

## Comandos

```
dotnet build src/MBExtreme.Core -c Debug
dotnet test tests/MBExtreme.Core.Tests
dotnet test tests/MBExtreme.Core.Tests --filter DiagnosticoRedeTests
```
