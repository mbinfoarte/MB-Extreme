using System.Buffers.Binary;

namespace MBExtreme.Core.Remote;

public enum RemoteMessageType : byte { Frame = 1, Mouse = 2, Key = 3, Control = 4, Audio = 5 }

public enum MouseAction : byte { Move = 0, Down = 1, Up = 2, Wheel = 3 }

public enum MouseButton : byte { None = 0, Left = 1, Right = 2, Middle = 3 }

public enum ControlCode : byte { End = 1, Ping = 2, Pong = 3 }

/// <summary>Imagem da tela do cliente: tamanho real da tela (para mapear o mouse) e a imagem JPEG.</summary>
public sealed record FrameMessage(int ScreenWidth, int ScreenHeight, byte[] Jpeg);

/// <summary>Mouse com posição normalizada (0 a 65535 nos dois eixos), independente da resolução.</summary>
public sealed record MouseMessage(MouseAction Action, MouseButton Button, int X, int Y, int Wheel);

public sealed record KeyMessage(int VirtualKey, bool Down, bool Extended);

public sealed record ControlMessage(ControlCode Code);

/// <summary>Trecho do som do computador do cliente: PCM de 16 bits, mono, na taxa indicada.</summary>
public sealed record AudioMessage(int SampleRate, byte[] Pcm);

/// <summary>Formato das mensagens dentro do canal cifrado. A leitura recusa qualquer coisa fora do esperado.</summary>
public static class RemoteMessages
{
    public const int MaxFrameBytes = 4 * 1024 * 1024;

    public static byte[] Encode(FrameMessage m)
    {
        if (m.Jpeg.Length == 0 || m.Jpeg.Length > MaxFrameBytes) throw new ArgumentException("Imagem fora do tamanho permitido.");
        var data = new byte[5 + m.Jpeg.Length];
        data[0] = (byte)RemoteMessageType.Frame;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(1), (ushort)Math.Clamp(m.ScreenWidth, 1, ushort.MaxValue));
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(3), (ushort)Math.Clamp(m.ScreenHeight, 1, ushort.MaxValue));
        m.Jpeg.CopyTo(data, 5);
        return data;
    }

    public static byte[] Encode(MouseMessage m)
    {
        var data = new byte[9];
        data[0] = (byte)RemoteMessageType.Mouse;
        data[1] = (byte)m.Action;
        data[2] = (byte)m.Button;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(3), (ushort)Math.Clamp(m.X, 0, ushort.MaxValue));
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(5), (ushort)Math.Clamp(m.Y, 0, ushort.MaxValue));
        BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(7), (short)Math.Clamp(m.Wheel, short.MinValue, short.MaxValue));
        return data;
    }

    public static byte[] Encode(KeyMessage m)
    {
        var data = new byte[5];
        data[0] = (byte)RemoteMessageType.Key;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(1), (ushort)Math.Clamp(m.VirtualKey, 0, 255));
        data[3] = m.Down ? (byte)1 : (byte)0;
        data[4] = m.Extended ? (byte)1 : (byte)0;
        return data;
    }

    public const int MaxAudioBytes = 32 * 1024;

    public static byte[] Encode(AudioMessage m)
    {
        if (m.Pcm.Length == 0 || m.Pcm.Length > MaxAudioBytes || m.Pcm.Length % 2 != 0) throw new ArgumentException("Trecho de áudio fora do tamanho permitido.");
        var data = new byte[3 + m.Pcm.Length];
        data[0] = (byte)RemoteMessageType.Audio;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(1), (ushort)Math.Clamp(m.SampleRate, 8000, ushort.MaxValue));
        m.Pcm.CopyTo(data, 3);
        return data;
    }

    public static byte[] Encode(ControlMessage m) => new[] { (byte)RemoteMessageType.Control, (byte)m.Code };

    /// <summary>Lê uma mensagem; devolve nulo se o formato for inválido.</summary>
    public static object? Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return null;
        switch ((RemoteMessageType)data[0])
        {
            case RemoteMessageType.Frame:
                if (data.Length <= 5 || data.Length > 5 + MaxFrameBytes) return null;
                return new FrameMessage(BinaryPrimitives.ReadUInt16BigEndian(data[1..]), BinaryPrimitives.ReadUInt16BigEndian(data[3..]), data[5..].ToArray());
            case RemoteMessageType.Mouse:
                if (data.Length != 9 || data[1] > (byte)MouseAction.Wheel || data[2] > (byte)MouseButton.Middle) return null;
                return new MouseMessage((MouseAction)data[1], (MouseButton)data[2], BinaryPrimitives.ReadUInt16BigEndian(data[3..]),
                    BinaryPrimitives.ReadUInt16BigEndian(data[5..]), BinaryPrimitives.ReadInt16BigEndian(data[7..]));
            case RemoteMessageType.Key:
                if (data.Length != 5 || data[3] > 1 || data[4] > 1) return null;
                int vk = BinaryPrimitives.ReadUInt16BigEndian(data[1..]);
                return vk is > 0 and <= 255 ? new KeyMessage(vk, data[3] == 1, data[4] == 1) : null;
            case RemoteMessageType.Audio:
            {
                if (data.Length <= 3 || data.Length > 3 + MaxAudioBytes || (data.Length - 3) % 2 != 0) return null;
                int rate = BinaryPrimitives.ReadUInt16BigEndian(data[1..]);
                return rate >= 8000 ? new AudioMessage(rate, data[3..].ToArray()) : null;
            }
            case RemoteMessageType.Control:
                return data.Length == 2 && data[1] is >= 1 and <= 3 ? new ControlMessage((ControlCode)data[1]) : null;
            default:
                return null;
        }
    }

    /// <summary>Converte a posição normalizada para pixels da tela do cliente.</summary>
    public static (int X, int Y) ToScreen(int normalizedX, int normalizedY, int screenWidth, int screenHeight) =>
        (Math.Clamp((int)((long)normalizedX * (screenWidth - 1) / ushort.MaxValue), 0, Math.Max(0, screenWidth - 1)),
         Math.Clamp((int)((long)normalizedY * (screenHeight - 1) / ushort.MaxValue), 0, Math.Max(0, screenHeight - 1)));

    public static int Normalize(double position, double size) => size <= 0 ? 0 : (int)Math.Clamp(position / size * ushort.MaxValue, 0, ushort.MaxValue);

    /// <summary>Teclas que o técnico não pode enviar (Win+L, Ctrl+Alt+Del não passam por SendInput de qualquer forma).</summary>
    public static bool IsKeyAllowed(int virtualKey) => virtualKey is > 0 and <= 254 && virtualKey is not (0x5B or 0x5C); // Windows esquerda/direita
}
