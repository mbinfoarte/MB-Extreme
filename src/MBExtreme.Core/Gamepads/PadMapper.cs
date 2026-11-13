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

    // ---- ajustes finos
    public bool InvertX { get; set; }
    /// <summary>Zona morta externa (%): a partir de 100% − este valor a saída já é máxima (controles gastos que não chegam na borda).</summary>
    public int OuterDeadzone { get; set; }
    /// <summary>Expoente da curva Personalizada × 100 (100 = linear, 200 = suave forte, 50 = agressiva).</summary>
    public int CurvePower { get; set; } = 100;
    /// <summary>Força do eixo vertical em relação ao horizontal (%).</summary>
    public int VerticalScale { get; set; } = 100;
    /// <summary>Suavização (%): tira a tremida da mão; valores altos atrasam um pouco o comando.</summary>
    public int Smoothing { get; set; }
    /// <summary>Anti-retorno: ao soltar o analógico de uma vez, a mola faz ele passar do centro e gerar um comando ao contrário. Isso segura.</summary>
    public bool AntiSnapback { get; set; }
}

/// <summary>Ajuste fino de um gatilho (L2 ou R2).</summary>
public sealed class TriggerTuning
{
    /// <summary>Zona morta inicial (%); null = usa a zona morta geral dos gatilhos.</summary>
    public int? Deadzone { get; set; }
    /// <summary>Até onde precisa apertar para chegar em 100% (%).</summary>
    public int Max { get; set; } = 100;
    /// <summary>Gatilho sensível: qualquer toque depois da zona morta já vale 100% (tiro rápido).</summary>
    public bool HairTrigger { get; set; }
    public StickCurve Curve { get; set; } = StickCurve.Linear;
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

    // ---- ajustes finos
    public TriggerTuning L2 { get; set; } = new();
    public TriggerTuning R2 { get; set; } = new();
    /// <summary>Movimentos menores que isto (graus por segundo) são ignorados: a mão parada não mexe a mira.</summary>
    public int GyroDeadzone { get; set; } = 1;
    /// <summary>Suavização do giroscópio (%), filtro que só suaviza movimentos lentos (mira fina) e deixa os rápidos sem atraso.</summary>
    public int GyroSmoothing { get; set; } = 30;
    public bool GyroInvertX { get; set; }
    public bool GyroInvertY { get; set; }
    /// <summary>Força do giroscópio no eixo vertical em relação ao horizontal (%).</summary>
    public int GyroVertical { get; set; } = 100;

    /// <summary>Cópia rasa sem a mira pelo giroscópio (para a prévia dos analógicos, sem mexer no que a emulação está usando).</summary>
    public EmulationSettings WithoutGyro()
    {
        var copy = (EmulationSettings)MemberwiseClone();
        copy.GyroAim = false;
        return copy;
    }
}

/// <summary>
/// Filtro "One Euro" (Casiez, Roussel e Vogel, 2012): passa-baixa cujo corte sobe com a velocidade. Parado ou devagar = muito suave;
/// rápido = quase sem atraso. Implementação própria a partir do artigo.
/// </summary>
public sealed class OneEuroFilter
{
    private double _x, _dx;
    private bool _has;
    public double MinCutoff { get; set; } = 1.0;
    public double Beta { get; set; } = 0.02;
    public double DerivativeCutoff { get; set; } = 1.0;

    private static double Alpha(double cutoff, double dt)
    {
        double tau = 1.0 / (2 * Math.PI * cutoff);
        return 1.0 / (1.0 + tau / dt);
    }

    public double Filter(double value, double dt)
    {
        if (dt <= 0 || double.IsNaN(value)) return value;
        if (!_has) { _x = value; _dx = 0; _has = true; return value; }
        double dx = (value - _x) / dt;
        _dx += Alpha(DerivativeCutoff, dt) * (dx - _dx);
        double cutoff = MinCutoff + Beta * Math.Abs(_dx);
        _x += Alpha(cutoff, dt) * (value - _x);
        return _x;
    }

    public void Reset() => _has = false;
}

/// <summary>Estado dos ajustes que dependem do tempo (suavização, anti-retorno, filtro do giroscópio). Um por controle virtual.</summary>
public sealed class PadFilter
{
    internal sealed class StickState
    {
        public double X, Y, RawX, RawY, Hold;
        public bool Has;
    }

    internal readonly StickState Left = new(), Right = new();
    internal readonly OneEuroFilter GyroX = new(), GyroY = new();

    public void Reset()
    {
        Left.Has = Right.Has = false;
        Left.Hold = Right.Hold = 0;
        GyroX.Reset();
        GyroY.Reset();
    }
}

/// <summary>
/// Converte o estado do controle físico no estado do controle virtual: remapeamento de botões,
/// zonas mortas (radial ou por eixo), curva, sensibilidade, gatilhos, troca de analógicos e mira pelo giroscópio.
/// </summary>
public static class PadMapper
{
    private static readonly PadButtons[] All = Enum.GetValues<PadButtons>().Where(b => b != PadButtons.None).ToArray();

    public static PadState Map(PadState s, EmulationSettings e) => Map(s, e, null, 0);

    /// <summary>Com filtro: suavização, anti-retorno e giroscópio filtrado (dt = segundos desde o pacote anterior).</summary>
    public static PadState Map(PadState s, EmulationSettings e, PadFilter? filter, double dt)
    {
        // botões
        var buttons = PadButtons.None;
        foreach (var b in All)
        {
            if ((s.Buttons & b) == 0) continue;
            buttons |= Target(b, e);
        }

        // analógicos
        var (lx, ly) = Stick(s.LX, s.LY, e.Left, filter?.Left, dt);
        var (rx, ry) = Stick(s.RX, s.RY, e.Right, filter?.Right, dt);
        if (e.SwapSticks) (lx, ly, rx, ry) = (rx, ry, lx, ly);

        // mira pelo giroscópio: soma a rotação ao analógico direito
        if (e.GyroAim && (!e.GyroOnlyWhenAiming || s.L2 > 0.3))
        {
            var (gx, gy) = Gyro(s.GyroYaw, s.GyroPitch, e, filter, dt);
            double k = Math.Clamp(e.GyroSensitivity, 1, 100) / 1500.0;
            rx = Math.Clamp(rx - gx * k, -1, 1);
            ry = Math.Clamp(ry - gy * k, -1, 1);
        }
        else { filter?.GyroX.Reset(); filter?.GyroY.Reset(); }

        double l2 = Trigger(s.L2, e.L2 ?? new(), e.TriggerDeadzone), r2 = Trigger(s.R2, e.R2 ?? new(), e.TriggerDeadzone);
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

    public static (double X, double Y) Stick(double x, double y, StickTuning t) => Stick(x, y, t, null, 0);

    internal static (double X, double Y) Stick(double x, double y, StickTuning t, PadFilter.StickState? st, double dt)
    {
        if (t.InvertY) y = -y;
        if (t.InvertX) x = -x;

        // anti-retorno: o analógico estava longe do centro e, de um pacote para o outro, foi parar do lado oposto = mola, não a pessoa
        if (st is not null && t.AntiSnapback && dt > 0)
        {
            double prev = Math.Sqrt(st.RawX * st.RawX + st.RawY * st.RawY);
            double now = Math.Sqrt(x * x + y * y);
            bool crossed = st.Has && prev > 0.5 && (st.RawX * x + st.RawY * y) < 0 && now < 0.7;
            st.RawX = x; st.RawY = y;
            if (crossed) st.Hold = 0.05;
            else if (st.Hold > 0) st.Hold -= dt;
            if (st.Hold > 0)
            {
                if (now < 0.7) { st.X = st.Y = 0; st.Has = true; return (0, 0); }
                st.Hold = 0;
            }
        }
        else if (st is not null) { st.RawX = x; st.RawY = y; }

        (double X, double Y) o = t.Axial ? (Axis(x, t), Axis(y, t)) : Radial(x, y, t);

        if (t.VerticalScale != 100) o.Y = Math.Clamp(o.Y * Math.Clamp(t.VerticalScale, 25, 200) / 100.0, -1, 1);

        // suavização: média que segue o comando com até ~40 ms de atraso no máximo
        if (st is not null && t.Smoothing > 0 && dt > 0)
        {
            if (!st.Has) { st.X = o.X; st.Y = o.Y; }
            double tau = Math.Clamp(t.Smoothing, 0, 100) / 100.0 * 0.04;
            double a = 1 - Math.Exp(-dt / tau);
            st.X += (o.X - st.X) * a;
            st.Y += (o.Y - st.Y) * a;
            if (Math.Abs(st.X) < 1e-4) st.X = 0;
            if (Math.Abs(st.Y) < 1e-4) st.Y = 0;
            o = (Math.Round(st.X, 4), Math.Round(st.Y, 4));
        }
        if (st is not null) { st.X = o.X; st.Y = o.Y; st.Has = true; }
        return o;
    }

    /// <summary>Resposta de 0..1 depois da zona morta: curva, anti-zona morta e sensibilidade (a mesma conta do perfil do controle).</summary>
    public static double Response(double r, StickTuning t)
    {
        double dz = Math.Clamp(t.Deadzone, 0, 50) / 100.0;
        double outer = Math.Clamp(t.OuterDeadzone, 0, 40) / 100.0;
        if (r <= dz) return 0;
        double span = Math.Max(0.05, 1 - dz - outer);
        double v = Math.Min(1, (r - dz) / span);
        v = Curve(v, t.Curve, t.CurvePower);
        double anti = Math.Clamp(t.AntiDeadzone, 0, 40) / 100.0;
        v = anti + (1 - anti) * v;
        return Math.Min(1, v * Math.Clamp(t.Sensitivity, 10, 200) / 100.0);
    }

    public static double Curve(double v, StickCurve curve, int power = 100) => curve switch
    {
        StickCurve.Suave => v * v,
        StickCurve.Agressiva => Math.Sqrt(v),
        StickCurve.Personalizada => Math.Pow(v, Math.Clamp(power, 25, 400) / 100.0),
        _ => v,
    };

    private static (double, double) Radial(double x, double y, StickTuning t)
    {
        double r = Math.Sqrt(x * x + y * y);
        if (r < 1e-9) return (0, 0);
        double v = Response(Math.Min(1, r), t);
        return (Math.Round(x / r * v, 4), Math.Round(y / r * v, 4));
    }

    // por eixo: cada eixo tem sua zona morta (bom para plataforma e luta)
    private static double Axis(double v, StickTuning t) => Math.Round(Math.Sign(v) * Response(Math.Min(1, Math.Abs(v)), t), 4);

    public static double Trigger(double v, int deadzonePercent) => Trigger(v, new TriggerTuning(), deadzonePercent);

    public static double Trigger(double v, TriggerTuning t, int generalDeadzone)
    {
        double dz = Math.Clamp(t.Deadzone ?? generalDeadzone, 0, 50) / 100.0;
        double max = Math.Clamp(t.Max, 50, 100) / 100.0;
        if (v <= dz) return 0;
        if (t.HairTrigger) return 1;
        double o = Math.Min(1, (v - dz) / Math.Max(0.05, max - dz));
        return Math.Round(Curve(o, t.Curve), 4);
    }

    /// <summary>Giroscópio para a mira: zona morta (mão parada), filtro One Euro, inversão e força vertical.</summary>
    public static (double X, double Y) Gyro(double yaw, double pitch, EmulationSettings e, PadFilter? f = null, double dt = 0)
    {
        double dz = Math.Clamp(e.GyroDeadzone, 0, 20);
        static double Dead(double v, double dz) => Math.Abs(v) <= dz ? 0 : v - Math.Sign(v) * dz;
        double gx = Dead(yaw, dz), gy = Dead(pitch, dz);
        if (f is not null && dt > 0 && e.GyroSmoothing > 0)
        {
            // mais suavização = corte mínimo mais baixo (5 Hz até 0,5 Hz)
            double min = 5.0 - 4.5 * Math.Clamp(e.GyroSmoothing, 0, 100) / 100.0;
            f.GyroX.MinCutoff = f.GyroY.MinCutoff = min;
            f.GyroX.Beta = f.GyroY.Beta = 0.02;
            gx = f.GyroX.Filter(gx, dt);
            gy = f.GyroY.Filter(gy, dt);
        }
        if (e.GyroInvertX) gx = -gx;
        if (e.GyroInvertY) gy = -gy;
        gy *= Math.Clamp(e.GyroVertical, 25, 200) / 100.0;
        return (gx, gy);
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
