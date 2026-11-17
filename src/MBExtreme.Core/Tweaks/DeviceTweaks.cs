using System.Runtime.Versioning;
using Microsoft.Win32;

namespace MBExtreme.Core.Tweaks;

/// <summary>Um valor do registro que um ajuste quer gravar.</summary>
public sealed record RegistryTarget(string Path, string Name, object Value, RegistryValueKind Kind);

/// <summary>
/// Ajuste cujos valores dependem do PC (placas de rede, dispositivos PCI...). A lista de alvos é descoberta na hora;
/// o desfazer usa o que foi guardado no backup, então funciona mesmo se o dispositivo mudar de lugar.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TargetedRegistryTweak : Tweak
{
    private readonly RegistryHive _hive;
    private readonly Func<IReadOnlyList<RegistryTarget>> _targets;

    public TargetedRegistryTweak(TweakStore store, string id, TweakCategory category, TweakRisk risk, string title, string description,
        RegistryHive hive, Func<IReadOnlyList<RegistryTarget>> targets)
        : base(store, id, category, risk, title, description)
    {
        _hive = hive;
        _targets = targets;
    }

    private IReadOnlyList<RegistryTarget> SafeTargets()
    {
        try { return _targets(); }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return Array.Empty<RegistryTarget>(); }
    }

    public override bool? IsApplied()
    {
        var targets = SafeTargets();
        if (targets.Count == 0) return null;
        return targets.All(t => RegistryAccess.SameValue(RegistryAccess.Read(_hive, t.Path, t.Name), t.Value));
    }

    public override void Apply()
    {
        var targets = SafeTargets();
        if (targets.Count == 0) throw new InvalidOperationException("Nenhum dispositivo compatível encontrado neste PC.");
        foreach (var t in targets)
        {
            RegistryAccess.Backup(Store, Id, _hive, t.Path, t.Name);
            RegistryAccess.Write(_hive, t.Path, t.Name, t.Value, t.Kind);
        }
    }

    public override void Revert()
    {
        string prefix = $"{Id}|{_hive}|";
        foreach (string key in Store.KeysWithPrefix(prefix))
        {
            string rest = key[prefix.Length..];
            int bar = rest.LastIndexOf('|');
            if (bar < 0) { Store.Remove(key); continue; }
            RegistryAccess.Restore(Store, Id, _hive, rest[..bar], rest[(bar + 1)..]);
        }
    }
}

/// <summary>Descoberta de placas de rede e dispositivos PCI no registro, para os ajustes de dispositivo.</summary>
[SupportedOSPlatform("windows")]
public static class DeviceRegistry
{
    public const string NetClass = "{4d36e972-e325-11ce-bfc1-08002be10318}";
    public const string DisplayClass = "{4d36e968-e325-11ce-bfc1-08002be10318}";
    public const string NetClassPath = @"SYSTEM\CurrentControlSet\Control\Class\" + NetClass;
    private const string PciPath = @"SYSTEM\CurrentControlSet\Enum\PCI";

    /// <summary>Placa de rede física (sem adaptadores virtuais, de VPN, miniport ou Bluetooth).</summary>
    public static bool IsPhysicalAdapter(RegistryKey key) =>
        key.GetValue("NetCfgInstanceId") is not null && key.GetValue("DriverDesc") is string d
        && !d.Contains("Virtual", StringComparison.OrdinalIgnoreCase) && !d.Contains("Miniport", StringComparison.OrdinalIgnoreCase)
        && !d.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase) && !d.Contains("VPN", StringComparison.OrdinalIgnoreCase)
        && !d.Contains("TAP", StringComparison.Ordinal);

    /// <summary>
    /// Propriedades avançadas das placas de rede físicas que o driver declara (Ndi\Params\palavra-chave).
    /// <paramref name="valueFor"/> recebe a chave da definição da propriedade e devolve o valor desejado, ou nulo para pular.
    /// </summary>
    public static IReadOnlyList<RegistryTarget> AdapterProperties(IEnumerable<string> keywords, Func<RegistryKey, string?> valueFor)
    {
        var result = new List<RegistryTarget>();
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var parent = root.OpenSubKey(NetClassPath, false);
        if (parent is null) return result;
        foreach (string name in parent.GetSubKeyNames())
        {
            try
            {
                using var adapter = parent.OpenSubKey(name, false);
                if (adapter is null || !IsPhysicalAdapter(adapter)) continue;
                foreach (string keyword in keywords)
                {
                    using var param = adapter.OpenSubKey(@"Ndi\Params\" + keyword, false);
                    if (param is null) continue;
                    string? value = valueFor(param);
                    if (value is not null) result.Add(new RegistryTarget(NetClassPath + "\\" + name, keyword, value, RegistryValueKind.String));
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { /* subchave protegida */ }
        }
        return result;
    }

    /// <summary>Maior valor numérico que o driver aceita para a propriedade (campo "max"), ou nulo se a propriedade não é numérica.</summary>
    public static string? NumericMax(RegistryKey param)
    {
        string type = param.GetValue("type") as string ?? "";
        if (!type.Equals("int", StringComparison.OrdinalIgnoreCase) && !type.Equals("dword", StringComparison.OrdinalIgnoreCase)
            && !type.Equals("word", StringComparison.OrdinalIgnoreCase) && !type.Equals("long", StringComparison.OrdinalIgnoreCase)) return null;
        return param.GetValue("max") is string max && int.TryParse(max, out int n) && n > 0 ? n.ToString() : null;
    }

    /// <summary>Opção "0" existe na lista de valores da propriedade (ex.: Desativado no LSO).</summary>
    public static string? EnumOffOption(RegistryKey param)
    {
        using var options = param.OpenSubKey("enum", false);
        return options?.GetValue("0") is not null ? "0" : null;
    }

    /// <summary>Chaves do modo MSI dos dispositivos PCI das classes pedidas (placa de vídeo, placa de rede) que já passaram pela instalação do driver.</summary>
    public static IReadOnlyList<RegistryTarget> MsiTargets(params string[] classGuids)
    {
        var result = new List<RegistryTarget>();
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var pci = root.OpenSubKey(PciPath, false);
        if (pci is null) return result;
        foreach (string device in pci.GetSubKeyNames())
        {
            try
            {
                using var deviceKey = pci.OpenSubKey(device, false);
                if (deviceKey is null) continue;
                foreach (string instance in deviceKey.GetSubKeyNames())
                {
                    using var inst = deviceKey.OpenSubKey(instance, false);
                    if (inst?.GetValue("ClassGUID") is not string cls || !classGuids.Any(g => g.Equals(cls, StringComparison.OrdinalIgnoreCase))) continue;
                    using var interrupts = inst.OpenSubKey(@"Device Parameters\Interrupt Management", false);
                    if (interrupts is null) continue; // dispositivo sem driver instalado
                    result.Add(new RegistryTarget($@"{PciPath}\{device}\{instance}\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties",
                        "MSISupported", 1, RegistryValueKind.DWord));
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { /* protegido */ }
        }
        return result;
    }
}

/// <summary>
/// Menu de contexto clássico do Windows 11: cria a chave vazia que faz o Explorador usar o menu antigo.
/// Desfazer apaga a chave se ela não existia antes.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ClassicContextMenuTweak : Tweak
{
    public const string ClsidPath = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
    public const string ServerPath = ClsidPath + @"\InprocServer32";

    public ClassicContextMenuTweak(TweakStore store, string id, TweakCategory category, TweakRisk risk, string title, string description)
        : base(store, id, category, risk, title, description) { }

    private string ExistedKey => Id + "|existed";

    public override bool? IsApplied() => RegistryAccess.Read(RegistryHive.CurrentUser, ServerPath, "") is string s && s.Length == 0;

    public override void Apply()
    {
        if (!Store.Has(ExistedKey))
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            using var existing = root.OpenSubKey(ClsidPath, false);
            Store.Set(ExistedKey, existing is null ? "0" : "1");
        }
        RegistryAccess.Backup(Store, Id, RegistryHive.CurrentUser, ServerPath, "");
        RegistryAccess.Write(RegistryHive.CurrentUser, ServerPath, "", "", RegistryValueKind.String);
    }

    public override void Revert()
    {
        if (Store.Get(ExistedKey) == "0")
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            root.DeleteSubKeyTree(ClsidPath, false);
            Store.Remove(RegistryAccess.BackupKey(Id, RegistryHive.CurrentUser, ServerPath, ""));
        }
        else
        {
            RegistryAccess.Restore(Store, Id, RegistryHive.CurrentUser, ServerPath, "");
        }
        Store.Remove(ExistedKey);
    }
}
