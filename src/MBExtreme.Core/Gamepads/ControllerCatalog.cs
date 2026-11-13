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

    public PadProfile Clone() => (PadProfile)MemberwiseClone();
}

public static class PadProfiles
{
    /// <summary>Pontos de partida por tipo de jogo; a pessoa ajusta e salva com o nome que quiser.</summary>
    public static IReadOnlyList<PadProfile> Defaults { get; } = new[]
    {
        new PadProfile { Name = "Tiro (FPS)", GameType = "Tiro", Deadzone = 6, AntiDeadzone = 4, Sensitivity = 100, Curve = StickCurve.Suave, TriggerDeadzone = 2, Vibration = 40,
            Notes = "Zona morta baixa para mira fina, curva suave para não passar do alvo e gatilho curto para atirar mais rápido. Pouca vibração para não tremer a mira." },
        new PadProfile { Name = "Corrida", GameType = "Corrida", Deadzone = 3, AntiDeadzone = 0, Sensitivity = 90, Curve = StickCurve.Linear, TriggerDeadzone = 0, Vibration = 100,
            Notes = "Direção linear e gatilhos sem zona morta para dosar acelerador e freio. Vibração cheia para sentir a pista." },
        new PadProfile { Name = "Luta", GameType = "Luta", Deadzone = 12, AntiDeadzone = 0, Sensitivity = 100, Curve = StickCurve.Linear, TriggerDeadzone = 10, Vibration = 60,
            Notes = "Zona morta maior para não sair comando sem querer; prefira o direcional (D-pad) para os golpes." },
        new PadProfile { Name = "Esporte (futebol)", GameType = "Esporte", Deadzone = 8, AntiDeadzone = 0, Sensitivity = 100, Curve = StickCurve.Linear, TriggerDeadzone = 5, Vibration = 80,
            Notes = "Equilíbrio entre precisão no passe e resposta rápida no drible." },
        new PadProfile { Name = "Aventura / RPG", GameType = "Aventura", Deadzone = 10, AntiDeadzone = 0, Sensitivity = 85, Curve = StickCurve.Suave, TriggerDeadzone = 5, Vibration = 100,
            Notes = "Câmera mais suave e confortável para jogar por muito tempo." },
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
