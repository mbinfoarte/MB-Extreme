using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using MBExtreme.Core.Windows;

namespace MBExtreme.Core.Apps;

public sealed record WingetApp(string Name, string Id, string Version, string Available);

public sealed record CatalogApp(string Id, string Title, string Description);

/// <summary>Atualiza e instala programas pelo winget, o instalador de pacotes da própria Microsoft.</summary>
[SupportedOSPlatform("windows")]
public static class WingetService
{
    private static readonly Regex SafeId = new(@"^[A-Za-z0-9][A-Za-z0-9.\-_+]*$");

    /// <summary>Programas comuns que dá para instalar com um clique.</summary>
    public static IReadOnlyList<CatalogApp> Catalog { get; } = new CatalogApp[]
    {
        new("Valve.Steam", "Steam", "Loja e biblioteca de jogos."),
        new("EpicGames.EpicGamesLauncher", "Epic Games Launcher", "Loja de jogos da Epic."),
        new("Discord.Discord", "Discord", "Voz e texto para jogar em grupo."),
        new("OBSProject.OBSStudio", "OBS Studio", "Gravação e transmissão de tela."),
        new("Google.Chrome", "Google Chrome", "Navegador."),
        new("Mozilla.Firefox", "Firefox", "Navegador."),
        new("VideoLAN.VLC", "VLC", "Player de vídeo e música."),
        new("7zip.7zip", "7-Zip", "Compactador de arquivos."),
        new("Notepad++.Notepad++", "Notepad++", "Editor de texto."),
        new("Microsoft.PowerToys", "PowerToys", "Utilitários da Microsoft para o Windows."),
        new("Guru3D.Afterburner", "MSI Afterburner", "Overclock e monitoramento de placa de vídeo."),
        new("CPUID.CPU-Z", "CPU-Z", "Informações detalhadas da CPU."),
        new("TechPowerUp.GPU-Z", "GPU-Z", "Informações detalhadas da placa de vídeo."),
        new("REALiX.HWiNFO", "HWiNFO", "Sensores e informações de hardware."),
        new("Intel.IntelDriverAndSupportAssistant", "Intel Driver & Support Assistant", "Atualiza drivers de componentes Intel."),
    };

    public static bool IsValidId(string id) => SafeId.IsMatch(id);

    public static bool IsAvailable()
    {
        try { return Shell.RunUtf8("winget.exe", "--version").ExitCode == 0; }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    /// <summary>O programa aparece na lista de instalados do winget?</summary>
    public static bool IsInstalled(string id)
    {
        if (!IsValidId(id)) return false;
        try
        {
            var result = Shell.RunUtf8("winget.exe", "list", "--id", id, "-e", "--accept-source-agreements", "--disable-interactivity");
            return result.ExitCode == 0 && result.Output.Contains(id, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public static IReadOnlyList<WingetApp> Upgradable()
    {
        var result = Shell.RunUtf8("winget.exe", "upgrade", "--accept-source-agreements", "--disable-interactivity");
        return ParseUpgradeTable(result.Output);
    }

    /// <summary>
    /// Lê a tabela do "winget upgrade". As colunas são descobertas pela posição das palavras do cabeçalho,
    /// por isso funciona em qualquer idioma. Termina na linha em branco ou no resumo.
    /// </summary>
    public static IReadOnlyList<WingetApp> ParseUpgradeTable(string output)
    {
        var lines = output.Replace("\r", "").Split('\n');
        int separator = Array.FindIndex(lines, l => l.Length >= 10 && l.All(c => c == '-'));
        if (separator < 1) return Array.Empty<WingetApp>();

        string header = lines[separator - 1];
        var starts = Regex.Matches(header, @"\S+").Select(m => m.Index).ToList();
        if (starts.Count < 4) return Array.Empty<WingetApp>();

        string Cut(string line, int column)
        {
            int from = Math.Min(starts[column], line.Length);
            int to = column + 1 < starts.Count ? Math.Min(starts[column + 1], line.Length) : line.Length;
            return line[from..to].Trim();
        }

        var apps = new List<WingetApp>();
        for (int i = separator + 1; i < lines.Length; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || char.IsDigit(line.TrimStart()[0]) && line.Contains(' ') && !line.Contains("  ")) break;
            if (line.Length < starts[1]) continue;

            string id = Cut(line, 1);
            if (!IsValidId(id)) continue;
            apps.Add(new WingetApp(Cut(line, 0), id, Cut(line, 2), Cut(line, 3)));
        }

        return apps;
    }

    private static (bool Ok, string Message) Run(string verb, string id)
    {
        if (!IsValidId(id)) throw new ArgumentException("Identificador de programa inválido.", nameof(id));
        var result = Shell.RunUtf8("winget.exe", verb, "--id", id, "-e", "--silent",
            "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity");
        string tail = string.Join(" ", result.Output.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).TakeLast(2));
        return (result.ExitCode == 0, tail);
    }

    public static (bool Ok, string Message) Upgrade(string id) => Run("upgrade", id);

    public static (bool Ok, string Message) Install(string id) => Run("install", id);
}
