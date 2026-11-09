<p align="center">
  <img src="src/MBExtreme.App/Assets/logo.png" width="96" alt="MB Extreme">
</p>

<h1 align="center">MB Extreme</h1>

<p align="center">
  <b>Otimizador completo para Windows, com segurança e tudo reversível.</b><br>
  Jogos, rede, limpeza, reparo, benchmark e estúdio de gravação num só programa. Da MB InfoArt.
</p>

<p align="center">
  <a href="https://github.com/mbinfoarte/MB-Extreme-Download/releases/latest/download/MBExtreme-Beta-Setup.exe"><b>⬇ Baixar o instalador</b></a> ·
  <a href="https://github.com/mbinfoarte/MB-Extreme-Download/releases/latest/download/MBExtreme-Instalar.exe">Instalador pequeno (online)</a> ·
  <a href="https://github.com/mbinfoarte/MB-Extreme-Download/releases/latest/download/MBExtreme-Beta-portatil-win-x64.zip">Versão portátil</a> ·
  <a href="https://mbinfoarte.github.io/MB-Extreme/">Site</a> ·
  <a href="https://github.com/mbinfoarte/MB-Extreme-Download/releases">Todas as versões</a>
</p>

<p align="center">
  <img alt="Windows 10 e 11" src="https://img.shields.io/badge/Windows-10%20%7C%2011-0b0b0e?style=flat-square&labelColor=ffc400&color=0b0b0e">
  <img alt="Versão" src="https://img.shields.io/github/v/release/mbinfoarte/MB-Extreme-Download?style=flat-square&label=vers%C3%A3o&labelColor=0b0b0e&color=ffc400">
</p>

---

## Instalar

1. Baixe o **[MBExtreme-Beta-Setup.exe](https://github.com/mbinfoarte/MB-Extreme-Download/releases/latest/download/MBExtreme-Beta-Setup.exe)**. Se preferir um arquivo pequeno, baixe o **[MBExtreme-Instalar.exe](https://github.com/mbinfoarte/MB-Extreme-Download/releases/latest/download/MBExtreme-Instalar.exe)** (~200 KB), que baixa e confere a versão mais nova sozinho.
2. Execute e aceite o pedido de administrador. O programa mexe em configurações do Windows.
3. Pronto: o MB Extreme abre no Painel com a nota de saúde do PC.

Requisitos: Windows 10 ou 11 (64 bits). Os links acima sempre levam à versão mais nova. Para remover, use **Aplicativos instalados** do Windows.

> Se o Windows mostrar "editor desconhecido", clique em **Mais informações** e depois em **Executar assim mesmo**. Baixe somente por esta página ou pelo site.

## Destaques

- **Otimizações com desfazer.** São mais de 80 ajustes, com filtros por objetivo: FPS e latência, Rede e ping, Qualidade de vida, Privacidade e segurança. Cada um mostra o risco e guarda o valor original. A **Central Desfazer** volta tudo.
- **Biblioteca de jogos.** Capas, otimização por jogo, perfil da NVIDIA, requisitos e FPS antes e depois.
- **Benchmark completo.** Processador, memória, disco, IA, uso do dia a dia e janela 3D separada, com notas por categoria e comparação.
- **Rede.** Teste de internet com mapa, ping, rota, DNS e ajustes da placa de rede.
- **Limpeza e reparo.**
  - limpeza com prévia: Windows, navegadores, Discord, Steam e shaders
  - Mapa do disco e arquivos duplicados
  - Reparar o Windows: SFC, DISM, CHKDSK, Windows Update, Loja, cache de ícones e de fontes
- **Estúdio.** Gravação, transmissão ao vivo, replay e Shorts verticais.
- **Ferramentas.**
  - drivers e programas (pelo winget)
  - calibração de controles DualSense e DualShock 4
  - backup para formatar e pós-formatação
  - suporte remoto
- **Segurança em primeiro lugar.** O MB Extreme nunca desliga sozinho o Defender, o Firewall, o Windows Update, o BitLocker, o UAC nem o SmartScreen.

## Tudo o que o MB Extreme faz

Detalhes em `docs/ESPECIFICACAO-FERRAMENTAS.md`; a base das recomendações (testes publicados e fontes) está em `docs/PESQUISA-OTIMIZACAO-GAMER.md`.

- **Painel**: medidores de temperatura da CPU e da GPU, memória e ping; gráfico animado de download e envio com ping, latência, oscilação, ping do roteador, DNS em uso e análise de rota; lista de itens que precisam de atenção. Clique nos medidores para abrir os detalhes. Janela sem moldura, com os botões dentro do layout. Sensores via LibreHardwareMonitor (`THIRD-PARTY.md`); para temperatura da CPU é preciso o driver PawnIO (o painel oferece instalar pelo winget).
- **Otimização**: Perfis (Jogo, Competitivo, PC fraco), Limpeza segura, Inicialização (liga/desliga programas), Desempenho, Efeitos visuais, Memória e Armazenamento (saúde e otimização de SSD/HD).
- **Jogos**: Modo Jogo automático, energia e agendamento de GPU, Diagnóstico de latência (mostra as configurações do Windows que afetam a latência, com liga/desliga, e mede qual driver causa micro-travadas), Assistente de jogo, Monitor de FPS com overlay e **Gravação e transmissão**: grava a tela inteira ou só a janela do jogo, transmite ao vivo (Twitch, YouTube ou outro servidor RTMP) e guarda os melhores momentos com o **replay** (Ctrl + Shift + F9), com som do PC e microfone, pela placa de vídeo (1080p, 1440p e 4K, 30 ou 60 fps).
- **Testes**: Processador, Placa de vídeo, Memória (com limpeza automática), Discos (velocidade ao vivo e espaço livre/usado) e Pendrives e HDs externos (com remoção segura), Informações do sistema, Monitor em tempo real (clock e uso por núcleo, tensões, temperaturas), Benchmark (CPU, memória com latência, disco e GPU em 3D), Antes e depois e Teste de estresse (geral e por núcleo).
- **Hardware e BIOS**: leitura das peças com alertas (XMP/EXPO, canal único, Windows 24H2, BIOS antiga, microcódigo Intel, chipset AMD), página de BIOS e Receita de BIOS/PBO. O app orienta; nunca atualiza BIOS sozinho.
- **Dispositivos**: lista de USB e do hardware, cache de portas USB.
- **Rede**: DNS, ajustes, Analisador de rede (Ping, TCPing, MTR, jitter) e Wi-Fi.
- **Ferramentas**: Processos, Arquivos grandes, Arquivos duplicados, Pastas vazias e atalhos inválidos, Destruir arquivos e Desligamento automático.
- **Suporte**: Suporte remoto com o AnyDesk (o cliente prepara, passa o ID e aceita a conexão; o técnico conecta pelo ID).
- **Licença**: ativação por conta do site (plano gratuito e Pro) com licença assinada, renovação automática e uso offline; ver `docs/LICENCIAMENTO.md`. Desligada por padrão no beta aberto.
- **Windows**: Segurança (antivírus, firewall, UAC), privacidade, remoção de aplicativos, boot, periféricos e **Reparo do Windows** (SFC, DISM, CHKDSK, Windows Update, loja, WMI, rede e cache de ícones, com a saída dos comandos ao vivo).
- **Drivers e programas**: driver da NVIDIA direto do servidor oficial, AMD/Intel pelo Windows Update, programas pelo winget.
- Todo ajuste mostra o risco, guarda o valor original e pode ser restaurado. Visual preto com cartões arredondados e botões amarelos; janela de progresso padrão e instalador (.exe) com mini tutorial.

## Para desenvolvedores

Feito em **.NET 8 + WPF (C#)**.

| Pasta | O que é |
|---|---|
| `src/MBExtreme.Core` | Núcleo sem interface (ajustes, limpeza, reparo, benchmark, rede...) |
| `src/MBExtreme.App` | Aplicativo WPF com tema preto e amarelo |
| `src/MBExtreme.Installer` | Instalador completo (.exe) com o programa embutido |
| `src/MBExtreme.Bootstrap` | Instalador pequeno (.NET Framework 4.8) que baixa a versão mais nova |
| `src/MBExtreme.LicenseServer` | Site, contas, licenças e afiliados (também gera o site estático do GitHub Pages) |
| `tests/` | Testes de unidade (xUnit) |
| `docs/` | Especificações, roteiro de testes e documentação |

```
dotnet test tests/MBExtreme.Core.Tests
dotnet run --project src/MBExtreme.App
```

Cada envio compila no GitHub Actions (`.github/workflows/build.yml`) e publica uma versão nova em **Releases**. O site é publicado por `.github/workflows/pages.yml`.

## Política de desenvolvimento

Veja `docs/CLEAN-ROOM.md`: funcionalidades próprias, sem copiar código, binários ou recursos de outros produtos.
