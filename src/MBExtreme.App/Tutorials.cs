using System.Diagnostics;

namespace MBExtreme.UI;

/// <summary>Botão "?" de cada tela: abre a aba Tutoriais do site já na parte daquela tela (pelo módulo do menu).</summary>
internal static class Tutorials
{
    /// <summary>Módulo do menu → seção da página de tutoriais do site.</summary>
    private static readonly Dictionary<string, string> Sections = new(StringComparer.Ordinal)
    {
        ["home"] = "painel", ["hub-otimizar"] = "otimizar", ["hub-disks"] = "otimizar", ["hub-drivers"] = "otimizar", ["hub-memoria"] = "otimizar", ["hub-control"] = "controle", ["record"] = "gravacao", ["shorts"] = "gravacao",
        ["bench"] = "jogos", ["gamelibrary"] = "jogos", ["hub-fps"] = "jogos", ["hub-meupc"] = "meu-pc", ["programs"] = "programas",
        ["hub-format"] = "formatacao", ["hub-seguranca"] = "seguranca", ["support"] = "suporte", ["remote"] = "suporte", ["service"] = "suporte", ["admin"] = "suporte", ["admin-live"] = "suporte", ["admin-site"] = "suporte", ["netcenter"] = "otimizar",
        ["hub-perif"] = "perifericos", ["hub-personal"] = "personalizacao", ["license"] = "conta",
    };

    public static string SectionOf(string? key)
    {
        if (key is null) return "";
        if (Sections.TryGetValue(key, out var s)) return s;
        // tela dentro de um módulo de abas (ex.: "cleanup" está em Otimizar › Limpeza)
        foreach (var (hub, tabs) in NavigationMap.Hubs)
            if (tabs.Any(t => t.Key == key)) return SectionOf(hub);
        return "";
    }

    public static void Open(string? key)
    {
        string section = SectionOf(key);
        string url = MBExtreme.Core.AppInfo.SiteUrl + "/tutoriais" + (section.Length > 0 ? "#" + section : "");
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { /* sem navegador padrão */ }
    }
}
