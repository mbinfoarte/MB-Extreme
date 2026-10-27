using System.IO.Pipes;
using System.Runtime.Versioning;

namespace MBExtreme.Core.Recording;

/// <summary>
/// Cano nomeado por onde o FFmpeg manda uma cópia pequena (JPEG) do que está no ar.
/// A leitura nunca para enquanto a sessão existe: se o programa não lesse, o FFmpeg travaria esperando e a live engasgaria.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProgramFeed : IDisposable
{
    private readonly NamedPipeServerStream _server;
    private readonly CancellationTokenSource _cts = new();
    private readonly MjpegSplitter _splitter = new();
    private sealed record Frame(byte[] Jpeg, long Number);
    private Frame? _latest;
    private long _count;
    private Task? _reader;

    public ProgramFeed()
    {
        Name = "mbx-program-" + Guid.NewGuid().ToString("N");
        _server = new NamedPipeServerStream(Name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 1 << 20, 0);
    }

    public string Name { get; }

    public string Path => @"\\.\pipe\" + Name;

    public (byte[] Jpeg, long Number)? Latest => Volatile.Read(ref _latest) is { } f ? (f.Jpeg, f.Number) : null;

    /// <summary>Espera o FFmpeg abrir o cano e então lê sem parar (chame antes de iniciar o FFmpeg).</summary>
    public void BeginWait() => _reader = Task.Run(ReadAsync);

    private async Task ReadAsync()
    {
        var token = _cts.Token;
        try
        {
            await _server.WaitForConnectionAsync(token).ConfigureAwait(false);
            var buffer = new byte[64 * 1024];
            while (!token.IsCancellationRequested)
            {
                int read = await _server.ReadAsync(buffer, token).ConfigureAwait(false);
                if (read <= 0) break;
                foreach (var frame in _splitter.Push(buffer.AsSpan(0, read)))
                    Volatile.Write(ref _latest, new Frame(frame, Interlocked.Increment(ref _count)));
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) { }
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch (ObjectDisposedException) { }
        _server.Dispose();
        try { _reader?.Wait(1000); } catch (AggregateException) { }
        _cts.Dispose();
    }
}
