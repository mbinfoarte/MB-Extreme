namespace MBExtreme.Core.Gamepads;

/// <summary>Como o controle está ligado ao PC.</summary>
public enum PadConnection { Usb, Bluetooth, Wireless, Unknown }

/// <summary>Um controle de jogo encontrado no Windows (HID ou XInput).</summary>
public sealed record GameController(string Name, string Brand, ushort Vendor, ushort Product, PadConnection Connection, string? Path, int InputLength, bool XInput = false)
{
    public string ConnectionText => Connection switch
    {
        PadConnection.Usb => "Cabo USB",
        PadConnection.Bluetooth => "Bluetooth",
        PadConnection.Wireless => "Sem fio (adaptador)",
        _ => "Desconhecida",
    };
}

/// <summary>Onde baixar driver, firmware e o programa oficial de cada marca.</summary>
public sealed record ControllerResource(string Title, string Description, string Url);

public static class ControllerCatalog
{
    public const ushort Sony = 0x054C, Microsoft = 0x045E, Nintendo = 0x057E, EightBitDo = 0x2DC8, Logitech = 0x046D, Razer = 0x1532,
        PowerA = 0x20D6, Thrustmaster = 0x044F, Hori = 0x0F0D, Nacon = 0x146B, Valve = 0x28DE;

    public static string Brand(ushort vendor) => vendor switch
    {
        Sony => "Sony PlayStation",
        Microsoft => "Microsoft Xbox",
        Nintendo => "Nintendo",
        EightBitDo => "8BitDo",
        Logitech => "Logitech",
        Razer => "Razer",
        PowerA => "PowerA",
        Thrustmaster => "Thrustmaster",
        Hori => "HORI",
        Nacon => "Nacon",
        Valve => "Valve (Steam)",
        _ => "Genérico",
    };

    /// <summary>Caminho do aparelho no Windows → tipo de ligação.</summary>
    public static PadConnection ConnectionOf(string? path)
    {
        if (string.IsNullOrEmpty(path)) return PadConnection.Unknown;
        string p = path.ToLowerInvariant();
        if (p.Contains("00001124-0000-1000-8000-00805f9b34fb") || p.Contains("bthenum") || p.Contains("bthledevice") || p.Contains("_bth") || p.Contains("{00001812")) return PadConnection.Bluetooth;
        if (p.Contains("xboxgip") || p.Contains("xbox wireless")) return PadConnection.Wireless;
        if (p.Contains("usb#") || p.Contains("hid#vid_")) return PadConnection.Usb;
        return PadConnection.Unknown;
    }

    /// <summary>Drivers, firmware e programa oficial do fabricante (só sites oficiais).</summary>
    public static IReadOnlyList<ControllerResource> Resources(ushort vendor) => vendor switch
    {
        Sony => new[]
        {
            new ControllerResource("Atualizar firmware do DualSense / DualSense Edge", "Programa oficial da Sony para Windows: ligue o controle no cabo USB e siga os passos.",
                "https://controller.dl.playstation.net/controller/lang/en/fwupdater.html"),
            new ControllerResource("PlayStation Accessories (DualSense Edge)", "Programa oficial para perfis e firmware do DualSense Edge e acessórios PlayStation.",
                "https://www.playstation.com/pt-br/support/hardware/playstation-accessories-app/"),
        },
        Microsoft => new[]
        {
            new ControllerResource("Acessórios Xbox (Microsoft Store)", "Atualiza o firmware do controle Xbox, remapeia botões e ajusta a vibração.", "ms-windows-store://pdp/?ProductId=9NBLGGH30XJ3"),
            new ControllerResource("Ajuda do controle Xbox no PC", "Driver do adaptador sem fio e solução de problemas (site da Microsoft).", "https://support.xbox.com/pt-BR/help/hardware-network/controller/connect-xbox-wireless-controller-to-pc"),
        },
        Nintendo => new[]
        {
            new ControllerResource("Pro Controller no PC (Steam Input)", "O Windows não tem driver oficial da Nintendo; o Steam reconhece o Pro Controller com giroscópio.", "https://help.steampowered.com/pt-br/faqs/view/7A9E-AB9E-A7D6-6C08"),
        },
        EightBitDo => new[]
        {
            new ControllerResource("8BitDo Ultimate Software / Firmware", "Programa oficial para atualizar o firmware e criar perfis nos controles 8BitDo.", "https://support.8bitdo.com/"),
        },
        Logitech => new[] { new ControllerResource("Logitech G HUB", "Driver, perfis e firmware dos controles e volantes Logitech.", "https://www.logitechg.com/pt-br/innovation/g-hub.html") },
        Razer => new[] { new ControllerResource("Razer Synapse / Razer Controller Setup", "Programa oficial para perfis e firmware dos controles Razer.", "https://www.razer.com/synapse-4") },
        Thrustmaster => new[] { new ControllerResource("Drivers Thrustmaster", "Drivers e firmware de controles e volantes Thrustmaster.", "https://support.thrustmaster.com/pt/") },
        _ => Array.Empty<ControllerResource>(),
    };

    /// <summary>Ajuda que vale para qualquer controle.</summary>
    public static IReadOnlyList<ControllerResource> General { get; } = new[]
    {
        new ControllerResource("Steam Input", "Faz quase qualquer controle funcionar em qualquer jogo da Steam, com perfis por jogo.", "steam://open/settings/controller"),
    };
}

/// <summary>Taxa de atualização do controle medida pelos horários dos pacotes que chegaram.</summary>
public sealed record PollingStats(int Reports, double RateHz, double AverageMs, double JitterMs, double WorstMs)
{
    public static PollingStats? From(IReadOnlyList<double> timestampsMs)
    {
        if (timestampsMs.Count < 10) return null;
        var gaps = new List<double>(timestampsMs.Count - 1);
        for (int i = 1; i < timestampsMs.Count; i++)
        {
            double gap = timestampsMs[i] - timestampsMs[i - 1];
            if (gap > 0 && gap < 250) gaps.Add(gap);   // pausa grande = controle parado, não conta
        }
        if (gaps.Count < 5) return null;
        double avg = gaps.Average();
        double jitter = Math.Sqrt(gaps.Sum(g => (g - avg) * (g - avg)) / gaps.Count);
        return new PollingStats(timestampsMs.Count, Math.Round(1000 / avg, 1), Math.Round(avg, 2), Math.Round(jitter, 2), Math.Round(gaps.Max(), 2));
    }

    /// <summary>Resumo para quem não é técnico.</summary>
    public string Verdict => RateHz >= 800 ? "Excelente (1000 Hz): resposta de controle competitivo."
        : RateHz >= 400 ? "Muito boa (500 Hz)."
        : RateHz >= 200 ? "Boa (250 Hz): normal para controles pelo cabo ou Bluetooth bom."
        : RateHz >= 100 ? "Normal (125 Hz). Pelo cabo USB costuma ficar mais rápido."
        : "Lenta: troque de porta USB, use o cabo ou deixe o controle mais perto do receptor.";
}

/// <summary>Curva de resposta do analógico.</summary>
public enum StickCurve { Linear, Suave, Agressiva, Personalizada }

/// <summary>Perfil do controle por tipo de jogo (valores em porcentagem).</summary>
public sealed class PadProfile
{
    public string Name { get; set; } = "";
    public string GameType { get; set; } = "";
    public int Deadzone { get; set; } = 8;
    public int AntiDeadzone { get; set; }
    public int Sensitivity { get; set; } = 100;
    public StickCurve Curve { get; set; } = StickCurve.Linear;
    public int TriggerDeadzone { get; set; } = 5;
    public int Vibration { get; set; } = 100;
    public string Notes { get; set; } = "";
    /// <summary>Executáveis dos jogos que usam este perfil (ex.: "FC27.exe"): quando um deles abre, o perfil entra sozinho.</summary>
    public List<string> Games { get; set; } = new();

    public PadProfile Clone()
    {
        var c = (PadProfile)MemberwiseClone();
        c.Games = Games.ToList();
        return c;
    }
}

public static class PadProfiles
{
    /// <summary>Pontos de partida por tipo de jogo; a pessoa ajusta e salva com o nome que quiser.</summary>
    public static IReadOnlyList<PadProfile> Defaults { get; } = new[]
    {
        new PadProfile { Name = "Tiro (FPS)", GameType = "Tiro", Deadzone = 6, AntiDeadzone = 4, Sensitivity = 100, Curve = StickCurve.Suave, TriggerDeadzone = 2, Vibration = 40,
            Notes = "Zona morta baixa para mira fina, curva suave para não passar do alvo e gatilho curto para atirar mais rápido. Pouca vibração para não tremer a mira." },
        new PadProfile { Name = "Call of Duty (campanha)", GameType = "Tiro", Deadzone = 5, AntiDeadzone = 0, Sensitivity = 100, Curve = StickCurve.Linear, TriggerDeadzone = 0, Vibration = 50,
            Games = KbmPresets.CallOfDuty.Games.ToList(),
            Notes = "Zona morta pequena no MB e a do jogo no mínimo, curva linear (o próprio COD já suaviza) e gatilho sem zona morta para o tiro sair na hora. Entra sozinho quando um COD com campanha offline abre." },
        new PadProfile { Name = "Call of Duty (2019 a 2026)", GameType = "Tiro", Deadzone = 4, AntiDeadzone = 0, Sensitivity = 100, Curve = StickCurve.Linear, TriggerDeadzone = 0, Vibration = 30,
            Games = new() { "cod", "cod24-cod", "cod23-cod", "cod22-cod", "ModernWarfare", "BlackOpsColdWar", "Vanguard" },
            Notes = "Para MW 2019, Cold War, Vanguard, MW2, MW3, Black Ops 6, Black Ops 7 e os próximos (todos abrem pelo cod.exe). O controle de PS5 funciona direto no jogo, até com os gatilhos adaptáveis. No jogo: zona morta do analógico 0 a 3, curva Dinâmica, sensibilidade 6 a 8 e gatilhos adaptáveis desligados para o tiro sair mais rápido. Pouca vibração para não tremer a mira." },
        new PadProfile { Name = "GTA V", GameType = "Ação", Deadzone = 8, AntiDeadzone = 0, Sensitivity = 90, Curve = StickCurve.Suave, TriggerDeadzone = 0, Vibration = 100,
            Games = new() { "GTA5", "GTA5_Enhanced", "PlayGTAV" },
            Notes = "Gatilhos sem zona morta para dosar acelerador e freio nos carros, mira suave a pé e vibração cheia. No jogo: Mira › Assistida completa na campanha e sensibilidade da câmera em 60 a 70%." },
        new PadProfile { Name = "Far Cry", GameType = "Tiro", Deadzone = 6, AntiDeadzone = 3, Sensitivity = 95, Curve = StickCurve.Suave, TriggerDeadzone = 2, Vibration = 70,
            Games = new() { "farcry3", "FarCry4", "FarCryPrimal", "FarCry5", "FarCryNewDawn", "FarCry6" },
            Notes = "Mira fina para tiro em mundo aberto, gatilho curto e vibração média. No jogo: zona morta mínima e aceleração da mira desligada, se existir." },
        new PadProfile { Name = "Resident Evil", GameType = "Aventura", Deadzone = 8, AntiDeadzone = 0, Sensitivity = 85, Curve = StickCurve.Suave, TriggerDeadzone = 3, Vibration = 100,
            Games = new() { "re2", "re3", "re4", "re7", "re8", "re9", "re0hdremaster", "bhd", "rerev", "rerev2" },
            Notes = "Câmera mais lenta e suave para mirar na cabeça com calma, vibração cheia para o susto. Para RE2, RE3, RE4, RE7, Village, Requiem, Revelations e o RE remake." },
        new PadProfile { Name = "Corrida", GameType = "Corrida", Deadzone = 3, AntiDeadzone = 0, Sensitivity = 90, Curve = StickCurve.Linear, TriggerDeadzone = 0, Vibration = 100,
            Notes = "Direção linear e gatilhos sem zona morta para dosar acelerador e freio. Vibração cheia para sentir a pista." },
        new PadProfile { Name = "Luta", GameType = "Luta", Deadzone = 12, AntiDeadzone = 0, Sensitivity = 100, Curve = StickCurve.Linear, TriggerDeadzone = 10, Vibration = 60,
            Notes = "Zona morta maior para não sair comando sem querer; prefira o direcional (D-pad) para os golpes." },
        new PadProfile { Name = "Esporte (futebol)", GameType = "Esporte", Deadzone = 8, AntiDeadzone = 0, Sensitivity = 100, Curve = StickCurve.Linear, TriggerDeadzone = 5, Vibration = 80,
            Notes = "Equilíbrio entre precisão no passe e resposta rápida no drible." },
        new PadProfile { Name = "Aventura / RPG", GameType = "Aventura", Deadzone = 10, AntiDeadzone = 0, Sensitivity = 85, Curve = StickCurve.Suave, TriggerDeadzone = 5, Vibration = 100,
            Notes = "Câmera mais suave e confortável para jogar por muito tempo." },
    };

    /// <summary>Junta os perfis salvos com os prontos (os salvos vencem quando o nome é igual).</summary>
    public static List<PadProfile> Merge(IEnumerable<PadProfile> saved) =>
        saved.Concat(Defaults.Where(d => saved.All(s => !string.Equals(s.Name, d.Name, StringComparison.OrdinalIgnoreCase)))).ToList();

    /// <summary>Perfil ligado ao executável do jogo, ou null.</summary>
    public static PadProfile? ForGame(IEnumerable<PadProfile> profiles, string? exeOrProcess)
    {
        if (string.IsNullOrWhiteSpace(exeOrProcess)) return null;
        string key = Norm(exeOrProcess);
        return profiles.FirstOrDefault(p => p.Games.Any(g => Norm(g) == key));
    }

    private static string Norm(string exe)
    {
        string t = exe.Trim();
        int slash = t.LastIndexOfAny(new[] { '\\', '/' });
        string n = (slash >= 0 ? t[(slash + 1)..] : t).ToLowerInvariant();
        return n.EndsWith(".exe", StringComparison.Ordinal) ? n[..^4] : n;
    }

    /// <summary>Leva os valores do perfil para a emulação (os dois analógicos, gatilhos e vibração).</summary>
    public static void ApplyTo(EmulationSettings e, PadProfile p)
    {
        foreach (var t in new[] { e.Left, e.Right })
        {
            t.Deadzone = p.Deadzone;
            t.AntiDeadzone = p.AntiDeadzone;
            t.Sensitivity = p.Sensitivity;
            t.Curve = p.Curve;
        }
        e.TriggerDeadzone = p.TriggerDeadzone;
    }

    /// <summary>Ícone simples pelo tipo de jogo (Segoe MDL2).</summary>
    public static string Glyph(PadProfile p) => (p.GameType + " " + p.Name).ToLowerInvariant() switch
    {
        var t when t.Contains("tiro") || t.Contains("fps") => "\uE7C8",
        var t when t.Contains("corrida") => "\uE804",
        var t when t.Contains("luta") => "\uE945",
        var t when t.Contains("esporte") || t.Contains("futebol") => "\uE805",
        var t when t.Contains("aventura") || t.Contains("rpg") => "\uE909",
        _ => "\uE7FC",
    };

    /// <summary>Aplica zona morta, anti-zona morta, curva e sensibilidade a um ponto do analógico (-1..1). Usado na prévia ao vivo.</summary>
    public static (double X, double Y) Apply(PadProfile p, double x, double y)
    {
        double r = Math.Sqrt(x * x + y * y);
        if (r < 1e-9) return (0, 0);
        double dz = Math.Clamp(p.Deadzone, 0, 50) / 100.0;
        if (r <= dz) return (0, 0);
        double t = Math.Min(1, (r - dz) / (1 - dz));             // 0..1 depois da zona morta
        t = p.Curve switch { StickCurve.Suave => t * t, StickCurve.Agressiva => Math.Sqrt(t), _ => t };   // Personalizada só existe nos ajustes finos
        double anti = Math.Clamp(p.AntiDeadzone, 0, 40) / 100.0;
        t = anti + (1 - anti) * t;
        t = Math.Min(1, t * Math.Clamp(p.Sensitivity, 10, 200) / 100.0);
        return (Math.Round(x / r * t, 4), Math.Round(y / r * t, 4));
    }
}
