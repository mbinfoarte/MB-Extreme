using MBExtreme.Core.Repair;
using Xunit;

namespace MBExtreme.Core.Tests;

public class EscanearWindowsTests
{
    [Theory]
    [InlineData("No component store corruption detected.\r\nThe operation completed successfully.", RepairScan.ImageState.Healthy)]
    [InlineData("O repositório de componentes é reparável.", RepairScan.ImageState.Repairable)]
    [InlineData("The component store is repairable.", RepairScan.ImageState.Repairable)]
    [InlineData("The component store is not repairable.", RepairScan.ImageState.NotRepairable)]
    [InlineData("Error: 740 Elevated permissions are required", RepairScan.ImageState.Unknown)]
    public void Le_o_dism(string output, RepairScan.ImageState expected) => Assert.Equal(expected, RepairScan.ParseDism(output));

    [Fact]
    public void Le_o_wmi_e_a_ordem()
    {
        Assert.True(RepairScan.ParseWmi("WMI repository is consistent"));
        Assert.False(RepairScan.ParseWmi("O repositório WMI está inconsistente"));
        Assert.Null(RepairScan.ParseWmi("?"));
        Assert.Equal(1, RepairScan.Order("dism-scan"));
        Assert.True(RepairScan.Order("dism-restore") < RepairScan.Order("sfc"));
        Assert.True(RepairScan.Order("sfc") < RepairScan.Order("chkdsk-scan"));
        Assert.Equal(0, RepairScan.Order("icons"));
        var ids = RepairCatalog.Create("C:", "pt").Select(t => t.Id).Concat(RepairCatalog.DiskTasks("C:", true, "pt").Select(t => t.Id)).ToHashSet();
        Assert.All(RepairScan.Sequence, id => Assert.Contains(id, ids));
    }
}
