using System.Runtime.Versioning;
using Microsoft.Win32;

namespace MBExtreme.Core.Tweaks;

public sealed record RegistryValueSpec(
    RegistryHive Hive,
    string Path,
    string Name,
    object Value,
    RegistryValueKind Kind = RegistryValueKind.DWord);

/// <summary>Acesso ao registro e backup do valor original, compartilhado pelos ajustes.</summary>
[SupportedOSPlatform("windows")]
internal static class RegistryAccess
{
    private const string Absent = "<ausente>";

    public static object? Read(RegistryHive hive, string path, string name)
    {
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = root.OpenSubKey(path, false);
        return key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }

    public static bool SameValue(object? current, object expected)
    {
        if (current is null) return false;
        if (current is int a && expected is int b) return a == b;
        return string.Equals(current.ToString(), expected.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public static string BackupKey(string tweakId, RegistryHive hive, string path, string name) =>
        $"{tweakId}|{hive}|{path}|{name}";

    /// <summary>Guarda o valor atual antes da primeira alteração.</summary>
    public static void Backup(TweakStore store, string tweakId, RegistryHive hive, string path, string name)
    {
        string key = BackupKey(tweakId, hive, path, name);
        if (store.Has(key)) return;

        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var subKey = root.OpenSubKey(path, false);
        object? current = subKey?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (current is null)
        {
            store.Set(key, Absent);
            return;
        }

        var kind = subKey!.GetValueKind(name);
        store.Set(key, $"{kind}:{current}");
    }

    public static void Write(RegistryHive hive, string path, string name, object value, RegistryValueKind kind)
    {
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = root.CreateSubKey(path, true)
            ?? throw new InvalidOperationException($"Não foi possível abrir a chave {path}.");
        key.SetValue(name, value, kind);
    }

    /// <summary>Restaura o valor guardado e apaga o backup.</summary>
    public static void Restore(TweakStore store, string tweakId, RegistryHive hive, string path, string name)
    {
        string key = BackupKey(tweakId, hive, path, name);
        string? saved = store.Get(key);
        if (saved is null) return;

        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        if (saved == Absent)
        {
            using var subKey = root.OpenSubKey(path, true);
            subKey?.DeleteValue(name, false);
        }
        else
        {
            int separator = saved.IndexOf(':');
            var kind = Enum.Parse<RegistryValueKind>(saved[..separator]);
            string text = saved[(separator + 1)..];
            object value = kind switch
            {
                RegistryValueKind.DWord => int.Parse(text),
                RegistryValueKind.QWord => long.Parse(text),
                _ => text,
            };
            Write(hive, path, name, value, kind);
        }

        store.Remove(key);
    }
}

/// <summary>Ajuste feito por um ou mais valores do registro.</summary>
[SupportedOSPlatform("windows")]
public sealed class RegistryTweak : Tweak
{
    private readonly IReadOnlyList<RegistryValueSpec> _specs;

    public RegistryTweak(
        TweakStore store, string id, TweakCategory category, TweakRisk risk, string title, string description,
        IReadOnlyList<RegistryValueSpec> specs)
        : base(store, id, category, risk, title, description)
    {
        _specs = specs;
    }

    public override bool? IsApplied()
    {
        foreach (var spec in _specs)
        {
            if (!RegistryAccess.SameValue(RegistryAccess.Read(spec.Hive, spec.Path, spec.Name), spec.Value))
                return false;
        }

        return true;
    }

    public override void Apply()
    {
        foreach (var spec in _specs)
        {
            RegistryAccess.Backup(Store, Id, spec.Hive, spec.Path, spec.Name);
            RegistryAccess.Write(spec.Hive, spec.Path, spec.Name, spec.Value, spec.Kind);
        }
    }

    public override void Revert()
    {
        foreach (var spec in _specs)
            RegistryAccess.Restore(Store, Id, spec.Hive, spec.Path, spec.Name);
    }
}
