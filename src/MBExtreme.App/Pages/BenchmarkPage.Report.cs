using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MBExtreme.Core.Benchmark;
using MBExtreme.Core.Fps;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Aba Resultados: nota geral com a diferença para o teste anterior, notas por categoria (com peso e a marca do
/// anterior), dia a dia lado a lado, IA, 3D com 1% low e frametime, limite por resolução com gargalo, consumo e fonte,
/// e os detalhes de cada teste com a variação. Botões para comparar com o anterior e salvar como imagem.
/// </summary>
public sealed partial class BenchmarkPage
{
    private static readonly Dictionary<string, (string Glyph, Color Color)> CategoryLook = new()
    {
        ["cpu1"] = ("", Color.FromRgb(0xFF, 0xC4, 0x00)),
        ["cpuN"] = ("", Color.FromRgb(0xFF, 0x9A, 0x1F)),
        ["ram"] = ("", Color.FromRgb(0x4D, 0xD0, 0xFF)),
        ["disk"] = ("", Color.FromRgb(0xFF, 0xB0, 0x40)),
        ["gpu"] = ("", Color.FromRgb(0x3D, 0xDC, 0x97)),
        ["game"] = ("", Color.FromRgb(0xFF, 0x5A, 0x8C)),
        ["ai"] = ("", Color.FromRgb(0x2E, 0xC4, 0xB6)),
        ["daily"] = ("", Color.FromRgb(0xA6, 0xE2, 0x2E)),
    };

    /// <summary>O teste anterior que dá para comparar: o último antes deste, na mesma versão das notas.</summary>
    private static BenchmarkResult? PreviousOf(BenchmarkResult current, IReadOnlyList<BenchmarkResult> history) =>
        history.Where(h => h.When < current.When && h.Version == current.Version && h.Score > 0 && h.Categories is { Count: > 0 }).LastOrDefault();

    private Border Badge(double percent, bool higherIsBetter = true)
    {
        bool same = Math.Abs(percent) < 3;
        bool good = higherIsBetter ? percent > 0 : percent < 0;
        var color = same ? Brush("MutedBrush") : good ? Brush("OkBrush") : Brush("BadBrush");
        return new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 1, 6, 2), VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            Child = new TextBlock { Text = (same ? "– " : percent > 0 ? "↑ " : "↓ ") + $"{percent:+0.0;-0.0;0.0}%", FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = color },
        };
    }

    private void RenderResults()
    {
        _resultsPanel.Children.Clear();
        var history = BenchmarkHistory.Load();
        var last = _viewing is not null ? history.FirstOrDefault(h => h.When == _viewing.When) ?? _viewing : history.LastOrDefault();
        if (last is null)
        {
            _resultsPanel.Children.Add(CardOf(Muted("Nenhum teste feito ainda. Comece em Novo teste.")));
            return;
        }
        var prev = PreviousOf(last, history);
        int number = history.ToList().FindIndex(h => h.When == last.When) + 1;

        // ---- topo: título e botões
        var top = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel();
        titles.Children.Add(new TextBlock { Text = last.Label is { Length: > 0 } l ? l : $"Teste {number}", FontSize = 20, FontWeight = FontWeights.SemiBold });
        titles.Children.Add(Muted($"{last.When:dd/MM/yyyy 'às' HH:mm}" + (last.Mode is { Length: > 0 } m ? $" · Modo {m}" : "") + (last.Passes > 1 ? $" ({last.Passes} passadas)" : "") +
                                  (last.Resolution is { Length: > 0 } r ? $" · 3D em {r}" : ""), 11.5));
        top.Children.Add(titles);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        if (prev is not null)
        {
            var cmp = new Button { Content = "Comparar com o anterior", Style = (Style)FindResource("PrimaryButton") };
            cmp.Click += (_, _) => { SelectTab(4); RenderCompare(prev, last); };
            buttons.Children.Add(cmp);
        }
        var save = new Button { Content = "Salvar imagem", Style = (Style)FindResource("GhostButton"), Margin = new Thickness(8, 0, 0, 0) };
        save.Click += (_, _) => SaveResultsImage(last);
        buttons.Children.Add(save);
        Grid.SetColumn(buttons, 1);
        top.Children.Add(buttons);
        _resultsPanel.Children.Add(top);

        if (prev is not null)
            _resultsPanel.Children.Add(Banner("", $"Comparação com o teste de {prev.When:dd/MM HH:mm} pronta: as setas mostram quanto cada parte mudou.", "AccentBrush"));
        if (last.FocusLost)
            _resultsPanel.Children.Add(Banner("", "Durante o 3D outra janela foi usada: o FPS pode ter ficado abaixo do real. Para um resultado preciso, repita sem mexer no PC.", "WarnBrush"));
        if (last.Version < BenchmarkScoring.Version && last.Score > 0)
            _resultsPanel.Children.Add(Banner("", "Teste de uma versão anterior do benchmark: as notas usam outra escala e não se comparam com as novas.", "MutedBrush"));

        // ---- nota geral
        var cats = last.Categories ?? (last.Version >= BenchmarkScoring.Version ? BenchmarkScoring.For(last) : Array.Empty<CategoryScore>());
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var ringBox = new Grid { Width = 130, Height = 130, Margin = new Thickness(0, 0, 18, 0) };
        var ring = new RingGauge { Thickness = 9, Fill = (Brush)FindResource("AccentGradientBrush") };
        ringBox.Children.Add(ring);
        int shown = last.Score > 0 ? last.Score : last.Gpu3DScore > 0 ? last.Gpu3DScore : last.RenderMultiScore;
        var number2 = new TextBlock { Text = shown.ToString("N0"), FontSize = 28, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center };
        ringBox.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { number2, new TextBlock { Text = "PONTOS", FontSize = 10, Foreground = Brush("MutedBrush"), HorizontalAlignment = HorizontalAlignment.Center } } });
        ring.Loaded += (_, _) => ring.AnimateTo(Math.Min(100, shown / 25.0)); // 2.500 enche o anel
        header.Children.Add(ringBox);
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock { Text = "PONTUAÇÃO GERAL", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brush("MutedBrush") });
        info.Children.Add(Muted(cats.Count > 0 ? $"Média ponderada de {cats.Count} categorias (1.000 = PC de referência)" : "Medição parcial", 11));
        if (prev is not null)
        {
            double delta = BenchmarkScoring.Delta(prev.Score, last.Score);
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 2) };
            line.Children.Add(Badge(delta));
            line.Children.Add(new TextBlock { Text = $"  vs teste de {prev.When:dd/MM HH:mm} ({prev.Score:N0} pts)", FontSize = 11.5, Foreground = Brush("MutedBrush"), VerticalAlignment = VerticalAlignment.Center });
            info.Children.Add(line);
            info.Children.Add(new TextBlock { Text = BenchmarkScoring.Verdict(delta), FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 0) });
        }
        else info.Children.Add(new TextBlock { Text = BenchmarkAdvice.Compare(last, history), FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        Grid.SetColumn(info, 1);
        header.Children.Add(info);
        var sys = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 340 };
        Grid.SetColumn(sys, 2);
        header.Children.Add(sys);
        _ = FillSystemLinesAsync(sys, last);
        var headerCard = CardOf(header);
        headerCard.Background = (Brush)FindResource("GlassSelectedBrush");
        _resultsPanel.Children.Add(headerCard);

        // ---- categorias
        if (cats.Count > 0)
        {
            var grid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 4, 0, 0) };
            foreach (var c in cats)
            {
                var before = prev?.Categories?.FirstOrDefault(x => x.Key == c.Key);
                grid.Children.Add(CategoryCard(c, before, last));
            }
            _resultsPanel.Children.Add(grid);
        }

        if (last.DailyTasks is { Count: > 0 } daily) _resultsPanel.Children.Add(DailyCard(daily, prev?.DailyTasks));
        if (last.AiGenerateTps > 0 || last.AiCpuGflops > 0) _resultsPanel.Children.Add(AiCard(last, prev));
        if (last.GpuStats is { Count: > 0 } stats) _resultsPanel.Children.Add(GpuCard(stats, prev?.GpuStats, last));
        if (last.ResolutionLimit is { Count: > 0 } limit) _resultsPanel.Children.Add(ResolutionCard(limit));
        if (last.Monitoring is { Count: > 0 } tracks)
        {
            _resultsPanel.Children.Add(ThermalCard(BenchmarkThermals.Analyze(tracks)));
            if (PowerCard(tracks) is { } power) _resultsPanel.Children.Add(power);
            _resultsPanel.Children.Add(MonitoringCard(tracks, last.Phases));
        }
        _resultsPanel.Children.Add(DetailsSection(last, prev));
        var scored = history.Where(h => h.Score > 0 && h.Version == last.Version).ToList();
        if (scored.Count > 1) _resultsPanel.Children.Add(RankingCard(scored, last));

        BuildRanking();
        _resultsPanel.Children.Add(CardOf(UiTree.Free(_rankingPanel)));
        Motion.Cascade(_resultsPanel, 25);
    }

    private Border Banner(string glyph, string text, string brush)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily(IconFont), FontSize = 13, Foreground = Brush(brush), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        row.Children.Add(new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        var card = CardOf(row);
        card.Padding = new Thickness(14, 8, 14, 8);
        card.Margin = new Thickness(0, 0, 0, 6);
        card.BorderBrush = Brush(brush);
        return card;
    }

    private async Task FillSystemLinesAsync(Panel panel, BenchmarkResult result)
    {
        void Line(string glyph, string text) => panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1),
            Children =
            {
                new TextBlock { Text = glyph, FontFamily = new FontFamily(IconFont), FontSize = 11, Width = 20, Foreground = Brush("MutedBrush"), VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = text, FontSize = 11, Foreground = Brush("MutedBrush"), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 310 },
            },
        });
        try
        {
            var hw = await AppServices.Hardware();
            Line("", $"{hw.Cpu.Name.Trim()} · {hw.Cpu.Cores} núcleos · {hw.Cpu.Threads} threads");
            Line("", result.GpuName is { Length: > 0 } g && !g.StartsWith("GPU 3D indisponível", StringComparison.Ordinal) ? g : string.Join(" + ", hw.Gpus.Select(x => x.Name)));
            var mem = hw.Memory.FirstOrDefault();
            Line("", $"{hw.TotalMemoryBytes / 1024.0 / 1024 / 1024:0} GB" + (mem is { ConfiguredMhz: > 0 } ? $" {mem.Type} {mem.ConfiguredMhz} MHz · {hw.Memory.Count} pente(s)" : ""));
            if (result.DiskName is { Length: > 0 } d) Line("", "Testado: " + d);
            Line("", $"{hw.Windows.Edition} {hw.Windows.DisplayVersion} (build {hw.Windows.Build})");
            string? plan = await Task.Run(BenchmarkAdvice.ActivePowerPlan);
            if (plan is not null) Line("", plan);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Management.ManagementException or AggregateException) { }
    }

    private Border CategoryCard(CategoryScore c, CategoryScore? before, BenchmarkResult result)
    {
        var look = CategoryLook.TryGetValue(c.Key, out var lk) ? lk : ("", Yellow);
        var stack = new StackPanel();
        var head = new DockPanel();
        var icon = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromArgb(0x30, look.Item2.R, look.Item2.G, look.Item2.B)), Margin = new Thickness(0, 0, 10, 0),
            Child = new TextBlock { Text = look.Item1, FontFamily = new FontFamily(IconFont), FontSize = 13, Foreground = new SolidColorBrush(look.Item2), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        head.Children.Add(icon);
        string extra = c.Key switch
        {
            "gpu" => result.ReflectionsFps > 0 ? $"{result.ReflectionsFps:0} / {result.GeometryFps:0} FPS" : "",
            "game" => result.GameFps > 0 ? $"{result.GameFps:0} FPS" : "",
            "ai" => result.AiGenerateTps > 0 ? $"{result.AiGenerateTps:0.0} tokens/s" : "",
            "daily" => result.DailyTasks is { } t ? $"{t.Sum(x => x.Seconds):0.0} s no total" : "",
            "disk" => result.DiskRandQ8Iops > 0 ? $"{result.DiskRandQ8Iops:N0} IOPS" : "",
            _ => "",
        };
        var right = new TextBlock { Text = extra, FontSize = 10.5, Foreground = new SolidColorBrush(look.Item2), VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(right, Dock.Right);
        head.Children.Insert(0, right);
        head.Children.Add(new StackPanel { Children = { new TextBlock { Text = c.Name, FontSize = 12.5, FontWeight = FontWeights.SemiBold }, new TextBlock { Text = $"peso {c.WeightPercent}%", FontSize = 10, Foreground = Brush("MutedBrush") } } });
        stack.Children.Add(head);
        var scoreLine = new DockPanel { Margin = new Thickness(0, 8, 0, 6) };
        if (before is not null) { var b = Badge(BenchmarkScoring.Delta(before.Score, c.Score)); DockPanel.SetDock(b, Dock.Right); scoreLine.Children.Add(b); }
        scoreLine.Children.Add(new TextBlock { Inlines = { new System.Windows.Documents.Run(c.Score.ToString("N0")) { FontSize = 24, FontWeight = FontWeights.SemiBold }, new System.Windows.Documents.Run(" pts") { FontSize = 11, Foreground = Brush("MutedBrush") } } });
        stack.Children.Add(scoreLine);
        // barra: escala até 1,5x o maior valor; a marca branca é o teste anterior
        double max = Math.Max(c.Score, before?.Score ?? 0) * 1.5;
        var bar = new Grid { Height = 5 };
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(2.5), Background = new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF)) });
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(2.5), HorizontalAlignment = HorizontalAlignment.Left, Background = new LinearGradientBrush(look.Item2, Color.FromArgb(0xFF, (byte)Math.Min(255, look.Item2.R + 30), (byte)(look.Item2.G * 0.7), look.Item2.B), 0) });
        var fill = (Border)bar.Children[1];
        bar.SizeChanged += (_, e) =>
        {
            fill.Width = e.NewSize.Width * c.Score / max;
            foreach (var mk in bar.Children.OfType<System.Windows.Shapes.Rectangle>().ToList()) bar.Children.Remove(mk);
            if (before is not null)
                bar.Children.Add(new System.Windows.Shapes.Rectangle { Width = 2, Height = 9, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(e.NewSize.Width * before.Score / max - 1, -2, 0, -2), ToolTip = $"Anterior: {before.Score:N0} pts" });
        };
        stack.Children.Add(bar);
        var card = CardOf(stack);
        card.Margin = new Thickness(0, 0, 8, 8);
        card.Padding = new Thickness(14, 10, 14, 12);
        return card;
    }

    private Border DailyCard(IReadOnlyList<DailyTask> tasks, IReadOnlyList<DailyTask>? before)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "Uso do dia a dia (pesado)", FontSize = 14, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(Muted("Tempo de cada tarefa (menor é melhor), tudo offline e sem abrir programas" + (before is null ? "" : " · amarelo = este teste, cinza = anterior"), 11.5));
        double max = Math.Max(0.01, tasks.Concat(before ?? Array.Empty<DailyTask>()).Max(t => t.Seconds));
        foreach (var t in tasks)
        {
            var reference = DailyBenchmark.Tasks.FirstOrDefault(x => x.Name == t.Name);
            var prior = before?.FirstOrDefault(x => x.Name == t.Name);
            var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.Children.Add(new TextBlock { Text = t.Name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, ToolTip = reference.Description });
            var bars = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
            bars.Children.Add(ThinBar(t.Seconds / max, (Brush)FindResource("AccentGradientBrush")));
            if (prior is { Seconds: > 0 }) bars.Children.Add(ThinBar(prior.Seconds / max, new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x66))));
            Grid.SetColumn(bars, 1);
            row.Children.Add(bars);
            var value = new TextBlock { Text = t.Seconds > 0 ? $"{t.Seconds:0.00} s" : "não medido", FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
                Foreground = t.Seconds > 0 ? Brush("TextBrush") : Brush("WarnBrush"), ToolTip = t.Note };
            Grid.SetColumn(value, 2);
            row.Children.Add(value);
            if (prior is { Seconds: > 0 } && t.Seconds > 0)
            {
                var badge = Badge(BenchmarkScoring.Delta(prior.Seconds, t.Seconds), higherIsBetter: false);
                badge.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(badge, 3);
                row.Children.Add(badge);
            }
            stack.Children.Add(row);
        }
        double total = tasks.Sum(t => t.Seconds), refTotal = DailyBenchmark.Tasks.Where(x => tasks.Any(t => t.Name == x.Name && t.Seconds > 0)).Sum(x => x.Reference);
        if (total > 0)
            stack.Children.Add(Muted($"No dia a dia este PC é {refTotal / total:0.0}x mais rápido que o PC de referência. Tempo total {total:0.0} s: processador, memória e disco pesam juntos, como no uso de verdade.", 11.5));
        return CardOf(stack);
    }

    private FrameworkElement ThinBar(double fraction, Brush fill)
    {
        var g = new Grid { Height = 5, Margin = new Thickness(0, 1, 0, 1) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Clamp(fraction, 0.005, 1), GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, 1 - fraction), GridUnitType.Star) });
        g.Children.Add(new Border { Background = fill, CornerRadius = new CornerRadius(2.5) });
        return g;
    }

    private UIElement Metric(string title, string value, string unit, string detail, double now = 0, double before = 0, bool higherIsBetter = true)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Brush("MutedBrush") });
        stack.Children.Add(new TextBlock { Inlines = { new System.Windows.Documents.Run(value) { FontSize = 20, FontWeight = FontWeights.SemiBold }, new System.Windows.Documents.Run(" " + unit) { FontSize = 11, Foreground = new SolidColorBrush(Yellow) } } });
        if (detail.Length > 0) stack.Children.Add(new TextBlock { Text = detail, FontSize = 10.5, Foreground = Brush("MutedBrush"), TextWrapping = TextWrapping.Wrap });
        if (before > 0 && now > 0) { var b = Badge(BenchmarkScoring.Delta(before, now), higherIsBetter); b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 4, 0, 0); stack.Children.Add(b); }
        var card = new Border { Child = stack, Background = Brush("Surface2Brush"), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8, 12, 10), Margin = new Thickness(0, 0, 8, 8) };
        return card;
    }

    private Border AiCard(BenchmarkResult r, BenchmarkResult? p)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "Inteligência artificial no seu PC", FontSize = 14, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(Muted("Rede neural de teste com 28 milhões de parâmetros (4 camadas, do mesmo jeito em todo PC). Escrever a resposta depende da memória; ler o pedido depende do cálculo.", 11.5));
        var grid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 8, 0, 0) };
        grid.Children.Add(Metric("Escrevendo a resposta", $"{r.AiGenerateTps:0.0}", "tokens/s", "Um token depois do outro, como o texto aparecendo no chat", r.AiGenerateTps, p?.AiGenerateTps ?? 0));
        grid.Children.Add(Metric("Lendo o seu pedido", $"{r.AiPromptTps:N0}", "tokens/s", "Velocidade para ler o texto que você manda antes de responder", r.AiPromptTps, p?.AiPromptTps ?? 0));
        grid.Children.Add(Metric("Poder de cálculo", $"{r.AiCpuGflops:N0}", "GFLOPS", $"INT8 {r.AiInt8Gops:N0} GOPS" + (r.AiGpuGflops > 0 ? $" · placa de vídeo {r.AiGpuGflops:N0} GFLOPS" : ""), r.AiCpuGflops, p?.AiCpuGflops ?? 0));
        stack.Children.Add(grid);
        var score = r.Categories?.FirstOrDefault(c => c.Key == "ai");
        if (score is not null) stack.Children.Add(Muted($"Este PC roda IA {score.Score / 1000.0:0.0}x mais rápido que o PC de referência. Para IAs maiores, a placa de vídeo e a quantidade de memória (RAM ou da placa) contam muito.", 11.5));
        return CardOf(stack);
    }

    private Border GpuCard(IReadOnlyList<GpuSceneStat> stats, IReadOnlyList<GpuSceneStat>? before, BenchmarkResult r)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "Desempenho 3D e em jogos", FontSize = 14, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(Muted($"Cenas em {r.Resolution ?? "1920×1080"} · FPS médio, 1% low e 0,1% low (média dos piores quadros) · clique numa cena para ver o frametime", 11.5));
        var grid = new UniformGrid { Columns = Math.Min(3, stats.Count), Margin = new Thickness(0, 8, 0, 0) };
        var chartHost = new StackPanel();
        var cards = new List<Border>();
        void ShowChart(GpuSceneStat s, Border card)
        {
            foreach (var c in cards) c.BorderBrush = Brushes.Transparent;
            card.BorderBrush = (Brush)FindResource("AccentGradientBrush");
            chartHost.Children.Clear();
            if (s.FrameMs.Count < 2) return;
            chartHost.Children.Add(new TextBlock { Text = $"Frametime: {s.Scene} (picos = engasgos; linha baixa e reta = jogo fluido)", FontSize = 11.5, Foreground = Brush("MutedBrush"), Margin = new Thickness(0, 4, 0, 4) });
            var series = new[] { (IReadOnlyList<double>)s.FrameMs.ToList() };
            var chart = new LiveChart(s.FrameMs.Count, new[] { Color.FromRgb(0xFF, 0xC4, 0x00) }) { Height = 120, Maximum = Math.Max(1, s.FrameMs.Max() * 1.2), FormatMax = v => $"{v:0.0} ms" };
            chart.Loaded += (_, _) => chart.Update(series);
            chartHost.Children.Add(chart);
        }
        foreach (var s in stats)
        {
            var prior = before?.FirstOrDefault(x => x.Scene == s.Scene);
            var inner = new StackPanel();
            inner.Children.Add(new TextBlock { Text = s.Scene, FontSize = 12.5, FontWeight = FontWeights.SemiBold });
            inner.Children.Add(new TextBlock { Text = s.Scene == Dx12RayBench.OnTitle ? "DirectX 12 · sombras, reflexos e oclusão por raio" : s.Scene == Dx12RayBench.OffTitle ? "DirectX 12 · a mesma cena sem efeitos de raio" : s.Scene.Contains("jogo") ? "Processador + placa de vídeo, como um jogo" : s.Scene.Contains("Geometria") ? "Geometria e vértices" : "Pixel shader pesado (raymarching)", FontSize = 10.5, Foreground = Brush("MutedBrush") });
            inner.Children.Add(new TextBlock { Inlines = { new System.Windows.Documents.Run($"{s.AvgFps:0.0}") { FontSize = 22, FontWeight = FontWeights.SemiBold }, new System.Windows.Documents.Run(" FPS") { FontSize = 11, Foreground = new SolidColorBrush(Yellow) } }, Margin = new Thickness(0, 4, 0, 0) });
            inner.Children.Add(new TextBlock { Text = $"1% low {s.Low1Fps:0.0} · 0,1% low {s.Low01Fps:0.0}", FontSize = 11, Foreground = Brush("MutedBrush") });
            if (prior is not null) { var b = Badge(BenchmarkScoring.Delta(prior.AvgFps, s.AvgFps)); b.HorizontalAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 4, 0, 0); inner.Children.Add(b); }
            var card = new Border { Child = inner, Background = Brush("Surface2Brush"), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8, 12, 10), Margin = new Thickness(0, 0, 8, 8), BorderThickness = new Thickness(1.5), BorderBrush = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
            card.MouseLeftButtonUp += (_, _) => ShowChart(s, card);
            cards.Add(card);
            grid.Children.Add(card);
        }
        stack.Children.Add(grid);
        if (r.StudioMsps > 0) stack.Children.Add(Muted($"Cena 3 · Estúdio (render em blocos): {r.StudioMsps:N0} milhões de amostras por segundo.", 11.5));
        if (r.RtOnFps > 0 && r.RtOffFps > 0)
            stack.Children.Add(Muted($"Ray tracing (DirectX 12): ligar sombras, reflexos e oclusão por raio custa {Math.Clamp((1 - r.RtOnFps / r.RtOffFps) * 100, 0, 100):0}% do FPS ({r.RtOffFps:0.0} → {r.RtOnFps:0.0} FPS).", 11.5));
        else if (r.RtNote is { Length: > 0 } note)
            stack.Children.Add(Muted("Ray tracing (DirectX 12): " + note + ".", 11.5));
        stack.Children.Add(chartHost);
        if (stats.Count > 0 && cards.Count > 0) ShowChart(stats[0], cards[0]);
        return CardOf(stack);
    }

    private Border ResolutionCard(IReadOnlyList<ResolutionFps> list)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "Limite do PC por resolução", FontSize = 14, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(Muted("A simulação de jogo renderizada de verdade em cada resolução · FPS médio e 1% low", 11.5));
        double max = Math.Max(1, list.Max(r => r.Fps));
        foreach (var r in list.OrderBy(x => (long)x.Width * x.Height))
        {
            var row = new Grid { Margin = new Thickness(0, 7, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            string name = r.Height switch { 720 => "HD", 1080 => "Full HD", 1440 => "QHD / 2K", 2160 => "4K Ultra HD", _ => "Nativa" };
            row.Children.Add(new StackPanel { Children = { new TextBlock { Text = r.Label, FontSize = 12, FontWeight = FontWeights.SemiBold }, new TextBlock { Text = name, FontSize = 10, Foreground = Brush("MutedBrush") } } });
            var bar = ThinBar(r.Fps / max, r.Fps >= 60 ? (Brush)FindResource("AccentGradientBrush") : new SolidColorBrush(Color.FromRgb(0x8A, 0x4A, 0x2A)));
            ((FrameworkElement)bar).Height = 8;
            ((FrameworkElement)bar).Margin = new Thickness(8, 0, 12, 0);
            Grid.SetColumn(bar, 1);
            row.Children.Add(bar);
            var fps = new StackPanel { Children = { new TextBlock { Text = $"{r.Fps:0.0} FPS", FontSize = 12.5, FontWeight = FontWeights.SemiBold }, new TextBlock { Text = r.Low1Fps > 0 ? $"1% low {r.Low1Fps:0.0}" : "", FontSize = 10, Foreground = Brush("MutedBrush") } } };
            Grid.SetColumn(fps, 2);
            row.Children.Add(fps);
            var tier = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 2, 8, 3), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(r.Fps >= 144 ? Color.FromArgb(0x40, 0xFF, 0xC4, 0x00) : r.Fps >= 60 ? Color.FromArgb(0x40, 0x3D, 0xDC, 0x97) : r.Fps >= 30 ? Color.FromArgb(0x40, 0xFF, 0xA9, 0x4D) : Color.FromArgb(0x40, 0xFF, 0x5A, 0x5F)),
                Child = new TextBlock { Text = r.Tier, FontSize = 10.5, FontWeight = FontWeights.SemiBold } };
            Grid.SetColumn(tier, 3);
            row.Children.Add(tier);
            stack.Children.Add(row);
        }
        var ordered = list.OrderBy(x => (long)x.Width * x.Height).ToList();
        var best = ordered.Where(r => r.Fps >= 60).LastOrDefault();
        string first = best is null ? "Nenhuma resolução passou de 60 FPS nesta cena pesada." : $"Roda fluido (60+ FPS) até {best.Label}.";
        string bottleneck = "";
        if (ordered.Count >= 2)
        {
            double drop = 1 - ordered[1].Fps / Math.Max(0.01, ordered[0].Fps);
            bottleneck = drop < 0.12
                ? " Gargalo: PROCESSADOR. Subir a resolução quase não muda o FPS; um processador mais rápido (ou o Windows otimizado) traz mais FPS."
                : " Gargalo: PLACA DE VÍDEO. O FPS cai quando a resolução sobe; para mais FPS, baixe a resolução ou a qualidade gráfica.";
        }
        var note = new Border { Background = Brush("Surface2Brush"), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 10, 0, 0),
            Child = new TextBlock { Text = first + bottleneck, FontSize = 12, TextWrapping = TextWrapping.Wrap } };
        stack.Children.Add(note);
        return CardOf(stack);
    }

    /// <summary>Consumo medido pelos sensores durante o teste e a fonte recomendada.</summary>
    /// <summary>Temperatura e calor: picos da CPU e da GPU e se o clock caiu do começo para o fim da carga (o calor segurando o desempenho).</summary>
    private Border ThermalCard(ThermalReport r)
    {
        string brush = r.Level switch { ThermalLevel.Hot => "BadBrush", ThermalLevel.Warm => "WarnBrush", ThermalLevel.Ok => "OkBrush", _ => "MutedBrush" };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "Temperatura e calor", FontSize = 14, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(new TextBlock { Text = r.Title, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brush(brush), Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
        var grid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 8, 0, 0) };
        if (r.CpuPeak is { } cpu) grid.Children.Add(Metric("Processador", $"{cpu:0}", "°C no pico", cpu >= BenchmarkThermals.CpuHot ? "no limite" : cpu >= BenchmarkThermals.CpuWarm ? "quente" : "bom"));
        if (r.GpuPeak is { } gpu) grid.Children.Add(Metric("Placa de vídeo", $"{gpu:0}", "°C no pico", gpu >= BenchmarkThermals.GpuHot ? "no limite" : gpu >= BenchmarkThermals.GpuWarm ? "quente" : "bom"));
        if (r.CpuClockDropPercent is { } cd) grid.Children.Add(Metric("Clock da CPU", $"-{cd:0}", "% do começo ao fim", cd >= BenchmarkThermals.DropAlert ? "perdeu velocidade" : "estável"));
        if (r.GpuClockDropPercent is { } gd) grid.Children.Add(Metric("Clock da GPU", $"-{gd:0}", "% do começo ao fim", gd >= BenchmarkThermals.DropAlert ? "perdeu velocidade" : "estável"));
        if (grid.Children.Count > 0) stack.Children.Add(grid);
        foreach (string note in r.Notes)
            stack.Children.Add(new TextBlock { Text = "•  " + note, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        if (r.Level != ThermalLevel.Unknown)
            stack.Children.Add(Muted("Dica: os modos Padrão e Preciso seguram a carga por vários minutos justamente para mostrar se o calor derruba o desempenho.", 11));
        return CardOf(stack);
    }

    private Border? PowerCard(IReadOnlyList<SensorTrack> tracks)
    {
        var cpu = tracks.FirstOrDefault(t => t.Name == "Potência da CPU");
        var gpu = tracks.FirstOrDefault(t => t.Name == "Potência da GPU");
        if (cpu is null && gpu is null) return null;
        var (peak, psu) = PowerAdvice.Estimate(cpu?.Max ?? 0, gpu?.Max ?? 0);
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "Consumo de energia e fonte", FontSize = 14, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(Muted("Medido pelos sensores durante o teste (o processador e a placa de vídeo informam o próprio consumo).", 11.5));
        var grid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 8, 0, 0) };
        if (cpu is not null) grid.Children.Add(Metric("Processador", $"{cpu.Max:0}", "W no pico", $"média {cpu.Values.Average():0} W"));
        if (gpu is not null) grid.Children.Add(Metric("Placa de vídeo", $"{gpu.Max:0}", "W no pico", $"média {gpu.Values.Average():0} W"));
        grid.Children.Add(Metric("Fonte recomendada", $"{psu}", "W", $"pico estimado do PC: {peak} W (com placa-mãe, memória, discos e ventoinhas) + 40% de folga"));
        stack.Children.Add(grid);
        return CardOf(stack);
    }

    /// <summary>Detalhes de cada teste em duas colunas, com a variação para o teste anterior.</summary>
    private UIElement DetailsSection(BenchmarkResult r, BenchmarkResult? p)
    {
        var outer = new StackPanel();
        outer.Children.Add(new TextBlock { Text = "Detalhes de cada teste" + (p is null ? "" : $"  ·  comparado com {p.When:dd/MM HH:mm}"), FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 8) });
        var left = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
        var right = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
        var cols = new Grid();
        cols.ColumnDefinitions.Add(new ColumnDefinition());
        cols.ColumnDefinitions.Add(new ColumnDefinition());
        cols.Children.Add(left);
        Grid.SetColumn(right, 1);
        cols.Children.Add(right);
        outer.Children.Add(cols);
        int n = 0;
        void Section(string key, string title, IEnumerable<(string Name, double Now, double Before, string Format, string Unit, bool Higher)> rows)
        {
            var list = rows.Where(x => x.Now > 0).ToList();
            if (list.Count == 0) return;
            var look = CategoryLook.TryGetValue(key, out var lk) ? lk.Color : Yellow;
            var stack = new StackPanel();
            var head = new DockPanel();
            var score = r.Categories?.FirstOrDefault(c => c.Key == key);
            if (score is not null) { var s = new TextBlock { Text = $"{score.Score:N0} pts", FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(look) }; DockPanel.SetDock(s, Dock.Right); head.Children.Add(s); }
            head.Children.Add(new TextBlock { Inlines = { new System.Windows.Documents.Run("● ") { Foreground = new SolidColorBrush(look) }, new System.Windows.Documents.Run(title) { FontWeight = FontWeights.SemiBold } }, FontSize = 13 });
            stack.Children.Add(head);
            foreach (var row in list)
            {
                var line = new Grid { Margin = new Thickness(0, 4, 0, 0) };
                line.ColumnDefinitions.Add(new ColumnDefinition());
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
                line.Children.Add(new TextBlock { Text = row.Name, FontSize = 11.5 });
                var value = new TextBlock { Text = row.Now.ToString(row.Format) + " " + row.Unit, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 8, 0) };
                Grid.SetColumn(value, 1);
                line.Children.Add(value);
                if (row.Before > 0) { var b = Badge(BenchmarkScoring.Delta(row.Before, row.Now), row.Higher); b.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(b, 2); line.Children.Add(b); }
                stack.Children.Add(line);
            }
            var card = CardOf(stack);
            card.Padding = new Thickness(14, 10, 14, 12);
            (n++ % 2 == 0 ? left : right).Children.Add(card);
        }

        double Rate(IReadOnlyList<CpuTaskResult>? l, string name) => l?.FirstOrDefault(t => t.Name == name)?.Rate ?? 0;
        Section("cpu1", "Processador · 1 núcleo", CpuTasks.Tasks.Select(t => (t.Name, Rate(r.CpuTasksSingle, t.Name), Rate(p?.CpuTasksSingle, t.Name), "N1", t.Unit, true)));
        Section("cpuN", "Processador · todos os núcleos", CpuTasks.Tasks.Select(t => (t.Name, Rate(r.CpuTasksMulti, t.Name), Rate(p?.CpuTasksMulti, t.Name), "N1", t.Unit, true))
            .Append(("Ganho com todos os núcleos", Rate(r.CpuTasksMulti, "Cálculo") / Math.Max(0.001, Rate(r.CpuTasksSingle, "Cálculo")), p is null ? 0 : Rate(p.CpuTasksMulti, "Cálculo") / Math.Max(0.001, Rate(p.CpuTasksSingle, "Cálculo")), "0.00", "x", true))
            .Append(("Vetorial (SIMD)", r.CpuSimdGflops, p?.CpuSimdGflops ?? 0, "N1", "GFLOPS", true)));
        Section("ram", "Memória RAM", new[]
        {
            ("Leitura", r.MemoryReadGBps, p?.MemoryReadGBps ?? 0, "N2", "GB/s", true), ("Escrita", r.MemoryWriteGBps, p?.MemoryWriteGBps ?? 0, "N2", "GB/s", true),
            ("Cópia", r.MemoryGBps, p?.MemoryGBps ?? 0, "N2", "GB/s", true), ("Latência", r.MemoryLatencyNs, p?.MemoryLatencyNs ?? 0, "N1", "ns", false),
        });
        Section("disk", "Armazenamento" + (r.DiskName is { Length: > 0 } dn ? $" ({dn})" : ""), new[]
        {
            ("Leitura sequencial", r.DiskReadMBps, p?.DiskReadMBps ?? 0, "N0", "MB/s", true), ("Gravação sequencial", r.DiskWriteMBps, p?.DiskWriteMBps ?? 0, "N0", "MB/s", true),
            ("Leitura aleatória 4K (1 fila)", r.DiskRandReadMBps, p?.DiskRandReadMBps ?? 0, "N2", "MB/s", true), ("Leitura aleatória 4K (8 filas)", r.DiskRandQ8Iops, p?.DiskRandQ8Iops ?? 0, "N0", "IOPS", true),
            ("Gravação aleatória 4K (1 fila)", r.DiskRandWriteMBps, p?.DiskRandWriteMBps ?? 0, "N2", "MB/s", true), ("Tempo de acesso", r.DiskAccessUs, p?.DiskAccessUs ?? 0, "N0", "µs", false),
        });
        GpuSceneStat? S(IReadOnlyList<GpuSceneStat>? l, string scene) => l?.FirstOrDefault(x => x.Scene == scene);
        var gpuRows = new List<(string, double, double, string, string, bool)>();
        foreach (var scene in new[] { GpuWindowBench.Reflections, GpuWindowBench.Geometry })
        {
            var a = S(r.GpuStats, scene); var b = S(p?.GpuStats, scene);
            if (a is null) continue;
            gpuRows.Add(($"{scene} · FPS médio", a.AvgFps, b?.AvgFps ?? 0, "N1", "FPS", true));
            gpuRows.Add(($"{scene} · 1% low", a.Low1Fps, b?.Low1Fps ?? 0, "N1", "FPS", true));
            gpuRows.Add(($"{scene} · 0,1% low", a.Low01Fps, b?.Low01Fps ?? 0, "N1", "FPS", true));
        }
        gpuRows.Add(("Cena 3 · Estúdio (render em blocos)", r.StudioMsps, p?.StudioMsps ?? 0, "N0", "M amostras/s", true));
        foreach (var scene in new[] { Dx12RayBench.OnTitle, Dx12RayBench.OffTitle })
        {
            var a = S(r.GpuStats, scene); var b = S(p?.GpuStats, scene);
            if (a is null) continue;
            gpuRows.Add(($"{scene} (DX12) · FPS médio", a.AvgFps, b?.AvgFps ?? 0, "N1", "FPS", true));
            gpuRows.Add(($"{scene} (DX12) · 1% low", a.Low1Fps, b?.Low1Fps ?? 0, "N1", "FPS", true));
        }
        Section("gpu", "Placa de vídeo (3D)", gpuRows);
        var game = S(r.GpuStats, GpuWindowBench.Game); var gameBefore = S(p?.GpuStats, GpuWindowBench.Game);
        var gameRows = new List<(string, double, double, string, string, bool)>();
        if (game is not null)
        {
            gameRows.Add(("Simulação de jogo · FPS médio", game.AvgFps, gameBefore?.AvgFps ?? 0, "N1", "FPS", true));
            gameRows.Add(("Simulação de jogo · 1% low", game.Low1Fps, gameBefore?.Low1Fps ?? 0, "N1", "FPS", true));
            gameRows.Add(("Simulação de jogo · 0,1% low", game.Low01Fps, gameBefore?.Low01Fps ?? 0, "N1", "FPS", true));
        }
        foreach (var res in r.ResolutionLimit ?? Array.Empty<ResolutionFps>())
            gameRows.Add(($"FPS em {res.Label}", res.Fps, p?.ResolutionLimit?.FirstOrDefault(x => x.Label == res.Label)?.Fps ?? 0, "N1", "FPS", true));
        Section("game", "Simulação de jogo", gameRows);
        Section("ai", "Inteligência artificial", new[]
        {
            ("Escrevendo a resposta", r.AiGenerateTps, p?.AiGenerateTps ?? 0, "N1", "tokens/s", true), ("Lendo o pedido", r.AiPromptTps, p?.AiPromptTps ?? 0, "N0", "tokens/s", true),
            ("Cálculo FP32 (todos os núcleos)", r.AiCpuGflops, p?.AiCpuGflops ?? 0, "N1", "GFLOPS", true), ("Modelo quantizado INT8", r.AiInt8Gops, p?.AiInt8Gops ?? 0, "N1", "GOPS", true),
            ("Na placa de vídeo", r.AiGpuGflops, p?.AiGpuGflops ?? 0, "N0", "GFLOPS", true),
        });
        Section("daily", "Uso do dia a dia (pesado)", (r.DailyTasks ?? Array.Empty<DailyTask>()).Select(t => (t.Name, t.Seconds, p?.DailyTasks?.FirstOrDefault(x => x.Name == t.Name)?.Seconds ?? 0, "N2", "s", false))
            .Append(("Tempo total", r.DailyTasks?.Sum(t => t.Seconds) ?? 0, p?.DailyTasks?.Sum(t => t.Seconds) ?? 0, "N1", "s", false)));
        return outer;
    }

    /// <summary>Salva a aba Resultados como imagem em Imagens\MB Extreme.</summary>
    private void SaveResultsImage(BenchmarkResult result)
    {
        try
        {
            double w = _resultsPanel.ActualWidth, h = _resultsPanel.ActualHeight;
            if (w < 10 || h < 10) return;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x10, 0x11, 0x14)), null, new Rect(0, 0, w + 32, h + 32));
                dc.DrawRectangle(new VisualBrush(_resultsPanel), null, new Rect(16, 16, w, h));
            }
            var bmp = new RenderTargetBitmap((int)((w + 32) * 1.5), (int)((h + 32) * 1.5), 144, 144, PixelFormats.Pbgra32);
            bmp.Render(visual);
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "MB Extreme");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, $"benchmark-{result.When:yyyy-MM-dd-HHmm}.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using (var stream = File.Create(file)) encoder.Save(stream);
            SetStatus("Imagem salva em " + file, "OkBrush");
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception) { }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OutOfMemoryException)
        {
            SetStatus("Não foi possível salvar a imagem: " + ex.Message, "BadBrush");
        }
    }
}
