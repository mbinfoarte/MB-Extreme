using System.Globalization;
using System.Text;

namespace MBExtreme.Core.Gamepads;

/// <summary>Um recurso do controle e se ele funciona nesta ligação.</summary>
public sealed record PadFeature(string Name, bool Supported, string Note = "");

/// <summary>Informações e recursos do controle para a tela "Meu controle" e para o botão Copiar.</summary>
public static class PadDetails
{
    /// <summary>Tamanho do relatório de entrada maior que 64 bytes = Bluetooth (DualSense 78, DualShock 4 547).</summary>
    public static PadConnection ConnectionOf(int inputLength, string? path = null)
    {
        if (inputLength > 64) return PadConnection.Bluetooth;
        var byPath = ControllerCatalog.ConnectionOf(path);
        return byPath == PadConnection.Unknown && inputLength > 0 ? PadConnection.Usb : byPath;
    }

    public static IReadOnlyList<PadFeature> Features(PadModel model, PadConnection connection)
    {
        bool ds4 = model == PadModel.DualShock4;
        bool usb = connection == PadConnection.Usb;
        var list = new List<PadFeature>
        {
            new("Gatilhos adaptáveis", !ds4),
            new("Barra de luz", true),
            new("Luzes do jogador", !ds4),
            new("Luz do mudo", !ds4),
            new("Touchpad", true),
            new("Giroscópio e acelerômetro", true),
            new("Vibração clássica", true, "USB e Bluetooth"),
            new("Vibração háptica (por som)", !ds4 && usb, ds4 ? "" : usb ? "Pelo cabo USB" : "Só pelo cabo USB"),
            new("Alto-falante e fone no controle", !ds4 && usb, ds4 ? "" : usb ? "Pelo cabo USB" : "Só pelo cabo USB"),
        };
        if (model == PadModel.DualSenseEdge) list.Add(new PadFeature("Botões de trás e FN", true));
        return list;
    }

    /// <summary>Endereço "E8:47:3A:3F:84:3C" → número de 48 bits (0 se inválido).</summary>
    public static ulong ParseMac(string? mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return 0;
        string hex = mac.Replace(":", "").Replace("-", "").Trim();
        return hex.Length == 12 && ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong v) ? v : 0;
    }

    public static string Duration(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";

    /// <summary>Texto para copiar e mandar ao suporte (uma linha por item).</summary>
    public static string ToText(string title, IEnumerable<(string Label, string Value)> rows, IEnumerable<PadFeature> features)
    {
        var sb = new StringBuilder();
        sb.AppendLine(title);
        foreach (var (label, value) in rows) sb.AppendLine($"{label}: {value}");
        sb.AppendLine("Recursos:");
        foreach (var f in features) sb.AppendLine($"  {(f.Supported ? "[x]" : "[ ]")} {f.Name}{(f.Note.Length > 0 ? " (" + f.Note + ")" : "")}");
        return sb.ToString();
    }
}
