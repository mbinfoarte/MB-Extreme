# Roteiro de testes do MB Optimizer no Windows

Use uma máquina virtual com **snapshot** (Hyper-V, VirtualBox ou VMware) para o que altera o sistema, e o seu PC para o resto.
Marque cada item e anote o que der diferente.

## Antes de começar
- [ ] Snapshot da máquina virtual criado.
- [ ] Windows 10 22H2 **e** Windows 11 (23H2 ou 24H2), se possível.
- [ ] Conta de administrador (o programa pede elevação).

## Critérios de aceite do beta (do plano)
| Critério | Como conferir | OK? |
|---|---|---|
| A análise não modifica o Windows | Rode a análise rápida e a profunda; depois, Central Desfazer não deve ter nada novo | |
| Reparos só com confirmação | Em "Corrigir", cancele a pergunta: nada deve rodar | |
| Não trava em operações demoradas | Análise profunda e DISM: a janela continua respondendo | |
| Cancelamento funciona | "Parar" na análise, na fila e no teste de internet | |
| Atualizações não simultâneas | Atualizar 3 programas: um de cada vez; tentar outra fila ao mesmo tempo é recusado | |
| Ajustes voltam ao valor anterior | Aplicar 3 ajustes, desfazer pela Central e conferir no registro/Configurações | |
| Funciona sem admin onde não precisa | (o programa hoje sempre pede administrador; anotar se algo deveria funcionar sem) | |
| Relatórios com resultados reais | Exportar o relatório do Painel e o da pós-formatação | |
| Módulos antigos continuam funcionando | Limpeza, Ajustes, Estúdio, Jogos, Benchmark | |
| Regressão | CI verde no GitHub | |

## Fase 1 — núcleo e Central Desfazer
- [ ] Abrir o programa duas vezes: a segunda só traz a primeira para frente.
- [ ] Alt + ← e o botão lateral do mouse voltam de página.
- [ ] Selo "Licença: Grátis" no cartão do cliente sem Pro; some com Pro.
- [ ] Central Desfazer lista ajustes aplicados; "Restaurar selecionados" volta e confere.
- [ ] Desligar o PC durante uma operação: ao abrir, ela aparece como "interrompida".

## Fase 2 — Painel inteligente
- [ ] Análise rápida em menos de 1 minuto, com "Etapa X de Y" e tempo.
- [ ] Análise profunda reaproveita a rápida (até 10 min) e mostra DISM/SFC/CHKDSK.
- [ ] "Corrigir selecionados" cria ponto de restauração e mostra cada item; no fim "Antes → Depois".
- [ ] Itens "não verificável" aparecem separados, não como erro.

## Fase 3 — Reparação
- [ ] Adiantar o relógio do Windows 10 min: o Painel acusa e "Acertar o relógio" corrige.
- [ ] Desativar o serviço Windows Update: o Painel acusa; o reparo religa.
- [ ] Com atualização pendente de reinício: aviso no Reparo do Windows.
- [ ] Tempo limite de 5 min num comando comum que trave é respeitado; DISM/SFC só mostram o aviso.

## Fase 4 — Limpeza e discos
- [ ] "Ver arquivos" mostra a lista; "Nunca limpar esta pasta" funciona e vale na limpeza semanal.
- [ ] Prefetch vem desmarcado.
- [ ] Com Windows.old, aparece o tamanho e o botão da Limpeza de Disco.
- [ ] Armazenamento mostra TRIM, otimização automática, Proteção do Sistema por unidade e a evolução do disco (abrir em dias diferentes).

## Fase 5 — Otimizações e pós-formatação
- [ ] No Windows 10 não aparecem Recall/Widgets; aparecem Notícias e interesses e barra menor.
- [ ] No Windows 11 24H2 aparece Recall; não aparece barra menor.
- [ ] Botões Seguros/Moderados/Avançados/Todos/Nenhum marcam o grupo certo.
- [ ] Pós-formatação: trocar o perfil muda as marcações; risco alto nunca vem marcado; relatório salvo em Documentos.

## Fase 6 — Programas
- [ ] Inventário com ícones e busca; apps da Loja aparecem.
- [ ] Atualizar um programa aberto: aparece o aviso.
- [ ] Desinstalar mostra a janela do desinstalador e o resultado.
- [ ] Erro do winget sem internet aparece em português.

## Fase 7 — Periféricos e internet
- [ ] Teclado: teclas acendem; contagem de teclas juntas.
- [ ] Mouse: botões laterais, roda, taxa (1000 Hz num mouse gamer).
- [ ] Controle Xbox: botões, gatilhos, analógicos, deriva, circularidade e vibração.
- [ ] Internet: valores coerentes com a sua conexão; histórico compara.

## Fase 8 — Interface
- [ ] Narrador (Ctrl + Win + Enter) lê os botões, inclusive os só com ícone.
- [ ] Tab mostra o contorno amarelo.
- [ ] Escala 90% a 150% e Ctrl +/−/0; nada cortado em 1366×768.

## Visual novo (beta-47): menu em seções, Painel com nota e telas novas

| Tela | O que testar | Esperado |
|---|---|---|
| Menu lateral | Clicar em cada item de cada seção; Ctrl + / Ctrl −; "Tamanho da letra"; versão no rodapé | Abre a tela certa, item destacado em amarelo; a versão responde mesmo sem atualização |
| Painel | ANALISAR; Parar no meio; Corrigir tudo; Resolver; Ignorar; Mostrar ignorados | Itens aparecem um a um; nota 0–100 com cor; ignorado não tira nota; correção pede confirmação e cria ponto de restauração |
| Painel (cards) | Clicar em Memória, Disco do Windows e no card do PC | Abrem Memória, Mapa do disco e Meu PC |
| Programas | Kit Gamer; filtrar categoria; buscar; Instalar (2 programas pequenos) | "Já instalado" aparece; instala um de cada vez; erro em português se falhar |
| Desinstalador | Abas Programas instalados e Aplicativos do Windows | Lista abre direto; desinstalar abre o desinstalador do programa |
| Windows 11 | Abrir no Windows 10 e no 11 | 10 requisitos com ✔/✖/?; dica no TPM/UEFI quando faltam |
| Drivers | Desativar um aparelho no Gerenciador e abrir Drivers | Aparece em "Aparelhos com problema" (código 22); placa-mãe com link do fabricante; lista de drivers com busca |
| Rede | Testar com nome; repetir | Roteador, ping, DNS, download, upload e sob carga; nota do ping sob carga; histórico com o nome |
| Periféricos | Tela, Alto-falantes (esquerdo/direito), Microfone (barra e gravar 5 s), Webcam (foto) | Cores em tela cheia com Esc; som no lado certo; barra mexe; foto aparece |
| Mapa do disco | Clicar no C:, entrar em pastas, Voltar, Abrir no Explorador | Blocos proporcionais; nada é apagado |
| Pontos de restauração | Criar com nome (2 vezes seguidas); Apagar; Voltar (só em máquina virtual) | Cria mesmo com outro ponto nas últimas 24 h; apagar pede confirmação |
| Backup para formatar | Backup num pendrive; Restaurar na mesma máquina; abrir Pós-formatação com o pendrive | Pasta MBExtreme-Backup com backup.json; Pós-formatação avisa que achou o backup |
| Pendrive original? | Pendrive pequeno (8–16 GB) | Escreve, confere e apaga os arquivos de teste; resultado "Capacidade verdadeira" |
| Inicialização | Abrir | Tempo para ligar (ou "Ainda sem medição"); "O que é" e selo em cada programa |
| Otimizações | Abrir no plano grátis com a licença ligada | Desempenho, jogos e rede não aparecem (aviso do Pro); os demais sim |
| Sobre | O que há de novo; Verificar atualização; Abrir pasta | Janela de novidades; também abre sozinha 1 vez depois de atualizar |

## Benchmark novo, menu de vidro e efeito cristal

| Onde | O que fazer | O que deve acontecer |
|---|---|---|
| Benchmark → Novo teste | Escolher Rápido, Padrão e Preciso | Cartão escolhido fica de vidro com borda amarela; duração estimada muda no rodapé (~30 s, ~1 min, ~2 min) |
| Benchmark → O que testar | Desmarcar Disco e depois Placa de vídeo | Somem as seções de disco e de resolução; sem processador ou memória avisa "sem a nota geral" |
| Benchmark → Disco | PC com 2 discos: escolher o D: e rodar o Rápido | Grava e lê em D:\MBExtreme-teste-de-disco e apaga a pasta no fim; disco sem espaço aparece apagado |
| Benchmark → Resolução | Monitor 1080p, depois um Quad HD/4K | 1080p mostra só 720p e 1080p; Quad HD/4K aparecem quando o monitor aguenta; ultrawide mostra "Nativa do monitor" |
| Benchmark → Em andamento | Iniciar o Padrão completo | Abre a aba sozinho; etapas giram e ficam com ✔; linha "Ao vivo" com CPU/GPU; na GPU aparece a imagem 3D e, no fim, o estúdio (esferas amarela e cromada, cubo branco) limpando o ruído |
| Benchmark → Parar | Parar no meio | "Interrompido. Nada foi salvo."; botão Iniciar volta |
| Benchmark → Resultados | Depois do teste | Nota, frase de comparação automática, cartões por parte com barra em relação ao seu melhor, "Render 3D do estúdio" em M amostras/s, monitoramento |
| Benchmark → Histórico | Ver e Comparar numa medição antiga | Ver abre em Resultados e muda a nota do painel lateral; Comparar abre a aba Comparar com antes/depois |
| Benchmark → Comparar | Trocar Antes e Depois nas caixas | Barras cinza x amarela, % verde/vermelho; aviso quando modos ou resoluções são diferentes |
| Benchmark → painel lateral | Notebook na bateria; plano Equilibrado | Dicas em laranja: "Ligue o carregador" e o plano de energia; Seu sistema com CPU, GPU, memória, discos, Windows e energia |
| Benchmark (janela estreita) | Diminuir a janela | Painel lateral vai para baixo das abas |
| Menu lateral | Passar o mouse devagar sobre os itens | Brilho amarelado acompanha o mouse; item escolhido com barra em degradê amarelo→laranja e fundo de vidro |
| Efeito cristal | Janela restaurada sobre um papel de parede colorido | Fundo cinza-escuro → preto com o que está atrás desfocado; cantos arredondados sem quadrado em volta; maximizada fica sem cantos |
| Efeito cristal | Acessibilidade → desmarcar "Fundo de vidro"; Windows com Efeitos de transparência desligado | Fundo fica sólido na hora; com o Windows desligado, fica sólido mesmo marcado |

## Benchmark v3: IA, dia a dia, janela 3D separada e etapas de 0 a 100

| Onde | O que fazer | O que deve acontecer |
|---|---|---|
| Novo teste | Ver "O que testar" | Chips novos: Inteligência artificial, Uso do dia a dia (pesado) e Placa de vídeo 3D (janela separada) |
| Em andamento | Iniciar o Padrão completo | Núcleo central girando com o ícone da etapa e "ETAPA X DE 11", barrinhas mexendo; % grande, barra, "Decorrido" e "Restante"; lista de etapas com anel enchendo de 0 a 100 e tempo de cada uma |
| Em andamento | Processador e dia a dia | Chips das partes acendem uma a uma (Cálculo, Ray tracing, Física...; Documentos, Navegação, Vídeo H.264, Planilhas, Fotos, Compactar, Tudo ao mesmo tempo) e ganham ✓ |
| Em andamento | Modo Preciso | Selo "Passada 2 de 3" aparece; o resultado é a mediana |
| Janela 3D | Etapas da placa de vídeo | Abre a janela "MB Bench · Cena 1 · Reflexos (não feche esta janela)" com o FPS no canto e a barra amarela embaixo; troca para Geometria, Estúdio, Simulação de jogo e as resoluções do limite |
| Janela 3D | Fechar a janela no meio | Teste interrompido ("Interrompido. Nada foi salvo.") sem travar o programa |
| Resultados | Depois do teste completo | Cartões: processador 1 núcleo e todos (pts), IA, dia a dia, cada cena em FPS; "Limite por resolução" com as barras e até onde passa de 60 FPS; tempos do dia a dia |
| Dia a dia sem FFmpeg | PC sem o FFmpeg do programa | "Vídeo H.264" aparece como não medido; o resto vale |
| Comparar | Duas medições completas | Linhas novas: Processador 1 núcleo/todos, IA, Dia a dia, Reflexos, Geometria, Estúdio, Simulação de jogo |
| Só a placa de vídeo | Testes avulsos | Roda só as 5 etapas da janela 3D |
