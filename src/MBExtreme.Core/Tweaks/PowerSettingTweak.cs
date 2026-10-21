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

    private int? ReadAc()
    {
        var result = Shell.Run("powercfg.exe", "/query", "SCHEME_CURRENT", _subgroup, _setting);
        return result.ExitCode == 0 ? ParseIndexes(result.Output).Ac : null;
    }

    private void WriteAc(int value)
    {
        var set = Shell.Run("powercfg.exe", "/setacvalueindex", "SCHEME_CURRENT", _subgroup, _setting, value.ToString());
        if (set.ExitCode != 0) throw new InvalidOperationException("O Windows recusou alterar o plano de energia.");
        Shell.Run("powercfg.exe", "/setactive", "SCHEME_CURRENT");
    }

    public override bool? IsApplied() => ReadAc() is { } current ? current == _value : null;

    public override void Apply()
    {
        if (!Store.Has(BackupKey))
        {
            int original = ReadAc() ?? throw new InvalidOperationException("Não foi possível ler a opção do plano de energia.");
            Store.Set(BackupKey, original.ToString());
        }

        WriteAc(_value);
    }

    public override void Revert()
    {
        if (Store.Get(BackupKey) is not { } saved || !int.TryParse(saved, out int original)) return;
        WriteAc(original);
        Store.Remove(BackupKey);
    }
}
