using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MBExtreme.Core.Hardware;
using MBExtreme.Core.Sensors;
using MBExtreme.Core.Stress;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>Teste de estresse de CPU e memória com limite de temperatura e detecção de erros.</summary>
public sealed class StressPage : ToolPageBase
{
    private static readonly int[] Minutes = { 1, 5, 10, 30 };
    private static readonly int[] Limits = { 80, 90, 95 };

    private readonly CheckBox _cpu = new() { Content = "CPU", IsChecked = true, Margin = new Thickness(0, 0, 18, 0) };
    private readonly CheckBox _memory = new() { Content = "Memória", IsChecked = true };
    private readonly ArcGauge _cpuTemp = Gauge("°C", 100, 80, 92);
    private readonly ArcGauge _cpuLoad = Gauge("%", 100, 101, 102);
    private readonly ArcGauge _gpuTemp = Gauge("°C", 100, 80, 92);
    private readonly TextBlock _live = new() { FontSize = 14, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly Button _start;
    private readonly Button _stop;
    private readonly Button _perCore;
    private readonly StackPanel _coreResults = new();
    private static readonly int[] SecondsPerCore = { 30, 60, 120 };
    private int _secondsIndex = 1;
    private int _minutesIndex = 1;
    private int _limitIndex = 1;
    private bool _running;

    public StressPage() : base("Teste de estresse",
        "Coloca CPU e memória sob carga máxima para ver se o PC aguenta (temperatura e estabilidade). Para sozinho ao chegar na duração ou na temperatura limite. Em notebooks, use com o carregador e fique de olho no calor.")
    {
        _start = AddButton("Iniciar teste", async (_, _) => await RunAsync());
        _stop = AddButton("Parar", (_, _) => Cts?.Cancel(), false);
        _stop.IsEnabled = false;
        _perCore = AddButton("Teste por núcleo (Curve Optimizer)", async (_, _) => await RunPerCoreAsync(), false);

        _cpu.Foreground = Brush("TextBrush");
        _memory.Foreground = Brush("TextBrush");
        var what = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        what.Children.Add(_cpu);
        what.Children.Add(_memory);

        Body.Children.Add(Heading("O que testar"));
        Body.Children.Add(what);
        Body.Children.Add(Heading("Duração"));
        Body.Children.Add(Choice(Minutes.Select(m => m + " min").ToList(), _minutesIndex, i => _minutesIndex = i));
        Body.Children.Add(Heading("Parar se a temperatura chegar a"));
        Body.Children.Add(Choice(Limits.Select(l => l + " °C").ToList(), _limitIndex, i => _limitIndex = i));
        Body.Children.Add(Heading("Teste por núcleo: tempo em cada núcleo"));
        Body.Children.Add(Choice(SecondsPerCore.Select(x => x + " s").ToList(), _secondsIndex, i => _secondsIndex = i));
        Body.Children.Add(Muted("Roda um núcleo de cada vez com cálculos conferidos. Serve para ajustar Curve Optimizer/undervolt: o núcleo que der erro está instável. Cada núcleo físico é testado uma vez."));
        Body.Children.Add(RecordOption());

        var gauges = new UniformGrid { Columns = 3, Margin = new Thickness(0, 18, 0, 0) };
        gauges.Children.Add(Captioned("Temperatura da CPU", _cpuTemp));
        gauges.Children.Add(Captioned("Uso da CPU", _cpuLoad));
        gauges.Children.Add(Captioned("Temperatura da GPU", _gpuTemp));
        Body.Children.Add(gauges);
        Body.Children.Add(_live);
        Body.Children.Add(_coreResults);
    }

    private static ArcGauge Gauge(string unit, double max, double warn, double bad) =>
        new() { Unit = unit, Maximum = max, WarnAt = warn, BadAt = bad, Width = 150, Height = 150 };

    private FrameworkElement Captioned(string caption, ArcGauge gauge)
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(gauge);
        panel.Children.Add(new TextBlock { Text = caption, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brush("MutedBrush") });
        return panel;
    }

    private async Task RunAsync()
    {
        if (_running) return;
        if (_cpu.IsChecked != true && _memory.IsChecked != true)
        {
            SetStatus("Escolha CPU, memória ou os dois.");
            return;
        }

        var options = new StressOptions(
            _cpu.IsChecked == true, _memory.IsChecked == true,
            TimeSpan.FromMinutes(Minutes[_minutesIndex]), Limits[_limitIndex], 1024);

        _running = true;
        _start.IsEnabled = false;
        _stop.IsEnabled = true;
        Cts = new CancellationTokenSource();
        var token = Cts.Token;
        SetStatus("Teste em andamento...");
        string? recording = BeginRecordingIfChecked("estresse");

        try
        {
            var sensors = await AppServices.Sensors;
            var result = await Task.Run(() => StressService.Run(options, () => ReadSafe(sensors), OnSample, token));
            SetStatus(result.Reason + $" Duração {result.Elapsed:mm\\:ss}. " +
                      (result.MaxCpuTemperature is { } c ? $"Máx. CPU {c:0} °C. " : "") +
                      (result.MaxGpuTemperature is { } g ? $"Máx. GPU {g:0} °C." : ""),
                result.Errors > 0 ? "BadBrush" : result.Completed ? "OkBrush" : "WarnBrush");
        }
        catch (Exception ex) when (ex is InvalidOperationException or OutOfMemoryException)
        {
            SetStatus("Não foi possível executar o teste: " + ex.Message, "BadBrush");
        }
        finally
        {
            EndRecording(recording);
            _running = false;
            _start.IsEnabled = true;
            _stop.IsEnabled = false;
        }
    }

    private async Task RunPerCoreAsync()
    {
        if (_running) return;
        _running = true;
        _start.IsEnabled = false;
        _perCore.IsEnabled = false;
        _stop.IsEnabled = true;
        Cts = new CancellationTokenSource();
        var token = Cts.Token;
        SetStatus("Teste por núcleo em andamento...");
        string? recording = BeginRecordingIfChecked("estresse-por-nucleo");
        try
        {
            var hw = await AppServices.Hardware();
            var cores = PerCoreStress.CoresToTest(hw.Cpu.Threads, hw.Cpu.Cores, true);
            var sensors = await AppServices.Sensors;
            var options = new PerCoreOptions(SecondsPerCore[_secondsIndex], Limits[_limitIndex], true);
            var results = await Task.Run(() => PerCoreStress.Run(options, cores, () => ReadSafe(sensors), OnCoreProgress, token));

            _live.Text = "";
            _coreResults.Children.Clear();
            _coreResults.Children.Add(Heading("Resultado por núcleo"));
            foreach (var r in results)
            {
                string state = r.Errors > 0 ? $"✖ {r.Errors} erro(s): instável" : r.Completed ? "✓ estável" : "interrompido";
                _coreResults.Children.Add(Row($"Núcleo {r.LogicalCore / (cores.Count < hw.Cpu.Threads ? 2 : 1) + 1}", $"{state}" + (r.MaxTemperature is { } t ? $" · máx {t:0} °C" : ""),
                    null, r.Errors > 0 ? "BadBrush" : "MutedBrush"));
            }

            bool bad = results.Any(r => r.Errors > 0);
            SetStatus(bad ? "Há núcleos instáveis: reduza o ajuste de undervolt/Curve Optimizer em 5 pontos para esses núcleos." :
                      results.All(r => r.Completed) ? "Todos os núcleos testados ficaram estáveis." : "Teste interrompido.", bad ? "BadBrush" : "OkBrush");
        }
        catch (Exception ex) when (ex is InvalidOperationException or AggregateException)
        {
            SetStatus("Não foi possível executar o teste: " + ex.Message, "BadBrush");
        }
        finally
        {
            EndRecording(recording);
            _running = false;
            _start.IsEnabled = true;
            _perCore.IsEnabled = true;
            _stop.IsEnabled = false;
        }
    }

    private void OnCoreProgress(PerCoreProgress p) => Dispatcher.BeginInvoke(new Action(() =>
    {
        _cpuTemp.Value = p.CpuTemperature;
        _live.Text = $"Núcleo {p.Index + 1} de {p.Total} · {p.Elapsed:mm\\:ss} · erros: {p.Errors}";
        _live.Foreground = Brush(p.Errors > 0 ? "BadBrush" : "TextBrush");
    }));

    private static SensorSnapshot? ReadSafe(SensorService sensors)
    {
        try { return sensors.Read(); }
        catch (Exception) { return null; } // sensor indisponível: o teste continua sem temperatura
    }

    private void OnSample(StressSample sample) => Dispatcher.BeginInvoke(new Action(() =>
    {
        _cpuTemp.Value = sample.CpuTemperature;
        _cpuLoad.Value = sample.CpuLoad;
        _gpuTemp.Value = sample.GpuTemperature;
        _live.Text = $"Tempo {sample.Elapsed:mm\\:ss} · erros: {sample.Errors}" +
                     (sample.CpuTemperature is null ? " · sem leitura de temperatura (instale o PawnIO para ver a CPU)" : "");
        _live.Foreground = Brush(sample.Errors > 0 ? "BadBrush" : "TextBrush");
    }));
}
