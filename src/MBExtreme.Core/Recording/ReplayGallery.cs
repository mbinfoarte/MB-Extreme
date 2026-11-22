using System.Globalization;
using System.Text.RegularExpressions;

namespace MBExtreme.Core.Recording;

/// <summary>Um clipe salvo pelo replay: jogo (pelo nome do arquivo), quando e tamanho.</summary>
public sealed record ReplayClip(string Path, string Game, DateTime At, long Bytes);

/// <summary>Galeria dos lances: lê a pasta de clipes, agrupa por jogo (mais recente primeiro) e monta as miniaturas.</summary>
public static class ReplayGallery
{
    private static readonly Regex Name = new(@"^Replay(?:_(?<game>.+?))?_(?<at>\d{8}_\d{6})(?:_\d+)?$", RegexOptions.Compiled);

    /// <summary>"Replay_Valorant_20261122_211400.mp4" → ("Valorant", 22/11 21:14). Sem jogo = "Outros".</summary>
    public static (string Game, DateTime? At) Parse(string fileName)
    {
        var m = Name.Match(System.IO.Path.GetFileNameWithoutExtension(fileName));
        if (!m.Success) return ("Outros", null);
        string game = m.Groups["game"].Success ? m.Groups["game"].Value.Replace('_', ' ').Trim() : "";
        DateTime? at = DateTime.TryParseExact(m.Groups["at"].Value, "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        return (game.Length > 0 ? game : "Outros", at);
    }

    public static IReadOnlyList<ReplayClip> List(string folder)
    {
        if (!Directory.Exists(folder)) return Array.Empty<ReplayClip>();
        var clips = new List<ReplayClip>();
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*.*").Where(f => f.Extension is ".mp4" or ".mkv"))
        {
            var (game, at) = Parse(file.Name);
            clips.Add(new ReplayClip(file.FullName, game, at ?? file.LastWriteTime, file.Length));
        }
        return clips.OrderByDescending(c => c.At).ToList();
    }

    /// <summary>Jogos na ordem do lance mais recente de cada um.</summary>
    public static IReadOnlyList<(string Game, IReadOnlyList<ReplayClip> Clips)> ByGame(IReadOnlyList<ReplayClip> clips) =>
        clips.GroupBy(c => c.Game, StringComparer.OrdinalIgnoreCase)
             .OrderByDescending(g => g.Max(c => c.At))
             .Select(g => (g.First().Game, (IReadOnlyList<ReplayClip>)g.OrderByDescending(c => c.At).ToList()))
             .ToList();

    public static string ThumbPath(string clip) =>
        System.IO.Path.Combine(System.IO.Path.GetDirectoryName(clip)!, ".miniaturas", System.IO.Path.GetFileNameWithoutExtension(clip) + ".jpg");

    /// <summary>Uma imagem do segundo 1 do clipe, 320 px de largura.</summary>
    public static IReadOnlyList<string> ThumbArgs(string clip, string thumb) => new[]
    {
        "-hide_banner", "-loglevel", "error", "-y", "-ss", "1", "-i", clip, "-frames:v", "1", "-vf", "scale=320:-2", "-q:v", "4", thumb,
    };
}
