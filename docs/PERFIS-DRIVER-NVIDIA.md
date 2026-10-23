# Perfis de driver NVIDIA por jogo (pesquisa)

Pesquisa feita em 23/10/2026 sobre o que o NVIDIA Profile Inspector permite e o que a comunidade recomenda, para o MB Extreme aplicar "perfis por jogo" (ex.: Battlefield) com desfazer.

## 1. Fonte confiável dos identificadores
O Profile Inspector é **MIT** (https://github.com/Orbmu2k/nvidiaProfileInspector). Ele lê as configurações do próprio driver (interface NVAPI/DRS) e a lista de nomes e valores fica em `CustomSettingNames.xml` (117 configurações documentadas). Isso **confirmou** os dois identificadores que já usamos e traz os demais abaixo. Como é MIT, podemos usar os identificadores e valores citando a origem.

| Configuração | ID | Valores |
|---|---|---|
| Modo de gerenciamento de energia | `0x1057EB71` | 0 Adaptativo · 1 Máximo desempenho · 5 Ideal |
| Quadros pré-renderizados | `0x007BA09E` | 0 App decide · 1…7 |
| Baixa latência (painel): estado | `0x0005F543` | 0 Off · 1 On · 2 Ultra |
| Baixa latência: ativado | `0x10835000` | 0 / 1 |
| Sincronia vertical | `0x00A879CF` | `0x08416747` forçar off · `0x47814940` forçar on · `0x60925292` o app decide · `0x18888888` Fast Sync |
| Otimização com threads | `0x20C1221E` | 0 Auto · 1 On · 2 Off |
| Filtragem de textura: qualidade | `0x00CE2691` | 0 Qualidade · `0x0A` Desempenho · `0x14` Alto desempenho · `0xFFFFFFF6` Alta qualidade |
| Cache de shaders: tamanho | `0x00AC8497` | 0 Off · `0x80`…`0x2000` (128 MB a 8 GB) |
| Cache de shaders: ativado | `0x00198FFF` | 0 / 1 |
| Resizable BAR (rBAR): ativar | `0x000F00BA` / v2 `0x000BFA21` | v2: 0 Off · 1 Auto · 2 On |
| rBAR: limite de tamanho / opções | `0x000F00FF` / `0x000F00BB` | tamanho em bytes (ex.: 1 GB) |
| Limitador de quadros V3 | `0x10835002` | 0 Off · valor = FPS |
| DLSS: substituir DLL | `0x10E41E01` (SR), `…E02` (RR), `…E03` (FG), `…E04` (NR) | 0 / 1 |
| DLSS: letra do preset | `0x10E41DF3` | `0x00FFFFFF` recomendado · 1=A … 6=F |
| DLSS: qualidade forçada | `0x10AFB768` | 0 Desempenho · 1 Equilibrado · 2 Qualidade · 4 DLAA |

Avisos do próprio projeto: muitas entradas são ocultas, experimentais ou dependem da versão do driver; **um valor que ajuda num jogo pode não fazer nada ou dar problema em outro**. Por isso a recomendação deles é mudar só o perfil do jogo e guardar backup (formato `.nip`).

## 2. O que a comunidade diz (resumo honesto)
Não existe um "perfil oficial" por jogo; o que há são guias com opiniões que **se contradizem em parte**:

- **Reflex tem prioridade sobre o modo de baixa latência do driver.** Em jogos com Reflex (Battlefield 6, Valorant, CS2, Apex, Fortnite), ligue Reflex dentro do jogo; o ajuste do driver costuma ser ignorado em DirectX 12. Não empilhar dois limitadores de FPS. (Blur Busters, TechSpot.)
- **Modo de energia "máximo desempenho" por jogo** é o ponto mais repetido nos guias de CS2 e Battlefield 6.
- **Resizable BAR**: a NVIDIA só liga em jogos de uma lista; forçar nos outros deu ganho em testes (até ~10% no 3DMark) mas pode causar regressão ou travamento, e exige estar ligado na BIOS. Battlefield 6 já tem perfil próprio de rBAR no driver, só confira se está habilitado.
- **Otimização com threads desligada no Battlefield 6** aparece num guia como "teste isto", sem benchmark. Tratar como experimento.
- Textura "Alto desempenho" e sincronia vertical desligada no driver aparecem em guias de CS2/Valorant; o ganho é pequeno e piora a imagem.
- Sites como o de "NVIDIA Inspector Fortnite" parecem comerciais e não foram aceitos como fonte.

## 3. Proposta para o MB Extreme
1. **Catálogo de perfis por jogo** (arquivo JSON versionado, com jogo → configurações (ID, valor, motivo, nível de confiança)), começando por Battlefield (6/2042), CS2, Valorant, Apex, Fortnite, Warzone e um perfil "mundo aberto/AAA".
2. **Só valores conservadores e reversíveis** por padrão: modo de energia, rBAR (se a BIOS permitir), cache de shaders grande, e nada de mexer em DLSS/vsync/textura sem você ligar.
3. **Nível "avançado"** (opcional, com aviso): otimização com threads, textura em alto desempenho, DLSS (substituir DLL/preset) e Fast Sync, cada um com explicação e desfazer individual.
4. **Perfil pré-existente do driver**: o MB Extreme detecta se o jogo já tem perfil no driver (a NVIDIA traz centenas) e só altera as configurações escolhidas, sem recriar o perfil.
5. **Exportar/importar `.nip`** para você poder comparar com o Profile Inspector.
6. **Teste antes de manter**: depois de aplicar, o assistente da Etapa 9 pede um teste curto (FPS/estabilidade) e oferece reverter.

Limite: ainda não foi possível testar nada disto em placa NVIDIA real; o primeiro lote deve ser validado no seu PC antes de ampliar.
