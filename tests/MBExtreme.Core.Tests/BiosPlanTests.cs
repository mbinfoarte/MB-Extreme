using MBExtreme.Core.Hardware;
using Xunit;

namespace MBExtreme.Core.Tests;

public class BiosPlanTests
{
    private static HardwareInventory Pc(string cpu, string vendor, int rated, int configured, bool? rebar = true, DateTime? bios = null) => new(
        new CpuInfo(cpu, vendor, 8, 16, 5500, 8192, 32768, null), new BoardInfo("ASUSTeK COMPUTER INC.", "TUF GAMING X870-PLUS"), new SystemModel("System", "Product"),
        new BiosInfo("American Megatrends", "1001", bios ?? DateTime.Now.AddMonths(-3)),
        new[] { new MemoryModule("A1", 16L << 30, rated, configured, "DDR5", "Kingston", "X"), new MemoryModule("B1", 16L << 30, rated, configured, "DDR5", "Kingston", "X") },
        new[] { new GpuSummary("NVIDIA GeForce RTX 4070", 12L << 30, "566", MBExtreme.Core.Drivers.GpuVendor.Nvidia) }, new WindowsInfo("Pro", "25H2", 26200), false, true, rebar, 144);

    [Fact]
    public void Memoria_abaixo_do_kit_vira_passo_a_fazer()
    {
        var steps = BiosPlan.Build(Pc("AMD Ryzen 7 9700X 8-Core Processor", "AuthenticAMD", 6000, 4800));
        var mem = steps.Single(s => s.Key == "memory");
        Assert.Equal(BiosStepState.Todo, mem.State);
        Assert.Contains("4800", mem.Detail);
        Assert.Contains("EXPO", mem.Title);
        Assert.Equal("PBO + Curve Optimizer (menos calor)", steps.Single(s => s.Key == "cpu").Title);
    }

    [Fact]
    public void Tudo_certo_nao_inventa_passo()
    {
        var steps = BiosPlan.Build(Pc("AMD Ryzen 7 9700X 8-Core Processor", "AuthenticAMD", 6000, 6000));
        Assert.Equal(BiosStepState.Done, steps.Single(s => s.Key == "memory").State);
        Assert.Equal(BiosStepState.Done, steps.Single(s => s.Key == "rebar").State);
        Assert.Equal(1, BiosPlan.Pending(steps));   // só o Curve Optimizer sugerido
    }

    [Fact]
    public void Bios_velha_e_calor_aparecem()
    {
        var steps = BiosPlan.Build(Pc("AMD Ryzen 7 9700X 8-Core Processor", "AuthenticAMD", 6000, 6000, bios: DateTime.Now.AddYears(-2)), maxCpuTemp: 90);
        Assert.Equal(BiosStepState.Todo, steps.Single(s => s.Key == "bios").State);
        Assert.Equal(BiosStepState.Suggested, steps.Single(s => s.Key == "thermal").State);
    }
}
