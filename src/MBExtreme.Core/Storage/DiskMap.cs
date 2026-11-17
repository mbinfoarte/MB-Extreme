namespace MBExtreme.Core.Storage;

/// <summary>Uma pasta (ou arquivo) no mapa do disco, com o tamanho de tudo que tem dentro.</summary>
public sealed class MapNode
{
    public MapNode(string name, string path, bool isFile)
    {
        Name = name;
        Path = path;
        IsFile = isFile;
    }

    public string Name { get; }
    public string Path { get; }
    public bool IsFile { get; }
    public long Size { get; set; }
    public int Files { get; set; }
    public bool Denied { get; set; }
    public List<MapNode> Children { get; } = new();

    /// <summary>Tipo mais comum (por tamanho) do que tem dentro.</summary>
    public Dictionary<FileKind, long> Kinds { get; } = new();

    public FileKind MainKind => IsFile ? DiskMap.KindOf(Name) : Kinds.Count == 0 ? FileKind.Other : Kinds.MaxBy(k => k.Value).Key;
}

public enum FileKind { Video, Image, Music, Document, Archive, Program, System, Other }

/// <summary>Retângulo do mapa (x, y, largura, altura) de um item.</summary>
public readonly record struct MapRect(double X, double Y, double W, double H);

/// <summary>
/// Mapa do disco: soma o tamanho de cada pasta (sem seguir atalhos de pasta, para não contar duas vezes) e desenha com o
/// "mapa de árvore quadrado" (squarified treemap): retângulos proporcionais ao tamanho, o mais quadrados possível. Só lê.
/// </summary>
public static class DiskMap
{
    /// <summary>Quantos arquivos soltos cada pasta mostra no mapa; o resto vira "outros arquivos".</summary>
    public const int FilesShown = 12;

    public static FileKind KindOf(string fileName) => System.IO.Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".webm" or ".flv" or ".m4v" or ".ts" => FileKind.Video,
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".heic" or ".raw" or ".cr2" or ".nef" or ".psd" or ".tif" or ".tiff" => FileKind.Image,
        ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".m4a" or ".wma" => FileKind.Music,
        ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".txt" or ".odt" or ".csv" or ".rtf" => FileKind.Document,
        ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".iso" or ".cab" or ".pak" or ".vpk" => FileKind.Archive,
        ".exe" or ".dll" or ".msi" or ".sys" or ".bin" or ".dat" or ".jar" or ".so" => FileKind.Program,
        ".log" or ".etl" or ".evtx" or ".tmp" => FileKind.System,
        _ => FileKind.Other,
    };

    public static string KindName(FileKind kind) => kind switch
    {
        FileKind.Video => "Vídeos", FileKind.Image => "Imagens", FileKind.Music => "Músicas", FileKind.Document => "Documentos",
        FileKind.Archive => "Compactados", FileKind.Program => "Programas e jogos", FileKind.System => "Windows e sistema", _ => "Outros",
    };

    /// <summary>Lê a pasta inteira. Pastas sem acesso entram com o que deu para ler e ficam marcadas.</summary>
    public static MapNode Scan(string root, IProgress<string>? progress, CancellationToken ct)
    {
        var node = new MapNode(System.IO.Path.GetFileName(root.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : root, root, false);
        int counter = 0;
        Fill(node, progress, ref counter, ct);
        return node;
    }

    private static void Fill(MapNode node, IProgress<string>? progress, ref int counter, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var dir = new DirectoryInfo(node.Path);
        var files = new List<MapNode>();
        try
        {
            foreach (var entry in dir.EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0, RecurseSubdirectories = false }))
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;   // atalho de pasta: o conteúdo já é contado no lugar original
                if (entry is FileInfo file)
                {
                    long size;
                    try { size = file.Length; } catch (IOException) { continue; }
                    var f = new MapNode(file.Name, file.FullName, true) { Size = size, Files = 1 };
                    files.Add(f);
                    node.Size += size;
                    node.Files++;
                    var kind = KindOf(file.Name);
                    node.Kinds[kind] = node.Kinds.GetValueOrDefault(kind) + size;
                }
                else if (entry is DirectoryInfo sub)
                {
                    var child = new MapNode(sub.Name, sub.FullName, false);
                    Fill(child, progress, ref counter, ct);
                    node.Children.Add(child);
                    node.Size += child.Size;
                    node.Files += child.Files;
                    if (child.Denied) node.Denied = true;
                    foreach (var (k, v) in child.Kinds) node.Kinds[k] = node.Kinds.GetValueOrDefault(k) + v;
                }
                if (++counter % 2000 == 0) progress?.Report(node.Path);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            node.Denied = true;
        }
        AddFiles(node, files);
        node.Children.Sort((a, b) => b.Size.CompareTo(a.Size));
    }

    /// <summary>Os maiores arquivos soltos entram no mapa; os outros viram um bloco só.</summary>
    public static void AddFiles(MapNode node, List<MapNode> files)
    {
        files.Sort((a, b) => b.Size.CompareTo(a.Size));
        node.Children.AddRange(files.Take(FilesShown));
        var rest = files.Skip(FilesShown).ToList();
        if (rest.Count > 0)
            node.Children.Add(new MapNode($"{rest.Count} outros arquivos", node.Path, true) { Size = rest.Sum(r => r.Size), Files = rest.Count });
    }

    /// <summary>
    /// Divide o retângulo em pedaços proporcionais aos tamanhos (do maior para o menor), em faixas, escolhendo quantos itens
    /// cabem em cada faixa para deixar os retângulos o mais quadrados possível (Bruls, Huizing e van Wijk). Tamanhos zero ficam com área zero.
    /// </summary>
    public static IReadOnlyList<MapRect> Layout(IReadOnlyList<long> sizes, MapRect area)
    {
        var result = new MapRect[sizes.Count];
        double total = sizes.Sum(s => (double)Math.Max(0, s));
        if (sizes.Count == 0 || total <= 0 || area.W <= 0 || area.H <= 0) return result;
        var order = Enumerable.Range(0, sizes.Count).OrderByDescending(i => sizes[i]).ToList();
        double scale = area.W * area.H / total;
        var areas = order.Select(i => Math.Max(0, sizes[i]) * scale).ToList();
        double x = area.X, y = area.Y, w = area.W, h = area.H;
        int start = 0;
        while (start < order.Count)
        {
            double side = Math.Min(w, h);
            int end = start + 1;
            double rowSum = areas[start];
            double worst = Worst(areas, start, end, rowSum, side);
            while (end < order.Count)
            {
                double nextSum = rowSum + areas[end];
                double nextWorst = Worst(areas, start, end + 1, nextSum, side);
                if (nextWorst > worst) break;
                rowSum = nextSum;
                worst = nextWorst;
                end++;
            }
            // assenta a faixa no lado menor
            double thickness = side > 0 ? rowSum / side : 0;
            double offset = 0;
            for (int k = start; k < end; k++)
            {
                double length = thickness > 0 ? areas[k] / thickness : 0;
                result[order[k]] = w >= h
                    ? new MapRect(x, y + offset, thickness, length)
                    : new MapRect(x + offset, y, length, thickness);
                offset += length;
            }
            if (w >= h) { x += thickness; w -= thickness; }
            else { y += thickness; h -= thickness; }
            start = end;
        }
        return result;
    }

    private static double Worst(List<double> areas, int start, int end, double sum, double side)
    {
        if (sum <= 0 || side <= 0) return 0;
        double max = 0, min = double.MaxValue;
        for (int k = start; k < end; k++) { max = Math.Max(max, areas[k]); min = Math.Min(min, areas[k]); }
        if (min <= 0) return double.MaxValue;
        double s2 = side * side, sum2 = sum * sum;
        return Math.Max(s2 * max / sum2, sum2 / (s2 * min));
    }
}
