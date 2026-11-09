namespace MBExtreme.Core.Setup;

/// <summary>
/// Modo de atualização do instalador: o programa abre o instalador com "/atualizar /pasta=C:\...\MB Extreme" e ele instala por cima,
/// sem as telas de boas-vindas, espera a versão antiga fechar e abre o programa de novo no fim.
/// </summary>
public sealed record UpdateArgs(bool Update, string? Folder)
{
    public const string Flag = "/atualizar";

    public static UpdateArgs Parse(IEnumerable<string> args)
    {
        bool update = false;
        string? folder = null;
        foreach (var raw in args)
        {
            string a = raw.Trim();
            if (a.Equals(Flag, StringComparison.OrdinalIgnoreCase) || a.Equals("/update", StringComparison.OrdinalIgnoreCase)) update = true;
            else if (a.StartsWith("/pasta=", StringComparison.OrdinalIgnoreCase)) folder = a["/pasta=".Length..].Trim('"');
        }
        // só aceita pasta absoluta (nada de caminho relativo ou de rede estranho)
        if (folder is not null && !System.Text.RegularExpressions.Regex.IsMatch(folder, @"^[A-Za-z]:\\") ) folder = null;   // C:\... (sem rede nem relativo)
        if (folder is not null && folder.Contains("..", StringComparison.Ordinal)) folder = null;
        return new UpdateArgs(update, folder);
    }

    /// <summary>Argumentos que o programa passa ao instalador baixado.</summary>
    public static string Build(string folder) => $"{Flag} \"/pasta={folder.TrimEnd('\\')}\"";
}
