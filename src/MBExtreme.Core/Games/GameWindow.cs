using System.Text;
using System.Text.RegularExpressions;

namespace MBExtreme.Core.Games;

/// <summary>Como o jogo abre: deixar como o jogo quer, tela cheia exclusiva, janela sem bordas (tela cheia em janela) ou janela.</summary>
public enum GameWindowMode { Padrao, TelaCheia, SemBordas, Janela }

/// <summary>Motor do jogo, descoberto pelos arquivos da pasta de instalação. Cada motor entende opções de inicialização diferentes.</summary>
public enum GameEngine { Desconhecido, Unity, Unreal, Source, Source2 }

/// <summary>
/// Tipo de janela por jogo. Usa só o que o próprio jogo entende: opções de inicialização do motor (Unity, Unreal, Source) passadas ao
/// abrir pelo MB Extreme, e, no Unreal, o FullscreenMode do GameUserSettings.ini (com cópia de segurança). Nada é gravado nos arquivos
/// do jogo na pasta de instalação.
/// </summary>
public static class GameWindow
{
    public static string Name(GameWindowMode mode) => mode switch
    {
        GameWindowMode.TelaCheia => "Tela cheia",
        GameWindowMode.SemBordas => "Janela sem bordas",
        GameWindowMode.Janela => "Janela",
        _ => "Como o jogo quiser",
    };

    public static string EngineName(GameEngine engine) => engine switch
    {
        GameEngine.Unity => "Unity", GameEngine.Unreal => "Unreal Engine", GameEngine.Source => "Source", GameEngine.Source2 => "Source 2", _ => "motor não identificado",
    };

    /// <summary>Descobre o motor pelos nomes de arquivo e pasta (lista vinda de <see cref="DetectEngine(string)"/> ou de um teste).</summary>
    public static GameEngine DetectEngine(IEnumerable<string> relativePaths)
    {
        bool unity = false, unreal = false, source = false, source2 = false;
        foreach (var raw in relativePaths)
        {
            string p = raw.Replace('/', '\\').ToLowerInvariant();
            string file = p[(p.LastIndexOf('\\') + 1)..];
            if (file == "unityplayer.dll" || file == "globalgamemanagers" || p.EndsWith("_data\\globalgamemanagers")) unity = true;
            if (Regex.IsMatch(file, @"-win64-shipping\.exe$") || p.Contains("\\engine\\binaries\\") || p.StartsWith("engine\\binaries\\")) unreal = true;
            if (file == "gameinfo.gi") source2 = true;
            if (file == "gameinfo.txt" || file == "hl2.exe" || (file == "engine.dll" && p.Contains("bin\\"))) source = true;
        }
        return unity ? GameEngine.Unity : unreal ? GameEngine.Unreal : source2 ? GameEngine.Source2 : source ? GameEngine.Source : GameEngine.Desconhecido;
    }

    /// <summary>Lê a pasta do jogo (até 3 níveis, no máximo 4000 itens) e descobre o motor.</summary>
    public static GameEngine DetectEngine(string installPath)
    {
        try
        {
            if (!Directory.Exists(installPath)) return GameEngine.Desconhecido;
            var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true };
            var names = Directory.EnumerateFileSystemEntries(installPath, "*", options).Take(4000).Select(p => Path.GetRelativePath(installPath, p));
            return DetectEngine(names);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return GameEngine.Desconhecido; }
    }

    /// <summary>Opções de inicialização do motor para o tipo de janela. Vazio = não precisa (Padrão); null = este motor não aceita por linha de comando.</summary>
    public static string? LaunchArgs(GameEngine engine, GameWindowMode mode)
    {
        if (mode == GameWindowMode.Padrao) return "";
        return engine switch
        {
            GameEngine.Unity => mode switch
            {
                GameWindowMode.TelaCheia => "-screen-fullscreen 1 -window-mode exclusive",
                GameWindowMode.SemBordas => "-screen-fullscreen 1 -window-mode borderless",
                _ => "-screen-fullscreen 0",
            },
            GameEngine.Source or GameEngine.Source2 => mode switch
            {
                GameWindowMode.TelaCheia => "-fullscreen",
                GameWindowMode.SemBordas => "-windowed -noborder",
                _ => "-windowed",
            },
            // Unreal: tela cheia e janela pela linha de comando; sem bordas só pelo GameUserSettings.ini (veja UnrealFullscreenMode)
            GameEngine.Unreal => mode switch
            {
                GameWindowMode.TelaCheia => "-fullscreen",
                GameWindowMode.Janela => "-windowed",
                _ => "",
            },
            _ => null,
        };
    }

    /// <summary>Valor do FullscreenMode do Unreal: 0 tela cheia, 1 janela sem bordas, 2 janela.</summary>
    public static int? UnrealFullscreenMode(GameWindowMode mode) => mode switch
    {
        GameWindowMode.TelaCheia => 0, GameWindowMode.SemBordas => 1, GameWindowMode.Janela => 2, _ => null,
    };

    /// <summary>Troca FullscreenMode, LastConfirmedFullscreenMode e PreferredFullscreenMode no texto do GameUserSettings.ini (cria as linhas se faltarem).</summary>
    public static string SetUnrealFullscreenMode(string ini, int mode)
    {
        var keys = new[] { "FullscreenMode", "LastConfirmedFullscreenMode", "PreferredFullscreenMode" };
        string nl = ini.Contains("\r\n") ? "\r\n" : "\n";
        var lines = ini.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        int section = lines.FindIndex(l => l.Trim().Equals("[/Script/Engine.GameUserSettings]", StringComparison.OrdinalIgnoreCase));
        if (section < 0)
        {
            if (lines.Count > 0 && lines[^1].Length > 0) lines.Add("");
            lines.Add("[/Script/Engine.GameUserSettings]");
            section = lines.Count - 1;
        }
        int end = lines.FindIndex(section + 1, l => l.TrimStart().StartsWith('['));
        if (end < 0) end = lines.Count;
        foreach (var key in keys)
        {
            int at = -1;
            for (int i = section + 1; i < end; i++)
                if (lines[i].TrimStart().StartsWith(key + "=", StringComparison.OrdinalIgnoreCase)) { at = i; break; }
            if (at >= 0) lines[at] = $"{key}={mode}";
            else { lines.Insert(end, $"{key}={mode}"); end++; }
        }
        return string.Join(nl, lines);
    }

    /// <summary>Onde o Unreal guarda o GameUserSettings.ini do projeto: %LocalAppData%\&lt;Projeto&gt;\Saved\Config\Windows (ou WindowsNoEditor).</summary>
    public static IEnumerable<string> UnrealIniCandidates(string localAppData, string installPath)
    {
        var projects = new List<string>();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(installPath))
                if (Directory.Exists(Path.Combine(dir, "Binaries")) && !Path.GetFileName(dir).Equals("Engine", StringComparison.OrdinalIgnoreCase))
                    projects.Add(Path.GetFileName(dir));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        foreach (var project in projects)
            foreach (var platform in new[] { "Windows", "WindowsNoEditor", "WinGDK" })
                yield return Path.Combine(localAppData, project, "Saved", "Config", platform, "GameUserSettings.ini");
    }

    // ------------------------------------------------------------ "Desativar otimizações de tela cheia" (compatibilidade do Windows)

    public const string LayersKey = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    public const string DisableFsoFlag = "DISABLEDXMAXIMIZEDWINDOWEDMODE";

    /// <summary>Liga ou desliga uma bandeira de compatibilidade no texto do Windows ("~ FLAG1 FLAG2"), mantendo as outras. Null = nada sobrou (apagar o valor).</summary>
    public static string? MergeLayers(string? existing, string flag, bool on)
    {
        var parts = (existing ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(p => p != "~").ToList();
        parts.RemoveAll(p => p.Equals(flag, StringComparison.OrdinalIgnoreCase));
        if (on) parts.Add(flag);
        return parts.Count == 0 ? null : "~ " + string.Join(' ', parts);
    }

    // ------------------------------------------------------------ como abrir

    /// <summary>Programa e argumentos para abrir o jogo com as opções. Steam: steam.exe -applaunch (não precisa mexer nas opções da Steam).</summary>
    public static (string File, string Arguments, string WorkingDirectory)? LaunchPlan(InstalledGame game, string? steamExe, string args, string? exePath)
    {
        if (string.IsNullOrWhiteSpace(args)) return null;
        if (game.Launcher == GameLauncher.Steam && game.SteamAppId is { } id && id.All(char.IsDigit) && steamExe is { Length: > 0 } && File.Exists(steamExe))
            return (steamExe, $"-applaunch {id} {args}", "");
        if (game.Launcher == GameLauncher.Manual && game.LaunchTarget is { } target && File.Exists(target))
            return (target, args, Path.GetDirectoryName(target) ?? "");
        if (exePath is { Length: > 0 } && File.Exists(exePath) && game.Launcher == GameLauncher.Gog)
            return (exePath, args, Path.GetDirectoryName(exePath) ?? "");
        return null;   // Epic e outros: as opções não passam pelo inicializador da loja
    }

    /// <summary>Opções de inicialização que o usuário escreveu: só caracteres comuns (nada de &amp;, |, &lt;, &gt;, aspas soltas).</summary>
    public static string CleanArgs(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var sb = new StringBuilder();
        foreach (char c in text.Trim())
            if (char.IsLetterOrDigit(c) || " -+_=.:,/\\".Contains(c)) sb.Append(c);
        string s = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        return s.Length > 300 ? s[..300] : s;
    }
}
