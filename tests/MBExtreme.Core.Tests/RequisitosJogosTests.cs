using MBExtreme.Core.Games;
using Xunit;

namespace MBExtreme.Core.Tests;

public class RequisitosJogosTests
{
    private const string English =
        "Minimum:\nOS: Windows 10 64-bit\nProcessor: Intel Core i5-8400 / AMD Ryzen 5 2600\nMemory: 8 GB RAM\nGraphics: NVIDIA GeForce GTX 1060 (6 GB VRAM) / AMD Radeon RX 580\nDirectX: Version 12\nStorage: 50 GB available space\nAdditional Notes: SSD recommended";

    private const string Portuguese =
        "Mínimo:\nSO: Windows 10 de 64 bits\nProcessador: Intel Core i5-6600K ou AMD Ryzen 3 1200\nMemória: 8 GB de RAM\nPlaca de vídeo: NVIDIA GeForce GTX 970 ou AMD Radeon R9 290\nDirectX: Versão 11\nArmazenamento: 40 GB de espaço disponível";

    private static MachineProfile Machine(string cpu = "Intel(R) Core(TM) i5-8400 CPU @ 2.80GHz", int cores = 6, string[]? gpus = null, double vram = 6, double ram = 16, int build = 19045) =>
        new(cpu, cores, cores * 2, gpus ?? new[] { "NVIDIA GeForce GTX 1060 6GB" }, vram, ram, build, "Windows 10 22H2");

    // ---- leitura do texto

    [Fact]
    public void Texto_em_ingles_vira_itens()
    {
        var s = RequirementParser.Parse(English);
        Assert.Equal("Windows 10 64-bit", s.Os);
        Assert.Equal("Intel Core i5-8400 / AMD Ryzen 5 2600", s.Cpu);
        Assert.Equal(8, s.RamGb);
        Assert.Contains("GTX 1060", s.Gpu);
        Assert.Equal(6, s.VramGb);
        Assert.Equal(50, s.DiskGb);
        Assert.Equal(12, s.DirectX);
        Assert.False(s.IsEmpty);
    }

    [Fact]
    public void Texto_em_portugues_vira_itens()
    {
        var s = RequirementParser.Parse(Portuguese);
        Assert.Contains("Windows 10", s.Os);
        Assert.Contains("i5-6600K", s.Cpu);
        Assert.Equal(8, s.RamGb);
        Assert.Contains("GTX 970", s.Gpu);
        Assert.Null(s.VramGb);
        Assert.Equal(40, s.DiskGb);
        Assert.Equal(11, s.DirectX);
    }

    [Fact]
    public void Itens_na_mesma_linha_e_unidades_diferentes()
    {
        var s = RequirementParser.Parse("OS: Windows 7 Processor: Core 2 Duo E8400 Memory: 4096 MB RAM Graphics: GeForce GTX 560, 1024 MB VRAM Storage: 1 TB");
        Assert.Equal("Windows 7", s.Os);
        Assert.Equal(4, s.RamGb);
        Assert.Equal(1, s.VramGb);
        Assert.Equal(1024, s.DiskGb);
        Assert.Contains("E8400", s.Cpu);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Sem requisitos aqui")]
    public void Sem_requisitos_fica_vazio(string? text) => Assert.True(RequirementParser.Parse(text).IsEmpty);

    // ---- níveis de placas e processadores

    [Theory]
    [InlineData("NVIDIA GeForce GTX 1060 6GB", 100)]
    [InlineData("NVIDIA GeForce GTX 1060 3GB", 90)]
    [InlineData("AMD Radeon RX 580 Series", 105)]
    [InlineData("NVIDIA GeForce RTX 4070 Ti SUPER", 470)]
    [InlineData("NVIDIA GeForce RTX 4070 SUPER", 395)]
    [InlineData("AMD Radeon RX 7900 XTX", 560)]
    [InlineData("Intel(R) Arc(TM) A770 Graphics", 220)]
    [InlineData("AMD Radeon R9 290X", 95)]
    [InlineData("NVIDIA GeForce GTX 1650 SUPER", 95)]
    [InlineData("Intel(R) UHD Graphics 630", 22)]
    [InlineData("AMD Radeon(TM) Graphics", 40)]
    [InlineData("Intel(R) Iris(R) Xe Graphics", 45)]
    public void Placas_conhecidas(string name, double expected) => Assert.Equal(expected, HardwareTiers.GpuIndex(name)!.Value, 6);

    [Fact]
    public void Placa_de_notebook_vale_menos_e_desconhecida_vira_nulo()
    {
        Assert.Equal(185 * 0.75, HardwareTiers.GpuIndex("NVIDIA GeForce RTX 3060 Laptop GPU")!.Value, 6);
        Assert.Equal(185 * 0.65, HardwareTiers.GpuIndex("NVIDIA GeForce RTX 3060 Max-Q")!.Value, 6);
        Assert.Null(HardwareTiers.GpuIndex("Matrox G200eW"));
        Assert.Null(HardwareTiers.GpuIndex(""));
        Assert.Null(HardwareTiers.GpuIndex(null));
        Assert.True(HardwareTiers.IsIntegratedGpu("Intel(R) UHD Graphics 630"));
        Assert.False(HardwareTiers.IsIntegratedGpu("NVIDIA GeForce GTX 1060"));
        Assert.False(HardwareTiers.IsIntegratedGpu("Intel(R) Arc(TM) A770 Graphics"));
    }

    [Fact]
    public void Ordem_das_placas_faz_sentido()
    {
        string[] ascending = { "GeForce GT 710", "GeForce GTX 750 Ti", "GeForce GTX 1050 Ti", "GeForce GTX 1060 6GB", "GeForce GTX 1660 Ti", "GeForce RTX 2070", "GeForce RTX 3070", "GeForce RTX 4080", "GeForce RTX 4090", "GeForce RTX 5090" };
        var values = ascending.Select(n => HardwareTiers.GpuIndex(n)!.Value).ToList();
        Assert.Equal(values.OrderBy(v => v), values);
    }

    [Theory]
    [InlineData("Intel(R) Core(TM) i5-8400 CPU @ 2.80GHz", 100)]
    [InlineData("13th Gen Intel(R) Core(TM) i5-13400F", 205)]
    [InlineData("AMD Ryzen 5 5600X 6-Core Processor", 170)]
    [InlineData("AMD Ryzen 7 7800X3D 8-Core Processor", 215 * 1.15 * 1.3)]
    [InlineData("Intel(R) Core(TM) i7-8700K CPU @ 3.70GHz", 100 * 1.2 * 1.08)]
    [InlineData("Intel(R) Core(TM) i7-4770K", 70 * 1.12 * 1.08)]
    [InlineData("Intel(R) Core(TM) 2 Duo CPU E8400", 25)]
    [InlineData("AMD FX(tm)-8350 Eight-Core Processor", 62)]
    [InlineData("Intel(R) Core(TM) i3-12100F", 170 * 0.85)]
    public void Processadores_conhecidos(string name, double expected) => Assert.Equal(expected, HardwareTiers.CpuIndex(name)!.Value, 4);

    [Fact]
    public void Processador_desconhecido_vira_nulo_e_a_ordem_faz_sentido()
    {
        Assert.Null(HardwareTiers.CpuIndex("Snapdragon X Elite"));
        Assert.Null(HardwareTiers.CpuIndex(null));
        string[] ascending = { "Intel Core i3-2100", "Intel Core i5-2500K", "Intel Core i7-4770K", "Intel Core i5-8400", "Intel Core i7-9700K", "Intel Core i5-12600K", "Intel Core i9-13900K", "AMD Ryzen 7 5800X", "AMD Ryzen 9 7950X" };
        var values = ascending.Select(n => HardwareTiers.CpuIndex(n)!.Value).ToList();
        Assert.Equal(values.OrderBy(v => v).Count(), values.Count);
        Assert.True(values[0] < values[3] && values[3] < values[5] && values[5] < values[6]);
        Assert.True(HardwareTiers.CpuIndex("Intel Core i7-1165G7")! < HardwareTiers.CpuIndex("Intel Core i7-11700K")!);   // notebook U < desktop K
    }

    // ---- comparação

    [Fact]
    public void Pc_forte_atende_o_recomendado()
    {
        var rec = "OS: Windows 10\nProcessor: Intel Core i5-8400\nMemory: 16 GB RAM\nGraphics: NVIDIA GeForce GTX 1060 6GB\nStorage: 50 GB";
        var machine = Machine("13th Gen Intel(R) Core(TM) i5-13600K", 14, new[] { "NVIDIA GeForce RTX 4070" }, 12, 32, 22631);
        var report = RequirementChecker.Check(English, rec, machine);
        Assert.Equal(Fit.Recommended, report.Verdict);
        Assert.Equal("Atende o recomendado.", report.Summary);
        Assert.Contains(report.Minimum, i => i.Name == "Placa de vídeo" && i.Status == ItemStatus.Ok);
        Assert.Contains(report.Recommended, i => i.Name == "Espaço em disco" && i.Status == ItemStatus.Info);
    }

    [Fact]
    public void Pc_fraco_fica_abaixo_do_minimo_e_diz_em_que()
    {
        var machine = Machine("Intel(R) Core(TM) i3-4130 CPU", 2, new[] { "NVIDIA GeForce GTX 750 Ti" }, 2, 4);
        var report = RequirementChecker.Check(English, null, machine);
        Assert.Equal(Fit.Below, report.Verdict);
        Assert.StartsWith("Abaixo do mínimo em:", report.Summary);
        Assert.Contains("placa de vídeo", report.Summary);
        Assert.Contains("memória", report.Summary);
        Assert.Contains("processador", report.Summary);
        Assert.Equal(ItemStatus.Below, report.Minimum.Single(i => i.Name == "Memória").Status);
    }

    [Fact]
    public void Alternativas_valem_pela_mais_fraca_e_o_mesmo_pc_atende()
    {
        // GTX 1060 (100) ou RX 580 (105): qualquer uma serve; quem tem RX 580 está acima
        var machine = Machine("AMD Ryzen 5 3600 6-Core Processor", 6, new[] { "AMD Radeon RX 580 Series" }, 8, 16);
        var report = RequirementChecker.Check(English, null, machine);
        Assert.Equal(ItemStatus.Ok, report.Minimum.Single(i => i.Name == "Placa de vídeo").Status);
        Assert.Equal(ItemStatus.Ok, report.Minimum.Single(i => i.Name == "Processador").Status);
        Assert.Equal(Fit.Minimum, report.Verdict);                  // sem texto recomendado: só dá para dizer que atende o mínimo
    }

    [Fact]
    public void Minimo_atendido_com_recomendado_acima_mostra_onde_falta()
    {
        var rec = "Processor: Intel Core i7-10700K\nMemory: 16 GB RAM\nGraphics: NVIDIA GeForce RTX 3070";
        var report = RequirementChecker.Check(English, rec, Machine());
        Assert.Equal(Fit.Minimum, report.Verdict);
        Assert.Contains("abaixo do recomendado em:", report.Summary);
        Assert.Contains("placa de vídeo", report.Summary);
    }

    [Fact]
    public void Por_pouco_conta_como_proximo()
    {
        // pede GTX 1060 (100); GTX 970 vale 85: 85% -> próximo, atende por pouco
        var machine = Machine("Intel(R) Core(TM) i5-8400", 6, new[] { "NVIDIA GeForce GTX 970" }, 4, 16);
        var report = RequirementChecker.Check("Graphics: NVIDIA GeForce GTX 1060\nMemory: 8 GB RAM", null, machine);
        Assert.Equal(ItemStatus.Close, report.Minimum.Single(i => i.Name == "Placa de vídeo").Status);
        Assert.Equal(Fit.Minimum, report.Verdict);
        Assert.Contains("por pouco", report.Summary);
    }

    [Fact]
    public void Windows_11_exigido_num_windows_10_fica_abaixo()
    {
        var report = RequirementChecker.Check("OS: Windows 11\nMemory: 8 GB RAM\nGraphics: GTX 1060", null, Machine(build: 19045));
        Assert.Equal(ItemStatus.Below, report.Minimum.Single(i => i.Name == "Sistema").Status);
        Assert.Equal(Fit.Below, report.Verdict);
        var ok = RequirementChecker.Check("OS: Windows 10\nMemory: 8 GB RAM\nGraphics: GTX 1060", null, Machine(build: 22631));
        Assert.Equal(ItemStatus.Ok, ok.Minimum.Single(i => i.Name == "Sistema").Status);
    }

    [Fact]
    public void Modelo_nao_reconhecido_fica_sem_dados_e_nao_chuta()
    {
        var report = RequirementChecker.Check("Processor: Cell Broadband\nGraphics: Placa Misteriosa X", null, Machine());
        Assert.Equal(Fit.Unknown, report.Verdict);
        Assert.All(report.Minimum, i => Assert.Equal(ItemStatus.Unknown, i.Status));
        Assert.Equal(Fit.Unknown, RequirementChecker.Check("", "", Machine()).Verdict);
        Assert.Equal("A loja não informou requisitos que dê para comparar.", RequirementChecker.Check(null, null, Machine()).Summary);
    }

    [Fact]
    public void Sem_modelo_de_processador_compara_so_os_nucleos()
    {
        var quad = RequirementChecker.Check("Processor: Quad-core 2.5 GHz\nMemory: 4 GB RAM", null, Machine(cores: 4));
        Assert.Equal(ItemStatus.Ok, quad.Minimum.Single(i => i.Name == "Processador").Status);
        var dual = RequirementChecker.Check("Processor: Quad-core 2.5 GHz\nMemory: 4 GB RAM", null, Machine(cores: 2));
        Assert.Equal(ItemStatus.Below, dual.Minimum.Single(i => i.Name == "Processador").Status);
    }

    [Fact]
    public void Placa_integrada_nao_atende_jogo_que_pede_dedicada_e_vram_desconhecida_fica_sem_dados()
    {
        var machine = Machine("Intel(R) Core(TM) i5-8400", 6, new[] { "Intel(R) UHD Graphics 630" }, 0, 16);
        var report = RequirementChecker.Check("Graphics: NVIDIA GeForce GTX 960 (2 GB VRAM)", null, machine);
        Assert.Equal(ItemStatus.Below, report.Minimum.Single(i => i.Name == "Placa de vídeo").Status);
        Assert.Equal(ItemStatus.Unknown, report.Minimum.Single(i => i.Name == "Memória de vídeo").Status);
    }
}
