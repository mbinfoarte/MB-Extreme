using System.Windows.Threading;
using MBExtreme.Core.Bottleneck;
using MBExtreme.Core.Memory;

namespace MBExtreme.UI;

/// <summary>Coleta uma medida por segundo enquanto você joga e entrega o relatório de gargalos ao parar.</summary>
internal sealed class BottleneckController
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<BottleneckSample> _samples = new();
    private readonly SessionHistory _history = new();
    private SystemSampler? _sampler;
    private string _game = "";
    private DateTime _startedAt;
    private bool _busy;

    public BottleneckController() => _timer.Tick += async (_, _) => await SampleAsync();

    public SessionHistory History => _history;
    public bool IsRecording { get; private set; }
    public string? CurrentLabel { get; private set; }
    public int SampleCount => _samples.Count;
    public string CurrentGame => _game;
    public event EventHandler? Changed;

    public void Start()
    {
        if (IsRecording) return;
        AppServices.Overlay.StartMonitoring();   // exige administrador
        _sampler = new SystemSampler();
        _samples.Clear();
        _game = "";
        _startedAt = DateTime.Now;
        IsRecording = true;
        CurrentLabel = null;
        _timer.Start();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Para a coleta, guarda a sessão no histórico local e devolve o relatório.</summary>
    public async Task<BottleneckReport?> StopAsync(string? settingsNote)
    {
        if (!IsRecording) return null;
        _timer.Stop();
        IsRecording = false;
        _sampler?.Dispose();
        _sampler = null;
        CurrentLabel = null;

        var report = BottleneckAnalyzer.Analyze(_game.Length == 0 ? "jogo" : _game, _samples.ToList(), AppServices.Settings.BottleneckTargetFps);
        if (report.Samples >= 30)
        {
            int tier = 2;
            long memory = 0;
            try
            {
                var hw = await AppServices.Hardware();
                tier = MBExtreme.Core.Games.GameAdvisor.Tier(hw);
                memory = hw.TotalMemoryBytes;
            }
            catch (Exception ex) when (ex is InvalidOperationException or AggregateException) { }

            _history.Add(new SessionRecord
            {
                Game = report.Game, Date = DateTime.Now, DurationSeconds = (int)report.Duration.TotalSeconds,
                AverageFps = report.AverageFps, Low1PercentFps = report.Low1PercentFps, Primary = BottleneckAnalyzer.Label(report.Primary),
                Tier = tier, Settings = string.IsNullOrWhiteSpace(settingsNote) ? null : settingsNote.Trim(),
            });

            if (AppServices.Settings.ShareAnonymousStats && memory > 0)
                LastAnonymous = AnonymousSessionStats.From(report.Game, tier, memory, report);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return report;
    }

    /// <summary>Último resumo anônimo pronto para envio (só existe se o usuário autorizou).</summary>
    public AnonymousSessionStats? LastAnonymous { get; set; }

    private async Task SampleAsync()
    {
        if (_busy || !IsRecording) return;
        _busy = true;
        try
        {
            var fps = AppServices.Overlay.Last;
            if (fps is null) { CurrentLabel = null; return; }
            if (_game.Length == 0 && AppServices.Overlay.LastProcess is { } name) _game = name;

            var sensors = await AppServices.Sensors;
            var s = await Task.Run(sensors.Read);
            var sampler = _sampler;
            double? core = null, queue = null;
            if (sampler is not null) (core, queue) = await Task.Run(() => (sampler.MaxCoreLoad(), sampler.DiskQueue()));
            var ram = MemoryService.GetStatus();

            var sample = new BottleneckSample(fps.Fps, fps.FrameTimeMs, fps.Low1PercentFps, s.CpuLoad, core ?? s.CpuLoad, s.CpuTemperature,
                s.GpuLoad, s.GpuTemperature, 100 - ram.FreePercent, queue);
            _samples.Add(sample);
            CurrentLabel = BottleneckAnalyzer.Label(BottleneckAnalyzer.Classify(sample));
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is InvalidOperationException or AggregateException)
        {
            // uma medida perdida não invalida a sessão
        }
        finally
        {
            _busy = false;
        }
    }

    public void Shutdown()
    {
        _timer.Stop();
        _sampler?.Dispose();
    }
}
