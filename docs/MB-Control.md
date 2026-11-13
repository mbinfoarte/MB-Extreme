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
