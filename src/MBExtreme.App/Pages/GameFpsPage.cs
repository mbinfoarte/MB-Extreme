using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using MBExtreme.Core.Fps;
using MBExtreme.Core.Sensors;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>Jogo (FPS): captura os quadros de um jogo por alguns minutos e mostra o relatório (FPS, 1% pior, gráfico de tempo de quadro, engasgos e sensores), com histórico e comparação.</summary>
public sealed class GameFpsPage : ToolPageBase
{
    private static readonly int[] Limits = { 0, 30, 60, 120, 300 };
    private static readonly int[] Warmups = { 0, 5, 10 };

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly TextBox _name = new() { MinWidth = 180, Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBox _label = new() { MinWidth = 140 };
    private readonly StackPanel _live = new() { Visibility = Visibility.Collapsed };
    private readonly StackPanel _report = new();
    private readonly StackPanel _history = new();
    private readonly TextBlock _big = new() { FontSize = 54, FontWeight = FontWeights.Bold, Text = "-- FPS" };
    private readonly TextBlock _bigDetail = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap };
    private readonly LiveChart _frameChart = new(300, Color.FromRgb(0xFF, 0xC4, 0x00)) { Height = 120, FormatMax = v => $"{v:0} ms" };
    private readonly LiveChart _fpsChart = new(60, Color.FromRgb(0x3D, 0xDC, 0x97)) { Height = 90, FormatMax = v => $"{v:0} FPS" };
    private readonly Dictionary<string, MetricTile> _tiles = new();
    private readonly List<double> _fpsHistory = new();
    private SensorRecorder _sensors = new();
    private Button? _start, _stop;
    private int _limit, _warmup = 5;
    private bool _capturing, _sampling;
    private double _minFps = double.MaxValue, _maxFps;
    private DateTime _startedAt, _lastSensor;
    private FpsSession? _current;
    private IReadOnlyList<double> _currentRaw = Array.Empty<double>();
    private string? _gpuName;

    public GameFpsPage() : base("Jogo (FPS)",
        "Meça um jogo de verdade: abra o jogo, clique em Iniciar captura, jogue um trecho (2 a 3 minutos) e clique em Parar. O relatório mostra FPS médio, 1% e 0,1% piores, o gráfico de tempo de cada quadro (os picos são engasgos), a temperatura e o uso do PC. A medição lê eventos do Windows, sem injetar nada no jogo, e precisa de administrador. Em tela cheia exclusiva pode falhar: prefira janela sem borda.")
    {
        _start = AddButton("Iniciar captura", (_, _) => Start());
        _stop = AddButton("Parar e analisar", async (_, _) => await StopAsync(), false);
        _stop.IsEnabled = false;

        var options = new StackPanel();
        var row1 = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        row1.Children.Add(Label("Programa do jogo (opcional):"));
        _name.ToolTip = "Nome do processo, como aparece no Gerenciador de Tarefas (ex.: FC26). Vazio: o programa que mais gera quadros.";
        row1.Children.Add(_name);
        row1.Children.Add(Label("Nome desta medição:"));
        _label.ToolTip = "Ex.: antes de otimizar, depois do driver novo";
        row1.Children.Add(_label);
        options.Children.Add(row1);
        options.Children.Add(Field("Parar sozinho", Chips(Limits.Select(l => l == 0 ? "Só quando eu parar" : l < 60 ? l + " s" : l / 60 + " min").ToList(), 0, i => _limit = Limits[i])));
        options.Children.Add(Field("Ignorar o começo", Chips(Warmups.Select(w => w == 0 ? "Nada" : w + " s").ToList(), 1, i => _warmup = Warmups[i])));
        options.Children.Add(Muted("Ranking anônimo: se você ligou a opção em Benchmark do PC, a medição também é enviada (só modelo do PC, nome do jogo e FPS, sem dados pessoais).", 11));
        Body.Children.Add(CardOf(options));

        BuildLive();
        Body.Children.Add(_live);
        Body.Children.Add(_report);
        Body.Children.Add(Heading("Medições guardadas"));
        Body.Children.Add(_history);

        _timer.Tick += async (_, _) => await TickAsync();
        Loaded += async (_, _) =>
        {
            RefreshHistory();
            try { var hw = await AppServices.Hardware(); _gpuName = hw.Gpus.FirstOrDefault()?.Name; }
            catch (Exception ex) when (ex is InvalidOperationException or System.Management.ManagementException or AggregateException) { }
        };
        Unloaded += (_, _) => { if (_capturing) _ = StopAsync(); };
    }

    private TextBlock Label(string text) => new() { Text = text, FontSize = 12, Foreground = Brush("MutedBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };

    private WrapPanel Chips(IReadOnlyList<string> options, int selected, Action<int> onChange)
    {
        var panel = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        var buttons = new List<Button>();
        void Paint(int index)
        {
            for (int i = 0; i < buttons.Count; i++) { buttons[i].Style = (Style)FindResource(i == index ? "PrimaryButton" : "GhostButton"); buttons[i].Padding = new Thickness(10, 3, 10, 3); buttons[i].FontSize = 12; }
        }
        for (int i = 0; i < options.Count; i++)
        {
            int index = i;
            var button = new Button { Content = options[i], Margin = new Thickness(0, 0, 5, 5) };
            button.Click += (_, _) => { Paint(index); onChange(index); };
            buttons.Add(button);
            panel.Children.Add(button);
        }
        Paint(selected);
        return panel;
    }

    private Grid Field(string label, UIElement content)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Brush("MutedBrush"), Margin = new Thickness(0, 5, 8, 0) });
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);
        return grid;
    }

    // ---------------------------------------------------------------- ao vivo

    private void BuildLive()
    {
        var head = new StackPanel();
        head.Children.Add(_big);
        _bigDetail.Foreground = Brush("MutedBrush");
        head.Children.Add(_bigDetail);
        _live.Children.Add(CardOf(head));

        var charts = new StackPanel();
        charts.Children.Add(new TextBlock { Text = "Tempo de cada quadro (menor é melhor; picos são engasgos)", FontSize = 12, Foreground = Brush("MutedBrush") });
        charts.Children.Add(_frameChart);
        charts.Children.Add(new TextBlock { Text = "FPS por segundo", FontSize = 12, Foreground = Brush("MutedBrush"), Margin = new Thickness(0, 8, 0, 0) });
        charts.Children.Add(_fpsChart);
        _live.Children.Add(CardOf(charts));

        var grid = new UniformGrid { Columns = 2 };
        foreach (var (key, title, color, max) in new[]
        {
            ("gpuload", "Uso da GPU", Color.FromRgb(0xFF, 0x8A, 0x4D), 100.0), ("gputemp", "Temperatura da GPU", Color.FromRgb(0xFF, 0x5A, 0x5F), 100.0),
            ("cpuload", "Uso da CPU", Color.FromRgb(0x4D, 0xA3, 0xFF), 100.0), ("cputemp", "Temperatura da CPU", Color.FromRgb(0xFF, 0x5A, 0x5F), 100.0),
        })
        {
            var tile = new MetricTile(title, color, max) { Margin = new Thickness(0, 0, 8, 8) };
            _tiles[key] = tile;
            grid.Children.Add(tile);
        }
        _live.Children.Add(grid);
    }

    private void Start()
    {
        try
        {
            AppServices.Overlay.BeginCapture(string.IsNullOrWhiteSpace(_name.Text) ? null : _name.Text.Trim());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            SetStatus("Não foi possível iniciar a captura (precisa de administrador): " + ex.Message, "BadBrush");
            return;
        }

        _sensors = new SensorRecorder();
        _capturing = true;
        _startedAt = DateTime.Now;
        _lastSensor = DateTime.MinValue;
        _minFps = double.MaxValue;
        _maxFps = 0;
        _fpsHistory.Clear();
        foreach (var tile in _tiles.Values) tile.Visibility = Visibility.Visible;
        _live.Visibility = Visibility.Visible;
        _report.Children.Clear();
        _start!.IsEnabled = false;
        _stop!.IsEnabled = true;
        _big.Text = "-- FPS";
        _bigDetail.Text = "Aguardando um jogo. Abra o jogo e mova a câmera.";
        SetStatus("Capturando... jogue um trecho e depois clique em Parar e analisar.");
        _timer.Start();
    }

    private async Task TickAsync()
    {
        if (!_capturing) return;
        double elapsed = (DateTime.Now - _startedAt).TotalSeconds;
        var live = AppServices.Overlay.CaptureLive(300);
        if (live is not null)
        {
            _minFps = Math.Min(_minFps, live.CurrentFps > 0 ? live.CurrentFps : _minFps);
            _maxFps = Math.Max(_maxFps, live.CurrentFps);
            _big.Text = $"{live.CurrentFps:0} FPS";
            _bigDetail.Text = $"{live.Name} · {live.Frames} quadros · {TimeSpan.FromSeconds(elapsed):mm\\:ss}" +
                (_minFps < double.MaxValue ? $" · mín. {_minFps:0}, máx. {_maxFps:0}" : "") +
                (elapsed < _warmup ? $" · ignorando os primeiros {_warmup} s" : "");
            _frameChart.Update(live.RecentFrameMs.ToList());
        }

        if ((DateTime.Now - _lastSensor).TotalSeconds >= 1)
        {
            _lastSensor = DateTime.Now;
            if (live is not null)
            {
                _fpsHistory.Add(live.CurrentFps);
                if (_fpsHistory.Count > 60) _fpsHistory.RemoveAt(0);
                _fpsChart.Update(_fpsHistory.ToList());
            }
            if (!_sampling) _ = SampleSensorsAsync(elapsed);
        }

        if (_limit > 0 && elapsed >= _limit + _warmup) await StopAsync();
    }

    private async Task SampleSensorsAsync(double elapsed)
    {
        _sampling = true;
        try
        {
            var sensors = await AppServices.Sensors;
            var snapshot = await Task.Run(sensors.Read);
            if (!_capturing) return;
            if (elapsed >= _warmup) _sensors.Add(snapshot);
            _tiles["gpuload"].Push(snapshot.GpuLoad, $"{snapshot.GpuLoad:0}%", snapshot.GpuClockMhz is { } c ? $"{c:0} MHz" + (snapshot.GpuPowerWatts is { } w ? $" · {w:0} W" : "") : "");
            _tiles["gputemp"].Push(snapshot.GpuTemperature, $"{snapshot.GpuTemperature:0}°C");
            _tiles["cpuload"].Push(snapshot.CpuLoad, $"{snapshot.CpuLoad:0}%", snapshot.CpuClockMhz is { } cc ? $"{cc:0} MHz" + (snapshot.CpuPowerWatts is { } cw ? $" · {cw:0} W" : "") : "");
            _tiles["cputemp"].Push(snapshot.CpuTemperature, $"{snapshot.CpuTemperature:0}°C");
        }
        catch (Exception ex) when (ex is InvalidOperationException or AggregateException) { }
        finally { _sampling = false; }
    }

    private async Task StopAsync()
    {
        if (!_capturing) return;
        _capturing = false;
        _timer.Stop();
        _start!.IsEnabled = true;
        _stop!.IsEnabled = false;
        var result = AppServices.Overlay.EndCapture();
        _live.Visibility = Visibility.Collapsed;
        if (result is null)
        {
            SetStatus("Nenhum jogo apresentou quadros durante a captura. Abra o jogo (em janela sem borda) antes de iniciar e confira o nome do programa.", "WarnBrush");
            return;
        }

        var session = await Task.Run(() => FpsSessionBuilder.Build(result.Name, string.IsNullOrWhiteSpace(_label.Text) ? null : _label.Text.Trim(), result.PresentSeconds, _sensors.Tracks(), _warmup, DateTime.Now, _gpuName));
        if (session is null)
        {
            SetStatus("Poucos quadros para analisar (precisa de pelo menos alguns segundos de jogo).", "WarnBrush");
            return;
        }

        _currentRaw = FrameAnalysis.FrameTimes(result.PresentSeconds, _warmup).FrameMs;
        try { FpsSessionHistory.Add(session); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SetStatus("A medição não pôde ser guardada: " + ex.Message, "WarnBrush"); }
        ShowSession(session);
        RefreshHistory();
        SetStatus($"Medição de {session.Game} pronta e guardada.", "OkBrush");
        if (RankingService.OptedIn)
        {
            var standing = await RankingService.SubmitGameAsync(session);
            SetStatus($"Medição de {session.Game} pronta e guardada.\nRanking: " + RankingService.Describe(standing, session.Metrics.AverageFps), standing is null ? "WarnBrush" : "OkBrush");
        }
    }

    // ---------------------------------------------------------------- relatório

    private Border StatCard(string title, string value, string detail = "", Brush? valueBrush = null)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontSize = 12, Foreground = Brush("MutedBrush") });
        stack.Children.Add(new TextBlock { Text = value, FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = valueBrush ?? Brush("AccentBrush") });
        if (detail.Length > 0) stack.Children.Add(new TextBlock { Text = detail, FontSize = 11, Foreground = Brush("MutedBrush"), TextWrapping = TextWrapping.Wrap });
        var card = CardOf(stack);
        card.Margin = new Thickness(0, 0, 8, 8);
        card.Padding = new Thickness(12, 8, 12, 8);
        return card;
    }

    private void ShowSession(FpsSession s)
    {
        _current = s;
        var m = s.Metrics;
        _report.Children.Clear();
        _report.Children.Add(Heading($"{s.Game}" + (s.Label is { Length: > 0 } ? $" · {s.Label}" : "") + $" · {s.When:dd/MM/yyyy HH:mm}"));

        var grid = new UniformGrid { Columns = 3 };
        grid.Children.Add(StatCard("FPS médio", $"{m.AverageFps:0.#}", $"mínimo {m.MinFps:0.#} · máximo {m.MaxFps:0.#}"));
        grid.Children.Add(StatCard("1% pior", $"{m.Low1Fps:0.#}", $"média dos 1% mais lentos · por tempo {m.Low1TimeFps:0.#}"));
        grid.Children.Add(StatCard("0,1% pior", $"{m.Low01Fps:0.#}", m.Low01Reliable ? $"por tempo {m.Low01TimeFps:0.#}" : "amostra pequena: use só como referência"));
        grid.Children.Add(StatCard("Tempo de quadro", $"{m.AverageFrameMs:0.##} ms", $"mediana {m.MedianFrameMs:0.##} · P99 {m.P99FrameMs:0.##} · pior {m.MaxFrameMs:0.#} ms"));
        grid.Children.Add(StatCard("Engasgos", m.Stutters.ToString(), $"{m.StuttersPerMinute:0.#} por minuto" + (m.Pauses > 0 ? $" · {m.Pauses} pausa(s) ignorada(s)" : "")));
        grid.Children.Add(StatCard("Duração", $"{m.DurationSeconds:0} s", $"{m.Frames} quadros analisados (sem os {s.WarmupSeconds:0} s iniciais)"));
        _report.Children.Add(grid);

        var rating = FrameAnalysis.Rate(m);
        var verdict = new TextBlock { Text = FrameAnalysis.Describe(rating), FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Foreground = Brush(rating switch { FrameAnalysis.Smoothness.Excellent => "OkBrush", FrameAnalysis.Smoothness.Good => "OkBrush", FrameAnalysis.Smoothness.Irregular => "WarnBrush", _ => "BadBrush" }) };
        _report.Children.Add(CardOf(verdict));

        var charts = new StackPanel();
        charts.Children.Add(new TextBlock { Text = "Tempo de cada quadro durante a sessão (picos = engasgos)", FontSize = 12, Foreground = Brush("MutedBrush") });
        var frameChart = new LiveChart(Math.Max(10, s.FrameMs.Length), Color.FromRgb(0xFF, 0xC4, 0x00)) { Height = 140, FormatMax = v => $"{v:0.#} ms" };
        frameChart.Loaded += (_, _) => frameChart.Update(s.FrameMs.ToList());
        charts.Children.Add(frameChart);
        if (s.FpsSeries.Length >= 2)
        {
            charts.Children.Add(new TextBlock { Text = "FPS por segundo", FontSize = 12, Foreground = Brush("MutedBrush"), Margin = new Thickness(0, 8, 0, 0) });
            var fpsChart = new LiveChart(Math.Max(10, s.FpsSeries.Length), Color.FromRgb(0x3D, 0xDC, 0x97)) { Height = 100, FormatMax = v => $"{v:0} FPS" };
            fpsChart.Loaded += (_, _) => fpsChart.Update(s.FpsSeries.ToList());
            charts.Children.Add(fpsChart);
        }
        _report.Children.Add(CardOf(charts));

        if (s.Sensors.Count > 0)
        {
            var sensorGrid = new UniformGrid { Columns = 2 };
            foreach (var track in s.Sensors)
            {
                var box = new StackPanel();
                box.Children.Add(new TextBlock { Text = $"{track.Name}: média {track.Average:0.#} {track.Unit} · máx. {track.Max:0.#} {track.Unit}", FontSize = 12, Foreground = Brush("MutedBrush"), TextWrapping = TextWrapping.Wrap });
                var chart = new LiveChart(Math.Max(10, track.Values.Length), Color.FromRgb(0xFF, 0x8A, 0x4D)) { Height = 70, Maximum = track.Unit == "%" ? 100 : 0, FormatMax = v => $"{v:0} {track.Unit}" };
                var values = track.Values.ToList();
                chart.Loaded += (_, _) => chart.Update(values);
                box.Children.Add(chart);
                var card = CardOf(box);
                card.Margin = new Thickness(0, 0, 8, 8);
                sensorGrid.Children.Add(card);
            }
            _report.Children.Add(sensorGrid);
        }

        var actions = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
        var html = SmallButton("Salvar relatório (HTML)...", (_, _) => SaveHtml(s));
        html.Margin = new Thickness(0, 0, 8, 4);
        actions.Children.Add(html);
        var csv = SmallButton("Exportar quadros (CSV)...", (_, _) => SaveCsv(s));
        csv.Margin = new Thickness(0, 0, 8, 4);
        csv.IsEnabled = _currentRaw.Count > 0 && ReferenceEquals(s, _current);
        actions.Children.Add(csv);
        var copy = SmallButton("Copiar resumo", (_, _) => { if (TryCopy(SessionReport.ToText(s))) SetStatus("Resumo copiado.", "OkBrush"); });
        copy.Margin = new Thickness(0, 0, 8, 4);
        actions.Children.Add(copy);
        _report.Children.Add(actions);
    }

    private void SaveHtml(FpsSession s)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Página (*.html)|*.html", FileName = $"fps-{ShortName(s.Game)}-{s.When:yyyyMMdd-HHmm}.html", Title = "Salvar o relatório" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, SessionReport.ToHtml(s), new System.Text.UTF8Encoding(false));
            Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
            SetStatus("Relatório salvo.", "OkBrush");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { SetStatus("Não foi possível salvar: " + ex.Message, "BadBrush"); }
    }

    private void SaveCsv(FpsSession s)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Planilha (*.csv)|*.csv", FileName = $"quadros-{ShortName(s.Game)}-{s.When:yyyyMMdd-HHmm}.csv", Title = "Exportar os tempos de quadro" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, FrameAnalysis.ToCsv(_currentRaw), new System.Text.UTF8Encoding(false));
            SetStatus("Quadros exportados.", "OkBrush");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SetStatus("Não foi possível salvar: " + ex.Message, "BadBrush"); }
    }

    private static string ShortName(string text) => new(text.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').Take(30).ToArray());

    // ---------------------------------------------------------------- histórico e comparação

    private void RefreshHistory()
    {
        _history.Children.Clear();
        var all = FpsSessionHistory.Load().OrderByDescending(x => x.When).ToList();
        if (all.Count == 0)
        {
            _history.Children.Add(Muted("Nenhuma medição ainda. Faça uma captura para começar o histórico."));
            return;
        }

        foreach (var session in all)
        {
            var s = session;
            string title = $"{s.Game}" + (s.Label is { Length: > 0 } ? $" · {s.Label}" : "") + $" · {s.When:dd/MM HH:mm}";
            string detail = $"{s.Metrics.AverageFps:0.#} FPS médio · 1% pior {s.Metrics.Low1Fps:0.#} · {s.Metrics.Stutters} engasgo(s) · {s.Metrics.DurationSeconds:0} s";
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            var view = SmallButton("Ver", (_, _) => { _currentRaw = Array.Empty<double>(); ShowSession(s); });
            var compare = SmallButton("Comparar com a aberta", (_, _) => Compare(s));
            compare.IsEnabled = _current is not null && _current.When != s.When;
            var remove = SmallButton("Apagar", (_, _) => { FpsSessionHistory.Remove(s.When); RefreshHistory(); });
            buttons.Children.Add(view);
            buttons.Children.Add(compare);
            buttons.Children.Add(remove);
            _history.Children.Add(Row(title, detail, buttons));
        }
    }

    /// <summary>Compara a medição escolhida (A) com a que está aberta (B), mostrando quanto mudou.</summary>
    private void Compare(FpsSession a)
    {
        if (_current is not { } b) return;
        var card = new StackPanel();
        card.Children.Add(new TextBlock { Text = $"Comparação: {Name(a)}  →  {Name(b)}", FontWeight = FontWeights.SemiBold, FontSize = 14, TextWrapping = TextWrapping.Wrap });
        foreach (var row in SessionComparison.Compare(a.Metrics, b.Metrics))
        {
            string brush = row.Improved ? "OkBrush" : row.Worsened ? "BadBrush" : "MutedBrush";
            string arrow = row.Improved ? "▲ melhorou" : row.Worsened ? "▼ piorou" : "≈ igual";
            var line = new Grid { Margin = new Thickness(0, 3, 0, 0) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            line.ColumnDefinitions.Add(new ColumnDefinition());
            line.Children.Add(new TextBlock { Text = row.Label, Foreground = Brush("MutedBrush") });
            var values = new TextBlock { Text = $"{row.A:0.##} → {row.B:0.##} {row.Unit}" };
            Grid.SetColumn(values, 1);
            line.Children.Add(values);
            var delta = new TextBlock { Text = $"{row.DeltaPercent:+0.#;-0.#;0}%  {arrow}", Foreground = Brush(brush), FontWeight = FontWeights.SemiBold };
            Grid.SetColumn(delta, 2);
            line.Children.Add(delta);
            card.Children.Add(line);
        }
        _history.Children.Insert(0, CardOf(card));
    }

    private static string Name(FpsSession s) => (s.Label is { Length: > 0 } ? s.Label : s.When.ToString("dd/MM HH:mm"))!;
}
