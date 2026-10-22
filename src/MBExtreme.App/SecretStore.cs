using System.Security.Cryptography;
using System.Text;

namespace MBExtreme.UI;

/// <summary>Guarda segredos (chave de transmissão) protegidos pelo Windows (DPAPI): só este usuário, neste PC, consegue ler.</summary>
internal static class SecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MBExtreme.Studio.v1");

    public static string Protect(string secret)
    {
        byte[] data = ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(data);
    }

    public static string? Unprotect(string? protectedText)
    {
        if (string.IsNullOrEmpty(protectedText)) return null;
        try
        {
            byte[] data = ProtectedData.Unprotect(Convert.FromBase64String(protectedText), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
