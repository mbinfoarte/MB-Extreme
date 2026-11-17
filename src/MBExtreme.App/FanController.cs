using System.Windows.Threading;
using MBExtreme.Core.Fans;

namespace MBExtreme.UI;

/// <summary>Mantém o controle das ventoinhas enquanto o MB Extreme está aberto e devolve tudo ao automático ao fechar.</summary>
internal sealed class FanController
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private FanEngine? _engine;
    private bool _busy;

    public FanController() => _timer.Tick += async (_, _) => await StepAsync();

    public FanProfileStore Profiles { get; } = new();
    public FanControlService? Service { get; private set; }
    public IReadOnlyList<FanStepResult> Last { get; private set; } = Array.Empty<FanStepResult>();
    public string? Error { get; private set; }
    public bool Running => _timer.IsEnabled;
    public event EventHandler? Changed;

    /// <summary>Abre os sensores (uma vez) e começa a aplicar os perfis. Seguro chamar de novo.</summary>
    public async Task EnsureStartedAsync()
    {
        if (Service is null)
        {
            Service = await Task.Run(() => new FanControlService());
            if (!Service.IsAvailable)
            {
                Error = Service.OpenError ?? "Sensores indisponíveis (execute como administrador).";
                Service = null;
                return;
            }
            _engine = new FanEngine(Service, () => Profiles.All());
        }
        Error = null;
        if (!_timer.IsEnabled) _timer.Start();
    }

    /// <summary>Se algum perfil não é automático, liga o motor ao abrir o programa.</summary>
    public async Task StartIfConfiguredAsync()
    {
        if (Profiles.All().Any(p => p.Mode != "auto")) await EnsureStartedAsync();
    }

    private async Task StepAsync()
    {
        if (_busy || _engine is null) return;
        _busy = true;
        try
        {
            Last = await Task.Run(_engine.Step);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            Error = ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Tudo volta ao controle automático do firmware e o motor para.</summary>
    public void ReleaseAll()
    {
        _timer.Stop();
        _engine?.Stop();
        foreach (var p in Profiles.All().Where(p => p.Mode != "auto"))
        {
            p.Mode = "auto";
            Profiles.Save(p);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Shutdown()
    {
        _timer.Stop();
        try { _engine?.Stop(); }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException) { }
        Service?.Dispose();
        Service = null;
    }
}
