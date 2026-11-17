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

## Benchmark v4: resultados detalhados, render em blocos, jogo CPU+GPU, disco aleatório e consumo

| Onde | O que fazer | O que deve acontecer |
|---|---|---|
| Em andamento | Teste completo | Barra geral em degradê amarelo→laranja com brilho passando; cada etapa terminada mostra o resultado na lista (ex.: "1.500 pts", "173,8 FPS · 1% low 86,2", "60+ FPS até 2560×1440") |
| Janela 3D | Modo Preciso | Cada cena roda 3 vezes com a câmera em outro ângulo ("passada 2 de 3" no título); vale a do meio |
| Janela 3D · Estúdio | Cena 3 | A imagem aparece quadrado por quadrado, do centro para fora, com cantos amarelos no quadrado atual |
| Janela 3D · Jogo | Simulação de jogo | Exércitos amarelo e azul se encontram no vale, com explosões de partículas nos choques; uso de CPU alto junto com a GPU |
| Disco | Etapa Armazenamento | Partes: gravação e leitura sequencial, leitura aleatória 4K com 1 e 8 filas, gravação aleatória 4K; o arquivo é apagado no fim |
| Resultados | Depois de 2 testes completos | Nota geral com seta e % do anterior e a frase ("Ganho expressivo..."); 8 cartões por categoria com peso, nota, seta e a marca branca do anterior na barra |
| Resultados | Dia a dia, IA e 3D | Barras lado a lado (este x anterior); IA em tokens/s; 3D com FPS médio, 1% low, 0,1% low e o frametime da cena clicada |
| Resultados | Limite por resolução | Faixas Competitivo 144+ / Fluido 60+ / Jogável 30+ e o gargalo (processador ou placa de vídeo) |
| Resultados | Consumo | Pico e média de watts do processador e da placa de vídeo e a fonte recomendada |
| Resultados | Salvar imagem | PNG em Imagens\MB Extreme e o Explorer abre com o arquivo selecionado |
| Resultados | Mexer em outra janela durante o 3D | Aviso de que o FPS pode ter ficado abaixo do real |
| Comparar | Antes e depois de otimizar | Cartão "GANHO TOTAL" com o % da nota geral, a frase e a variação de cada categoria |

## Calibrar controle (DualSense e DualShock 4)

| Onde | O que fazer | O que deve acontecer |
|---|---|---|
| Calibrar controle | Abrir sem controle; depois ligar o DualSense pelo cabo USB | "Procurando controle..."; ao ligar, a faixa mostra conexão USB, bateria, série, placa, cor e memória "Temporária" |
| Calibrar controle | Ligar pelo Bluetooth | Aviso para usar o cabo USB |
| Calibrar controle | Steam ou DS4Windows abertos | Se não abrir o controle, aparece o motivo (outro programa usando) |
| Modo simples | Começar → seguir as 6 etapas | Diagnóstico (drift 3 s + girar), Centro, Alcance (desenho fecha), Conferir, tabela Antes × Depois; Gravar pede confirmação e grava |
| Modo simples | Reiniciar o controle antes de gravar | A calibração volta ao que era |
| Modo avançado | Mexer em tudo | Botões acendem, L2/R2 enchem, giroscópio e acelerômetro mudam, circularidade com erro médio |
| Modo avançado | Medir drift; Calibrar centro; Começar/Concluir alcance; Gravar | Resultados na linha de status; cada passo funciona separado |
| Modo avançado | Restaurar cópia (DualSense) | Volta à calibração guardada no primeiro contato e grava |
| Atendimento | Preencher cliente e Gerar laudo | Página HTML em Documentos\MB Extreme\Controles com antes, depois e se foi gravado |
| Controle clone / DualSense Edge | Ligar | Testes funcionam; calibração desligada com o motivo |
| Xbox / Nintendo | Escolher a marca | Aviso de que a calibração ainda é só PlayStation e botão para Periféricos |

## Recursos da comparação com o Hone

1. **Otimizações**: os chips FPS e latência, Rede e ping, Qualidade de vida e Privacidade e segurança filtram a lista. Os títulos de categoria vazios somem. A busca ignora acentos ("classico" acha "clássico"). O contador "x de y ativas" bate com o que está aplicado.
2. Itens com selo **Novo** e botão **i**: o "i" abre a explicação sem marcar ou desmarcar a caixa.
3. **Menu clássico (Windows 11)**: aplicar, sair e entrar na conta. O botão direito mostra o menu completo. Desfazer volta o menu novo.
4. **Atalhos sem "- Atalho"**, **Explorador compacto**, **abrir em Este Computador** e **mostrar extensões**: conferir cada um e o desfazer.
5. **Papel de parede 100%**: aplicar e escolher o papel de parede de novo. **Tela de login sem desfoque** e **ir direto para a senha**: bloquear com Win+L.
6. **Menu de inicialização em 3 s**: `bcdedit /enum {bootmgr}` mostra timeout 3; desfazer volta o valor anterior. **Mensagens detalhadas**: reiniciar e ver os textos.
7. **Visualizador de Fotos**: aparece em Abrir com > Escolher outro aplicativo.
8. **Buffers da placa de rede**, **LSO** e **modo MSI** (risco médio): criar um ponto de restauração antes. Conferir em Gerenciador de Dispositivos > placa > Avançado. Para o MSI, use o Diagnóstico de latência antes e depois. Desfazer e reiniciar.
9. **NVIDIA: indicador do DLSS**: só aparece com placa NVIDIA. Num jogo com DLSS, o texto aparece no canto.
10. **Limpeza**: Cache do Discord (feche o Discord), Cache e registros do Steam e Documentos recentes. Jogos, saves e mensagens não são apagados.
11. **Reparar o Windows > Reconstruir o cache de fontes**: o serviço FontCache para e volta a funcionar.
12. **Programas > kit Técnico**: Process Explorer, Autoruns e Process Monitor pelo winget.
13. **MBExtreme-Instalar.exe** (instalador pequeno, ~175 KB): com o repositório público, ele baixa a versão mais nova, mostra o progresso, confere o SHA-256 e abre o instalador completo. Com o repositório privado, mostra "O download ainda não está público" e o botão "Baixar pelo site".

## Internet em abas (mapa do Brasil, servidores perto de você e DNS)

1. **Topo:** provedor, cidade, IP e adaptador. Servidor do teste (Cloudflare com o código da cidade, ou LibreSpeed), Conexões (Auto/4/8) e INICIAR TESTE. O botão Parar aparece durante o teste.
2. **Faixa de resultados:** Download, Upload, Ping, Variação, Sob carga e DNS em uso. O cartão da etapa em andamento fica com a borda amarela pulsando. O ping e a variação ganham cor (verde, amarelo, laranja, vermelho).
3. **Velocidade:** velocímetro com o botão redondo, mapa do servidor do teste, nome do teste, nota do ping sob carga. LibreSpeed abre a lista dos servidores mais rápidos nessa aba.
4. **Jogos e servidores:**
   - O mapa começa no Brasil; o botão Mundo mostra o mapa-múndi, com as setas e a cor do ping.
   - "Perto de mim" lista os 12 mais perto pela sua cidade; há filtros por região e por provedor (Valve, AWS, Oracle, LibreSpeed, Referência).
   - "Testar os 10 mais perto" e "Testar marcados" medem 8 servidores de cada vez; a lista se reordena pelo ping.
   - A frase embaixo compara o ping com o mínimo possível pela distância.
   - Conferir se aparecem as cidades da Valve (precisa da Steam acessível) e os servidores do LibreSpeed no Brasil.
5. **DNS:**
   - O teste roda sozinho ao abrir a aba; cada linha tem a barra, os ms e o botão Usar.
   - Usar troca o DNS na hora (a faixa "DNS em uso" muda); Voltar ao automático restaura o DNS do provedor.
6. **Roteador e rota:** dados da conexão, Medir o roteador, Traçar rota até o servidor escolhido (10 s, com média, perda e pior tempo por salto).
7. **Histórico:** os últimos 15 testes com a barra do download e a comparação com os anteriores.

## Correção: erro de thread ao instalar programas (e telas parecidas)

O `Progress<string>` criado dentro de um `Task.Run` guardava a thread de fundo e, ao mexer na tela, estourava "O thread de chamada não pode acessar este objeto porque ele pertence a um thread diferente". Criado o ajudante `UiProgress` (entrega sempre no Dispatcher da interface) e aplicado em Programas, Painel (AnalyzerHero e Diagnóstico), Mapa do disco e Backup para formatar.

Testar: marcar programas em **Programas > Instalar programas** e clicar em Instalar (não pode fechar com erro; o status muda para "Instalando..."). Repetir em: Painel > Analisar e Corrigir, Mapa do disco (ler uma unidade) e Backup para formatar (criar e restaurar).

## Drivers: baixar o driver que falta dentro do programa

A tela Drivers já mostra os aparelhos sem driver ou com problema (Gerenciador de Dispositivos). Agora, em vez de só copiar o ID, ela baixa e instala o driver direto do Windows Update (fonte oficial da Microsoft), casando o ID de hardware do aparelho com o do driver — sem abrir o site do fabricante.

- **Baixar os drivers que faltam** (botão no topo da lista): procura no Windows Update uma vez, casa com todos os aparelhos com problema, cria ponto de restauração e instala os que achou.
- **Baixar e instalar o driver** (em cada aparelho): mesma coisa, só para aquele aparelho.
- Quando o Windows Update não tem o driver, a mensagem orienta a copiar o ID e procurar no Catálogo do Windows Update ou no site do fabricante (o botão Copiar ID e o Gerenciador de Dispositivos continuam ali).

Testar num notebook com algum aparelho sem driver (ex.: logo após formatar): abrir Drivers, conferir a lista "Aparelhos sem driver ou com problema", clicar em Baixar os drivers que faltam e ver o progresso (Procurar → Ponto de restauração → Instalar). A busca do Windows Update pode levar alguns minutos.

## Atualização quase em tempo real + aviso de versão beta

- **Checagem de atualização:** além de ao abrir, agora o programa checa a cada 15 minutos e sempre que você volta para a janela do MB Extreme (no máximo uma vez a cada 5 minutos). Assim, logo depois de sair uma versão nova no GitHub, o balão "Nova versão disponível" aparece sozinho, sem clicar em Verificar atualização. O GitHub não empurra o aviso; o programa é que pergunta com frequência.
- **Aviso de versão beta (Painel):** um cartão amarelo logo abaixo da análise avisa que é uma avaliação gratuita e que os recursos Pro vão exigir assinatura, com "Ver planos" (abre Licença e conta) e "✕" para esconder. Some quando a pessoa assina o Pro e volta a aparecer quando sai uma versão nova.

Testar: deixar o programa aberto, alternar para outra janela e voltar — ele checa atualização. No Painel, conferir o aviso de beta, clicar em Ver planos e no ✕ (some e não volta até a próxima versão).

## Papel de parede

Novo módulo em "Meu PC → Papel de parede":
- **MB Extreme:** 6 modelos próprios (Clássico, Degradê, Grade, Raio, Minimalista, Faixa amarela), desenhados na resolução do monitor.
- **Galeria online:** banco licenciado Pexels (precisa de uma chave de API grátis, colada na própria tela; mostra o crédito do autor).
- **Do meu PC:** importar uma imagem.
- **Onde aplicar:** todos os monitores ou um específico, cada um com a sua resolução.
- **Ajuste:** Preencher, Ajustar, Esticar, Lado a lado, Centralizar e "Esticar nos monitores" (2+ telas).

Testar: abrir o módulo, conferir se lê os monitores com a resolução certa, escolher um modelo MB e aplicar; trocar o ajuste; importar uma imagem do PC; (com chave) buscar no Pexels e aplicar.

## Minimizar para a bandeja + widget flutuante

- **Bandeja:** ao clicar no X, o programa some na bandeja (ao lado do relógio) e continua rodando. Clique no ícone abre; botão direito tem Abrir, Mostrar/ocultar o widget e Sair. Em Acessibilidade dá para desligar o "ir para a bandeja".
- **Widget flutuante:** barrinha sempre no topo com RAM, CPU, Disco e Rede, atualizando a cada segundo; arrasta pelo foguete e guarda a posição; botões de abrir o app e esconder. Liga/desliga pelo menu da bandeja e em Acessibilidade.

Testar: fechar no X e ver o ícone na bandeja; reabrir pelo ícone; Sair fecha de vez; ligar o widget, mover, conferir os números e esconder.

## Estúdio: perfis, Ensaio, câmeras em 4K e AV1

1. **Codificador**: numa RTX 40/50 (ou RX 7000+), abra o Estúdio pela primeira vez depois da atualização → em Vídeo, o escolhido deve ser **AV1 · NVIDIA NVENC** (ou AV1 · AMD AMF). Em placa sem AV1, fica H.265 ou H.264.
2. **Live com AV1 escolhido**: Transmitir para a **Twitch** → a live sai em H.264 da mesma placa (sem erro). Para o **YouTube** → sai em AV1.
3. **Perfis**: botão **Perfis** no topo → dê um nome → "Salvar a configuração atual". Mude várias coisas (resolução, câmera, cena) → Perfis → **Carregar** → tudo volta como estava. A chave de transmissão continua lembrada e não aparece no arquivo do perfil (%AppData%\MBExtreme\perfis-estudio).
4. **Ensaio**: clique em **Ensaio** ao lado do Iniciar → o quadro "No ar" mostra a imagem final (cenas, câmera, sobreposições). Nada é gravado (pasta de vídeos sem arquivo novo) e nada vai para a plataforma. Troque de cena e use a Transição.
5. **Do ensaio para o ar**: com Transmitir ligado, clique em **Entrar no ar** → confirma → a live começa com a mesma montagem.
6. **Câmeras**: aba Câmeras → "Qualidade das câmeras: Máxima (nativa, até 4K)". Webcam 4K deve abrir em 3840×2160; celular por endereço (RTSP/HTTP) abre no tamanho real dele (8K vira 4K). Se o PC pesar, escolha Full HD.
