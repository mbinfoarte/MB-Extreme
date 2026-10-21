# MB Extreme

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

Baixe `MBExtreme-Setup.exe` na página de releases do repositório e execute. O instalador mostra uma janela com barra de progresso e um mini tutorial do sistema, copia o programa para `C:\Program Files\MB Extreme`, cria atalhos e registra o programa em "Aplicativos instalados". Para remover, use "Aplicativos instalados" do Windows (ou `MBExtreme.exe /uninstall`). Também há uma versão portátil (`MBExtreme-portatil-win-x64.zip`).

## Como compilar

Requer Windows 10/11 e o [.NET 8 SDK](https://dotnet.microsoft.com/download).

```
dotnet test tests/MBExtreme.Core.Tests
dotnet run --project src/MBExtreme.App
```

O app pede privilégio de administrador (limpar a lista de espera da RAM e alterar o DNS exigem isso).

O build do Windows também roda no GitHub Actions (`.github/workflows/build.yml`) e publica o executável como artefato.

## Estado atual (v0.3)

- **Painel** (tela inicial): medidores de temperatura da CPU e da placa de vídeo, uso de memória e ping; gráficos de uso da internet e da CPU; ventoinhas e consumo de energia. Os sensores usam a biblioteca LibreHardwareMonitor (veja `THIRD-PARTY.md`); para ler a temperatura da CPU é preciso o driver PawnIO (o painel oferece instalar pelo winget).
- **Menu por módulos** (Otimização, Rede, Windows, Ferramentas): ao clicar, aparecem atalhos com ícones; cada atalho abre a página com botão de voltar.
- **Limpeza**: escaneia temporários, Prefetch, relatórios de erro, cache de navegadores, miniaturas e memória RAM; mostra o que foi encontrado e só apaga o que você marcar. Nunca toca em cookies, senhas, histórico ou documentos.
- **Ajustes reversíveis** (23), remoção de aplicativos do Windows, Memória e DNS, como nas versões anteriores.
- Visual: fundo preto, cartões arredondados e botões amarelos com texto preto. Janela de progresso padrão e instalador (.exe) com mini tutorial.
- Páginas ainda em desenvolvimento: Armazenamento, Analisador de rede, Hardware e benchmarks, Drivers e programas, Central de ações.

## Política de desenvolvimento

Veja `docs/CLEAN-ROOM.md`: funcionalidades próprias, sem copiar código, binários ou recursos de outros produtos.
