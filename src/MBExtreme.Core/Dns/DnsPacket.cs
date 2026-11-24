using System.Text;

namespace MBExtreme.Core.Dns;

/// <summary>Montagem e validação mínima de pacotes DNS (consulta tipo A, classe IN, via UDP).</summary>
public static class DnsPacket
{
    public static byte[] BuildQuery(ushort id, string host)
    {
        var packet = new List<byte>
        {
            (byte)(id >> 8), (byte)(id & 0xFF),
            0x01, 0x00,             // flags: recursão desejada
            0x00, 0x01,             // 1 pergunta
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        };

        foreach (var label in host.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            if (bytes.Length is 0 or > 63)
                throw new ArgumentException($"Rótulo DNS inválido em '{host}'.", nameof(host));
            packet.Add((byte)bytes.Length);
            packet.AddRange(bytes);
        }

        packet.Add(0x00);           // fim do nome
        packet.AddRange(new byte[] { 0x00, 0x01, 0x00, 0x01 }); // tipo A, classe IN
        return packet.ToArray();
    }

    /// <summary>Resposta válida: mesmo id, bit de resposta ligado e código de erro zero.</summary>
    public static bool IsSuccessResponse(byte[] data, int length, ushort id)
    {
        if (length < 12) return false;
        ushort responseId = (ushort)((data[0] << 8) | data[1]);
        bool isResponse = (data[2] & 0x80) != 0;
        int rcode = data[3] & 0x0F;
        return responseId == id && isResponse && rcode == 0;
    }
}
