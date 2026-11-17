using MBExtreme.Core.Gamepads;
using Xunit;

namespace MBExtreme.Core.Tests;

public class InstalarDriversTests
{
    private const string Release = """
    {"tag_name":"v0.9.0","assets":[
      {"name":"viiper-linux-amd64.tar.gz","browser_download_url":"https://github.com/Alia5/VIIPER/releases/download/v0.9.0/viiper-linux-amd64.tar.gz","size":10},
      {"name":"viiper-windows-arm64.exe","browser_download_url":"https://github.com/Alia5/VIIPER/releases/download/v0.9.0/viiper-windows-arm64.exe","size":11},
      {"name":"viiper-windows-amd64.exe","browser_download_url":"https://github.com/Alia5/VIIPER/releases/download/v0.9.0/viiper-windows-amd64.exe","size":12,"digest":"sha256:ABCDEF"},
      {"name":"FakerInput_0.1.0_x64.msi","browser_download_url":"https://github.com/Ryochan7/FakerInput/releases/download/v0.1.0/FakerInput_0.1.0_x64.msi","size":13},
      {"name":"FakerInput_0.1.0_x86.msi","browser_download_url":"https://github.com/Ryochan7/FakerInput/releases/download/v0.1.0/FakerInput_0.1.0_x86.msi","size":14},
      {"name":"evil.exe","browser_download_url":"https://example.com/evil.exe","size":1}
    ]}
    """;

    [Fact]
    public void Escolhe_o_arquivo_certo_da_versao_mais_nova()
    {
        var viiper = DriverDownloads.Pick(Release, DriverDownloads.For("viiper")!.Asset)!;
        Assert.Equal(DriverDownloads.IsArm ? "viiper-windows-arm64.exe" : "viiper-windows-amd64.exe", viiper.Name);
        Assert.Equal("v0.9.0", viiper.Tag);
        if (!DriverDownloads.IsArm) Assert.Equal("abcdef", viiper.Sha256);
        Assert.Equal("FakerInput_0.1.0_x64.msi", DriverDownloads.Pick(Release, DriverDownloads.For("fakerinput")!.Asset)!.Name);
        // só baixa de github.com
        Assert.Null(DriverDownloads.Pick(Release, n => n == "evil.exe"));
        Assert.Null(DriverDownloads.Pick("{ruim", _ => true));
        Assert.Null(DriverDownloads.Pick("""{"tag_name":"v1"}""", _ => true));
    }

    [Fact]
    public void Pacotes_e_codigos_de_saida()
    {
        foreach (string id in new[] { "viiper", "fakerinput", "vigem", "hidhide" }) Assert.NotNull(DriverDownloads.For(id));
        Assert.Null(DriverDownloads.For("vcredist"));
        Assert.True(DriverDownloads.For("viiper")!.Silent);
        Assert.True(DriverDownloads.For("fakerinput")!.Msi);
        Assert.Equal("https://api.github.com/repos/Alia5/VIIPER/releases/latest", DriverDownloads.LatestUrl("Alia5/VIIPER"));
        Assert.Equal((true, false), DriverDownloads.Result(0));
        Assert.Equal((true, true), DriverDownloads.Result(3010));
        Assert.Equal((true, true), DriverDownloads.Result(1641));
        Assert.Equal((false, false), DriverDownloads.Result(1602));
        Assert.Equal((false, false), DriverDownloads.Result(1));
    }
}
