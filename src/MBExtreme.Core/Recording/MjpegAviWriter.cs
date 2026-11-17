using System.Text;

namespace MBExtreme.Core.Recording;

/// <summary>
/// Grava vídeo em AVI com quadros JPEG (Motion JPEG), sem programas externos. O arquivo abre no Windows Media Player, VLC e na maioria dos editores.
/// Limite do formato AVI clássico: cerca de 2 GB.
/// </summary>
public sealed class MjpegAviWriter : IDisposable
{
    public const long MaxBytes = 1_900_000_000;

    private readonly Stream _stream;
    private readonly BinaryWriter _writer;
    private readonly bool _leaveOpen;
    private readonly int _width;
    private readonly int _height;
    private readonly List<(long Offset, int Size)> _index = new();

    private long _riffSizePos, _moviSizePos, _moviStart;
    private long _avihMicrosPos, _avihFramesPos, _strhScalePos, _strhLengthPos;
    private bool _finished;

    public MjpegAviWriter(Stream stream, int width, int height, double fps, bool leaveOpen = false)
    {
        if (!stream.CanSeek) throw new ArgumentException("O fluxo precisa permitir busca.", nameof(stream));
        _stream = stream;
        _leaveOpen = leaveOpen;
        _width = width;
        _height = height;
        _writer = new BinaryWriter(stream, Encoding.ASCII, true);
        WriteHeader(fps);
    }

    public int FrameCount => _index.Count;
    public long Length => _stream.Length;

    private void Fourcc(string text) => _writer.Write(Encoding.ASCII.GetBytes(text));

    private void WriteHeader(double fps)
    {
        int micros = (int)(1_000_000 / Math.Max(fps, 1));

        Fourcc("RIFF"); _riffSizePos = _stream.Position; _writer.Write(0); Fourcc("AVI ");

        Fourcc("LIST"); long hdrlSize = _stream.Position; _writer.Write(0); Fourcc("hdrl");
        long hdrlStart = _stream.Position;

        Fourcc("avih"); _writer.Write(56);
        _avihMicrosPos = _stream.Position; _writer.Write(micros);
        _writer.Write(0); _writer.Write(0); _writer.Write(0x10);
        _avihFramesPos = _stream.Position; _writer.Write(0);
        _writer.Write(0); _writer.Write(1); _writer.Write(_width * _height * 3);
        _writer.Write(_width); _writer.Write(_height);
        _writer.Write(0); _writer.Write(0); _writer.Write(0); _writer.Write(0);

        Fourcc("LIST"); long strlSize = _stream.Position; _writer.Write(0); Fourcc("strl");
        long strlStart = _stream.Position;

        Fourcc("strh"); _writer.Write(56);
        Fourcc("vids"); Fourcc("MJPG");
        _writer.Write(0); _writer.Write((short)0); _writer.Write((short)0); _writer.Write(0);
        _strhScalePos = _stream.Position; _writer.Write(1000); _writer.Write((int)(fps * 1000));
        _writer.Write(0);
        _strhLengthPos = _stream.Position; _writer.Write(0);
        _writer.Write(_width * _height * 3); _writer.Write(-1); _writer.Write(0);
        _writer.Write((short)0); _writer.Write((short)0); _writer.Write((short)_width); _writer.Write((short)_height);

        Fourcc("strf"); _writer.Write(40);
        _writer.Write(40); _writer.Write(_width); _writer.Write(_height);
        _writer.Write((short)1); _writer.Write((short)24); Fourcc("MJPG");
        _writer.Write(_width * _height * 3); _writer.Write(0); _writer.Write(0); _writer.Write(0); _writer.Write(0);

        PatchSize(strlSize, strlStart);
        PatchSize(hdrlSize, hdrlStart);

        Fourcc("LIST"); _moviSizePos = _stream.Position; _writer.Write(0); Fourcc("movi");
        _moviStart = _stream.Position - 4;
    }

    private void PatchSize(long sizePos, long dataStart)
    {
        long end = _stream.Position;
        _stream.Position = sizePos;
        _writer.Write((int)(end - dataStart + 4));
        _stream.Position = end;
    }

    /// <summary>Acrescenta um quadro já codificado em JPEG.</summary>
    public void AddFrame(byte[] jpeg)
    {
        long chunkPos = _stream.Position;
        Fourcc("00dc");
        _writer.Write(jpeg.Length);
        _writer.Write(jpeg);
        if ((jpeg.Length & 1) == 1) _writer.Write((byte)0);
        _index.Add((chunkPos - _moviStart, jpeg.Length));
    }

    /// <summary>Fecha o arquivo escrevendo o índice e os totais. <paramref name="actualFps"/> corrige a velocidade se a captura foi mais lenta.</summary>
    public void Finish(double actualFps)
    {
        if (_finished) return;
        _finished = true;

        long moviEnd = _stream.Position;
        _stream.Position = _moviSizePos;
        _writer.Write((int)(moviEnd - _moviSizePos - 4));
        _stream.Position = moviEnd;

        Fourcc("idx1");
        _writer.Write(_index.Count * 16);
        foreach (var (offset, size) in _index)
        {
            Fourcc("00dc"); _writer.Write(0x10); _writer.Write((int)offset); _writer.Write(size);
        }

        long end = _stream.Position;
        double fps = Math.Max(actualFps, 0.1);

        _stream.Position = _riffSizePos; _writer.Write((int)(end - 8));
        _stream.Position = _avihMicrosPos; _writer.Write((int)(1_000_000 / fps));
        _stream.Position = _avihFramesPos; _writer.Write(_index.Count);
        _stream.Position = _strhScalePos; _writer.Write(1000); _writer.Write((int)(fps * 1000));
        _stream.Position = _strhLengthPos; _writer.Write(_index.Count);
        _stream.Position = end;
        _writer.Flush();
    }

    public void Dispose()
    {
        _writer.Dispose();
        if (!_leaveOpen) _stream.Dispose();
    }
}
