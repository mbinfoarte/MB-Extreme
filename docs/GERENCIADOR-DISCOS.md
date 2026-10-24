# Gerenciador de discos e reparo por disco

Módulo *Windows* → **Gerenciador de discos** e **Reparo do Windows** (agora com escolha do disco).

## Gerenciador de discos

Lê discos, partições e volumes com `Get-Disk`, `Get-Partition` e `Get-Volume` (PowerShell) e desenha cada disco como uma barra: um bloco por partição ou espaço livre, com largura proporcional. Clicar num bloco mostra as opções.

| Operação | Comando | Observações |
|---|---|---|
| Renomear | `Set-Volume -NewFileSystemLabel` | Só o nome; não toca nos arquivos. |
| Trocar a letra | `Set-Partition -NewDriveLetter` | Só letras D a Z livres. Avisa que programas e jogos podem perder o caminho. |
| Formatar | `Format-Volume` | NTFS, exFAT ou FAT32 (até 32 GB, limite do Windows); rápida ou completa. Pede para digitar a letra. |
| Reduzir | `Resize-Partition` | Usa `Get-PartitionSupportedSize` para saber o quanto dá. Só NTFS. |
| Estender | `Resize-Partition` | Só com espaço livre logo depois da partição. |
| Dividir em duas | reduzir + `New-Partition \| Format-Volume` | Um passo só, com o comando completo à mostra. |
| Criar partição | `New-Partition \| Format-Volume` | No espaço livre; tamanho, letra, sistema de arquivos e nome. |
| Excluir | `Remove-Partition` | Pede para digitar a letra (ou EXCLUIR). |
| Preparar disco novo | `Initialize-Disk` + criar + formatar | Assistente: só discos novos/vazios, GPT ou MBR; pede para digitar PREPARAR. |
| Reparar este disco | abre o Reparo do Windows já com a unidade escolhida | |

Antes de executar, cada operação mostra o que vai acontecer e o comando exato (e dá para ler o resultado na barra de status).

### Informações como no Gerenciamento de Disco do Windows

- Cabeçalho do disco: "Disco 0 · Básico · GPT · 476,94 GB · Online" (ou "Não inicializado", "Offline", "Somente leitura").
- Tamanhos no mesmo formato do Windows (unidade binária, duas casas: 931,51 GB, 3,64 TB, 100 MB).
- Nome da partição: "Windows (C:)"; sem nome, "Disco Local (D:)".
- Status: "Íntegro (Inicialização, Arquivo de Paginação, Despejo de Memória, Partição Primária)", "Íntegro (Partição do Sistema EFI)", "Íntegro (Partição de Recuperação)". O arquivo de paginação vem de `Win32_PageFileUsage`.
- Espaço sem partição aparece como "Não alocado". A partição reservada da Microsoft (MSR) não é desenhada, como no Windows.
- Capacidade, espaço livre e % livre por volume.

## Proteções (`DiskSafety`)

- O **disco do Windows** (sistema ou boot) não aceita formatar, excluir, trocar letra nem inicializar por aqui.
- Partições de **sistema, reservada (MSR), recuperação e ocultas sem letra** nunca podem ser alteradas nem redimensionadas.
- A unidade onde o **Windows está instalado** e a unidade de onde o **MB Extreme está rodando** não podem ser formatadas, excluídas nem mudar de letra.
- Discos **offline** ou **somente leitura** não aceitam alterações.
- Nomes de unidade só aceitam letras, números, espaço, hífen, ponto e sublinhado (nada disso chega ao PowerShell de outro jeito) e respeitam o limite de cada sistema de arquivos (NTFS 32, exFAT 15, FAT32 11).
- Reduzir/estender só em NTFS (ou ReFS), como o Windows.
- A partição do Windows (C:) **pode** ser reduzida, para criar outra unidade, como no Gerenciamento de Disco.

Não existe "desfazer" para formatar, excluir e inicializar: o programa avisa e exige confirmação digitada, mas faça backup antes.

## Reparo do Windows por disco

Na seção *Disco* há botões com as unidades do PC (letra, nome, tamanho; "Windows" e "externo" indicados). A unidade escolhida define os comandos:

- **Disco do Windows:** o que já existia (verificar online; reparo profundo agendado para o próximo reinício).
- **Outros discos (HD, SSD, pendrive):**
  - Verificar sem alterar: `Repair-Volume -DriveLetter X -Scan`.
  - Reparar: `Repair-Volume -DriveLetter X -OfflineScanAndFix` (tira a unidade do ar por instantes, não precisa reiniciar).
  - Reparo profundo: `chkdsk X: /f /r /x` (setores defeituosos; pode levar horas).
- **Saúde dos discos (SMART):** `Get-PhysicalDisk`.

A letra passa por `RepairCatalog.NormalizeDrive`, que só aceita uma letra e dois pontos.

## Não verificado em hardware real

Os comandos e as regras têm testes automáticos, mas nada disso foi executado em um PC com discos de verdade (nem o desenho da barra de partições e as janelas de confirmação). Teste primeiro com um pendrive sem nada importante. O comportamento de `Repair-Volume` e `Resize-Partition` em unidades em uso depende do Windows.
