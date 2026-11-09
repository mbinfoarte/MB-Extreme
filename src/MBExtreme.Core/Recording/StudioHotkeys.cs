namespace MBExtreme.Core.Recording;

public enum HotkeyModifier { CtrlShift, CtrlAlt }

public enum StudioAction { SaveReplay, ToggleRecord, ToggleStream }

/// <summary>Atalho global do estúdio: Ctrl+Shift ou Ctrl+Alt com uma tecla de função (F5 a F12, que o Windows e a maioria dos jogos não usam).</summary>
public sealed record HotkeyBinding(HotkeyModifier Modifier, int FKey)
{
    public const int MinKey = 5, MaxKey = 12;

    public bool IsValid => FKey is >= MinKey and <= MaxKey;

    public uint VirtualKey => (uint)(0x6F + FKey);

    public string Text => (Modifier == HotkeyModifier.CtrlShift ? "Ctrl + Shift + F" : "Ctrl + Alt + F") + FKey;

    /// <summary>Texto guardado nas configurações, por exemplo "CtrlShift+F9".</summary>
    public string Stored => (Modifier == HotkeyModifier.CtrlShift ? "CtrlShift" : "CtrlAlt") + "+F" + FKey;

    public static HotkeyBinding? Parse(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;
        var parts = stored.Split('+');
        if (parts.Length != 2 || parts[1].Length < 2 || parts[1][0] != 'F' || !int.TryParse(parts[1][1..], out int key)) return null;
        HotkeyModifier? mod = parts[0] switch { "CtrlShift" => HotkeyModifier.CtrlShift, "CtrlAlt" => HotkeyModifier.CtrlAlt, _ => null };
        if (mod is null) return null;
        var binding = new HotkeyBinding(mod.Value, key);
        return binding.IsValid ? binding : null;
    }
}

public static class StudioHotkeys
{
    public static string DefaultFor(StudioAction action) => action switch
    {
        StudioAction.SaveReplay => "CtrlShift+F9",
        StudioAction.ToggleRecord => "CtrlShift+F10",
        _ => "CtrlShift+F11",
    };

    public static string Title(StudioAction action) => action switch
    {
        StudioAction.SaveReplay => "Salvar replay",
        StudioAction.ToggleRecord => "Iniciar ou parar a gravação",
        _ => "Iniciar ou parar a transmissão",
    };

    /// <summary>Atalhos que aparecem repetidos entre as ações (o Windows só deixa um programa usar cada combinação).</summary>
    public static IReadOnlyList<string> Conflicts(IReadOnlyDictionary<StudioAction, HotkeyBinding?> bindings) =>
        bindings.Where(b => b.Value is not null)
            .GroupBy(b => b.Value!.Stored)
            .Where(g => g.Count() > 1)
            .Select(g => g.First().Value!.Text + " está em mais de uma ação: " + string.Join(" e ", g.Select(x => Title(x.Key).ToLowerInvariant())))
            .ToList();
}
