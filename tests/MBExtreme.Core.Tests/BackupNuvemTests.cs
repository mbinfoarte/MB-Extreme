using MBExtreme.Core.Settings;
using Xunit;

namespace MBExtreme.Core.Tests;

public class BackupNuvemTests
{
    [Theory]
    [InlineData("PexelsApiKey", false)]
    [InlineData("StudioYtSecretProtected", false)]
    [InlineData("RemoteProtectedSecret", false)]
    [InlineData("LicensePublicKey", false)]
    [InlineData("WidgetX", false)]
    [InlineData("RatingDone", false)]
    [InlineData("PadProfiles", true)]
    [InlineData("OverlayItems", true)]
    [InlineData("GameProcesses", true)]
    [InlineData("PadAutoSwitch", true)]
    public void So_vai_o_que_pode(string name, bool allowed) => Assert.Equal(allowed, CloudBackup.Allowed(name));

    [Fact]
    public void Backup_nao_leva_segredo_e_volta_as_escolhas()
    {
        var folder = Path.Combine(Path.GetTempPath(), "mbx-bk-" + Guid.NewGuid().ToString("N"));
        var s = new AppSettings { PexelsApiKey = "segredo-123", OverlayScale = 140, WidgetX = 77 };
        s.PadProfiles.Add(new MBExtreme.Core.Gamepads.PadProfile { Name = "Meu FC", Deadzone = 11 });
        string json = CloudBackup.Build(s, folder, "0.9.0");
        Assert.DoesNotContain("segredo-123", json);
        Assert.DoesNotContain("WidgetX", json);

        var other = new AppSettings { PexelsApiKey = "do-outro-pc", WidgetX = 5 };
        var restored = CloudBackup.Restore(json, other, folder);
        Assert.Equal(140, restored.OverlayScale);
        Assert.Equal("Meu FC", Assert.Single(restored.PadProfiles).Name);
        Assert.Equal("do-outro-pc", restored.PexelsApiKey);   // segredo deste PC fica
        Assert.Equal(5, restored.WidgetX);                    // posição deste PC fica
    }

    [Fact]
    public void Backup_de_versao_nova_e_recusado() =>
        Assert.Throws<InvalidDataException>(() => CloudBackup.Restore("{\"v\":99}", new AppSettings(), Path.GetTempPath()));
}
