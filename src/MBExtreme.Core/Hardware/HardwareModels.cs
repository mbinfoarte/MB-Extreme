using MBExtreme.Core.Drivers;

namespace MBExtreme.Core.Hardware;

public sealed record CpuInfo(string Name, string Vendor, int Cores, int Threads, int MaxMhz, long L2Kb, long L3Kb, uint? Microcode)
{
    public bool IsAmd => Vendor.Contains("AMD", StringComparison.OrdinalIgnoreCase) || Name.Contains("AMD", StringComparison.OrdinalIgnoreCase);
    public bool IsIntel => Vendor.Contains("Intel", StringComparison.OrdinalIgnoreCase) || Name.Contains("Intel", StringComparison.OrdinalIgnoreCase);
}

public sealed record BoardInfo(string Manufacturer, string Product);

public sealed record SystemModel(string Manufacturer, string Model);

public sealed record BiosInfo(string Vendor, string Version, DateTime? ReleaseDate);

/// <summary>Um pente de memória. <see cref="RatedMhz"/> é o que o pente suporta; <see cref="ConfiguredMhz"/> é a velocidade em uso.</summary>
public sealed record MemoryModule(string Slot, long CapacityBytes, int RatedMhz, int ConfiguredMhz, string Type, string Manufacturer, string PartNumber);

public sealed record GpuSummary(string Name, long VramBytes, string DriverVersion, GpuVendor Vendor);

public sealed record WindowsInfo(string Edition, string DisplayVersion, int Build)
{
    public bool IsWindows11 => Build >= 22000;
}

public sealed record HardwareInventory(
    CpuInfo Cpu,
    BoardInfo Board,
    SystemModel System,
    BiosInfo Bios,
    IReadOnlyList<MemoryModule> Memory,
    IReadOnlyList<GpuSummary> Gpus,
    WindowsInfo Windows,
    bool IsLaptop,
    bool? AmdChipsetDriverInstalled,
    bool? ResizableBar,
    int RefreshRateHz)
{
    public long TotalMemoryBytes => Memory.Sum(m => m.CapacityBytes);
}

public enum CheckLevel { Ok, Info, Attention, Problem }

/// <summary>Resultado de uma verificação do hardware: o que foi visto e o que fazer.</summary>
public sealed record HardwareCheck(CheckLevel Level, string Title, string Detail, string? Action = null);
