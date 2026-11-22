using MBExtreme.Core.Hardware;
using Xunit;

namespace MBExtreme.Core.Tests;

public class BiosMenuTests
{
    private static HardwareInventory Pc(string cpu, string vendor, string board, string mem = "DDR5", int rated = 6000, int configured = 4800, bool? bar = false) => new(
        new CpuInfo(cpu, vendor, 8, 16, 5000, 8192, 32768, null), new BoardInfo(board, "B650"), new SystemModel("", ""), new BiosInfo("AMI", "1.0", null),
        new[] { new MemoryModule("A1", 16L << 30, rated, configured, mem, "Kingston", "X") }, Array.Empty<GpuSummary>(),
        new WindowsInfo("Pro", "24H2", 26100), false, null, bar, 144);

    [Fact]
    public void Menu_completo_com_os_oito_assuntos()
    {
        var topics = BiosTopics.Build(Pc("AMD Ryzen 7 7800X3D", "AuthenticAMD", "ASUSTeK COMPUTER INC."));
        Assert.Equal(new[] { "undervolt", "overclock", "tpm", "rebar", "xmp", "fans", "water" }, topics.Select(t => t.Key));
        Assert.Equal(8, BiosTopics.Menu.Count);
        Assert.Contains("Ai Tweaker", topics[0].WhereInBios);
        Assert.Contains("Curve Optimizer", topics[0].Title);
        Assert.Equal("Não disponível neste processador", topics[1].Status);   // X3D
        Assert.Contains("EXPO", topics.First(t => t.Key == "xmp").Title);
        Assert.Contains("Desligado", topics.First(t => t.Key == "xmp").Status);
        Assert.Equal("Desligado", topics.First(t => t.Key == "rebar").Status);
    }

    [Fact]
    public void Intel_sem_k_nao_tem_overclock_e_msi_tem_o_caminho_dela()
    {
        var topics = BiosTopics.Build(Pc("Intel(R) Core(TM) i5-12400F", "GenuineIntel", "Micro-Star International Co., Ltd.", "DDR4", 3200, 3200, true));
        Assert.Contains("Offset", topics[0].Title);
        Assert.Equal("Não disponível neste processador", topics[1].Status);
        Assert.StartsWith("Advanced (F7) › OC", topics[0].WhereInBios);
        Assert.Contains("XMP", topics.First(t => t.Key == "xmp").Title);
        Assert.StartsWith("Ligado", topics.First(t => t.Key == "xmp").Status);
        var k = BiosTopics.Build(Pc("Intel(R) Core(TM) i7-14700K", "GenuineIntel", "Gigabyte Technology"));
        Assert.Equal("Opcional (avançado)", k[1].Status);
        Assert.Equal("Gigabyte", BiosTopics.Brand("Gigabyte Technology Co., Ltd."));
    }
}
