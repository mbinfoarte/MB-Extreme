using System.Security.Cryptography;

namespace MBExtreme.Core.Remote;

/// <summary>ID numérico de 9 dígitos (o que o cliente informa ao técnico) e senha de uso único da sessão.</summary>
public static class RemoteIds
{
    // sem vogais nem caracteres parecidos (0/O, 1/I): fácil de ditar por telefone
    private const string PasswordAlphabet = "BCDFGHJKLMNPQRSTVWXZ23456789";
    public const int PasswordLength = 10;

    public static string NewId()
    {
        // 100 000 000 a 999 999 999
        return RandomNumberGenerator.GetInt32(100_000_000, 1_000_000_000).ToString();
    }

    public static string FormatId(string id) => id.Length == 9 ? $"{id[..3]} {id[3..6]} {id[6..]}" : id;

    /// <summary>Aceita "123 456 789", "123-456-789" ou "123456789". Devolve nulo se não for um ID válido.</summary>
    public static string? ParseId(string? input)
    {
        if (input is null) return null;
        string digits = new string(input.Where(char.IsDigit).ToArray());
        return digits.Length == 9 && digits[0] != '0' && input.All(c => char.IsDigit(c) || c is ' ' or '-' or '.') ? digits : null;
    }

    public static string NewPassword()
    {
        var chars = new char[PasswordLength];
        for (int i = 0; i < chars.Length; i++) chars[i] = PasswordAlphabet[RandomNumberGenerator.GetInt32(PasswordAlphabet.Length)];
        return new string(chars);
    }

    /// <summary>"ABCDE-FGHJK" para facilitar a leitura.</summary>
    public static string FormatPassword(string password) => password.Length == PasswordLength ? password[..5] + "-" + password[5..] : password;

    public static string NormalizePassword(string? input) =>
        new string((input ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
}
