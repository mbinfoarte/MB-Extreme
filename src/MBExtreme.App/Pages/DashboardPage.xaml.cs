using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MBExtreme.Core.Memory;
using MBExtreme.Core.Network;
using MBExtreme.Core.Sensors;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>Painel: sensores, memória, internet e ping em tempo real, com atalho para a limpeza.</summary>
public partial class DashboardPage : UserControl
{
    private const int HistoryLength = 60;
    private const double BytesPerGb = 1024.0 * 1024 * 1024;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<double> _downHistory = new();
    private readonly List<double> _upHistory = new();
    private readonly LiveChart _netChart = new(HistoryLength, Color.FromRgb(0xFF, 0xC4, 0x00), Color.FromRgb(0xE8, 0xE8, 0xEC));
    private readonly PingWindow _pings = new(20);
    private ActiveAdapter? _adapter;
    private double? _localPing;
    private readonly List<double> _cpuHistory = new();
    private int _tick;
    private bool _busy;
    private double? _lastPing;

    public DashboardPage()
    {
        InitializeComponent();
        CpuCard.MouseLeftButtonUp += (_, _) => MainWindow.Current?.Go("cpu-detail");
        GpuCard.MouseLeftButtonUp += (_, _) => MainWindow.Current?.Go("gpu-detail");
        RamCard.MouseLeftButtonUp += (_, _) => MainWindow.Current?.Go("ram-detail");
        NetCard.MouseLeftButtonUp += (_, _) => MainWindow.Current?.Go("netanalyzer");
        _netChart.FormatMax = v => Rate(v * 125_000);
        NetChartHost.Child = _netChart;
        _timer.Tick += async (_, _) => await UpdateAsync();
        Loaded += async (_, _) =>
        {
            _timer.Start();
            await UpdateAsync();
        };
        Unloaded += (_, _) => _timer.Stop();
    }

    private static string Rate(double bytesPerSecond)
    {
        double bits = bytesPerSecond * 8;
        return bits >= 1_000_000 ? $"{bits / 1_000_000:0.0} Mb/s" : $"{bits / 1_000:0} kb/s";
    }

    private async Task UpdateAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var memory = MemoryService.GetStatus();
            UpdateMemory(memory);

            var network = AppServices.Network.Sample();
            UpdateNetwork(network);

            if (_tick % 15 == 0) _adapter = await Task.Run(NetworkStatus.Active);
            if (_tick % 3 == 0)
            {
                _lastPing = await NetworkMonitor.PingAsync();
                _pings.Add(_lastPing);
                _localPing = _adapter?.Gateway is { } gateway ? await NetAnalyzer.PingAsync(gateway, 1000) : null;
            }
            _tick++;
            UpdatePing(_lastPing);
            UpdateNetworkInfo();

            var sensors = await AppServices.Sensors;
            var snapshot = await Task.Run(sensors.Read);
            UpdateSensors(snapshot);
        }
        catch (Exception ex) when (ex is InvalidOperationException or AggregateException)
        {
            HealthText.Text = "Não foi possível ler todos os sensores: " + ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private void UpdateMemory(MemoryStatus memory)
    {
        double used = 100 - memory.FreePercent;
        RamGauge.Value = memory.TotalBytes > 0 ? used : null;
        RamDetail.Text = $"{memory.UsedBytes / BytesPerGb:0.0} GB de {memory.TotalBytes / BytesPerGb:0.#} GB";

        (string text, string brush) = memory.Level switch
        {
            MemoryLevel.Good => ("Seu PC está em boas condições.", "OkBrush"),
            MemoryLevel.NeedsCleaning => ("A memória está ficando cheia. Vale a pena limpar.", "WarnBrush"),
            _ => ("A memória está quase cheia. Faça uma limpeza.", "BadBrush"),
        };
        HealthText.Text = text;
        HealthText.Foreground = (Brush)FindResource(brush);
    }

    private void UpdateNetwork(NetworkSample sample)
    {
        DownloadText.Text = Rate(sample.DownloadBytesPerSecond);
        UploadText.Text = Rate(sample.UploadBytesPerSecond);
        Push(_downHistory, sample.DownloadBytesPerSecond * 8 / 1_000_000);
        Push(_upHistory, sample.UploadBytesPerSecond * 8 / 1_000_000);
        _netChart.Update(_downHistory.ToList(), _upHistory.ToList());
    }

    private void UpdatePing(double? ping)
    {
        PingGauge.Value = ping;
        PingDetail.Text = ping is null ? "Sem resposta de 1.1.1.1" : "até 1.1.1.1";
    }

    private void UpdateNetworkInfo()
    {
        PingStat.Text = _lastPing is double p ? $"{p:0} ms" : "sem resposta";
        JitterStat.Text = _pings.Average is double avg
            ? $"média {avg:0} ms · oscilação {_pings.Jitter ?? 0:0.0} ms · perda {_pings.LossPercent:0}%"
            : "—";
        LocalPingStat.Text = _adapter?.Gateway is null
            ? "roteador não identificado"
            : _localPing is double lp ? $"{lp:0} ms ({_adapter.Gateway})" : $"sem resposta ({_adapter.Gateway})";
        DnsStat.Text = _adapter is null ? "—" : NetworkStatus.DescribeDns(_adapter.Dns, _adapter.Gateway);
    }

    private async void AnalyzeRoute_Click(object sender, RoutedEventArgs e)
    {
        RouteButton.IsEnabled = false;
        RouteStat.Text = "Analisando a rota (cerca de 10 segundos)...";
        using var cts = new CancellationTokenSource();
        IReadOnlyList<HopStats>? last = null;
        int rounds = 0;
        try
        {
            await NetAnalyzer.TraceAsync("1.1.1.1", 20, hops =>
            {
                last = hops;
                if (++rounds >= 4) cts.Cancel();
            }, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // as rodadas pedidas terminaram
        }
        catch (Exception ex) when (ex is System.Net.NetworkInformation.PingException or InvalidOperationException)
        {
            RouteStat.Text = "Não foi possível analisar: " + ex.Message;
            RouteButton.IsEnabled = true;
            return;
        }

        var report = last is null ? null : RouteSummary.Summarize(last);
        RouteStat.Text = report?.Text ?? "Sem resultado.";
        RouteStat.Foreground = (Brush)FindResource(report is { Healthy: true } ? "OkBrush" : "WarnBrush");
        RouteButton.IsEnabled = true;
    }

    private void UpdateSensors(SensorSnapshot s)
    {
        SensorHint.Visibility = s.Message is null ? Visibility.Collapsed : Visibility.Visible;
        SensorHintText.Text = s.Message ?? "";

        CpuGauge.Value = s.CpuTemperature;
        CpuDetail.Text = Join(
            s.CpuLoad is double cl ? $"Uso {cl:0}%" : null,
            s.CpuClockMhz is double cc ? $"{cc / 1000:0.0} GHz" : null,
            s.CpuPowerWatts is double cp ? $"{cp:0} W" : null);

        GpuGauge.Value = s.GpuTemperature;
        GpuDetail.Text = Join(
            s.GpuLoad is double gl ? $"Uso {gl:0}%" : null,
            s.GpuClockMhz is double gc ? $"{gc:0} MHz" : null,
            s.GpuPowerWatts is double gp ? $"{gp:0} W" : null);

        CpuLoadText.Text = s.CpuLoad is double load ? $"{load:0}%" : "—";
        if (s.CpuLoad is double value)
        {
            Push(_cpuHistory, value);
            CpuChart.Values = _cpuHistory.ToList();
        }

        FansText.Text = s.Fans.Count == 0
            ? "Nenhuma ventoinha lida."
            : string.Join("\n", s.Fans.Take(6).Select(f => $"{f.Name}: {f.Rpm:0} rpm"));

        double? total = s.CpuPowerWatts is null && s.GpuPowerWatts is null
            ? null
            : (s.CpuPowerWatts ?? 0) + (s.GpuPowerWatts ?? 0);
        PowerTotalText.Text = total is double w ? $"{w:0} W" : "—";
        PowerDetail.Text = Join(
            s.CpuPowerWatts is double a ? $"CPU {a:0} W" : null,
            s.GpuPowerWatts is double b ? $"GPU {b:0} W" : null);
    }

    private static string Join(params string?[] parts) =>
        string.Join("  ·  ", parts.Where(p => !string.IsNullOrEmpty(p)));

    private static void Push(List<double> history, double value)
    {
        history.Add(value);
        if (history.Count > HistoryLength) history.RemoveAt(0);
    }

    private void ScanButton_Click(object sender, RoutedEventArgs e) => MainWindow.Current?.Go("cleanup", autoStart: true);

    private void InstallSensorDriver_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "Para ler a temperatura da CPU, o MB Extreme precisa do driver PawnIO. Vou instalá-lo pelo winget (instalador oficial do Windows). Continuar?\n\nDepois de instalar, feche e abra o MB Extreme de novo.",
            "Ativar sensores da CPU", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            Process.Start(new ProcessStartInfo("winget.exe",
                "install --id namazso.PawnIO -e --accept-source-agreements --accept-package-agreements")
            { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            MessageBox.Show("Não encontrei o winget neste Windows. Instale o PawnIO manualmente: github.com/namazso/PawnIO.Setup",
                "Ativar sensores da CPU", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
