using MBExtreme.Core.Cleanup;
using MBExtreme.Core.Storage;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>Fase 4: prévia e pastas excluídas da limpeza, Prefetch fora do padrão, Proteção do Sistema, otimização agendada e histórico dos discos.</summary>
[Collection("Limpeza")]   // mexe no CleanupService.Excluded (estático)
public class LimpezaDiscosFase4Tests
{
    [Fact]
    public void Pasta_excluida_vale_para_ela_e_subpastas_e_nao_pega_nome_parecido()
    {
        var excluded = new[] { @"C:\Users\Ana\AppData\Local\Temp\Projeto" };
        Assert.True(CleanupService.IsExcluded(@"C:\Users\Ana\AppData\Local\Temp\Projeto\a.tmp", excluded));
        Assert.True(CleanupService.IsExcluded(@"c:\users\ana\appdata\local\temp\projeto\sub\b.tmp", excluded));
        Assert.False(CleanupService.IsExcluded(@"C:\Users\Ana\AppData\Local\Temp\Projeto2\c.tmp", excluded));
        Assert.False(CleanupService.IsExcluded(@"C:\Users\Ana\AppData\Local\Temp\d.tmp", excluded));
    }

    [Fact]
    public void Previa_mostra_os_maiores_e_a_limpeza_respeita_as_excluidas()
    {
        string root = Path.Combine(Path.GetTempPath(), "mbx-limpeza-" + Guid.NewGuid().ToString("N"));
        string keep = Path.Combine(root, "guardar");
        Directory.CreateDirectory(keep);
        File.WriteAllBytes(Path.Combine(root, "grande.tmp"), new byte[5000]);
        File.WriteAllBytes(Path.Combine(root, "pequeno.tmp"), new byte[10]);
        File.WriteAllBytes(Path.Combine(keep, "importante.tmp"), new byte[8000]);
        var category = new CleanupCategory("teste", "Teste", "d", new[] { root });
        var before = CleanupService.Excluded;
        try
        {
            var preview = CleanupService.Preview(category, DateTime.UtcNow);
            Assert.Equal(3, preview.Count);
            Assert.EndsWith("importante.tmp", preview[0].Path);   // do maior para o menor

            CleanupService.Excluded = new[] { keep };
            Assert.Equal(2, CleanupService.Preview(category, DateTime.UtcNow).Count);
            Assert.Equal(5010, CleanupService.Scan(category, DateTime.UtcNow).Bytes);
            var result = CleanupService.Clean(category, DateTime.UtcNow);
            Assert.Equal(2, result.FilesDeleted);
            Assert.True(File.Exists(Path.Combine(keep, "importante.tmp")));   // a pasta protegida ficou intacta
        }
        finally
        {
            CleanupService.Excluded = before;
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Prefetch_nao_vem_marcado_por_padrao()
    {
        var prefetch = CleanupCatalog.Create().Single(c => c.Id == "prefetch");
        Assert.False(prefetch.DefaultSelected);
        Assert.Contains("Não recomendado", prefetch.Note);
    }

    [Fact]
    public void Le_as_unidades_com_protecao_do_sistema_no_registro()
    {
        var values = new[]
        {
            @"\\?\Volume{1b9a2c3d-0000-0000-0000-100000000000}\:(C%3A)",
            @"\\?\Volume{2b9a2c3d-0000-0000-0000-100000000000}\:(e%3A)",
            "lixo sem letra",
        };
        var drives = DiskMaintenance.ParseProtected(values);
        Assert.Equal(new[] { 'C', 'E' }, drives.OrderBy(c => c));
        Assert.Empty(DiskMaintenance.ParseProtected(Array.Empty<string>()));
    }

    [Fact]
    public void Le_a_otimizacao_agendada_e_ignora_a_data_de_nunca_rodou()
    {
        var on = DiskMaintenance.ParseOptimize("Ready|2026-11-01T10:00:00.0000000Z");
        Assert.True(on!.Enabled);
        Assert.Equal(2026, on.LastRun!.Value.Year);
        var off = DiskMaintenance.ParseOptimize("Disabled|1999-11-30T00:00:00.0000000Z");
        Assert.False(off!.Enabled);
        Assert.Null(off.LastRun);
        Assert.Null(DiskMaintenance.ParseOptimize(""));
    }

    [Fact]
    public void Historico_do_disco_guarda_uma_por_dia_e_mostra_a_evolucao()
    {
        string path = Path.Combine(Path.GetTempPath(), $"mbx-discos-{Guid.NewGuid():N}.json");
        var history = new DiskHistory(path);
        DiskInfo Disk(double wear, long hours, long errors) => new("Samsung 970", "SSD", "NVMe", "Healthy", 1L << 40, 40, wear, hours, errors, 0);
        var day1 = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        history.Record(new[] { Disk(3, 1000, 0) }, day1);
        history.Record(new[] { Disk(3, 1001, 0) }, day1.AddHours(1));                 // mesmo dia: troca
        var saved = history.Record(new[] { Disk(5, 1500, 2) }, day1.AddDays(30));
        Assert.Equal(2, saved.Count);
        string trend = DiskHistory.Trend(saved, "samsung 970")!;
        Assert.Contains("desgaste 3% → 5%", trend);
        Assert.Contains("+499 h ligado", trend);
        Assert.Contains("2 erro(s) novo(s)", trend);
        Assert.Null(DiskHistory.Trend(saved, "outro disco"));

        var later = history.Record(new[] { Disk(6, 1600, 2) }, day1.AddDays(400));   // mais de um ano: os antigos saem
        Assert.Single(later);
    }
}
