using MBExtreme.Core.Progress;

namespace MBExtreme.Core.Cleanup;

/// <summary>Um grupo de arquivos que pode ser limpo sem afetar o uso do Windows.</summary>
public sealed record CleanupCategory(
    string Id,
    string Title,
    string Description,
    IReadOnlyList<string> Roots,
    TimeSpan? MinAge = null,
    bool DefaultSelected = true,
    string Note = "",
    string FilePattern = "*");

public sealed record CleanupItem(CleanupCategory Category, long Bytes, int Files);

public sealed record CleanupResult(long FreedBytes, int FilesDeleted, int FilesSkipped);

/// <summary>Procura e apaga arquivos descartáveis. Só mexe nas pastas do catálogo e ignora links (junções).</summary>
public static class CleanupService
{
    private static readonly EnumerationOptions Recursive = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false,
    };

    /// <summary>Pastas que o usuário pediu para nunca limpar (valem para todas as categorias).</summary>
    public static IReadOnlyList<string> Excluded { get; set; } = Array.Empty<string>();

    /// <summary>O arquivo está dentro de uma pasta excluída?</summary>
    public static bool IsExcluded(string path, IReadOnlyList<string> excluded)
    {
        string file = path.Replace('/', '\\');
        foreach (string folder in excluded)
        {
            if (string.IsNullOrWhiteSpace(folder)) continue;
            string root = folder.Replace('/', '\\').TrimEnd('\\');
            if (file.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase) || string.Equals(file, root, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>Os maiores arquivos que a limpeza apagaria nesta categoria (para ver antes de limpar).</summary>
    public static IReadOnlyList<(string Path, long Bytes, DateTime Modified)> Preview(CleanupCategory category, DateTime nowUtc, int max = 300)
    {
        var list = new List<(string, long, DateTime)>();
        foreach (var file in Targets(category, nowUtc))
        {
            try { list.Add((file.FullName, file.Length, file.LastWriteTime)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return list.OrderByDescending(f => f.Item2).Take(max).ToList();
    }

    private static IEnumerable<FileInfo> Targets(CleanupCategory category, DateTime nowUtc)
    {
        var excluded = Excluded;
        foreach (var root in category.Roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;

            IEnumerable<FileInfo> files;
            try
            {
                files = new DirectoryInfo(root).EnumerateFiles(category.FilePattern, Recursive);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (category.MinAge is TimeSpan age && nowUtc - file.LastWriteTimeUtc < age) continue;
                if (excluded.Count > 0 && IsExcluded(file.FullName, excluded)) continue;
                yield return file;
            }
        }
    }

    public static CleanupItem Scan(CleanupCategory category, DateTime nowUtc)
    {
        long bytes = 0;
        int count = 0;
        foreach (var file in Targets(category, nowUtc))
        {
            try
            {
                bytes += file.Length;
                count++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        return new CleanupItem(category, bytes, count);
    }

    public static IReadOnlyList<CleanupItem> ScanAll(
        IReadOnlyList<CleanupCategory> categories,
        IProgress<ProgressEvent>? progress = null,
        CancellationToken ct = default)
    {
        var items = new List<CleanupItem>();
        for (int i = 0; i < categories.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new ProgressEvent("Procurar", 100.0 * i / categories.Count, "Analisando " + categories[i].Title));
            items.Add(Scan(categories[i], DateTime.UtcNow));
        }

        progress?.Report(new ProgressEvent("Procurar", 100, "Análise concluída"));
        return items;
    }

    public static CleanupResult Clean(CleanupCategory category, DateTime nowUtc, CancellationToken ct = default)
    {
        long freed = 0;
        int deleted = 0, skipped = 0;

        foreach (var file in Targets(category, nowUtc).ToList())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                long length = file.Length;
                file.Delete();
                freed += length;
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped++; // em uso ou protegido: deixar
            }
        }

        RemoveEmptyDirectories(category);
        return new CleanupResult(freed, deleted, skipped);
    }

    /// <summary>Apaga pastas vazias dentro das raízes (nunca a própria raiz), das mais profundas para as mais rasas.</summary>
    private static void RemoveEmptyDirectories(CleanupCategory category)
    {
        foreach (var root in category.Roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
            try
            {
                var directories = new DirectoryInfo(root)
                    .EnumerateDirectories("*", Recursive)
                    .OrderByDescending(d => d.FullName.Length)
                    .ToList();
                foreach (var directory in directories)
                {
                    try { directory.Delete(false); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
