using MBExtreme.Core.Repair;
using MBExtreme.Core.Storage;
using Xunit;

namespace MBExtreme.Core.Tests;

public class GerenciadorDiscosTests
{
    private const long Gb = DiskOps.Gb;

    private const string Json = """
    [
      {"Number":0,"Name":"Samsung SSD 970","Size":512110190592,"Style":"GPT","Bus":"NVMe","Health":"Healthy","IsSystem":true,"IsBoot":true,"IsOffline":false,"IsReadOnly":false,
       "Partitions":[
         {"Number":1,"Letter":"","Offset":1048576,"Size":104857600,"Type":"System","IsSystem":true,"IsBoot":false,"IsHidden":true,"Volume":null},
         {"Number":2,"Letter":"","Offset":105906176,"Size":16777216,"Type":"Reserved","IsSystem":false,"IsBoot":false,"IsHidden":true,"Volume":null},
         {"Number":3,"Letter":"C","Offset":122683392,"Size":511000000000,"Type":"Basic","IsSystem":false,"IsBoot":true,"IsHidden":false,
          "Volume":{"Label":"Windows","FileSystem":"NTFS","Size":511000000000,"Free":200000000000,"Health":"Healthy"}}
       ]},
      {"Number":1,"Name":"WDC HD","Size":1000204886016,"Style":"GPT","Bus":"SATA","Health":"Healthy","IsSystem":false,"IsBoot":false,"IsOffline":false,"IsReadOnly":false,
       "Partitions":{"Number":2,"Letter":"D","Offset":1048576,"Size":500000000000,"Type":"Basic","IsSystem":false,"IsBoot":false,"IsHidden":false,
          "Volume":{"Label":"Jogos","FileSystem":"NTFS","Size":500000000000,"Free":123000000000,"Health":"Healthy"}}},
      {"Number":2,"Name":"Pendrive","Size":32000000000,"Style":"RAW","Bus":"USB","Health":"Healthy","IsSystem":false,"IsBoot":false,"IsOffline":false,"IsReadOnly":false,"Partitions":null}
    ]
    """;

    private static IReadOnlyList<ManagedDisk> Disks() => DiskInventory.Parse(Json);

    // ---- leitura

    [Fact]
    public void Le_discos_particoes_e_volumes_mesmo_com_um_item_so_ou_nulo()
    {
        var disks = Disks();
        Assert.Equal(3, disks.Count);
        Assert.Equal(3, disks[0].Partitions.Count);
        Assert.Equal('C', disks[0].Partitions[2].Letter);
        Assert.Equal("Windows", disks[0].Partitions[2].Volume!.Label);
        Assert.Single(disks[1].Partitions);                  // o PowerShell entrega objeto, não lista, quando só há um
        Assert.Equal('D', disks[1].Partitions[0].Letter);
        Assert.True(disks[2].IsRaw);
        Assert.Empty(disks[2].Partitions);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[]")]
    public void Saida_vazia_nao_quebra(string json) => Assert.Empty(DiskInventory.Parse(json));

    [Fact]
    public void Espacos_sem_particao_aparecem_na_ordem_do_disco()
    {
        var hd = Disks()[1];
        var segments = DiskInventory.Segments(hd);
        Assert.Equal(2, segments.Count);                    // D: e o resto do disco livre
        Assert.False(segments[0].IsFree);
        Assert.True(segments[1].IsFree);
        Assert.Equal(1048576 + 500000000000, segments[1].Offset);
        Assert.True(segments[1].Size > 400 * Gb);

        Assert.Single(DiskInventory.Segments(Disks()[2]));  // disco novo: tudo livre
        Assert.True(DiskInventory.Segments(Disks()[2])[0].IsFree);
    }

    [Fact]
    public void Folgas_de_alinhamento_pequenas_nao_viram_espaco_livre()
    {
        var disk = Disks()[0];
        Assert.All(DiskInventory.Segments(disk), s => Assert.False(s.IsFree && s.Size < 8 * DiskOps.Mb));
    }

    // ---- segurança

    [Fact]
    public void Disco_e_particoes_do_windows_ficam_protegidos()
    {
        var system = Disks()[0];
        foreach (var p in system.Partitions)
            Assert.NotNull(DiskSafety.CanChangeData(system, p, "C:", "D:"));
        Assert.NotNull(DiskSafety.CanInitialize(system));
    }

    [Fact]
    public void Disco_de_dados_pode_ser_alterado_mas_nao_a_unidade_do_aplicativo()
    {
        var hd = Disks()[1];
        var d = hd.Partitions[0];
        Assert.Null(DiskSafety.CanChangeData(hd, d, "C:", "C:"));
        Assert.Contains("MB Extreme", DiskSafety.CanChangeData(hd, d, "C:", "D:"));
        Assert.Contains("Windows", DiskSafety.CanChangeData(hd, d, "D:", "C:"));
    }

    [Fact]
    public void Disco_offline_ou_somente_leitura_nao_aceita_alteracoes()
    {
        var hd = Disks()[1] with { IsOffline = true };
        Assert.Contains("offline", DiskSafety.CanChangeData(hd, hd.Partitions[0], "C:", "C:"));
        var ro = Disks()[1] with { IsReadOnly = true };
        Assert.Contains("leitura", DiskSafety.CanResize(ro, ro.Partitions[0]));
    }

    [Fact]
    public void Redimensionar_so_particoes_ntfs_com_letra_e_nunca_as_de_sistema()
    {
        var system = Disks()[0];
        Assert.Null(DiskSafety.CanResize(system, system.Partitions[2]));      // C: pode reduzir para criar outra unidade
        Assert.NotNull(DiskSafety.CanResize(system, system.Partitions[0]));   // EFI
        Assert.NotNull(DiskSafety.CanResize(system, system.Partitions[1]));   // MSR

        var fat = Disks()[1];
        var p = fat.Partitions[0] with { Volume = fat.Partitions[0].Volume! with { FileSystem = "FAT32" } };
        Assert.Contains("NTFS", DiskSafety.CanResize(fat, p));
    }

    [Fact]
    public void Inicializar_so_disco_novo_e_fora_do_windows()
    {
        Assert.Null(DiskSafety.CanInitialize(Disks()[2]));
        Assert.Contains("já foi inicializado", DiskSafety.CanInitialize(Disks()[1]));
    }

    [Fact]
    public void Criar_particao_exige_espaco_livre_e_disco_inicializado()
    {
        var hd = Disks()[1];
        var free = DiskInventory.Segments(hd)[1];
        Assert.Null(DiskSafety.CanCreate(hd, free));
        Assert.NotNull(DiskSafety.CanCreate(hd, DiskInventory.Segments(hd)[0]));
        var raw = Disks()[2];
        Assert.Contains("Inicialize", DiskSafety.CanCreate(raw, DiskInventory.Segments(raw)[0]));
        Assert.Contains("pequeno", DiskSafety.CanCreate(hd, free with { Size = 10 * DiskOps.Mb }));
    }

    // ---- comandos

    [Fact]
    public void Formatar_monta_o_comando_certo()
    {
        var c = DiskOps.Format('d', FileSystemKind.Ntfs, "Jogos", full: false);
        Assert.Contains("Format-Volume -DriveLetter D -FileSystem NTFS -NewFileSystemLabel 'Jogos' -Full:$false -Confirm:$false", c.Script);
        Assert.Contains("Apaga tudo em D:", c.Summary);
        Assert.Contains("-Full:$true", DiskOps.Format('E', FileSystemKind.ExFat, "", true).Script);
    }

    [Theory]
    [InlineData("a'; Remove-Item C:\\ -Recurse; '")]
    [InlineData("nome\"aspas")]
    [InlineData("a|b")]
    [InlineData("a$(x)")]
    [InlineData("a;b")]
    public void Nome_do_disco_nao_deixa_entrar_comandos(string label)
    {
        Assert.NotNull(DiskOps.ValidateLabel(label, FileSystemKind.Ntfs));
        Assert.Throws<ArgumentException>(() => DiskOps.Format('D', FileSystemKind.Ntfs, label, false));
        Assert.Throws<ArgumentException>(() => DiskOps.Rename('D', label));
    }

    [Fact]
    public void Nome_aceita_acentos_e_respeita_o_limite_de_cada_sistema_de_arquivos()
    {
        Assert.Null(DiskOps.ValidateLabel("Vídeos e Jogos", FileSystemKind.Ntfs));
        Assert.NotNull(DiskOps.ValidateLabel(new string('a', 12), FileSystemKind.Fat32));
        Assert.Null(DiskOps.ValidateLabel(new string('a', 11), FileSystemKind.Fat32));
        Assert.NotNull(DiskOps.ValidateLabel(new string('a', 16), FileSystemKind.ExFat));
    }

    [Fact]
    public void Fat32_so_ate_32_gb_e_letras_invalidas_sao_recusadas()
    {
        Assert.NotNull(DiskOps.ValidateFormat(FileSystemKind.Fat32, 64 * Gb));
        Assert.Null(DiskOps.ValidateFormat(FileSystemKind.Fat32, 16 * Gb));
        Assert.Null(DiskOps.ValidateFormat(FileSystemKind.Ntfs, 4000 * Gb));
        Assert.Throws<ArgumentException>(() => DiskOps.Format('1', FileSystemKind.Ntfs, "", false));
        Assert.Throws<ArgumentException>(() => DiskOps.ChangeLetter(1, 2, 'D', 'C'));
        Assert.Throws<ArgumentException>(() => DiskOps.CreatePartition(1, 0, 0, 'A', FileSystemKind.Ntfs, ""));
    }

    [Fact]
    public void Letras_livres_nao_incluem_as_em_uso_nem_A_B_C()
    {
        var free = DiskOps.FreeLetters(new[] { 'C', 'D', 'e' });
        Assert.DoesNotContain('D', free);
        Assert.DoesNotContain('E', free);
        Assert.DoesNotContain('C', free);
        Assert.Equal('F', free[0]);
        Assert.Equal('Z', free[^1]);
    }

    [Fact]
    public void Outras_operacoes_geram_os_comandos_esperados()
    {
        Assert.Equal("Set-Partition -DiskNumber 1 -PartitionNumber 2 -NewDriveLetter G; 'Letra alterada.'", DiskOps.ChangeLetter(1, 2, 'D', 'g').Script);
        Assert.Equal("Resize-Partition -DiskNumber 1 -PartitionNumber 2 -Size 400000000000; 'Partição reduzida.'", DiskOps.Shrink(1, 2, 'D', 400_000_000_000).Script);
        Assert.Contains("Resize-Partition", DiskOps.Extend(1, 2, 'D', 600_000_000_000).Script);
        Assert.Equal("Remove-Partition -DiskNumber 1 -PartitionNumber 2 -Confirm:$false; 'Partição excluída.'", DiskOps.Delete(1, 2, 'D').Script);
        Assert.Equal("Initialize-Disk -Number 2 -PartitionStyle GPT; 'Disco inicializado.'", DiskOps.Initialize(2, true).Script);
        Assert.Contains("MBR", DiskOps.Initialize(2, false).Script);
        Assert.Throws<ArgumentException>(() => DiskOps.Shrink(1, 2, 'D', 0));
    }

    [Fact]
    public void Criar_particao_usa_o_espaco_todo_ou_um_tamanho_e_ja_formata()
    {
        var all = DiskOps.CreatePartition(1, 0, 0, 'E', FileSystemKind.Ntfs, "Dados");
        Assert.Equal("New-Partition -DiskNumber 1 -UseMaximumSize -DriveLetter E | Format-Volume -FileSystem NTFS -NewFileSystemLabel 'Dados' -Confirm:$false | Out-Null; 'Partição criada.'", all.Script);
        var sized = DiskOps.CreatePartition(1, 0, 100 * Gb, 'E', FileSystemKind.ExFat, "");
        Assert.Contains("-Size 107374182400", sized.Script);
        Assert.Throws<ArgumentException>(() => DiskOps.CreatePartition(1, 0, 64 * Gb, 'E', FileSystemKind.Fat32, ""));
    }

    [Fact]
    public void Tamanhos_aceitos_pelo_windows_sao_lidos_da_saida()
    {
        Assert.True(DiskOps.TryParseSupportedSizes("52428800\r\n511000000000\r\n", out long min, out long max));
        Assert.Equal(52428800, min);
        Assert.Equal(511000000000, max);
        Assert.False(DiskOps.TryParseSupportedSizes("erro", out _, out _));
        Assert.Contains("Get-PartitionSupportedSize -DiskNumber 1 -PartitionNumber 3", DiskOps.SupportedSizesScript(1, 3));
    }

    // ---- reparo por disco

    [Fact]
    public void Reparo_do_disco_de_dados_usa_a_letra_escolhida_sem_reiniciar()
    {
        var tasks = RepairCatalog.DiskTasks("e", false, "pt");
        var scan = tasks.First(t => t.Id == "chkdsk-scan");
        var fix = tasks.First(t => t.Id == "chkdsk-fix");
        var deep = tasks.First(t => t.Id == "chkdsk-deep");
        Assert.Contains("Repair-Volume -DriveLetter E -Scan", scan.ManualCommands);
        Assert.Contains("Repair-Volume -DriveLetter E -OfflineScanAndFix", fix.ManualCommands);
        Assert.Contains("chkdsk.exe E: /f /r /x", deep.ManualCommands);
        Assert.NotNull(fix.Caution);
        Assert.All(new[] { scan, fix, deep }, t => Assert.False(t.NeedsRestart));
        Assert.Contains(tasks, t => t.Id == "disk-health");
        Assert.DoesNotContain("C:", string.Join(" ", tasks.Select(t => t.ManualCommands)).Replace("Get-PhysicalDisk", ""));
    }

    [Fact]
    public void Reparo_do_disco_do_windows_continua_agendando_no_reinicio()
    {
        var tasks = RepairCatalog.DiskTasks("C:", true, "pt");
        var fix = tasks.First(t => t.Id == "chkdsk-fix");
        Assert.Contains("C: /f /r", fix.ManualCommands);
        Assert.True(fix.NeedsRestart);
    }

    [Theory]
    [InlineData("E", "E:")]
    [InlineData("e:", "E:")]
    [InlineData("D:\\", "D:")]
    public void Letra_da_unidade_e_normalizada(string input, string expected) => Assert.Equal(expected, RepairCatalog.NormalizeDrive(input));

    [Theory]
    [InlineData("")]
    [InlineData("EF")]
    [InlineData("1:")]
    [InlineData("E: & calc")]
    [InlineData("E:\\x")]
    public void Letra_de_unidade_estranha_e_recusada(string input) =>
        Assert.Throws<ArgumentException>(() => RepairCatalog.NormalizeDrive(input));
}
