# Controle de hardware: ventoinhas, RGB, limites da GPU, remoção de driver

## Ventoinhas e refrigeração (página "Ventoinhas e refrigeração")
- Lê as ventoinhas da placa-mãe (Super I/O), da placa de vídeo e dos controladores que a biblioteca de sensores (LibreHardwareMonitor) conhece. O que o hardware não deixa controlar aparece só para leitura.
- Modos por ventoinha: **Automático (BIOS)**, **Manual** (percentual) e **Curva por temperatura** (predefinições Silenciosa/Equilibrada/Desempenho e pontos editáveis; escolha da temperatura de referência).
- **Segurança por cima de qualquer curva:** bomba de líquido nunca abaixo de 60%; cooler de CPU nunca abaixo de 30%; acima de 80 °C no mínimo 80%; acima de 90 °C, 100%; sem leitura da temperatura, 100%. A velocidade sobe na hora e desce aos poucos (histerese) para não oscilar.
- Ao fechar o MB Extreme tudo volta ao automático do firmware. **Se o programa for encerrado à força, a velocidade pode ficar fixa até reiniciar o PC.**
- Limites conhecidos: algumas placas recentes bloqueiam o controle por programas; o conector precisa estar em PWM/Auto na BIOS. Controladores de AIO/bomba só aparecem se a biblioteca de sensores os suportar.
- **Não testado em hardware real** (só com um simulador no teste automático).

## Limites da placa NVIDIA (mesma página)
Pelo `nvidia-smi` do driver: limite de potência (do mínimo até o padrão) e clock máximo. Só **reduz** (menos calor e consumo); não faz overclock. Volta ao padrão ao reiniciar ou em "Restaurar padrão". A **curva de clock/tensão completa** (estilo MSI Afterburner) e overclock **não** foram feitos: exigem APIs não públicas da NVIDIA/AMD e podem danificar a placa.

## Iluminação RGB / ARGB (página "Iluminação RGB")
Usa o **OpenRGB** (livre e gratuito; o MB Extreme não o inclui): lista os dispositivos (placa-mãe, memória, placa de vídeo, coolers, fitas, ventoinhas), aplica cor, modo e brilho, apaga os LEDs e tem "cor pela temperatura" (azul → vermelho). Feche iCUE/Armoury Crate/Mystic Light antes. O formato da listagem do OpenRGB foi lido de forma tolerante, mas **não foi testado com o OpenRGB real**.

## Monitor em tempo real (estilo HWiNFO)
Já listava todos os sensores com mínimo e máximo. Agora também: **busca de sensor**, **gravação contínua em CSV** (um registro por segundo, em Documentos\MB Extreme) e **alerta de temperatura** (80, 85, 90 ou 95 °C, com aviso e som).

## Remover driver de vídeo (página "Remover driver de vídeo")
Remove por completo o driver NVIDIA, AMD ou Intel com o `pnputil` do Windows (lista os pacotes de vídeo com `Get-WindowsDriver`), apaga as pastas que o instalador deixa (só as do fabricante), pode impedir o Windows Update de reinstalar driver de vídeo e cria um ponto de restauração antes. Para limpar também o registro, existe o DDU, programa de terceiros que **não** vem junto: a página abre o site oficial dele.

## Biblioteca de jogos
Grade de capas (Steam: capas que a própria Steam guarda no PC, com download da loja só se faltar; outros: ícone do programa). Cada jogo tem **tipo** (Competitivo, Mundo aberto/AAA, Corrida e simulação, Leve, Programa), sugerido pelo executável e pelos gêneros/categorias da loja da Steam. A otimização liga só ajustes de **risco baixo** do catálogo e dá dicas de configuração do jogo para a **marca da placa** (NVIDIA: Reflex/DLSS; AMD: Anti-Lag/FSR; Intel: XeSS) e para o nível do hardware. "Desfazer otimização" reverte só o que ela ligou, poupando o que outro jogo ainda usa. Mostra os requisitos informados pela loja.

## Biblioteca de jogos: configuração atual × recomendada (por jogo)

Ao escolher um jogo, a tabela mostra cada ponto com o valor **agora** e o **recomendado** para o tipo de jogo (✓ certo, ▲ diferente):

- **Windows**: os ajustes do pacote do tipo de jogo (gravação em segundo plano, prioridade de multimídia etc.) e a prioridade de CPU do jogo.
- **Placa de vídeo usada pelo jogo**: a mesma opção de *Configurações do Windows > Tela > Elementos gráficos* (chave `HKCU\Software\Microsoft\DirectX\UserGpuPreferences`).
- **Driver NVIDIA** (só com placa GeForce): *Modo de gerenciamento de energia* e *Quadros pré-renderizados* (baixa latência), gravados no perfil do próprio jogo pela interface pública NVAPI (grupo DRS), a mesma que o Painel de Controle da NVIDIA usa. Se o jogo não tem perfil, o MB Extreme cria um chamado "MB Extreme - jogo.exe".
- **DLSS**: lê a versão de `nvngx_dlss.dll` na pasta do jogo e diz se é antiga. **O MB Extreme não troca esse arquivo** (pode acionar antitrapaça); para atualizar use "Substituição do DLSS" da NVIDIA App.

Nada é gravado sem você clicar em **Otimizar**; as mudanças de driver e de placa pedem confirmação listando "antes → depois". O valor de antes fica em `%AppData%\MBExtreme\game-driver.json` e **Desfazer otimização** devolve (ou remove a configuração do perfil, se ela não existia). Reflex/Anti-Lag continuam sendo opções dentro do jogo.

**Validação pendente:** o acesso ao NVAPI foi escrito a partir da interface pública e não pôde ser testado sem uma placa NVIDIA. Em caso de falha o programa mostra o código de erro e não altera nada. Teste primeiro com um jogo sem importância.
