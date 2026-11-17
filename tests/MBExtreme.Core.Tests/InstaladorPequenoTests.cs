using MBExtreme.Bootstrap;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>Instalador online (MBExtreme-Instalar.exe): escolha da Release e conferência.</summary>
public class InstaladorPequenoTests
{
    private static Dictionary<string, object> Asset(string name, string url, string digest = "") =>
        new() { ["name"] = name, ["browser_download_url"] = url, ["size"] = 1234, ["digest"] = digest };

    private static Dictionary<string, object> Release(string tag, params object[] assets) =>
        new() { ["tag_name"] = tag, ["name"] = tag, ["draft"] = false, ["assets"] = assets };

    private const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void Escolhe_a_maior_compilacao_com_instalador_conferivel()
    {
        var releases = new object[]
        {
            Release("beta-52", Asset(ReleasePicker.SetupAsset, "https://github.com/mbinfoarte/MB-Extreme/releases/download/beta-52/MBExtreme-Beta-Setup.exe", "sha256:" + Sha)),
            Release("beta-54", Asset("outro.zip", "https://github.com/x/y.zip")),   // sem instalador
            Release("beta-53",
                Asset(ReleasePicker.SetupAsset, "https://github.com/mbinfoarte/MB-Extreme/releases/download/beta-53/MBExtreme-Beta-Setup.exe"),
                Asset(ReleasePicker.SetupAsset + ".sha256", "https://github.com/mbinfoarte/MB-Extreme/releases/download/beta-53/MBExtreme-Beta-Setup.exe.sha256")),
        };
        var choice = ReleasePicker.Pick(releases);
        Assert.NotNull(choice);
        Assert.Equal(53, choice.Build);
        Assert.EndsWith(".sha256", choice.Sha256Url);
        Assert.Equal(1234, choice.Size);
    }

    [Fact]
    public void Recusa_endereco_fora_do_github_rascunho_e_release_sem_impressao_digital()
    {
        Assert.Null(ReleasePicker.Pick(new object[] { Release("beta-9", Asset(ReleasePicker.SetupAsset, "http://github.com/a.exe", "sha256:" + Sha)) }));
        Assert.Null(ReleasePicker.Pick(new object[] { Release("beta-9", Asset(ReleasePicker.SetupAsset, "https://exemplo.com/a.exe", "sha256:" + Sha)) }));
        Assert.Null(ReleasePicker.Pick(new object[] { Release("beta-9", Asset(ReleasePicker.SetupAsset, "https://github.com/a.exe")) }));
        var draft = Release("beta-9", Asset(ReleasePicker.SetupAsset, "https://github.com/a.exe", "sha256:" + Sha));
        draft["draft"] = true;
        Assert.Null(ReleasePicker.Pick(new object[] { draft }));
        Assert.True(ReleasePicker.IsSafeUrl("https://objects.githubusercontent.com/x"));
        Assert.False(ReleasePicker.IsSafeUrl("https://githubusercontent.com.mal.com/x"));
    }

    [Fact]
    public void Le_o_arquivo_sha256_e_o_numero_da_tag()
    {
        Assert.Equal(Sha, ReleasePicker.ParseShaFile(Sha.ToUpperInvariant() + "  MBExtreme-Beta-Setup.exe"));
        Assert.Null(ReleasePicker.ParseShaFile("nada aqui"));
        Assert.Equal(53, ReleasePicker.BuildOf("beta-53"));
        Assert.Equal(0, ReleasePicker.BuildOf("sem-numero"));
    }
}
