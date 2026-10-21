using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using MBExtreme.Core.Windows;

namespace MBExtreme.Core.Tweaks;

/// <summary>Ajuste de uma opção do plano de energia ativo (valor "na tomada"). O valor original é guardado e restaurado.</summary>
[SupportedOSPlatform("windows")]
public sealed class PowerSettingTweak : Tweak
{
    private static readonly Regex Hex = new(@"0x([0-9a-fA-F]{8})");
    private readonly string _subgroup;
    private readonly string _setting;
    private readonly int _value;

    public PowerSettingTweak(
        TweakStore store, string id, TweakCategory category, TweakRisk risk, string title, string description,
        string subgroupGuid, string settingGuid, int value)
        : base(store, id, category, risk, title, description)
    {
        _subgroup = subgroupGuid;
        _setting = settingGuid;
        _value = value;
    }

    private string BackupKey => $"{Id}|ac";

    /// <summary>
    /// Lê os dois últimos valores 0x... da saída de "powercfg /query": o penúltimo é o índice na tomada (CA) e o último, na bateria (CC).
    /// Não depende do idioma do Windows.
    /// </summary>
    public static (int? Ac, int? Dc) ParseIndexes(string powercfgOutput)
    {
        var matches = Hex.Matches(powercfgOutput);
        if (matches.Count < 2) return (null, null);
        int Value(Match m) => (int)Convert.ToUInt32(m.Groups[1].Value, 16);
        return (Value(matches[^2]), Value(matches[^1]));
    }

    private static string? ActiveScheme() => FirstGuid(Shell.Run("powercfg.exe", "/getactivescheme").Output);

    private static string? FirstGuid(string text) => PowerPlanTweak.FirstGuid(text);

    /// <summary>Plano em que o ajuste foi aplicado (guardado no backup); sem backup, o plano ativo.</summary>
    private (string? Scheme, int? Original) Saved()
    {
        string? saved = Store.Get(BackupKey);
        if (saved is null) return (null, null);
        int split = saved.IndexOf(':');
        if (split < 0) return (null, int.TryParse(saved, out int legacy) ? legacy : null);
        return (saved[..split], int.TryParse(saved[(split + 1)..], out int value) ? value : null);
    }

    private int? ReadAc(string scheme)
    {
        var result = Shell.Run("powercfg.exe", "/query", scheme, _subgroup, _setting);
        return result.ExitCode == 0 ? ParseIndexes(result.Output).Ac : null;
    }

    private void WriteAc(string scheme, int value)
    {
        var set = Shell.Run("powercfg.exe", "/setacvalueindex", scheme, _subgroup, _setting, value.ToString());
        if (set.ExitCode != 0) throw new InvalidOperationException("O Windows recusou alterar o plano de energia.");
        // Reaplica só se o plano alterado é o que está ativo agora.
        if (string.Equals(ActiveScheme(), scheme, StringComparison.OrdinalIgnoreCase))
            Shell.Run("powercfg.exe", "/setactive", scheme);
    }

    public override bool? IsApplied()
    {
        string? scheme = Saved().Scheme ?? ActiveScheme();
        if (scheme is null) return null;
        return ReadAc(scheme) is { } current ? current == _value : null;
    }

    public override void Apply()
    {
        var (savedScheme, _) = Saved();
        string scheme = savedScheme ?? ActiveScheme()
            ?? throw new InvalidOperationException("Não foi possível ler o plano de energia atual.");

        if (!Store.Has(BackupKey))
        {
            int original = ReadAc(scheme) ?? throw new InvalidOperationException("Não foi possível ler a opção do plano de energia.");
            Store.Set(BackupKey, $"{scheme}:{original}");
        }

        WriteAc(scheme, _value);
    }

    public override void Revert()
    {
        var (scheme, original) = Saved();
        if (original is not { } value) return;
        WriteAc(scheme ?? ActiveScheme() ?? "SCHEME_CURRENT", value);
        Store.Remove(BackupKey);
    }
}
