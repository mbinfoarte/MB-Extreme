using MBExtreme.Core.Games;
using Xunit;

namespace MBExtreme.Core.Tests;

public class BibliotecaJogosTests
{
    private const string Libraries = """
        "libraryfolders"
        {
            "0" { "path" "C:\\Program Files (x86)\\Steam" "label" "" }
            "1" { "path" "D:\\Jogos\\SteamLibrary" }
        }
        """;

    private const string Manifest = """
        "AppState"
        {
            "appid"		"730"
            "name"		"Counter-Strike 2"
            "installdir"		"Counter-Strike Global Offensive"
        }
        """;

    [Fact]
    public void LeAsPastasDeBibliotecaDaSteam()
    {
        var folders = GameParsers.SteamLibraryFolders(Libraries);
        Assert.Equal(2, folders.Count);
        Assert.Equal(@"D:\Jogos\SteamLibrary", folders[1]);
    }

    [Fact]
    public void LeOManifestoDeUmJogoDaSteam()
    {
        var game = GameParsers.SteamManifest(Manifest, @"D:\Jogos\SteamLibrary");
        Assert.NotNull(game);
        Assert.Equal("Counter-Strike 2", game!.Name);
        Assert.Equal("steam://rungameid/730", game.LaunchTarget);
        Assert.Equal(@"D:\Jogos\SteamLibrary\steamapps\common\Counter-Strike Global Offensive", game.InstallPath.Replace('/', '\\'));
        Assert.Equal(GameLauncher.Steam, game.Launcher);
    }

    [Fact]
    public void IgnoraFerramentasDaSteam()
    {
        string tool = "\"AppState\" { \"appid\" \"228980\" \"name\" \"Steamworks Common Redistributables\" \"installdir\" \"Steamworks Shared\" }";
        Assert.Null(GameParsers.SteamManifest(tool, @"C:\Steam"));
        Assert.Null(GameParsers.SteamManifest("lixo sem estrutura", @"C:\Steam"));
    }

    [Fact]
    public void LeOManifestoDaEpic()
    {
        string json = """{"DisplayName":"Fortnite","AppName":"Fortnite","InstallLocation":"C:\\Epic\\Fortnite","LaunchExecutable":"FortniteGame/Binaries/Win64/FortniteClient-Win64-Shipping.exe","bIsApplication":true}""";
        var game = GameParsers.EpicManifest(json);
        Assert.NotNull(game);
        Assert.Equal("FortniteClient-Win64-Shipping.exe", game!.ExeName);
        Assert.StartsWith("com.epicgames.launcher://apps/Fortnite", game.LaunchTarget);
        Assert.Null(GameParsers.EpicManifest("{ não é json"));
        Assert.Null(GameParsers.EpicManifest("""{"DisplayName":"DLC","AppName":"x","InstallLocation":"C:\\x","bIsApplication":false}"""));
    }

    [Fact]
    public void EscolheOMaiorExecutavelQueNaoSejaInstalador()
    {
        var picked = ExeGuesser.Pick(new (string, long)[]
        {
            (@"C:\g\unins000.exe", 9_000_000), (@"C:\g\UnityCrashHandler64.exe", 1_000_000),
            (@"C:\g\bin\game.exe", 80_000_000), (@"C:\g\EasyAntiCheat\launcher.exe", 99_000_000), (@"C:\g\tool.exe", 2_000_000),
        });
        Assert.Equal("game.exe", picked);
        Assert.Null(ExeGuesser.Pick(Array.Empty<(string, long)>()));
    }

    [Theory]
    [InlineData("cs2.exe", true)]
    [InlineData("CS2.EXE", true)]
    [InlineData("..\\evil.exe", false)]
    [InlineData("a/b.exe", false)]
    [InlineData("game.dll", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ValidaONomeDoExecutavel(string? name, bool valid) => Assert.Equal(valid, ExeGuesser.IsValidExeName(name));

    [Fact]
    public void GuardaPerfilTempoEJogosManuaisEmDisco()
    {
        string file = Path.Combine(Path.GetTempPath(), "mbx-games-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new GameLibraryStore(file);
            store.AddManual("Meu Jogo", @"C:\Jogos\meu.exe");
            store.AddManual("Duplicado", @"c:\jogos\MEU.exe");
            store.Update("steam:730", s => { s.ExeName = "cs2.exe"; s.HighPriority = true; });
            Assert.True(store.AddPlaytime("cs2", 90, new DateTime(2026, 10, 22)));
            Assert.False(store.AddPlaytime("outro", 90, DateTime.Now));

            var again = new GameLibraryStore(file);
            Assert.Single(again.ManualGames());
            Assert.Equal(90, again.Get("steam:730").MinutesPlayed);
            Assert.True(again.Get("steam:730").HighPriority);
            again.RemoveManual(@"C:\JOGOS\meu.exe");
            Assert.Empty(again.ManualGames());
        }
        finally
        {
            File.Delete(file);
        }
    }
}
