using System.Text.Json;
using MBExtreme.Core.Benchmark;
using Xunit;

namespace MBExtreme.Core.Tests;

public class IndiceHardwareTests
{
    private static readonly string[] Scenes = { "bmw27", "classroom", "barbershop" };
    private static readonly double[] BaseSeconds = { 100, 200, 400 };

    private static string NewStyle(string device, string type, double speed, string scene, double baseSeconds, string version = "4.2.0") =>
        JsonSerializer.Serialize(new
        {
            schema_version = "v4",
            data = new[]
            {
                new
                {
                    device_info = new { compute_devices = new[] { new { name = device, type } } },
                    system_info = new { system = "Windows", bitness = "64bit" },
                    blender_version = new { version },
                    scene = new { label = scene },
                    stats = new { result = "OK", total_render_time = baseSeconds / speed },
                },
            },
        });

    private static string OldStyle(string device, string type, double speed, string version = "3.6.0") =>
        JsonSerializer.Serialize(new
        {
            schema_version = "v2",
            data = new
            {
                device_info = new { compute_devices = new[] { new { name = device, type } } },
                blender_version = new { version },
                scenes = Scenes.Select((s, i) => new { name = s, stats = new { result = "OK", total_render_time = BaseSeconds[i] / speed } }).ToArray(),
            },
        });

    private static List<string> Lines(params (string Name, string Type, double Speed, int Runs)[] devices)
    {
        var lines = new List<string>();
        foreach (var (name, type, speed, runs) in devices)
            for (int r = 0; r < runs; r++)
                for (int i = 0; i < Scenes.Length; i++)
                    lines.Add(NewStyle(name, type, speed * (1 + (r % 3 - 1) * 0.01), Scenes[i], BaseSeconds[i]));
        return lines;
    }

    private static (string, string, double, int)[] Devices() => new[]
    {
        ("NVIDIA GeForce RTX 4090", "OPTIX", 8.0, 5), ("NVIDIA GeForce RTX 3070", "OPTIX", 3.0, 5), ("NVIDIA GeForce GTX 1060 6GB", "CUDA", 1.0, 5),
        ("AMD Radeon RX 6800", "HIP", 2.0, 5), ("AMD Ryzen 9 5950X 16-Core Processor", "CPU", 1.5, 5), ("Intel(R) Core(TM) i7-9700K CPU @ 3.60GHz", "CPU", 0.5, 5),
        ("AMD Ryzen 5 5600X 6-Core Processor", "CPU", 0.9, 5), ("Intel(R) Core(TM) i5-8400 CPU @ 2.80GHz", "CPU", 0.35, 5),
    };

    [Fact]
    public void Indice_separa_cpu_de_gpu_e_ordena_pela_velocidade()
    {
        var data = HardwareIndexBuilder.Build(Lines(Devices()), new DateTime(2026, 10, 24), minSamplesPerDevice: 3, minGroupSize: 4);
        Assert.Equal(new[] { "NVIDIA GeForce RTX 4090", "NVIDIA GeForce RTX 3070", "AMD Radeon RX 6800", "NVIDIA GeForce GTX 1060 6GB" }, data.Gpu.Select(e => e.Name));
        Assert.Equal(new[] { "AMD Ryzen 9 5950X 16-Core Processor", "AMD Ryzen 5 5600X 6-Core Processor", "Intel(R) Core(TM) i7-9700K CPU @ 3.60GHz", "Intel(R) Core(TM) i5-8400 CPU @ 2.80GHz" }, data.Cpu.Select(e => e.Name));
        Assert.Equal(HardwareIndexBuilder.SourceName, data.Source);
    }

    [Fact]
    public void Nota_e_proporcional_a_velocidade_e_a_mediana_vale_100()
    {
        var data = HardwareIndexBuilder.Build(Lines(Devices()), DateTime.Now, 3, 4);
        double r4090 = data.Gpu.Single(e => e.Name.Contains("4090")).Score;
        double r3070 = data.Gpu.Single(e => e.Name.Contains("3070")).Score;
        Assert.Equal(8.0 / 3.0, r4090 / r3070, 1);
        var gpuScores = data.Gpu.Select(e => e.Score).OrderBy(x => x).ToList();
        Assert.Equal(100, (gpuScores[1] + gpuScores[2]) / 2, 0);                                  // mediana de 4 valores
        Assert.All(data.Gpu.Concat(data.Cpu), e => Assert.Equal(5 * 3, e.Samples));
    }

    [Fact]
    public void Aceita_o_formato_antigo_com_lista_de_cenas_e_ignora_linhas_ruins()
    {
        var lines = Lines(Devices());
        for (int i = 0; i < 6; i++) lines.Add(OldStyle("NVIDIA GeForce RTX 4080", "OPTIX", 5.0, version: "4.2.0"));   // 3 cenas por linha
        lines.Add("{ não é json");
        lines.Add("");
        lines.Add("{\"data\": [{\"device_info\": {}}]}");                                   // sem dispositivo
        lines.Add(NewStyle("NVIDIA GeForce RTX 2070", "OPTIX", -1, "bmw27", 100));          // tempo negativo
        var data = HardwareIndexBuilder.Build(lines, DateTime.Now, 3, 4);
        var r4080 = data.Gpu.Single(e => e.Name.Contains("4080"));
        Assert.Equal(18, r4080.Samples);
        Assert.True(r4080.Score > data.Gpu.Single(e => e.Name.Contains("3070")).Score);
        Assert.DoesNotContain(data.Gpu, e => e.Name.Contains("2070"));
    }

    [Fact]
    public void Resultado_com_falha_e_dispositivos_com_poucas_medicoes_ficam_de_fora()
    {
        var lines = Lines(Devices());
        lines.Add(JsonSerializer.Serialize(new
        {
            data = new[] { new { device_info = new { compute_devices = new[] { new { name = "NVIDIA GeForce RTX 3080", type = "OPTIX" } } }, blender_version = new { version = "4.2.0" }, scene = new { label = "bmw27" }, stats = new { result = "CRASHED", total_render_time = 10.0 } } },
        }));
        lines.AddRange(Lines(("NVIDIA GeForce RTX 3060", "OPTIX", 2.5, 1)));               // 3 amostras: no limite
        lines.AddRange(Lines(("NVIDIA GeForce RTX 3050", "OPTIX", 1.5, 0)));
        var data = HardwareIndexBuilder.Build(lines, DateTime.Now, 4, 4);
        Assert.DoesNotContain(data.Gpu, e => e.Name.Contains("3080"));
        Assert.DoesNotContain(data.Gpu, e => e.Name.Contains("3060"));                       // exigimos 4 amostras aqui
        Assert.DoesNotContain(data.Gpu, e => e.Name.Contains("3050"));
    }

    [Fact]
    public void Versoes_do_blender_sao_normalizadas_separadamente()
    {
        // a versão nova é 2x mais rápida para todos: o índice não pode mudar por isso
        var lines = new List<string>();
        foreach (var (name, type, speed, runs) in Devices())
            for (int r = 0; r < runs; r++)
                for (int i = 0; i < Scenes.Length; i++)
                    lines.Add(NewStyle(name, type, speed, Scenes[i], BaseSeconds[i], r % 2 == 0 ? "3.6.0" : "4.2.0"));
        var mixed = HardwareIndexBuilder.Build(lines, DateTime.Now, 3, 4);
        var pure = HardwareIndexBuilder.Build(Lines(Devices()), DateTime.Now, 3, 4);
        Assert.Equal(pure.Gpu.Select(e => e.Name), mixed.Gpu.Select(e => e.Name));
        Assert.InRange(mixed.Gpu[0].Score / mixed.Gpu[1].Score, 2.5, 2.9);
    }

    [Fact]
    public void Sem_dados_suficientes_o_indice_fica_vazio()
    {
        var data = HardwareIndexBuilder.Build(new[] { "{}", "x" }, DateTime.Now);
        Assert.Empty(data.Cpu);
        Assert.Empty(data.Gpu);
        Assert.Null(HardwareIndex.Load(Path.Combine(Path.GetTempPath(), "nao-existe-" + Guid.NewGuid() + ".json")));
    }

    // ---- nomes e consulta

    [Theory]
    [InlineData("NVIDIA GeForce RTX 3070 (OptiX)", "RTX 3070")]
    [InlineData("NVIDIA GeForce RTX 3070 Laptop GPU", "RTX 3070")]
    [InlineData("AMD Ryzen 5 5600X 6-Core Processor", "RYZEN 5 5600X")]
    [InlineData("Intel(R) Core(TM) i7-9700K CPU @ 3.60GHz", "CORE I7-9700K")]
    [InlineData("11th Gen Intel(R) Core(TM) i7-11700K @ 3.60GHz", "CORE I7-11700K")]
    [InlineData("AMD Radeon RX 6800 XT", "RX 6800 XT")]
    [InlineData("Intel(R) Arc(TM) A770 Graphics", "ARC A770")]
    [InlineData("", "")]
    public void Nome_vira_chave(string name, string key) => Assert.Equal(key, HardwareNames.Key(name));

    [Fact]
    public void Consulta_acha_o_mesmo_modelo_escrito_de_outro_jeito_e_calcula_a_posicao()
    {
        var data = HardwareIndexBuilder.Build(Lines(Devices()), DateTime.Now, 3, 4);
        var index = new HardwareIndex(data);
        var top = index.Gpu("NVIDIA GeForce RTX 4090")!;
        Assert.Equal(75, top.BeatsPercent, 6);                                       // supera 3 dos 4 modelos
        Assert.Equal(4, top.Models);
        Assert.Equal(0, index.Gpu("NVIDIA GeForce GTX 1060 6GB")!.BeatsPercent, 6);
        Assert.Equal(50, index.Cpu("AMD Ryzen 5 5600X 6-Core Processor")!.BeatsPercent, 6);
        Assert.Equal(25, index.Cpu("Intel(R) Core(TM) i7-9700K CPU @ 3.60GHz")!.BeatsPercent, 6);
        Assert.Null(index.Gpu("NVIDIA GeForce RTX 3070 Ti"));                         // outro modelo: sem chute
        Assert.Null(index.Gpu("Matrox G200"));
        Assert.Null(index.Cpu(null));
    }

    [Fact]
    public void Salvar_e_carregar_mantem_o_indice()
    {
        string path = Path.Combine(Path.GetTempPath(), "mbx-index-" + Guid.NewGuid().ToString("N"), "hardware-index.json");
        try
        {
            var data = HardwareIndexBuilder.Build(Lines(Devices()), new DateTime(2026, 10, 24), 3, 4);
            HardwareIndex.Save(data, path);
            var loaded = HardwareIndex.Load("nao-existe.json", path)!;
            Assert.Equal(data.Gpu.Count, loaded.Data.Gpu.Count);
            Assert.Equal(data.Cpu[0].Name, loaded.Data.Cpu[0].Name);
            Assert.Equal(new DateTime(2026, 10, 24), loaded.Data.GeneratedAt);
        }
        finally { var dir = Path.GetDirectoryName(path)!; if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
