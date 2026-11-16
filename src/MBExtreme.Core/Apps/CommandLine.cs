namespace MBExtreme.Core.Apps;

/// <summary>Leitura de linhas de comando guardadas no registro (inicialização, desinstaladores, App Paths).</summary>
public static class CommandLine
{
    /// <summary>
    /// O .exe de um comando: "\"C:\\App\\a.exe\" --x" → C:\App\a.exe; "C:\\App\\a.exe /min" → C:\App\a.exe; com %variáveis% expandidas.
    /// Null quando não dá para saber.
    /// </summary>
    public static string? ExePath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        string c = Environment.ExpandEnvironmentVariables(command.Trim());
        if (c.StartsWith('"'))
        {
            int end = c.IndexOf('"', 1);
            return end > 1 ? c[1..end] : null;
        }
        int exe = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? c[..(exe + 4)] : null;
    }

    /// <summary>Nome do arquivo de um caminho do Windows (funciona com \ em qualquer sistema).</summary>
    public static string FileName(string path)
    {
        int i = path.LastIndexOfAny(new[] { '\\', '/' });
        return i < 0 ? path : path[(i + 1)..];
    }

    /// <summary>Pasta de um caminho do Windows ("" quando não tem).</summary>
    public static string Folder(string path)
    {
        int i = path.LastIndexOfAny(new[] { '\\', '/' });
        return i <= 0 ? "" : path[..i];
    }
}
