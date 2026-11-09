# MB Extreme × Hone — o que temos e o que falta

Comparação feita a partir da lista de recursos do Hone (16 prints). O MB Extreme já tem 59 otimizações (53 no catálogo + 6 por comando) e várias telas que o Hone não tem.
Regra que continua valendo: **nada que desligue Defender, Firewall, Windows Update, BitLocker, UAC ou SmartScreen de forma automática.**

## 1. O que o MB Extreme já cobre

| Hone | MB Extreme |
|---|---|
| Game Mode (Balanced / Performance / Custom), Lite e Power Preset | Modo Jogo e perfis de otimização (Recomendado, Jogos, Avançado) |
| Optimize FPS & Input Lag | Prioridade do primeiro plano, agendador multimídia (MMCSS), tempo reservado a tarefas de fundo, tela cheia sem Game Bar, MPO, HAGS |
| Optimize NVIDIA Performance | Perfil da NVIDIA pelo driver (NvApiDrs) na tela do jogo |
| Optimize Network Performance | Limite de pacotes, QoS, Nagle, RSS, RSC, placa de rede sem economia de energia, IPv6 |
| Optimize Storage & Memory | TRIM, data do último acesso, nomes 8.3, SysMain, liberar RAM, limpeza automática de memória |
| Reduce Windows Background Activity | Apps da Loja em segundo plano, EcoQoS, telemetria sob demanda, serviço de mapas |
| Disable Hibernation | Desativar a hibernação |
| Maximum Performance Power Mode | Plano Desempenho Máximo, processador sempre no máximo, núcleos sem estacionar, PCIe e USB sem economia |
| Disable Windows GameBar | Game DVR / gravação em segundo plano |
| Optimize Mouse / Sticky Keys | Aceleração do mouse, teclas de aderência, repetição rápida do teclado |
| Reduce Ads & Tracking / Device Tracking | Anúncios, recentes, Copilot, Recall, Widgets, Notícias, dicas da tela de bloqueio, Iniciar, Explorador, Cortana |
| Set Visual Effects / Disable Transparency | Transparência, animações, atraso dos menus, Aero Peek, barra de tarefas menor |
| Disable VBS & Hyper-V | Integridade da Memória/VBS e hipervisor (risco alto, com aviso) |
| Disable Superfetch (Sysmain) | Desativar o SysMain |
| Disable NVIDIA Telemetry | NVIDIA: desativar a telemetria (e AMD: telemetria, ULPS, Crash Defender) |
| Disable Driver Updates | Impedir o Windows Update de trocar drivers |
| Disable Fullscreen Optimization | Tela cheia sem a camada do Game Bar |
| Junk Cleaner, Log & Crash Cleaner, Thumbnail Cache | Limpeza (temporários, prefetch, relatórios de erro, navegadores, miniaturas, shaders, Windows Update) + Mapa do disco + Arquivos duplicados |
| System Fixer | Reparar o Windows (SFC, DISM, componentes, Windows Update, Loja, WMI, rede) |
| Drive Fixer / Drive Optimizer | Verificar o disco (chkdsk), TRIM e otimização em Saúde dos discos |
| Games Library | Biblioteca de jogos com capas, perfis por jogo, requisitos e comparação |

**O que só o MB Extreme tem:** Painel com nota de saúde, Central Desfazer, pontos de restauração, benchmark completo (3D, IA, dia a dia), teste de estresse, FPS dos jogos, gargalos, teste de internet com mapa, estúdio de gravação/live, Shorts, drivers, calibração de controles, backup para formatar, pós-formatação e suporte remoto.

## 2. O que vale acrescentar (seguro e com como desfazer)

**Situação:** implementado, exceto a predefinição de efeitos visuais (já coberta pelos itens de transparência, animações e menus; gravar a máscara binária do Windows não tem desfazer confiável). A Desinstalação do OneDrive e do Xbox já existia, o instalador de driver NVIDIA já existia em Drivers e os navegadores já tinham o ajuste de segundo plano.

| Recurso do Hone | Proposta para o MB Extreme | Risco |
|---|---|---|
| Optimize Message Signal Interrupts | Ligar o modo MSI na placa de vídeo e na placa de rede (quando o driver suporta), com desfazer | Médio |
| Optimize Browser Background | Chrome e Edge sem rodar em segundo plano e sem "inicialização acelerada" | Baixo |
| Optimize Windows Explorer / Context Menu | Menu do botão direito clássico no Windows 11, modo compacto, sem o sufixo "- Atalho", abrir em "Este Computador" | Baixo |
| Set Visual Effects For performance | Predefinição "melhor desempenho" dos efeitos visuais, mantendo fontes suaves | Baixo |
| Optimize JPEG Wallpaper Quality | Papel de parede sem perda de qualidade (100%) | Baixo |
| Disable Lock Screen / Lock Screen Blur | Ir direto para a senha; sem desfoque na tela de login | Baixo |
| Disable Notifications / Focus Assist | Opção "sem notificações durante jogos" (já existe no Modo Jogo) e a versão permanente | Baixo |
| Show PC Boot Information / Reduce Boot Menu Timeout | Mensagens detalhadas na inicialização e menu de boot em 3 s | Baixo |
| Disable Storage Sense / Windows Insider | Desligar o Sensor de Armazenamento e esconder o Insider | Baixo |
| Disable OneDrive / Xbox / Classic Photo Viewer | OneDrive e Xbox já saem no Desinstalador; falta o Visualizador de Fotos clássico | Baixo |
| Maximize Receive and Transmit Buffer / Large Send Offload | Buffers da placa de rede no máximo; LSO como opção avançada | Médio |
| Disable Browser Hardware Acceleration | Só como dica (piora vídeo no navegador na maioria dos PCs) | — |
| Enable DLSS Indicator | Indicador do DLSS na tela (NVIDIA) para conferir se está ativo | Baixo |
| Discord Cache/Session Cleaner, Steam Cleaner | Novas categorias na Limpeza: cache do Discord, cache e logs do Steam | Baixo |
| Font Cache, Icon Cache, Recent Documents | Reparos: refazer o cache de fontes e de ícones; limpeza de documentos recentes | Baixo |
| NVIDIA Driver Installer | Na tela Drivers: baixar e instalar o Game Ready mais novo pelo site da NVIDIA | Médio |
| Process Explorer / Registry editor | No kit "Técnico" em Programas (Sysinternals pelo winget) | Baixo |
| Filtros FPS & Latency / Network & Ping / Quality of Life / Privacy & Security | Filtros por objetivo na tela Otimizações, contador "x de y ativas", selos Novo e Atualizado e botão "i" com explicação | — |

## 3. O que fica de fora (segurança)

| Recurso do Hone | Por quê |
|---|---|
| Disable Windows Defender, SmartScreen, UAC, Windows Automatic Updates, Mitigations | Deixa o PC exposto a vírus e falhas conhecidas (regra do projeto) |
| Disable User Choice Protection Driver (UCPD) | Mexe em proteção do Windows contra troca de programas padrão |
| Disable Browser Updates | O navegador é a porta de entrada mais atacada |
| Bypass Windows 11 Checks | Instalar sem os requisitos pode impedir atualizações da Microsoft (a tela Windows 11 só explica o que falta) |
| Disable VPN Support | Quebra VPNs de trabalho e de jogos |
| Optimize Device Affinities | Ganho incerto e fácil de piorar; fica só a medição de latência (DPC) |
| Unlock EU Privacy Mode, Modern Memory Management, Windows Boot Interface | Sem documentação clara do que mudam; só com fonte confiável |

## 4. Instalador pequeno (igual ao do Hone)

Hoje o instalador tem ~200 MB porque leva o programa inteiro (com FFmpeg). Proposta:

- **MBExtreme-Instalar.exe (~0,2 a 3 MB):** um instalador "online" que só baixa a versão mais nova, mostra o progresso com a marca MB, confere o SHA-256 e instala.
- Feito em .NET Framework 4.8, que já vem no Windows 10 e 11, então o arquivo fica bem pequeno. A outra opção é Native AOT (~3 MB).
- O instalador completo (~200 MB) continua existindo para PCs sem internet.
- **Depende de:** o repositório ficar público (ou o servidor estar no ar), porque o download não pode pedir login.
- **Mexe no CI:** compilar e assinar o instalador pequeno e publicá-lo junto na Release. Precisa da sua autorização.

**Situação:** o projeto `src/MBExtreme.Bootstrap` está pronto. Gera o `MBExtreme-Instalar.exe` com ~175 KB, em .NET Framework 4.8 e com o tema MB. Ele procura a Release mais nova, baixa com progresso, confere o SHA-256 e abre o instalador completo, que pede administrador. Ele já foi incluído no CI: é assinado junto com o instalador completo e publicado em cada Release. Os passos são estes:

```yaml
      - name: Instalador pequeno
        run: dotnet build src/MBExtreme.Bootstrap/MBExtreme.Bootstrap.csproj -c Release "-p:Version=1.0.${{ github.run_number }}"
      # assinar com o mesmo signtool do instalador completo
      # e incluir src/MBExtreme.Bootstrap/bin/Release/net48/MBExtreme-Instalar.exe no gh release create
```
