using System.Buffers.Binary;
using System.Text;

namespace MBExtreme.Core.Gamepads;

// Protocolo dos controles PlayStation (DualShock 4, DualSense e DualSense Edge) para ler informações,
// calibrar o centro e o alcance dos analógicos e gravar na memória do controle.
// Baseado no projeto aberto dualshock-tools (https://github.com/dualshock-tools/dualshock-tools.github.io),
// licença MIT, Copyright (c) 2024 the_al. O aviso completo está em THIRD-PARTY.md.

public enum PadModel { DualShock4, DualSense, DualSenseEdge }

/// <summary>Canal HID com o controle. Os buffers seguem o Windows: o byte 0 é o número do relatório.</summary>
public interface IHidChannel : IDisposable
{
    /// <summary>Lê um relatório de recurso (feature). Devolve o buffer com o número do relatório no byte 0.</summary>
    byte[] GetFeature(byte reportId);

    /// <summary>Envia um relatório de recurso: <paramref name="payload"/> vem logo depois do número do relatório (o resto é zero).</summary>
    void SetFeature(byte reportId, ReadOnlySpan<byte> payload);

    /// <summary>Próximo relatório de entrada (estado dos botões e analógicos), ou null se nada chegou no tempo.</summary>
    byte[]? ReadInput(int timeoutMs);

    /// <summary>Tamanho do relatório de entrada: 64 bytes no cabo USB; maior no Bluetooth.</summary>
    int InputLength { get; }
}

public static class PadIds
{
    public const ushort Sony = 0x054C;

    public static PadModel? Identify(ushort vendor, ushort product) => vendor != Sony ? null : product switch
    {
        0x05C4 or 0x09CC => PadModel.DualShock4,
        0x0CE6 => PadModel.DualSense,
        0x0DF2 => PadModel.DualSenseEdge,
        _ => null,
    };

    public static string Name(PadModel model) => model switch
    {
        PadModel.DualShock4 => "DualShock 4",
        PadModel.DualSense => "DualSense",
        _ => "DualSense Edge",
    };

    public static readonly ushort[] Products = { 0x05C4, 0x09CC, 0x0CE6, 0x0DF2 };
}

/// <summary>Estado da memória de calibração: temporária (some ao desligar) ou permanente (grava de vez).</summary>
public enum NvStatus { Unknown, Temporary, Permanent, PendingReboot, Error }

/// <summary>Informações do controle.</summary>
public sealed record PadInfo(
    PadModel Model,
    string? Serial,
    string? Board,
    string? Firmware,
    string? BuildDate,
    string? Color,
    string? BluetoothAddress,
    bool LooksClone,
    string? CloneReason,
    NvStatus Memory);

/// <summary>Erro de um comando de calibração (o controle respondeu algo inesperado).</summary>
public sealed class PadCommandException : Exception
{
    public PadCommandException(string message) : base(message) { }
}

/// <summary>Comandos do controle PlayStation pelo cabo USB.</summary>
public sealed class SonyPad
{
    private readonly IHidChannel _hid;
    public PadModel Model { get; }

    public SonyPad(IHidChannel hid, PadModel model)
    {
        _hid = hid;
        Model = model;
    }

    /// <summary>Pausa entre um comando e a resposta (o DualShock 4 precisa de tempo).</summary>
    public Action<int> Sleep { get; set; } = ms => Thread.Sleep(ms);

    private bool Ds4 => Model == PadModel.DualShock4;

    /// <summary>Os 4 primeiros bytes da resposta, em ordem "big endian" (como o protocolo confere).</summary>
    internal static uint Head(byte[] report) => report.Length >= 4 ? BinaryPrimitives.ReadUInt32BigEndian(report) : 0;

    private byte[] Ask(byte setId, byte getId, params byte[] payload)
    {
        _hid.SetFeature(setId, payload);
        return _hid.GetFeature(getId);
    }

    // ---------------------------------------------------------------- informações

    /// <summary>Texto de um item de "informação do sistema" do DualSense (relatório 0x80/0x81).</summary>
    public string? SystemInfo(byte group, byte item, int length, bool text = true)
    {
        var r = Ask(0x80, 0x81, group, item);
        if (r.Length < 4 + length || r[1] != group || r[2] != item || r[3] != 2) return null;
        var slice = r.AsSpan(4, length);
        return text ? Encoding.ASCII.GetString(slice).TrimEnd('\0', ' ') : Convert.ToHexString(slice);
    }

    public PadInfo ReadInfo()
    {
        if (Ds4) return ReadInfoDs4();
        var r = _hid.GetFeature(0x20);
        if (r.Length < 60 || r[0] != 0x20) throw new PadCommandException("O controle não respondeu às informações (relatório 0x20).");
        string date = Encoding.ASCII.GetString(r, 1, 11).Trim('\0', ' ');
        string time = Encoding.ASCII.GetString(r, 12, 8).Trim('\0', ' ');
        uint hw = BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(24));
        uint fw = BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(28));
        string? serial = SafeInfo(() => SystemInfo(1, 19, 17));
        string? board = Model == PadModel.DualSense ? BoardDs5(hw) : null;
        string? reason = null;
        if (int.TryParse(date.Length >= 4 ? date[^4..] : "", out int year) && year > DateTime.Now.Year + 1) reason = "data do firmware impossível";
        else if (Model == PadModel.DualSense && SafeRead(ReadFinetune) is null) reason = "não responde aos comandos de calibração";
        return new PadInfo(Model, serial, board, $"0x{fw:X8}", $"{date} {time}", serial is { Length: >= 6 } ? ColorDs5(serial) : null,
            SafeInfo(BluetoothAddress), reason is not null, reason, QueryNv());
    }

    private PadInfo ReadInfoDs4()
    {
        var r = _hid.GetFeature(0xA3);
        bool clone = r.Length < 49 || r[0] != 0xA3;
        string date = clone ? "" : (Encoding.ASCII.GetString(r, 1, 15) + " " + Encoding.ASCII.GetString(r, 0x10, 16)).Replace("\0", "").Trim();
        ushort hwMinor = r.Length >= 0x25 ? BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(0x23)) : (ushort)0;
        uint sw = r.Length >= 0x29 ? BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(0x25)) : 0;
        string? reason = clone ? "resposta de informações fora do padrão" : null;
        if (!clone)
        {
            try { _hid.GetFeature(0x81); }
            catch (Exception ex) when (ex is IOException or TimeoutException or PadCommandException) { reason = "não responde a comandos do original"; }
        }
        string? mac = reason is null ? SafeInfo(() => FormatMac(_hid.GetFeature(0x12), 1)) : null;
        return new PadInfo(Model, mac, reason is null ? BoardDs4(hwMinor) : null, $"0x{sw:X8}", date, null, mac, reason is not null, reason,
            reason is null ? QueryNv() : NvStatus.Unknown);
    }

    private static string? SafeInfo(Func<string?> read)
    {
        try { return read(); }
        catch (Exception ex) when (ex is IOException or TimeoutException or PadCommandException) { return null; }
    }

    private static T? SafeRead<T>(Func<T?> read) where T : class
    {
        try { return read(); }
        catch (Exception ex) when (ex is IOException or TimeoutException or PadCommandException) { return null; }
    }

    public string? BluetoothAddress() => FormatMac(Ask(0x80, 0x81, 9, 2), 4);

    internal static string? FormatMac(byte[] r, int offset)
    {
        if (r.Length < offset + 6) return null;
        return string.Join(":", Enumerable.Range(0, 6).Select(i => r[offset + 5 - i].ToString("X2")));
    }

    public static string BoardDs5(uint hw) => ((hw >> 8) & 0xFF) switch
    {
        0x03 => "BDM-010",
        0x04 => "BDM-020",
        0x05 => "BDM-030",
        0x06 => "BDM-040",
        0x07 or 0x08 => "BDM-050",
        0x09 => "BDM-060R",
        0x11 => "BDM-060M",
        0x13 => "BDM-060X",
        _ => "Desconhecida",
    };

    public static string BoardDs4(ushort hw)
    {
        int a = hw >> 8;
        if (a == 0x31) return "JDM-001";
        if (a == 0x43) return "JDM-011";
        if (a == 0x54) return "JDM-030";
        if (a >= 0x64 && a <= 0x74) return "JDM-040";
        if ((a > 0x80 && a < 0x84) || a == 0x93) return "JDM-020";
        if (a is 0xA4 or 0x90 or 0xA0) return "JDM-050";
        if (a is 0xB0 or 0xB4) return "JDM-055";
        return "Desconhecida";
    }

    /// <summary>Cor do DualSense pelos caracteres 5 e 6 do número de série.</summary>
    public static string ColorDs5(string serial) => serial.Substring(4, 2) switch
    {
        "00" => "Branco",
        "01" => "Preto (Midnight Black)",
        "02" => "Vermelho (Cosmic Red)",
        "03" => "Rosa (Nova Pink)",
        "04" => "Roxo (Galactic Purple)",
        "05" => "Azul (Starlight Blue)",
        "06" => "Camuflado cinza",
        "07" => "Vermelho (Volcanic Red)",
        "08" => "Prata (Sterling Silver)",
        "09" => "Azul (Cobalt Blue)",
        "10" => "Chroma Teal",
        "11" => "Chroma Indigo",
        "12" => "Chroma Pearl",
        "30" => "Edição 30 anos",
        "Z1" => "God of War Ragnarök",
        "Z2" => "Spider-Man 2",
        "Z3" => "Astro Bot",
        "Z4" => "Fortnite",
        "Z6" => "The Last of Us",
        "ZA" => "God of War 20 anos",
        _ => "Desconhecida",
    };

    // ---------------------------------------------------------------- memória (temporária x permanente)

    public NvStatus QueryNv()
    {
        try
        {
            if (Ds4)
            {
                var d = Ask(0x08, 0x11, 0xFF, 0, 12);
                return d.Length < 2 ? NvStatus.Unknown : d[1] switch { 1 => NvStatus.Temporary, 0 => NvStatus.Permanent, _ => NvStatus.Unknown };
            }
            var r = Ask(0x80, 0x81, 3, 3);
            if (r.Length < 5) return NvStatus.Unknown;
            uint v = BinaryPrimitives.ReadUInt32BigEndian(r.AsSpan(1));
            return v switch
            {
                0x15010100 => NvStatus.PendingReboot,
                0x03030201 => NvStatus.Temporary,
                0x03030200 => NvStatus.Permanent,
                _ => NvStatus.Unknown,
            };
        }
        catch (Exception ex) when (ex is IOException or TimeoutException)
        {
            return NvStatus.Error;
        }
    }

    /// <summary>Grava a calibração atual na memória permanente do controle (destrava e trava a memória).</summary>
    public void SaveToController()
    {
        if (Model == PadModel.DualSenseEdge) throw new PadCommandException("Gravar no DualSense Edge ainda não é suportado.");
        if (Ds4)
        {
            _hid.SetFeature(0xA0, new byte[] { 10, 2, 0x3E, 0x71, 0x7F, 0x89 });
            _hid.SetFeature(0xA0, new byte[] { 10, 1, 0 });
            return;
        }
        Ask(0x80, 0x81, 3, 2, 101, 50, 64, 12);
        Ask(0x80, 0x81, 3, 1);
    }

    /// <summary>Reinicia o controle: o que não foi gravado volta ao que era.</summary>
    public void Reboot()
    {
        try
        {
            if (Ds4) _hid.SetFeature(0xA0, new byte[] { 4, 1, 0 });
            else _hid.SetFeature(0x80, new byte[] { 1, 1 });
        }
        catch (IOException) { /* o controle desconecta ao reiniciar */ }
    }

    // ---------------------------------------------------------------- calibração

    private void Expect(string what, uint got, params uint[] ok)
    {
        if (!ok.Contains(got)) throw new PadCommandException($"{what}: o controle respondeu 0x{got:X8}.");
    }

    /// <summary>Comando de calibração: modo 1 = centro, 2 = alcance; passo 1 = começar, 2 = terminar, 3 = amostra.</summary>
    private void Calib(byte step, byte kind, string what)
    {
        if (Ds4)
        {
            _hid.SetFeature(0x90, new byte[] { step, 1, kind });
            Sleep(200);
            uint d1 = Head(_hid.GetFeature(0x91)), d2 = Head(_hid.GetFeature(0x92));
            (uint, uint) want = (step, kind) switch
            {
                (1, 1) or (3, 1) => (0x91010101, 0x920101FF),
                (2, 1) => (0x91010102, 0x92010101),
                (1, 2) => (0x91010201, 0x920102FF),
                _ => (0x91010202, 0x92010201),
            };
            if (d1 != want.Item1 || d2 != want.Item2) throw new PadCommandException($"{what}: o controle respondeu 0x{d1:X8} / 0x{d2:X8}.");
            return;
        }
        _hid.SetFeature(0x82, new byte[] { step, 1, kind });
        uint r = Head(_hid.GetFeature(0x83));
        uint expected = (step, kind) switch
        {
            (1, 1) or (3, 1) => 0x83010101,
            (2, 1) => 0x83010102,
            (1, 2) => 0x83010201,
            _ => 0x83010202,
        };
        if (Model == PadModel.DualSenseEdge && step == 2)
        {
            // o Edge confirma o fim em duas respostas
            Expect(what, r, kind == 1 ? 0x83010101u : 0x83010201u, expected);
            _hid.SetFeature(0x82, new byte[] { 2, 1, kind });
            Expect(what, Head(_hid.GetFeature(0x83)), kind == 1 ? new uint[] { 0x83010103, 0x83010312 } : new uint[] { 0x83010203 });
            return;
        }
        Expect(what, r, expected);
    }

    public void CenterBegin() => Calib(1, 1, "Começar a calibração do centro");
    public void CenterSample() => Calib(3, 1, "Amostra do centro");
    public void CenterEnd() => Calib(2, 1, "Terminar a calibração do centro");
    public void RangeBegin() => Calib(1, 2, "Começar a calibração do alcance");
    public void RangeEnd() => Calib(2, 2, "Terminar a calibração do alcance");

    /// <summary>Centro completo: começa, tira 5 amostras com os analógicos soltos e termina.</summary>
    public void CalibrateCenter(Action<double>? progress = null, int samples = 5)
    {
        CenterBegin();
        progress?.Invoke(0.2);
        for (int i = 0; i < samples; i++)
        {
            Sleep(150);
            CenterSample();
            progress?.Invoke(0.2 + 0.7 * (i + 1) / samples);
        }
        Sleep(200);
        CenterEnd();
        progress?.Invoke(1);
    }

    // ---------------------------------------------------------------- ajuste fino (cópia de segurança da calibração)

    /// <summary>Os 12 valores de ajuste da calibração (DualSense). Null quando o controle não responde (clone).</summary>
    public ushort[]? ReadFinetune()
    {
        if (Ds4) return null;
        _hid.SetFeature(0x80, new byte[] { 12, (byte)(Model == PadModel.DualSenseEdge ? 4 : 2) });
        Sleep(100);
        var r = _hid.GetFeature(0x81);
        if (r.Length < 28 || r[0] != 0x81 || r[1] != 12 || (r[2] != 2 && r[2] != 4) || r[3] != 2) return null;
        return Enumerable.Range(0, 12).Select(i => BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(4 + i * 2))).ToArray();
    }

    /// <summary>Escreve os 12 valores de ajuste (na memória temporária; para valer de vez, grave depois).</summary>
    public void WriteFinetune(IReadOnlyList<ushort> values)
    {
        if (Ds4 || values.Count != 12) throw new PadCommandException("Ajuste fino só no DualSense, com 12 valores.");
        var payload = new byte[2 + 24];
        payload[0] = 12;
        payload[1] = 1;
        for (int i = 0; i < 12; i++) BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2 + i * 2), values[i]);
        _hid.SetFeature(0x80, payload);
    }
}
