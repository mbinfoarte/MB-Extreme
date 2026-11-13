# MB Control — especificação (em montagem)

Módulo novo do MB Extreme que substitui o "Calibrar controle". Nome no menu: **MB Control**.
Referências analisadas (só ideias de função; visual, textos, ícones e código são nossos): DSX (DualSenseX), DS4Windows, reWASD.

## Submódulos

### 1. Calibrador
- Teste de todos os botões, gatilhos, touchpad, giroscópio e vibração (desenho do controle reagindo ao vivo).
- Drift: medir e corrigir; zona morta; circularidade.
- Calibração gravada na memória do controle (DualSense / DS4 / Edge): centro e alcance, temporária ou permanente, backup e restauração.
- Modo para troca de analógico **Hall Effect / TMR** (magnéticos): calibração de alcance completo depois da instalação, verificação de centro e curva, relatório para o cliente (bancada técnica).
- Já existe: núcleo HID Sony, drift, circularidade, backup, relatório, histórico de atendimentos.

### 2. Emulação de controle (controle virtual)
- Objetivo: PS4, PS5 e Xbox funcionando nos jogos do Windows como controle nativo.
- Drivers: ViGEmBus (gratuito, descontinuado em 2023; cria Xbox 360 e DualShock 4 virtuais) + HidHide (gratuito; esconde o controle físico para não haver comando duplicado).
  DualSense virtual só com o VirtualPad da Nefarius (comercial, exige licença/contrato) — decisão pendente.
- Assistente guiado de instalação dos drivers (sites oficiais), tela de estado ("tudo certo"/o que falta), botão "Corrigir comando duplicado".
- Seletor do dispositivo virtual: Nativo · Xbox 360 · DualShock 4 · (DualSense se licenciado).
- Repasse de vibração, gatilhos adaptáveis e LED do jogo para o controle físico.
- Telas (inspiradas no DSX, desenho nosso): perfil do controle; mapeamento de botões clicando no desenho; zonas mortas por analógico e gatilho (Direto/Simples/Avançada, Radial/Axial, prévia com entrada, saída e zona morta); analógicos (curvas); sensores de movimento (giroscópio → mouse, analógico E/D, teclado, botões, desligado); touchpad como mouse; mais opções.
- Luz: predefinições; barra de luz (brilho, estática, multicor, respiração com velocidade/cor aleatória, estroboscópio, por bateria); seletor de cor (roda, R/G/B, hex, paleta); indicador do jogador; luz do mudo; bateria fraca piscando.
- Gatilhos adaptáveis e vibração.
- Rede (opcional, regra de firewall automática): giroscópio para emuladores (protocolo aberto DSU/cemuhook: Cemu, Yuzu/Ryujinx, Dolphin); telemetria de corrida (F1, Forza, Assetto Corsa) → gatilhos e barra de luz (RPM, freio, patinagem); canal para mods (depois).
- Do reWASD: camadas Shift, toque curto/longo/duplo, macros com tempo, alternar, turbo (com aviso: só offline), até 4 posições de perfil trocadas pelo controle (LEDs do jogador mostram 1–4), paddles do Xbox Elite / DualSense Edge.
- Leitura de controles: HID (Sony e genéricos), XInput e Windows.Gaming.Input (paddles do Elite, Switch Pro).
- Perfis por jogo ligados à Biblioteca de jogos (troca automática ao abrir o jogo); tempo de jogo da Steam.

### 3. Teclado e mouse → controle
- Teclado e mouse viram um controle virtual (Xbox 360/DS4) para jogos que só aceitam controle.
- Mouse → analógico direito com sensibilidade, curva, aceleração, zona morta de saída e suavização; teclas → botões, gatilhos e analógico esquerdo (WASD com caminhada/corrida).
- Atalho para ligar/desligar e soltar o mouse; perfis por jogo.
- Aviso obrigatório: em jogos online com mira assistida (ex.: Call of Duty, Fortnite) isso é tratado como trapaça e pode banir a conta; recomendado para jogos offline/sem mira assistida.

## Fases
1. Sem driver: visão geral ao vivo, luz, gatilhos/vibração, giroscópio → mouse e DSU, botões → teclado (Shift, macros), 4 posições, telemetria de corrida, perfis por jogo; mantém calibrador, latência, drivers/firmware.
2. Com ViGEmBus + HidHide: controle virtual Xbox 360/DS4, remapeamento/zonas mortas valendo no jogo, teclado e mouse → controle.
3. Depois: perfis da comunidade pelo site, troca de perfil pelo celular, DualSense virtual (se licenciado).

## Pendências
- Telas que o cliente ainda vai enviar.
- Decisão sobre licença do VirtualPad (DualSense virtual).
- Sites dualsensex.com e rewasd.com não abriram nesta sessão (rede); análise feita pelas imagens.

## Análise do DS4Windows (código-fonte) e DS4Windows Nexus (pacote x64)
Arquivos enviados pelo cliente; analisados sem executar nada.

- Licença: **GPL-3.0** (DS4Windows e o fork Nexus). Não copiar código para o MB Extreme (fechado); usar só como referência de comportamento.
- Arquitetura do DS4Windows:
  - Entrada: DualShock 4, DualSense (com gatilhos adaptáveis), Switch Pro, Joy-Con, DS3 (HID próprio).
  - Saída virtual: Xbox 360 e DS4 pelo ViGEm (Nefarius.ViGEm.Client).
  - Teclado/mouse: SendInput (sem driver) ou FakerInput (driver de teclado/mouse virtual, MIT).
  - HidHide (MIT) para esconder o controle físico.
  - Servidor UDP (DSU) para giroscópio em emuladores; macros; ações especiais; troca automática de perfil pelo programa em foco;
    curvas do analógico (editor Bézier); filtro One Euro para suavizar o giroscópio; debounce de botões; correção de drift.
- DS4Windows Nexus (fork, out/2026): troca o ViGEm por **VIIPER + USBIP** para criar o controle virtual — inclui **DualSense virtual**
  (pasta device/dualsense do VIIPER) sem o VirtualPad licenciado. USBip para Windows: BSD-2. A licença do próprio VIIPER **não veio
  no pacote** (só a de uma dependência): verificar antes de usar.
- Licenças dos drivers no pacote: HidHide 1.5.230 MIT · FakerInput 0.1.0 MIT · USBip 0.9.7.7 BSD-2 → podem ser distribuídos com o
  aviso de licença. ViGEmBus (fora do pacote): BSD-3.

### Consequências para o plano
- Fase 2 pode ter dois motores: ViGEmBus (Xbox 360/DS4, simples) e, se a licença do VIIPER permitir, VIIPER+USBIP (DualSense virtual de verdade).
- Teclado e mouse → controle e controle → teclado/mouse: SendInput primeiro; FakerInput opcional para jogos que ignoram SendInput.
- Giroscópio: suavização One Euro (algoritmo público) e DSU para emuladores.
