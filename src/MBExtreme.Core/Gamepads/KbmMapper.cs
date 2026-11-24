namespace MBExtreme.Core.Gamepads;

/// <summary>Teclado e mouse → controle virtual: ajustes (salvos nas configurações).</summary>
public sealed class KbmSettings
{
    public VirtualTarget Target { get; set; } = VirtualTarget.Xbox360;
    /// <summary>Sensibilidade da mira pelo mouse (1-100).</summary>
    public int Sensitivity { get; set; } = 55;
    /// <summary>Aceleração: movimentos rápidos ganham mais (0-100).</summary>
    public int Acceleration { get; set; } = 10;
    /// <summary>Anti-zona morta: começa já fora da zona morta do jogo (0-40).</summary>
    public int AntiDeadzone { get; set; } = 18;
    /// <summary>Suavização (0-90): mais = movimento mais macio, menos = mais rápido.</summary>
    public int Smoothing { get; set; } = 25;
    public StickCurve Curve { get; set; } = StickCurve.Suave;
    public string ToggleKey { get; set; } = "F8";
    /// <summary>Tecla → ação do controle (nome em <see cref="KbmMapper.Actions"/>).</summary>
    public Dictionary<string, string> Bindings { get; set; } = KbmMapper.DefaultBindings();
}

/// <summary>Converte teclas apertadas e movimento do mouse no estado de um controle.</summary>
public static class KbmMapper
{
    /// <summary>Ações possíveis do controle (nome interno → nome mostrado).</summary>
    public static IReadOnlyList<(string Id, string Name)> Actions { get; } = new[]
    {
        ("MoveUp", "Andar para frente"), ("MoveDown", "Andar para trás"), ("MoveLeft", "Andar para a esquerda"), ("MoveRight", "Andar para a direita"),
        ("Cross", "A / ✕"), ("Circle", "B / ○"), ("Square", "X / □"), ("Triangle", "Y / △"),
        ("L1", "LB / L1"), ("R1", "RB / R1"), ("L2", "LT / L2 (mirar)"), ("R2", "RT / R2 (atirar)"),
        ("L3", "Analógico esq. (correr)"), ("R3", "Analógico dir. (faca)"),
        ("Up", "Direcional cima"), ("Down", "Direcional baixo"), ("Left", "Direcional esquerda"), ("Right", "Direcional direita"),
        ("Create", "Back / Share"), ("Options", "Start / Options"), ("PS", "Guia / PS"),
    };

    public static Dictionary<string, string> DefaultBindings() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["W"] = "MoveUp", ["S"] = "MoveDown", ["A"] = "MoveLeft", ["D"] = "MoveRight",
        ["Space"] = "Cross", ["C"] = "Circle", ["R"] = "Square", ["E"] = "Triangle",
        ["Q"] = "L1", ["G"] = "R1", ["MouseRight"] = "L2", ["MouseLeft"] = "R2",
        ["LeftShift"] = "L3", ["V"] = "R3", ["MouseMiddle"] = "R3",
        ["D1"] = "Up", ["D2"] = "Right", ["D3"] = "Down", ["D4"] = "Left",
        ["Tab"] = "Create", ["Escape"] = "Options",
    };

    /// <summary>Teclas e botões do mouse que dá para usar, com o código virtual do Windows.</summary>
    public static IReadOnlyDictionary<string, int> Keys { get; } = BuildKeys();

    private static Dictionary<string, int> BuildKeys()
    {
        var k = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["MouseLeft"] = 0x01, ["MouseRight"] = 0x02, ["MouseMiddle"] = 0x04, ["Mouse4"] = 0x05, ["Mouse5"] = 0x06,
            ["Space"] = 0x20, ["Tab"] = 0x09, ["Escape"] = 0x1B, ["Enter"] = 0x0D, ["Backspace"] = 0x08,
            ["LeftShift"] = 0xA0, ["RightShift"] = 0xA1, ["LeftCtrl"] = 0xA2, ["RightCtrl"] = 0xA3, ["LeftAlt"] = 0xA4,
            ["Up"] = 0x26, ["Down"] = 0x28, ["Left"] = 0x25, ["Right"] = 0x27, ["CapsLock"] = 0x14,
        };
        for (char c = 'A'; c <= 'Z'; c++) k[c.ToString()] = c;
        for (int d = 0; d <= 9; d++) k["D" + d] = 0x30 + d;
        for (int f = 1; f <= 12; f++) k["F" + f] = 0x6F + f;
        return k;
    }

    /// <summary>Nome amigável da tecla para a tela.</summary>
    public static string KeyLabel(string key) => key switch
    {
        "MouseLeft" => "Clique esq.", "MouseRight" => "Clique dir.", "MouseMiddle" => "Botão do meio", "Mouse4" => "Mouse 4", "Mouse5" => "Mouse 5",
        "Space" => "Espaço", "LeftShift" => "Shift", "LeftCtrl" => "Ctrl", "LeftAlt" => "Alt", "Escape" => "Esc", "Backspace" => "Apagar",
        _ when key.Length == 2 && key[0] == 'D' && char.IsDigit(key[1]) => key[1].ToString(),
        _ => key,
    };

    /// <summary>
    /// Velocidade do mouse (contagens por milissegundo) → posição do analógico direito (-1..1), com sensibilidade, aceleração,
    /// curva e anti-zona morta. Parado devolve 0.
    /// </summary>
    public static (double X, double Y) MouseToStick(double vx, double vy, KbmSettings s)
    {
        double speed = Math.Sqrt(vx * vx + vy * vy);
        if (speed < 1e-6) return (0, 0);
        double sens = Math.Clamp(s.Sensitivity, 1, 100) / 100.0 * 0.6;            // ~1,7 contagem/ms para o máximo com 100%
        double accel = 1 + Math.Clamp(s.Acceleration, 0, 100) / 100.0 * Math.Min(speed, 4);
        double t = Math.Min(1, speed * sens * accel);
        t = s.Curve switch { StickCurve.Suave => Math.Pow(t, 0.75), StickCurve.Agressiva => Math.Sqrt(t), _ => t };
        double anti = Math.Clamp(s.AntiDeadzone, 0, 40) / 100.0;
        t = anti + (1 - anti) * t;
        return (vx / speed * t, vy / speed * t);
    }

    /// <summary>Suavização exponencial entre o valor anterior e o novo (0 = sem suavizar).</summary>
    public static double Smooth(double previous, double next, int smoothing)
    {
        double a = Math.Clamp(smoothing, 0, 90) / 100.0;
        return previous * a + next * (1 - a);
    }

    /// <summary>Monta o estado do controle a partir das ações apertadas e do analógico direito já calculado.</summary>
    public static PadState Build(IReadOnlySet<string> actions, double rx, double ry)
    {
        var b = PadButtons.None;
        foreach (string a in actions)
            if (Enum.TryParse<PadButtons>(a, out var btn) && btn is not (PadButtons.L2 or PadButtons.R2)) b |= btn;
        double lx = (actions.Contains("MoveRight") ? 1 : 0) - (actions.Contains("MoveLeft") ? 1 : 0);
        double ly = (actions.Contains("MoveDown") ? 1 : 0) - (actions.Contains("MoveUp") ? 1 : 0);
        if (lx != 0 && ly != 0) { lx *= 0.7071; ly *= 0.7071; }
        return PadState.Empty with
        {
            Buttons = b, LX = lx, LY = ly, RX = Math.Clamp(rx, -1, 1), RY = Math.Clamp(ry, -1, 1),
            L2 = actions.Contains("L2") ? 1 : 0, R2 = actions.Contains("R2") ? 1 : 0,
        };
    }
}
