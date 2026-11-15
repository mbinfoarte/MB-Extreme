using MBExtreme.Core.Gamepads;
using Xunit;

namespace MBExtreme.Core.Tests;

public class KbmPresetTests
{
    [Fact]
    public void CallOfDuty_UsesKnownKeysAndActions()
    {
        var actions = KbmMapper.Actions.Select(a => a.Id).ToHashSet();
        foreach (var (key, action) in KbmPresets.CallOfDuty.Bindings)
        {
            Assert.True(KbmMapper.Keys.ContainsKey(key), key);
            Assert.Contains(action, actions);
        }
    }

    [Fact]
    public void ApplyTo_CopiesAndMatches_KeepsToggleKey()
    {
        var s = new KbmSettings { ToggleKey = "F9" };
        Assert.False(KbmPresets.CallOfDuty.Matches(s));
        KbmPresets.CallOfDuty.ApplyTo(s);
        Assert.True(KbmPresets.CallOfDuty.Matches(s));
        Assert.Equal("F9", s.ToggleKey);
        Assert.Equal(VirtualTarget.Xbox360, s.Target);
        Assert.Equal(0, s.Acceleration);
        s.Bindings["W"] = "Cross";
        Assert.False(KbmPresets.CallOfDuty.Matches(s));
    }

    [Theory]
    [InlineData(@"C:\Games\Black Ops III\BlackOps3.exe", true)]
    [InlineData("t6sp", true)]
    [InlineData("cod.exe", false)]
    [InlineData("notepad", false)]
    public void ForGame_OnlyOfflineCampaigns(string exe, bool expected) => Assert.Equal(expected, KbmPresets.ForGame(exe) is not null);

    [Theory]
    [InlineData("cod.exe", true)]
    [InlineData(@"D:\Call of Duty\_retail_\cod24-cod.exe", true)]
    [InlineData("BlackOps3.exe", false)]
    public void IsProtected_RicochetGames(string exe, bool expected) => Assert.Equal(expected, KbmPresets.IsProtected(exe));

    [Fact]
    public void PadProfile_CallOfDuty_LinkedToSameGames()
    {
        var p = PadProfiles.ForGame(PadProfiles.Defaults, "iw4sp.exe");
        Assert.NotNull(p);
        Assert.Equal("Call of Duty (campanha)", p!.Name);
    }
}

public class PadGameProfileTests
{
    [Theory]
    [InlineData("cod.exe", "Call of Duty (2019 a 2026)")]
    [InlineData("BlackOps3.exe", "Call of Duty (campanha)")]
    [InlineData("GTA5_Enhanced.exe", "GTA V")]
    [InlineData("FarCry6.exe", "Far Cry")]
    [InlineData("re4.exe", "Resident Evil")]
    public void GameOpensItsProfile(string exe, string profile) => Assert.Equal(profile, PadProfiles.ForGame(PadProfiles.Defaults, exe)?.Name);

    [Fact]
    public void NoExeInTwoProfiles()
    {
        var all = PadProfiles.Defaults.SelectMany(p => p.Games.Select(g => g.ToLowerInvariant())).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
    }
}
