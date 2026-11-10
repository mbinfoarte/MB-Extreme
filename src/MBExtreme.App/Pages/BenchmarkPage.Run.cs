using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MBExtreme.Core.Benchmark;
using MBExtreme.Core.Fps;
using MBExtreme.Core.Progress;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Aba Em andamento: o núcleo animado com o ícone da etapa, o progresso geral com tempo decorrido e restante,
/// a etapa atual com a parte que está rodando e a passada, e a lista de etapas indo de 0 a 100.
/// </summary>
public sealed partial class BenchmarkPage
{
    /// <summary>Ícone e explicação de cada etapa.</summary>
    private static readonly Dictionary<string, (string Glyph, string Description)> StepInfo = new()
    {
        [BenchmarkService.StepSingle] = ("", "Cálculo, ray tracing, física, compressão, criptografia e ordenação em 1 linha de execução"),
        [BenchmarkService.StepMulti] = ("", "As mesmas tarefas em todas as linhas de execução, mais as instruções vetoriais"),
        [BenchmarkService.StepMemory] = ("", "Banda de leitura, escrita e cópia e o tempo de acesso do cache até a RAM"),
        [BenchmarkService.StepDisk] = ("", "Sequencial, aleatório 4K com 1 e 8 filas (IOPS), gravação aleatória e tempo de acesso, sem o cache do Windows"),
        [BenchmarkService.StepAi] = ("", "Uma rede de 28 milhões de parâmetros escrevendo e lendo texto (tokens/s), cálculo em FP32 e INT8 e a mesma conta na placa de vídeo"),
        [BenchmarkService.StepDaily] = ("", "Documentos, sites, vídeo H.264 de verdade, planilhas, fotos, compactação e tudo ao mesmo tempo, sem internet"),
        [GpuWindowBench.Reflections] = ("", "Raymarching com reflexos, sombras suaves e oclusão, na janela 3D separada"),
        [GpuWindowBench.Geometry] = ("", "2.500 objetos deformados (5,8 milhões de triângulos por quadro) com 3 luzes que se movem"),
        [GpuWindowBench.Studio] = ("", "Render em blocos: a imagem de estúdio vai sendo completada quadrado por quadrado (path tracing, a luz rebate até 5 vezes)"),
        [GpuWindowBench.Game] = ("", "Processador + placa de vídeo, como um jogo: 10 mil soldados animados com IA no processador, disparos de energia, explosões, fumaça e um lago com reflexo"),
        [GpuWindowBench.ResolutionLimit] = ("", "A simulação de jogo em cada resolução até a escolhida: mostra até onde passa de 60 FPS"),
        [Dx12RayBench.Step] = ("", "DirectX 12 com ray tracing por hardware: a mesma arena com RT desligado e ligado (sombras, reflexos e oclusão por raio) para mostrar quanto o RT custa"),
        ["1 núcleo"] = ("", "A cena desenhada por raios, bloco a bloco, com 1 linha de execução"),
        ["Todos os núcleos"] = ("", "A mesma cena com todas as linhas de execução"),
    };

    private readonly BenchCore _core = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _runTitle = new() { FontSize = 20, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _runSubtitle = new() { FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _runPercent = new() { FontSize = 22, FontWeight = FontWeights.Bold, Width = 64, VerticalAlignment = VerticalAlignment.Center };
    private readonly ProgressBar _overall = new() { Height = 8, Maximum = 100, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _runClock = new() { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    private readonly TextBlock _stepTitle = new() { FontSize = 15, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _stepDescription = new() { FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _stepDetail = new() { FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)) };
    private readonly Border _passBadge = new() { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 2, 8, 3), VerticalAlignment = VerticalAlignment.Bottom, Visibility = Visibility.Collapsed };
    private readonly TextBlock _passText = new() { FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black };
    private readonly WrapPanel _subtasks = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock _live = new() { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _windowHint = new() { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed,
        Text = "A placa de vídeo está desenhando na janela \"MB Bench\" (o FPS aparece no canto dela). Não clique nela nem feche: fechar interrompe o teste." };
    private readonly StackPanel _stepList = new();
    private readonly Image _preview = new() { Stretch = Stretch.Uniform, MaxHeight = 360 };
    private readonly Border _previewCard = new() { CornerRadius = new CornerRadius(12), ClipToBounds = true, Background = Brushes.Black, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
    private readonly TextBlock _previewCaption = new() { FontSize = 12, Margin = new Thickness(12, 0, 0, 10), VerticalAlignment = VerticalAlignment.Bottom, Foreground = Brushes.White };
    private readonly Dictionary<string, StepRow> _rows = new();
    private readonly System.Windows.Threading.DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private WriteableBitmap? _bitmap;
    private Stopwatch _runWatch = new();
    private BenchmarkOptions? _runOptions;
    private string[] _runSteps = Array.Empty<string>();
    private string? _currentStep;
    private double _currentStepPercent;

    private sealed class StepRow
    {
        public required RingGauge Ring { get; init; }
        public required TextBlock Name { get; init; }
        public required TextBlock Info { get; init; }
        public required ProgressBar Bar { get; init; }
        public required Border Card { get; init; }
        public double Estimate { get; init; }
        public Stopwatch Watch { get; } = new();
        public bool Done { get; set; }
    }

    private void BuildRunPanel()
    {
        GradientBar(_overall);
        _previewCard.Child = new Grid { Children = { _preview, _previewCaption } };
        _passBadge.Background = (Brush)FindResource("AccentGradientBrush");
        _passBadge.Child = _passText;
        RenderIdleRun();
    }

    private void RenderIdleRun()
    {
        if (_running) return;
        _runPanel.Children.Clear();
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 30, 0, 30) };
        stack.Children.Add(new TextBlock { Text = "", FontFamily = new FontFamily(IconFont), FontSize = 40, HorizontalAlignment = HorizontalAlignment.Center, Foreground = (Brush)FindResource("AccentGradientBrush") });
        stack.Children.Add(new TextBlock { Text = "Nenhum teste rodando", FontSize = 17, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 4) });
        stack.Children.Add(Muted("Escolha o modo e o que testar em Novo teste. Aqui aparecem as etapas indo de 0 a 100, o tempo que falta e os sensores; o 3D abre numa janela separada com o FPS no canto.", 12));
        var go = new Button { Content = "Ir para Novo teste", Style = (Style)FindResource("PrimaryButton"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
        go.Click += (_, _) => SelectTab(0);
        stack.Children.Add(go);
        _runPanel.Children.Add(CardOf(stack));
        if (_bitmap is not null)
        {
            _previewCaption.Text = "Última imagem renderizada";
            _previewCard.Visibility = Visibility.Visible;
            _runPanel.Children.Add(UiTree.Free(_previewCard));
        }
    }

    private void BeginRun(string title, string subtitle, IReadOnlyList<string> steps, BenchmarkOptions? options)
    {
        _running = true;
        _runOptions = options;
        _runSteps = steps.ToArray();
        _currentStep = null;
        _currentStepPercent = 0;
        Cts = new CancellationTokenSource();
        foreach (var b in _extraButtons) b.IsEnabled = false;
        UpdateSummary();
        _runPanel.Children.Clear();
        _rows.Clear();
        _stepList.Children.Clear();
        _subtasks.Children.Clear();
        _overall.Value = 0;
        _runPercent.Text = "0%";
        _runTitle.Text = title;
        _runSubtitle.Text = subtitle;
        _runSubtitle.Foreground = Brush("MutedBrush");
        _runClock.Foreground = Brush("MutedBrush");
        _stepDescription.Foreground = Brush("MutedBrush");
        _live.Foreground = Brush("MutedBrush");
        _windowHint.Foreground = Brush("WarnBrush");
        _live.Text = "";
        _stepTitle.Text = "Preparando...";
        _stepDescription.Text = "";
        _stepDetail.Text = "";
        _passBadge.Visibility = Visibility.Collapsed;
        _windowHint.Visibility = Visibility.Collapsed;
        _previewCard.Visibility = Visibility.Collapsed;
        _core.Running = true;
        _core.StepPercent = 0;
        _core.Caption = $"ETAPA 1 DE {steps.Count}";

        // cabeçalho: título, resumo e Cancelar
        var head = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new StackPanel { Children = { UiTree.Free(_runTitle), UiTree.Free(_runSubtitle) } });
        var cancel = new Button { Style = (Style)FindResource("GhostButton"), VerticalAlignment = VerticalAlignment.Top,
            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { new TextBlock { Text = "", FontFamily = new FontFamily(IconFont), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), Foreground = Brush("BadBrush") }, new TextBlock { Text = "Cancelar" } } } };
        cancel.Click += (_, _) => Cts?.Cancel();
        Grid.SetColumn(cancel, 1);
        head.Children.Add(cancel);
        _runPanel.Children.Add(head);

        // progresso geral
        var bar = new Grid();
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition());
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.Children.Add(UiTree.Free(_runPercent));
        Grid.SetColumn(_overall, 1);
        bar.Children.Add(UiTree.Free(_overall));
        Grid.SetColumn(_runClock, 2);
        bar.Children.Add(UiTree.Free(_runClock));
        var barCard = CardOf(bar);
        barCard.Padding = new Thickness(16, 10, 16, 10);
        _runPanel.Children.Add(barCard);

        // centro: núcleo animado e a etapa atual | direita: lista de etapas
        var main = new Grid();
        main.ColumnDefinitions.Add(new ColumnDefinition());
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        var center = new StackPanel();
        center.Children.Add(UiTree.Free(_core));
        center.Children.Add(UiTree.Free(_previewCard));
        var stepBox = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        stepBox.ColumnDefinitions.Add(new ColumnDefinition());
        stepBox.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        stepBox.Children.Add(new StackPanel { Children = { UiTree.Free(_stepTitle), UiTree.Free(_stepDescription), UiTree.Free(_stepDetail) } });
        Grid.SetColumn(_passBadge, 1);
        stepBox.Children.Add(UiTree.Free(_passBadge));
        center.Children.Add(stepBox);
        center.Children.Add(UiTree.Free(_subtasks));
        center.Children.Add(UiTree.Free(_windowHint));
        center.Children.Add(UiTree.Free(_live));
        var centerCard = CardOf(center);
        centerCard.Margin = new Thickness(0, 0, 10, 8);
        main.Children.Add(centerCard);

        var list = new StackPanel();
        list.Children.Add(new TextBlock { Text = "Etapas", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        foreach (string step in steps)
        {
            double estimate = options is null ? 0 : BenchmarkModes.StepSeconds(options, step);
            var ring = new RingGauge { Width = 22, Height = 22, Thickness = 2.6, Fill = (Brush)FindResource("AccentGradientBrush"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 8, 0) };
            var name = new TextBlock { Text = step, FontSize = 12.5, Foreground = Brush("MutedBrush"), TextTrimming = TextTrimming.CharacterEllipsis };
            var info = new TextBlock { Text = estimate > 0 ? "~" + Clock(estimate) : "", FontSize = 10.5, Foreground = Brush("MutedBrush"), TextTrimming = TextTrimming.CharacterEllipsis };
            var progress = new ProgressBar { Maximum = 100, Margin = new Thickness(0, 3, 0, 0), Visibility = Visibility.Collapsed };
            GradientBar(progress);
            progress.Height = 3;
            var text = new StackPanel { Children = { name, info, progress } };
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(ring);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            var card = new Border { Child = row, CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 5, 6, 5), Margin = new Thickness(0, 1, 0, 1), BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent };
            _stepList.Children.Add(card);
            _rows[step] = new StepRow { Ring = ring, Name = name, Info = info, Bar = progress, Card = card, Estimate = estimate };
        }
        list.Children.Add(UiTree.Free(_stepList));
        var listCard = CardOf(list);
        listCard.Padding = new Thickness(12, 10, 12, 10);
        Grid.SetColumn(listCard, 1);
        main.Children.Add(listCard);
        _runPanel.Children.Add(main);

        _runWatch = Stopwatch.StartNew();
        UpdateClock();
        _clockTimer.Start();
        SelectTab(1);
    }

    /// <summary>Barra em degradê com brilho passando (o brilho some quando as animações estão desligadas).</summary>
    private void GradientBar(ProgressBar bar)
    {
        bar.Style = (Style)FindResource("GradientProgress");
        if (!AppServices.Settings.Animations || !SystemParameters.ClientAreaAnimation)
            bar.Loaded += (_, _) => { if (bar.Template?.FindName("Shine", bar) is UIElement shine) shine.Visibility = Visibility.Collapsed; };
    }

    private static string Clock(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");

    private void UpdateClock()
    {
        if (!_running) return;
        double remaining = 0;
        bool after = _currentStep is null;
        foreach (string step in _runSteps)
        {
            if (!_rows.TryGetValue(step, out var row) || row.Done) continue;
            if (step == _currentStep)
            {
                double left = Math.Max(row.Estimate * (1 - _currentStepPercent / 100), row.Estimate - row.Watch.Elapsed.TotalSeconds);
                remaining += left > 0 ? left : 1;
                after = true;
            }
            else if (after) remaining += row.Estimate;
        }
        _runClock.Text = $"Decorrido {Clock(_runWatch.Elapsed.TotalSeconds)}" + (remaining > 0 ? $"  ·  Restante ~{Clock(remaining)}" : "");
        if (_currentStep is not null && _rows.TryGetValue(_currentStep, out var current)) current.Info.Text = $"{_currentStepPercent:0}% · {Clock(current.Watch.Elapsed.TotalSeconds)}";
    }

    private void ReportRun(ProgressEvent e)
    {
        _overall.Value = Math.Clamp(e.Percent, 0, 100);
        _runPercent.Text = $"{e.Percent:0}%";
        if (!_rows.TryGetValue(e.Step, out var row)) return;
        if (e.Result is { Length: > 0 } result)
        {
            // fim da etapa: o resultado aparece na lista
            if (!row.Done) MarkDone(row);
            row.Info.Text = "✓ " + result;
            row.Info.ToolTip = $"{result} · {Clock(row.Watch.Elapsed.TotalSeconds)}";
            if (_currentStep == e.Step) _currentStep = null;
            return;
        }
        if (row.Done) return;
        if (e.Step != _currentStep)
        {
            if (_currentStep is { } previous && _rows.TryGetValue(previous, out var done)) MarkDone(done);
            _currentStep = e.Step;
            row.Watch.Start();
            row.Name.Foreground = Brush("TextBrush");
            row.Name.FontWeight = FontWeights.SemiBold;
            row.Bar.Visibility = Visibility.Visible;
            row.Card.Background = (Brush)FindResource("GlassSelectedBrush");
            row.Card.BorderBrush = (Brush)FindResource("SideSelectedEdgeBrush");
            int index = Array.IndexOf(_runSteps, e.Step);
            var info = StepInfo.TryGetValue(e.Step, out var i) ? i : ("", "");
            _core.Glyph = info.Item1;
            _core.Caption = $"ETAPA {index + 1} DE {_runSteps.Length}";
            _stepTitle.Text = e.Step;
            _stepDescription.Text = info.Item2;
            bool gpu = BenchmarkService.GpuSteps.Contains(e.Step);
            _windowHint.Visibility = gpu ? Visibility.Visible : Visibility.Collapsed;
            BuildSubtasks(e.Step);
            Motion.Enter(_stepTitle, fromY: 6);
        }
        double stepPercent = e.StepPercent >= 0 ? e.StepPercent : row.Bar.Value;
        _currentStepPercent = stepPercent;
        row.Bar.Value = stepPercent;
        row.Ring.Jump(stepPercent);
        _core.StepPercent = stepPercent;
        _stepDetail.Text = e.Detail is { Length: > 0 } d ? d : e.Message;
        if (e.Passes > 1 && e.Pass > 0)
        {
            _passText.Text = $"Passada {e.Pass} de {e.Passes}";
            _passBadge.Visibility = Visibility.Visible;
        }
        else _passBadge.Visibility = Visibility.Collapsed;
        MarkSubtask(e.Detail);
    }

    /// <summary>Partes da etapa como chips: a que está rodando acende, as já feitas ganham ✓.</summary>
    private void BuildSubtasks(string step)
    {
        _subtasks.Children.Clear();
        IEnumerable<string> names = step switch
        {
            BenchmarkService.StepSingle or BenchmarkService.StepMulti => CpuTasks.Tasks.Select(t => t.Name).Concat(step == BenchmarkService.StepMulti ? new[] { "Vetorial" } : Array.Empty<string>()),
            BenchmarkService.StepMemory => new[] { "Banda", "Latência" },
            BenchmarkService.StepDisk => new[] { "Gravação sequencial", "Leitura sequencial", "Leitura aleatória 4K (1 fila)", "Leitura aleatória 4K (8 filas)", "Gravação aleatória 4K (1 fila)" },
            BenchmarkService.StepAi => new[] { AiBenchmark.PartGenerate, AiBenchmark.PartPrompt, AiBenchmark.PartFp32, AiBenchmark.PartInt8 }.Concat(_runOptions?.Gpu3D == true ? new[] { AiBenchmark.PartGpu } : Array.Empty<string>()),
            BenchmarkService.StepDaily => DailyBenchmark.Tasks.Select(t => t.Name),
            _ => Array.Empty<string>(),
        };
        foreach (string name in names)
            _subtasks.Children.Add(new Border
            {
                Tag = name, CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 3, 9, 4), Margin = new Thickness(0, 0, 6, 6),
                Background = Brush("Surface2Brush"), BorderBrush = Brush("BorderBrush"), BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = name, FontSize = 11, Foreground = Brush("MutedBrush") },
            });
    }

    private void MarkSubtask(string? current)
    {
        if (current is null) return;
        int index = _subtasks.Children.OfType<Border>().ToList().FindIndex(b => (string)b.Tag == current);
        if (index < 0) return;
        var chips = _subtasks.Children.OfType<Border>().ToList();
        for (int i = 0; i < chips.Count; i++)
        {
            var text = (TextBlock)chips[i].Child;
            string name = (string)chips[i].Tag;
            if (i < index) { text.Text = "✓ " + name; text.Foreground = Brush("OkBrush"); chips[i].BorderBrush = Brush("BorderBrush"); chips[i].Background = Brush("Surface2Brush"); }
            else if (i == index) { text.Text = name; text.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)); chips[i].BorderBrush = (Brush)FindResource("AccentGradientBrush"); chips[i].Background = (Brush)FindResource("GlassSelectedBrush"); }
            else { text.Text = name; text.Foreground = Brush("MutedBrush"); chips[i].BorderBrush = Brush("BorderBrush"); chips[i].Background = Brush("Surface2Brush"); }
        }
    }

    private void MarkDone(StepRow row)
    {
        row.Done = true;
        row.Watch.Stop();
        row.Ring.Jump(100);
        row.Bar.Visibility = Visibility.Collapsed;
        row.Name.FontWeight = FontWeights.Normal;
        row.Info.Text = $"✓ {Clock(row.Watch.Elapsed.TotalSeconds)}";
        row.Info.Foreground = Brush("OkBrush");
        row.Card.Background = Brushes.Transparent;
        row.Card.BorderBrush = Brushes.Transparent;
    }

    private void EndRun(string text, string brush)
    {
        if (brush == "OkBrush")
        {
            foreach (var row in _rows.Values.Where(r => !r.Done && r.Watch.IsRunning)) MarkDone(row);
            _overall.Value = 100;
            _runPercent.Text = "100%";
        }
        _currentStep = null;
        _clockTimer.Stop();
        _core.Running = false;
        _core.Glyph = brush == "OkBrush" ? "" : "";
        _core.Caption = brush == "OkBrush" ? "CONCLUÍDO" : "INTERROMPIDO";
        _core.InvalidateVisual();
        _stepTitle.Text = text;
        _stepTitle.Foreground = Brush(brush);
        _stepDescription.Text = "";
        _stepDetail.Text = "";
        _passBadge.Visibility = Visibility.Collapsed;
        _windowHint.Visibility = Visibility.Collapsed;
        _runClock.Text = $"Tempo total {Clock(_runWatch.Elapsed.TotalSeconds)}";
        _running = false;
        foreach (var b in _extraButtons) b.IsEnabled = true;
        UpdateSummary();
    }

    private async Task RunAsync(BenchmarkOptions? only = null, string? title = null)
    {
        if (_running) return;
        var options = only ?? OptionsFor(_mode);
        if (!options.Cpu && !options.Memory && !options.Disk && !options.Gpu3D) return;
        if (options.Disk && SelectedDisk is { } disk && !BenchmarkDisks.HasRoom(disk, BenchmarkModes.For(options).DiskMegabytes))
        {
            SetStatus($"O disco {disk.Letter} está com pouco espaço para o arquivo de teste. Escolha outro disco ou um modo mais leve.", "WarnBrush");
            return;
        }
        string? recording = BeginRecordingIfChecked("benchmark");
        var steps = BenchmarkService.StepsFor(options);
        var profile = BenchmarkModes.For(options);
        int number = BenchmarkHistory.Load().Count + 1;
        string subtitle = $"Modo {profile.Name}" + (profile.Passes > 1 ? $" ({profile.Passes} passadas, mediana)" : "") + $" · {steps.Length} etapas" +
                          (options.Gpu3D ? $" · 3D em {options.GpuWidth}×{options.GpuHeight}" : "") + " · não use o computador durante o teste";
        _stepTitle.Foreground = Brush("TextBrush");
        BeginRun(title ?? $"Teste {number}", subtitle, steps, options);
        var token = Cts!.Token;

        var clock = Stopwatch.StartNew();
        var phases = new List<PhaseMark>();
        string? lastStep = null;
        var recorder = new SensorRecorder();
        using var stopSampling = new CancellationTokenSource();
        IProgress<ProgressEvent> progress = new Progress<ProgressEvent>(e =>
        {
            ReportRun(e);
            if (e.Step != lastStep) { lastStep = e.Step; phases.Add(new PhaseMark(e.Step, clock.Elapsed.TotalSeconds)); }
        });
        var sampler = SampleSensorsAsync(recorder, stopSampling.Token);
        try
        {
            var result = await Task.Run(() => BenchmarkService.Run(progress, token, options));
            stopSampling.Cancel();
            await sampler;
            result = result with { Monitoring = recorder.Tracks(), Phases = phases };
            BenchmarkHistory.Add(result);
            _viewing = result;
            _ = SubmitAndShowAsync(result);
            string done = result.Score > 0 ? $"Pronto! Pontuação {result.Score:N0}." : result.Gpu3DScore > 0 ? $"Pronto! {result.Gpu3DScore:N0} pontos na placa de vídeo." : "Pronto!";
            EndRun(done, "OkBrush");
            _ = FillSideAsync();
            SelectTab(2);
        }
        catch (OperationCanceledException)
        {
            EndRun("Interrompido. Nada foi salvo.", "WarnBrush");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            EndRun("Não foi possível usar o disco escolhido: " + ex.Message, "BadBrush");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            EndRun("Não foi possível concluir: " + ex.Message, "BadBrush");
        }
        finally
        {
            stopSampling.Cancel();
            try { await sampler; } catch (OperationCanceledException) { }
            EndRecording(recording);
        }
    }

    /// <summary>Lê os sensores a cada segundo enquanto o teste roda (gráfico do resultado e a linha "ao vivo").</summary>
    private Task SampleSensorsAsync(SensorRecorder recorder, CancellationToken stop) => Task.Run(async () =>
    {
        try
        {
            var sensors = await AppServices.Sensors;
            while (!stop.IsCancellationRequested)
            {
                var s = sensors.Read();
                recorder.Add(s);
                if (s.Available)
                {
                    var parts = new List<string>();
                    if (s.CpuLoad is { } cl) parts.Add($"CPU {cl:0}%" + (s.CpuTemperature is { } ct ? $" · {ct:0} °C" : ""));
                    if (s.GpuLoad is { } gl) parts.Add($"GPU {gl:0}%" + (s.GpuTemperature is { } gt ? $" · {gt:0} °C" : ""));
                    if (parts.Count > 0)
                    {
                        string text = "Ao vivo: " + string.Join("   |   ", parts);
                        _ = Dispatcher.BeginInvoke(new Action(() => _live.Text = text));
                    }
                }
                await Task.Delay(1000, stop);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is InvalidOperationException or AggregateException) { }
    });

    /// <summary>Só a placa de vídeo: as cenas da janela 3D e o limite por resolução.</summary>
    private Task RunGpuLiveAsync()
    {
        var o = OptionsFor(_mode) with { Cpu = false, Memory = false, Disk = false, Gpu3D = true };
        return RunAsync(o, "Placa de vídeo 3D");
    }

    /// <summary>Teste de renderização da CPU: desenha uma cena por blocos (1 núcleo e depois todos) e mostra a imagem aparecendo.</summary>
    private async Task RunRenderAsync()
    {
        if (_running) return;
        _stepTitle.Foreground = Brush("TextBrush");
        BeginRun("Render da CPU", "A mesma cena desenhada por raios com 1 linha de execução e depois com todas", new[] { "1 núcleo", "Todos os núcleos" }, null);
        var token = Cts!.Token;
        _previewCard.Visibility = Visibility.Visible;
        string? recording = BeginRecordingIfChecked("benchmark-render");
        IProgress<ProgressEvent> report = new Progress<ProgressEvent>(ReportRun);
        try
        {
            NewBitmap(CpuRenderBenchmark.DefaultWidth, CpuRenderBenchmark.DefaultHeight);
            _previewCaption.Text = "Render da CPU: 1 núcleo";
            Action<double> single = p => report.Report(new ProgressEvent("1 núcleo", p / 2, $"{p:0}% da imagem") { StepPercent = p });
            Action<double> multi = p => report.Report(new ProgressEvent("Todos os núcleos", 50 + p / 2, $"{p:0}% da imagem") { StepPercent = p });
            var one = await Task.Run(() => CpuRenderBenchmark.Run(1, 6, token, ShowTile, single));
            NewBitmap(CpuRenderBenchmark.DefaultWidth, CpuRenderBenchmark.DefaultHeight);
            _previewCaption.Text = $"Render da CPU: {Environment.ProcessorCount} linhas de execução";
            var all = await Task.Run(() => CpuRenderBenchmark.Run(Environment.ProcessorCount, 8, token, ShowTile, multi));
            var result = new BenchmarkResult(DateTime.Now, Environment.ProcessorCount, 0, 0, 0, 0, 0, 0,
                Label: string.IsNullOrWhiteSpace(_label.Text) ? null : _label.Text.Trim(), RenderSingleScore: one.Score, RenderMultiScore: all.Score);
            BenchmarkHistory.Add(result);
            _viewing = result;
            EndRun($"1 núcleo {one.Score:N0} pts · todos os núcleos {all.Score:N0} pts ({all.MegaRaysPerSecond:0.0} milhões de raios por segundo).", "OkBrush");
        }
        catch (OperationCanceledException)
        {
            EndRun("Interrompido.", "WarnBrush");
        }
        finally
        {
            EndRecording(recording);
        }
    }
}
