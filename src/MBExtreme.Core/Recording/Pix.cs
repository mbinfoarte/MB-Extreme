using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MBExtreme.Core.Recording;

/// <summary>Pix "copia e cola" (BR Code estático): gera o texto que vira o QR para receber doações. O Pix estático não avisa quando alguém paga: quem recebe confere no aplicativo do banco.</summary>
public static class PixPayload
{
    private static readonly Regex Email = new(@"^[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}$", RegexOptions.Compiled);
    private static readonly Regex Evp = new(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", RegexOptions.Compiled);
    private static readonly Regex Phone = new(@"^\+55\d{10,11}$", RegexOptions.Compiled);

    /// <summary>Devolve a chave no formato aceito pelo Pix (CPF e CNPJ só com números, e-mail em minúsculas, telefone com +55) ou null se não for uma chave válida.</summary>
    public static string? NormalizeKey(string? key)
    {
        string text = (key ?? "").Trim();
        if (text.Length == 0) return null;
        if (Email.IsMatch(text)) return text.ToLowerInvariant();
        if (Evp.IsMatch(text)) return text.ToLowerInvariant();
        if (text.StartsWith('+'))
        {
            string phone = "+" + new string(text.Where(char.IsDigit).ToArray());
            return Phone.IsMatch(phone) ? phone : null;
        }

        string digits = new(text.Where(char.IsDigit).ToArray());
        if (text.Any(c => !char.IsDigit(c) && c is not ('.' or '-' or '/' or ' '))) return null;
        if (digits.Length == 11 && ValidCpf(digits)) return digits;
        if (digits.Length == 14 && ValidCnpj(digits)) return digits;
        return null;
    }

    private static bool ValidCpf(string d)
    {
        if (d.Distinct().Count() == 1) return false;
        for (int t = 9; t < 11; t++)
        {
            int sum = 0;
            for (int i = 0; i < t; i++) sum += (d[i] - '0') * (t + 1 - i);
            int digit = sum * 10 % 11 % 10;
            if (digit != d[t] - '0') return false;
        }
        return true;
    }

    private static bool ValidCnpj(string d)
    {
        if (d.Distinct().Count() == 1) return false;
        int[] w1 = { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
        int[] w2 = { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
        int Digit(int count, int[] w)
        {
            int sum = 0;
            for (int i = 0; i < count; i++) sum += (d[i] - '0') * w[i];
            int mod = sum % 11;
            return mod < 2 ? 0 : 11 - mod;
        }
        return Digit(12, w1) == d[12] - '0' && Digit(13, w2) == d[13] - '0';
    }

    /// <summary>Nome e cidade do recebedor: sem acento, maiúsculas, só letras, números e espaço, no tamanho que o Pix aceita.</summary>
    public static string Clean(string? text, int maxLength)
    {
        string normalized = (text ?? "").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (char c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (c < 128 && (char.IsLetterOrDigit(c) || c == ' ')) sb.Append(char.ToUpperInvariant(c));
        }

        string clean = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        return clean.Length <= maxLength ? clean : clean[..maxLength].TrimEnd();
    }

    private static string Field(string id, string value) => id + value.Length.ToString("00", CultureInfo.InvariantCulture) + value;

    /// <summary>Monta o BR Code. Valor nulo ou zero: quem paga digita o valor.</summary>
    public static string Build(string key, string receiverName, string city, decimal? amount = null, string? description = null)
    {
        string? normalized = NormalizeKey(key);
        if (normalized is null) throw new ArgumentException("Chave Pix inválida.", nameof(key));
        string name = Clean(receiverName, 25);
        string town = Clean(city, 15);
        if (name.Length == 0) throw new ArgumentException("Informe o nome de quem recebe.", nameof(receiverName));
        if (town.Length == 0) throw new ArgumentException("Informe a cidade.", nameof(city));
        if (amount is { } a && (a < 0 || a > 99_999_999)) throw new ArgumentException("Valor inválido.", nameof(amount));

        string info = Field("00", "br.gov.bcb.pix") + Field("01", normalized);
        string note = Clean(description, 40);
        if (note.Length > 0) info += Field("02", note);
        if (info.Length > 99) throw new ArgumentException("Chave e descrição somados passam do tamanho que o Pix aceita. Encurte a descrição.", nameof(description));

        var sb = new StringBuilder();
        sb.Append(Field("00", "01"));
        sb.Append(Field("26", info));
        sb.Append(Field("52", "0000"));
        sb.Append(Field("53", "986"));
        if (amount is { } value && value > 0) sb.Append(Field("54", value.ToString("0.00", CultureInfo.InvariantCulture)));
        sb.Append(Field("58", "BR"));
        sb.Append(Field("59", name));
        sb.Append(Field("60", town));
        sb.Append(Field("62", Field("05", "***")));
        sb.Append("6304");
        sb.Append(Crc16(sb.ToString()).ToString("X4", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    /// <summary>CRC-16/CCITT-FALSE (polinômio 0x1021, início 0xFFFF), como o Pix exige.</summary>
    public static ushort Crc16(string text)
    {
        ushort crc = 0xFFFF;
        foreach (byte b in Encoding.ASCII.GetBytes(text))
        {
            crc ^= (ushort)(b << 8);
            for (int i = 0; i < 8; i++) crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }
        return crc;
    }
}
