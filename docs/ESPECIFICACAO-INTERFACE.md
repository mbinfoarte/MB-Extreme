# Especificação de interface e módulos adicionais (rascunho v1)

Base: referências de layout enviadas pelo dono do projeto (programa de limpeza com abas no topo, botão central de varredura e tela de progresso). **Usamos apenas a ideia de organização.** Cores, ícones, textos, nome e arte são próprios (veja `CLEAN-ROOM.md`).

## 1. Identidade visual: preto e amarelo

| Token | Valor sugerido | Uso |
|---|---|---|
| `--bg` | `#0B0B0B` | fundo principal |
| `--surface` | `#B3101010` (translúcido) | cartões e painéis sobre o fundo com brilho amarelo |
| `--surface-2` | `#CC1C1C1C` (translúcido) | itens, campos, abas inativas |
| `--border` | `#33FFFFFF` | divisórias |
| `--accent` | `#FFCC00` | amarelo da marca (cor do logotipo): botões, progresso, aba ativa |
| `--accent-hover` | `#FFDB4D` | hover |
| `--accent-glow` | `rgba(255,204,0,.35)` | brilho do botão central e do anel de progresso |
| `--text` | `#F2F2F2` | texto |
| `--text-muted` | `#9A9A9A` | texto secundário |
| `--ok` / `--warn` / `--bad` | `#2ECC71` / `#FF8A3D` / `#E5484D` | estados (usados com parcimônia, o amarelo é o destaque; o aviso usa laranja `#FF8A3D` para não se confundir) |

Regras: só duas cores de marca (preto e amarelo); o contraste do texto amarelo sobre preto deve ser verificado para acessibilidade; ícones de traço simples; cantos arredondados.

## 2. Estrutura de navegação

Abas no topo (ou menu lateral, a decidir):

1. **Acelerar**: ajustes de sistema, limpeza de RAM, modo jogo (ver `ESPECIFICACAO-FUNCIONAL.md`)
2. **Limpar e otimizar**: varredura central (seção 3)
3. **Rede**: analisador, DNS (ver `ESPECIFICACAO-REDE.md` e seção 6)
4. **Desempenho**: informações do hardware, sensores, benchmarks e stress (seção 5)
5. **Ferramentas**: drivers, programas, inicialização (seção 7)
6. **Central de ações**: histórico, restaurar alterações, relatórios

Tela inicial: botão grande central **ESCANEAR** com anel brilhante em amarelo, seleção das categorias com caixas de marcação ("Selecionar tudo") e indicador de saúde do PC.

## 3. Varredura e janela de progresso (padrão para todas as ações)

Toda operação longa usa o **mesmo componente de progresso**:

- Lista de etapas à esquerda, com a etapa atual destacada em amarelo e as concluídas marcadas.
- Anel circular grande com a **porcentagem** (0 a 100%) preenchendo em amarelo.
- Linha de status com o que está sendo analisado agora (ex.: "Procurando arquivos temporários...").
- Caixa **Corrigir automaticamente** (padrão: desligada) e botões **Pular** (etapa) e **Parar** (tudo).
- Ao fim: resumo do que foi encontrado/feito, espaço liberado, e **Desfazer**.

Contrato técnico: toda tarefa publica eventos `{etapa, percentual, mensagem, cancelavel}`; o componente apenas os exibe. A mesma janela serve para limpeza, RAM, DNS, benchmarks, drivers e instalação de programas.

### Categorias da varredura

| Categoria | Faço? | Observação |
|---|---|---|
| Arquivos temporários e lixo | Sim | Pastas conhecidas; mostrar o que será removido e o tamanho antes |
| Rastros de privacidade | Sim | Históricos e caches de navegadores/Windows, só com confirmação |
| Atalhos inválidos | Sim | Atalhos que apontam para arquivos inexistentes |
| Otimização de inicialização | Sim | Ver `REFERENCIA-RESETEXPLODE.md` |
| Otimização de sistema e de internet | Sim | Os ajustes das especificações anteriores |
| Verificação e saúde de disco | Sim | S.M.A.R.T. e espaço |
| Drivers e programas desatualizados | Sim | Seção 7 |
| **Limpeza e desfragmentação de registro** | **Não no MVP** | Ganho de desempenho não comprovado e risco de quebrar o Windows. Se um dia entrar, com backup e só chaves órfãs seguras |
| Remoção de spyware, antivírus, firewall | **Não** | Fora do escopo. Apenas mostrar o status do Segurança do Windows |

## 4. Regras de segurança (valem para tudo)

- Ponto de restauração/snapshot antes de alterar o sistema; tudo aparece na Central de ações com **Desfazer**.
- Nada é apagado sem prévia e confirmação (modo "Corrigir automaticamente" é opt-in).
- Executar como administrador só quando necessário.
- Sem telemetria sem consentimento.

## 5. Desempenho: informações, sensores, benchmarks e stress

O objetivo é ter dentro do app o que o usuário hoje busca em programas separados. **Programas como OCCT, AIDA64, 3DMark, CPU-Z e HWMonitor são produtos de terceiros: não os embutimos nem os copiamos.** Fazemos equivalentes próprios.

| Função (tipo de ferramenta) | Implementação própria |
|---|---|
| Informações do sistema (estilo CPU-Z/AIDA) | CPU, placa-mãe/BIOS, RAM por módulo, GPU, discos via WMI e APIs do Windows |
| Sensores em tempo real (estilo HWMonitor) | Biblioteca de código aberto **LibreHardwareMonitor** (MPL 2.0, manter o aviso de licença); temperatura, clock, carga, tensão, ventoinhas, consumo |
| Stress test (estilo OCCT) | Carga de CPU, RAM e GPU com limites de temperatura, parada automática e detecção de erros |
| Benchmarks (estilo 3DMark/Futuremark) | Testes próprios de CPU, GPU, memória e disco, com nota própria e histórico. A nota **não é comparável** com a de produtos de terceiros, e não usamos o nome deles |
| FPS em jogos | Medição por eventos do Windows (sem injetar no jogo), com FPS, frametime e 1% low |

## 6. DNS estilo "DNS Jumper"

- Varredura de uma lista de provedores (com campo para o usuário adicionar o seu), com várias consultas por provedor e **ranking por tempo de resposta**; sugestão do mais rápido.
- Botão **Aplicar** direto no app (adaptadores ativos), com **Restaurar padrão do Windows**.
- Mostrar se o provedor filtra conteúdo/anúncios.
- **Ponto honesto:** o DNS muda o tempo de resolução de nomes e a velocidade para abrir sites e conectar a servidores, **não a rota nem o ping dentro da partida**. Para rota e ping de jogo, a ferramenta é o módulo de rotas (MTR, ping por região e, em versão futura, relays). O app deve deixar isso claro.

## 7. Drivers e programas

**Drivers**
- Detectar dispositivos e versão do driver instalado (placa de vídeo, rede, áudio, chipset).
- Buscar atualizações em **fontes oficiais** (Windows Update e páginas/ferramentas dos fabricantes). Evitar bancos de drivers de terceiros.
- Criar ponto de restauração antes de instalar e permitir reverter. Nunca instalar driver sem confirmação.

**Programas**
- Catálogo de aplicativos comuns instaláveis em lote pelo **winget**, com detecção do que já está instalado e atualização dos instalados.
- Instalação silenciosa, um por vez, com a janela de progresso padrão.

## 8. Pontos em aberto

- Confirmar a stack: .NET 8 com WPF (recomendada para esse visual) ou Tauri com Rust.
- Nome final e logotipo (o nome "MB Extreme" é nosso; logotipo da MB InfoArt aplicado (ícone do app e menu)).
- Menu em abas no topo ou lateral.
- Idiomas: português primeiro; inglês depois?
