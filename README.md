# MB Extreme Beta

Software de otimização completa para Windows, da MB InfoArt. Visual preto, com cartões arredondados e botões amarelos.

## Objetivo

1. **Otimização de ping e rota**: mede latência e perda de pacotes por caminho e escolhe a melhor rota.
2. **Desempenho do Windows**: ajustes reversíveis, limpeza de RAM, inicialização, DNS.
3. **IA de decisão**: aprende o perfil de uso e de rede e recomenda ou aplica ajustes.
4. **Ferramentas**: informações de hardware, sensores, benchmarks, drivers e programas.
5. **Instalador Windows** com atualização automática.

## Stack

- **.NET 8 + WPF** (C#): interface e lógica de Windows (registro, serviços, rede).
- `src/MBExtreme.Core`: núcleo sem interface (memória, DNS, progresso, configurações).
- `src/MBExtreme.App`: aplicativo WPF com menu lateral e tema preto e amarelo.
- `src/MBExtreme.Installer`: instalador (.exe) com barra de progresso e mini tutorial; embute o aplicativo.
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

## Instalação (usuário)

Baixe `MBExtreme-Beta-Setup.exe` na página de releases do repositório e execute. O instalador mostra uma janela com barra de progresso e um mini tutorial do sistema, copia o programa para `C:\Program Files\MB Extreme`, cria atalhos e registra o programa em "Aplicativos instalados". Para remover, use "Aplicativos instalados" do Windows (ou `MBExtreme.exe /uninstall`). Também há uma versão portátil (`MBExtreme-Beta-portatil-win-x64.zip`).

## Como compilar

Requer Windows 10/11 e o [.NET 8 SDK](https://dotnet.microsoft.com/download).

```
dotnet test tests/MBExtreme.Core.Tests
dotnet run --project src/MBExtreme.App
```

O app pede privilégio de administrador (limpar a lista de espera da RAM e alterar o DNS exigem isso).

O build do Windows também roda no GitHub Actions (`.github/workflows/build.yml`) e publica o executável como artefato.

## O que o MB Extreme Beta faz

Detalhes em `docs/ESPECIFICACAO-FERRAMENTAS.md`; a base das recomendações (testes publicados e fontes) está em `docs/PESQUISA-OTIMIZACAO-GAMER.md`.

- **Painel**: medidores de temperatura da CPU e da GPU, memória e ping; gráficos de internet e CPU. Sensores via LibreHardwareMonitor (`THIRD-PARTY.md`); para temperatura da CPU é preciso o driver PawnIO (o painel oferece instalar pelo winget).
- **Otimização**: Perfis (Jogo, Competitivo, PC fraco), Limpeza segura, Inicialização (liga/desliga programas), Desempenho, Efeitos visuais, Memória e Armazenamento (saúde e otimização de SSD/HD).
- **Jogos**: Modo Jogo automático, energia e agendamento de GPU, Diagnóstico de latência (driver que causa micro-travadas), Assistente de jogo, Monitor de FPS com overlay e Gravação de tela pela placa de vídeo (1080p, 1440p e 4K, 30 ou 60 fps).
- **Testes**: Informações do sistema, Monitor em tempo real (clock e uso por núcleo, tensões, temperaturas), Benchmark (CPU, memória com latência, disco e GPU em 3D), Antes e depois e Teste de estresse (geral e por núcleo).
- **Hardware e BIOS**: leitura das peças com alertas (XMP/EXPO, canal único, Windows 24H2, BIOS antiga, microcódigo Intel, chipset AMD), página de BIOS e Receita de BIOS/PBO. O app orienta; nunca atualiza BIOS sozinho.
- **Dispositivos**: lista de USB e do hardware, cache de portas USB.
- **Rede**: DNS, ajustes, Analisador de rede (Ping, TCPing, MTR, jitter) e Wi-Fi.
- **Windows**: privacidade, remoção de aplicativos, boot e periféricos.
- **Drivers e programas**: driver da NVIDIA direto do servidor oficial, AMD/Intel pelo Windows Update, programas pelo winget.
- Todo ajuste mostra o risco, guarda o valor original e pode ser restaurado. Visual preto com cartões arredondados e botões amarelos; janela de progresso padrão e instalador (.exe) com mini tutorial.

## Política de desenvolvimento

Veja `docs/CLEAN-ROOM.md`: funcionalidades próprias, sem copiar código, binários ou recursos de outros produtos.
