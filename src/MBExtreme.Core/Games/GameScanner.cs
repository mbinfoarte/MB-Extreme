using System.Runtime.Versioning;
using Microsoft.Win32;

namespace MBExtreme.Core.Games;

/// <summary>Procura jogos instalados na Steam, Epic Games e GOG (só leitura) e junta com os adicionados à mão.</summary>
[SupportedOSPlatform("windows")]
public static class GameScanner
{
    public static IReadOnlyList<InstalledGame> Scan(GameLibraryStore store)
    {
        var games = new List<InstalledGame>();
        TryAdd(games, SteamGames);
        TryAdd(games, EpicGames);
        TryAdd(games, GogGames);
        foreach (var m in store.ManualGames())
        {
            if (!File.Exists(m.ExePath)) continue;
            games.Add(new InstalledGame(GameLibraryStore.ManualId(m.ExePath), m.Name, GameLauncher.Manual,
                Path.GetDirectoryName(m.ExePath) ?? "", Path.GetFileName(m.ExePath), m.ExePath));
        }

        return games
            .GroupBy(g => g.Id, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .Select(g => g.ExeName is null ? g with { ExeName = GuessExe(g, store) } : g)
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static void TryAdd(List<InstalledGame> list, Func<IEnumerable<InstalledGame>> source)
    {
        try { list.AddRange(source()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
    }

    private static string? GuessExe(InstalledGame game, GameLibraryStore store)
    {
        string? saved = store.Get(game.Id).ExeName;
        if (ExeGuesser.IsValidExeName(saved)) return saved;
        try
        {
            if (!Directory.Exists(game.InstallPath)) return null;
            var files = new List<(string, long)>();
            foreach (string exe in Directory.EnumerateFiles(game.InstallPath, "*.exe", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true }))
            {
                try { files.Add((exe, new FileInfo(exe).Length)); } catch (IOException) { }
            }
            return ExeGuesser.Pick(files);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Pasta de instalação da Steam (para achar as capas já guardadas por ela).</summary>
    public static string SteamPath()
    {
        string? steam = ReadSteamPath();
        if (string.IsNullOrWhiteSpace(steam)) steam = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        return steam.Replace('/', '\\');
    }

    private static string? ReadSteamPath()
    {
        try { return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string; }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return null; }
    }

    private static IEnumerable<InstalledGame> SteamGames()
    {
        string? steam = ReadSteamPath();
        if (string.IsNullOrWhiteSpace(steam)) steam = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        steam = steam.Replace('/', '\\');
        string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        var libraries = new List<string> { steam };
        if (File.Exists(vdf)) libraries.AddRange(GameParsers.SteamLibraryFolders(File.ReadAllText(vdf)).Select(p => p.Replace("\\\\", "\\")));

        foreach (string library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string apps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (string file in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                InstalledGame? game = null;
                try { game = GameParsers.SteamManifest(File.ReadAllText(file), library); }
                catch (IOException) { }
                if (game is not null) yield return game;
            }
        }
    }

    private static IEnumerable<InstalledGame> EpicGames()
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(folder)) yield break;
        foreach (string file in Directory.EnumerateFiles(folder, "*.item"))
        {
            InstalledGame? game = null;
            try { game = GameParsers.EpicManifest(File.ReadAllText(file)); }
            catch (IOException) { }
            if (game is not null) yield return game;
        }
    }

    private static IEnumerable<InstalledGame> GogGames()
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using var games = root.OpenSubKey(@"SOFTWARE\GOG.com\Games");
        if (games is null) yield break;
        foreach (string id in games.GetSubKeyNames())
        {
            using var key = games.OpenSubKey(id);
            string? name = key?.GetValue("gameName") as string;
            string? path = key?.GetValue("path") as string;
            string? exe = key?.GetValue("exe") as string;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) continue;
            yield return new InstalledGame("gog:" + id, name, GameLauncher.Gog, path,
                string.IsNullOrWhiteSpace(exe) ? null : Path.GetFileName(exe), string.IsNullOrWhiteSpace(exe) ? null : exe);
        }
    }
}
