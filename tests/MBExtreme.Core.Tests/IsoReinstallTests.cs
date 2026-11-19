using System.Xml.Linq;
using MBExtreme.Core.Install;
using Xunit;

namespace MBExtreme.Core.Tests;

public class IsoBuilderTests
{
    private const string WimInfo = """
        Deployment Image Servicing and Management tool
        Version: 10.0.26100.1

        Details for image : D:\sources\install.wim

        Index : 1
        Name : Windows 11 Home
        Description : Windows 11 Home
        Size : 18,734,567,890 bytes

        Index : 6
        Name : Windows 11 Pro
        Description : Windows 11 Pro
        Size : 19.012.345.678 bytes

        The operation completed successfully.
        """;

    [Fact]
    public void ReadsEditionsFromDism()
    {
        var list = IsoBuilder.ParseWimInfo(WimInfo);
        Assert.Equal(2, list.Count);
        Assert.Equal(new WimEdition(6, "Windows 11 Pro", 19012345678), list[1]);
        Assert.Equal(6, IsoBuilder.Pick(list, "windows 11 pro")!.Index);
        Assert.Equal(6, IsoBuilder.Pick(list, "Windows 11 Education")!.Index);   // não tem: cai na Pro
        Assert.Null(IsoBuilder.Pick(Array.Empty<WimEdition>(), "x"));
    }

    [Fact]
    public void RemovesOnlyTheChosenApps()
    {
        string[] pkgs =
        {
            "Clipchamp.Clipchamp_2.2.8.0_neutral_~_yxz26nhyzhsrt", "Microsoft.BingNews_4.2_x64__8wekyb3d8bbwe",
            "Microsoft.WindowsStore_22401_neutral_~_8wekyb3d8bbwe", "Microsoft.DesktopAppInstaller_1.22_neutral_~_8wekyb3d8bbwe",
            "Microsoft.GamingApp_2404_neutral_~_8wekyb3d8bbwe", "Microsoft.WindowsCalculator_11_neutral_~_8wekyb3d8bbwe",
        };
        var basic = IsoBuilder.AppsToRemove(pkgs, new IsoOptions());
        Assert.Equal(new[] { pkgs[0], pkgs[1] }, basic);

        var all = IsoBuilder.AppsToRemove(pkgs, new IsoOptions { RemoveXbox = true, KeepStore = false });
        Assert.Contains(pkgs[2], all);
        Assert.Contains(pkgs[4], all);
        Assert.DoesNotContain(pkgs[3], all);   // o winget nunca sai
        Assert.DoesNotContain(pkgs[5], all);

        Assert.Empty(IsoBuilder.AppsToRemove(pkgs, new IsoOptions { RemoveApps = false, DisableCopilot = false }));
    }

    [Fact]
    public void ParsesProvisionedPackages()
    {
        var names = IsoBuilder.ParseProvisioned("Displayname : Clipchamp\r\nPackageName : Clipchamp.Clipchamp_2.2.8.0_neutral_~_yxz\r\n\r\nPackageName : Microsoft.BingNews_1_x64__8we\r\n");
        Assert.Equal(new[] { "Clipchamp.Clipchamp_2.2.8.0_neutral_~_yxz", "Microsoft.BingNews_1_x64__8we" }, names);
    }

    [Fact]
    public void UnattendIsValidXmlAndKeepsDiskScreen()
    {
        string xml = IsoBuilder.Unattend(new IsoOptions { LocalAccount = "Técnico <MB>"[..7], BypassChecks = true });
        var doc = XDocument.Parse(xml);
        XNamespace u = "urn:schemas-microsoft-com:unattend";
        Assert.Equal(3, doc.Root!.Elements(u + "settings").Count());
        Assert.Contains("<Name>Técnico</Name>", xml);
        Assert.Contains("BypassTPMCheck", xml);
        Assert.Contains("mb-primeiro-login.cmd", xml);
        Assert.DoesNotContain("DiskConfiguration", xml);   // a tela de disco fica para o técnico
        Assert.DoesNotContain("<InstallTo>", xml);

        string plain = IsoBuilder.Unattend(new IsoOptions { InstallMbExtreme = false, Kit = "" });
        Assert.DoesNotContain("BypassTPMCheck", plain);
        Assert.DoesNotContain("FirstLogonCommands", plain);
    }

    [Fact]
    public void EscapesAccountName()
    {
        string xml = IsoBuilder.Unattend(new IsoOptions { LocalAccount = "A&B" });
        Assert.Contains("<Name>A&amp;B</Name>", xml);
        XDocument.Parse(xml);
        Assert.NotNull(IsoBuilder.ValidateAccount("A&B"));
        Assert.NotNull(IsoBuilder.ValidateAccount("Administrador"));
        Assert.Null(IsoBuilder.ValidateAccount("Técnico"));
    }

    [Fact]
    public void FirstLogonScriptSkipsUnsafeIds()
    {
        string s = IsoBuilder.FirstLogonScript(new IsoOptions { GamerProfile = true }, new[] { "Google.Chrome", "x & del c:\\", "Google.Chrome" });
        Assert.Single(s.Split('\n'), l => l.Contains("winget install"));
        Assert.DoesNotContain("del c:", s);
        Assert.Contains(IsoBuilder.SetupUrl, s);
        Assert.Contains("primeira-execucao.json", s);
        Assert.Equal("gamer", IsoBuilder.ReadFirstRun(IsoBuilder.FirstRunJson(new IsoOptions { GamerProfile = true }))!.Profile);
    }

    [Fact]
    public void OscdimgBootsBiosAndUefi()
    {
        var args = IsoBuilder.OscdimgArgs(@"W:\midia", @"D:\MB.iso", "MB Windows 11 Pro");
        Assert.Contains("-lMB_WINDOWS_11_PRO", args);
        Assert.Contains(args, a => a.StartsWith("-bootdata:2#p0,e,b", StringComparison.Ordinal) && a.Contains("#pEF,e,b"));
        Assert.Equal(@"D:\MB.iso", args[^1]);
        Assert.Equal("MB-Win11-Pro.iso", IsoBuilder.DefaultOutputName("Windows 11 Pro"));
    }

    [Fact]
    public void UsbScriptRejectsSystemLetters()
    {
        Assert.Throws<ArgumentException>(() => IsoBuilder.PrepareUsbScript('C'));
        string s = IsoBuilder.PrepareUsbScript('e');
        Assert.Contains("-DriveLetter E", s);
        Assert.Contains("BusType -ne 'USB'", s);
        Assert.Equal("'it''s'", IsoBuilder.Ps("it's"));
    }

    [Fact]
    public void TemplatesKeepUsesAndReplaceByName()
    {
        var list = IsoBuilder.SaveTemplate(Array.Empty<IsoTemplate>(), new IsoTemplate("Gamer", new IsoOptions { GamerProfile = true }));
        list = IsoBuilder.CountUse(list, "gamer");
        list = IsoBuilder.SaveTemplate(list, new IsoTemplate("GAMER", new IsoOptions()));
        Assert.Single(list);
        Assert.Equal(1, list[0].Uses);

        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        IsoBuilder.WriteTemplates(file, list);
        Assert.Equal("GAMER", IsoBuilder.LoadTemplates(file)[0].Name);
        File.Delete(file);
        Assert.Equal("Win11 Pro + kit Essencial + perfil Gamer", IsoBuilder.Summary(new IsoOptions { GamerProfile = true }));
    }
}

public class ReinstallTests
{
    private static ReinstallFacts Good => new(true, "GPT", false, false, 80 * Reinstall.Gb, 400 * Reinstall.Gb, 120 * Reinstall.Gb, 0, 3);

    [Fact]
    public void ChecksBlockTheRiskyCases()
    {
        long bytes = Reinstall.PartitionBytes(6 * Reinstall.Gb);
        Assert.Equal(8 * Reinstall.Gb, bytes);
        Assert.Equal(9 * Reinstall.Gb, Reinstall.PartitionBytes(7 * Reinstall.Gb + 100));
        Assert.True(Reinstall.CanPrepare(Reinstall.Evaluate(Good, bytes)));
        Assert.False(Reinstall.CanPrepare(Reinstall.Evaluate(Good with { BitLocker = true }, bytes)));
        Assert.False(Reinstall.CanPrepare(Reinstall.Evaluate(Good with { OnBattery = true }, bytes)));
        Assert.False(Reinstall.CanPrepare(Reinstall.Evaluate(Good with { FreeBytes = 10 * Reinstall.Gb }, bytes)));
        Assert.False(Reinstall.CanPrepare(Reinstall.Evaluate(Good with { CMinSize = 395 * Reinstall.Gb }, bytes)));
        Assert.Contains(Reinstall.Evaluate(Good with { PartitionStyle = "MBR" }, bytes), c => c.Level == CheckLevel.Warn);
    }

    [Fact]
    public void ParsesPowerShellOutputs()
    {
        var f = Reinstall.ParseFacts("aviso qualquer\r\n0|3|429496729600|128849018880|GPT|2\r\n")!.Value;
        Assert.Equal((0, 3, 429496729600L, 128849018880L, "GPT", false), f);
        Assert.True(Reinstall.ParseFacts("1|2|10|5|MBR|1")!.Value.BitLocker);
        Assert.Null(Reinstall.ParseFacts("erro"));
        Assert.Equal(('M', 5), Reinstall.ParseCreated("\r\nM|5\r\n"));
        Assert.Equal((0, 5, (char?)'M'), Reinstall.ParseFound("0|5|M\r\n"));
        Assert.Equal((0, 5, (char?)null), Reinstall.ParseFound("0|5|\r\n"));
        Assert.Equal("{0a1b2c3d-1111-2222-3333-444455556666}", Reinstall.ParseGuid("A entrada {0a1b2c3d-1111-2222-3333-444455556666} foi criada com êxito."));
    }

    [Fact]
    public void BootEntryUsesRamdiskAndOneTimeSequence()
    {
        var cmds = Reinstall.BootCommands('m', "{dev}", "{ldr}", uefi: true);
        Assert.Contains(cmds, c => c.SequenceEqual(new[] { "/set", "{ldr}", "device", @"ramdisk=[M:]\sources\boot.wim,{dev}" }));
        Assert.Contains(cmds, c => c[^1].EndsWith("winload.efi", StringComparison.Ordinal));
        Assert.Equal(new[] { "/bootsequence", "{ldr}" }, cmds[^1]);
        Assert.Contains(Reinstall.BootCommands('M', "{d}", "{l}", uefi: false), c => c[^1].EndsWith("winload.exe", StringComparison.Ordinal));
    }

    [Fact]
    public void RemoveChecksTheLabelFirst()
    {
        string s = Reinstall.RemoveScript(0, 5);
        Assert.True(s.IndexOf("MB-SETUP", StringComparison.Ordinal) < s.IndexOf("Remove-Partition", StringComparison.Ordinal));
        var state = new ReinstallState(0, 5, 'M', 8 * Reinstall.Gb, "{l}", "{d}", new DateTime(2026, 11, 19));
        Assert.Equal(state, Reinstall.FromJson(Reinstall.ToJson(state)));
    }
}
