using System.Runtime.Versioning;
using Microsoft.Win32;
using MBExtreme.Core.Tweaks;

namespace MBExtreme.Core.Games;

/// <summary>
/// Prioridade de CPU alta por programa, pelo recurso do próprio Windows (Image File Execution Options\PerfOptions).
/// O Windows aplica toda vez que o jogo abre. O valor original é guardado e a chave é removida ao desfazer.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GamePriorityService
{
    private const string Root = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
    private const string ValueName = "CpuPriorityClass";
    private const int High = 3;
    private readonly TweakStore _store;

    public GamePriorityService(TweakStore store) => _store = store;

    private static string PerfPath(string exe) => $@"{Root}\{exe}\PerfOptions";
    private static string Id(string exe) => "game-prio:" + exe.ToLowerInvariant();

    public bool IsApplied(string exe)
    {
        if (!ExeGuesser.IsValidExeName(exe)) return false;
        return RegistryAccess.SameValue(RegistryAccess.Read(RegistryHive.LocalMachine, PerfPath(exe), ValueName), High);
    }

    public void Apply(string exe)
    {
        if (!ExeGuesser.IsValidExeName(exe)) throw new ArgumentException("Nome de programa inválido.", nameof(exe));
        RegistryAccess.Backup(_store, Id(exe), RegistryHive.LocalMachine, PerfPath(exe), ValueName);
        RegistryAccess.Write(RegistryHive.LocalMachine, PerfPath(exe), ValueName, High, RegistryValueKind.DWord);
    }

    public void Revert(string exe)
    {
        if (!ExeGuesser.IsValidExeName(exe)) return;
        RegistryAccess.Restore(_store, Id(exe), RegistryHive.LocalMachine, PerfPath(exe), ValueName);
        // remove as chaves vazias que o próprio ajuste criou
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var perf = root.OpenSubKey(PerfPath(exe), false);
        if (perf is not null && perf.GetValueNames().Length == 0 && perf.SubKeyCount == 0)
        {
            using var ifeo = root.OpenSubKey($@"{Root}\{exe}", true);
            ifeo?.DeleteSubKey("PerfOptions", false);
            using var parent = root.OpenSubKey(Root, true);
            using var app = parent?.OpenSubKey(exe, false);
            if (app is not null && app.GetValueNames().Length == 0 && app.SubKeyCount == 0) parent!.DeleteSubKey(exe, false);
        }
    }
}
