using MBExtreme.Core.Apps;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>Fase 6: inventário (registro e Loja), comando de desinstalação, programa aberto e correspondência com o winget.</summary>
public class ProgramasFase6Tests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\App\\unins000.exe\" /SILENT", "C:\\Program Files\\App\\unins000.exe", "/SILENT")]
    [InlineData("MsiExec.exe /X{12345678-1234-1234-1234-123456789012}", "MsiExec.exe", "/X{12345678-1234-1234-1234-123456789012}")]
    [InlineData("C:\\Program Files\\App\\uninstall.exe --remove", "C:\\Program Files\\App\\uninstall.exe", "--remove")]
    [InlineData("\"C:\\App\\u.exe\"", "C:\\App\\u.exe", "")]
    public void Separa_o_desinstalador_dos_argumentos(string command, string file, string args)
    {
        var split = InstalledPrograms.SplitCommand(command)!.Value;
        Assert.Equal(file, split.File);
        Assert.Equal(args, split.Arguments);
    }

    [Fact]
    public void Comando_vazio_ou_quebrado_nao_vira_nada()
    {
        Assert.Null(InstalledPrograms.SplitCommand(""));
        Assert.Null(InstalledPrograms.SplitCommand("\"C:\\sem fim"));
    }

    [Theory]
    [InlineData("\"C:\\App\\app.exe\",0", "C:\\App\\app.exe")]
    [InlineData("C:\\App\\app.ico", "C:\\App\\app.ico")]
    [InlineData("C:\\App\\app.exe,-101", "C:\\App\\app.exe")]
    [InlineData("", null)]
    public void Caminho_do_icone(string value, string? expected) => Assert.Equal(expected, InstalledPrograms.IconFile(value));

    [Fact]
    public void Data_de_instalacao_e_o_que_entra_na_lista()
    {
        Assert.Equal(new DateTime(2026, 11, 6), InstalledPrograms.ParseInstallDate("20261106"));
        Assert.Null(InstalledPrograms.ParseInstallDate("2026"));
        Assert.Null(InstalledPrograms.ParseInstallDate("20261341"));
        Assert.True(InstalledPrograms.ShouldList("7-Zip", 0, null, null));
        Assert.False(InstalledPrograms.ShouldList("Componente", 1, null, null));          // componente do sistema
        Assert.False(InstalledPrograms.ShouldList("Atualização", 0, "Office", null));      // filho de outro programa
        Assert.False(InstalledPrograms.ShouldList("KB123", 0, null, "Security Update"));
        Assert.False(InstalledPrograms.ShouldList(" ", 0, null, null));
    }

    [Fact]
    public void Le_os_aplicativos_da_loja_em_json()
    {
        string many = "[{\"Name\":\"Microsoft.WindowsCalculator\",\"Version\":\"11.2405.2.0\",\"PackageFullName\":\"Microsoft.WindowsCalculator_11.2405.2.0_x64__8wekyb3d8bbwe\",\"InstallLocation\":\"C:\\\\Program Files\\\\WindowsApps\\\\Calc\"}," +
                      "{\"Name\":\"SpotifyAB.SpotifyMusic\",\"Version\":\"1.2\",\"PackageFullName\":\"SpotifyAB.SpotifyMusic_1.2_x64__zpdnekdrzrea0\"}]";
        var list = InstalledPrograms.ParseStore(many);
        Assert.Equal(2, list.Count);
        Assert.Equal("Windows Calculator", list[0].Name);
        Assert.Equal(ProgramSource.Store, list[0].Source);
        Assert.Equal("Spotify Music", list[1].Name);
        Assert.Single(InstalledPrograms.ParseStore("{\"Name\":\"Microsoft.Paint\",\"Version\":\"1\",\"PackageFullName\":\"x\"}"));
        Assert.Empty(InstalledPrograms.ParseStore("quebrado"));
        Assert.Empty(InstalledPrograms.ParseStore(""));
    }

    [Fact]
    public void Avisa_quando_o_programa_esta_aberto()
    {
        var processes = new (string, string?)[]
        {
            ("Discord", @"C:\Users\Ana\AppData\Local\Discord\app-1.0\Discord.exe"),
            ("Discord", @"C:\Users\Ana\AppData\Local\Discord\app-1.0\Discord.exe"),
            ("chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe"),
            ("System", null),
        };
        Assert.Equal(new[] { "Discord" }, InstalledPrograms.RunningFrom(@"C:\Users\Ana\AppData\Local\Discord", processes));
        Assert.Empty(InstalledPrograms.RunningFrom(@"C:\Program Files\Google\Chrome2", processes));
        Assert.Empty(InstalledPrograms.RunningFrom(null, processes));
    }

    [Fact]
    public void Acha_no_inventario_o_programa_da_atualizacao_do_winget()
    {
        InstalledProgram P(string name) => new(name, "1", "", ProgramSource.Win32, null, null, @"C:\" + name, null, null, null, null);
        var programs = new[] { P("Discord"), P("Mozilla Firefox (x64 pt-BR)"), P("7-Zip 24.08 (x64)") };
        Assert.Equal("Discord", InstalledPrograms.Match(programs, "Discord")!.Name);
        Assert.Equal("Mozilla Firefox (x64 pt-BR)", InstalledPrograms.Match(programs, "Mozilla Firefox")!.Name);
        Assert.Null(InstalledPrograms.Match(programs, "Steam"));
    }
}
