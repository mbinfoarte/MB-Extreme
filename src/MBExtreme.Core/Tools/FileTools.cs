using System.Security.Cryptography;

namespace MBExtreme.Core.Tools;

public sealed record FoundFile(string Path, long Length, DateTime Modified);

public sealed record DuplicateGroup(long Length, IReadOnlyList<FoundFile> Files)
{
    /// <summary>Espaço que se recupera guardando só uma cópia.</summary>
    public long WastedBytes => Length * (Files.Count - 1);
}

/// <summary>Impede que as ferramentas de arquivos toquem em pastas do Windows e dos programas.</summary>
public static class PathGuard
{
    public static IReadOnlyList<string> ProtectedRoots()
    {
        var list = new List<string>();
        foreach (var folder in new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.System })
        {
            string path = Environment.GetFolderPath(folder);
            if (path.Length > 0) list.Add(path);
        }
        string data = Environment.GetEnvironmentVariable("ProgramData") ?? "";
        if (data.Length > 0) list.Add(data);
        return list;
    }

    /// <summary>Verdadeiro para a pasta do Windows, Program Files, ProgramData, a raiz de um disco ou qualquer coisa dentro deles.</summary>
    public static bool IsProtected(string path, IReadOnlyList<string>? roots = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;
        string full;
        try { full = System.IO.Path.GetFullPath(path).TrimEnd('\\', '/'); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return true; }

        string? root = System.IO.Path.GetPathRoot(full)?.TrimEnd('\\', '/');
        if (full.Length == 0 || string.Equals(full, root, StringComparison.OrdinalIgnoreCase)) return true;

        foreach (string protectedRoot in roots ?? ProtectedRoots())
        {
            string p = protectedRoot.TrimEnd('\\', '/');
            if (p.Length == 0) continue;
            if (full.Equals(p, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(p + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(p + System.IO.Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }
}

/// <summary>Procura de arquivos grandes, duplicados e pastas vazias. Só lê; apagar é um passo separado e confirmado pelo usuário.</summary>
public static class FileSearch
{
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
        ReturnSpecialDirectories = false,
    };

    private static IEnumerable<string> Files(string root, IReadOnlyList<string> skip)
    {
        foreach (string file in Directory.EnumerateFiles(root, "*", Options))
        {
            if (PathGuard.IsProtected(file, skip)) continue;
            yield return file;
        }
    }

    public static IReadOnlyList<FoundFile> Largest(string root, long minBytes, int max, CancellationToken token,
        Action<long>? onScanned = null, IReadOnlyList<string>? skip = null)
    {
        skip ??= PathGuard.ProtectedRoots();
        var found = new List<FoundFile>();
        long scanned = 0;
        foreach (string path in Files(root, skip))
        {
            token.ThrowIfCancellationRequested();
            if (++scanned % 500 == 0) onScanned?.Invoke(scanned);
            try
            {
                var info = new FileInfo(path);
                if (info.Length >= minBytes) found.Add(new FoundFile(path, info.Length, info.LastWriteTime));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        onScanned?.Invoke(scanned);
        return found.OrderByDescending(f => f.Length).Take(max).ToList();
    }

    /// <summary>Agrupa por tamanho, depois por um trecho do início e por fim confere o conteúdo inteiro (SHA-256).</summary>
    public static IReadOnlyList<DuplicateGroup> Duplicates(IEnumerable<string> roots, long minBytes, CancellationToken token,
        Action<string>? onStep = null, IReadOnlyList<string>? skip = null)
    {
        skip ??= PathGuard.ProtectedRoots();
        var bySize = new Dictionary<long, List<FoundFile>>();
        onStep?.Invoke("Listando arquivos");
        foreach (string root in roots.Where(Directory.Exists))
        {
            foreach (string path in Files(root, skip))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var info = new FileInfo(path);
                    if (info.Length < minBytes) continue;
                    if (!bySize.TryGetValue(info.Length, out var list)) bySize[info.Length] = list = new List<FoundFile>();
                    list.Add(new FoundFile(path, info.Length, info.LastWriteTime));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }

        var groups = new List<DuplicateGroup>();
        var candidates = bySize.Where(kv => kv.Value.Count > 1).ToList();
        int done = 0;
        foreach (var (length, files) in candidates)
        {
            token.ThrowIfCancellationRequested();
            onStep?.Invoke($"Comparando arquivos ({++done} de {candidates.Count} tamanhos)");
            foreach (var group in files.GroupBy(f => HashOf(f.Path, 64 * 1024)).Where(g => g.Key is not null && g.Count() > 1))
                foreach (var full in group.GroupBy(f => HashOf(f.Path, long.MaxValue)).Where(g => g.Key is not null && g.Count() > 1))
                    groups.Add(new DuplicateGroup(length, full.OrderBy(f => f.Modified).ToList()));
        }

        return groups.OrderByDescending(g => g.WastedBytes).ToList();
    }

    private static string? HashOf(string path, long limit)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920);
            using var sha = SHA256.Create();
            if (limit == long.MaxValue) return Convert.ToHexString(sha.ComputeHash(stream));
            var buffer = new byte[(int)Math.Min(limit, stream.Length)];
            int read = stream.Read(buffer, 0, buffer.Length);
            return Convert.ToHexString(sha.ComputeHash(buffer, 0, read));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Pastas sem nenhum arquivo dentro (nem em subpastas). Devolve primeiro as mais profundas.</summary>
    public static IReadOnlyList<string> EmptyFolders(string root, CancellationToken token, IReadOnlyList<string>? skip = null)
    {
        skip ??= PathGuard.ProtectedRoots();
        var empty = new List<string>();
        bool Walk(string dir)
        {
            token.ThrowIfCancellationRequested();
            bool hasFiles;
            try { hasFiles = Directory.EnumerateFiles(dir).Any(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }

            bool anyChildKept = false;
            try
            {
                foreach (string child in Directory.EnumerateDirectories(dir))
                {
                    var attrs = File.GetAttributes(child);
                    if ((attrs & (FileAttributes.ReparsePoint | FileAttributes.System)) != 0 || PathGuard.IsProtected(child, skip)) { anyChildKept = true; continue; }
                    if (Walk(child)) anyChildKept = true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }

            bool keep = hasFiles || anyChildKept;
            if (!keep && !string.Equals(dir, root, StringComparison.OrdinalIgnoreCase)) empty.Add(dir);
            return keep;
        }

        Walk(root);
        return empty;
    }
}

/// <summary>Apagamento definitivo: sobrescreve o conteúdo antes de apagar. Em SSD e em discos com cópias de segurança o efeito não é garantido.</summary>
public static class Shredder
{
    public static void Shred(string path, int passes = 1)
    {
        if (PathGuard.IsProtected(path)) throw new InvalidOperationException("Este arquivo está em uma pasta protegida do Windows.");
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("Arquivo não encontrado.", path);

        info.Attributes = FileAttributes.Normal;
        long length = info.Length;
        var buffer = new byte[1024 * 1024];
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None, buffer.Length, FileOptions.WriteThrough))
        {
            for (int pass = 0; pass < Math.Max(1, passes); pass++)
            {
                stream.Position = 0;
                long remaining = length;
                while (remaining > 0)
                {
                    RandomNumberGenerator.Fill(buffer);
                    int count = (int)Math.Min(buffer.Length, remaining);
                    stream.Write(buffer, 0, count);
                    remaining -= count;
                }
                stream.Flush(true);
            }
        }

        string renamed = System.IO.Path.Combine(info.DirectoryName!, Guid.NewGuid().ToString("N"));
        File.Move(path, renamed);
        File.Delete(renamed);
    }
}

/// <summary>Programa o desligamento ou o reinício do Windows (shutdown.exe).</summary>
public static class PowerSchedule
{
    public const int MaxSeconds = 24 * 3600;

    public static IReadOnlyList<string> ShutdownArguments(bool restart, int seconds)
    {
        if (seconds < 0 || seconds > MaxSeconds) throw new ArgumentOutOfRangeException(nameof(seconds));
        return new[] { restart ? "/r" : "/s", "/t", seconds.ToString(), "/c", "Programado pelo MB Extreme" };
    }

    public static IReadOnlyList<string> CancelArguments => new[] { "/a" };
}
