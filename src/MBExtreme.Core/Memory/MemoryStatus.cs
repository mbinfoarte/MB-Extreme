namespace MBExtreme.Core.Memory;

public enum MemoryLevel
{
    Good,
    NeedsCleaning,
    Critical,
}

/// <summary>Situação da memória física. Os limiares de <see cref="Level"/> são por porcentagem livre.</summary>
public readonly record struct MemoryStatus(long TotalBytes, long AvailableBytes)
{
    public long UsedBytes => TotalBytes - AvailableBytes;

    public double FreePercent => TotalBytes <= 0 ? 0 : 100.0 * AvailableBytes / TotalBytes;

    public MemoryLevel Level =>
        FreePercent >= 30 ? MemoryLevel.Good :
        FreePercent >= 15 ? MemoryLevel.NeedsCleaning :
        MemoryLevel.Critical;
}

public sealed record CleanResult(long FreedBytes, int ProcessesTrimmed, bool StandbyPurged, string? Error);
