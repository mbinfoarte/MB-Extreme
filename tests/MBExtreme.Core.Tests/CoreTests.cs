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

public class ToolsTests
{
    [Fact]
    public void PresentTrackerComputesFpsAndFrameTime()
    {
        var times = Enumerable.Range(0, 120).Select(i => i / 60.0).ToList(); // 2 s a 60 fps
        var reading = MBExtreme.Core.Fps.PresentTracker.Compute(10, times, times[^1]);

        Assert.NotNull(reading);
        Assert.InRange(reading!.Fps, 58, 62);
        Assert.InRange(reading.FrameTimeMs, 16, 17.5);
        Assert.InRange(reading.Low1PercentFps, 55, 62);
    }

    [Fact]
    public void PresentTrackerOneSlowFrameLowersOnePercent()
    {
        var times = new List<double>();
        double t = 0;
        for (int i = 0; i < 200; i++) { times.Add(t); t += i == 100 ? 0.2 : 1 / 100.0; }
        var reading = MBExtreme.Core.Fps.PresentTracker.Compute(1, times, times[^1]);

        Assert.True(reading!.Low1PercentFps < 20);
    }

    [Fact]
    public void AviWriterProducesValidHeaderAndIndex()
    {
        var stream = new MemoryStream();
        using (var writer = new MBExtreme.Core.Recording.MjpegAviWriter(stream, 64, 48, 15, leaveOpen: true))
        {
            writer.AddFrame(new byte[] { 1, 2, 3 });
            writer.AddFrame(new byte[] { 4, 5, 6, 7 });
            writer.AddFrame(new byte[] { 8 });
            writer.Finish(15);
        }

        var bytes = stream.ToArray();
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal(bytes.Length - 8, BitConverter.ToInt32(bytes, 4));
        Assert.Equal("AVI ", System.Text.Encoding.ASCII.GetString(bytes, 8, 4));

        string text = System.Text.Encoding.ASCII.GetString(bytes);
        int avih = text.IndexOf("avih", StringComparison.Ordinal);
        Assert.Equal(3, BitConverter.ToInt32(bytes, avih + 8 + 16)); // dwTotalFrames
        Assert.Equal(6, System.Text.RegularExpressions.Regex.Matches(text, "00dc").Count); // 3 quadros + 3 entradas no índice
        Assert.Contains("idx1", text);
    }

    [Fact]
    public void DisksAreParsedFromJson()
    {
        const string json =
            "[{\"Name\":\"Samsung SSD\",\"Media\":\"SSD\",\"Bus\":\"NVMe\",\"Health\":\"Healthy\",\"Size\":500107862016,\"Temp\":38,\"Wear\":3,\"Hours\":1200,\"ReadErrors\":0,\"WriteErrors\":null}," +
            "{\"Name\":\"HD\",\"Media\":\"HDD\",\"Bus\":\"SATA\",\"Health\":\"Warning\",\"Size\":1000000000000,\"Temp\":null,\"Wear\":null,\"Hours\":null,\"ReadErrors\":null,\"WriteErrors\":null}]";

        var disks = MBExtreme.Core.Storage.StorageService.ParseDisks(json);

        Assert.Equal(2, disks.Count);
        Assert.Equal(38, disks[0].Temperature);
        Assert.Null(disks[0].WriteErrors);
        Assert.Equal("Warning", disks[1].Health);
        Assert.Null(disks[1].Temperature);
    }

    [Theory]
    [InlineData("C:", true)]
    [InlineData("D:\\", true)]
    [InlineData("C:\\Windows", false)]
    [InlineData("C: & calc", false)]
    public void DriveNamesAreValidated(string name, bool valid) =>
        Assert.Equal(valid, MBExtreme.Core.Storage.StorageService.IsValidDrive(name));

    [Fact]
    public void HopStatsTracksLossAndLatency()
    {
        var hop = new MBExtreme.Core.Network.HopStats { Hop = 1 };
        hop.Add(10);
        hop.Add(null);
        hop.Add(30);

        Assert.Equal(3, hop.Sent);
        Assert.Equal(2, hop.Received);
        Assert.InRange(hop.LossPercent, 33, 34);
        Assert.Equal(20, hop.Average);
        Assert.Equal(10, hop.BestOrZero);
        Assert.Equal(30, hop.Worst);
    }

    [Theory]
    [InlineData("1.1.1.1", true)]
    [InlineData("exemplo.com.br", true)]
    [InlineData("", false)]
    [InlineData("a b; calc", false)]
    public void HostsAreValidated(string host, bool valid) =>
        Assert.Equal(valid, MBExtreme.Core.Network.NetAnalyzer.IsValidHost(host));

    [Fact]
    public void BenchmarkHistoryKeepsLastResults()
    {
        string path = Path.Combine(Path.GetTempPath(), "mbx-bench-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            for (int i = 0; i < 25; i++)
                MBExtreme.Core.Benchmark.BenchmarkHistory.Add(
                    new MBExtreme.Core.Benchmark.BenchmarkResult(DateTime.Now, 8, 1, 2, 3, 4, 5, i), path);

            var loaded = MBExtreme.Core.Benchmark.BenchmarkHistory.Load(path);
            Assert.Equal(20, loaded.Count);
            Assert.Equal(24, loaded[^1].Score);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void StressRunsAndStopsOnTime()
    {
        var options = new MBExtreme.Core.Stress.StressOptions(true, false, TimeSpan.FromSeconds(2), null, 64);
        var samples = 0;

        var result = MBExtreme.Core.Stress.StressService.Run(options, () => null, _ => samples++, CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal(0, result.Errors);
        Assert.True(samples >= 1);
    }

    [Fact]
    public void StressStopsWhenCancelled()
    {
        using var cts = new CancellationTokenSource(500);
        var options = new MBExtreme.Core.Stress.StressOptions(true, true, TimeSpan.FromMinutes(5), null, 64);

        var result = MBExtreme.Core.Stress.StressService.Run(options, () => null, _ => { }, cts.Token);

        Assert.False(result.Completed);
        Assert.Contains("Interrompido", result.Reason);
    }
}
