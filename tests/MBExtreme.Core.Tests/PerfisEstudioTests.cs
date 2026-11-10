using MBExtreme.Core.Settings;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>Perfis salvos do Estúdio: tudo da live e da gravação num nome, sem levar chaves nem senhas.</summary>
public class PerfisEstudioTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "mbx-perfis-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Salva_e_volta_live_gravacao_cameras_e_cenas()
    {
        string folder = TempFolder();
        try
        {
            var s = new AppSettings { StudioFps = 30, StudioStreamService = 1, StudioEncoderName = "av1_nvenc", StudioPixKey = "pix@exemplo.com", StudioHotkeyRecord = "CtrlShift+F5" };
            s.StudioCameras.Add(new StudioCameraSetting { Name = "Webcam 4K", Corner = 2, Percent = 30, On = true });
            s.StudioScenes.Add(new StudioSceneSetting { Name = "Jogo" });
            Assert.Equal("Live de sexta", StudioProfiles.Save(s, "Live de sexta", folder));

            var other = new AppSettings();
            Assert.True(StudioProfiles.Load(other, "Live de sexta", folder) > 50);
            Assert.Equal((30, 1, "av1_nvenc", "pix@exemplo.com", "CtrlShift+F5"), (other.StudioFps, other.StudioStreamService, other.StudioEncoderName, other.StudioPixKey, other.StudioHotkeyRecord));
            Assert.Equal("Webcam 4K", Assert.Single(other.StudioCameras).Name);
            Assert.Equal("Jogo", Assert.Single(other.StudioScenes).Name);
            Assert.NotSame(s.StudioCameras, other.StudioCameras);   // cópia, não a mesma lista

            Assert.Equal("Live de sexta", Assert.Single(StudioProfiles.List(folder)).Name);
            Assert.True(StudioProfiles.Delete("Live de sexta", folder));
            Assert.Empty(StudioProfiles.List(folder));
            Assert.Equal(-1, StudioProfiles.Load(other, "Live de sexta", folder));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void Chaves_senhas_e_logins_nunca_entram_no_perfil()
    {
        var s = new AppSettings { StudioProtectedKey = "chave-protegida", StudioYtRefreshProtected = "token", StudioTwitchSecretProtected = "segredo" };
        string json = StudioProfiles.Capture(s).ToJsonString();
        Assert.DoesNotContain("chave-protegida", json);
        Assert.DoesNotContain("token", json);
        Assert.DoesNotContain("segredo", json);
        Assert.DoesNotContain(StudioProfiles.Properties, p => p.Name.Contains("Protected") || p.Name.Contains("Secret"));
        Assert.Contains(StudioProfiles.Properties, p => p.Name == "StudioPixKey");                 // a chave Pix é pública: fica
        Assert.DoesNotContain(StudioProfiles.Properties, p => p.Name == "StudioEncoderCache");    // teste da placa: é do PC, não do perfil
    }

    [Fact]
    public void Perfil_antigo_ou_estragado_aplica_so_o_que_serve()
    {
        var s = new AppSettings { StudioFps = 60 };
        var profile = new System.Text.Json.Nodes.JsonObject { ["StudioFps"] = "isto não é número", ["StudioMp4"] = true, ["CampoQueNaoExisteMais"] = 1 };
        Assert.Equal(1, StudioProfiles.Apply(s, profile));
        Assert.Equal(60, s.StudioFps);
        Assert.True(s.StudioMp4);
    }

    [Theory]
    [InlineData("  Live / Twitch?  ", "Live  Twitch")]
    [InlineData("...", null)]
    [InlineData("", null)]
    public void Nome_do_perfil_vira_nome_de_arquivo_seguro(string name, string? expected)
    {
        Assert.Equal(expected, StudioProfiles.CleanName(name));
    }
}
