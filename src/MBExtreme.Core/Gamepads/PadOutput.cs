using System.Buffers.Binary;

namespace MBExtreme.Core.Gamepads;

/// <summary>Modo da barra de luz.</summary>
public enum LightMode { Static, Breathing, Rainbow, Strobe, Battery }

/// <summary>Luz do botão de mudo (DualSense).</summary>
public enum MuteLed { Off, On, Pulse }

/// <summary>Efeito de um gatilho adaptável (DualSense).</summary>
public enum TriggerKind { Off, Resistance, Weapon, Vibration }

/// <summary>Configuração de um gatilho adaptável. Posições 0-9 (curso do gatilho), força 1-8, frequência 1-255 Hz.</summary>
public sealed class TriggerEffect
{
    public TriggerKind Kind { get; set; }
    public int Start { get; set; } = 2;
    public int End { get; set; } = 6;
    public int Strength { get; set; } = 5;
    public int Frequency { get; set; } = 30;

    public TriggerEffect Clone() => (TriggerEffect)MemberwiseClone();
}

/// <summary>Luz, gatilhos e vibração que o MB Control manda para o controle (salvo nas configurações).</summary>
public sealed class PadLightSettings
{
    public bool Enabled { get; set; }
    public LightMode Mode { get; set; } = LightMode.Static;
    public byte R { get; set; } = 0xFF;
    public byte G { get; set; } = 0xC4;
    public byte B { get; set; }
    public int Brightness { get; set; } = 100;
    public int Speed { get; set; } = 30;
    public int PlayerLeds { get; set; } = 1;
    public int PlayerBrightness { get; set; }
    public MuteLed Mute { get; set; }
    public TriggerEffect Left { get; set; } = new();
    public TriggerEffect Right { get; set; } = new();
}

/// <summary>O que vai num pacote de saída.</summary>
public sealed record PadOutputFrame(byte R, byte G, byte B, int PlayerMask, int PlayerBrightness, MuteLed Mute,
    TriggerEffect Left, TriggerEffect Right, byte RumbleStrong, byte RumbleWeak);

/// <summary>Monta os pacotes de saída do DualSense e do DualShock 4, pelo cabo (sem verificação) e pelo Bluetooth (com CRC32).</summary>
public static class PadOutput
{
    /// <summary>Luzes do jogador acesas para o número 1 a 5 (padrão do PS5: 1 = só a do meio).</summary>
    public static int PlayerMask(int player) => player switch
    {
        <= 0 => 0,
        1 => 0b00100,
        2 => 0b01010,
        3 => 0b10101,
        4 => 0b11011,
        _ => 0b11111,
    };

    /// <summary>Cor da barra de luz no instante <paramref name="ms"/>, conforme o modo, a velocidade (1-100) e o brilho (0-100).</summary>
    public static (byte R, byte G, byte B) LightColor(PadLightSettings s, double ms, int battery)
    {
        double speed = Math.Clamp(s.Speed, 1, 100);
        double k = Math.Clamp(s.Brightness, 0, 100) / 100.0;
        double r = s.R, g = s.G, b = s.B;
        switch (s.Mode)
        {
            case LightMode.Breathing:
                double period = 6000 - speed * 50;   // 6 s (lento) a 1 s (rápido)
                k *= (1 - Math.Cos(2 * Math.PI * (ms % period) / period)) / 2;
                break;
            case LightMode.Rainbow:
                double hue = (ms * speed / 20) % 360;
                (r, g, b) = Hsv(hue);
                break;
            case LightMode.Strobe:
                double cycle = 1100 - speed * 10;     // 1,1 s a 0,1 s
                if (ms % cycle > cycle / 2) k = 0;
                break;
            case LightMode.Battery:
                int level = battery < 0 ? 100 : Math.Clamp(battery, 0, 100);
                (r, g, b) = level >= 50 ? (255 * (100 - level) / 50.0, 255.0, 0.0) : (255.0, 255 * level / 50.0, 0.0);
                break;
        }
        return ((byte)Math.Round(r * k), (byte)Math.Round(g * k), (byte)Math.Round(b * k));
    }

    private static (double, double, double) Hsv(double h)
    {
        double x = 1 - Math.Abs(h / 60 % 2 - 1);
        (double r, double g, double b) = (h / 60) switch { < 1 => (1.0, x, 0.0), < 2 => (x, 1.0, 0.0), < 3 => (0.0, 1.0, x), < 4 => (0.0, x, 1.0), < 5 => (x, 0.0, 1.0), _ => (1.0, 0.0, x) };
        return (r * 255, g * 255, b * 255);
    }

    /// <summary>Os 11 bytes do efeito do gatilho adaptável.</summary>
    public static byte[] TriggerBytes(TriggerEffect e)
    {
        var t = new byte[11];
        int strength = Math.Clamp(e.Strength, 1, 8) - 1;
        switch (e.Kind)
        {
            case TriggerKind.Resistance:
            {
                int start = Math.Clamp(e.Start, 0, 9);
                ushort zones = 0;
                uint forces = 0;
                for (int i = start; i < 10; i++)
                {
                    zones |= (ushort)(1 << i);
                    forces |= (uint)strength << (3 * i);
                }
                t[0] = 0x21;
                BinaryPrimitives.WriteUInt16LittleEndian(t.AsSpan(1), zones);
                BinaryPrimitives.WriteUInt32LittleEndian(t.AsSpan(3), forces);
                break;
            }
            case TriggerKind.Weapon:
            {
                int start = Math.Clamp(e.Start, 2, 7);
                int end = Math.Clamp(e.End, start + 1, 8);
                t[0] = 0x25;
                BinaryPrimitives.WriteUInt16LittleEndian(t.AsSpan(1), (ushort)((1 << start) | (1 << end)));
                t[3] = (byte)strength;
                break;
            }
            case TriggerKind.Vibration:
            {
                int start = Math.Clamp(e.Start, 0, 9);
                ushort zones = 0;
                uint amps = 0;
                for (int i = start; i < 10; i++)
                {
                    zones |= (ushort)(1 << i);
                    amps |= (uint)strength << (3 * i);
                }
                t[0] = 0x26;
                BinaryPrimitives.WriteUInt16LittleEndian(t.AsSpan(1), zones);
                BinaryPrimitives.WriteUInt32LittleEndian(t.AsSpan(3), amps);
                t[9] = (byte)Math.Clamp(e.Frequency, 1, 255);
                break;
            }
            default:
                t[0] = 0x05;
                break;
        }
        return t;
    }

    /// <summary>Os 47 bytes comuns do DualSense (iguais no USB e no Bluetooth).</summary>
    private static byte[] DualSenseCommon(PadOutputFrame f)
    {
        var c = new byte[47];
        c[0] = 0x01 | 0x02 | 0x04 | 0x08;        // vibração compatível + gatilhos direito e esquerdo
        c[1] = 0x01 | 0x04 | 0x10;               // luz do mudo + barra de luz + luzes do jogador
        c[2] = f.RumbleWeak;                     // motor direito (leve)
        c[3] = f.RumbleStrong;                   // motor esquerdo (forte)
        c[8] = (byte)f.Mute;
        TriggerBytes(f.Right).CopyTo(c, 10);
        TriggerBytes(f.Left).CopyTo(c, 21);
        c[38] = 0x02;                            // controlar a "luz de entrada" azul
        c[41] = 0x02;                            // apagar a luz de entrada para valer a nossa cor
        c[42] = (byte)Math.Clamp(f.PlayerBrightness, 0, 2);
        c[43] = (byte)(f.PlayerMask & 0x1F);
        c[44] = f.R; c[45] = f.G; c[46] = f.B;
        return c;
    }

    public static byte[] DualSense(PadOutputFrame f, bool bluetooth, byte sequence = 0)
    {
        var common = DualSenseCommon(f);
        if (!bluetooth)
        {
            var usb = new byte[48];
            usb[0] = 0x02;
            common.CopyTo(usb, 1);
            return usb;
        }
        var bt = new byte[78];
        bt[0] = 0x31;
        bt[1] = (byte)((sequence & 0x0F) << 4);
        bt[2] = 0x10;
        common.CopyTo(bt, 3);
        WriteCrc(bt);
        return bt;
    }

    public static byte[] DualShock4(PadOutputFrame f, bool bluetooth)
    {
        if (!bluetooth)
        {
            var usb = new byte[32];
            usb[0] = 0x05;
            usb[1] = 0x07;          // vibração + luz + piscar
            usb[4] = f.RumbleWeak;
            usb[5] = f.RumbleStrong;
            usb[6] = f.R; usb[7] = f.G; usb[8] = f.B;
            return usb;
        }
        var bt = new byte[78];
        bt[0] = 0x11;
        bt[1] = 0xC0;               // relatório HID com CRC
        bt[3] = 0x07;
        bt[6] = f.RumbleWeak;
        bt[7] = f.RumbleStrong;
        bt[8] = f.R; bt[9] = f.G; bt[10] = f.B;
        WriteCrc(bt);
        return bt;
    }

    /// <summary>CRC32 do Bluetooth: começa com o byte 0xA2 e cobre tudo menos os 4 últimos bytes, que recebem o resultado.</summary>
    private static void WriteCrc(byte[] report)
    {
        uint crc = Crc32(0xFFFFFFFF, 0xA2);
        for (int i = 0; i < report.Length - 4; i++) crc = Crc32(crc, report[i]);
        BinaryPrimitives.WriteUInt32LittleEndian(report.AsSpan(report.Length - 4), ~crc);
    }

    public static uint BluetoothCrc(ReadOnlySpan<byte> data)
    {
        uint crc = Crc32(0xFFFFFFFF, 0xA2);
        foreach (byte b in data) crc = Crc32(crc, b);
        return ~crc;
    }

    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            t[i] = c;
        }
        return t;
    }

    private static uint Crc32(uint crc, byte b) => Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
}
