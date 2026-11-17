using Microsoft.Win32;
using MBExtreme.Core.Apps;
using MBExtreme.Core.Tweaks;
using Xunit;

namespace MBExtreme.Core.Tests;

public class TweakCatalogTests
{
    private static IReadOnlyList<Tweak> Catalog()
    {
        var store = new TweakStore(Path.Combine(Path.GetTempPath(), $"mbx-{Guid.NewGuid():N}.json"));
        return TweakCatalog.Create(store);
    }

    [Fact]
    public void IdsAreUniqueAndTextsAreFilled()
    {
        if (!OperatingSystem.IsWindows()) return;
        var tweaks = Catalog();

        Assert.True(tweaks.Count >= 20);
        Assert.Equal(tweaks.Count, tweaks.Select(t => t.Id).Distinct().Count());
        Assert.All(tweaks, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Title));
            Assert.False(string.IsNullOrWhiteSpace(t.Description));
        });
    }

    [Fact]
    public void HighRiskTweaksAreNeverRecommendedAndHaveWarning()
    {
        if (!OperatingSystem.IsWindows()) return;
        var high = Catalog().Where(t => t.Risk == TweakRisk.High).ToList();

        Assert.NotEmpty(high);
        Assert.All(high, t =>
        {
            Assert.False(t.Recommended);
            Assert.False(string.IsNullOrWhiteSpace(t.Warning));
        });
    }

    [Fact]
    public void MediumRiskTweaksAreNeverRecommendedAndHaveWarning()
    {
        if (!OperatingSystem.IsWindows()) return;
        var medium = Catalog().Where(t => t.Risk == TweakRisk.Medium).ToList();

        Assert.All(medium, t =>
        {
            Assert.False(t.Recommended);
            Assert.False(string.IsNullOrWhiteSpace(t.Warning));
        });
    }
}

public class RegistryTweakTests : IDisposable
{
    private const string TestPath = @"Software\MBExtremeTests";
    private readonly string _storePath = Path.Combine(Path.GetTempPath(), $"mbx-{Guid.NewGuid():N}.json");

    public RegistryTweakTests()
    {
        if (OperatingSystem.IsWindows()) Registry.CurrentUser.DeleteSubKeyTree(TestPath, false);
    }

    public void Dispose()
    {
        if (OperatingSystem.IsWindows()) Registry.CurrentUser.DeleteSubKeyTree(TestPath, false);
        if (File.Exists(_storePath)) File.Delete(_storePath);
    }

    private RegistryTweak NewTweak(TweakStore store) =>
        new(store, "test", TweakCategory.Performance, TweakRisk.Low, "t", "d",
            new[]
            {
                new RegistryValueSpec(RegistryHive.CurrentUser, TestPath, "Number", 5),
                new RegistryValueSpec(RegistryHive.CurrentUser, TestPath, "Text", "on", RegistryValueKind.String),
            });

    [Fact]
    public void ApplyThenRevertRemovesValuesThatDidNotExist()
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = new TweakStore(_storePath);
        var tweak = NewTweak(store);

        Assert.False(tweak.IsApplied());
        Assert.False(tweak.HasBackup);

        tweak.Apply();
        Assert.True(tweak.IsApplied());
        Assert.True(tweak.HasBackup);

        tweak.Revert();
        Assert.False(tweak.IsApplied());
        Assert.False(tweak.HasBackup);
        using var key = Registry.CurrentUser.OpenSubKey(TestPath);
        Assert.Null(key?.GetValue("Number"));
    }

    [Fact]
    public void RevertRestoresTheOriginalValue()
    {
        if (!OperatingSystem.IsWindows()) return;
        using (var key = Registry.CurrentUser.CreateSubKey(TestPath))
        {
            key.SetValue("Number", 9, RegistryValueKind.DWord);
            key.SetValue("Text", "off", RegistryValueKind.String);
        }

        var store = new TweakStore(_storePath);
        var tweak = NewTweak(store);

        tweak.Apply();
        tweak.Apply(); // aplicar duas vezes não pode sobrescrever o backup
        Assert.True(tweak.IsApplied());

        tweak.Revert();
        using var after = Registry.CurrentUser.OpenSubKey(TestPath)!;
        Assert.Equal(9, after.GetValue("Number"));
        Assert.Equal("off", after.GetValue("Text"));
    }

    [Fact]
    public void BackupSurvivesReloadingTheStore()
    {
        if (!OperatingSystem.IsWindows()) return;
        NewTweak(new TweakStore(_storePath)).Apply();

        var reloaded = NewTweak(new TweakStore(_storePath));
        Assert.True(reloaded.HasBackup);
        reloaded.Revert();
        Assert.False(reloaded.IsApplied());
    }
}

public class BloatCatalogTests
{
    [Theory]
    [InlineData("*Microsoft.WindowsStore*", "Microsoft.WindowsStore", true)]
    [InlineData("*xbox*", "Microsoft.XboxGamingOverlay", true)]
    [InlineData("*xbox*", "Microsoft.Windows.Photos", false)]
    [InlineData("*Microsoft.People*", "microsoft.people", true)]
    [InlineData("*Copilot*", "Microsoft.Copilot", true)]
    public void WildcardMatching(string pattern, string package, bool expected) =>
        Assert.Equal(expected, BloatCatalog.Matches(pattern, package));

    [Fact]
    public void IdsAreUniqueAndDangerousAppsAreMarked()
    {
        var all = BloatCatalog.All;
        Assert.Equal(all.Count, all.Select(a => a.Id).Distinct().Count());
        Assert.Contains(all, a => a.Id == "store" && a.Level == AppRemovalLevel.Dangerous);
        Assert.Contains(all, a => a.Id == "xbox" && a.Level == AppRemovalLevel.Dangerous);
        Assert.All(all, a => Assert.False(string.IsNullOrWhiteSpace(a.Note)));
    }
}
