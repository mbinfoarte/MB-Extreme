using System.Diagnostics;
using System.Text;

namespace MBExtreme.Core.Recording;

public sealed record SegmentFile(string Path, DateTime WrittenUtc);

/// <summary>Replay (melhores momentos): guarda os últimos segundos em pedaços curtos e junta os mais recentes em um clipe quando você pedir.</summary>
public static class ReplayBuffer
{
    public static string ClipsFolder(string recordingsFolder) => Path.Combine(recordingsFolder, "Clipes");

    /// <summary>
    /// Escolhe os pedaços que formam os últimos <paramref name="seconds"/> segundos. O mais recente está sendo gravado e fica de fora;
    /// um intervalo grande entre dois pedaços indica sobra de uma sessão antiga e corta a lista ali.
    /// </summary>
    public static IReadOnlyList<SegmentFile> SelectSegments(IEnumerable<SegmentFile> files, int seconds, int segmentSeconds = StudioCommand.SegmentSeconds)
    {
        var newestFirst = files.OrderByDescending(f => f.WrittenUtc).ToList();
        if (newestFirst.Count <= 1) return Array.Empty<SegmentFile>();
        newestFirst.RemoveAt(0);

        int wanted = Math.Max(1, (int)Math.Ceiling(seconds / (double)segmentSeconds));
        var picked = new List<SegmentFile>();
        foreach (var file in newestFirst)
        {
            if (picked.Count >= wanted) break;
            if (picked.Count > 0 && (picked[^1].WrittenUtc - file.WrittenUtc).TotalSeconds > segmentSeconds * 2.5) break;
            picked.Add(file);
        }

        picked.Reverse();
        return picked;
    }

    public static IReadOnlyList<SegmentFile> ListSegments(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return Array.Empty<SegmentFile>();
            return Directory.GetFiles(folder, "seg*.mkv")
                .Select(f => new FileInfo(f))
                .Where(i => i.Length > 0)
                .Select(i => new SegmentFile(i.FullName, i.LastWriteTimeUtc))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<SegmentFile>();
        }
    }

    /// <summary>Conteúdo do arquivo de lista usado pelo demuxer "concat" do FFmpeg.</summary>
    public static string ConcatList(IEnumerable<string> paths)
    {
        var builder = new StringBuilder();
        foreach (string path in paths)
            builder.Append("file '").Append(path.Replace('\\', '/').Replace("'", "'\\''")).Append("'\n");
        return builder.ToString();
    }

    public static IReadOnlyList<string> ConcatArguments(string listFile, string outputFile) => new[]
    {
        "-hide_banner", "-loglevel", "error", "-y", "-f", "concat", "-safe", "0", "-i", listFile,
        "-c", "copy", "-movflags", "+faststart", outputFile,
    };

    public static string ClipName(string? game, DateTime now)
    {
        string label = Sanitize(game);
        return $"Replay{(label.Length > 0 ? "_" + label : "")}_{now:yyyyMMdd_HHmmss}.mp4";
    }

    internal static string Sanitize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var builder = new StringBuilder();
        foreach (char c in text.Trim())
            builder.Append(char.IsLetterOrDigit(c) ? c : (c is ' ' or '-' or '_' ? '_' : '\0'));
        string clean = builder.ToString().Replace("\0", "");
        return clean.Length > 40 ? clean[..40] : clean;
    }

    /// <summary>Junta os pedaços em um MP4 sem recodificar (rápido). Retorna o caminho do clipe.</summary>
    public static string SaveClip(string ffmpeg, IReadOnlyList<SegmentFile> segments, string clipsFolder, string? game, DateTime now)
    {
        if (segments.Count == 0) throw new InvalidOperationException("Ainda não há replay suficiente. Aguarde alguns segundos depois de iniciar.");
        Directory.CreateDirectory(clipsFolder);
        string output = Path.Combine(clipsFolder, ClipName(game, now));
        string list = Path.Combine(Path.GetTempPath(), $"mbx-replay-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(list, ConcatList(segments.Select(s => s.Path)), new UTF8Encoding(false));
            var info = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            foreach (string a in ConcatArguments(list, output)) info.ArgumentList.Add(a);
            using var process = Process.Start(info) ?? throw new InvalidOperationException("Não foi possível iniciar o FFmpeg.");
            string error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(60_000))
            {
                try { process.Kill(true); } catch (InvalidOperationException) { }
                throw new InvalidOperationException("O FFmpeg demorou demais para montar o clipe.");
            }
            if (process.ExitCode != 0 || !File.Exists(output))
                throw new InvalidOperationException("Não foi possível montar o clipe: " + error.Trim());
            return output;
        }
        finally
        {
            try { File.Delete(list); } catch (IOException) { }
        }
    }

    public static void ClearSegments(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) { Directory.CreateDirectory(folder); return; }
            foreach (string f in Directory.GetFiles(folder, "seg*.mkv")) File.Delete(f);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
