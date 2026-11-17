using System.IO.Compression;
using MBExtreme.Core.Benchmark;

// Gera o índice de velocidade de CPUs e placas de vídeo a partir do Blender Open Data (licença CC0).
//   dotnet run --project tools/MBExtreme.IndexTool -- --download saida.json
//   dotnet run --project tools/MBExtreme.IndexTool -- opendata-latest.zip saida.json
// Também aceita um arquivo .jsonl solto. Código de saída 0 só quando o índice sai com dados.

const string SnapshotUrl = "https://opendata.blender.org/snapshots/opendata-latest.zip";

if (args.Length < 2)
{
    Console.Error.WriteLine("Uso: IndexTool (--download | <opendata.zip|.jsonl>) <saida.json>");
    return 1;
}

string output = args[1];
string input = args[0];
string? temp = null;
try
{
    if (input == "--download")
    {
        temp = Path.Combine(Path.GetTempPath(), "opendata-" + Guid.NewGuid().ToString("N") + ".zip");
        Console.WriteLine("Baixando " + SnapshotUrl + " ...");
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MBExtreme-IndexTool/1.0");
        using var response = await http.GetAsync(SnapshotUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using (var file = File.Create(temp)) await response.Content.CopyToAsync(file);
        input = temp;
    }

    IEnumerable<string> Lines()
    {
        if (input.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(input);
            foreach (var entry in zip.Entries.Where(e => e.Name.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)))
            {
                using var reader = new StreamReader(entry.Open());
                string? line;
                while ((line = reader.ReadLine()) is not null) yield return line;
            }
        }
        else
        {
            foreach (var line in File.ReadLines(input)) yield return line;
        }
    }

    var data = HardwareIndexBuilder.Build(Lines(), DateTime.UtcNow);
    Console.WriteLine($"Processadores no índice: {data.Cpu.Count} · placas de vídeo: {data.Gpu.Count}");
    if (data.Cpu.Count == 0 && data.Gpu.Count == 0)
    {
        Console.Error.WriteLine("O índice saiu vazio: o formato dos dados pode ter mudado. Nada foi gravado.");
        return 2;
    }

    HardwareIndex.Save(data, output);
    Console.WriteLine("Índice gravado em " + output);
    return 0;
}
catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException)
{
    Console.Error.WriteLine("Não foi possível gerar o índice: " + ex.Message);
    return 3;
}
finally
{
    if (temp is not null && File.Exists(temp)) File.Delete(temp);
}
