using MBExtreme.Core.Benchmark;
using MBExtreme.Core.Drivers;
using MBExtreme.Core.Games;
using MBExtreme.Core.Hardware;
using MBExtreme.Core.Latency;
using MBExtreme.Core.Network;
using MBExtreme.Core.Progress;
using MBExtreme.Core.Recording;
using MBExtreme.Core.Sensors;
using MBExtreme.Core.Startup;
using MBExtreme.Core.Stress;
using MBExtreme.Core.Tweaks;
using Xunit;

namespace MBExtreme.Core.Tests;

internal static class Fakes
{
    public const long Gb = 1024L * 1024 * 1024;

    public static CpuInfo Cpu(string name, string vendor, uint? microcode = null, int cores = 8, int threads = 16) =>
        new(name, vendor, cores, threads, 5000, 1024, 32768, microcode);

    public static MemoryModule Ram(int rated, int configured, string type = "DDR5", long gb = 16) =>
        new("DIMM_A1", gb * Gb, rated, configured, type, "Kingston", "KF560C36");

    public static HardwareInventory Inventory(
        CpuInfo? cpu = null, IReadOnlyList<MemoryModule>? memory = null, int build = 26100, bool laptop = false,
        DateTime? biosDate = null, bool? chipset = true, bool? rebar = null, IReadOnlyList<GpuSummary>? gpus = null,
        string boardMaker = "ASUSTeK COMPUTER INC.", string board = "ROG STRIX B650-A", string sysMaker = "System manufacturer", string sysModel = "System Product Name") =>
        new(cpu ?? Cpu("AMD Ryzen 7 7800X3D 8-Core Processor", "AuthenticAMD"),
            new BoardInfo(boardMaker, board),
            new SystemModel(sysMaker, sysModel),
            new BiosInfo("American Megatrends", "1805", biosDate ?? new DateTime(2026, 6, 1)),
            memory ?? new[] { Ram(6000, 6000), Ram(6000, 6000) },
            gpus ?? new[] { new GpuSummary("NVIDIA GeForce RTX 3060 Ti", 8 * Gb, "32.0.15.6094", GpuVendor.Nvidia) },
            new WindowsInfo("Windows 11 Pro", "24H2", build),
            laptop, chipset, rebar, 144);
}

public class HardwareAdvisorTests
{
    private static readonly DateTime Now = new(2026, 10, 21);

    [Fact]
    public void MemoryBelowRatedSpeedTellsAmdUsersToEnableExpo()
    {
        var hw = Fakes.Inventory(memory: new[] { Fakes.Ram(6000, 4800), Fakes.Ram(6000, 4800) });
        var check = HardwareAdvisor.Evaluate(hw, Now).Single(c => c.Title.Contains("abaixo da velocidade"));

        Assert.Equal(CheckLevel.Attention, check.Level);
        Assert.Contains("EXPO", check.Action);
        Assert.Contains("4800", check.Detail);
    }

    [Fact]
    public void MemoryBelowRatedSpeedTellsIntelUsersToEnableXmp()
    {
        var hw = Fakes.Inventory(cpu: Fakes.Cpu("12th Gen Intel(R) Core(TM) i5-12600K", "GenuineIntel"),
            memory: new[] { Fakes.Ram(3600, 2133, "DDR4"), Fakes.Ram(3600, 2133, "DDR4") });
        var check = HardwareAdvisor.Evaluate(hw, Now).Single(c => c.Title.Contains("abaixo da velocidade"));

        Assert.Contains("XMP", check.Action);
    }

    [Fact]
    public void MemoryAtRatedSpeedIsOk() =>
        Assert.Contains(HardwareAdvisor.Evaluate(Fakes.Inventory(), Now), c => c.Title == "Velocidade da memória" && c.Level == CheckLevel.Ok);

    [Fact]
    public void LaptopMemorySpeedIsOnlyInformation()
    {
        var hw = Fakes.Inventory(memory: new[] { Fakes.Ram(5600, 4800) }, laptop: true);
        var checks = HardwareAdvisor.Evaluate(hw, Now);

        Assert.DoesNotContain(checks, c => c.Level == CheckLevel.Attention && c.Title.Contains("Memória"));
    }

    [Fact]
    public void SingleModuleOnDesktopIsAnAttention()
    {
        var hw = Fakes.Inventory(memory: new[] { Fakes.Ram(6000, 6000) });
        Assert.Contains(HardwareAdvisor.Evaluate(hw, Now), c => c.Title == "Memória em canal único" && c.Level == CheckLevel.Attention);
    }

    [Fact]
    public void LowRamIsFlagged()
    {
        var hw = Fakes.Inventory(memory: new[] { Fakes.Ram(2666, 2666, "DDR4", 4) });
        Assert.Contains(HardwareAdvisor.Evaluate(hw, Now), c => c.Title == "Pouca memória RAM");
    }

    [Theory]
    [InlineData(22631, true)]
    [InlineData(26100, false)]
    public void RyzenOnOlderWindows11IsToldToUpdateTo24H2(int build, bool warns)
    {
        var checks = HardwareAdvisor.Evaluate(Fakes.Inventory(build: build), Now);
        Assert.Equal(warns, checks.Any(c => c.Title.Contains("anterior ao 24H2")));
    }

    [Fact]
    public void Windows10IsFlaggedAsUnsupported() =>
        Assert.Contains(HardwareAdvisor.Evaluate(Fakes.Inventory(build: 19045), Now), c => c.Title == "Windows 10");

    [Theory]
    [InlineData(40, CheckLevel.Attention)]
    [InlineData(20, CheckLevel.Info)]
    [InlineData(5, CheckLevel.Ok)]
    public void BiosAgeLevels(int months, CheckLevel expected)
    {
        var hw = Fakes.Inventory(biosDate: Now.AddDays(-months * 30.5));
        var check = HardwareAdvisor.Evaluate(hw, Now).Single(c => c.Title.StartsWith("BIOS"));
        Assert.Equal(expected, check.Level);
    }

    [Theory]
    [InlineData(0x125u, CheckLevel.Problem)]
    [InlineData(0x12Bu, CheckLevel.Info)]
    [InlineData(0x12Fu, CheckLevel.Ok)]
    public void IntelRaptorLakeMicrocodeLevels(uint microcode, CheckLevel expected)
    {
        var hw = Fakes.Inventory(cpu: Fakes.Cpu("13th Gen Intel(R) Core(TM) i9-13900K", "GenuineIntel", microcode));
        var check = HardwareAdvisor.Evaluate(hw, Now).Single(c => c.Title.Contains("icrocódigo") || c.Title.Contains("BIOS sem a correção"));
        Assert.Equal(expected, check.Level);
    }

    [Fact]
    public void MobileRaptorLakeIsNotChecked()
    {
        var hw = Fakes.Inventory(cpu: Fakes.Cpu("13th Gen Intel(R) Core(TM) i7-13700H", "GenuineIntel", 0x100));
        Assert.DoesNotContain(HardwareAdvisor.Evaluate(hw, Now), c => c.Title.Contains("icrocódigo") || c.Title.Contains("sem a correção"));
        Assert.False(HardwareAdvisor.IsIntel13Or14GenDesktop(hw.Cpu));
        Assert.True(HardwareAdvisor.IsIntel13Or14GenDesktop(Fakes.Cpu("Intel(R) Core(TM) i7-14700KF", "GenuineIntel")));
    }

    [Fact]
    public void MissingAmdChipsetDriverIsFlagged() =>
        Assert.Contains(HardwareAdvisor.Evaluate(Fakes.Inventory(chipset: false), Now), c => c.Title.Contains("chipset AMD") && c.Level == CheckLevel.Attention);

    [Fact]
    public void ResizableBarOffIsOnlyInformation()
    {
        var check = HardwareAdvisor.Evaluate(Fakes.Inventory(rebar: false), Now).Single(c => c.Title.StartsWith("Resizable BAR"));
        Assert.Equal(CheckLevel.Info, check.Level);
    }

    [Fact]
    public void MicrocodeIsReadFromRegistryBytes()
    {
        var bytes = new byte[8];
        BitConverter.GetBytes(0x12Bu).CopyTo(bytes, 4);
        Assert.Equal(0x12Bu, HardwareAdvisor.ParseMicrocode(bytes));
        Assert.Null(HardwareAdvisor.ParseMicrocode(null));
        Assert.Null(HardwareAdvisor.ParseMicrocode(new byte[2]));
    }

    [Fact]
    public void ResizableBarIsReadFromNvidiaSmi()
    {
        const string on = "FB Memory Usage\n    Total : 8192 MiB\n    Used : 500 MiB\nBAR1 Memory Usage\n    Total : 8192 MiB\n    Used : 5 MiB";
        const string off = "FB Memory Usage\n    Total : 8192 MiB\nBAR1 Memory Usage\n    Total : 256 MiB";
        Assert.True(HardwareAdvisor.ParseResizableBar(on));
        Assert.False(HardwareAdvisor.ParseResizableBar(off));
        Assert.Null(HardwareAdvisor.ParseResizableBar("sem dados"));
    }

    [Theory]
    [InlineData("AMD Ryzen 5 3600 6-Core Processor", 2)]
    [InlineData("AMD Ryzen 5 5600X 6-Core Processor", 3)]
    [InlineData("AMD Ryzen 7 7800X3D 8-Core Processor", 4)]
    [InlineData("AMD Ryzen 9 PRO 7945", 4)]
    [InlineData("AMD Ryzen 7 9800X3D 8-Core Processor", 5)]
    [InlineData("AMD Ryzen AI 9 HX 370", 5)]
    [InlineData("Intel(R) Core(TM) i7-12700K", 0)]
    public void RyzenGenerationIsDetected(string name, int generation) =>
        Assert.Equal(generation, HardwareAdvisor.RyzenGeneration(name));

    [Theory]
    [InlineData(26, "DDR4")]
    [InlineData(34, "DDR5")]
    [InlineData(35, "LPDDR5")]
    [InlineData(0, "")]
    public void MemoryTypeNames(int code, string name) => Assert.Equal(name, HardwareAdvisor.MemoryTypeName(code));
}

public class BiosGuideTests
{
    [Fact]
    public void AsusBoardSearchIsRestrictedToAsusSite()
    {
        var link = BiosGuide.SupportSearch(Fakes.Inventory());
        Assert.Equal("ASUS", link.Vendor);
        Assert.Contains("site%3Aasus.com", link.Url);
        Assert.Contains("ROG", Uri.UnescapeDataString(link.Url));
    }

    [Fact]
    public void BrandedPcUsesTheSystemModel()
    {
        var hw = Fakes.Inventory(boardMaker: "Dell Inc.", board: "0X1234", sysMaker: "Dell Inc.", sysModel: "Inspiron 5510");
        var link = BiosGuide.SupportSearch(hw);

        Assert.Equal("Dell", link.Vendor);
        Assert.Contains("Inspiron", Uri.UnescapeDataString(link.Url));
        Assert.Contains("dell.com", link.Url);
    }

    [Fact]
    public void UnknownVendorFallsBackToAGenericSearch()
    {
        var hw = Fakes.Inventory(boardMaker: "Fabricante Raro", board: "X1", sysMaker: "", sysModel: "");
        Assert.StartsWith("https://www.google.com/search?q=", BiosGuide.SupportSearch(hw).Url);
    }

    [Fact]
    public void RyzenRecipeHasCurveOptimizerAndExpo()
    {
        string text = BiosGuide.RecipeToText(Fakes.Inventory());

        Assert.Contains("Curve Optimizer", text);
        Assert.Contains("EXPO", text);
        Assert.Contains("−10", text);
        Assert.Contains("X3D", text);
        Assert.Contains("Load Optimized Defaults", text);
    }

    [Fact]
    public void RaptorLakeRecipeUsesIntelDefaultSettings()
    {
        var hw = Fakes.Inventory(cpu: Fakes.Cpu("13th Gen Intel(R) Core(TM) i9-13900K", "GenuineIntel", 0x125));
        string text = BiosGuide.RecipeToText(hw);

        Assert.Contains("Intel Default Settings", text);
        Assert.Contains("0x12B", text);
        Assert.DoesNotContain("Curve Optimizer", text);
        Assert.Contains("XMP", text);
    }

    [Fact]
    public void RecipeMentionsSingleModule()
    {
        var hw = Fakes.Inventory(memory: new[] { Fakes.Ram(6000, 6000) });
        Assert.Contains("1 pente", BiosGuide.RecipeToText(hw));
    }

    [Fact]
    public void ChecklistWarnsAgainstAutomaticUpdates() =>
        Assert.Contains(BiosGuide.UpdateChecklist(), l => l.Contains("não atualiza BIOS sozinho"));
}

public class TweakExtensionTests
{
    [Fact]
    public void PowerIndexesAreParsedInEnglishAndPortuguese()
    {
        const string en = "  Power Setting GUID: 893dee8e\n    Minimum Possible Setting: 0x00000000\n    Maximum Possible Setting: 0x00000064\n    Current AC Power Setting Index: 0x00000064\n    Current DC Power Setting Index: 0x00000005\n";
        const string pt = "  GUID da Configuração de Energia: 893dee8e\n    Configuração Mínima Possível: 0x00000000\n    Índice da Configuração de Energia CA Atual: 0x00000032\n    Índice da Configuração de Energia CC Atual: 0x00000000\n";

        Assert.Equal((100, 5), PowerSettingTweak.ParseIndexes(en));
        Assert.Equal((50, 0), PowerSettingTweak.ParseIndexes(pt));
        Assert.Equal((null, null), PowerSettingTweak.ParseIndexes("nada"));
    }

    [Fact]
    public void CatalogHasVisualAndPowerTweaksWithSafeRisk()
    {
        var all = TweakCatalog.Create(new TweakStore(Path.Combine(Path.GetTempPath(), "mbx-" + Guid.NewGuid().ToString("N") + ".json")));

        Assert.Contains(all, t => t.Category == TweakCategory.Visual);
        Assert.Contains(all, t => t.Id == "power-pcie-aspm");
        Assert.Equal(all.Count, all.Select(t => t.Id).Distinct().Count());
        Assert.All(all.Where(t => t.Risk != TweakRisk.Low), t => Assert.False(t.Recommended));
        Assert.Equal(TweakRisk.High, all.Single(t => t.Id == "perf-vbs-off").Risk);
    }

    [Fact]
    public void ProfilesOnlyContainLowRiskTweaks()
    {
        var all = TweakCatalog.Create(new TweakStore(Path.Combine(Path.GetTempPath(), "mbx-" + Guid.NewGuid().ToString("N") + ".json")));
        var profiles = TweakProfiles.Build(all);

        Assert.Equal(3, profiles.Count);
        Assert.All(profiles, p =>
        {
            Assert.NotEmpty(p.Tweaks);
            Assert.All(p.Tweaks, t => Assert.Equal(TweakRisk.Low, t.Risk));
        });

        var game = profiles.Single(p => p.Name == "Jogo").Tweaks.Select(t => t.Id).ToHashSet();
        var competitive = profiles.Single(p => p.Name.StartsWith("Competitivo")).Tweaks.Select(t => t.Id).ToHashSet();
        Assert.True(game.IsSubsetOf(competitive));
        Assert.Contains("power-min-cpu", competitive);
        Assert.Contains(profiles.Single(p => p.Name == "PC fraco").Tweaks, t => t.Category == TweakCategory.Visual);
    }
}

public class StartupTests
{
    [Theory]
    [InlineData(new byte[] { 0x02, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 0x06, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 0x03, 0, 0, 0 }, false)]
    [InlineData(new byte[] { 0x07, 0, 0, 0 }, false)]
    public void StartupApprovedBytesAreDecoded(byte[] data, bool enabled) => Assert.Equal(enabled, StartupService.IsEnabledBytes(data));

    [Fact]
    public void MissingValueMeansEnabled() => Assert.True(StartupService.IsEnabledBytes(null));

    [Fact]
    public void EncodedStateRoundTrips()
    {
        var off = StartupService.EncodeState(false);
        var on = StartupService.EncodeState(true);

        Assert.Equal(12, off.Length);
        Assert.False(StartupService.IsEnabledBytes(off));
        Assert.True(StartupService.IsEnabledBytes(on));
        Assert.NotEqual(0, BitConverter.ToInt64(off, 4));
    }
}

public class BenchmarkExtensionTests
{
    private static BenchmarkResult Result(int score, double single, double latency = 80) =>
        new(DateTime.Now, 8, single, single * 7, 20, 2000, 3000, score, MemoryLatencyNs: latency);

    [Fact]
    public void SmallDifferencesAreNoise()
    {
        var deltas = BenchmarkComparison.Compare(Result(10000, 100), Result(10200, 101));
        Assert.All(deltas, d => Assert.Equal(DeltaVerdict.Same, d.Verdict));
        Assert.Contains("Sem diferença", BenchmarkComparison.Summary(deltas));
    }

    [Fact]
    public void ImprovementAndRegressionAreSeparated()
    {
        var before = Result(10000, 100, latency: 80);
        var after = Result(11500, 115, latency: 100);
        var deltas = BenchmarkComparison.Compare(before, after);

        Assert.Equal(DeltaVerdict.Better, deltas.Single(d => d.Name == "Pontuação geral").Verdict);
        Assert.Equal(DeltaVerdict.Worse, deltas.Single(d => d.Name == "Memória: latência").Verdict);
        Assert.Contains("Melhorou", BenchmarkComparison.Summary(deltas));
        Assert.Contains("piorou", BenchmarkComparison.Summary(deltas));
    }

    [Fact]
    public void MissingMetricsAreSkipped()
    {
        var deltas = BenchmarkComparison.Compare(Result(1000, 100), Result(1100, 110));
        Assert.DoesNotContain(deltas, d => d.Name == "GPU 3D");
    }

    [Fact]
    public void QuickBenchmarkMeasuresEverythingWithoutDisk()
    {
        var events = new List<ProgressEvent>();
        IProgress<ProgressEvent> progress = new SyncProgress(events.Add);

        var result = BenchmarkService.Run(progress, CancellationToken.None, new BenchmarkOptions(Disk: false, Quick: true, Label: "teste"));

        Assert.Equal("teste", result.Label);
        Assert.True(result.CpuSingleMops > 0);
        Assert.True(result.CpuMultiMops > 0);
        Assert.True(result.CpuSimdGflops > 0);
        Assert.True(result.CryptoMBps > 0);
        Assert.True(result.CompressMBps > 0);
        Assert.True(result.MemoryReadGBps > 0 && result.MemoryWriteGBps > 0 && result.MemoryGBps > 0);
        Assert.True(result.MemoryLatencyNs > 0);
        Assert.Equal(4, result.LatencyCurve!.Count);
        Assert.Equal(0, result.DiskWriteMBps);
        Assert.True(result.Score > 0);
        Assert.All(events, e => Assert.Contains(e.Step, BenchmarkService.StepsFor(new BenchmarkOptions(Disk: false, Quick: true)).Append(BenchmarkService.StepDisk)));
    }

    [Fact]
    public void LatencyGrowsWithWorkingSetSize()
    {
        var curve = BenchmarkService.MemoryLatencyCurve(quick: true, CancellationToken.None);
        Assert.True(curve[^1].Nanoseconds > curve[0].Nanoseconds);
    }

    private sealed class SyncProgress : IProgress<ProgressEvent>
    {
        private readonly Action<ProgressEvent> _action;
        public SyncProgress(Action<ProgressEvent> action) => _action = action;
        public void Report(ProgressEvent value) => _action(value);
    }
}

public class StressExtensionTests
{
    [Fact]
    public void SmtCpuTestsOneThreadPerPhysicalCore()
    {
        var cores = PerCoreStress.CoresToTest(16, 8, physicalOnly: true);
        Assert.Equal(new[] { 0, 2, 4, 6, 8, 10, 12, 14 }, cores);
    }

    [Fact]
    public void CpuWithoutSmtTestsEveryCore() =>
        Assert.Equal(Enumerable.Range(0, 6).ToList(), PerCoreStress.CoresToTest(6, 6, physicalOnly: true));

    [Fact]
    public void LogicalModeTestsEveryThread() =>
        Assert.Equal(16, PerCoreStress.CoresToTest(16, 8, physicalOnly: false).Count);

    [Fact]
    public void VectorKernelIsDeterministic() =>
        Assert.Equal(PerCoreStress.VectorUnit(), PerCoreStress.VectorUnit());

    [Fact]
    public void PerCoreRunReportsEachCoreAndNoErrors()
    {
        var options = new PerCoreOptions(1, null, true);
        var progress = new List<PerCoreProgress>();

        var results = PerCoreStress.Run(options, new[] { 0, 1 }, () => null, progress.Add, CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => { Assert.True(r.Completed); Assert.Equal(0, r.Errors); });
        Assert.NotEmpty(progress);
    }
}

public class LatencyTests
{
    [Fact]
    public void RoutinesAreMappedToTheDriverThatOwnsTheAddress()
    {
        var agg = new DpcIsrAggregator();
        agg.AddImage(0xFFFFF80000000000, 0x100000, @"\SystemRoot\System32\drivers\ndis.sys");
        agg.AddImage(0xFFFFF80010000000, 0x200000, @"\SystemRoot\System32\drivers\nvlddmkm.sys");
        agg.AddEvent("DPC", 0xFFFFF80000001000, 200);
        agg.AddEvent("DPC", 0xFFFFF80000002000, 1500);
        agg.AddEvent("ISR", 0xFFFFF80010000500, 80);
        agg.AddEvent("DPC", 0xFFFFF80099999999, 30);

        var top = agg.Top(5);

        Assert.Equal("ndis.sys", top[0].Module);
        Assert.Equal(1500, top[0].MaxMicroseconds);
        Assert.Equal(2, top[0].Count);
        Assert.Equal(850, top[0].AverageMicroseconds);
        Assert.Contains(top, d => d.Module == "nvlddmkm.sys" && d.Kind == "ISR");
        Assert.Contains(top, d => d.Module == "desconhecido");
        Assert.Equal(4, agg.TotalEvents);
    }

    [Theory]
    [InlineData(100, "ótimo")]
    [InlineData(700, "bom")]
    [InlineData(1500, "atenção")]
    [InlineData(5000, "ruim")]
    public void Verdicts(double microseconds, string verdict) => Assert.Equal(verdict, DpcIsrAggregator.Verdict(microseconds));

    [Theory]
    [InlineData("nvlddmkm.sys", "vídeo")]
    [InlineData("ndis.sys", "Rede")]
    [InlineData("HDAudBus.sys", "Áudio")]
    [InlineData("stornvme.sys", "Armazenamento")]
    [InlineData("USBXHCI.sys", "USB")]
    [InlineData("ACPI.sys", "BIOS")]
    public void AdviceMatchesTheDriverType(string module, string keyword) => Assert.Contains(keyword, DpcIsrAggregator.Advice(module));
}

public class GameTests
{
    [Theory]
    [InlineData(1, 16, 0)]
    [InlineData(4, 16, 1)]
    [InlineData(8, 16, 2)]
    [InlineData(12, 32, 3)]
    [InlineData(24, 32, 4)]
    [InlineData(8, 4, 1)]
    public void TierFollowsVideoMemoryAndRam(long vramGb, long ramGb, int tier)
    {
        var gpu = new GpuSummary("NVIDIA GeForce RTX 3060 Ti", vramGb * Fakes.Gb, "1", GpuVendor.Nvidia);
        var hw = Fakes.Inventory(gpus: new[] { gpu }, memory: new[] { Fakes.Ram(3200, 3200, "DDR4", ramGb) });
        Assert.Equal(tier, GameAdvisor.Tier(hw));
    }

    [Fact]
    public void IntegratedGraphicsAreTierZero()
    {
        var gpu = new GpuSummary("Intel(R) UHD Graphics 770", 2 * Fakes.Gb, "1", GpuVendor.Intel);
        Assert.Equal(0, GameAdvisor.Tier(Fakes.Inventory(gpus: new[] { gpu })));
    }

    [Fact]
    public void AdviceMentionsUpscalerFrameCapAndReflex()
    {
        var advice = GameAdvisor.Recommend(Fakes.Inventory());
        string text = string.Join("\n", advice.Lines);

        Assert.Contains("DLSS", text);
        Assert.Contains("141", text); // 144 Hz - 3
        Assert.Contains("Reflex", text);
        Assert.Equal("Intermediário", advice.Tier);
    }

    [Fact]
    public void AmdGpuGetsFsrAndAntiLag()
    {
        var gpu = new GpuSummary("AMD Radeon RX 7800 XT", 16 * Fakes.Gb, "1", GpuVendor.Amd);
        string text = string.Join("\n", GameAdvisor.Recommend(Fakes.Inventory(gpus: new[] { gpu })).Lines);

        Assert.Contains("FSR", text);
        Assert.Contains("Anti-Lag", text);
    }

    [Fact]
    public void BoostSuggestionsContainCommonBackgroundApps() =>
        Assert.Contains("OneDrive", GameBoostService.SuggestedCloseApps);
}

public class NetworkExtensionTests
{
    [Fact]
    public void JitterIsTheAverageChangeBetweenReplies()
    {
        var hop = new HopStats { Hop = 1 };
        foreach (double ms in new double[] { 20, 30, 20, 40 }) hop.Add(ms);
        hop.Add(null);

        Assert.Equal((10 + 10 + 20) / 3.0, hop.Jitter, 3);
    }

    [Fact]
    public void WifiIsParsedInEnglish()
    {
        const string output = "    Name                   : Wi-Fi\n    State                  : connected\n    SSID                   : Casa 5G\n    BSSID                  : aa:bb:cc:dd:ee:ff\n    Radio type             : 802.11ax\n    Channel                : 44\n    Receive rate (Mbps)    : 1201\n    Transmit rate (Mbps)   : 1021\n    Signal                 : 87%\n";
        var status = WifiService.Parse(output)!;

        Assert.True(status.Connected);
        Assert.Equal("Casa 5G", status.Ssid);
        Assert.Equal(87, status.SignalPercent);
        Assert.Equal(44, status.Channel);
        Assert.Equal("5 GHz", status.Band);
        Assert.Equal(1201, status.ReceiveMbps);
    }

    [Fact]
    public void WifiIsParsedInPortugueseAndAdvisesOnWeakSignal()
    {
        const string output = "    Nome                   : Wi-Fi\n    Estado                 : conectado\n    SSID                   : Apto 12\n    Tipo de rádio          : 802.11n\n    Canal                  : 6\n    Taxa de recepção (Mbps): 72,2\n    Taxa de transmissão (Mbps): 65\n    Sinal                  : 42%\n";
        var status = WifiService.Parse(output)!;

        Assert.True(status.Connected);
        Assert.Equal(42, status.SignalPercent);
        Assert.Equal("2,4 GHz", status.Band);
        var tips = WifiService.Advice(status);
        Assert.Contains(tips, t => t.Contains("Sinal fraco"));
        Assert.Contains(tips, t => t.Contains("2,4 GHz"));
    }

    [Fact]
    public void PortugueseNetworkTypeIsNotTheRadioType()
    {
        const string output = "    Nome                   : Wi-Fi\n    Estado                 : conectado\n    SSID                   : Casa\n    Tipo de rede           : Infraestrutura\n    Tipo de rádio          : 802.11ac\n    Canal                  : 36\n    Sinal                  : 90%\n";
        Assert.Equal("802.11ac", WifiService.Parse(output)!.RadioType);
    }

    [Fact]
    public void MissingStateMeansNoWifiInterface() =>
        Assert.Null(WifiService.Parse("A solicitação requer permissão de localização para acessar informações de Wi-Fi.\n"));

    [Fact]
    public void DisconnectedWifiRecommendsCable()
    {
        var status = WifiService.Parse("    State : disconnected\n    SSID :\n")!;
        Assert.False(status.Connected);
        Assert.Contains(WifiService.Advice(status), t => t.Contains("cabo"));
    }
}

public class RecordingExtensionTests
{
    private static readonly EncoderOption Nvenc = new(VideoCodec.H264, EncoderKind.Nvenc, "h264_nvenc");

    [Fact]
    public void EncodersAreDetectedFromFfmpegOutput()
    {
        const string output = " V....D h264_nvenc           NVIDIA NVENC H.264 encoder\n V....D hevc_nvenc           NVIDIA NVENC hevc encoder\n V....D av1_amf              AMD AMF AV1 encoder\n V....D libx264              x264";
        var list = FfmpegCommand.ParseEncoders(output);

        Assert.Equal(new[] { "h264_nvenc", "hevc_nvenc", "av1_amf" }, list.Select(e => e.FfmpegName));
        Assert.Equal("AV1 · AMD AMF", list[2].Title);
    }

    [Fact]
    public void NativeSizeKeepsFramesOnTheGpu()
    {
        var args = FfmpegCommand.Build(new CaptureRequest(2560, 1440, 2560, 1440, 60, Nvenc, 45, @"C:\v\a.mkv"));

        string source = args[args.ToList().IndexOf("-i") + 1];
        Assert.Contains("ddagrab", source);
        Assert.Contains("framerate=60", source);
        Assert.DoesNotContain("hwdownload", source);
        Assert.Contains("h264_nvenc", args);
        Assert.Equal(@"C:\v\a.mkv", args[^1]);
        Assert.Equal("120", args[args.ToList().IndexOf("-g") + 1]);
    }

    [Fact]
    public void DifferentSizeScalesAfterDownload()
    {
        var args = FfmpegCommand.Build(new CaptureRequest(3840, 2160, 2560, 1440, 30, Nvenc, 50, "x.mkv"));
        Assert.Contains("hwdownload,format=bgra,scale=3840:2160", args[args.ToList().IndexOf("-i") + 1]);
    }

    [Fact]
    public void QuickSyncAlwaysUsesTheCpuPath()
    {
        var qsv = new EncoderOption(VideoCodec.Hevc, EncoderKind.Qsv, "hevc_qsv");
        var args = FfmpegCommand.Build(new CaptureRequest(1920, 1080, 1920, 1080, 60, qsv, 20, "x.mp4"));
        Assert.Contains("hwdownload", args[args.ToList().IndexOf("-i") + 1]);
    }

    [Theory]
    [InlineData(1920, 1080, 30, 16)]
    [InlineData(1920, 1080, 60, 24)]
    [InlineData(2560, 1440, 60, 45)]
    [InlineData(3840, 2160, 60, 75)]
    [InlineData(1280, 720, 30, 8)]
    public void BitrateGrowsWithResolutionAndFrameRate(int w, int h, int fps, int mbps) =>
        Assert.Equal(mbps, FfmpegCommand.DefaultBitrateMbps(w, h, fps));

    [Fact]
    public void ContainerExtension()
    {
        Assert.Equal(".mkv", FfmpegCommand.ExtensionFor(false));
        Assert.Equal(".mp4", FfmpegCommand.ExtensionFor(true));
    }
}

public class SensorFormatTests
{
    [Theory]
    [InlineData("Clock", 4950.0, "4,95 GHz")]
    [InlineData("Clock", 800.0, "800 MHz")]
    [InlineData("Temperature", 65.5, "65,5 °C")]
    [InlineData("Voltage", 1.2345, "1,235 V")]
    [InlineData("Load", 87.0, "87 %")]
    [InlineData("Fan", 1200.0, "1200 RPM")]
    public void ValuesAreFormattedWithUnits(string kind, double value, string expected)
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");
        try { Assert.Equal(expected, HardwareMonitorService.Format(kind, value)); }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void MissingValueShowsDash() => Assert.Equal("—", HardwareMonitorService.Format("Clock", null));

    [Fact]
    public void CsvHasHeaderAndRows()
    {
        var groups = new[] { new SensorGroup("AMD Ryzen 7", "Cpu", new[] { new SensorValue("/cpu/0/clock/1", "CPU Core #1", "Clock", 4900, 800, 5100) }) };
        string csv = HardwareMonitorService.ToCsv(groups);

        Assert.StartsWith("Componente;Categoria;Sensor", csv);
        Assert.Contains("CPU Core #1", csv);
    }
}

public class AppIdentityTests
{
    [Fact]
    public void ProductIsCalledBeta()
    {
        Assert.Equal("MB Extreme Beta", AppInfo.Name);
        Assert.Contains("Beta", AppInfo.DisplayVersion);
    }
}
