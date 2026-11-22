using MBExtreme.Core.Storage;
using Xunit;

namespace MBExtreme.Core.Tests;

public class DiscosSaudeTests
{
    private static DiskInfo Disk(string health = "Healthy", long? realloc = null, long? pending = null, double? wear = null, double? temp = null, string media = "SSD", string bus = "SATA") =>
        new("Samsung SSD 860 EVO 500GB", media, bus, health, 500_000_000_000, temp, wear, 1000, 0, 0, Reallocated: realloc, Pending: pending);

    [Fact]
    public void Le_smart_cru()
    {
        var data = new byte[512];
        void Put(int slot, byte id, long raw) { int at = 2 + slot * 12; data[at] = id; for (int b = 0; b < 4; b++) data[at + 5 + b] = (byte)(raw >> (8 * b)); }
        Put(0, 5, 12); Put(1, 197, 3); Put(2, 9, 70000);
        var map = DiskHealth.ParseSmart(data);
        Assert.Equal(12, map[5]);
        Assert.Equal(3, map[197]);
        Assert.Equal(70000, map[9]);
    }

    [Fact]
    public void Nota_como_crystaldiskinfo()
    {
        Assert.Equal(DiskGrade.Good, DiskHealth.Grade(Disk()).Grade);
        Assert.Equal(DiskGrade.Caution, DiskHealth.Grade(Disk(realloc: 3)).Grade);
        Assert.Equal(DiskGrade.Bad, DiskHealth.Grade(Disk(pending: 1)).Grade);
        Assert.Equal(DiskGrade.Bad, DiskHealth.Grade(Disk(wear: 95)).Grade);
        Assert.Equal(DiskGrade.Bad, DiskHealth.Grade(Disk(health: "Warning")).Grade);
        Assert.Equal(DiskGrade.Caution, DiskHealth.Grade(Disk(temp: 58, media: "HDD")).Grade);
        Assert.Equal(DiskGrade.Good, DiskHealth.Grade(Disk(temp: 58)).Grade);
        Assert.Equal("SSD NVMe", DiskHealth.Kind(Disk(bus: "NVMe")));
    }

    [Fact]
    public void Casa_o_nome_do_wmi_com_o_modelo()
    {
        Assert.True(DiskHealth.SameDisk(@"SCSI\Disk&Ven_&Prod_Samsung_SSD_860\4&2a&0&000000_0", "Samsung SSD 860 EVO 500GB"));
        Assert.False(DiskHealth.SameDisk(@"SCSI\Disk&Ven_WDC&Prod_WD10EZEX\4&2a", "Samsung SSD 860 EVO 500GB"));
    }

    [Fact]
    public void Le_velocidade_do_winsat()
    {
        Assert.Equal(545.12, DiskBench.ParseMbps("> Disk  Sequential 64.0 Read                   545.12 MB/s          7.9"));
        Assert.Null(DiskBench.ParseMbps("erro"));
        Assert.Equal(new[] { "disk", "-seq", "-read", "-drive", "c" }, DiskBench.Tests('C')[0].Args);
    }
}
