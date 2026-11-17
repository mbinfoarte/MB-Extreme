namespace MBExtreme.Core.Recording;

/// <summary>Separa as imagens JPEG de um fluxo contínuo (MJPEG) que chega aos pedaços pela saída do FFmpeg.</summary>
public sealed class MjpegSplitter
{
    private byte[] _buffer = new byte[1 << 16];
    private int _length;
    private const int MaxFrame = 4 * 1024 * 1024;

    /// <summary>Entrega os dados que chegaram e devolve cada imagem completa (começa em FF D8, termina em FF D9).</summary>
    public IReadOnlyList<byte[]> Push(ReadOnlySpan<byte> data)
    {
        if (_length + data.Length > _buffer.Length) Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _length + data.Length));
        data.CopyTo(_buffer.AsSpan(_length));
        _length += data.Length;

        var frames = new List<byte[]>();
        int start = 0;
        while (true)
        {
            int soi = IndexOf(0xD8, start);
            if (soi < 0) { start = Math.Max(start, _length - 1); break; }   // nada de começo ainda: guarda só o último byte (pode ser o FF)
            int eoi = IndexOf(0xD9, soi + 2);
            if (eoi < 0)
            {
                start = soi;
                if (_length - soi > MaxFrame) start = _length;               // lixo grande demais: descarta
                break;
            }
            frames.Add(_buffer.AsSpan(soi, eoi + 2 - soi).ToArray());
            start = eoi + 2;
        }

        if (start > 0)
        {
            Buffer.BlockCopy(_buffer, start, _buffer, 0, _length - start);
            _length -= start;
        }
        return frames;
    }

    // procura o par FF xx
    private int IndexOf(byte second, int from)
    {
        for (int i = Math.Max(0, from); i + 1 < _length; i++)
            if (_buffer[i] == 0xFF && _buffer[i + 1] == second) return i;
        return -1;
    }
}

/// <summary>Ajustes de cor aplicados à pré-visualização da tela (a imagem final usa o FFmpeg; aqui é só uma amostra fiel, em tempo real).</summary>
public static class ColorPreview
{
    /// <summary>Aplica brilho, contraste, saturação, gama e vinheta a pixels BGRA (4 bytes por pixel), no lugar.</summary>
    public static void Apply(byte[] bgra, int width, int height, int stride, ImageFilter filter)
    {
        var f = filter.Clamped();
        if (f.IsNeutral || (Math.Abs(f.Brightness) < 0.005 && Math.Abs(f.Contrast - 1) < 0.005 && Math.Abs(f.Saturation - 1) < 0.005 && Math.Abs(f.Gamma - 1) < 0.005 && f.Vignette <= 0)) return;

        var lut = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            double v = i / 255.0;
            v = (v - 0.5) * f.Contrast + 0.5 + f.Brightness;
            v = Math.Pow(Math.Clamp(v, 0, 1), 1.0 / f.Gamma);
            lut[i] = (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
        }

        double strength = f.Vignette switch { 1 => 0.25, 2 => 0.45, 3 => 0.65, _ => 0 };
        double cx = (width - 1) / 2.0, cy = (height - 1) / 2.0, maxDist = Math.Sqrt(cx * cx + cy * cy);
        for (int y = 0; y < height; y++)
        {
            int row = y * stride;
            for (int x = 0; x < width; x++)
            {
                int p = row + x * 4;
                if (p + 3 >= bgra.Length) return;
                double b = bgra[p], g = bgra[p + 1], r = bgra[p + 2];
                double luma = 0.114 * b + 0.587 * g + 0.299 * r;
                b = luma + (b - luma) * f.Saturation;
                g = luma + (g - luma) * f.Saturation;
                r = luma + (r - luma) * f.Saturation;
                double scale = 1;
                if (strength > 0)
                {
                    double d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / maxDist;
                    scale = 1 - strength * d * d;
                }
                bgra[p] = (byte)(lut[(int)Math.Clamp(b, 0, 255)] * scale);
                bgra[p + 1] = (byte)(lut[(int)Math.Clamp(g, 0, 255)] * scale);
                bgra[p + 2] = (byte)(lut[(int)Math.Clamp(r, 0, 255)] * scale);
            }
        }
    }
}
