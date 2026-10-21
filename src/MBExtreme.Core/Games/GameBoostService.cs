using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MBExtreme.Core.Memory;
using MBExtreme.Core.Tweaks;
using MBExtreme.Core.Windows;

namespace MBExtreme.Core.Games;

public sealed record BoostOptions(
    IReadOnlyList<string> CloseApps,
    bool SwitchPowerPlan,
    bool CleanMemory,
    IReadOnlyList<string> MemoryAllowlist);

public sealed record BoostReport(IReadOnlyList<string> Closed, string? PowerPlan, bool PriorityRaised, long MemoryFreedBytes);

/// <summary>
/// Modo Jogo do MB Extreme: quando um jogo abre, troca para o plano de energia de alto desempenho, pede para fechar programas pesados
/// que você escolheu (sem forçar), tira o jogo do modo de eficiência (EcoQoS), sobe a prioridade e, se quiser, libera RAM.
/// Ao fechar o jogo, tudo volta ao que era.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GameBoostService
{
    private const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(IntPtr process, int informationClass, ref ProcessPowerThrottlingState info, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessPowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    private string? _previousPlan;
    private int? _boostedPid;

    public bool IsActive { get; private set; }
    public string? ActiveGame { get; private set; }

    /// <summary>Processos de fundo que costumam pesar e que muita gente prefere fechar antes de jogar.</summary>
    public static IReadOnlyList<string> SuggestedCloseApps { get; } = new[]
    {
        "OneDrive", "ms-teams", "Teams", "Skype", "Dropbox", "GoogleDriveFS", "PhoneExperienceHost", "YourPhone", "WidgetService", "Widgets", "AdobeIPCBroker",
    };

    public BoostReport Activate(string gameName, int? gameProcessId, BoostOptions options)
    {
        if (IsActive) Deactivate();

        string? planName = null;
        if (options.SwitchPowerPlan)
        {
            string? current = PowerPlanTweak.FirstGuid(Shell.Run("powercfg.exe", "/getactivescheme").Output);
            string? target = FindPerformancePlan();
            if (current is not null && target is not null && !current.Equals(target, StringComparison.OrdinalIgnoreCase)
                && Shell.Run("powercfg.exe", "/setactive", target).ExitCode == 0)
            {
                _previousPlan = current;
                planName = "Alto desempenho";
            }
        }

        var closed = new List<string>();
        foreach (string name in options.CloseApps.Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var process in SafeProcesses(name))
            {
                try
                {
                    if (process.Id == Environment.ProcessId || process.Id == gameProcessId) continue;
                    if (process.CloseMainWindow() && process.WaitForExit(3000)) closed.Add(name);
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    // processo sem janela ou protegido: deixa como está
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        bool raised = false;
        if (gameProcessId is { } pid) raised = BoostProcess(pid);
        _boostedPid = raised ? gameProcessId : null;

        long freed = 0;
        if (options.CleanMemory)
        {
            try { freed = MemoryService.Clean(options.MemoryAllowlist.Append(gameName).ToList()).FreedBytes; }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
        }

        IsActive = true;
        ActiveGame = gameName;
        return new BoostReport(closed.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), planName, raised, freed);
    }

    public void Deactivate()
    {
        if (!IsActive) return;
        if (_previousPlan is not null) Shell.Run("powercfg.exe", "/setactive", _previousPlan);
        _previousPlan = null;

        if (_boostedPid is { } pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                process.PriorityClass = ProcessPriorityClass.Normal;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
            {
                // o jogo já fechou
            }
        }

        _boostedPid = null;
        IsActive = false;
        ActiveGame = null;
    }

    /// <summary>Plano "Desempenho Máximo" se existir (criado pelo ajuste de energia); senão o "Alto desempenho" padrão do Windows.</summary>
    private static string? FindPerformancePlan()
    {
        var list = GameSettingsService.ParsePlans(Shell.Run("powercfg.exe", "/list").Output);
        var ultimate = list.FirstOrDefault(p => p.Name.Contains("Ultimate", StringComparison.OrdinalIgnoreCase)
                                                 || p.Name.Contains("Máximo", StringComparison.OrdinalIgnoreCase)
                                                 || p.Name.Contains("Maximo", StringComparison.OrdinalIgnoreCase));
        if (ultimate is not null) return ultimate.Guid;
        var high = list.FirstOrDefault(p => p.Guid.Equals(HighPerformance, StringComparison.OrdinalIgnoreCase));
        return high?.Guid;
    }

    private static bool BoostProcess(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.PriorityClass = ProcessPriorityClass.High;

            // Tira o jogo do "modo de eficiência" (EcoQoS): ControlMask liga o controle, StateMask 0 = sem limitação.
            var state = new ProcessPowerThrottlingState { Version = 1, ControlMask = 1, StateMask = 0 };
            SetProcessInformation(process.Handle, 4, ref state, Marshal.SizeOf<ProcessPowerThrottlingState>());
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    private static Process[] SafeProcesses(string name)
    {
        try { return Process.GetProcessesByName(name); }
        catch (InvalidOperationException) { return Array.Empty<Process>(); }
    }
}
