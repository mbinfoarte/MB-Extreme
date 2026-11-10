using System.Runtime.Versioning;
using Microsoft.Win32;

namespace MBExtreme.Core.Windows;

/// <summary>Uma ação do MB Extreme que pode ser chamada de fora (botão direito, painel da bandeja): MBExtreme.exe --acao chave [caminho].</summary>
public sealed record ShellAction(string Key, string Title, string Page, string Where, bool AutoStart = false);

/// <summary>
/// Itens "MB Extreme" no botão direito do Windows: na área de trabalho, em pastas e em programas (.exe). Ficam só no registro do
/// usuário (HKCU\Software\Classes), sem precisar de administrador, e somem por completo ao desligar.
/// No Windows 11 aparecem em "Mostrar mais opções" (o menu novo só aceita apps empacotados).
/// </summary>
public static class ShellActions
{
    public const string Argument = "--acao";
    public const string Desktop = "desktop", Folder = "folder", Exe = "exe";

    public static IReadOnlyList<ShellAction> All { get; } = new[]
    {
        new ShellAction("otimizar", "Otimizar agora", "home", Desktop, AutoStart: true),
        new ShellAction("limpeza", "Limpeza rápida", "cleanup", Desktop, AutoStart: true),
        new ShellAction("modojogo", "Modo jogo (liga/desliga)", "", Desktop),
        new ShellAction("wallpaper", "Trocar papel de parede MB", "wallpaper", Desktop),
        new ShellAction("reparar", "Reparar o Windows", "repair", Desktop),
        new ShellAction("restauracao", "Criar ponto de restauração", "restorepoints", Desktop),
        new ShellAction("suporte", "Suporte remoto MB InfoArt", "support", Desktop),
        new ShellAction("mapa", "Ver o que ocupa espaço (Mapa do disco)", "diskmap", Folder),
        new ShellAction("duplicados", "Procurar arquivos duplicados", "duplicates", Folder),
        new ShellAction("desinstalar", "Desinstalar com limpeza (MB)", "uninstaller", Exe),
        new ShellAction("fps", "Medir o FPS deste jogo", "gamelibrary", Exe),
    };

    public static ShellAction? Find(string? key) => All.FirstOrDefault(a => a.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Lê os argumentos do programa: --acao chave [caminho]. Null se não for uma ação conhecida.</summary>
    public static (ShellAction Action, string? Path)? Parse(IReadOnlyList<string> args)
    {
        int i = args.ToList().FindIndex(a => a.Equals(Argument, StringComparison.OrdinalIgnoreCase));
        if (i < 0 || i + 1 >= args.Count || Find(args[i + 1]) is not { } action) return null;
        string? path = i + 2 < args.Count && args[i + 2].Length > 0 ? args[i + 2] : null;
        return (action, path);
    }

    /// <summary>Onde cada grupo vai no registro (dentro de HKCU\Software\Classes).</summary>
    public static string RootFor(string where) => where switch
    {
        Desktop => @"DesktopBackground\Shell\MBExtreme",
        Folder => @"Directory\shell\MBExtreme",
        _ => @"SystemFileAssociations\.exe\shell\MBExtreme",
    };

    /// <summary>Valores a gravar: (subchave, nome do valor, valor). Nome vazio = valor padrão.</summary>
    public static IReadOnlyList<(string Key, string Name, string Value)> RegistryPlan(string exePath)
    {
        var plan = new List<(string, string, string)>();
        string exe = exePath.Replace("\"", "");
        foreach (var group in All.GroupBy(a => a.Where))
        {
            string root = RootFor(group.Key);
            plan.Add((root, "MUIVerb", "MB Extreme"));
            plan.Add((root, "Icon", $"\"{exe}\",0"));
            plan.Add((root, "SubCommands", ""));
            int n = 1;
            foreach (var action in group)
            {
                string item = $@"{root}\shell\{n++:00}{action.Key}";
                plan.Add((item, "MUIVerb", action.Title));
                string target = group.Key == Desktop ? "" : " \"%1\"";
                plan.Add((item + @"\command", "", $"\"{exe}\" {Argument} {action.Key}{target}"));
            }
        }
        return plan;
    }

    [SupportedOSPlatform("windows")]
    public static bool IsInstalled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + RootFor(Desktop));
        return key is not null;
    }

    /// <summary>Grava os itens no registro do usuário (refaz do zero para atualizar o caminho do programa).</summary>
    [SupportedOSPlatform("windows")]
    public static void Install(string exePath)
    {
        Uninstall();
        foreach (var (key, name, value) in RegistryPlan(exePath))
        {
            using var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + key, writable: true);
            k.SetValue(name, value, RegistryValueKind.String);
        }
    }

    [SupportedOSPlatform("windows")]
    public static void Uninstall()
    {
        foreach (var where in new[] { Desktop, Folder, Exe })
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + RootFor(where), throwOnMissingSubKey: false);
    }
}
