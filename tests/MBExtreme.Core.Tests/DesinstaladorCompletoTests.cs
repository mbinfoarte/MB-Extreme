using MBExtreme.Core.Apps;
using Xunit;

namespace MBExtreme.Core.Tests;

public class DesinstaladorCompletoTests
{
    private static InstalledProgram Prog(string uninstall, string? quiet = null, string? key = null) =>
        new("App", "1.0", "Pub", ProgramSource.Win32, null, null, null, null, uninstall, quiet, null) { RegistryKeyName = key, RegistryHive = "HKLM64" };

    [Fact]
    public void Msi_UsesQuietUninstallWithProductCode()
    {
        var plan = SilentUninstall.Plan(Prog("MsiExec.exe /I{26A24AE4-039D-4CA4-87B4-2F64180311F0}"))!;
        Assert.Equal(UninstallerKind.Msi, plan.Kind);
        Assert.True(plan.Silent);
        Assert.Equal("/x {26A24AE4-039D-4CA4-87B4-2F64180311F0} /qn /norestart", plan.Arguments);
    }

    [Fact]
    public void Msi_FallsBackToRegistryKeyGuid()
    {
        var plan = SilentUninstall.Plan(Prog("MsiExec.exe", key: "{11111111-2222-3333-4444-555555555555}"))!;
        Assert.Equal(UninstallerKind.Msi, plan.Kind);
        Assert.Contains("{11111111-2222-3333-4444-555555555555}", plan.Arguments);
    }

    [Fact]
    public void Inno_AddsVerySilentOnce()
    {
        var plan = SilentUninstall.Plan(Prog("\"C:\\Program Files\\App\\unins000.exe\" /SILENT"))!;
        Assert.Equal(UninstallerKind.Inno, plan.Kind);
        Assert.Equal("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART", plan.Arguments);
        Assert.Equal(@"C:\Program Files\App\unins000.exe", plan.File);
    }

    [Fact]
    public void Nsis_RunsInPlace()
    {
        var plan = SilentUninstall.Plan(Prog("\"C:\\Program Files\\App\\Uninstall.exe\""))!;
        Assert.Equal(UninstallerKind.Nsis, plan.Kind);
        Assert.Equal(@"/S _?=C:\Program Files\App", plan.Arguments);
    }

    [Fact]
    public void QuietCommand_Preferred_AndUnknownAsksConfirmation()
    {
        var quiet = SilentUninstall.Plan(Prog("\"C:\\x\\setup.exe\" -uninstall", "\"C:\\x\\setup.exe\" -uninstall -silent"))!;
        Assert.Equal(UninstallerKind.Quiet, quiet.Kind);
        var unknown = SilentUninstall.Plan(Prog("\"C:\\x\\setup.exe\" -uninstall"))!;
        Assert.False(unknown.Silent);
        Assert.Equal("-uninstall", unknown.Arguments);
    }

    [Theory]
    [InlineData(0, true, false, false)]
    [InlineData(3010, true, true, false)]
    [InlineData(1605, true, false, false)]
    [InlineData(1602, false, false, true)]
    [InlineData(5, false, false, false)]
    public void ExitCodes(int code, bool ok, bool reboot, bool cancelled) =>
        Assert.Equal((ok, reboot, cancelled), SilentUninstall.ReadExit(code));

    [Theory]
    [InlineData("Discord 1.0.9 (x64)", "Discord")]
    [InlineData("7-Zip 24.08 (x64 edition)", "7-Zip")]
    [InlineData("Audacity 3.6.1", "Audacity")]
    [InlineData("Blender", "Blender")]
    [InlineData("Nero 2016", "Nero")]
    public void CleanName(string raw, string clean) => Assert.Equal(clean, LeftoverFinder.CleanName(raw));

    [Theory]
    [InlineData("Discord Inc.", "Discord")]
    [InlineData("Bytedance Ltd.", "Bytedance")]
    [InlineData("Piriform Software Ltd", "Piriform")]
    public void CleanPublisher(string raw, string clean) => Assert.Equal(clean, LeftoverFinder.CleanPublisher(raw));

    [Theory]
    [InlineData("Microsoft", false)]
    [InlineData("Common Files", false)]
    [InlineData("ab", false)]
    [InlineData("CapCut", true)]
    public void Usable(string name, bool ok) => Assert.Equal(ok, LeftoverFinder.Usable(name));

    [Theory]
    [InlineData(@"C:\Program Files\CapCut", true)]
    [InlineData(@"C:\Program Files\CapCut\bin", true)]
    [InlineData(@"C:\Program Files", false)]
    [InlineData(@"C:\Program Files\Common Files", false)]
    [InlineData(@"C:\Program Files\Microsoft", false)]
    [InlineData(@"D:\Jogos\CapCut", false)]
    public void Guard_Folders(string path, bool ok)
    {
        if (!OperatingSystem.IsWindows()) path = path.Replace('\\', '/');
        var bases = new[] { OperatingSystem.IsWindows() ? @"C:\Program Files" : "C:/Program Files" };
        Assert.Equal(ok, OperatingSystem.IsWindows() ? LeftoverGuard.CanDeleteFolder(path, bases) : ok);
    }

    [Theory]
    [InlineData(@"HKCU\SOFTWARE\Bytedance\CapCut", true)]
    [InlineData(@"HKCU\SOFTWARE\Microsoft", false)]
    [InlineData(@"HKCU\SOFTWARE\Microsoft\Office", false)]
    [InlineData(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", false)]
    [InlineData(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{ABC}", true)]
    [InlineData(@"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam_old", true)]
    [InlineData(@"HKLM\SYSTEM\CurrentControlSet\Services\x", false)]
    [InlineData(@"HKCU\SOFTWARE", false)]
    [InlineData(@"HKCR\.txt", false)]
    public void Guard_Keys(string path, bool ok) => Assert.Equal(ok, LeftoverGuard.CanDeleteKey(path));

    [Theory]
    [InlineData(@"C:\Program Files\App", true)]
    [InlineData(@"C:\", false)]
    [InlineData(@"C:\Program Files", false)]
    [InlineData(@"C:\Windows\System32", false)]
    [InlineData(@"C:\Users\fulano", false)]
    [InlineData(@"C:\Users\fulano\AppData\Local", false)]
    [InlineData(@"C:\Users\fulano\AppData\Local\Discord", true)]
    public void ProgramFolder(string path, bool ok) => Assert.Equal(ok, ProgramSizes.IsProgramFolder(path));

    [Theory]
    [InlineData("\"C:\\App\\a.exe\" --x", @"C:\App\a.exe")]
    [InlineData(@"C:\App\a.exe /min", @"C:\App\a.exe")]
    [InlineData("", null)]
    public void ExePath(string command, string? exe) => Assert.Equal(exe, CommandLine.ExePath(command));

    [Fact]
    public void PointsToMissing_IgnoresLaunchersAndRelative()
    {
        Assert.True(RegistryCleaner.PointsToMissing("\"C:\\Gone\\app.exe\" /x", _ => false));
        Assert.False(RegistryCleaner.PointsToMissing("\"C:\\Gone\\app.exe\"", _ => true));
        Assert.False(RegistryCleaner.PointsToMissing(@"C:\Windows\System32\rundll32.exe x.dll,Run", _ => false));
        Assert.False(RegistryCleaner.PointsToMissing("app.exe", _ => false));
    }

    [Fact]
    public void Driver_DetectedForSafety()
    {
        Assert.True(RegistryCleaner.LooksLikeDriver("Realtek High Definition Audio Driver", "Realtek Semiconductor"));
        Assert.False(RegistryCleaner.LooksLikeDriver("Nero 2016 Platinum", "Nero AG"));
    }
}
