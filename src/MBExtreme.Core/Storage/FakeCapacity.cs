using System.Diagnostics;

namespace MBExtreme.Core.Storage;

/// <summary>Resultado do teste de capacidade.</summary>
public sealed record CapacityResult(long Written, long Verified, long Corrupt, double WriteMBps, double ReadMBps, bool Cancelled)
{
    /// <summary>Original = tudo que foi escrito voltou igual.</summary>
    public bool Genuine => !Cancelled && Written > 0 && Corrupt == 0 && Verified == Written;
}

public sealed record CapacityProgress(string Stage, long Done, long Total, double MBps);

/// <summary>
/// Pendrive ou cartão com capacidade falsa: o aparelho diz ter (por exemplo) 128 GB mas só guarda 8; o resto some ou volta errado.
/// O teste escreve arquivos com um padrão conhecido em todo o espaço livre, lê de volta e confere cada pedaço. Não apaga o que já existe
/// no pendrive: só usa o espaço livre e apaga os arquivos de teste no fim.
/// </summary>
public static class FakeCapacity
{
    public const int BlockSize = 1024 * 1024;
    public const long FileSize = 1024L * 1024 * 1024;
    public const string Prefix = "MBX-TESTE-";

    /// <summary>Enche o bloco com o padrão da posição (cada megabyte do disco tem um conteúdo diferente).</summary>
    public static void Fill(byte[] block, long offset)
    {
        ulong state = (ulong)offset * 0x9E3779B97F4A7C15UL + 0xD1B54A32D192ED03UL;
        for (int i = 0; i + 8 <= block.Length; i += 8)
        {
            state ^= state << 13; state ^= state >> 7; state ^= state << 17;
            BitConverter.TryWriteBytes(block.AsSpan(i, 8), state);
        }
        // os primeiros 8 bytes guardam a própria posição: um pendrive falso que "repete" o começo é pego aqui
        BitConverter.TryWriteBytes(block.AsSpan(0, 8), offset);
    }

    /// <summary>O bloco lido é igual ao que foi escrito nesta posição?</summary>
    public static bool Check(byte[] block, int length, long offset, byte[] scratch)
    {
        Fill(scratch, offset);
        return block.AsSpan(0, length).SequenceEqual(scratch.AsSpan(0, length));
    }

    public static CapacityResult Run(string root, IProgress<CapacityProgress>? progress, CancellationToken ct, long? limitBytes = null)
    {
        long free = limitBytes ?? Math.Max(0, new DriveInfo(root).AvailableFreeSpace - 4 * BlockSize);
        var block = new byte[BlockSize];
        var scratch = new byte[BlockSize];
        var files = new List<(string Path, long Start, long Length)>();
        long written = 0, verified = 0, corrupt = 0;
        bool cancelled = false;
        var clock = Stopwatch.StartNew();
        try
        {
            // escrita
            int index = 1;
            while (written < free && !ct.IsCancellationRequested)
            {
                string path = Path.Combine(root, $"{Prefix}{index++:0000}.dat");
                long length = Math.Min(FileSize, free - written);
                length -= length % BlockSize;
                if (length <= 0) break;
                long start = written;
                try
                {
                    using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, BlockSize, FileOptions.WriteThrough);
                    for (long pos = 0; pos < length && !ct.IsCancellationRequested; pos += BlockSize)
                    {
                        Fill(block, start + pos);
                        stream.Write(block, 0, BlockSize);
                        written += BlockSize;
                        if (written % (64 * BlockSize) == 0) progress?.Report(new("Escrevendo", written, free, written / 1048576.0 / Math.Max(0.001, clock.Elapsed.TotalSeconds)));
                    }
                }
                catch (IOException)
                {
                    // disco cheio antes do esperado (comum nos falsos): para de escrever e confere o que foi
                    files.Add((path, start, written - start));
                    break;
                }
                files.Add((path, start, written - start));
            }
            double writeSpeed = written / 1048576.0 / Math.Max(0.001, clock.Elapsed.TotalSeconds);
            cancelled = ct.IsCancellationRequested;

            // leitura e conferência
            clock.Restart();
            long read = 0;
            foreach (var (path, start, length) in files)
            {
                if (ct.IsCancellationRequested) { cancelled = true; break; }
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BlockSize, FileOptions.SequentialScan);
                    for (long pos = 0; pos < length && !ct.IsCancellationRequested; pos += BlockSize)
                    {
                        int got = 0;
                        while (got < BlockSize) { int n = stream.Read(block, got, BlockSize - got); if (n == 0) break; got += n; }
                        if (got == BlockSize && Check(block, got, start + pos, scratch)) verified += BlockSize;
                        else corrupt += BlockSize;
                        read += BlockSize;
                        if (read % (64 * BlockSize) == 0) progress?.Report(new("Conferindo", read, written, read / 1048576.0 / Math.Max(0.001, clock.Elapsed.TotalSeconds)));
                    }
                }
                catch (IOException) { corrupt += length; }
            }
            double readSpeed = read / 1048576.0 / Math.Max(0.001, clock.Elapsed.TotalSeconds);
            return new CapacityResult(written, verified, corrupt, writeSpeed, readSpeed, cancelled || ct.IsCancellationRequested);
        }
        finally
        {
            foreach (var (path, _, _) in files) try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
