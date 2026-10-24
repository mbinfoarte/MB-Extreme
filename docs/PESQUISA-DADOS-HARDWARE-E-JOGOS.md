# Pesquisa: bases de dados para comparar hardware e para requisitos de jogos

Objetivo: (1) dizer "seu PC está acima de X% dos PCs" e "esta placa rende tanto" sem inventar números; (2) mostrar na Biblioteca se o PC atende aos requisitos de cada jogo.
Itens marcados **(confirmar)** não foram confirmados em fonte oficial. **Nada aqui é parecer jurídico**: antes de lançar, confirme os termos de cada fonte.

## 1. Dados de desempenho de CPU/GPU

| Fonte | O que tem | Acesso | Pode usar num produto comercial? |
|---|---|---|---|
| **Blender Open Data** | Mais de 330 mil resultados do Blender Benchmark, por CPU e GPU (amostras de renderização por minuto) | Cópia completa em arquivo `.jsonl` (um JSON por linha) e anônima | **Sim**: a licença é **CC0** (domínio público) ([sobre](https://opendata.blender.org/about/), [repositório](https://projects.blender.org/infrastructure/blender-open-data/)). Não achei API de consulta, só a cópia em lote |
| **PassMark** (cpubenchmark.net, videocardbenchmark.net) | Notas de CPU, disco e vídeo de milhares de PCs | Sem API pública; vendem arquivos CSV (médias por modelo, atualizadas todo dia, e resultados individuais, toda semana) ([PassMark](https://passmark.com/services/market-analysis.php)) | **Só com licença paga**; o administrador diz que não se pode copiar o site ([fórum](https://forums.passmark.com/pc-hardware-and-benchmarks/38618-legality-of-using-your-cpu-benchmark)) |
| **Geekbench Browser** | Resultados públicos de CPU e GPU | Sem API documentada; existe biblioteca de terceiros que raspa o site ([PyPI](https://pypi.org/project/geekbench-browser-python)) | Termos não confirmados. Não raspar; perguntar à Primate Labs |
| **TechPowerUp GPU Database** | Especificações e desempenho relativo de placas de vídeo | Sem API; há cópias raspadas: pacote `dbgpu` (MIT) e conjunto no Hugging Face (Apache 2.0) ([dbgpu](https://pypi.org/project/dbgpu/), [HF](https://huggingface.co/datasets/Jr23xd23/gpu-database/blob/main/README.md)) | A licença das cópias não cobre o site de origem. Risco jurídico; pedir permissão ao TechPowerUp |
| **GFXBench / CompuBench** (Kishonti) | Banco público de resultados de GPU | Serviço **encerrado** em dezembro de 2025; foi liberado um resumo agregado (máximo, mediana e média) e o código sob licença BSD ([KitGuru](https://www.kitguru.net/gaming/joao-silva/gfxbench-and-compubench-are-moving-to-open-source-after-21-years/)) | Licença do resumo não confirmada; é voltado a celulares, pouco útil para PC |
| **OpenBenchmarking.org** (Phoronix) | Milhões de resultados públicos | API e licença dos dados não confirmadas ([site](https://openbenchmarking.org)) | Não confirmado; focado em Linux |
| **Steam Hardware Survey** | Participação de placas, CPUs e memória entre usuários da Steam | Página mensal, sem termos de reuso claros | Só como contexto, sem redistribuir |
| **3DMark / Cinebench / Unigine** | Ranking próprio de cada um | Sem API aberta | Não |

Reddit e fóruns: as buscas não trouxeram um conjunto de dados aberto novo; as discussões encontradas confirmam que quase todos os bancos úteis (PassMark, UserBenchmark, 3DMark) são fechados e que os dados abertos são raros. A exceção real é o **Blender Open Data**.

### Conclusão sobre desempenho
1. **Fonte aberta que dá para usar já**: Blender Open Data (CC0). Serve para uma posição honesta do tipo "no teste de render do Blender, sua CPU/GPU está acima de X% dos resultados públicos". Cobertura menor para placas AMD antigas (sem suporte no Cycles) e para modelos raros.
2. **Fonte própria, a melhor no longo prazo**: o ranking **anônimo e opcional** com os usuários do MB Extreme. Cada medição (nota do teste, modelo da CPU/GPU, FPS por jogo e resolução) vai para o nosso servidor, que calcula percentis por modelo. Sem problema de licença e comparável com o que o usuário mede aqui. Exige consentimento (LGPD), filtro de resultados absurdos, e um mínimo de amostras antes de mostrar (por exemplo, 30).
3. **Licenciar** PassMark ou pedir permissão ao TechPowerUp, se quisermos tabelas de modelos que quase ninguém mede.
4. **Tabela própria de níveis** (feita por nós, offline): lista de ~150 placas e ~100 CPUs com um nível de 1 a 10. É trabalho nosso, sem dependência de ninguém, e serve para estimar requisitos de jogos. Precisa de revisão cuidadosa e rótulo "estimativa".

## 2. Requisitos de jogos

| Fonte | O que tem | Acesso | Observações |
|---|---|---|---|
| **Steam (appdetails)** | Requisitos mínimos e recomendados (`pc_requirements`, texto com HTML), gêneros, categorias | Endpoint da loja, **não oficial** (`store.steampowered.com/api/appdetails`) ([notas de uso](https://dev.to/morinaga/four-undocumented-steam-store-api-behaviors-every-game-directory-builder-should-know-3f63)); limite relatado de cerca de 200 pedidos a cada 5 minutos | **O app já usa** na Biblioteca (com cache em disco). Termos do endpoint não estão escritos; os termos da Web API da Steam falam em distribuir dados "para uso pessoal do usuário" e exigem política de privacidade ([Valve](https://steamcommunity.com/dev/apiterms), página de 2010). Mantém o padrão menos arriscado: cada PC consulta sob demanda e guarda cache, sem redistribuir cópia em massa |
| **IGDB** (Twitch) | Metadados, capas, gêneros, notas | Gratuito com login da Twitch; 4 pedidos por segundo ([resumo](https://macmyths.com/apps/igdb/)) | Uso comercial exige parceria **(confirmar)**; não vi campo de requisitos |
| **RAWG** | 500 mil jogos, plataformas | Chave gratuita; uso comercial grátis até 100 mil usuários ativos por mês, com atribuição e link ([termos](https://pypi.org/project/rawg/)) | Pode trazer requisitos de PC em alguns jogos **(confirmar nos documentos da API)** |
| **PCGamingWiki** | Requisitos e correções, muito completo | Tem consulta (Cargo), mas a licença é **CC BY-NC-SA** (uso não comercial) ([Wikipédia](https://en.wikipedia.org/wiki/PCGamingWiki)) | Não usar no produto sem permissão; pode servir como link para o usuário |
| **GOG** | Requisitos dos jogos da GOG | API não oficial (`api.gog.com/products/{id}`) ([docs da comunidade](https://gogapidocs.readthedocs.io/)) | Campo de requisitos não confirmado; testar com um jogo real |
| **Kaggle: Steam Store Games** | 27 mil jogos de 2019 com requisitos em texto | Conjunto do autor "nikdavis", uso livre segundo ele ([cópia](https://baselight.app/u/kaggle/dataset/nikdavis_steam_store_games)) | Antigo; só útil para testar o leitor de requisitos |
| **Epic, Ubisoft, EA, Battle.net** | Sem API pública de requisitos | — | Para esses jogos: campo manual, ou dados dos usuários |

### Como mostrar "roda ou não roda" na Biblioteca
1. **Ler o texto dos requisitos** (já temos) e separar sistema, processador, memória, placa de vídeo, VRAM, DirectX e espaço em disco.
2. **Memória, VRAM, disco e Windows**: comparação numérica direta e confiável.
3. **Processador e placa**: converter os nomes ("GTX 1060", "RX 580", "Core i5-8400") em um nível da nossa tabela e comparar com o nível do PC. Resultado em três faixas: *atende o recomendado*, *atende o mínimo*, *abaixo do mínimo*, com cada item marcado e o motivo. Quando o nome não for reconhecido, mostrar "sem dados" em vez de chutar.
4. **FPS esperado**: só quando houver dados reais (nossas sessões ou, no futuro, a base dos usuários). Antes disso, mostrar apenas a faixa e dizer que é estimativa.

## 3. Ordem sugerida
1. Leitor e comparador de requisitos + tabela própria de níveis + etiquetas na Biblioteca (sem depender de terceiros).
2. Ferramenta interna (nossa) que baixa o snapshot do Blender Open Data e gera um índice leve de CPU/GPU para o app mostrar "acima de X%".
3. Ranking anônimo opcional no servidor e envio das medições com consentimento.
4. Perguntar a PassMark e TechPowerUp sobre licença, se quisermos mais cobertura.
5. Queimador de GPU e redesenho do Teste de estresse.

## 4. Implementado a partir desta pesquisa
1. **Requisitos na Biblioteca**: leitor do texto da Steam (inglês e português), comparação do PC com o que o jogo pede (memória, VRAM, Windows em números; processador e placa pela nossa tabela de níveis, `HardwareTiers`), veredito (*atende o recomendado / o mínimo / abaixo do mínimo / sem dados*) e lista item a item com ✓, ≈ e ✗. Modelos desconhecidos viram "sem dados".
2. **Índice do Blender Open Data**: ferramenta `tools/MBExtreme.IndexTool` (e passo do CI, que não derruba o build se falhar) baixa o arquivo de resultados (CC0), normaliza os tempos por cena e versão do Blender e grava `hardware-index.json` (nome do modelo e nota, sem os resultados originais). O app mostra "mais rápido que X% dos N modelos" para o processador e a placa. **O formato exato dos dados do Blender não pôde ser conferido** (o site não abre neste ambiente): a leitura é tolerante e o índice sai vazio, sem quebrar nada, se o formato mudou.
3. **Ranking anônimo opcional**: servidor com banco separado (`ranking.db`), código de instalação embaralhado, validação, limites por IP e por instalação, medições com mais de 18 meses apagadas, "apagar meus dados"; no app, consentimento explícito (desligado por padrão), botão para ver o que será enviado e envio das medições do benchmark e do jogo. A posição só aparece com amostras suficientes (30 PCs com a mesma placa; 10 no caso de um jogo).
4. **Teste de estresse com GPU**: queimador 3D (fractal, cubos, preenchimento, cálculo puro ou combinado) com a imagem na tela, conferência de um quadro de referência a cada 3 s (diferença = erro), detecção de reinício da placa (TDR), limite de temperatura; monitoramento ao vivo (uso, temperatura, clock e potência de GPU e CPU, FPS da cena), relatório com gráficos e veredito, HTML exportável e histórico.

### O que ainda depende de você
- Configurar o endereço do servidor (`AppInfo.DefaultServerUrl` ou a configuração do usuário) para o ranking funcionar.
- Conferir o primeiro índice gerado pelo CI (passo "Gerar o índice de hardware") e, se o Blender mudou o formato, ajustar `HardwareIndexBuilder`.
- Revisar a tabela de níveis de placas e processadores (`HardwareTiers`) com calma: são estimativas escritas por nós.
- Atualizar o texto jurídico da política de privacidade e os termos com um advogado antes de lançar o ranking.
