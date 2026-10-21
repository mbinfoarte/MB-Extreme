using System.Windows;
using MBExtreme.Core.Games;
using MBExtreme.Core.Settings;

namespace MBExtreme.UI;

/// <summary>Liga o Modo Jogo à mão ou sozinho (quando um jogo da lista abre) e avisa as páginas das mudanças.</summary>
internal sealed class BoostController
{
    private readonly GameBoostService _service = new();
    private readonly GameWatcher _watcher = new();

    public BoostController()
    {
        _watcher.GameStarted += (name, pid) => Application.Current?.Dispatcher.BeginInvoke(new Action(() => Start(name, pid)));
        _watcher.GameStopped += _ => Application.Current?.Dispatcher.BeginInvoke(new Action(Stop));
    }

    public AppSettings Settings { get; } = AppSettings.Load();
    public bool IsActive => _service.IsActive;
    public string? ActiveGame => _service.ActiveGame;
    public BoostReport? LastReport { get; private set; }
    public event EventHandler? Changed;

    public void SetAuto(bool on)
    {
        Settings.BoostAuto = on;
        Settings.Save();
        if (on) _watcher.Start(() => Settings.GameProcesses.ToList());
        else _watcher.Stop();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Aplica o boost agora, sem esperar um jogo abrir.</summary>
    public void ActivateNow() => Start("manual", null);

    public void Deactivate() => Stop();

    private void Start(string game, int? pid)
    {
        LastReport = _service.Activate(game, pid, new BoostOptions(
            Settings.BoostCloseApps.ToList(), Settings.BoostSwitchPlan, Settings.BoostCleanMemory, Settings.MemoryAllowlist.ToList()));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Stop()
    {
        _service.Deactivate();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Shutdown()
    {
        _watcher.Dispose();
        _service.Deactivate();
    }
}
