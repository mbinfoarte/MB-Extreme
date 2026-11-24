using System.Text.Json;

namespace MBExtreme.Core.Apps;

/// <summary>Tamanho e data de um programa: medidos na pasta dele, ou o valor do Windows ("~") quando não dá para medir.</summary>
public sealed record ProgramMeasure(long? Bytes, bool Estimated, DateTime? InstalledOn, bool DateFromFolder, DateTime MeasuredAt);

/// <summary>
/// Mede quanto cada programa ocupa de verdade (somando os arquivos da pasta dele) e acha a data de instalação quando o
/// programa não informou (data da pasta). Guarda as medições para a tela abrir já preenchida da próxima vez.
/// </summary>
public static class ProgramSizes
{
    private static readonly string[] SystemNames = { "windows", "system32", "syswow64", "program files", "program files (x86)", "programdata", "users", "common files", "appdata", "local", "roaming", "programs", "microsoft", "windowsapps", "installer", "package cache" };

    /// <summary>A pasta do programa: a de instalação, senão a do desinstalador, senão a do ícone (nunca uma pasta do sistema).</summary>
    public static string? FolderOf(InstalledProgram program)
    {
        foreach (string? candidate in new[]
                 {
                     program.InstallLocation,
                     InstalledPrograms.SplitCommand(program.UninstallCommand) is { } c ? Path.GetDirectoryName(c.File) : null,
                     program.IconPath is { } icon ? Path.GetDirectoryName(icon) : null,
                 })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            string path = Environment.ExpandEnvironmentVariables(candidate.Trim().Trim('"')).TrimEnd('\\', '/');
            if (IsProgramFolder(path) && Directory.Exists(path)) return path;
        }
        return null;
    }

    /// <summary>Letra do disco de um caminho ("D:\Jogos\Steam" → 'D'), ou null (caminho de rede, vazio ou sem letra).</summary>
    public static char? DriveLetter(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string p = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        return p.Length >= 2 && p[1] == ':' && char.IsAsciiLetter(p[0]) ? char.ToUpperInvariant(p[0]) : null;
    }

    /// <summary>
    /// Onde o programa está: a pasta medida (quando existe) ou, se o programa não deixou pasta acessível, o primeiro caminho que ele
    /// informa (pasta de instalação, desinstalador ou ícone). Programas da Store ficam no disco da pasta deles. Null = local desconhecido.
    /// </summary>
    public static (char? Drive, string? Folder) LocationOf(InstalledProgram program, Func<InstalledProgram, string?>? folderOf = null)
    {
        string? folder = program.Source == ProgramSource.Store ? program.InstallLocation : (folderOf ?? FolderOf)(program);
        if (folder is not null) return (DriveLetter(folder), folder);
        foreach (string? candidate in new[] { program.InstallLocation, InstalledPrograms.SplitCommand(program.UninstallCommand)?.File, program.IconPath })
            if (DriveLetter(candidate) is { } d) return (d, null);
        return (null, null);
    }

    /// <summary>Serve como pasta de um programa? Raiz de disco e pastas do sistema (Windows, Program Files, AppData...) não.</summary>
    public static bool IsProgramFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        string trimmed = path.Replace('/', '\\').TrimEnd('\\');
        var parts = trimmed.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;   // "C:" ou "C:\"
        if (parts[1].Equals("Windows", StringComparison.OrdinalIgnoreCase)) return false;
        string last = parts[^1].ToLowerInvariant();
        if (SystemNames.Contains(last)) return false;
        if (parts.Length == 3 && parts[1].Equals("Users", StringComparison.OrdinalIgnoreCase)) return false;   // C:\Users\fulano
        return true;
    }

    /// <summary>Soma os arquivos da pasta (sem seguir atalhos de pasta, pulando o que não dá para ler).</summary>
    public static long Measure(string folder, CancellationToken ct = default)
    {
        long total = 0;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        try
        {
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", options))
            {
                ct.ThrowIfCancellationRequested();
                try { total += file.Length; }
                catch (IOException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        return total;
    }

    /// <summary>Mede um programa: pasta medida quando dá; senão o tamanho do Windows marcado como estimado.</summary>
    public static ProgramMeasure MeasureProgram(InstalledProgram program, CancellationToken ct = default)
    {
        string? folder = program.Source == ProgramSource.Store ? program.InstallLocation : FolderOf(program);
        long? bytes = null;
        DateTime? folderDate = null;
        if (folder is not null && Directory.Exists(folder))
        {
            long measured = Measure(folder, ct);
            if (measured > 0) bytes = measured;
            try { folderDate = Directory.GetCreationTime(folder); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        bool estimated = bytes is null;
        bytes ??= program.SizeBytes;
        var date = program.InstalledOn ?? folderDate;
        return new ProgramMeasure(bytes, estimated && bytes is not null, date, program.InstalledOn is null && folderDate is not null, DateTime.Now);
    }

    public static string KeyOf(InstalledProgram p) => p.Name + "|" + p.Version;

    // ------------------------------------------------------------------ medições guardadas

    public static string CacheFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "tamanhos.json");

    public static Dictionary<string, ProgramMeasure> Load(string? file = null)
    {
        try
        {
            string path = file ?? CacheFile;
            if (!File.Exists(path)) return new(StringComparer.OrdinalIgnoreCase);
            return JsonSerializer.Deserialize<Dictionary<string, ProgramMeasure>>(File.ReadAllText(path)) is { } map
                ? new Dictionary<string, ProgramMeasure>(map, StringComparer.OrdinalIgnoreCase) : new(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(StringComparer.OrdinalIgnoreCase); }
    }

    public static void Save(IReadOnlyDictionary<string, ProgramMeasure> map, string? file = null)
    {
        try
        {
            string path = file ?? CacheFile;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(map));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
