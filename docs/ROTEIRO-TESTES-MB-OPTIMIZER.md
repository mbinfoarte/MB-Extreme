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
