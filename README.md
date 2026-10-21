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

## Estado atual (v0.2)

- Janela com menu lateral organizado por grupos (Otimizar, Rede, Windows, Ferramentas), tema preto e laranja e **janela de progresso padrão**.
- **Motor de ajustes reversíveis**: cada ajuste guarda o valor original antes de mudar; risco baixo, médio ou alto; confirmação para risco alto.
- **Ajustes**: Desempenho, Jogos e GPU, Otimizações de rede, Privacidade, Inicialização e boot, Periféricos (23 ajustes).
- **Aplicativos**: remoção individual de apps do Windows, com ponto de restauração.
- **Memória** e **DNS**, como na v0.1.
- Demais abas aparecem como "Em desenvolvimento". Veja `docs/CATALOGO-AJUSTES.md` e `docs/ROADMAP.md`.

## Política de desenvolvimento

Veja `docs/CLEAN-ROOM.md`: funcionalidades próprias, sem copiar código, binários ou recursos de outros produtos.
