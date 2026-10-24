using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MBExtreme.Core.Benchmark;
using MBExtreme.Core.Fps;
using MBExtreme.Core.Hardware;
using MBExtreme.Core.Sensors;
using MBExtreme.Core.Stress;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>Teste de estresse de CPU, memória e GPU 3D com monitoramento ao vivo, limite de temperatura, detecção de erros, relatório e histórico.</summary>
public sealed class StressPage : ToolPageBase
{
    private static readonly int[] Minutes = { 1, 5, 10, 30 };
    private static readonly int[] Limits = { 80, 90, 95 };
    private static readonly string[] SceneNames = { "Combinado", "Fractal", "Cubos", "Preenchimento", "Cálculo puro" };

    private readonly CheckBox _cpu = new() { Content = "CPU", IsChecked = true, Margin = new Thickness(0, 0, 18, 0) };
    private readonly CheckBox _memory = new() { Content = "Memória", IsChecked = true, Margin = new Thickness(0, 0, 18, 0) };
    private readonly CheckBox _gpu = new() { Content = "GPU 3D (queimador)", IsChecked = false };
    private readonly CheckBox _check = new() { Content = "Conferir erros de imagem da GPU (quadro de referência)", IsChecked = true, Margin = new Thickness(0, 6, 0, 0) };
    private readonly Image _preview = new() { Stretch = Stretch.Uniform, MaxHeight = 300 };
    private readonly TextBlock _previewText = new() { Foreground = Brushes.White, FontSize = 12, Margin = new Thickness(10, 8, 0, 0), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Border _previewCard = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 12, 0, 0), CornerRadius = new CornerRadius(14), ClipToBounds = true, Background = Brushes.Black };
    private readonly Dictionary<string, MetricTile> _tiles = new();
    private readonly UniformGrid _tileGrid = new() { Columns = 3, Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
    private readonly TextBlock _live = new() { FontSize = 14, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _report = new();
    private readonly StackPanel _history = new();
    private readonly Button _start;
    private readonly Button _stop;
    private readonly Button _perCore;
    private readonly StackPanel _coreResults = new();
    private static readonly int[] SecondsPerCore = { 30, 60, 120 };
    private int _secondsIndex = 1;
    private int _minutesIndex = 1;
    private int _limitIndex = 1;
    private int _sceneIndex;
    private bool _running, _userStopped;
    private SensorSnapshot? _latest;
    private WriteableBitmap? _bitmap;
    private StressRun? _current;

    public StressPage() : base("Teste de estresse",
        "Coloca CPU, memória e placa de vídeo sob carga máxima para ver se o PC aguenta: acompanha temperatura, uso, clock e potência ao vivo, confere os cálculos e a imagem para achar instabilidade e gera um relatório. Para sozinho ao chegar na duração ou na temperatura limite. Em notebooks, use com o carregador e fique de olho no calor.")
    {
        _start = AddButton("Iniciar teste", async (_, _) => await RunAsync());
        _stop = AddButton("Parar", (_, _) => { _userStopped = true; Cts?.Cancel(); }, false);
        _stop.IsEnabled = false;
        _perCore = AddButton("Teste por núcleo (Curve Optimizer)", async (_, _) => await RunPerCoreAsync(), false);

        foreach (var box in new[] { _cpu, _memory, _gpu, _check }) box.Foreground = Brush("TextBrush");
        var what = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        what.Children.Add(_cpu);
        what.Children.Add(_memory);
        what.Children.Add(_gpu);

        Body.Children.Add(Heading("O que testar"));
        Body.Children.Add(what);
        Body.Children.Add(Heading("Cena da GPU"));
        Body.Children.Add(Choice(SceneNames, _sceneIndex, i => _sceneIndex = i));
        Body.Children.Add(_check);
        Body.Children.Add(Muted("A GPU desenha cenas pesadas nossas (fractal, milhares de cubos, camadas de preenchimento e cálculo puro). \"Combinado\" alterna as quatro a cada 10 s. Se o Windows reiniciar a placa (TDR) ou o quadro de referência sair diferente, o teste acusa instabilidade."));
        Body.Children.Add(Heading("Duração"));
        Body.Children.Add(Choice(Minutes.Select(m => m + " min").ToList(), _minutesIndex, i => _minutesIndex = i));
        Body.Children.Add(Heading("Parar se a temperatura chegar a"));
        Body.Children.Add(Choice(Limits.Select(l => l + " °C").ToList(), _limitIndex, i => _limitIndex = i));
        Body.Children.Add(Heading("Teste por núcleo: tempo em cada núcleo"));
        Body.Children.Add(Choice(SecondsPerCore.Select(x => x + " s").ToList(), _secondsIndex, i => _secondsIndex = i));
        Body.Children.Add(Muted("Roda um núcleo de cada vez com cálculos conferidos. Serve para ajustar Curve Optimizer/undervolt: o núcleo que der erro está instável. Cada núcleo físico é testado uma vez."));
        Body.Children.Add(RecordOption());

        var overlay = new Grid();
        overlay.Children.Add(_preview);
        overlay.Children.Add(_previewText);
        _previewCard.Child = overlay;
        Body.Children.Add(_previewCard);

        foreach (var (key, title, color, max) in new[]
        {
            ("gpuload", "Uso da GPU", Color.FromRgb(0xFF, 0x4D, 0x4D), 100.0), ("gputemp", "Temperatura da GPU", Color.FromRgb(0xFF, 0x8A, 0x4D), 100.0), ("gpuclock", "Clock da GPU", Color.FromRgb(0x4D, 0xD0, 0xFF), 0.0),
            ("gpupower", "Potência da GPU", Color.FromRgb(0xB0, 0x7C, 0xFF), 0.0), ("fps", "FPS da cena 3D", Color.FromRgb(0x3D, 0xDC, 0x97), 0.0), ("cpuload", "Uso da CPU", Color.FromRgb(0x4D, 0xA3, 0xFF), 100.0),
            ("cputemp", "Temperatura da CPU", Color.FromRgb(0xFF, 0x5A, 0x5F), 100.0), ("cpuclock", "Clock da CPU", Color.FromRgb(0xFF, 0xC4, 0x00), 0.0), ("cpupower", "Potência da CPU", Color.FromRgb(0xFF, 0x9A, 0x1F), 0.0),
        })
        {
            var tile = new MetricTile(title, color, max) { Margin = new Thickness(0, 0, 8, 8) };
            _tiles[key] = tile;
            _tileGrid.Children.Add(tile);
        }
        Body.Children.Add(_tileGrid);
        Body.Children.Add(_live);
        Body.Children.Add(_coreResults);
        Body.Children.Add(_report);
        Body.Children.Add(Heading("Testes anteriores"));
        Body.Children.Add(_history);
        Loaded += (_, _) => RefreshHistory();
    }

    // ---------------------------------------------------------------- teste combinado

    private async Task RunAsync()
    {
        if (_running) return;
        bool wantCpu = _cpu.IsChecked == true, wantMemory = _memory.IsChecked == true, wantGpu = _gpu.IsChecked == true;
        if (!wantCpu && !wantMemory && !wantGpu)
        {
            SetStatus("Escolha CPU, memória, GPU ou mais de um.");
            return;
        }

        var duration = TimeSpan.FromMinutes(Minutes[_minutesIndex]);
        double limit = Limits[_limitIndex];
        _running = true;
        _userStopped = false;
        _latest = null;
        _start.IsEnabled = false;
        _perCore.IsEnabled = false;
        _stop.IsEnabled = true;
        _report.Children.Clear();
        _coreResults.Children.Clear();
        _tileGrid.Visibility = Visibility.Visible;
        _previewCard.Visibility = wantGpu ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (key, tile) in _tiles) tile.Visibility = key switch { "fps" => wantGpu ? Visibility.Visible : Visibility.Collapsed, _ => Visibility.Visible };
        Cts = new CancellationTokenSource();
        var token = Cts.Token;
        var recorder = new SensorRecorder();
        SetStatus("Teste em andamento...");
        string? recording = BeginRecordingIfChecked("estresse");
        string? cpuName = null, gpuName = null;
        try
        {
            var sensors = await AppServices.Sensors;
            try
            {
                var hw = await AppServices.Hardware();
                cpuName = hw.Cpu.Name.Trim();
                gpuName = hw.Gpus.OrderByDescending(g => g.VramBytes).FirstOrDefault()?.Name;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Management.ManagementException or AggregateException) { }

            var started = DateTime.Now;
            Task<StressResult>? cpuTask = wantCpu || wantMemory
                ? Task.Run(() => StressService.Run(new StressOptions(wantCpu, wantMemory, duration, limit, 1024), () => _latest, _ => { }, token))
                : null;
            Task<GpuBurnResult>? gpuTask = wantGpu
                ? Task.Run(() => Gpu3DBenchmark.Burn(new GpuBurnOptions((BurnScene)_sceneIndex, duration, _check.IsChecked == true),
                    () => _latest?.GpuTemperature is { } t && t >= limit ? $"Parou: a GPU chegou a {t:0} °C (limite de {limit:0} °C)." : null, OnGpuSample, ShowFrame, token))
                : null;

            var all = Task.WhenAll(new Task?[] { cpuTask, gpuTask }.Where(t => t is not null).Select(t => (Task)t!));
            var cancelOthers = Task.Run(async () =>
            {
                // se um lado parar sozinho (temperatura ou erro), para o outro também
                var pending = new List<Task>(new Task?[] { cpuTask, gpuTask }.Where(t => t is not null)!);
                while (pending.Count > 0)
                {
                    var done = await Task.WhenAny(pending);
                    pending.Remove(done);
                    if (done is Task<StressResult> { Result.Completed: false } || done is Task<GpuBurnResult> { Result.Completed: false }) Cts.Cancel();
                }
            });

            while (!all.IsCompleted)
            {
                var snapshot = await Task.Run(() => ReadSafe(sensors));
                if (snapshot is not null)
                {
                    _latest = snapshot;
                    recorder.Add(snapshot);
                    PushSensors(snapshot);
                }
                _live.Text = $"Tempo {DateTime.Now - started:mm\\:ss}" + (snapshot is null || snapshot.CpuTemperature is null && wantCpu ? " · sem leitura de temperatura (instale o PawnIO para ver a CPU)" : "");
                await Task.WhenAny(all, Task.Delay(1000));
            }
            await cancelOthers;

            var cpuResult = cpuTask is null ? null : await cpuTask;
            var gpuResult = gpuTask is null ? null : await gpuTask;
            var run = BuildRun(cpuResult, gpuResult, wantCpu, wantMemory, wantGpu, cpuName, gpuName, recorder);
            try { StressHistory.Add(run); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            ShowRun(run);
            RefreshHistory();
            SetStatus(run.Verdict, run.Stable ? "OkBrush" : run.Errors > 0 ? "BadBrush" : "WarnBrush");
            _live.Text = "";
        }
        catch (Exception ex) when (ex is InvalidOperationException or OutOfMemoryException or AggregateException)
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
            _previewCard.Visibility = Visibility.Collapsed;
        }
    }

    private StressRun BuildRun(StressResult? cpu, GpuBurnResult? gpu, bool wantCpu, bool wantMemory, bool wantGpu, string? cpuName, string? gpuName, SensorRecorder recorder)
    {
        var parts = new List<(bool Ok, string Reason)>();
        if (cpu is not null) parts.Add((cpu.Completed, cpu.Reason));
        if (gpu is not null) parts.Add((gpu.Completed, gpu.Reason));
        bool completed = parts.All(p => p.Ok);
        string reason = "Concluído no tempo escolhido.";
        if (!completed)
        {
            var failed = parts.Where(p => !p.Ok).Select(p => p.Reason).ToList();
            reason = _userStopped ? "Interrompido por você." : failed.FirstOrDefault(r => !r.StartsWith("Interrompido por você")) ?? failed[0];
        }

        double elapsed = Math.Max(cpu?.Elapsed.TotalSeconds ?? 0, gpu?.Elapsed.TotalSeconds ?? 0);
        double? maxGpuTemp = cpu?.MaxGpuTemperature;
        var tracks = recorder.Tracks();
        if (tracks.FirstOrDefault(t => t.Name == "Temperatura da GPU") is { } gt) maxGpuTemp = Math.Max(maxGpuTemp ?? 0, gt.Max);
        double? maxCpuTemp = cpu?.MaxCpuTemperature;
        if (tracks.FirstOrDefault(t => t.Name == "Temperatura da CPU") is { } ct) maxCpuTemp = Math.Max(maxCpuTemp ?? 0, ct.Max);
        return new StressRun(DateTime.Now, TimeSpan.FromSeconds(elapsed), wantCpu, wantMemory, wantGpu, wantGpu ? SceneNames[_sceneIndex] : null, completed, reason,
            cpu?.Errors ?? 0, gpu?.Errors ?? 0, maxCpuTemp, maxGpuTemp, gpu?.AverageFps ?? 0, gpu?.MinFps ?? 0, cpuName, gpuName, tracks);
    }

    private void PushSensors(SensorSnapshot s)
    {
        _tiles["gpuload"].Push(s.GpuLoad, $"{s.GpuLoad:0}%");
        _tiles["gputemp"].Push(s.GpuTemperature, $"{s.GpuTemperature:0}°C");
        _tiles["gpuclock"].Push(s.GpuClockMhz, $"{s.GpuClockMhz:0} MHz");
        _tiles["gpupower"].Push(s.GpuPowerWatts, $"{s.GpuPowerWatts:0} W");
        _tiles["cpuload"].Push(s.CpuLoad, $"{s.CpuLoad:0}%");
        _tiles["cputemp"].Push(s.CpuTemperature, $"{s.CpuTemperature:0}°C");
        _tiles["cpuclock"].Push(s.CpuClockMhz, $"{s.CpuClockMhz:0} MHz");
        _tiles["cpupower"].Push(s.CpuPowerWatts, $"{s.CpuPowerWatts:0} W");
    }

    private void OnGpuSample(GpuBurnSample sample) => Dispatcher.BeginInvoke(new Action(() =>
    {
        _tiles["fps"].Push(sample.Fps, $"{sample.Fps:0} FPS", sample.Scene);
        _previewText.Text = $"{sample.Scene}\n{sample.Fps:0} FPS · {sample.FrameMs:0.0} ms · erros de imagem: {sample.Errors}";
    }));

    private void ShowFrame(byte[] bgra, int width, int height) => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (_bitmap is null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
        {
            _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            _preview.Source = _bitmap;
        }
        _bitmap.WritePixels(new Int32Rect(0, 0, width, height), bgra, width * 4, 0);
    }));

    // ---------------------------------------------------------------- relatório e histórico

    private void ShowRun(StressRun run)
    {
        _current = run;
        _report.Children.Clear();
        _report.Children.Add(Heading($"Resultado: {run.Title} · {run.When:dd/MM/yyyy HH:mm}"));
        string brush = run.Stable ? "OkBrush" : run.Errors > 0 ? "BadBrush" : "WarnBrush";
        _report.Children.Add(CardOf(new TextBlock { Text = run.Verdict, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Foreground = Brush(brush) }));

        var grid = new UniformGrid { Columns = 3 };
        void Card(string title, string value, string detail = "")
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = title, FontSize = 12, Foreground = Brush("MutedBrush") });
            stack.Children.Add(new TextBlock { Text = value, FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Brush("AccentBrush") });
            if (detail.Length > 0) stack.Children.Add(new TextBlock { Text = detail, FontSize = 11, Foreground = Brush("MutedBrush"), TextWrapping = TextWrapping.Wrap });
            var card = CardOf(stack);
            card.Margin = new Thickness(0, 0, 8, 8);
            card.Padding = new Thickness(12, 8, 12, 8);
            grid.Children.Add(card);
        }
        Card("Duração", $"{run.Elapsed:mm\\:ss}", run.Completed ? "completo" : "interrompido");
        Card("Erros", run.Errors.ToString(), $"cálculo e memória {run.CpuMemoryErrors} · imagem da GPU {run.GpuErrors}");
        if (run.MaxCpuTemperature is { } c) Card("CPU, temperatura máxima", $"{c:0} °C");
        if (run.MaxGpuTemperature is { } g) Card("GPU, temperatura máxima", $"{g:0} °C");
        if (run.Gpu) Card("GPU 3D", $"{run.GpuAverageFps:0.#} FPS", $"mínimo {run.GpuMinFps:0.#} · {run.GpuScene}");
        _report.Children.Add(grid);

        if (run.Tracks.Count > 0)
        {
            var charts = new UniformGrid { Columns = 2 };
            foreach (var track in run.Tracks)
            {
                var box = new StackPanel();
                box.Children.Add(new TextBlock { Text = $"{track.Name}: média {track.Average:0.#} {track.Unit} · máx. {track.Max:0.#} {track.Unit}", FontSize = 12, Foreground = Brush("MutedBrush"), TextWrapping = TextWrapping.Wrap });
                var chart = new LiveChart(Math.Max(10, track.Values.Length), Color.FromRgb(0xFF, 0x8A, 0x4D)) { Height = 80, Maximum = track.Unit == "%" ? 100 : 0, FormatMax = v => $"{v:0} {track.Unit}" };
                var values = track.Values.ToList();
                chart.Loaded += (_, _) => chart.Update(values);
                box.Children.Add(chart);
                var card = CardOf(box);
                card.Margin = new Thickness(0, 0, 8, 8);
                charts.Children.Add(card);
            }
            _report.Children.Add(charts);
        }

        var actions = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
        var html = SmallButton("Salvar relatório (HTML)...", (_, _) => SaveHtml(run));
        html.Margin = new Thickness(0, 0, 8, 4);
        actions.Children.Add(html);
        var copy = SmallButton("Copiar resumo", (_, _) => { if (TryCopy(StressReportBuilder.ToText(run))) SetStatus("Resumo copiado.", "OkBrush"); });
        copy.Margin = new Thickness(0, 0, 8, 4);
        actions.Children.Add(copy);
        _report.Children.Add(actions);
    }

    private void SaveHtml(StressRun run)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Página (*.html)|*.html", FileName = $"estresse-{run.When:yyyyMMdd-HHmm}.html", Title = "Salvar o relatório" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, StressReportBuilder.ToHtml(run), new System.Text.UTF8Encoding(false));
            Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
            SetStatus("Relatório salvo.", "OkBrush");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { SetStatus("Não foi possível salvar: " + ex.Message, "BadBrush"); }
    }

    private void RefreshHistory()
    {
        _history.Children.Clear();
        var all = StressHistory.Load().OrderByDescending(r => r.When).ToList();
        if (all.Count == 0)
        {
            _history.Children.Add(Muted("Nenhum teste ainda."));
            return;
        }
        foreach (var run in all)
        {
            var r = run;
            var view = SmallButton("Ver", (_, _) => ShowRun(r));
            _history.Children.Add(Row($"{r.Title} · {r.When:dd/MM HH:mm}", r.Verdict, view, r.Stable ? "MutedBrush" : r.Errors > 0 ? "BadBrush" : "WarnBrush"));
        }
    }

    // ---------------------------------------------------------------- teste por núcleo

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
        _tiles["cputemp"].Push(p.CpuTemperature, $"{p.CpuTemperature:0}°C");
        _live.Text = $"Núcleo {p.Index + 1} de {p.Total} · {p.Elapsed:mm\\:ss} · erros: {p.Errors}";
        _live.Foreground = Brush(p.Errors > 0 ? "BadBrush" : "TextBrush");
    }));

    private static SensorSnapshot? ReadSafe(SensorService sensors)
    {
        try { return sensors.Read(); }
        catch (Exception) { return null; } // sensor indisponível: o teste continua sem temperatura
    }
}
