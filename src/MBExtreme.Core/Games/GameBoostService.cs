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
    IReadOnlyList<string> MemoryAllowlist,
    bool Smart = false,
    bool PauseServices = false,
    IReadOnlyList<string>? NeverClose = null);

public sealed record BoostReport(IReadOnlyList<string> Closed, string? PowerPlan, bool PriorityRaised, long MemoryFreedBytes,
    int Lowered = 0, IReadOnlyList<string>? ServicesPaused = null)
{
    public string Summary => GameModeRules.Summary(Closed.Count, Lowered, ServicesPaused?.Count ?? 0, MemoryFreedBytes);
}

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
    // Modo Jogo 2.0: o que mudamos, para devolver ao sair do jogo
    private readonly List<(int Pid, DateTime Start, ProcessPriorityClass Priority)> _lowered = new();
    private readonly List<string> _stoppedServices = new();
    private readonly List<(string Path, string Args)> _reopen = new();

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

        int lowered = 0;
        var paused = new List<string>();
        if (options.Smart)
        {
            var keep = new HashSet<string>(options.NeverClose ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase) { gameName };
            closed.AddRange(CloseBackground(keep, gameProcessId));
            lowered = LowerBackground(keep, gameProcessId);
        }
        if (options.PauseServices) paused.AddRange(PauseServices());

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
        return new BoostReport(closed.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), planName, raised, freed, lowered, paused);
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
        RestoreBackground();
        IsActive = false;
        ActiveGame = null;
    }

    // ------------------------------------------------------------ Modo Jogo 2.0

    /// <summary>Fecha os ajudantes de fundo da lista segura (atualizadores, widgets, sincronização) e guarda quais reabrir depois.</summary>
    private List<string> CloseBackground(IReadOnlySet<string> keep, int? gamePid)
    {
        var closed = new List<string>();
        foreach (string name in GameModeRules.AutoClose)
        {
            if (!GameModeRules.CanAutoClose(name, keep)) continue;
            foreach (var process in SafeProcesses(name))
            {
                try
                {
                    if (process.Id == Environment.ProcessId || process.Id == gamePid || process.SessionId != Process.GetCurrentProcess().SessionId) continue;
                    string? path = null;
                    try { path = process.MainModule?.FileName; } catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
                    bool done;
                    if (name.Equals("OneDrive", StringComparison.OrdinalIgnoreCase) && path is not null)
                        done = Shell.Run(path, "/shutdown").ExitCode == 0 || process.WaitForExit(4000);
                    else
                    {
                        done = process.CloseMainWindow() && process.WaitForExit(3000);
                        if (!done) { process.Kill(); done = process.WaitForExit(3000); }
                    }
                    if (!done) continue;
                    closed.Add(name);
                    if (path is not null && GameModeRules.ReopenAfter.Contains(name, StringComparer.OrdinalIgnoreCase) && _reopen.All(r => r.Path != path))
                        _reopen.Add((path, name.Equals("OneDrive", StringComparison.OrdinalIgnoreCase) ? "/background" : ""));
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException) { }
                finally { process.Dispose(); }
            }
        }
        return closed;
    }

    /// <summary>Abaixa a prioridade e liga o modo de eficiência nos programas de fundo do usuário (sem fechar nada). Guarda o valor de antes.</summary>
    private int LowerBackground(IReadOnlySet<string> keep, int? gamePid)
    {
        int mySession = Process.GetCurrentProcess().SessionId;
        int count = 0;
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                bool gameOrSelf = process.Id == Environment.ProcessId || process.Id == gamePid;
                if (!GameModeRules.CanDeprioritize(process.ProcessName, process.SessionId, mySession, gameOrSelf, keep)) continue;
                var before = process.PriorityClass;
                if (before is not (ProcessPriorityClass.Normal or ProcessPriorityClass.AboveNormal)) continue;
                var start = process.StartTime;
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
                var eco = new ProcessPowerThrottlingState { Version = 1, ControlMask = 1, StateMask = 1 };
                SetProcessInformation(process.Handle, 4, ref eco, Marshal.SizeOf<ProcessPowerThrottlingState>());
                _lowered.Add((process.Id, start, before));
                count++;
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException) { /* protegido ou já fechou */ }
            finally { process.Dispose(); }
        }
        return count;
    }

    /// <summary>Para as tarefas pesadas do Windows que estiverem rodando (precisa de administrador; sem ele, não faz nada).</summary>
    private List<string> PauseServices()
    {
        var paused = new List<string>();
        foreach (var (service, title) in GameModeRules.PausableServices)
        {
            var query = Shell.Run("sc.exe", "query", service);
            if (query.ExitCode != 0 || !query.Output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase)) continue;
            if (Shell.Run("sc.exe", "stop", service).ExitCode == 0) { _stoppedServices.Add(service); paused.Add(title); }
        }
        return paused;
    }

    /// <summary>Devolve tudo: prioridades, modo de eficiência, tarefas do Windows e programas de sincronização.</summary>
    private void RestoreBackground()
    {
        foreach (var (pid, start, priority) in _lowered)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                if (process.StartTime != start) continue;   // o número foi reaproveitado por outro programa
                process.PriorityClass = priority;
                var normal = new ProcessPowerThrottlingState { Version = 1, ControlMask = 0, StateMask = 0 };
                SetProcessInformation(process.Handle, 4, ref normal, Marshal.SizeOf<ProcessPowerThrottlingState>());
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception) { }
        }
        _lowered.Clear();
        foreach (var service in _stoppedServices) Shell.Run("sc.exe", "start", service);
        _stoppedServices.Clear();
        foreach (var (path, args) in _reopen)
        {
            try { if (File.Exists(path)) Process.Start(new ProcessStartInfo(path, args) { UseShellExecute = true }); }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
        }
        _reopen.Clear();
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
