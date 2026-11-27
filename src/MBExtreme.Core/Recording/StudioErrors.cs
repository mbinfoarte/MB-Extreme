using System.Text.RegularExpressions;

namespace MBExtreme.Core.Recording;

/// <summary>Traduz a última mensagem do FFmpeg ao iniciar: diz qual entrada (tela, câmera, áudio, logo, imagem) não abriu.</summary>
public static class StudioErrors
{
    private static readonly Regex OpeningFile = new(@"Error opening input file (?<file>.+?)\.?$", RegexOptions.Compiled);

    /// <summary>Nome amigável de uma entrada do FFmpeg.</summary>
    public static string SourceName(string input)
    {
        string s = input.Trim().Trim('"');
        if (s.Contains("mbx-overlay-", StringComparison.OrdinalIgnoreCase)) return "a camada do logo";
        if (s.Contains("mbx-audio-", StringComparison.OrdinalIgnoreCase)) return "o áudio";
        if (s.Contains("mbx-camfeed-", StringComparison.OrdinalIgnoreCase)) return "a câmera";
        if (s.StartsWith("ddagrab", StringComparison.OrdinalIgnoreCase)) return "a captura da tela";
        if (s.StartsWith("title=", StringComparison.OrdinalIgnoreCase)) return "a janela " + s[6..];
        if (s.StartsWith("video=", StringComparison.OrdinalIgnoreCase)) return "a câmera " + s[6..];
        if (CameraSource.IsStream(s)) return "a câmera " + CameraSource.Display(s);
        return "o arquivo " + s[(s.LastIndexOfAny(new[] { '\\', '/' }) + 1)..];   // funciona com \ e / em qualquer sistema
    }

    /// <summary>Junta "Error opening input files: X" com a linha anterior que diz qual arquivo era.</summary>
    public static string Explain(string? previous, string last)
    {
        if (!last.StartsWith("Error opening input files", StringComparison.OrdinalIgnoreCase) || previous is null) return last;
        var m = OpeningFile.Match(previous);
        if (!m.Success) return last;
        string reason = last.Contains(':') ? last[(last.IndexOf(':') + 1)..].Trim() : last;
        return $"não abriu {SourceName(m.Groups["file"].Value)} ({reason})";
    }
}
