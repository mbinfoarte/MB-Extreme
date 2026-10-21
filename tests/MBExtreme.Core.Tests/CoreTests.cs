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
