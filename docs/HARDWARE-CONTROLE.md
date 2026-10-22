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
