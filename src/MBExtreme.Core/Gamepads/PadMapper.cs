namespace MBExtreme.Core.Gamepads;

/// <summary>Controle virtual que o jogo vai enxergar.</summary>
public enum VirtualTarget { Xbox360, DualShock4 }

/// <summary>Zona morta e resposta de um analógico na emulação.</summary>
public sealed class StickTuning
{
    public int Deadzone { get; set; } = 8;
    public int AntiDeadzone { get; set; }
    public int Sensitivity { get; set; } = 100;
    public StickCurve Curve { get; set; } = StickCurve.Linear;
    public bool Axial { get; set; }
    public bool InvertY { get; set; }
}

/// <summary>Configuração da emulação (salva nas configurações do MB).</summary>
public sealed class EmulationSettings
{
    public VirtualTarget Target { get; set; } = VirtualTarget.Xbox360;
    public bool HidePhysical { get; set; } = true;
    public StickTuning Left { get; set; } = new();
    public StickTuning Right { get; set; } = new();
    public int TriggerDeadzone { get; set; } = 4;
    public bool SwapSticks { get; set; }
    public bool NintendoLayout { get; set; }
    /// <summary>Remapeamento: botão físico → botão virtual (nome do enum PadButtons; "None" = desligado).</summary>
    public Dictionary<string, string> Remap { get; set; } = new();
    public bool GyroAim { get; set; }
    public int GyroSensitivity { get; set; } = 40;
    /// <summary>Mira pelo giroscópio só enquanto segura L2 (mirando).</summary>
    public bool GyroOnlyWhenAiming { get; set; } = true;
}

/// <summary>
/// Converte o estado do controle físico no estado do controle virtual: remapeamento de botões,
/// zonas mortas (radial ou por eixo), curva, sensibilidade, gatilhos, troca de analógicos e mira pelo giroscópio.
/// </summary>
public static class PadMapper
{
    private static readonly PadButtons[] All = Enum.GetValues<PadButtons>().Where(b => b != PadButtons.None).ToArray();

    public static PadState Map(PadState s, EmulationSettings e)
    {
        // botões
        var buttons = PadButtons.None;
        foreach (var b in All)
        {
            if ((s.Buttons & b) == 0) continue;
            buttons |= Target(b, e);
        }

        // analógicos
        var (lx, ly) = Stick(s.LX, s.LY, e.Left);
        var (rx, ry) = Stick(s.RX, s.RY, e.Right);
        if (e.SwapSticks) (lx, ly, rx, ry) = (rx, ry, lx, ly);

        // mira pelo giroscópio: soma a rotação ao analógico direito
        if (e.GyroAim && (!e.GyroOnlyWhenAiming || s.L2 > 0.3))
        {
            double k = Math.Clamp(e.GyroSensitivity, 1, 100) / 1500.0;
            rx = Math.Clamp(rx - s.GyroYaw * k, -1, 1);
            ry = Math.Clamp(ry - s.GyroPitch * k, -1, 1);
        }

        double l2 = Trigger(s.L2, e.TriggerDeadzone), r2 = Trigger(s.R2, e.TriggerDeadzone);
        return s with { Buttons = buttons, LX = lx, LY = ly, RX = rx, RY = ry, L2 = l2, R2 = r2 };
    }

    private static PadButtons Target(PadButtons b, EmulationSettings e)
    {
        if (e.Remap.TryGetValue(b.ToString(), out string? to))
            return Enum.TryParse<PadButtons>(to, out var t) ? t : PadButtons.None;
        if (e.NintendoLayout)
            return b switch
            {
                PadButtons.Cross => PadButtons.Circle,
                PadButtons.Circle => PadButtons.Cross,
                PadButtons.Square => PadButtons.Triangle,
                PadButtons.Triangle => PadButtons.Square,
                _ => b,
            };
        return b;
    }

    public static (double X, double Y) Stick(double x, double y, StickTuning t)
    {
        if (t.InvertY) y = -y;
        var profile = new PadProfile { Deadzone = t.Deadzone, AntiDeadzone = t.AntiDeadzone, Sensitivity = t.Sensitivity, Curve = t.Curve };
        if (!t.Axial) return PadProfiles.Apply(profile, x, y);
        // por eixo: cada eixo tem sua zona morta (bom para plataforma e luta)
        static double Axis(double v, PadProfile p) => PadProfiles.Apply(p, v, 0).X;
        return (Axis(x, profile), Axis(y, profile));
    }

    public static double Trigger(double v, int deadzonePercent)
    {
        double dz = Math.Clamp(deadzonePercent, 0, 50) / 100.0;
        return v <= dz ? 0 : Math.Min(1, (v - dz) / (1 - dz));
    }

    /// <summary>Analógico -1..1 → eixo do Xbox (short, Y para cima positivo).</summary>
    public static short XboxAxis(double v, bool invert = false)
    {
        double d = Math.Clamp(invert ? -v : v, -1, 1);
        return (short)Math.Round(d >= 0 ? d * 32767 : d * 32768);
    }

    /// <summary>Analógico -1..1 → eixo do PS4 (byte, centro 128).</summary>
    public static byte Ds4Axis(double v) => (byte)Math.Clamp(Math.Round((Math.Clamp(v, -1, 1) + 1) * 127.5), 0, 255);

    public static byte TriggerByte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);

    /// <summary>
    /// Caminho do aparelho HID → identificador de instância do Windows (o que o HidHide usa).
    /// \\?\hid#vid_054c&amp;pid_0ce6&amp;mi_03#8&amp;1a2b&amp;0&amp;0000#{guid} → HID\VID_054C&amp;PID_0CE6&amp;MI_03\8&amp;1A2B&amp;0&amp;0000
    /// </summary>
    public static string? InstanceId(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string p = path.Trim();
        if (p.StartsWith(@"\\?\")) p = p[4..];
        int guid = p.LastIndexOf("#{", StringComparison.Ordinal);
        if (guid > 0) p = p[..guid];
        string[] parts = p.Split('#');
        return parts.Length < 3 ? null : string.Join('\\', parts).ToUpperInvariant();
    }
}
