using System.Buffers.Binary;

namespace MBExtreme.Core.Gamepads;

/// <summary>Botões do controle PlayStation (bits).</summary>
[Flags]
public enum PadButtons : uint
{
    None = 0,
    Up = 1 << 0, Right = 1 << 1, Down = 1 << 2, Left = 1 << 3,
    Square = 1 << 4, Cross = 1 << 5, Circle = 1 << 6, Triangle = 1 << 7,
    L1 = 1 << 8, R1 = 1 << 9, L2 = 1 << 10, R2 = 1 << 11,
    Create = 1 << 12, Options = 1 << 13, L3 = 1 << 14, R3 = 1 << 15,
    PS = 1 << 16, Touchpad = 1 << 17, Mute = 1 << 18,
}

/// <summary>
/// Estado do controle num instante. Analógicos de -1 a 1 (Y positivo para baixo, como na tela), gatilhos de 0 a 1,
/// giroscópio em graus por segundo, acelerômetro em g, bateria de 0 a 100.
/// </summary>
public sealed record PadState(
    double LX, double LY, double RX, double RY,
    double L2, double R2,
    PadButtons Buttons,
    double GyroPitch, double GyroYaw, double GyroRoll,
    double AccelX, double AccelY, double AccelZ,
    int Battery, bool Charging, bool Cable)
{
    public static readonly PadState Empty = new(0, 0, 0, 0, 0, 0, PadButtons.None, 0, 0, 0, 0, 0, 0, 0, false, false);
}

public static class PadInput
{
    /// <summary>Analógico de 0..255 para -1..1 (o centro 127,5 vira 0).</summary>
    public static double Axis(byte raw) => Math.Round((raw - 127.5) / 128.0, 4);

    private static PadButtons Dpad(int hat) => hat switch
    {
        0 => PadButtons.Up,
        1 => PadButtons.Up | PadButtons.Right,
        2 => PadButtons.Right,
        3 => PadButtons.Right | PadButtons.Down,
        4 => PadButtons.Down,
        5 => PadButtons.Down | PadButtons.Left,
        6 => PadButtons.Left,
        7 => PadButtons.Left | PadButtons.Up,
        _ => PadButtons.None,
    };

    /// <summary>
    /// Lê o relatório de entrada pelo cabo USB (relatório 0x01, com o número no byte 0).
    /// DualSense: analógicos 1-4, gatilhos 5-6, botões 8-10, sensores a partir do 16, bateria 53.
    /// DualShock 4: analógicos 1-4, botões 5-7, gatilhos 8-9, sensores a partir do 13, bateria 30.
    /// </summary>
    public static PadState? Parse(PadModel model, byte[] r)
    {
        if (r.Length < 32 || r[0] != 0x01) return null;
        bool ds4 = model == PadModel.DualShock4;
        int buttons = ds4 ? 5 : 8, l2 = ds4 ? 8 : 5, r2 = ds4 ? 9 : 6, imu = ds4 ? 13 : 16, bat = ds4 ? 30 : 53;
        if (r.Length <= Math.Max(imu + 12, bat)) return null;

        var b = Dpad(r[buttons] & 0x0F);
        if ((r[buttons] & 0x10) != 0) b |= PadButtons.Square;
        if ((r[buttons] & 0x20) != 0) b |= PadButtons.Cross;
        if ((r[buttons] & 0x40) != 0) b |= PadButtons.Circle;
        if ((r[buttons] & 0x80) != 0) b |= PadButtons.Triangle;
        byte b1 = r[buttons + 1], b2 = r[buttons + 2];
        if ((b1 & 0x01) != 0) b |= PadButtons.L1;
        if ((b1 & 0x02) != 0) b |= PadButtons.R1;
        if ((b1 & 0x04) != 0) b |= PadButtons.L2;
        if ((b1 & 0x08) != 0) b |= PadButtons.R2;
        if ((b1 & 0x10) != 0) b |= PadButtons.Create;
        if ((b1 & 0x20) != 0) b |= PadButtons.Options;
        if ((b1 & 0x40) != 0) b |= PadButtons.L3;
        if ((b1 & 0x80) != 0) b |= PadButtons.R3;
        if ((b2 & 0x01) != 0) b |= PadButtons.PS;
        if ((b2 & 0x02) != 0) b |= PadButtons.Touchpad;
        if (!ds4 && (b2 & 0x04) != 0) b |= PadButtons.Mute;

        short S(int at) => BinaryPrimitives.ReadInt16LittleEndian(r.AsSpan(at));
        const double gyro = 14.31, accel = 8192.0;

        int level; bool charging, cable;
        byte battery = r[bat];
        if (ds4)
        {
            int data = battery & 0x0F;
            cable = ((battery >> 4) & 1) == 1;
            charging = cable && data <= 10;
            level = cable ? (data < 10 ? Math.Min(data * 10 + 5, 100) : data <= 11 ? 100 : 0) : Math.Min(data * 10 + 5, 100);
        }
        else
        {
            int charge = battery & 0x0F, status = battery >> 4;
            charging = status == 1;
            cable = status is 1 or 2;
            level = status == 2 ? 100 : status == 15 ? 0 : Math.Min(charge * 10 + 5, 100);
        }

        return new PadState(Axis(r[1]), Axis(r[2]), Axis(r[3]), Axis(r[4]), r[l2] / 255.0, r[r2] / 255.0, b,
            S(imu) / gyro, S(imu + 2) / gyro, S(imu + 4) / gyro, S(imu + 6) / accel, S(imu + 8) / accel, S(imu + 10) / accel,
            level, charging, cable);
    }
}

/// <summary>
/// Circularidade de um analógico: guarda a maior distância do centro em 48 direções enquanto a pessoa gira.
/// O erro é o desvio médio (raiz quadrática) dessa borda em relação ao círculo perfeito, em %.
/// </summary>
public sealed class CircularityMap
{
    public const int Size = 48;
    private readonly double[] _max = new double[Size];

    public IReadOnlyList<double> Values => _max;

    public void Add(double x, double y)
    {
        double d = Math.Sqrt(x * x + y * y);
        int i = ((int)Math.Round(Math.Atan2(y, x) * Size / 2.0 / Math.PI) + Size) % Size;
        if (d > _max[i]) _max[i] = d;
    }

    public void Clear() => Array.Clear(_max);

    /// <summary>Quantas direções já foram alcançadas perto da borda (0 a 1).</summary>
    public double Coverage => _max.Count(v => v > 0.8) / (double)Size;

    public double Error
    {
        get
        {
            var valid = _max.Where(v => v > 0.2).ToList();
            return valid.Count == 0 ? 0 : Math.Sqrt(valid.Sum(v => (v - 1) * (v - 1)) / valid.Count) * 100;
        }
    }
}

/// <summary>Drift: quanto o analógico se afasta do centro com ele solto.</summary>
public sealed class DriftMeter
{
    private double _sumX, _sumY, _max;
    private int _n;

    public void Add(double x, double y)
    {
        _sumX += x; _sumY += y; _n++;
        _max = Math.Max(_max, Math.Sqrt(x * x + y * y));
    }

    public void Clear() { _sumX = _sumY = _max = 0; _n = 0; }

    public int Samples => _n;
    public double OffsetX => _n == 0 ? 0 : _sumX / _n;
    public double OffsetY => _n == 0 ? 0 : _sumY / _n;

    /// <summary>Distância média do centro (0 a 1).</summary>
    public double Offset => Math.Sqrt(OffsetX * OffsetX + OffsetY * OffsetY);

    /// <summary>Maior distância vista com o analógico solto.</summary>
    public double Peak => _max;

    /// <summary>Um passo do analógico vale 1/128 (0,78%); até 2 passos é normal.</summary>
    public static string Verdict(double peak) => peak <= 0.025 ? "Sem drift" : peak <= 0.07 ? "Drift leve" : peak <= 0.15 ? "Drift" : "Drift forte";

    /// <summary>Nos jogos, a zona morta comum é 10%: abaixo dela o drift não aparece.</summary>
    public static bool VisibleInGames(double peak) => peak > 0.1;
}
