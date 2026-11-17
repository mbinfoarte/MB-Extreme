using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MBExtreme.Core.Benchmark;

/// <summary>Uma tarefa do uso do dia a dia e quanto tempo levou.</summary>
public sealed record DailyTask(string Name, double Seconds, string? Note = null);

public sealed record DailyResult(IReadOnlyList<DailyTask> Tasks, int Score);

/// <summary>
/// Uso do dia a dia (pesado), sem internet e sem abrir programas: escreve e revisa documentos, monta e lê páginas de
/// sites, grava e assiste um vídeo H.264 de verdade (com o FFmpeg do programa), recalcula planilhas, edita uma foto de
/// 12 megapixels, compacta e descompacta arquivos e, no fim, faz várias dessas coisas ao mesmo tempo. Marca o tempo de cada uma.
/// </summary>
public static partial class DailyBenchmark
{
    public const string Documents = "Documentos";
    public const string Browsing = "Navegação de sites";
    public const string Video = "Vídeo H.264";
    public const string Sheets = "Planilhas";
    public const string Photos = "Edição de fotos";
    public const string Zip = "Compactar arquivos";
    public const string Together = "Tudo ao mesmo tempo";

    /// <summary>Tarefas na ordem, com o tempo de referência em segundos (um PC comum de 2022; a nota 1000 é igual a ele).</summary>
    public static readonly IReadOnlyList<(string Name, double Reference, string Description)> Tasks = new[]
    {
        (Documents, 1.2, "Escreve um texto longo, conta palavras, procura e troca termos e exporta para página"),
        (Browsing, 1.2, "Monta 60 páginas de sites, lê as marcações e os dados (JSON) como um navegador"),
        (Video, 6.0, "Grava 4 segundos em Full HD (H.264) e assiste o vídeo inteiro"),
        (Sheets, 1.2, "Planilha de 200 mil células: fórmulas, procura, ordenação e resumo por grupo"),
        (Photos, 1.5, "Foto de 12 megapixels: desfoque, nitidez, cor e redução de tamanho"),
        (Zip, 1.5, "Compacta 40 MB de documentos e fotos e descompacta de novo"),
        (Together, 2.5, "Documentos, planilha, foto e compactação ao mesmo tempo"),
    };

    public static DailyResult Run(CancellationToken token, Action<string, double>? progress = null, string? ffmpeg = null, string? tempDirectory = null)
    {
        var list = new List<DailyTask>();
        for (int i = 0; i < Tasks.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            string name = Tasks[i].Name;
            progress?.Invoke(name, i / (double)Tasks.Count);
            if (name == Video)
            {
                list.Add(VideoTask(ffmpeg, tempDirectory ?? Path.GetTempPath(), token));
                continue;
            }
            var watch = Stopwatch.StartNew();
            switch (name)
            {
                case Documents: DocumentsWork(); break;
                case Browsing: BrowsingWork(); break;
                case Sheets: SheetsWork(); break;
                case Photos: PhotoWork(); break;
                case Zip: ZipWork(); break;
                default:
                    Task.WaitAll(new[] { Task.Run(DocumentsWork), Task.Run(SheetsWork), Task.Run(PhotoWork), Task.Run(ZipWork) }, token);
                    break;
            }
            list.Add(new DailyTask(name, watch.Elapsed.TotalSeconds));
        }
        progress?.Invoke(Together, 1);
        return new DailyResult(list, Score(list));
    }

    /// <summary>1000 x média geométrica de (tempo de referência / tempo medido). Tarefa que não rodou (vídeo sem FFmpeg) fica de fora.</summary>
    public static int Score(IReadOnlyList<DailyTask> tasks)
    {
        var ratios = tasks.Where(t => t.Seconds > 0)
            .Select(t => (t, Tasks.FirstOrDefault(x => x.Name == t.Name).Reference))
            .Where(x => x.Reference > 0).Select(x => x.Reference / x.t.Seconds).ToList();
        return ratios.Count == 0 ? 0 : (int)Math.Round(1000 * Math.Exp(ratios.Average(Math.Log)));
    }

    private static int _sink;

    private static readonly string[] Words =
        ("o a de que e do da em um para é com não uma os no se na por mais as dos como mas foi ao ele das tem à seu sua ou ser quando muito " +
         "nos já está eu também só pelo pela até isso ela entre era depois sem mesmo aos ter seus quem nas me esse eles estão você tinha foram " +
         "processador memória placa vídeo computador jogo arquivo pasta documento planilha relatório reunião cliente pedido entrega").Split(' ');

    private static void DocumentsWork()
    {
        var rng = new Random(11);
        var sb = new StringBuilder(2_000_000);
        for (int p = 0; p < 1600; p++)
        {
            int sentences = 4 + rng.Next(5);
            for (int s = 0; s < sentences; s++)
            {
                int n = 8 + rng.Next(14);
                for (int w = 0; w < n; w++)
                {
                    string word = Words[rng.Next(Words.Length)];
                    sb.Append(w == 0 ? char.ToUpperInvariant(word[0]) + word[1..] : word).Append(w == n - 1 ? ". " : " ");
                }
            }
            sb.Append("\n\n");
        }
        string text = sb.ToString();
        int words = 0;
        var counts = new Dictionary<string, int>();
        foreach (Match m in WordPattern().Matches(text))
        {
            words++;
            string key = m.Value.ToLowerInvariant();
            counts[key] = counts.TryGetValue(key, out int c) ? c + 1 : 1;
        }
        text = text.Replace("computador", "PC").Replace("relatório", "documento");
        string html = "<p>" + string.Join("</p><p>", text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Select(System.Net.WebUtility.HtmlEncode)) + "</p>";
        _sink += words + counts.Count + html.Length;
    }

    private static void BrowsingWork()
    {
        var rng = new Random(22);
        int total = 0;
        for (int page = 0; page < 60; page++)
        {
            var sb = new StringBuilder();
            sb.Append("<html><head><title>Loja ").Append(page).Append("</title></head><body><div class=\"lista\">");
            var items = new List<object>();
            for (int i = 0; i < 400; i++)
            {
                double price = Math.Round(rng.NextDouble() * 5000, 2);
                sb.Append("<div class=\"produto\" data-id=\"").Append(i).Append("\"><a href=\"/p/").Append(i).Append("\">")
                  .Append(Words[rng.Next(Words.Length)]).Append("</a><span class=\"preco\">R$ ").Append(price).Append("</span></div>");
                items.Add(new { id = i, nome = Words[rng.Next(Words.Length)], preco = price, estoque = rng.Next(100) });
            }
            sb.Append("</div></body></html>");
            string html = sb.ToString();
            // "navegador": lê as marcações, monta a árvore e procura os preços
            int depth = 0, maxDepth = 0, links = 0;
            foreach (Match m in TagPattern().Matches(html))
            {
                if (m.Value.StartsWith("</", StringComparison.Ordinal)) depth--;
                else { depth++; if (m.Value.StartsWith("<a ", StringComparison.Ordinal)) links++; }
                maxDepth = Math.Max(maxDepth, depth);
            }
            string json = JsonSerializer.Serialize(items);
            using var doc = JsonDocument.Parse(json);
            double sum = doc.RootElement.EnumerateArray().Sum(e => e.GetProperty("preco").GetDouble());
            total += links + maxDepth + (int)sum % 7;
        }
        _sink += total;
    }

    private static void SheetsWork()
    {
        const int rows = 20000, cols = 10;
        var rng = new Random(33);
        var cells = new double[rows, cols];
        var groups = new string[rows];
        for (int r = 0; r < rows; r++)
        {
            groups[r] = "Região " + rng.Next(12);
            for (int c = 0; c < cols; c++) cells[r, c] = Math.Round(rng.NextDouble() * 1000, 2);
        }
        // fórmulas: total da linha, média móvel, imposto e um PROCV por código
        var lookup = new Dictionary<int, double>();
        for (int r = 0; r < rows; r++) lookup[r * 7 % 10007] = cells[r, 0];
        var totals = new double[rows];
        for (int pass = 0; pass < 3; pass++)
            for (int r = 0; r < rows; r++)
            {
                double sum = 0;
                for (int c = 0; c < cols; c++) sum += cells[r, c];
                double moving = r >= 5 ? (totals[r - 1] + totals[r - 2] + totals[r - 3] + totals[r - 4] + totals[r - 5]) / 5 : sum;
                double tax = sum > 5000 ? sum * 0.275 - 869.36 : sum * 0.15;
                lookup.TryGetValue(r % 10007, out double found);
                totals[r] = sum + moving * 0.01 + tax * 0.001 + found * 0.0001;
            }
        var order = Enumerable.Range(0, rows).OrderByDescending(r => totals[r]).ThenBy(r => groups[r], StringComparer.Ordinal).ToArray();
        var pivot = order.GroupBy(r => groups[r]).Select(g => (g.Key, g.Sum(r => totals[r]), g.Average(r => cells[r, 3]))).ToList();
        _sink += order[0] + pivot.Count;
    }

    private static void PhotoWork()
    {
        const int w = 4000, h = 3000;
        var src = new byte[w * h * 3];
        new Random(44).NextBytes(src);
        var tmp = new byte[src.Length];
        // desfoque (horizontal e vertical), nitidez, ajuste de cor e redução pela metade
        Parallel.For(0, h, y =>
        {
            int row = y * w * 3;
            for (int x = 2; x < w - 2; x++)
                for (int c = 0; c < 3; c++)
                {
                    int i = row + x * 3 + c;
                    tmp[i] = (byte)((src[i - 6] + 4 * src[i - 3] + 6 * src[i] + 4 * src[i + 3] + src[i + 6]) >> 4);
                }
        });
        Parallel.For(2, h - 2, y =>
        {
            int stride = w * 3;
            for (int x = 0; x < stride; x++)
            {
                int i = y * stride + x;
                int blur = (tmp[i - 2 * stride] + 4 * tmp[i - stride] + 6 * tmp[i] + 4 * tmp[i + stride] + tmp[i + 2 * stride]) >> 4;
                int sharp = src[i] + (src[i] - blur) / 2;
                int color = (int)(sharp * 1.08 + 6);
                src[i] = (byte)Math.Clamp(color, 0, 255);
            }
        });
        var half = new byte[w / 2 * (h / 2) * 3];
        Parallel.For(0, h / 2, y =>
        {
            for (int x = 0; x < w / 2; x++)
                for (int c = 0; c < 3; c++)
                {
                    int a = (2 * y * w + 2 * x) * 3 + c;
                    half[(y * (w / 2) + x) * 3 + c] = (byte)((src[a] + src[a + 3] + src[a + w * 3] + src[a + w * 3 + 3]) >> 2);
                }
        });
        _sink += half[half.Length / 2];
    }

    private static readonly byte[] Text = System.Text.Encoding.UTF8.GetBytes(string.Join(' ', Words) + ". ");

    private static void ZipWork()
    {
        var rng = new Random(55);
        using var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, true))
        {
            for (int f = 0; f < 40; f++)
            {
                var entry = zip.CreateEntry($"pasta/arquivo{f}.{(f % 3 == 0 ? "jpg" : "docx")}", CompressionLevel.Optimal);
                using var stream = entry.Open();
                var data = new byte[1024 * 1024];
                if (f % 3 == 0) rng.NextBytes(data); // fotos quase não comprimem
                else for (int i = 0; i < data.Length; i++) data[i] = Text[(i + f * 31) % Text.Length];
                stream.Write(data);
            }
        }
        archive.Position = 0;
        long read = 0;
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Read, true))
        {
            var buffer = new byte[81920];
            foreach (var entry in zip.Entries)
            {
                using var stream = entry.Open();
                int n;
                while ((n = stream.Read(buffer, 0, buffer.Length)) > 0) read += n;
            }
        }
        _sink += (int)(read % 1000);
    }

    /// <summary>Grava 4 s de vídeo Full HD em H.264 e assiste (decodifica) o arquivo. Sem o FFmpeg, a tarefa fica de fora.</summary>
    private static DailyTask VideoTask(string? ffmpeg, string tempDirectory, CancellationToken token)
    {
        if (ffmpeg is null || !File.Exists(ffmpeg)) return new DailyTask(Video, 0, "FFmpeg não encontrado: tarefa não medida");
        string file = Path.Combine(tempDirectory, "mbextreme-bench-" + Guid.NewGuid().ToString("N") + ".mp4");
        var watch = Stopwatch.StartNew();
        try
        {
            if (!RunFfmpeg(ffmpeg, token, "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc2=size=1920x1080:rate=30", "-t", "4",
                    "-c:v", "libx264", "-preset", "veryfast", "-pix_fmt", "yuv420p", "-y", file))
                return new DailyTask(Video, 0, "O FFmpeg não conseguiu gravar o vídeo");
            if (!RunFfmpeg(ffmpeg, token, "-hide_banner", "-loglevel", "error", "-i", file, "-f", "null", "-"))
                return new DailyTask(Video, 0, "O FFmpeg não conseguiu ler o vídeo");
            return new DailyTask(Video, watch.Elapsed.TotalSeconds);
        }
        finally
        {
            try { File.Delete(file); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static bool RunFfmpeg(string ffmpeg, CancellationToken token, params string[] args)
    {
        var info = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (string a in args) info.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(info);
            if (p is null) return false;
            _ = p.StandardError.ReadToEndAsync();
            _ = p.StandardOutput.ReadToEndAsync();
            var limit = Stopwatch.StartNew();
            while (!p.WaitForExit(200))
            {
                if (token.IsCancellationRequested || limit.Elapsed.TotalSeconds > 120)
                {
                    try { p.Kill(true); } catch (InvalidOperationException) { }
                    token.ThrowIfCancellationRequested();
                    return false;
                }
            }
            return p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex WordPattern();

    [GeneratedRegex(@"</?[a-z][^>]*>")]
    private static partial Regex TagPattern();
}
