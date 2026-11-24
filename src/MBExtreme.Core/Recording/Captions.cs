using System.Globalization;
using System.Text;

namespace MBExtreme.Core.Recording;

/// <summary>Onde a legenda aparece. "Na divisória" só faz sentido com a tela dividida (no vídeo comum vira o centro).</summary>
public enum CaptionPosition { Bottom, Center, Top, Divider }

public enum CaptionColor { White, Yellow, Green }

/// <summary>Legenda queimada no vídeo em tempo real. O texto fica em dois arquivos (duas linhas) que o app reescreve e o FFmpeg relê a cada quadro.</summary>
public sealed record CaptionOverlay(string Line1File, string Line2File, string FontFile, int SizePercent, CaptionPosition Position, CaptionColor Color, bool Uppercase)
{
    public const int MaxLines = 2;

    public int ClampedSize => Math.Clamp(SizePercent, 3, 10);

    public static bool IsValidPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && path.IndexOfAny(new[] { '"', '|', '\'', '\r', '\n', ';', '[', ']' }) < 0;

    /// <summary>Quantas letras cabem numa linha (a legenda ocupa até 90% da largura).</summary>
    public int MaxChars(int videoWidth)
    {
        double glyph = ClampedSize / 100.0 * (Uppercase ? 0.68 : 0.58);
        return Math.Clamp((int)(0.9 / glyph), 8, 60);
    }
}

public static class CaptionFilter
{
    /// <summary>Caminho dentro de uma opção de filtro do FFmpeg: barras normais, dois-pontos escapado e entre aspas simples.</summary>
    public static string EscapePath(string path) => "'" + path.Replace('\\', '/').Replace(":", "\\:") + "'";

    private static string Hex(CaptionColor c) => c switch { CaptionColor.Yellow => "0xFFE000", CaptionColor.Green => "0x7CFF6B", _ => "0xFFFFFF" };

    private static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// Dois "drawtext" em sequência (linha de cima e linha de baixo), cada um centralizado. A linha de baixo é a mais nova: com uma linha só, ela fica embaixo.
    /// <paramref name="anchor"/> é a posição vertical (0 a 1) do centro da legenda.
    /// </summary>
    public static string Build(CaptionOverlay caption, string input, string output, int width, int height, double dividerFraction)
    {
        int size = Math.Max(12, width * caption.ClampedSize / 100) & ~1;
        double anchor = caption.Position switch
        {
            CaptionPosition.Top => 0.14,
            CaptionPosition.Center => 0.5,
            CaptionPosition.Divider => Math.Clamp(dividerFraction, 0.2, 0.8),
            _ => 0.74,
        };

        int border = Math.Max(2, size / 11);
        string common = $"fontfile={EscapePath(caption.FontFile)}:fontsize={size}:fontcolor={Hex(caption.Color)}:borderw={border}:bordercolor=black:shadowx=2:shadowy=2:shadowcolor=black@0.6:expansion=none:reload=1:x=(w-text_w)/2";
        int top = (int)Math.Round(height * anchor);
        int gap = Math.Max(2, size / 8);
        string line1 = $"drawtext=textfile={EscapePath(caption.Line1File)}:{common}:y={top - size - gap}";
        string line2 = $"drawtext=textfile={EscapePath(caption.Line2File)}:{common}:y={top + gap}";
        return $"{input}{line1},{line2}{output}";
    }
}

/// <summary>
/// Junta o que o reconhecedor vai ouvindo (palavras parciais que mudam e frases confirmadas) nas duas linhas que aparecem no vídeo.
/// Mostra sempre o trecho mais recente e apaga tudo depois de um tempo de silêncio.
/// </summary>
public sealed class CaptionComposer
{
    private const int MaxWords = 40;

    private readonly int _maxChars;
    private readonly bool _upper;
    private readonly double _holdSeconds;
    private readonly List<string> _confirmed = new();
    private string _partial = "";
    private double _lastUpdate = double.NegativeInfinity;

    public CaptionComposer(int maxChars, bool uppercase, double holdSeconds = 3)
    {
        _maxChars = Math.Max(6, maxChars);
        _upper = uppercase;
        _holdSeconds = Math.Max(1, holdSeconds);
    }

    public void Partial(string text, double now)
    {
        _partial = Clean(text);
        if (_partial.Length > 0) _lastUpdate = now;
    }

    public void Final(string text, double now)
    {
        string clean = Clean(text);
        _partial = "";
        if (clean.Length == 0) return;
        _confirmed.AddRange(clean.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (_confirmed.Count > MaxWords) _confirmed.RemoveRange(0, _confirmed.Count - MaxWords);
        _lastUpdate = now;
    }

    private string Clean(string text) => (_upper ? text.ToUpper(new CultureInfo("pt-BR")) : text).Replace('\r', ' ').Replace('\n', ' ').Replace('%', ' ').Trim();

    /// <summary>As duas linhas do momento; a de baixo é a mais nova. Vazias depois do silêncio.</summary>
    public (string Line1, string Line2) Current(double now)
    {
        if (now - _lastUpdate > _holdSeconds)
        {
            _confirmed.Clear();
            _partial = "";
            return ("", "");
        }

        var words = new List<string>(_confirmed);
        if (_partial.Length > 0) words.AddRange(_partial.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var lines = Wrap(words, _maxChars);
        if (lines.Count == 0) return ("", "");
        if (lines.Count == 1) return ("", lines[0]);
        return (lines[^2], lines[^1]);
    }

    public static List<string> Wrap(IEnumerable<string> words, int maxChars)
    {
        var lines = new List<string>();
        var line = new StringBuilder();
        foreach (var raw in words)
        {
            string word = raw.Length > maxChars ? raw[..maxChars] : raw;
            if (line.Length > 0 && line.Length + 1 + word.Length > maxChars)
            {
                lines.Add(line.ToString());
                line.Clear();
            }
            if (line.Length > 0) line.Append(' ');
            line.Append(word);
        }
        if (line.Length > 0) lines.Add(line.ToString());
        return lines;
    }
}

/// <summary>Reescreve os arquivos de legenda sem deixar o FFmpeg ler um texto pela metade. Texto vazio vira um espaço (o FFmpeg não aceita arquivo vazio).</summary>
public sealed class CaptionFiles
{
    private readonly string _line1, _line2;
    private string? _last1, _last2;

    public CaptionFiles(string line1File, string line2File)
    {
        _line1 = line1File;
        _line2 = line2File;
    }

    public static string Normalize(string text) => text.Length == 0 ? " " : text;

    /// <summary>Grava o que mudou. Devolve false se algum arquivo estava em uso (tenta de novo na próxima vez).</summary>
    public bool Write(string line1, string line2)
    {
        bool ok = Save(_line1, Normalize(line1), ref _last1);
        ok &= Save(_line2, Normalize(line2), ref _last2);
        return ok;
    }

    private static bool Save(string path, string text, ref string? last)
    {
        if (last == text) return true;
        try
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
            last = text;
            return true;
        }
        catch (IOException)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                var bytes = new UTF8Encoding(false).GetBytes(text);
                stream.Write(bytes, 0, bytes.Length);
                last = text;
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
        catch (UnauthorizedAccessException) { return false; }
    }
}

/// <summary>Quem transforma voz em texto (no app, o Vosk, que funciona sem internet). O áudio entra em 16 kHz, mono, 16 bits.</summary>
public interface ISpeechEngine : IDisposable
{
    event Action<string>? Partial;

    event Action<string>? Final;

    void Accept(byte[] pcm16kMono, int count);
}

/// <summary>Liga o reconhecedor às duas linhas de legenda: a cada 100 ms atualiza os arquivos que o FFmpeg lê.</summary>
public sealed class CaptionService : IDisposable
{
    private readonly CaptionComposer _composer;
    private readonly CaptionFiles _files;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly Timer _timer;
    private readonly object _gate = new();
    private ISpeechEngine? _engine;
    private SpeechPcm? _pcm;
    private (string, string) _shown = ("", "");

    public CaptionService(CaptionOverlay overlay, int videoWidth)
    {
        _composer = new CaptionComposer(overlay.MaxChars(videoWidth), overlay.Uppercase);
        _files = new CaptionFiles(overlay.Line1File, overlay.Line2File);
        _files.Write("", "");
        _timer = new Timer(_ => Tick(), null, 100, 100);
    }

    /// <summary>Última legenda mostrada (para a pré-visualização na tela).</summary>
    public (string Line1, string Line2) Shown
    {
        get { lock (_gate) return _shown; }
    }

    public void Attach(ISpeechEngine engine, WaveFormatInfo format)
    {
        _engine = engine;
        _pcm = new SpeechPcm(format);
        engine.Partial += t => { lock (_gate) _composer.Partial(t, _clock.Elapsed.TotalSeconds); };
        engine.Final += t => { lock (_gate) _composer.Final(t, _clock.Elapsed.TotalSeconds); };
    }

    /// <summary>Recebe o áudio da fonte escolhida (microfone ou som do PC) no formato original.</summary>
    public void Feed(byte[] data, int count)
    {
        if (_engine is null || _pcm is null) return;
        var converted = _pcm.Convert(data, count);
        if (converted.Length > 0) _engine.Accept(converted, converted.Length);
    }

    private void Tick()
    {
        (string, string) now;
        lock (_gate) now = _composer.Current(_clock.Elapsed.TotalSeconds);
        if (now.Equals(_shown)) return;
        if (_files.Write(now.Item1, now.Item2)) lock (_gate) _shown = now;
    }

    public void Dispose()
    {
        _timer.Dispose();
        _engine?.Dispose();
        _files.Write("", "");
    }
}

public readonly record struct WaveFormatInfo(int SampleRate, int Channels, int BitsPerSample, bool IsFloat);

/// <summary>Converte o áudio capturado (qualquer taxa, canais e formato PCM/float) para 16 kHz, mono, 16 bits.</summary>
public sealed class SpeechPcm
{
    public const int TargetRate = 16000;

    private readonly WaveFormatInfo _format;
    private double _position;       // posição fracionária no fluxo de entrada
    private float _previous;

    public SpeechPcm(WaveFormatInfo format) => _format = format;

    public byte[] Convert(byte[] data, int count)
    {
        int bytesPer = Math.Max(1, _format.BitsPerSample / 8);
        int channels = Math.Max(1, _format.Channels);
        int frames = count / (bytesPer * channels);
        if (frames == 0 || _format.SampleRate <= 0) return Array.Empty<byte>();

        var mono = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            float sum = 0;
            for (int c = 0; c < channels; c++) sum += Read(data, (f * channels + c) * bytesPer, bytesPer);
            mono[f] = sum / channels;
        }

        double step = _format.SampleRate / (double)TargetRate;
        var output = new List<byte>((int)(frames / step * 2) + 4);
        while (_position < frames)
        {
            int i = (int)Math.Floor(_position);
            float a = i == 0 ? _previous : mono[i - 1];
            float b = mono[i];
            double frac = _position - i;
            float sample = (float)(a + (b - a) * frac);
            short value = (short)Math.Round(Math.Clamp(sample, -1f, 1f) * short.MaxValue);
            output.Add((byte)(value & 0xFF));
            output.Add((byte)((value >> 8) & 0xFF));
            _position += step;
        }

        _position -= frames;
        _previous = mono[^1];
        return output.ToArray();
    }

    private float Read(byte[] data, int offset, int bytes)
    {
        if (_format.IsFloat && bytes == 4) return BitConverter.ToSingle(data, offset);
        return bytes switch
        {
            2 => BitConverter.ToInt16(data, offset) / 32768f,
            3 => ((data[offset] << 8 | data[offset + 1] << 16 | data[offset + 2] << 24) >> 8) / 8388608f,
            4 => BitConverter.ToInt32(data, offset) / 2147483648f,
            _ => (data[offset] - 128) / 128f,
        };
    }
}

/// <summary>Modelo de voz em português para o Vosk (funciona sem internet depois de baixado).</summary>
public static class SpeechModels
{
    public const string FolderName = "vosk-model-small-pt-0.3";

    public const string DownloadUrl = "https://alphacephei.com/vosk/models/vosk-model-small-pt-0.3.zip";

    public const long ApproxBytes = 32L * 1024 * 1024;

    /// <summary>O modelo está pronto quando existe a pasta com os arquivos principais.</summary>
    public static bool IsInstalled(string root) =>
        Directory.Exists(Path.Combine(root, FolderName, "am")) && File.Exists(Path.Combine(root, FolderName, "am", "final.mdl"));

    public static string ModelPath(string root) => Path.Combine(root, FolderName);
}
