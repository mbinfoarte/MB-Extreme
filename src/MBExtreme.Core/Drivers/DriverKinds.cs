using System.Text.RegularExpressions;

namespace MBExtreme.Core.Drivers;

/// <summary>Tipo de um driver (para o ícone e o nome amigável) a partir da classe do Windows ou do título.</summary>
public static class DriverKinds
{
    private static readonly (string Pattern, string Title, string Glyph)[] Kinds =
    {
        (@"display|vídeo|video|graphics|gpu|nvidia|geforce|radeon|amd .*graphics|intel.*(uhd|iris|arc)", "Vídeo", ""),
        (@"bluetooth", "Bluetooth", ""),
        (@"wi-?fi|wireless|wlan|802\.11", "Wi-Fi", ""),
        (@"\bnet\b|network|ethernet|rede|lan|realtek pcie gbe|intel.*ethernet|killer", "Rede", ""),
        (@"audio|áudio|sound|som|media|hdaudio|realtek.*audio|nahimic", "Som", ""),
        (@"usb", "USB", ""),
        (@"camera|câmera|image|webcam", "Câmera", ""),
        (@"hid|mouse|keyboard|teclado|touchpad|xinput|controller|controle", "Mouse, teclado e controle", ""),
        (@"disk|storage|nvme|sata|ahci|raid|scsi|ssd", "Armazenamento", ""),
        (@"monitor", "Monitor", ""),
        (@"print|impress", "Impressora", ""),
        (@"system|chipset|pci|smbus|management engine|serial io|firmware|bios", "Chipset e sistema", ""),
    };

    public static (string Title, string Glyph) Of(string classOrTitle)
    {
        string t = classOrTitle ?? "";
        foreach (var (pattern, title, glyph) in Kinds)
            if (Regex.IsMatch(t, pattern, RegexOptions.IgnoreCase)) return (title, glyph);
        return ("Outros", "");
    }
}
