using System.Windows;
using System.Windows.Threading;
using MBExtreme.Core.Memory;

namespace MBExtreme.UI;

/// <summary>Limpeza automática de memória: quando o uso passa do limite escolhido, libera memória sem mexer em jogos, na lista de permissões nem no programa em uso.</summary>
internal sealed class MemoryAutoCleaner
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private DateTime? _lastCleanUtc;
    private bool _cleaning;

    public MemoryAutoCleaner() => _timer.Tick += async (_, _) => await TickAsync();

    public bool Enabled => AppServices.Settings.MemoryAutoClean;

    public DateTime? LastClean => _lastCleanUtc?.ToLocalTime();

    public long LastFreedBytes { get; private set; }

    public int Cleanings { get; private set; }

    public event EventHandler? Changed;

    /// <summary>Liga ou desliga (e grava a escolha).</summary>
    public void SetEnabled(bool on)
    {
        AppServices.Settings.MemoryAutoClean = on;
        AppServices.Settings.Save();
        if (on) _timer.Start();
        else _timer.Stop();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetThreshold(int percent)
    {
        AppServices.Settings.MemoryAutoCleanThreshold = percent;
        AppServices.Settings.Save();
    }

    public void StartIfEnabled()
    {
        if (Enabled) _timer.Start();
    }

    public void Shutdown() => _timer.Stop();

    private async Task TickAsync()
    {
        if (_cleaning) return;
        var status = MemoryService.GetStatus();
        if (status.TotalBytes <= 0) return;

        double used = 100 - status.FreePercent;
        if (!MemoryAutoPolicy.ShouldClean(used, AppServices.Settings.MemoryAutoCleanThreshold, _lastCleanUtc, DateTime.UtcNow)) return;

        _cleaning = true;
        try
        {
            var protectedNames = new HashSet<string>(AppServices.Settings.MemoryAllowlist, StringComparer.OrdinalIgnoreCase);
            foreach (string game in AppServices.Settings.GameProcesses) protectedNames.Add(game);
            if (ForegroundProcess.Name() is { } foreground) protectedNames.Add(foreground);

            var result = await Task.Run(() => MemoryService.Clean(protectedNames));
            LastFreedBytes = result.FreedBytes;
            Cleanings++;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // não conseguiu limpar agora; tenta de novo depois do intervalo
        }
        finally
        {
            _lastCleanUtc = DateTime.UtcNow;
            _cleaning = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
