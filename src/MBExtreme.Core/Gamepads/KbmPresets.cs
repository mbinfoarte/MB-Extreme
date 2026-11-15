namespace MBExtreme.Core.Gamepads;

/// <summary>Perfil pronto de teclado e mouse → controle para um jogo (mira, curva e teclas).</summary>
public sealed record KbmPreset(
    string Name,
    string Description,
    VirtualTarget Target,
    int Sensitivity,
    int Acceleration,
    int AntiDeadzone,
    int Smoothing,
    StickCurve Curve,
    IReadOnlyDictionary<string, string> Bindings,
    IReadOnlyList<string> Games,
    IReadOnlyList<string> InGameTips)
{
    /// <summary>Copia o perfil para os ajustes (a tecla de pausa fica como a pessoa escolheu).</summary>
    public void ApplyTo(KbmSettings s)
    {
        s.Target = Target;
        s.Sensitivity = Sensitivity;
        s.Acceleration = Acceleration;
        s.AntiDeadzone = AntiDeadzone;
        s.Smoothing = Smoothing;
        s.Curve = Curve;
        s.Bindings = new Dictionary<string, string>(Bindings, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Os ajustes de agora são iguais a este perfil?</summary>
    public bool Matches(KbmSettings s) =>
        s.Target == Target && s.Sensitivity == Sensitivity && s.Acceleration == Acceleration && s.AntiDeadzone == AntiDeadzone
        && s.Smoothing == Smoothing && s.Curve == Curve
        && s.Bindings.Count == Bindings.Count && Bindings.All(b => s.Bindings.TryGetValue(b.Key, out var v) && v == b.Value);
}

public static class KbmPresets
{
    /// <summary>
    /// Call of Duty com campanha offline de verdade (sem anti-cheat). Os novos (MW 2019 em diante, Warzone, Black Ops 6...)
    /// ficam de fora de propósito: rodam com o Ricochet até na campanha e aceitam mouse e teclado direto.
    /// </summary>
    public static KbmPreset CallOfDuty { get; } = new(
        "Call of Duty (campanha)",
        "Mira fina para a campanha offline: Xbox 360, sem aceleração, anti-zona morta baixa (deixe a do jogo no mínimo) e teclas no padrão do COD.",
        VirtualTarget.Xbox360, Sensitivity: 65, Acceleration: 0, AntiDeadzone: 8, Smoothing: 12, StickCurve.Suave,
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["W"] = "MoveUp", ["S"] = "MoveDown", ["A"] = "MoveLeft", ["D"] = "MoveRight",
            ["Space"] = "Cross",                         // pular
            ["C"] = "Circle", ["LeftCtrl"] = "Circle",   // agachar / deitar / deslizar
            ["R"] = "Square", ["F"] = "Square",          // recarregar e usar (segurar)
            ["E"] = "Triangle", ["Mouse4"] = "Triangle", // trocar de arma
            ["Q"] = "L1",                                // granada tática
            ["G"] = "R1", ["Mouse5"] = "R1",             // granada letal
            ["MouseRight"] = "L2", ["MouseLeft"] = "R2", // mirar e atirar
            ["LeftShift"] = "L3",                        // correr / prender a respiração
            ["V"] = "R3", ["MouseMiddle"] = "R3",        // faca
            ["D1"] = "Up", ["D2"] = "Right", ["D3"] = "Down", ["D4"] = "Left", // equipamentos e sequências
            ["Tab"] = "Create", ["Escape"] = "Options",  // objetivos / placar e pausa
        },
        new[]
        {
            "iw3sp", "CoDWaW", "iw4sp", "BlackOps", "iw5sp", "t6sp", "iw6sp64_ship", "s1_sp64_ship", "BlackOps3",
            "h1_sp64_ship", "iw7_ship", "s2_sp64_ship",
        },
        new[]
        {
            "No jogo: Opções › Controle › tipo de entrada Controle (desligue mouse e teclado para os ícones não piscarem).",
            "Zona morta do analógico direito no mínimo (0 ou 1) e curva de resposta Linear, se existir.",
            "Sensibilidade do controle no jogo entre 6 e 8. Ajuste fino por ela, não pelo MB.",
            "Se a mira andar sozinha, abaixe a anti-zona morta do MB de 2 em 2%. Se demorar para mexer, suba.",
        });

    /// <summary>Call of Duty com anti-cheat (Ricochet) até na campanha: aqui o controle virtual não deve ficar ligado.</summary>
    public static IReadOnlyList<string> ProtectedGames { get; } = new[] { "cod", "cod24-cod", "ModernWarfare", "BlackOpsColdWar", "Vanguard", "cod22-cod", "cod23-cod" };

    public static IReadOnlyList<KbmPreset> All { get; } = new[] { CallOfDuty };

    /// <summary>Perfil ligado ao executável do jogo, ou null.</summary>
    public static KbmPreset? ForGame(string? exeOrProcess)
    {
        string key = Norm(exeOrProcess);
        return key.Length == 0 ? null : All.FirstOrDefault(p => p.Games.Any(g => Norm(g) == key));
    }

    /// <summary>Jogo com anti-cheat que pune controle virtual (o MB desliga a conversão quando ele abre).</summary>
    public static bool IsProtected(string? exeOrProcess)
    {
        string key = Norm(exeOrProcess);
        return key.Length > 0 && ProtectedGames.Any(g => Norm(g) == key);
    }

    private static string Norm(string? exe)
    {
        if (string.IsNullOrWhiteSpace(exe)) return "";
        string t = exe.Trim();
        int slash = t.LastIndexOfAny(new[] { '\\', '/' });
        string n = (slash >= 0 ? t[(slash + 1)..] : t).ToLowerInvariant();
        return n.EndsWith(".exe", StringComparison.Ordinal) ? n[..^4] : n;
    }
}
