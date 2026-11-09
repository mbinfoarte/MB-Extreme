using MBExtreme.Core.Drivers;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>Casar o aparelho sem driver com o driver do Windows Update pelo ID de hardware.</summary>
public class DriversFaltandoTests
{
    private static ProblemDevice Device(params string[] hwids) =>
        new("Aparelho X", "Net", 28, @"PCI\VEN_8086&DEV_1234\3&11", hwids);

    [Fact]
    public void Normaliza_id_tirando_revisao_e_caixa()
    {
        Assert.Equal(@"PCI\VEN_8086&DEV_1234&SUBSYS_00011025",
            DriverService.NormalizeHardwareId(@"pci\ven_8086&dev_1234&subsys_00011025&rev_05"));
        Assert.Equal(@"USB\VID_0BDA&PID_8153", DriverService.NormalizeHardwareId(@"USB\VID_0BDA&PID_8153"));
    }

    [Fact]
    public void Acha_o_driver_do_aparelho_ignorando_revisao()
    {
        var device = Device(@"PCI\VEN_8086&DEV_1234&REV_05", @"PCI\VEN_8086&DEV_1234");
        var pending = new[]
        {
            new PendingDriver("11111111-1111-1111-1111-111111111111", "Intel - Net", "Intel", "Wi-Fi", "2026", @"PCI\VEN_8086&DEV_1234"),
            new PendingDriver("22222222-2222-2222-2222-222222222222", "Outro", "Realtek", "LAN", "2026", @"PCI\VEN_10EC&DEV_8168"),
        };
        var match = DriverService.DriversForDevice(device, pending);
        Assert.Single(match);
        Assert.Equal("11111111-1111-1111-1111-111111111111", match[0].Id);
    }

    [Fact]
    public void Sem_id_de_hardware_nao_casa_nada()
    {
        var pending = new[] { new PendingDriver("33333333-3333-3333-3333-333333333333", "X", "Y", "Z", "2026", @"PCI\VEN_8086&DEV_1234") };
        Assert.Empty(DriverService.DriversForDevice(Device(), pending));
        var deviceSemDriverHw = Device(@"PCI\VEN_8086&DEV_1234");
        var pendingSemHw = new[] { new PendingDriver("44444444-4444-4444-4444-444444444444", "X", "Y", "Z", "2026", "") };
        Assert.Empty(DriverService.DriversForDevice(deviceSemDriverHw, pendingSemHw));
    }

    [Fact]
    public void Codigo_28_marca_aparelho_sem_driver_e_tem_link_do_catalogo()
    {
        Assert.True(Device(@"PCI\VEN_8086&DEV_1234").Missing);
        Assert.False(new ProblemDevice("X", "Net", 43, "id", new[] { "hw" }).Missing);
        Assert.StartsWith("https://www.catalog.update.microsoft.com/Search.aspx?q=", DeviceDrivers.CatalogUrl(@"PCI\VEN_8086&DEV_1234"));
    }

    [Fact]
    public void Le_o_id_de_hardware_do_json_do_windows_update()
    {
        const string json = """
            [{"Id":"55555555-5555-5555-5555-555555555555","Title":"Intel Wi-Fi","Manufacturer":"Intel","Model":"AX201","Date":"2026-01-01","HardwareId":"PCI\\VEN_8086&DEV_A0F0"}]
            """;
        var list = DriverService.ParsePendingDrivers(json);
        Assert.Single(list);
        Assert.Equal(@"PCI\VEN_8086&DEV_A0F0", list[0].HardwareId);
    }
}
