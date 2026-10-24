using System.Text.RegularExpressions;

namespace MBExtreme.Core.Games;

/// <summary>
/// Índice de desempenho aproximado de placas de vídeo e processadores, feito por nós (não vem de nenhum site): serve só para comparar o seu PC com o que o jogo pede.
/// Placas: GTX 1060 6 GB = 100, em jogos a 1080p. Processadores: Core i5-8400 = 100, em jogos. São estimativas; um modelo desconhecido devolve nulo (sem chute).
/// </summary>
public static class HardwareTiers
{
    private static readonly Dictionary<string, double> Gpus = new(StringComparer.Ordinal)
    {
        // NVIDIA, linhas antigas
        ["GT 710"] = 8, ["GT 730"] = 12, ["GT 1030"] = 25, ["GTX 650"] = 20, ["GTX 650 TI"] = 27, ["GTX 660"] = 38, ["GTX 660 TI"] = 45, ["GTX 670"] = 50, ["GTX 680"] = 58,
        ["GTX 750"] = 27, ["GTX 750 TI"] = 32, ["GTX 760"] = 42, ["GTX 770"] = 52, ["GTX 780"] = 62, ["GTX 780 TI"] = 72, ["GTX TITAN"] = 70,
        ["GTX 950"] = 48, ["GTX 960"] = 55, ["GTX 970"] = 85, ["GTX 980"] = 100, ["GTX 980 TI"] = 128, ["GTX TITAN X"] = 133,
        ["GTX 1050"] = 52, ["GTX 1050 TI"] = 63, ["GTX 1060"] = 100, ["GTX 1070"] = 143, ["GTX 1070 TI"] = 165, ["GTX 1080"] = 180, ["GTX 1080 TI"] = 235,
        ["GTX 1630"] = 45, ["GTX 1650"] = 70, ["GTX 1650 SUPER"] = 95, ["GTX 1660"] = 115, ["GTX 1660 SUPER"] = 125, ["GTX 1660 TI"] = 130,
        ["RTX 2060"] = 160, ["RTX 2060 SUPER"] = 185, ["RTX 2070"] = 195, ["RTX 2070 SUPER"] = 215, ["RTX 2080"] = 240, ["RTX 2080 SUPER"] = 250, ["RTX 2080 TI"] = 300,
        ["RTX 3050"] = 135, ["RTX 3060"] = 185, ["RTX 3060 TI"] = 245, ["RTX 3070"] = 285, ["RTX 3070 TI"] = 310, ["RTX 3080"] = 370, ["RTX 3080 TI"] = 420, ["RTX 3090"] = 440, ["RTX 3090 TI"] = 470,
        ["RTX 4050"] = 190, ["RTX 4060"] = 235, ["RTX 4060 TI"] = 280, ["RTX 4070"] = 345, ["RTX 4070 SUPER"] = 395, ["RTX 4070 TI"] = 415, ["RTX 4070 TI SUPER"] = 470, ["RTX 4080"] = 560, ["RTX 4080 SUPER"] = 575, ["RTX 4090"] = 780,
        ["RTX 5050"] = 240, ["RTX 5060"] = 270, ["RTX 5060 TI"] = 320, ["RTX 5070"] = 420, ["RTX 5070 TI"] = 540, ["RTX 5080"] = 640, ["RTX 5090"] = 960,
        // AMD
        ["HD 7750"] = 15, ["HD 7770"] = 25, ["HD 7850"] = 38, ["HD 7870"] = 50, ["HD 7950"] = 55, ["HD 7970"] = 65,
        ["R7 240"] = 10, ["R7 250"] = 22, ["R7 260X"] = 35, ["R7 265"] = 38, ["R9 270"] = 45, ["R9 270X"] = 50, ["R9 280"] = 55, ["R9 280X"] = 62,
        ["R9 290"] = 85, ["R9 290X"] = 95, ["R9 380"] = 60, ["R9 390"] = 105, ["R9 390X"] = 112, ["R9 FURY"] = 120, ["R9 FURY X"] = 130, ["R9 NANO"] = 115,
        ["RX 460"] = 45, ["RX 470"] = 78, ["RX 480"] = 95, ["RX 550"] = 40, ["RX 560"] = 52, ["RX 570"] = 95, ["RX 580"] = 105, ["RX 590"] = 115,
        ["VEGA 56"] = 155, ["VEGA 64"] = 175, ["RADEON VII"] = 215,
        ["RX 5500 XT"] = 85, ["RX 5600 XT"] = 150, ["RX 5700"] = 185, ["RX 5700 XT"] = 215,
        ["RX 6400"] = 70, ["RX 6500 XT"] = 85, ["RX 6600"] = 195, ["RX 6600 XT"] = 225, ["RX 6650 XT"] = 245, ["RX 6700"] = 255, ["RX 6700 XT"] = 285, ["RX 6750 XT"] = 305,
        ["RX 6800"] = 365, ["RX 6800 XT"] = 410, ["RX 6900 XT"] = 440, ["RX 6950 XT"] = 460,
        ["RX 7600"] = 220, ["RX 7600 XT"] = 235, ["RX 7700 XT"] = 325, ["RX 7800 XT"] = 390, ["RX 7900 GRE"] = 420, ["RX 7900 XT"] = 480, ["RX 7900 XTX"] = 560,
        ["RX 9060 XT"] = 300, ["RX 9070"] = 460, ["RX 9070 XT"] = 510,
        // Intel Arc
        ["ARC A380"] = 60, ["ARC A580"] = 150, ["ARC A750"] = 195, ["ARC A770"] = 220, ["ARC B570"] = 245, ["ARC B580"] = 270,
    };

    private static readonly Dictionary<int, double> VegaIntegrated = new() { [3] = 20, [6] = 30, [7] = 34, [8] = 38, [10] = 45, [11] = 52 };
    private static readonly Dictionary<string, double> RadeonMobile = new() { ["610M"] = 30, ["660M"] = 60, ["680M"] = 75, ["740M"] = 40, ["760M"] = 55, ["780M"] = 95, ["880M"] = 110, ["890M"] = 120 };

    /// <summary>Placa integrada (usa a memória do PC): não serve para jogos que pedem placa dedicada.</summary>
    public static bool IsIntegratedGpu(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        string u = name.ToUpperInvariant();
        if (Regex.IsMatch(u, @"\b(UHD|IRIS|HD GRAPHICS)\b") && !u.Contains("ARC")) return true;
        if (Regex.IsMatch(u, @"\bVEGA\s*(3|6|7|8|10|11)\b") || Regex.IsMatch(u, @"\b(610|660|680|740|760|780|880|890)M\b")) return true;
        return Regex.IsMatch(u, @"RADEON\s*(\(TM\)\s*)?GRAPHICS\s*$");
    }

    public static double? GpuIndex(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        string u = name.ToUpperInvariant().Replace("(R)", "").Replace("(TM)", "").Replace("®", "").Replace("™", "");
        bool mobile = Regex.IsMatch(u, @"LAPTOP|MOBILE|NOTEBOOK|MAX-Q");
        double factor = u.Contains("MAX-Q") ? 0.65 : mobile ? 0.75 : 1.0;

        double? integrated = IntegratedIndex(u);
        if (integrated is { } iv) return iv;

        string key = DiscreteKey(u);
        if (key.Length == 0 || !Gpus.TryGetValue(key, out double value)) return null;
        if (key == "GTX 1060" && Regex.IsMatch(u, @"\b3\s*GB\b")) value = 90;
        return value * factor;
    }

    private static double? IntegratedIndex(string u)
    {
        if (u.Contains("ARC")) return null;
        var uhd = Regex.Match(u, @"UHD\s*GRAPHICS\s*(\d{3})?");
        if (uhd.Success)
            return uhd.Groups[1].Value switch { "600" or "605" => 15, "610" => 14, "615" => 16, "620" => 20, "630" => 22, "730" => 28, "750" => 32, "770" => 38, _ => 22 };
        if (u.Contains("IRIS XE")) return 45;
        if (Regex.IsMatch(u, @"IRIS\s*(PLUS|PRO)")) return 28;
        var hd = Regex.Match(u, @"HD\s*GRAPHICS\s*(\d{3,4})?");
        if (hd.Success && !Regex.IsMatch(u, @"RADEON\s*HD"))
            return hd.Groups[1].Value switch { "2000" => 5, "3000" => 6, "4000" => 8, "4600" => 10, "520" => 14, "530" => 17, "620" => 20, _ => 12 };
        var vega = Regex.Match(u, @"\bVEGA\s*(\d{1,2})\b");
        if (vega.Success && int.Parse(vega.Groups[1].Value) is var v && VegaIntegrated.TryGetValue(v, out double vi)) return vi;
        var mobile = Regex.Match(u, @"\b(\d{3}M)\b");
        if (mobile.Success && RadeonMobile.TryGetValue(mobile.Groups[1].Value, out double rm)) return rm;
        if (Regex.IsMatch(u, @"RADEON\s*GRAPHICS\s*$")) return 40;
        return null;
    }

    private static string DiscreteKey(string u)
    {
        var nv = Regex.Match(u, @"\b(GTX|RTX|GT)\s*(\d{3,4})\s*(TI\s*SUPER|SUPER|TI)?\b");
        if (nv.Success) return Join(nv.Groups[1].Value, nv.Groups[2].Value, nv.Groups[3].Value);
        var titan = Regex.Match(u, @"\bTITAN\s*(X)?\b");
        if (titan.Success) return titan.Groups[1].Success ? "GTX TITAN X" : "GTX TITAN";

        var rx = Regex.Match(u, @"\bRX\s*(\d{3,4})\s*(XTX|XT|GRE)?\b");
        if (rx.Success) return Join("RX", rx.Groups[1].Value, rx.Groups[2].Value);
        var r9 = Regex.Match(u, @"\bR([579])\s*(\d{3})\s*(X)?\b");
        if (r9.Success) return $"R{r9.Groups[1].Value} {r9.Groups[2].Value}{r9.Groups[3].Value}";
        var fury = Regex.Match(u, @"\bFURY\s*(X)?\b");
        if (fury.Success) return fury.Groups[1].Success ? "R9 FURY X" : "R9 FURY";
        if (Regex.IsMatch(u, @"\bNANO\b")) return "R9 NANO";
        var hd = Regex.Match(u, @"\bHD\s*(\d{4})\b");
        if (hd.Success) return "HD " + hd.Groups[1].Value;
        var vega = Regex.Match(u, @"\bVEGA\s*(56|64)\b");
        if (vega.Success) return "VEGA " + vega.Groups[1].Value;
        if (Regex.IsMatch(u, @"RADEON\s*VII\b")) return "RADEON VII";
        var arc = Regex.Match(u, @"\bARC\s*(?:PRO\s*)?([AB]\d{3})\b");
        if (arc.Success) return "ARC " + arc.Groups[1].Value;
        return "";
    }

    private static string Join(string prefix, string number, string suffix)
    {
        string s = Regex.Replace(suffix.Trim(), @"\s+", " ");
        return s.Length == 0 ? $"{prefix} {number}" : $"{prefix} {number} {s}";
    }

    // ---------------------------------------------------------------- processadores

    private static readonly Dictionary<int, double> IntelBase = new()
    {
        [2] = 55, [3] = 62, [4] = 70, [5] = 74, [6] = 80, [7] = 90, [8] = 100, [9] = 112, [10] = 120, [11] = 135, [12] = 170, [13] = 205, [14] = 215,
    };

    private static readonly Dictionary<int, double> RyzenBase = new()
    {
        [1] = 85, [2] = 98, [3] = 125, [4] = 120, [5] = 170, [6] = 175, [7] = 215, [8] = 190, [9] = 235,
    };

    public static double? CpuIndex(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        string u = name.ToUpperInvariant().Replace("(R)", "").Replace("(TM)", "").Replace("®", "").Replace("™", "");

        // Intel Core Ultra (Arrow Lake e Meteor Lake)
        var ultra = Regex.Match(u, @"CORE\s*ULTRA\s*([3579])\s*(\d{3})([A-Z]{0,2})");
        if (ultra.Success)
        {
            double tier = ultra.Groups[1].Value switch { "3" => 0.8, "5" => 1.0, "7" => 1.15, _ => 1.3 };
            double gen = ultra.Groups[2].Value[0] == '2' ? 300 : 230;
            return gen * tier * Suffix(ultra.Groups[3].Value);
        }

        // Intel Core iX-NNNN / iX-NNNNN
        var core = Regex.Match(u, @"\bI([3579])[- ]?(\d{4,5})([A-Z]{0,2}\d?)\b");
        if (core.Success)
        {
            string digits = core.Groups[2].Value;
            // números de 4 dígitos que começam com 1 são das gerações 10 a 14 de notebook (ex.: i7-1165G7); nos de mesa, o primeiro dígito é a geração
            int gen = digits.Length == 5 || digits[0] == '1' ? int.Parse(digits[..2]) : int.Parse(digits[..1]);
            if (!IntelBase.TryGetValue(gen, out double baseValue)) return null;
            int tierNumber = int.Parse(core.Groups[1].Value);
            double tier = tierNumber switch { 3 => gen >= 12 ? 0.85 : 0.78, 5 => 1.0, 7 => gen >= 8 ? 1.2 : 1.12, _ => 1.3 };
            return baseValue * tier * Suffix(core.Groups[3].Value);
        }

        // Ryzen N NNNN
        var ryzen = Regex.Match(u, @"RYZEN\s*(?:AI\s*)?([3579])\s*(?:PRO\s*)?(\d{4})\s*(X3D|[A-Z]{0,3})");
        if (ryzen.Success)
        {
            int gen = ryzen.Groups[2].Value[0] - '0';
            if (!RyzenBase.TryGetValue(gen, out double baseValue)) return null;
            double tier = ryzen.Groups[1].Value switch { "3" => 0.7, "5" => 1.0, "7" => gen >= 5 ? 1.15 : 1.12, _ => 1.3 };
            string suffix = ryzen.Groups[3].Value;
            double s = suffix switch { "X3D" => 1.3, "G" or "GE" => 0.85, "U" => 0.6, "H" or "HS" or "HX" => 0.8, "T" => 0.8, _ => 1.0 };
            return baseValue * tier * s;
        }

        // famílias antigas
        if (Regex.IsMatch(u, @"\bFX[- ]?(\d)(\d{3})")) return Regex.Match(u, @"\bFX[- ]?(\d)").Groups[1].Value switch { "4" => 42, "6" => 52, "8" or "9" => 62, _ => 50 };
        if (Regex.IsMatch(u, @"PHENOM")) return 40;
        if (Regex.IsMatch(u, @"CORE\s*2\s*QUAD|Q[69]\d{3}")) return 35;
        if (Regex.IsMatch(u, @"CORE\s*2\s*DUO|E[678]\d{3}")) return 25;
        if (Regex.IsMatch(u, @"PENTIUM\s*(GOLD\s*)?G?\d{4}")) return 55;
        if (Regex.IsMatch(u, @"CELERON")) return 40;
        if (Regex.IsMatch(u, @"\bATHLON\b")) return 35;
        return null;
    }

    /// <summary>Sufixos dos processadores Intel: K/KF/KS (liberados), F, T (baixo consumo), U e H (notebook).</summary>
    private static double Suffix(string suffix) => suffix switch
    {
        "K" or "KF" or "KS" => 1.08,
        "T" => 0.78,
        "U" or "UE" => 0.65,
        "H" or "HQ" or "HK" or "HX" => 0.9,
        "P" => 0.8,
        _ when suffix.Length == 2 && suffix[0] == 'G' && char.IsDigit(suffix[1]) => 0.7,
        _ => 1.0,
    };
}
