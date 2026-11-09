using MBExtreme.Core.Setup;
using MBExtreme.Core.Update;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>Atualização pelas Releases públicas do GitHub e o modo de atualização do instalador.</summary>
public class AtualizacaoGitHubTests
{
    private const string Sha = "1ae479114707c9113847b3b7e88b538a84307ff2a4b1ce34d22d62b54837e1a9";

    private static string Release(string tag, bool pre, string body, bool digest = true, bool shaFile = false) => $$"""
        {"tag_name":"{{tag}}","name":"MB Extreme - {{tag}}","draft":false,"prerelease":{{(pre ? "true" : "false")}},"body":{{System.Text.Json.JsonSerializer.Serialize(body)}},
         "assets":[
           {"name":"MBExtreme-Beta-Setup.exe","size":205422708,"browser_download_url":"https://github.com/mbinfoarte/MB-Extreme/releases/download/{{tag}}/MBExtreme-Beta-Setup.exe"{{(digest ? $",\"digest\":\"sha256:{Sha}\"" : "")}}},
           {{(shaFile ? $"{{\"name\":\"MBExtreme-Beta-Setup.exe.sha256\",\"browser_download_url\":\"https://github.com/x/y/releases/download/{tag}/MBExtreme-Beta-Setup.exe.sha256\"}}," : "")}}
           {"name":"MBExtreme-Beta-portatil-win-x64.zip","browser_download_url":"https://github.com/x/y/z.zip"}]}
        """;

    [Theory]
    [InlineData("beta-49", 49)]
    [InlineData("v1.0.52", 52)]
    [InlineData("sem-numero", 0)]
    public void Compilacao_pela_tag(string tag, int build) => Assert.Equal(build, GitHubReleases.BuildOf(tag));

    [Fact]
    public void Le_a_Release_com_o_digest_do_GitHub()
    {
        var m = GitHubReleases.Newest(Release("beta-49", true, "## Novidades\n\n- Mapa da conexão\n- Velocímetro\n\n---\nCompilação 49 (commit abc)."), includePrerelease: true);
        Assert.NotNull(m);
        Assert.Equal(49, m!.Build);
        Assert.Equal(Sha, m.Sha256);
        Assert.Equal(205422708, m.Size);
        Assert.Equal(0, m.MinimumBuild);
        Assert.Equal("- Mapa da conexão\n- Velocímetro", m.Notes);
        Assert.EndsWith("/beta-49/MBExtreme-Beta-Setup.exe", m.Url);
    }

    [Fact]
    public void Sem_digest_usa_o_arquivo_sha256()
    {
        var m = GitHubReleases.Newest(Release("beta-50", true, "x", digest: false, shaFile: true), true);
        Assert.EndsWith("MBExtreme-Beta-Setup.exe.sha256", m!.Sha256);   // lido depois, no CheckAsync
        Assert.Equal(Sha, GitHubReleases.ParseShaFile($"{Sha.ToUpperInvariant()}  MBExtreme-Beta-Setup.exe\r\n"));
        Assert.Null(GitHubReleases.ParseShaFile("vazio"));
    }

    [Fact]
    public void Versoes_de_teste_so_para_quem_quer_beta()
    {
        string list = "[" + Release("beta-51", true, "teste") + "," + Release("v1.0.48", false, "estável") + "]";
        Assert.Equal(51, GitHubReleases.Newest(list, includePrerelease: true)!.Build);
        Assert.Equal(48, GitHubReleases.Newest(list, includePrerelease: false)!.Build);
    }

    [Fact]
    public void Marca_de_obrigatoria_e_compilacao_minima()
    {
        var required = GitHubReleases.Newest(Release("beta-52", true, "Correção de segurança [obrigatória]"), true)!;
        Assert.Equal(52, required.MinimumBuild);
        Assert.Equal("Correção de segurança", required.Notes);
        Assert.Equal(UpdateState.Required, AppUpdates.Evaluate(48, required));
        var minimum = GitHubReleases.Newest(Release("beta-53", true, "Novidades\nCompilação mínima: 50"), true)!;
        Assert.Equal(50, minimum.MinimumBuild);
        Assert.Equal(UpdateState.Available, AppUpdates.Evaluate(51, minimum));
        Assert.Equal(UpdateState.Required, AppUpdates.Evaluate(49, minimum));
    }

    [Fact]
    public void Release_sem_instalador_ou_com_endereco_inseguro_e_ignorada()
    {
        Assert.Null(GitHubReleases.Newest("""[{"tag_name":"beta-60","prerelease":true,"assets":[]}]""", true));
        Assert.Null(GitHubReleases.Newest("""[{"tag_name":"beta-61","prerelease":true,"assets":[{"name":"MBExtreme-Beta-Setup.exe","browser_download_url":"http://inseguro/x.exe"}]}]""", true));
        Assert.Null(GitHubReleases.Newest("quebrado", true));
        Assert.Null(GitHubReleases.Newest("""[{"tag_name":"beta-62","draft":true,"assets":[]}]""", true));
    }

    [Fact]
    public void Instalador_em_modo_de_atualizacao()
    {
        string args = UpdateArgs.Build(@"C:\Program Files\MB Extreme\");
        Assert.Equal("/atualizar \"/pasta=C:\\Program Files\\MB Extreme\"", args);
        var parsed = UpdateArgs.Parse(new[] { "/atualizar", "/pasta=C:\\Program Files\\MB Extreme" });
        Assert.True(parsed.Update);
        Assert.Equal(@"C:\Program Files\MB Extreme", parsed.Folder);
        Assert.False(UpdateArgs.Parse(Array.Empty<string>()).Update);
        Assert.Null(UpdateArgs.Parse(new[] { "/atualizar", "/pasta=..\\outra" }).Folder);            // caminho relativo: recusado
        Assert.Null(UpdateArgs.Parse(new[] { "/atualizar", @"/pasta=\\servidor\pasta" }).Folder);   // pasta de rede: recusada
    }
}
