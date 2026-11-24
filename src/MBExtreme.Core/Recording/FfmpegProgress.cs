using System.Globalization;

namespace MBExtreme.Core.Recording;

/// <summary>Estado atual lido do relatório de progresso do FFmpeg (-progress).</summary>
public sealed class FfmpegProgress
{
    public double Fps { get; private set; }

    public double BitrateKbps { get; private set; }

    public TimeSpan Elapsed { get; private set; }

    public long TotalBytes { get; private set; }

    public int DroppedFrames { get; private set; }

    public int DuplicatedFrames { get; private set; }

    public double Speed { get; private set; }

    /// <summary>Lê uma linha "chave=valor". Retorna true se alguma medida mudou.</summary>
    public bool Feed(string line)
    {
        int eq = line.IndexOf('=');
        if (eq <= 0) return false;
        string key = line[..eq].Trim();
        string value = line[(eq + 1)..].Trim();
        var inv = CultureInfo.InvariantCulture;
        switch (key)
        {
            case "fps":
                if (double.TryParse(value, NumberStyles.Float, inv, out double fps)) Fps = fps;
                return true;
            case "bitrate":
                // "1234.5kbits/s" ou "N/A"
                string number = value.Replace("kbits/s", "").Trim();
                BitrateKbps = double.TryParse(number, NumberStyles.Float, inv, out double kb) ? kb : 0;
                return true;
            case "out_time_us":
            case "out_time_ms":
                if (long.TryParse(value, NumberStyles.Integer, inv, out long us) && us >= 0) Elapsed = TimeSpan.FromTicks(us * 10);
                return true;
            case "total_size":
                if (long.TryParse(value, NumberStyles.Integer, inv, out long size)) TotalBytes = size;
                return true;
            case "drop_frames":
                if (int.TryParse(value, NumberStyles.Integer, inv, out int dropped)) DroppedFrames = dropped;
                return true;
            case "dup_frames":
                if (int.TryParse(value, NumberStyles.Integer, inv, out int dup)) DuplicatedFrames = dup;
                return true;
            case "speed":
                string speed = value.Replace("x", "").Trim();
                Speed = double.TryParse(speed, NumberStyles.Float, inv, out double sp) ? sp : 0;
                return true;
            default:
                return false;
        }
    }
}
