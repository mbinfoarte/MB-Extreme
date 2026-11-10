using System.Windows;
using MBExtreme.Core.Games;
using MBExtreme.Core.Settings;

namespace MBExtreme.UI;

/// <summary>Liga o Modo Jogo à mão ou sozinho (quando um jogo da lista abre) e avisa as páginas das mudanças.</summary>
internal sealed class BoostController
{
    private readonly GameBoostService _service = new();
    private readonly GameWatcher _watcher = new();
    private DateTime? _startedAt;
    private volatile string[] _games = Array.Empty<string>();

    public BoostController()
    {
        _watcher.GameStarted += (name, pid) =>
        {
            _startedAt = DateTime.Now;
            Application.Current?.Dispatcher.BeginInvoke(new Action(async () => await StartAsync(name, pid)));
        };
        _watcher.GameStopped += name =>
        {
            if (_startedAt is { } start) AppServices.GameLibrary.AddPlaytime(name, (DateTime.Now - start).TotalMinutes, DateTime.Now);
            _startedAt = null;
            Application.Current?.Dispatcher.BeginInvoke(new Action(async () => await StopAsync()));
        };
    }

    public AppSettings Settings => AppServices.Settings;
    public bool IsActive => _service.IsActive;
    public string? ActiveGame => _service.ActiveGame;
    public BoostReport? LastReport { get; private set; }
    public string? LastError { get; private set; }
    public event EventHandler? Changed;

    /// <summary>Atualiza a cópia da lista de jogos que o vigia usa (a lista original é alterada pela interface).</summary>
    public void RefreshGames() => _games = Settings.GameProcesses.ToArray();

    public void SetAuto(bool on)
    {
        Settings.BoostAuto = on;
        Settings.Save();
        RefreshGames();
        if (on) _watcher.Start(() => _games);
        else _watcher.Stop();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Aplica o boost agora, sem esperar um jogo abrir.</summary>
    public Task ActivateNowAsync() => StartAsync("manual", null);

    public Task DeactivateAsync() => StopAsync();

    private async Task StartAsync(string game, int? pid)
    {
        // os outros processos dos seus jogos (launcher, ajudantes) também ficam de fora
        var keep = Settings.BoostNeverClose.Concat(Settings.GameProcesses).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var options = new BoostOptions(Settings.BoostCloseApps.ToList(), Settings.BoostSwitchPlan, Settings.BoostCleanMemory, Settings.MemoryAllowlist.ToList(),
            Settings.BoostSmart, Settings.BoostPauseServices, keep);
        try
        {
            // powercfg, pedidos para fechar programas e limpeza de RAM levam alguns segundos: fora da thread da interface.
            LastReport = await Task.Run(() => _service.Activate(game, pid, options));
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task StopAsync()
    {
        try
        {
            await Task.Run(_service.Deactivate);
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Shutdown()
    {
        _watcher.Dispose();
        try { _service.Deactivate(); }
        catch (Exception) { /* fechando: o plano de energia volta no próximo uso do Windows */ }
    }
}
