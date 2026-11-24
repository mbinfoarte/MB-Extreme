using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using MBExtreme.Core.Tweaks;

namespace MBExtreme.Core.Windows;

public sealed record PowerPlan(string Guid, string Name, bool Active);

/// <summary>Recursos do próprio Windows para jogos: Modo de Jogo, agendamento de GPU (HAGS) e planos de energia. Tudo com backup do valor original.</summary>
[SupportedOSPlatform("windows")]
public sealed class GameSettingsService
{
    private const string GameModeId = "gs-gamemode";
    private const string HagsId = "gs-hags";
    private const string PowerId = "gs-power";
    private const string GameBarPath = @"Software\Microsoft\GameBar";
    private const string GraphicsPath = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    private static readonly Regex PlanLine = new(
        @"(?<guid>[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12})\s+\((?<name>.*?)\)\s*(?<star>\*)?");

    private readonly TweakStore _store;

    public GameSettingsService(TweakStore store) => _store = store;

    public bool HasBackup => _store.HasPrefix("gs-");

    /// <summary>Modo de Jogo: no Windows 10 e 11 vem ligado quando o valor não existe.</summary>
    public bool GameModeEnabled()
    {
        object? value = RegistryAccess.Read(RegistryHive.CurrentUser, GameBarPath, "AutoGameModeEnabled");
        return value is not int number || number != 0;
    }

    public void SetGameMode(bool on)
    {
        foreach (var name in new[] { "AutoGameModeEnabled", "AllowAutoGameMode" })
        {
            RegistryAccess.Backup(_store, GameModeId, RegistryHive.CurrentUser, GameBarPath, name);
            RegistryAccess.Write(RegistryHive.CurrentUser, GameBarPath, name, on ? 1 : 0, RegistryValueKind.DWord);
        }
    }

    /// <summary>true = ligado, false = desligado, null = padrão do Windows (o valor não existe).</summary>
    public bool? HagsEnabled()
    {
        object? value = RegistryAccess.Read(RegistryHive.LocalMachine, GraphicsPath, "HwSchMode");
        return value is int mode ? mode == 2 : null;
    }

    public void SetHags(bool on)
    {
        RegistryAccess.Backup(_store, HagsId, RegistryHive.LocalMachine, GraphicsPath, "HwSchMode");
        RegistryAccess.Write(RegistryHive.LocalMachine, GraphicsPath, "HwSchMode", on ? 2 : 1, RegistryValueKind.DWord);
    }

    public static IReadOnlyList<PowerPlan> ParsePlans(string powercfgOutput) =>
        PlanLine.Matches(powercfgOutput)
            .Select(m => new PowerPlan(m.Groups["guid"].Value, m.Groups["name"].Value, m.Groups["star"].Success))
            .ToList();

    public IReadOnlyList<PowerPlan> PowerPlans() => ParsePlans(Shell.Run("powercfg.exe", "/list").Output);

    public void ActivatePlan(string guid)
    {
        if (!Regex.IsMatch(guid, @"^[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}$"))
            throw new ArgumentException("Plano de energia inválido.", nameof(guid));

        string? current = PowerPlans().FirstOrDefault(p => p.Active)?.Guid;
        if (current is not null && !_store.Has(PowerId + "|previous")) _store.Set(PowerId + "|previous", current);

        if (Shell.Run("powercfg.exe", "/setactive", guid).ExitCode != 0)
            throw new InvalidOperationException("O Windows recusou ativar o plano de energia.");
    }

    /// <summary>Volta Modo de Jogo, HAGS e plano de energia aos valores de antes da primeira alteração feita aqui.</summary>
    public void RestoreAll()
    {
        foreach (var name in new[] { "AutoGameModeEnabled", "AllowAutoGameMode" })
            RegistryAccess.Restore(_store, GameModeId, RegistryHive.CurrentUser, GameBarPath, name);
        RegistryAccess.Restore(_store, HagsId, RegistryHive.LocalMachine, GraphicsPath, "HwSchMode");

        string? previous = _store.Get(PowerId + "|previous");
        if (previous is not null)
        {
            Shell.Run("powercfg.exe", "/setactive", previous);
            _store.Remove(PowerId + "|previous");
        }
    }
}
