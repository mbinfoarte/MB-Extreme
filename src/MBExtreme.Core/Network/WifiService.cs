using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using MBExtreme.Core.Windows;

namespace MBExtreme.Core.Network;

public sealed record WifiStatus(bool Connected, string Ssid, int SignalPercent, int Channel, string RadioType, double ReceiveMbps, double TransmitMbps, string Band);

/// <summary>Estado do Wi-Fi (sinal, canal, banda e velocidade) lido de "netsh wlan show interfaces", em português ou inglês.</summary>
[SupportedOSPlatform("windows")]
public static class WifiService
{
    public static WifiStatus? Read()
    {
        var result = Shell.RunUtf8("netsh.exe", "wlan", "show", "interfaces");
        return result.ExitCode == 0 ? Parse(result.Output) : null;
    }

    public static WifiStatus? Parse(string output)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in output.Split('\n'))
        {
            var match = Regex.Match(raw, @"^\s*([^:]+?)\s*:\s*(.+?)\s*$");
            if (match.Success && !values.ContainsKey(match.Groups[1].Value)) values[match.Groups[1].Value] = match.Groups[2].Value;
        }

        if (values.Count == 0) return null;

        string? Find(params string[] keys)
        {
            foreach (var kv in values)
                if (keys.Any(k => kv.Key.Contains(k, StringComparison.OrdinalIgnoreCase))) return kv.Value;
            return null;
        }

        double Number(string? text) =>
            double.TryParse(Regex.Match(text ?? "", @"[\d.,]+").Value.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double n) ? n : 0;

        string state = Find("State", "Estado") ?? "";
        bool connected = state.Contains("connected", StringComparison.OrdinalIgnoreCase) && !state.Contains("disconnected", StringComparison.OrdinalIgnoreCase)
                         || state.Contains("conectad", StringComparison.OrdinalIgnoreCase) && !state.Contains("desconectad", StringComparison.OrdinalIgnoreCase);

        string ssid = values.TryGetValue("SSID", out var s) ? s : "";
        int channel = (int)Number(Find("Channel", "Canal"));
        string band = Find("Band", "Banda") ?? (channel is > 0 and <= 14 ? "2,4 GHz" : channel >= 32 ? "5 GHz" : "");
        return new WifiStatus(connected, ssid, (int)Number(Find("Signal", "Sinal")), channel,
            Find("Radio type", "Tipo de r") ?? "", Number(Find("Receive rate", "recep")), Number(Find("Transmit rate", "transm")), band);
    }

    public static IReadOnlyList<string> Advice(WifiStatus status)
    {
        var tips = new List<string>();
        if (!status.Connected)
        {
            tips.Add("Sem conexão Wi-Fi no momento. Se você joga online, cabo de rede (Ethernet) é sempre a melhor opção.");
            return tips;
        }

        if (status.SignalPercent is > 0 and < 50)
            tips.Add($"Sinal fraco ({status.SignalPercent}%). Aproxime o PC do roteador ou use um repetidor/cabo: sinal fraco gera perda de pacotes e picos de ping.");
        else if (status.SignalPercent is >= 50 and < 70)
            tips.Add($"Sinal razoável ({status.SignalPercent}%). Para jogos competitivos, procure ficar acima de 70%.");
        else if (status.SignalPercent >= 70)
            tips.Add($"Sinal bom ({status.SignalPercent}%).");

        if (status.Band.StartsWith("2", StringComparison.Ordinal) || status.Channel is > 0 and <= 14)
            tips.Add("Você está na banda de 2,4 GHz: ela alcança mais longe, mas é congestionada (vizinhos, micro-ondas). Se o roteador tiver 5 GHz, use-a e fique no mesmo cômodo.");

        tips.Add("O Wi-Fi tem mais variação de ping (jitter) que o cabo, mesmo com sinal bom. Meça em Rede > Analisador de rede e compare o jitter.");
        tips.Add("Evite fazer download grande ou streaming no mesmo roteador enquanto joga.");
        return tips;
    }
}
