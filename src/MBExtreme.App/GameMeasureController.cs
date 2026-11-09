using System.IO;
using System.Diagnostics;
using System.Windows.Threading;
using MBExtreme.Core.Games;

namespace MBExtreme.UI;

/// <summary>
/// Mede o FPS de um jogo aberto pela biblioteca (leitura de eventos do Windows, sem injetar nada no jogo) e grava a sessão ao fechar.
/// Usa o mesmo monitor do painel de FPS, por isso precisa de administrador.
/// </summary>
internal sealed class GameMeasureController
{
    private const int GiveUpAfterSeconds = 180;
    private const int ClosedAfterSeconds = 5;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private SessionRecorder _recorder = new();
    private string _process = "";
    private bool _optimized, _seen, _startedMonitor;
    private int _waited, _missing;
    private DateTime _started;

    public GameMeasureController() => _timer.Tick += (_, _) => Tick();

    public bool IsMeasuring => GameId is not null;
    public string? GameId { get; private set; }
    public string GameName { get; private set; } = "";
    public string Status { get; private set; } = "";

    /// <summary>Mudou o andamento (texto para mostrar).</summary>
    public event EventHandler? Changed;

    /// <summary>A medição terminou (sessão gravada ou null se foi curta/cancelada).</summary>
    public event Action<string, PerfSession?>? Finished;

    /// <summary>Começa a vigiar o jogo. Devolve o texto do problema, ou null se começou.</summary>
    public string? Start(InstalledGame game, bool optimized)
    {
        if (IsMeasuring) return $"Já estou medindo {GameName}. Pare essa medição primeiro.";
        if (string.IsNullOrWhiteSpace(game.ExeName)) return "Não sei qual é o executável deste jogo; a medição precisa dele.";
        try
        {
            _startedMonitor = !AppServices.Overlay.IsMonitoring;
            AppServices.Overlay.StartMonitoring();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return "Não foi possível medir o FPS (execute o MB Extreme como administrador): " + ex.Message;
        }

        _recorder = new SessionRecorder();
        _process = Path.GetFileNameWithoutExtension(game.ExeName);
        _optimized = optimized;
        _seen = false;
        _waited = _missing = 0;
        _started = DateTime.Now;
        GameId = game.Id;
        GameName = game.Name;
        Set($"Aguardando {game.Name} abrir para medir o FPS...");
        _timer.Start();
        return null;
    }

    public void Stop() => Finish(save: true);

    private void Tick()
    {
        var reading = AppServices.Overlay.Last;
        string? proc = AppServices.Overlay.LastProcess;
        if (reading is not null && string.Equals(proc, _process, StringComparison.OrdinalIgnoreCase))
        {
            _seen = true;
            _missing = 0;
            _recorder.Add(reading.Fps, reading.Low1PercentFps, reading.FrameTimeMs);
            Set($"Medindo {GameName}: {reading.Fps:0} FPS · {_recorder.Count} s gravados" + (_recorder.Count < SessionRecorder.MinimumSeconds ? $" (mínimo {SessionRecorder.MinimumSeconds} s)" : ""));
            return;
        }

        if (!_seen)
        {
            if (++_waited > GiveUpAfterSeconds)
            {
                Finish(save: false);
                Set("Não detectei quadros do jogo. Ele pode usar tela cheia exclusiva ou uma API não suportada pelo monitor.");
            }
            return;
        }

        if (Process.GetProcessesByName(_process).Length == 0 && ++_missing >= ClosedAfterSeconds) Finish(save: true);
    }

    private void Finish(bool save)
    {
        if (GameId is not { } id) return;
        _timer.Stop();
        var session = save ? _recorder.Finish(_optimized, _started) : null;
        if (session is not null) AppServices.Perf.Add(id, session);
        if (_startedMonitor && !AppServices.Overlay.OverlayVisible) AppServices.Overlay.StopMonitoring();
        GameId = null;
        Set(save
            ? session is null ? $"Medição curta demais (menos de {SessionRecorder.MinimumSeconds} s); não foi guardada." : $"Medição guardada: {session.AvgFps:0} FPS em média, {session.Seconds} s."
            : "Medição cancelada.");
        Finished?.Invoke(id, session);
    }

    private void Set(string text)
    {
        Status = text;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
