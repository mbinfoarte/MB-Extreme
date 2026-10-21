# MB Extreme

Software de otimização completa para Windows, da MB InfoArt. Visual preto e laranja.

## Objetivo

1. **Otimização de ping e rota**: mede latência e perda de pacotes por caminho e escolhe a melhor rota.
2. **Desempenho do Windows**: ajustes reversíveis, limpeza de RAM, inicialização, DNS.
3. **IA de decisão**: aprende o perfil de uso e de rede e recomenda ou aplica ajustes.
4. **Ferramentas**: informações de hardware, sensores, benchmarks, drivers e programas.
5. **Instalador Windows** com atualização automática.

## Stack

- **.NET 8 + WPF** (C#): interface e lógica de Windows (registro, serviços, rede).
- `src/MBExtreme.Core`: núcleo sem interface (memória, DNS, progresso, configurações).
- `src/MBExtreme.App`: aplicativo WPF com menu lateral e tema preto e laranja.
- `tests/MBExtreme.Core.Tests`: testes de unidade (xUnit).

## Estrutura

```
src/MBExtreme.Core/    Núcleo: Memory, Dns, Progress, Settings
src/MBExtreme.App/     Interface: Themes, Controls (progresso), Pages, MainWindow
tests/                 Testes
docs/                  Especificações, arquitetura, roadmap e política de desenvolvimento
installer/             Empacotamento do instalador (a fazer)
scripts/               Automação (a fazer)
```

## Como compilar

Requer Windows 10/11 e o [.NET 8 SDK](https://dotnet.microsoft.com/download).

```
dotnet test tests/MBExtreme.Core.Tests
dotnet run --project src/MBExtreme.App
```

O app pede privilégio de administrador (limpar a lista de espera da RAM e alterar o DNS exigem isso).

O build do Windows também roda no GitHub Actions (`.github/workflows/build.yml`) e publica o executável como artefato.

## Estado atual (v0.1)

- Janela com menu lateral, tema preto e laranja e **janela de progresso padrão** (anel com porcentagem, etapas, parar).
- **Início**: análise rápida (somente leitura) de memória e DNS.
- **Memória**: status, limpeza (processos e lista de espera) e lista de aplicativos protegidos.
- **DNS**: teste de provedores, ranking, aplicar e restaurar.
- Demais abas aparecem como "Em desenvolvimento". Veja `docs/ROADMAP.md`.

## Política de desenvolvimento

Veja `docs/CLEAN-ROOM.md`: funcionalidades próprias, sem copiar código, binários ou recursos de outros produtos.
