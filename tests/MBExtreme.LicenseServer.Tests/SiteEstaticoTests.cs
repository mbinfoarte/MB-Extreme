using MBExtreme.LicenseServer;
using Xunit;

namespace MBExtreme.LicenseServer.Tests;

/// <summary>Versão estática do site para o GitHub Pages.</summary>
public class SiteEstaticoTests
{
    [Fact]
    public void Caminho_base_normalizado()
    {
        Assert.Equal("/", StaticSite.NormalizeBase(""));
        Assert.Equal("/", StaticSite.NormalizeBase(null));
        Assert.Equal("/MB-Extreme/", StaticSite.NormalizeBase("/MB-Extreme"));
        Assert.Equal("/MB-Extreme/", StaticSite.NormalizeBase("MB-Extreme/"));
    }

    [Fact]
    public void Links_ganham_o_caminho_base_e_os_do_servidor_vao_para_o_download()
    {
        string html = "<a href=\"/planos\">P</a><img src=\"/logo.png\"><a href=\"/login?next=/affiliate\">E</a><a href=\"/register\">T</a>"
                      + "<a href=\"https://github.com/x\">G</a><a href=\"//cdn.exemplo/x\">C</a><a href=\"#recursos\">R</a>";
        string result = StaticSite.Rewrite(html, "/MB-Extreme");
        Assert.Contains("href=\"/MB-Extreme/planos\"", result);
        Assert.Contains("src=\"/MB-Extreme/logo.png\"", result);
        Assert.DoesNotContain("login", result);
        Assert.DoesNotContain("register", result);
        Assert.Equal(2, result.Split("href=\"/MB-Extreme/download\"").Length - 1);
        Assert.Contains("href=\"https://github.com/x\"", result);
        Assert.Contains("href=\"//cdn.exemplo/x\"", result);
        Assert.Contains("href=\"#recursos\"", result);
    }

    [Fact]
    public void Exporta_todas_as_paginas_publicas()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"mbx-site-{Guid.NewGuid():N}");
        var files = StaticSite.Export(Pages.Options with { SetupUrl = "https://github.com/mbinfoarte/MB-Extreme/releases/latest/download/MBExtreme-Beta-Setup.exe" },
            dir, "/MB-Extreme/", Path.Combine(dir, "sem-legal"), null);
        Assert.Equal(StaticSite.Routes.Count + 1, files.Count);
        Assert.True(File.Exists(Path.Combine(dir, "index.html")));
        Assert.True(File.Exists(Path.Combine(dir, ".nojekyll")));
        string download = File.ReadAllText(Path.Combine(dir, "download", "index.html"));
        Assert.Contains("releases/latest/download/MBExtreme-Beta-Setup.exe", download);
        Assert.DoesNotContain("href=\"/login", download);
        Directory.Delete(dir, true);
    }
}
