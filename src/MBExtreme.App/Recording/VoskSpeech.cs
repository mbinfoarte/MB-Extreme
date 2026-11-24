using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using MBExtreme.Core.Recording;
using Vosk;

namespace MBExtreme.UI.Recording;

/// <summary>Reconhecimento de voz sem internet (Vosk). O trabalho pesado roda numa thread própria para não atrapalhar a captura do áudio.</summary>
internal sealed class VoskSpeech : ISpeechEngine
{
    private readonly Model _model;
    private readonly VoskRecognizer _recognizer;
    private readonly BlockingCollection<byte[]> _queue = new(boundedCapacity: 100);
    private readonly Thread _worker;

    public event Action<string>? Partial;

    public event Action<string>? Final;

    public VoskSpeech(string modelPath)
    {
        Vosk.Vosk.SetLogLevel(-1);
        _model = new Model(modelPath);
        _recognizer = new VoskRecognizer(_model, SpeechPcm.TargetRate);
        _worker = new Thread(Work) { IsBackground = true, Name = "Legendas ao vivo" };
        _worker.Start();
    }

    public void Accept(byte[] pcm16kMono, int count)
    {
        var copy = new byte[count];
        Buffer.BlockCopy(pcm16kMono, 0, copy, 0, count);
        _queue.TryAdd(copy);   // se o reconhecedor atrasar, descarta em vez de acumular
    }

    private void Work()
    {
        try
        {
            foreach (var chunk in _queue.GetConsumingEnumerable())
            {
                if (_recognizer.AcceptWaveform(chunk, chunk.Length)) Final?.Invoke(Read(_recognizer.Result(), "text"));
                else Partial?.Invoke(Read(_recognizer.PartialResult(), "partial"));
            }
        }
        catch (ObjectDisposedException) { }
    }

    private static string Read(string json, string key)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(key, out var value) ? value.GetString() ?? "" : "";
        }
        catch (JsonException) { return ""; }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _worker.Join(1500);
        _recognizer.Dispose();
        _model.Dispose();
    }
}

/// <summary>Baixa e instala o modelo de voz em português (cerca de 32 MB, uma vez só).</summary>
internal static class SpeechInstaller
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "speech");

    public static bool IsInstalled => SpeechModels.IsInstalled(Root);

    public static async Task DownloadAsync(IProgress<double> progress, CancellationToken token)
    {
        Directory.CreateDirectory(Root);
        string zip = Path.Combine(Root, "modelo.zip.parte");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            using var response = await http.GetAsync(SpeechModels.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? SpeechModels.ApproxBytes;
            await using (var source = await response.Content.ReadAsStreamAsync(token))
            await using (var file = new FileStream(zip, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, token)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), token);
                    done += read;
                    progress.Report(Math.Min(0.95, done / (double)total));
                }
            }

            string target = SpeechModels.ModelPath(Root);
            if (Directory.Exists(target)) Directory.Delete(target, true);
            await Task.Run(() => ZipFile.ExtractToDirectory(zip, Root, overwriteFiles: true), token);
            if (!IsInstalled) throw new InvalidDataException("O arquivo baixado não tem o modelo de voz esperado.");
            progress.Report(1);
        }
        finally
        {
            try { if (File.Exists(zip)) File.Delete(zip); } catch (IOException) { }
        }
    }
}
