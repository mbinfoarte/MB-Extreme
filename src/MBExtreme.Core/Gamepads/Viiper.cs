using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace MBExtreme.Core.Gamepads;

/// <summary>
/// Formato das mensagens do VIIPER (servidor de controles virtuais por USBIP, https://github.com/Alia5/VIIPER). O servidor é um programa à
/// parte (GPL-3.0), instalado pela própria pessoa; o MB Extreme só conversa com ele pela API TCP documentada (as bibliotecas cliente
/// oficiais são MIT). Implementação própria a partir da documentação da API e do dispositivo DualSense.
/// </summary>
public static class ViiperWire
{
    public const int ApiPort = 3242;
    public const string ReleasesUrl = "https://github.com/Alia5/VIIPER/releases/latest";
    public const string UsbipUrl = "https://github.com/vadimgrn/usbip-win2/releases/latest";
    public const int DualSenseInputSize = 33;
    public const int DualSenseFeedbackSize = 6;

    /// <summary>Pedido: caminho, payload opcional e o byte nulo no fim.</summary>
    public static byte[] Request(string path, string? payload = null) =>
        Encoding.UTF8.GetBytes((payload is null ? path : path + " " + payload) + "\0");

    /// <summary>Tipo do aparelho no VIIPER para o controle virtual escolhido.</summary>
    public static string DeviceType(VirtualTarget target) => target == VirtualTarget.DualSenseEdge ? "dualsenseedge" : "dualsense";

    private static sbyte Stick(double v) => (sbyte)Math.Clamp(Math.Round(Math.Clamp(v, -1, 1) * 127), -128, 127);
    private static short Clamp16(double v) => (short)Math.Clamp(Math.Round(v), short.MinValue, short.MaxValue);

    /// <summary>
    /// Estado do controle → pacote de 33 bytes do DualSense virtual (little-endian): analógicos int8 (Y positivo para baixo, como o
    /// controle de verdade), botões uint32 (os mesmos bits do nosso <see cref="PadButtons"/> de □ até Mudo), direcional
    /// (cima 1, baixo 2, esquerda 4, direita 8), gatilhos, toques (desligados), giroscópio (16,384 por grau/s) e acelerômetro (8192 por g).
    /// </summary>
    public static byte[] DualSense(PadState s)
    {
        var b = new byte[DualSenseInputSize];
        b[0] = (byte)Stick(s.LX);
        b[1] = (byte)Stick(s.LY);
        b[2] = (byte)Stick(s.RX);
        b[3] = (byte)Stick(s.RY);
        uint buttons = (uint)s.Buttons & 0x7FFF0;   // □ ✕ ○ △ L1 R1 L2 R2 Create Options L3 R3 PS Touchpad Mudo
        if (s.L2 > 0.5) buttons |= (uint)PadButtons.L2;
        if (s.R2 > 0.5) buttons |= (uint)PadButtons.R2;
        BitConverter.TryWriteBytes(b.AsSpan(4, 4), buttons);
        byte dpad = 0;
        if ((s.Buttons & PadButtons.Up) != 0) dpad |= 0x01;
        if ((s.Buttons & PadButtons.Down) != 0) dpad |= 0x02;
        if ((s.Buttons & PadButtons.Left) != 0) dpad |= 0x04;
        if ((s.Buttons & PadButtons.Right) != 0) dpad |= 0x08;
        b[8] = dpad;
        b[9] = PadMapper.TriggerByte(s.L2);
        b[10] = PadMapper.TriggerByte(s.R2);
        // 11..20: dois toques do touchpad, desligados
        BitConverter.TryWriteBytes(b.AsSpan(21, 2), Clamp16(s.GyroPitch * 16.384));
        BitConverter.TryWriteBytes(b.AsSpan(23, 2), Clamp16(s.GyroYaw * 16.384));
        BitConverter.TryWriteBytes(b.AsSpan(25, 2), Clamp16(s.GyroRoll * 16.384));
        bool noAccel = s.AccelX == 0 && s.AccelY == 0 && s.AccelZ == 0;
        BitConverter.TryWriteBytes(b.AsSpan(27, 2), Clamp16(s.AccelX * 8192));
        BitConverter.TryWriteBytes(b.AsSpan(29, 2), Clamp16(s.AccelY * 8192));
        BitConverter.TryWriteBytes(b.AsSpan(31, 2), noAccel ? (short)-8192 : Clamp16(s.AccelZ * 8192));   // sem sensor: controle deitado
        return b;
    }

    /// <summary>Retorno do jogo (6 bytes): vibração fraca e forte, cor da barra de luz e luzes do jogador.</summary>
    public readonly record struct Feedback(byte RumbleSmall, byte RumbleLarge, byte Red, byte Green, byte Blue, byte PlayerLeds);

    public static Feedback? ParseFeedback(ReadOnlySpan<byte> b) =>
        b.Length < DualSenseFeedbackSize ? null : new Feedback(b[0], b[1], b[2], b[3], b[4], b[5]);

    /// <summary>Lê a resposta JSON: erro (status ≥ 400, no estilo RFC 7807) vira exceção com o texto do servidor.</summary>
    public static JsonElement Parse(string response)
    {
        string text = response.Trim().TrimEnd('\0').Trim();
        if (text.Length == 0) return default;
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement.Clone();
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.Number && status.GetInt32() >= 400)
        {
            string title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
            string detail = root.TryGetProperty("detail", out var d) ? d.GetString() ?? "" : "";
            throw new InvalidOperationException($"VIIPER: {title} {detail}".Trim());
        }
        return root;
    }
}

/// <summary>Conversa com o servidor VIIPER local (127.0.0.1:3242): cria o barramento, o controle virtual e abre o canal de dados.</summary>
public sealed class ViiperClient
{
    private readonly string _host;
    private readonly int _port;

    public ViiperClient(string host = "127.0.0.1", int port = ViiperWire.ApiPort)
    {
        _host = host;
        _port = port;
    }

    /// <summary>Um pedido: conecta, envia, lê até o servidor fechar.</summary>
    public string Send(string path, string? payload = null, int timeoutMs = 3000)
    {
        using var tcp = new TcpClient { NoDelay = true, ReceiveTimeout = timeoutMs, SendTimeout = timeoutMs };
        if (!tcp.ConnectAsync(_host, _port).Wait(timeoutMs)) throw new IOException("O VIIPER não respondeu.");
        using var stream = tcp.GetStream();
        stream.Write(ViiperWire.Request(path, payload));
        using var ms = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            ms.Write(buffer, 0, read);
            if (ms.Length > 1_000_000) break;
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>Versão do servidor, ou null se ele não está rodando.</summary>
    public string? Ping()
    {
        try
        {
            var json = ViiperWire.Parse(Send("ping", timeoutMs: 800));
            return json.ValueKind == JsonValueKind.Object && json.TryGetProperty("version", out var v) ? v.GetString() ?? "?" : "?";
        }
        catch (Exception ex) when (ex is IOException or SocketException or AggregateException or InvalidOperationException or JsonException) { return null; }
    }

    public int CreateBus()
    {
        var json = ViiperWire.Parse(Send("bus/create"));
        return json.GetProperty("busId").GetInt32();
    }

    public string AddDevice(int bus, string type)
    {
        var json = ViiperWire.Parse(Send($"bus/{bus}/add", JsonSerializer.Serialize(new { type })));
        var dev = json.GetProperty("devId");
        return dev.ValueKind == JsonValueKind.Number ? dev.GetInt32().ToString() : dev.GetString() ?? "1";
    }

    public void RemoveBus(int bus)
    {
        try { ViiperWire.Parse(Send("bus/remove", bus.ToString(), 1500)); }
        catch (Exception ex) when (ex is IOException or SocketException or AggregateException or InvalidOperationException or JsonException) { }
    }

    /// <summary>Abre o canal de dados do aparelho (o controle some se ninguém se conectar em 5 s).</summary>
    public TcpClient OpenStream(int bus, string device)
    {
        var tcp = new TcpClient { NoDelay = true };
        if (!tcp.ConnectAsync(_host, _port).Wait(3000)) { tcp.Dispose(); throw new IOException("O VIIPER não respondeu."); }
        tcp.GetStream().Write(ViiperWire.Request($"bus/{bus}/{device}"));
        return tcp;
    }

    /// <summary>Onde o instalador do VIIPER coloca o programa.</summary>
    public static string DefaultExe => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VIIPER", "viiper.exe");
}
