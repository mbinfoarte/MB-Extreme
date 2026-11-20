using MBExtreme.Core.Remote;
using Xunit;

namespace MBExtreme.Core.Tests;

public class RustDeskTests
{
    [Theory]
    [InlineData("482 917 305", "482917305")]
    [InlineData("482-917-305", "482917305")]
    [InlineData("1234567890", "1234567890")]
    [InlineData("12345", null)]
    [InlineData("48291730a", null)]
    [InlineData("", null)]
    public void NormalizesIds(string input, string? expected) => Assert.Equal(expected, RustDesk.NormalizeId(input));

    [Fact]
    public void ReadsIdAndFormats()
    {
        Assert.Equal("482917305", RustDesk.ParseId("\r\n482917305\r\n"));
        Assert.Null(RustDesk.ParseId("Installation and administrative privileges required!"));
        Assert.Equal("482 917 305", RustDesk.FormatId("482917305"));
        Assert.Equal("123 456 789 0", RustDesk.FormatId("1234567890"));
    }

    [Fact]
    public void BuildsSafeCommands()
    {
        Assert.Equal(new[] { "--connect", "482917305", "--password", "k7m4-2xqp" }, RustDesk.ConnectArgs("482 917 305", " k7m4-2xqp "));
        Assert.Equal(new[] { "--file-transfer", "482917305" }, RustDesk.ConnectArgs("482917305", null, files: true));
        Assert.Throws<ArgumentException>(() => RustDesk.ConnectArgs("482917305 & del c:\\", null));
        Assert.Equal(new[] { "--password", "a b" }, RustDesk.PasswordArgs("a b"));
        Assert.Contains(("approve-mode", "password"), RustDesk.Options(unattended: true));
        Assert.Contains(("approve-mode", ""), RustDesk.Options(unattended: false));
    }

    [Fact]
    public void SessionPasswordsAreRandomAndReadable()
    {
        var a = RustDesk.NewSessionPassword();
        Assert.Matches("^[a-z2-9]{4}-[a-z2-9]{4}$", a);
        Assert.DoesNotContain('l', a.Replace("-", ""));
        Assert.NotEqual(a, RustDesk.NewSessionPassword());
        Assert.Null(RustDesk.ValidateFixedPassword("loja2026x"));
        Assert.NotNull(RustDesk.ValidateFixedPassword("curta1"));
        Assert.NotNull(RustDesk.ValidateFixedPassword("semnumeros"));
        Assert.NotNull(RustDesk.ValidateFixedPassword("com espaco 1"));
    }

    [Fact]
    public void DetectsOpenSession()
    {
        Assert.True(RustDesk.IsSessionProcess("\"C:\\Program Files\\RustDesk\\rustdesk.exe\" --cm"));
        Assert.False(RustDesk.IsSessionProcess("\"C:\\Program Files\\RustDesk\\rustdesk.exe\" --service"));
        Assert.False(RustDesk.IsSessionProcess("outro.exe --cm"));
        Assert.False(RustDesk.IsSessionProcess(null));
    }

    [Fact]
    public void RemembersPeersNewestFirst()
    {
        var t = new DateTime(2026, 11, 20, 9, 40, 0);
        var list = RustDesk.Remember(Array.Empty<RustDeskPeer>(), "812004731", "Loja Center", t);
        list = RustDesk.Remember(list, "305118902", null, t.AddHours(1));
        list = RustDesk.Remember(list, "812 004 731", null, t.AddHours(2));
        Assert.Equal(new[] { "812004731", "305118902" }, list.Select(p => p.Id));
        Assert.Equal("Loja Center", list[0].Name);   // o nome continua
        Assert.Equal("305 118 902", list[1].Name);
        string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        RustDesk.SavePeers(file, list);
        Assert.Equal(2, RustDesk.LoadPeers(file).Count);
        File.Delete(file);
        Assert.Contains("482 917 305", RustDesk.ShareText("482917305", "k7m4-2xqp"));
    }
}
