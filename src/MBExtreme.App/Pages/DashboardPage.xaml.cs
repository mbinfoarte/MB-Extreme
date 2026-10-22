using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MBExtreme.Core.Memory;
using MBExtreme.Core.Network;
using MBExtreme.Core.Sensors;
using MBExtreme.Core.Startup;
using MBExtreme.Core.Windows;
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
    private int _securityProblems;
    private SecurityStatus? _securityStatus;
    private int _startupCount;
    private double? _cpuTemperature;
    private bool _securityLoading;
    private double _memoryUsed;
    private string _attentionKey = "";
    private readonly List<double> _cpuHistory = new();
    private readonly List<double> _gpuHistory = new();
    private readonly List<double> _ramHistory = new();
    private readonly List<double> _pingHistory = new();
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
        UpdateRamAuto();
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
            if (_tick % 10 == 1) await RefreshAttentionAsync();

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
        _memoryUsed = used;
        if (memory.TotalBytes > 0)
        {
            Push(_ramHistory, used);
            RamChart.Values = _ramHistory.ToList();
        }
    }

    private async Task RefreshAttentionAsync()
    {
        try
        {
            _startupCount = await Task.Run(() => StartupService.List().Count(e => e.Enabled));
            var drives = await Task.Run(() => DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady && d.TotalSize > 0)
                .Select(d => (Name: d.Name.TrimEnd('\\'), Used: 100.0 * (d.TotalSize - d.AvailableFreeSpace) / d.TotalSize)).ToList());
            var worst = drives.OrderByDescending(d => d.Used).FirstOrDefault();

            if (_securityProblems < 0 || (_tick % 300 == 1 && !_securityLoading) || !_securityChecked)
            {
                _securityChecked = true;
                _securityLoading = true;
                _ = Task.Run(() =>
                {
                    try
                    {
                        var status = SecurityService.Read();
                        _securityStatus = status;
                        _securityProblems = SecurityService.Evaluate(status, DateTime.Now).Count(i => i.Level == SecurityLevel.Bad);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException) { }
                    finally { _securityLoading = false; }
                });
            }

            var items = AttentionAdvisor.Evaluate(new AttentionFacts(_memoryUsed, _startupCount, worst.Name is null ? null : worst.Used, worst.Name,
                _securityProblems, _cpuTemperature, AppServices.AutoMemory.Enabled));
            ShowAttention(items);
            UpdateRamAuto();
        }
        catch (Exception ex) when (ex is InvalidOperationException or AggregateException or IOException)
        {
            // sem resumo desta vez
        }
    }

    private bool _securityChecked;

    private void ShowAttention(IReadOnlyList<AttentionItem> items)
    {
        HealthText.Text = AttentionAdvisor.Headline(items);
        HealthText.Foreground = (Brush)FindResource(items.Count == 0 ? "OkBrush" : items.Any(i => i.Urgent) ? "BadBrush" : "WarnBrush");

        string key = string.Join("|", items.Select(i => i.Title + i.Detail));
        if (key == _attentionKey) return;
        _attentionKey = key;

        AttentionPanel.Children.Clear();
        if (items.Count == 0) return;

        // cabeçalho: corrigir tudo o que dá para corrigir sozinho, ou reparar o Windows
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        if (items.Any(i => i.FixKind is "security" or "memory"))
        {
            var all = new Button { Content = "Corrigir tudo", Style = (Style)FindResource("PrimaryButton"), Padding = new Thickness(18, 7, 18, 7) };
            all.Click += async (_, _) => await FixAllAsync(items);
            header.Children.Add(all);
        }
        var repair = new Button { Content = "Reparar o Windows (SFC e DISM)", Style = (Style)FindResource("GhostButton"), Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(8, 0, 0, 0) };
        repair.Click += (_, _) => MainWindow.Current?.Go("repair");
        header.Children.Add(repair);
        AttentionPanel.Children.Add(header);

        foreach (var item in items)
        {
            var current = item;
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = item.Title, FontWeight = FontWeights.SemiBold, FontSize = 14 });
            text.Children.Add(new TextBlock { Text = item.Detail, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("MutedBrush") });
            grid.Children.Add(text);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (item.FixKind.Length > 0)
            {
                var fix = new Button { Content = item.FixText, Style = (Style)FindResource("PrimaryButton"), Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
                fix.Click += async (_, _) => await FixAsync(current);
                buttons.Children.Add(fix);
            }
            var details = new Button { Content = item.ActionText, Style = (Style)FindResource("GhostButton"), Padding = new Thickness(14, 6, 14, 6) };
            details.Click += (_, _) => MainWindow.Current?.Go(current.TargetPage);
            buttons.Children.Add(details);
            Grid.SetColumn(buttons, 1);
            grid.Children.Add(buttons);

            AttentionPanel.Children.Add(new Border
            {
                Style = (Style)FindResource("Card"),
                Padding = new Thickness(18, 12, 18, 12),
                Margin = new Thickness(0, 0, 0, 8),
                BorderBrush = (Brush)FindResource(item.Urgent ? "BadBrush" : "WarnBrush"),
                Child = grid,
            });
        }
    }

    private async Task FixAsync(AttentionItem item)
    {
        switch (item.FixKind)
        {
            case "security":
                await FixSecurityAsync(confirm: true);
                break;
            case "memory":
                AppServices.AutoMemory.SetEnabled(true);
                UpdateRamAuto();
                _attentionKey = "";
                await RefreshAttentionAsync();
                MessageBox.Show("Limpeza automática da memória ligada. Ela libera memória de programas em segundo plano quando o uso passar do limite, sem mexer no programa em uso nem nos jogos da sua lista.",
                    "Memória", MessageBoxButton.OK, MessageBoxImage.Information);
                break;
            case "cleanup":
                MainWindow.Current?.Go("cleanup", autoStart: true);
                break;
        }
    }

    private async Task FixAllAsync(IReadOnlyList<AttentionItem> items)
    {
        var plan = _securityStatus is null ? Array.Empty<SecurityFix>() : SecurityFixer.Plan(_securityStatus);
        bool memory = items.Any(i => i.FixKind == "memory");
        var lines = plan.Select(p => "• " + p.Title).ToList();
        if (memory) lines.Add("• Ligar a limpeza automática da memória");
        if (lines.Count == 0) { MainWindow.Current?.Go("security"); return; }

        if (MessageBox.Show("O MB Extreme vai fazer o seguinte:\n\n" + string.Join("\n", lines) + "\n\nContinuar?", "Corrigir tudo",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        if (memory) AppServices.AutoMemory.SetEnabled(true);
        if (plan.Count > 0) await FixSecurityAsync(confirm: false);
        else await RefreshAfterFixAsync();
    }

    private async Task FixSecurityAsync(bool confirm)
    {
        var plan = _securityStatus is null ? Array.Empty<SecurityFix>() : SecurityFixer.Plan(_securityStatus);
        if (plan.Count == 0) { MainWindow.Current?.Go("security"); return; }

        if (confirm && MessageBox.Show("O MB Extreme vai fazer o seguinte:\n\n" + string.Join("\n", plan.Select(p => $"• {p.Title}\n   {p.Detail}")) + "\n\nContinuar?",
                "Corrigir segurança", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        HealthText.Text = "Corrigindo...";
        var results = await Task.Run(() => SecurityFixer.Apply(plan));
        await RefreshAfterFixAsync();

        string summary = string.Join("\n", results.Select(r => $"{(r.Ok ? "✔" : "✖")} {r.Fix.Title}: {r.Message}"));
        MessageBox.Show(summary, "Resultado", MessageBoxButton.OK, results.All(r => r.Ok) ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private async Task RefreshAfterFixAsync()
    {
        try
        {
            var status = await Task.Run(SecurityService.Read);
            _securityStatus = status;
            _securityProblems = SecurityService.Evaluate(status, DateTime.Now).Count(i => i.Level == SecurityLevel.Bad);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception) { }

        _attentionKey = "";
        await RefreshAttentionAsync();
        UpdateRamAuto();
    }

    // ---- limpeza automática da memória, direto no cartão

    private void RamAuto_Click(object sender, RoutedEventArgs e)
    {
        AppServices.AutoMemory.SetEnabled(!AppServices.AutoMemory.Enabled);
        UpdateRamAuto();
        _attentionKey = "";
        _ = RefreshAttentionAsync();
    }

    private void UpdateRamAuto()
    {
        bool on = AppServices.AutoMemory.Enabled;
        RamAutoButton.Style = (Style)FindResource(on ? "PrimaryButton" : "GhostButton");
        RamAutoButton.ToolTip = on
            ? $"Limpeza automática ligada (acima de {AppServices.Settings.MemoryAutoCleanThreshold}%). Clique para desligar."
            : "Clique para ligar a limpeza automática da memória.";
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
        if (ping is double ms)
        {
            Push(_pingHistory, ms);
            PingChart.Maximum = Math.Max(100, _pingHistory.Max() * 1.2);
            PingChart.Values = _pingHistory.ToList();
        }
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

        _cpuTemperature = s.CpuTemperature;
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

        if (s.CpuLoad is double value)
        {
            Push(_cpuHistory, value);
            CpuUseChart.Values = _cpuHistory.ToList();
        }
        if (s.GpuLoad is double gpuValue)
        {
            Push(_gpuHistory, gpuValue);
            GpuUseChart.Values = _gpuHistory.ToList();
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
