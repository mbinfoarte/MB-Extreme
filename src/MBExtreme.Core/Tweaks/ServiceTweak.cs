using System.Runtime.Versioning;
using Microsoft.Win32;
using MBExtreme.Core.Windows;

namespace MBExtreme.Core.Tweaks;

/// <summary>Muda o tipo de inicialização de serviços do Windows (3 = manual, 4 = desativado).</summary>
[SupportedOSPlatform("windows")]
public sealed class ServiceTweak : Tweak
{
    private const string ServicesPath = @"SYSTEM\CurrentControlSet\Services\";
    private readonly IReadOnlyList<string> _services;
    private readonly int _startType;

    public ServiceTweak(
        TweakStore store, string id, TweakCategory category, TweakRisk risk, string title, string description,
        IReadOnlyList<string> services, int startType)
        : base(store, id, category, risk, title, description)
    {
        _services = services;
        _startType = startType;
    }

    public override bool? IsApplied()
    {
        bool anyFound = false;
        foreach (var service in _services)
        {
            object? current = RegistryAccess.Read(RegistryHive.LocalMachine, ServicesPath + service, "Start");
            if (current is null) continue;
            anyFound = true;
            if (!RegistryAccess.SameValue(current, _startType)) return false;
        }

        return anyFound ? true : null;
    }

    public override void Apply()
    {
        foreach (var service in _services)
        {
            string path = ServicesPath + service;
            if (RegistryAccess.Read(RegistryHive.LocalMachine, path, "Start") is null) continue;

            RegistryAccess.Backup(Store, Id, RegistryHive.LocalMachine, path, "Start");
            RegistryAccess.Write(RegistryHive.LocalMachine, path, "Start", _startType, RegistryValueKind.DWord);

            // Desativado: também para o serviço agora. O sc.exe pode falhar se ele já estiver parado.
            if (_startType == 4) Shell.Run("sc.exe", "stop", service);
        }
    }

    public override void Revert()
    {
        foreach (var service in _services)
            RegistryAccess.Restore(Store, Id, RegistryHive.LocalMachine, ServicesPath + service, "Start");
    }
}
