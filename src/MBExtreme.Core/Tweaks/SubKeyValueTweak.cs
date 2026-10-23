using System.Runtime.Versioning;
using Microsoft.Win32;

namespace MBExtreme.Core.Tweaks;

/// <summary>
/// Grava os mesmos valores em várias subchaves de uma chave (adaptadores de rede, placas de vídeo...).
/// Cada subchave que passa no filtro tem o valor original guardado.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SubKeyValueTweak : Tweak
{
    private readonly RegistryHive _hive;
    private readonly string _parent;
    private readonly IReadOnlyList<(string Name, object Value, RegistryValueKind Kind)> _values;
    private readonly Func<RegistryKey, bool> _filter;

    public SubKeyValueTweak(TweakStore store, string id, TweakCategory category, TweakRisk risk, string title, string description,
        RegistryHive hive, string parentPath, IReadOnlyList<(string Name, object Value, RegistryValueKind Kind)> values, Func<RegistryKey, bool> filter)
        : base(store, id, category, risk, title, description)
    {
        _hive = hive;
        _parent = parentPath;
        _values = values;
        _filter = filter;
    }

    private List<string> Paths()
    {
        var result = new List<string>();
        using var root = RegistryKey.OpenBaseKey(_hive, RegistryView.Registry64);
        RegistryKey? parent;
        string[] names;
        try
        {
            parent = root.OpenSubKey(_parent, false);
            names = parent?.GetSubKeyNames() ?? Array.Empty<string>();
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { return result; }
        using var parentKey = parent;
        if (parentKey is null) return result;
        foreach (string name in names)
        {
            try
            {
                using var key = parentKey.OpenSubKey(name, false);
                if (key is not null && _filter(key)) result.Add(_parent + "\\" + name);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { /* subchave protegida: ignora */ }
        }
        return result;
    }

    public override bool? IsApplied()
    {
        var paths = Paths();
        if (paths.Count == 0) return null;
        return paths.All(p => _values.All(v => RegistryAccess.SameValue(RegistryAccess.Read(_hive, p, v.Name), v.Value)));
    }

    public override void Apply()
    {
        foreach (string path in Paths())
            foreach (var (name, value, kind) in _values)
            {
                RegistryAccess.Backup(Store, Id, _hive, path, name);
                RegistryAccess.Write(_hive, path, name, value, kind);
            }
    }

    public override void Revert()
    {
        foreach (string path in Paths())
            foreach (var (name, _, _) in _values)
                RegistryAccess.Restore(Store, Id, _hive, path, name);
    }
}

/// <summary>Descobre as marcas de placa de vídeo pelas chaves de driver do Windows (leitura rápida, sem WMI).</summary>
[SupportedOSPlatform("windows")]
public static class GpuClass
{
    public const string ClassPath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    public static bool IsVendorKey(RegistryKey key, params string[] words)
    {
        string desc = key.GetValue("DriverDesc") as string ?? "";
        return desc.Length > 0 && words.Any(w => desc.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    public static bool Has(params string[] words)
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        try
        {
            using var parent = root.OpenSubKey(ClassPath, false);
            if (parent is null) return false;
            foreach (string name in parent.GetSubKeyNames())
            {
                try
                {
                    using var key = parent.OpenSubKey(name, false);
                    if (key is not null && IsVendorKey(key, words)) return true;
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { }
        return false;
    }

    public static bool Amd() => Has("AMD", "Radeon");
    public static bool Nvidia() => Has("NVIDIA", "GeForce");
    public static bool Intel() => Has("Intel(R)", "Intel ");
}
