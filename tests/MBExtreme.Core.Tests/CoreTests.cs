using MBExtreme.Core.Dns;
using MBExtreme.Core.Memory;
using MBExtreme.Core.Progress;
using Xunit;

namespace MBExtreme.Core.Tests;

public class MemoryStatusTests
{
    private const long Gb = 1024L * 1024 * 1024;

    [Fact]
    public void ManyFreeIsGood() =>
        Assert.Equal(MemoryLevel.Good, new MemoryStatus(16 * Gb, 8 * Gb).Level);

    [Fact]
    public void LowFreeNeedsCleaning() =>
        Assert.Equal(MemoryLevel.NeedsCleaning, new MemoryStatus(16 * Gb, (long)(3.6 * Gb)).Level);

    [Fact]
    public void VeryLowFreeIsCritical() =>
        Assert.Equal(MemoryLevel.Critical, new MemoryStatus(16 * Gb, 1 * Gb).Level);

    [Fact]
    public void UsedIsTotalMinusAvailable() =>
        Assert.Equal(12 * Gb, new MemoryStatus(16 * Gb, 4 * Gb).UsedBytes);

    [Fact]
    public void ZeroTotalDoesNotDivideByZero() =>
        Assert.Equal(0, new MemoryStatus(0, 0).FreePercent);
}

public class DnsPacketTests
{
    [Fact]
    public void QueryHasHeaderNameAndTypeClass()
    {
        var query = DnsPacket.BuildQuery(0x1234, "a.bc");

        Assert.Equal(22, query.Length);
        Assert.Equal(0x12, query[0]);
        Assert.Equal(0x34, query[1]);
        Assert.Equal(new byte[] { 1, (byte)'a', 2, (byte)'b', (byte)'c', 0 }, query[12..18]);
        Assert.Equal(new byte[] { 0, 1, 0, 1 }, query[18..22]);
    }

    [Fact]
    public void TooLongLabelIsRejected() =>
        Assert.Throws<ArgumentException>(() => DnsPacket.BuildQuery(1, new string('a', 64) + ".com"));

    [Fact]
    public void ValidResponseIsAccepted()
    {
        var data = DnsPacket.BuildQuery(7, "a.bc");
        data[2] |= 0x80;
        Assert.True(DnsPacket.IsSuccessResponse(data, data.Length, 7));
    }

    [Fact]
    public void ErrorCodeOrWrongIdOrQueryIsRejected()
    {
        var data = DnsPacket.BuildQuery(7, "a.bc");
        Assert.False(DnsPacket.IsSuccessResponse(data, data.Length, 7)); // ainda é consulta

        data[2] |= 0x80;
        Assert.False(DnsPacket.IsSuccessResponse(data, data.Length, 8)); // id diferente

        data[3] |= 0x03; // NXDOMAIN
        Assert.False(DnsPacket.IsSuccessResponse(data, data.Length, 7));
        Assert.False(DnsPacket.IsSuccessResponse(data, 5, 7));           // curto demais
    }
}

public class ProgressEventTests
{
    [Fact]
    public void PercentIsClampedToZeroAndHundred()
    {
        Assert.Equal(100, new ProgressEvent("a", 150, "x").Percent);
        Assert.Equal(0, new ProgressEvent("a", -5, "x").Percent);
    }
}

public class CleanupServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mbx-clean-" + Guid.NewGuid().ToString("N"));

    public CleanupServiceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private string Make(string relative, int bytes, TimeSpan age)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);
        return path;
    }

    private MBExtreme.Core.Cleanup.CleanupCategory Category(TimeSpan? minAge) =>
        new("t", "Teste", "d", new[] { _root }, minAge);

    [Fact]
    public void ScanCountsFilesAndRespectsMinimumAge()
    {
        Make("old.tmp", 100, TimeSpan.FromDays(3));
        Make("sub/old2.tmp", 50, TimeSpan.FromDays(2));
        Make("new.tmp", 999, TimeSpan.FromMinutes(5));

        var item = MBExtreme.Core.Cleanup.CleanupService.Scan(Category(TimeSpan.FromDays(1)), DateTime.UtcNow);

        Assert.Equal(2, item.Files);
        Assert.Equal(150, item.Bytes);
    }

    [Fact]
    public void CleanDeletesOnlyOldFilesAndRemovesEmptyFolders()
    {
        string old = Make("sub/deep/old.tmp", 10, TimeSpan.FromDays(3));
        string recent = Make("keep.tmp", 10, TimeSpan.FromMinutes(1));

        var result = MBExtreme.Core.Cleanup.CleanupService.Clean(Category(TimeSpan.FromDays(1)), DateTime.UtcNow);

        Assert.Equal(1, result.FilesDeleted);
        Assert.Equal(10, result.FreedBytes);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
        Assert.False(Directory.Exists(Path.Combine(_root, "sub")));
        Assert.True(Directory.Exists(_root));
    }

    [Fact]
    public void MissingRootsAreIgnored()
    {
        var category = new MBExtreme.Core.Cleanup.CleanupCategory("x", "x", "x", new[] { Path.Combine(_root, "nao-existe") });
        var item = MBExtreme.Core.Cleanup.CleanupService.Scan(category, DateTime.UtcNow);
        Assert.Equal(0, item.Files);
    }

    [Fact]
    public void CatalogHasUniqueIdsAndNeverTouchesCookiesOrHistory()
    {
        var all = MBExtreme.Core.Cleanup.CleanupCatalog.Create();
        Assert.Equal(all.Count, all.Select(c => c.Id).Distinct().Count());
        foreach (var root in all.SelectMany(c => c.Roots))
        {
            Assert.DoesNotContain("Cookies", root, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("History", root, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Login Data", root, StringComparison.OrdinalIgnoreCase);
        }
    }
}

public class ParsingTests
{
    [Fact]
    public void WingetTableIsParsedByHeaderColumns()
    {
        const string output =
            "Nome            Id                  Versão   Disponível Fonte\n" +
            "-----------------------------------------------------------------\n" +
            "Discord         Discord.Discord     1.0.9    1.0.10     winget\n" +
            "7-Zip           7zip.7zip           23.01    24.08      winget\n" +
            "2 atualizações disponíveis.\n";

        var apps = MBExtreme.Core.Apps.WingetService.ParseUpgradeTable(output);

        Assert.Equal(2, apps.Count);
        Assert.Equal("Discord.Discord", apps[0].Id);
        Assert.Equal("1.0.10", apps[0].Available);
        Assert.Equal("7zip.7zip", apps[1].Id);
        Assert.Equal("23.01", apps[1].Version);
    }

    [Fact]
    public void WingetWithoutTableReturnsEmpty() =>
        Assert.Empty(MBExtreme.Core.Apps.WingetService.ParseUpgradeTable("Nenhuma atualização disponível."));

    [Theory]
    [InlineData("Valve.Steam", true)]
    [InlineData("7zip.7zip", true)]
    [InlineData("x; calc", false)]
    [InlineData("", false)]
    public void WingetIdsAreValidated(string id, bool valid) =>
        Assert.Equal(valid, MBExtreme.Core.Apps.WingetService.IsValidId(id));

    [Fact]
    public void WingetCatalogHasUniqueValidIds()
    {
        var catalog = MBExtreme.Core.Apps.WingetService.Catalog;
        Assert.Equal(catalog.Count, catalog.Select(c => c.Id).Distinct().Count());
        Assert.All(catalog, c => Assert.True(MBExtreme.Core.Apps.WingetService.IsValidId(c.Id)));
    }

    [Theory]
    [InlineData("32.0.15.6094", "560.94")]
    [InlineData("31.0.15.5123", "551.23")]
    [InlineData("30.0.14.7168", "471.68")]
    public void NvidiaVersionIsConvertedToTheMarketingNumber(string windows, string expected) =>
        Assert.Equal(expected, MBExtreme.Core.Drivers.DriverService.NvidiaDisplayVersion(windows));

    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_2684", MBExtreme.Core.Drivers.GpuVendor.Nvidia)]
    [InlineData(@"PCI\VEN_1002&DEV_744C", MBExtreme.Core.Drivers.GpuVendor.Amd)]
    [InlineData(@"PCI\VEN_8086&DEV_A780", MBExtreme.Core.Drivers.GpuVendor.Intel)]
    [InlineData(@"PCI\VEN_1234&DEV_0001", MBExtreme.Core.Drivers.GpuVendor.Other)]
    public void GpuVendorIsReadFromThePciId(string id, MBExtreme.Core.Drivers.GpuVendor expected) =>
        Assert.Equal(expected, MBExtreme.Core.Drivers.DriverService.VendorFromPnpId(id));

    [Fact]
    public void PowerPlansAreParsedAndActiveIsDetected()
    {
        const string output =
            "Existing Power Schemes (* Active)\n-----------------------------------\n" +
            "Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced) *\n" +
            "Power Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (High performance)\n";

        var plans = MBExtreme.Core.Windows.GameSettingsService.ParsePlans(output);

        Assert.Equal(2, plans.Count);
        Assert.True(plans[0].Active);
        Assert.Equal("Balanced", plans[0].Name);
        Assert.False(plans[1].Active);
    }

    [Fact]
    public void DeviceJsonAcceptsASingleObjectOrAnArray()
    {
        var one = MBExtreme.Core.Devices.DeviceService.ParseDevices("{\"InstanceId\":\"USB\\\\VID_1\",\"FriendlyName\":\"Pen\",\"Class\":\"USB\"}");
        var many = MBExtreme.Core.Devices.DeviceService.ParseDevices("[{\"InstanceId\":\"A\"},{\"InstanceId\":\"B\",\"FriendlyName\":\"Mouse\"}]");

        Assert.Single(one);
        Assert.Equal("Pen", one[0].Name);
        Assert.Equal(2, many.Count);
        Assert.Equal("A", many[0].Name);
        Assert.Empty(MBExtreme.Core.Devices.DeviceService.ParseDevices(""));
    }

    [Fact]
    public void PendingDriversWithoutAValidGuidAreIgnored()
    {
        const string json = "[{\"Id\":\"123e4567-e89b-12d3-a456-426614174000\",\"Title\":\"Driver A\"},{\"Id\":\"nao-e-guid\",\"Title\":\"B\"}]";
        var drivers = MBExtreme.Core.Drivers.DriverService.ParsePendingDrivers(json);
        Assert.Single(drivers);
        Assert.Equal("Driver A", drivers[0].Title);
    }
}
