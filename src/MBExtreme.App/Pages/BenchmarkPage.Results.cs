using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MBExtreme.Core.Benchmark;
using MBExtreme.Core.Fps;
using MBExtreme.Core.Games;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>Abas Resultados, Histórico e Comparar, o painel lateral e o ranking anônimo.</summary>
public sealed partial class BenchmarkPage
{
    private static readonly Color Orange = Color.FromRgb(0xFF, 0x9A, 0x1F);
    private static readonly Color Yellow = Color.FromRgb(0xFF, 0xC4, 0x00);
    private readonly StackPanel _rankingPanel = new();
    private static string _rankingNote = "";

    protected override void OnProgressClosed()
    {
        if (_tab == 2) RenderResults();
    }

    // ================================================================== painel lateral

    private async Task FillSideAsync()
    {
        _side.Children.Clear();
        var history = BenchmarkHistory.Load();
        var last = _viewing ?? history.LastOrDefault();

        // ---- pontuação e comparação automática
        var score = new StackPanel();
        score.Children.Add(SideTitle("", "Pontuação"));
        var ringBox = new Grid { Width = 150, Height = 150, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 6) };
        var ring = new RingGauge { Thickness = 9, Fill = (Brush)FindResource("AccentGradientBrush") };
        ringBox.Children.Add(ring);
        var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        int shown = last is null ? 0 : last.Score > 0 ? last.Score : last.Gpu3DScore > 0 ? last.Gpu3DScore : last.RenderMultiScore;
        var number = new TextBlock { Text = last is null ? "—" : shown.ToString("N0"), FontSize = 30, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center };
        center.Children.Add(number);
        center.Children.Add(new TextBlock { Text = last is null ? "sem medição" : last.Score > 0 ? "pontos" : last.Gpu3DScore > 0 ? "pontos da GPU" : "pts de render", FontSize = 11, Foreground = Brush("MutedBrush"), HorizontalAlignment = HorizontalAlignment.Center });
        ringBox.Children.Add(center);
        score.Children.Add(ringBox);
        if (last is not null)
        {
            var same = history.Where(h => h.Score > 0).ToList();
            double fill = last.Score > 0 && same.Count > 0 ? 100.0 * last.Score / same.Max(h => h.Score) : 100;
            ring.Loaded += (_, _) => ring.AnimateTo(fill);
            if (last.Score > 0) number.Loaded += (_, _) => Motion.CountUp(number, 0, last.Score, "N0");
            score.Children.Add(new TextBlock { Text = "Comparação automática", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 2) });
            score.Children.Add(Muted(BenchmarkAdvice.Compare(last, history), 11.5));
            score.Children.Add(Muted($"{last.When:dd/MM/yyyy HH:mm}" + (last.Mode is { Length: > 0 } m ? $" · modo {m}" : "") + (last.Resolution is { Length: > 0 } r ? $" · 3D em {r}" : ""), 10.5));
        }
        else score.Children.Add(Muted("Faça o primeiro teste para ver a nota aqui.", 11.5));
        _side.Children.Add(SideCard(score));

        // ---- seu sistema
        var system = new StackPanel();
        system.Children.Add(SideTitle("", "Seu sistema"));
        var systemRows = new StackPanel();
        system.Children.Add(systemRows);
        _side.Children.Add(SideCard(system));

        // ---- dicas
        var tips = new StackPanel();
        tips.Children.Add(SideTitle("", "Dicas"));
        var tipRows = new StackPanel();
        tips.Children.Add(tipRows);
        _side.Children.Add(SideCard(tips));
        Motion.Cascade(_side, 60);

        string? plan = await Task.Run(BenchmarkAdvice.ActivePowerPlan);
        bool? battery = NativeDisplay.OnBattery();

        bool laptop = false;
        try
        {
            var hw = await AppServices.Hardware();
            laptop = hw.IsLaptop;
            SideRow(systemRows, "", "Processador", $"{hw.Cpu.Name.Trim()}", $"{hw.Cpu.Cores} núcleos · {hw.Cpu.Threads} threads" + (hw.Cpu.MaxMhz > 0 ? $" · até {hw.Cpu.MaxMhz / 1000.0:0.0#} GHz" : ""));
            foreach (var g in hw.Gpus.Take(2))
                SideRow(systemRows, "", "Placa de vídeo", g.Name, g.VramBytes > 0 ? $"{g.VramBytes / 1024.0 / 1024 / 1024:0.#} GB de memória" : "");
            var mem = hw.Memory.FirstOrDefault();
            SideRow(systemRows, "", "Memória", $"{hw.TotalMemoryBytes / 1024.0 / 1024 / 1024:0.#} GB" + (mem is { Type.Length: > 0 } ? $" {mem.Type}" : ""),
                mem is { ConfiguredMhz: > 0 } ? $"{mem.ConfiguredMhz} MHz · {hw.Memory.Count} pente(s)" : $"{hw.Memory.Count} pente(s)");
            if (_disks.Count > 0)
                SideRow(systemRows, "", "Armazenamento", string.Join(" · ", _disks.Take(3).Select(d => $"{d.Letter} {Gb(d.TotalBytes)}")), $"{Gb(_disks.Sum(d => d.FreeBytes))} livres no total");
            SideRow(systemRows, "", "Sistema", $"{hw.Windows.Edition}", $"{hw.Windows.DisplayVersion} · build {hw.Windows.Build}");
            SideRow(systemRows, "", "Energia", plan ?? "Plano de energia não lido",
                battery == true ? "Na bateria" : battery == false ? (laptop ? "Na tomada" : "Ligado na tomada") : "");
            AddStanding(systemRows, hw);
            var advice = GameAdvisor.Recommend(hw);
            systemRows.Children.Add(new TextBlock { Text = $"Nível para jogos: {advice.Tier}", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Yellow), Margin = new Thickness(0, 8, 0, 0) });
            systemRows.Children.Add(Muted(advice.Headline + " (estimativa pelo hardware)", 10.5));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Management.ManagementException or AggregateException)
        {
            systemRows.Children.Add(Muted("Não foi possível ler os dados do sistema.", 11.5));
        }

        int windows = 0;
        try { windows = System.Diagnostics.Process.GetProcesses().Count(p => { try { return p.MainWindowHandle != IntPtr.Zero && p.ProcessName != "MBExtreme"; } catch (InvalidOperationException) { return false; } }); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        foreach (var tip in BenchmarkAdvice.Tips(laptop, battery, plan, _mode, windows))
        {
            var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new TextBlock { Text = tip.Important ? "" : "", FontFamily = new FontFamily(IconFont), FontSize = 12, Margin = new Thickness(0, 2, 0, 0),
                Foreground = tip.Important ? Brush("WarnBrush") : Brush("OkBrush") });
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = tip.Title, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            text.Children.Add(new TextBlock { Text = tip.Detail, FontSize = 11, Foreground = Brush("MutedBrush"), TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            tipRows.Children.Add(row);
        }
    }

    private Border SideCard(UIElement child)
    {
        var card = CardOf(child);
        card.Padding = new Thickness(16, 12, 16, 14);
        card.Margin = new Thickness(0, 0, 0, 10);
        return card;
    }

    private FrameworkElement SideTitle(string glyph, string title)
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        head.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily(IconFont), FontSize = 13, Foreground = (Brush)FindResource("AccentGradientBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        head.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        return head;
    }

    private void SideRow(Panel host, string glyph, string title, string value, string detail)
    {
        var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily(IconFont), FontSize = 14, Foreground = Brush("MutedBrush"), Margin = new Thickness(0, 2, 0, 0) });
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = title, FontSize = 10.5, Foreground = Brush("MutedBrush") });
        text.Children.Add(new TextBlock { Text = value, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold });
        if (detail.Length > 0) text.Children.Add(new TextBlock { Text = detail, FontSize = 11, Foreground = Brush("MutedBrush"), TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        host.Children.Add(row);
    }

    private static HardwareIndex? _index;
    private static bool _indexLoaded;

    /// <summary>Posição do seu processador e da sua placa entre os modelos do Blender Open Data (se o índice veio junto do app).</summary>
    private void AddStanding(Panel panel, MBExtreme.Core.Hardware.HardwareInventory hw)
    {
        if (!_indexLoaded)
        {
            _indexLoaded = true;
            _index = HardwareIndex.Load(Path.Combine(AppContext.BaseDirectory, "hardware-index.json"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "hardware-index.json"));
        }
        if (_index is null) return;

        var lines = new List<string>();
        if (_index.Cpu(hw.Cpu.Name) is { } cpu) lines.Add($"Processador mais rápido que {cpu.BeatsPercent:0}% dos {cpu.Models} modelos");
        var gpu = hw.Gpus.Select(g => _index.Gpu(g.Name)).FirstOrDefault(x => x is not null);
        if (gpu is not null) lines.Add($"Placa de vídeo mais rápida que {gpu.BeatsPercent:0}% dos {gpu.Models} modelos");
        if (lines.Count == 0) return;
        panel.Children.Add(new TextBlock { Text = "Entre os modelos (Blender Open Data)", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        foreach (var line in lines) panel.Children.Add(new TextBlock { Text = line, FontSize = 11.5, Foreground = new SolidColorBrush(Yellow), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(Muted($"Medido em renderização 3D (CC0, {_index.Data.GeneratedAt:dd/MM/yyyy}); referência, não nota de jogos.", 10));
    }

    // ================================================================== Resultados

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

        var scored = history.Where(h => h.Score > 0).ToList();
        int shown = last.Score > 0 ? last.Score : last.Gpu3DScore > 0 ? last.Gpu3DScore : last.RenderMultiScore;

        // ---- cabeçalho com a nota
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        var badge = new Border { Width = 120, Height = 120, CornerRadius = new CornerRadius(60), BorderThickness = new Thickness(7), BorderBrush = (Brush)FindResource("AccentGradientBrush"), Margin = new Thickness(0, 0, 18, 0) };
        var badgeText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        badgeText.Children.Add(new TextBlock { Text = shown.ToString("N0"), FontSize = 28, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center });
        badgeText.Children.Add(new TextBlock { Text = last.Score > 0 ? "pontos" : last.Gpu3DScore > 0 ? "pts da GPU" : "pts de render", FontSize = 11, Foreground = Brush("MutedBrush"), HorizontalAlignment = HorizontalAlignment.Center });
        badge.Child = badgeText;
        header.Children.Add(badge);
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock { Text = BenchmarkAdvice.Compare(last, history), FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        info.Children.Add(Muted($"{last.When:dd/MM/yyyy HH:mm} · {last.Threads} threads" + (last.Mode is { Length: > 0 } m ? $" · modo {m}" : "") +
                                (last.Resolution is { Length: > 0 } r ? $" · 3D em {r}" : "") + (last.DiskName is { Length: > 0 } d ? $" · disco {d}" : "") +
                                (last.Label is { Length: > 0 } l ? $" · {l}" : "")));
        if (scored.Count > 0)
        {
            var stats = new UniformGrid { Columns = 3, Margin = new Thickness(0, 10, 0, 0) };
            void Stat(string title, string value, string color) => stats.Children.Add(new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = title, FontSize = 11.5, Foreground = Brush("MutedBrush") },
                    new TextBlock { Text = value, FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = Brush(color) },
                },
            });
            Stat("Esta medição", last.Score > 0 ? last.Score.ToString("N0") : "—", "TextBrush");
            Stat("Média das suas", ((int)scored.Average(h => h.Score)).ToString("N0"), "MutedBrush");
            Stat("Melhor das suas", scored.Max(h => h.Score).ToString("N0"), "OkBrush");
            info.Children.Add(stats);
        }
        Grid.SetColumn(info, 1);
        header.Children.Add(info);
        _resultsPanel.Children.Add(CardOf(header));

        // ---- notas por parte, com barra em relação ao seu melhor
        var parts = new UniformGrid { Columns = 3 };
        void Part(string glyph, string title, string value, string detail, double now, Func<BenchmarkResult, double> pick)
        {
            double best = Math.Max(now, history.Select(pick).DefaultIfEmpty(0).Max());
            var stack = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily(IconFont), FontSize = 12, Foreground = Brush("MutedBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            head.Children.Add(new TextBlock { Text = title, FontSize = 11.5, Foreground = Brush("MutedBrush") });
            stack.Children.Add(head);
            stack.Children.Add(new TextBlock { Text = value, FontSize = 21, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Yellow) });
            if (detail.Length > 0) stack.Children.Add(new TextBlock { Text = detail, FontSize = 10.5, Foreground = Brush("MutedBrush"), TextWrapping = TextWrapping.Wrap });
            var bar = new ProgressBar { Height = 4, Maximum = 100, Value = best > 0 ? 100 * now / best : 0, Margin = new Thickness(0, 6, 0, 0), ToolTip = "Em relação ao seu melhor resultado nesta parte" };
            stack.Children.Add(bar);
            var card = CardOf(stack);
            card.Margin = new Thickness(0, 0, 8, 8);
            card.Padding = new Thickness(12, 9, 12, 9);
            parts.Children.Add(card);
        }
        if (last.CpuSingleMops > 0) Part("", "CPU, 1 núcleo", $"{last.CpuSingleMops:0.#}", "milhões de operações por segundo", last.CpuSingleMops, h => h.CpuSingleMops);
        if (last.CpuMultiMops > 0) Part("", "CPU, todos os núcleos", $"{last.CpuMultiMops:0.#}", $"{last.Threads} linhas de execução", last.CpuMultiMops, h => h.CpuMultiMops);
        if (last.RenderMultiScore > 0) Part("", "Render da CPU", $"{last.RenderMultiScore:N0} pts", $"1 núcleo: {last.RenderSingleScore:N0} pts", last.RenderMultiScore, h => h.RenderMultiScore);
        if (last.MemoryGBps > 0) Part("", "Memória", $"{last.MemoryGBps:0.#} GB/s", last.MemoryLatencyNs > 0 ? $"latência {last.MemoryLatencyNs:0} ns" : "", last.MemoryGBps, h => h.MemoryGBps);
        if (last.DiskWriteMBps > 0) Part("", "Disco" + (last.DiskName is { Length: > 0 } dn ? $" {dn}" : ""), $"{last.DiskWriteMBps:0} MB/s", $"gravação · leitura {last.DiskReadMBps:0} MB/s", last.DiskWriteMBps, h => h.DiskWriteMBps);
        if (last.Gpu3DScore > 0) Part("", "GPU 3D", last.Gpu3DScore.ToString("N0"), last.GpuName ?? "", last.Gpu3DScore, h => h.Gpu3DScore);
        var studio = last.GpuScenes?.FirstOrDefault(s => s.Unit == "M amostras/s");
        if (studio is not null) Part("", "Render 3D do estúdio", $"{studio.Fps:N0}", "milhões de amostras por segundo (path tracing)", studio.Fps,
            h => h.GpuScenes?.FirstOrDefault(s => s.Unit == "M amostras/s")?.Fps ?? 0);
        if (parts.Children.Count > 0) _resultsPanel.Children.Add(parts);

        if (last.Monitoring is { Count: > 0 } tracks) _resultsPanel.Children.Add(MonitoringCard(tracks, last.Phases));
        if (scored.Count > 1) _resultsPanel.Children.Add(RankingCard(scored, last));

        _resultsPanel.Children.Add(Heading("Detalhes"));
        if (last.CpuSimdGflops > 0)
            _resultsPanel.Children.Add(Row("CPU vetorial, criptografia e compressão",
                $"{last.CpuSimdGflops:0.0} GFLOPS · AES {last.CryptoMBps:0} MB/s · compressão {last.CompressMBps:0} MB/s"));
        if (last.MemoryGBps > 0)
            _resultsPanel.Children.Add(Row("Memória",
                $"Leitura {Fmt(last.MemoryReadGBps)} · escrita {Fmt(last.MemoryWriteGBps)} · cópia {last.MemoryGBps:0.0} GB/s" +
                (last.MemoryLatencyNs > 0 ? $"\nLatência {last.MemoryLatencyNs:0} ns (acesso aleatório em memória grande)" : "")));
        if (last.LatencyCurve is { Count: > 0 } curve)
            _resultsPanel.Children.Add(Row("Curva de latência (cache e RAM)",
                string.Join("  ·  ", curve.Select(p => $"{(p.SizeKb >= 1024 ? p.SizeKb / 1024 + " MB" : p.SizeKb + " KB")}: {p.Nanoseconds:0.0} ns"))));
        if (last.DiskWriteMBps > 0)
            _resultsPanel.Children.Add(Row("Disco" + (last.DiskName is { Length: > 0 } dk ? $" {dk}" : ""), $"Gravação {last.DiskWriteMBps:0} MB/s · leitura {last.DiskReadMBps:0} MB/s (a leitura pode vir do cache do Windows)"));
        if (last.Gpu3DScore > 0 && last.GpuScenes is { Count: > 0 } sc)
            _resultsPanel.Children.Add(Row($"GPU 3D: {last.GpuName}" + (last.Resolution is { Length: > 0 } rs ? $" em {rs}" : ""), string.Join(" · ", sc.Select(x => $"{x.Scene} {x.Display}"))));
        else if (last.Gpu3DScore <= 0 && last.GpuName is { Length: > 0 } gpuNote)
            _resultsPanel.Children.Add(Row("GPU 3D", gpuNote, null, "WarnBrush"));

        BuildRanking();
        _resultsPanel.Children.Add(CardOf(Free(_rankingPanel)));
        Motion.Cascade(_resultsPanel, 30);
    }

    private Border MonitoringCard(IReadOnlyList<SensorTrack> tracks, IReadOnlyList<PhaseMark>? phases)
    {
        var wanted = new[] { ("Uso da GPU", Color.FromRgb(0xFF, 0x4D, 0x4D)), ("Temperatura da GPU", Color.FromRgb(0x4D, 0xD0, 0xFF)), ("Uso da CPU", Color.FromRgb(0xB0, 0x7C, 0xFF)), ("Temperatura da CPU", Color.FromRgb(0x3D, 0xDC, 0x97)) }
            .Where(w => tracks.Any(t => t.Name == w.Item1)).ToList();
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "Monitoramento durante o teste", FontSize = 14, FontWeight = FontWeights.SemiBold });
        if (wanted.Count == 0)
        {
            stack.Children.Add(Muted("Sem leitura de sensores nesta medição."));
            return CardOf(stack);
        }

        var series = wanted.Select(w => (IReadOnlyList<double>)tracks.First(t => t.Name == w.Item1).Values.ToList()).ToArray();
        var chart = new LiveChart(Math.Max(10, series.Max(x => x.Count)), wanted.Select(w => w.Item2).ToArray()) { Height = 130, Maximum = 100, Margin = new Thickness(0, 6, 0, 0), FormatMax = v => $"{v:0}" };
        chart.Loaded += (_, _) => chart.Update(series);
        stack.Children.Add(chart);

        var legend = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var (name, color) in wanted)
        {
            var track = tracks.First(t => t.Name == name);
            legend.Children.Add(new TextBlock { Text = $"■ {name} (máx. {track.Max:0.#} {track.Unit})", Foreground = new SolidColorBrush(color), FontSize = 12, Margin = new Thickness(0, 0, 14, 2) });
        }
        stack.Children.Add(legend);
        if (phases is { Count: > 0 })
            stack.Children.Add(Muted("Etapas: " + string.Join("  ·  ", phases.Select(p => $"{TimeSpan.FromSeconds(p.StartSeconds):m\\:ss} {p.Name}")), 11));
        return CardOf(stack);
    }

    private Border RankingCard(IReadOnlyList<BenchmarkResult> scored, BenchmarkResult current)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "Ranking das suas medições", FontSize = 14, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(Muted("A medição que você está vendo aparece em laranja. A nota só vale para comparar este computador com ele mesmo.", 11));
        int top = scored.Max(h => h.Score);
        int position = 1;
        foreach (var item in scored.OrderByDescending(h => h.Score).Take(8))
        {
            bool mine = item.When == current.When;
            var row = new Grid { Height = 26, Margin = new Thickness(0, 4, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.02, item.Score / (double)top), GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0, 1 - item.Score / (double)top), GridUnitType.Star) });
            row.Children.Add(new Border { Background = mine ? (Brush)FindResource("AccentGradientBrush") : new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x31)), CornerRadius = new CornerRadius(4) });
            string name = item.Label is { Length: > 0 } ? item.Label : item.When.ToString("dd/MM HH:mm");
            var text = new TextBlock { Text = $"{position++}. {name}" + (item.Mode is { Length: > 0 } md ? $" ({md})" : "") + $"    {item.Score:N0}", Foreground = mine ? Brushes.Black : Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), FontWeight = mine ? FontWeights.SemiBold : FontWeights.Normal };
            Grid.SetColumnSpan(text, 2);
            row.Children.Add(text);
            stack.Children.Add(row);
        }
        return CardOf(stack);
    }

    private static string Fmt(double value) => value > 0 ? value.ToString("0.0") : "—";

    // ================================================================== Histórico

    private void RenderHistoryTab()
    {
        _historyPanel.Children.Clear();
        var history = BenchmarkHistory.Load();
        if (history.Count == 0)
        {
            _historyPanel.Children.Add(CardOf(Muted("Nenhum teste feito ainda.")));
            return;
        }
        _historyPanel.Children.Add(Muted("Clique em Ver para abrir a medição em Resultados, ou em Comparar para ver as diferenças com a anterior.", 11.5));
        for (int i = history.Count - 1; i >= 0; i--)
        {
            var item = history[i];
            var previous = history.Take(i).LastOrDefault(h => h.Score > 0 && item.Score > 0);
            int value = item.Score > 0 ? item.Score : item.Gpu3DScore > 0 ? item.Gpu3DScore : item.RenderMultiScore;
            string kind = item.Score > 0 ? "completo" : item.Gpu3DScore > 0 ? "só a GPU" : item.RenderMultiScore > 0 ? "render da CPU" : "parcial";

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var num = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            num.Children.Add(new TextBlock { Text = value.ToString("N0"), FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Yellow) });
            if (previous is not null)
            {
                int delta = item.Score - previous.Score;
                double pct = delta * 100.0 / previous.Score;
                num.Children.Add(new TextBlock { Text = $"{(delta >= 0 ? "▲" : "▼")} {Math.Abs(pct):0.#}%", FontSize = 11, Foreground = Math.Abs(pct) <= 3 ? Brush("MutedBrush") : delta > 0 ? Brush("OkBrush") : Brush("BadBrush") });
            }
            grid.Children.Add(num);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = item.Label is { Length: > 0 } lb ? lb : item.When.ToString("dd/MM/yyyy 'às' HH:mm"), FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = $"{item.When:dd/MM/yyyy HH:mm} · {kind}" + (item.Mode is { Length: > 0 } m ? $" · {m}" : "") + (item.Resolution is { Length: > 0 } r ? $" · 3D {r}" : "") + (item.DiskName is { Length: > 0 } d ? $" · {d}" : ""),
                FontSize = 11, Foreground = Brush("MutedBrush"), TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var view = new Button { Content = "Ver", Style = (Style)FindResource("GhostButton") };
            view.Click += (_, _) => { _viewing = item; _ = FillSideAsync(); SelectTab(2); };
            buttons.Children.Add(view);
            if (previous is not null)
            {
                var cmp = new Button { Content = "Comparar", Style = (Style)FindResource("GhostButton"), Margin = new Thickness(8, 0, 0, 0) };
                cmp.Click += (_, _) => { SelectTab(4); RenderCompare(previous, item); };
                buttons.Children.Add(cmp);
            }
            Grid.SetColumn(buttons, 2);
            grid.Children.Add(buttons);
            var card = CardOf(grid);
            card.Padding = new Thickness(14, 10, 14, 10);
            if (_viewing is not null && item.When == _viewing.When) { card.BorderBrush = (Brush)FindResource("AccentGradientBrush"); card.Background = (Brush)FindResource("GlassSelectedBrush"); }
            _historyPanel.Children.Add(card);
        }
        Motion.Cascade(_historyPanel, 25);
    }

    // ================================================================== Comparar

    private void RenderCompare(BenchmarkResult? before, BenchmarkResult? after)
    {
        _comparePanel.Children.Clear();
        var history = BenchmarkHistory.Load().ToList();
        if (history.Count < 2)
        {
            _comparePanel.Children.Add(CardOf(Muted("Faça pelo menos duas medições para comparar. Dica: meça, aplique um ajuste (por exemplo o perfil Jogo) e meça de novo no mesmo modo.")));
            return;
        }
        after ??= _viewing is not null ? history.FirstOrDefault(h => h.When == _viewing.When) ?? history[^1] : history[^1];
        before ??= history.Where(h => h.When < after.When).LastOrDefault() ?? history.First(h => h.When != after.When);

        string Name(BenchmarkResult r) => (r.Label is { Length: > 0 } l ? l + " · " : "") + r.When.ToString("dd/MM HH:mm") + (r.Mode is { Length: > 0 } m ? $" · {m}" : "") +
                                          $" · {(r.Score > 0 ? r.Score : r.Gpu3DScore > 0 ? r.Gpu3DScore : r.RenderMultiScore):N0}";
        var pickers = new Grid();
        pickers.ColumnDefinitions.Add(new ColumnDefinition());
        pickers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        pickers.ColumnDefinitions.Add(new ColumnDefinition());
        ComboBox Picker(BenchmarkResult selected)
        {
            var box = new ComboBox { Margin = new Thickness(0, 4, 0, 0) };
            foreach (var r in Enumerable.Reverse(history)) box.Items.Add(new ComboBoxItem { Content = Name(r), Tag = r, IsSelected = r.When == selected.When });
            return box;
        }
        var a = Picker(before);
        var b = Picker(after);
        var left = new StackPanel { Children = { new TextBlock { Text = "Antes", FontSize = 11.5, Foreground = Brush("MutedBrush") }, a } };
        var right = new StackPanel { Children = { new TextBlock { Text = "Depois", FontSize = 11.5, Foreground = Brush("MutedBrush") }, b } };
        pickers.Children.Add(left);
        var arrow = new TextBlock { Text = "", FontFamily = new FontFamily(IconFont), FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6), Foreground = (Brush)FindResource("AccentGradientBrush") };
        Grid.SetColumn(arrow, 1);
        pickers.Children.Add(arrow);
        Grid.SetColumn(right, 2);
        pickers.Children.Add(right);
        void Changed()
        {
            if ((a.SelectedItem as ComboBoxItem)?.Tag is BenchmarkResult x && (b.SelectedItem as ComboBoxItem)?.Tag is BenchmarkResult y) RenderCompare(x, y);
        }
        a.SelectionChanged += (_, _) => Changed();
        b.SelectionChanged += (_, _) => Changed();
        _comparePanel.Children.Add(CardOf(pickers));

        var deltas = BenchmarkComparison.Compare(before, after);
        var summary = CardOf(new TextBlock { Text = BenchmarkComparison.Summary(deltas), FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        summary.Background = (Brush)FindResource("GlassSelectedBrush");
        _comparePanel.Children.Add(summary);
        if (before.Mode != after.Mode && before.Mode is not null && after.Mode is not null)
            _comparePanel.Children.Add(Muted($"Atenção: modos diferentes ({before.Mode} e {after.Mode}). Compare no mesmo modo para um resultado justo.", 11.5));
        if (before.Resolution != after.Resolution && before.Resolution is not null && after.Resolution is not null)
            _comparePanel.Children.Add(Muted($"As cenas 3D rodaram em resoluções diferentes ({before.Resolution} e {after.Resolution}); a nota da GPU é convertida para 1080p, mas pode variar.", 11.5));

        foreach (var d in deltas)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
            grid.Children.Add(new TextBlock { Text = d.Name, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            double max = Math.Max(d.Before, d.After);
            var bars = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
            bars.Children.Add(Bar(d.Before / max, new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x55)), $"{d.Before:0.#} {d.Unit}"));
            bars.Children.Add(Bar(d.After / max, (Brush)FindResource("AccentGradientBrush"), $"{d.After:0.#} {d.Unit}"));
            Grid.SetColumn(bars, 1);
            grid.Children.Add(bars);
            var pct = new TextBlock
            {
                Text = $"{(d.Percent >= 0 ? "+" : "")}{d.Percent:0.#}%", FontSize = 14, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
                Foreground = d.Verdict switch { DeltaVerdict.Better => Brush("OkBrush"), DeltaVerdict.Worse => Brush("BadBrush"), _ => Brush("MutedBrush") },
                ToolTip = d.Verdict == DeltaVerdict.Same ? "Dentro da variação normal entre rodadas (3%)" : d.HigherIsBetter ? "Maior é melhor" : "Menor é melhor",
            };
            Grid.SetColumn(pct, 2);
            grid.Children.Add(pct);
            var card = CardOf(grid);
            card.Padding = new Thickness(14, 8, 14, 8);
            card.Margin = new Thickness(0, 0, 0, 6);
            _comparePanel.Children.Add(card);
        }
        Motion.Cascade(_comparePanel, 25);
    }

    private FrameworkElement Bar(double fraction, Brush fill, string text)
    {
        var grid = new Grid { Height = 16, Margin = new Thickness(0, 1, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.01, fraction), GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, 1 - fraction), GridUnitType.Star) });
        grid.Children.Add(new Border { Background = fill, CornerRadius = new CornerRadius(3) });
        var label = new TextBlock { Text = text, FontSize = 10.5, Foreground = Brushes.White, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        label.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Opacity = 0.9 };
        Grid.SetColumnSpan(label, 2);
        grid.Children.Add(label);
        return grid;
    }

    // ================================================================== ranking anônimo (opcional)

    private void BuildRanking()
    {
        _rankingPanel.Children.Clear();
        _rankingPanel.Children.Add(new TextBlock { Text = "Ranking anônimo (opcional)", FontSize = 15, FontWeight = FontWeights.SemiBold });
        _rankingPanel.Children.Add(Muted("Compare o seu PC com outros que usam o MB Extreme. Se você participar, cada medição envia só o modelo do processador e da placa de vídeo, a memória, as notas e um código aleatório criado neste PC. Não vão nome, e-mail, licença nem arquivos. Desligado por padrão: você escolhe, pode ver o que seria enviado e apagar tudo quando quiser.", 11.5));

        var opt = new CheckBox { Content = "Participar do ranking anônimo", IsChecked = RankingService.OptedIn, Foreground = Brush("TextBrush"), Margin = new Thickness(0, 8, 0, 4), FontWeight = FontWeights.SemiBold };
        opt.Click += (_, _) =>
        {
            AppServices.Settings.RankingOptIn = opt.IsChecked == true;
            AppServices.Settings.Save();
            if (opt.IsChecked != true) _rankingNote = "";
            BuildRanking();
        };
        _rankingPanel.Children.Add(opt);
        if (!RankingService.Configured)
            _rankingPanel.Children.Add(new TextBlock { Text = "O servidor do ranking ainda não está configurado nesta versão, então nada é enviado.", FontSize = 12, Foreground = Brush("WarnBrush"), TextWrapping = TextWrapping.Wrap });

        var buttons = new WrapPanel();
        var preview = SmallButton("Ver o que será enviado", async (_, _) => await ShowPreviewAsync());
        preview.Margin = new Thickness(0, 4, 8, 4);
        buttons.Children.Add(preview);
        var forget = SmallButton("Apagar meus dados do ranking", async (_, _) => await ForgetRankingAsync());
        forget.Margin = new Thickness(0, 4, 8, 4);
        forget.IsEnabled = RankingService.Configured;
        buttons.Children.Add(forget);
        _rankingPanel.Children.Add(buttons);

        if (_rankingNote.Length > 0)
            _rankingPanel.Children.Add(new TextBlock { Text = _rankingNote, FontSize = 13, Foreground = new SolidColorBrush(Yellow), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
    }

    private async Task ShowPreviewAsync()
    {
        try
        {
            var hw = await AppServices.Hardware();
            var last = BenchmarkHistory.Load().LastOrDefault(r => r.Score > 0);
            string text = last is null
                ? "Faça um benchmark completo primeiro: aqui aparece exatamente o que seria enviado dele."
                : RankingService.Preview(RankingService.BuildBench(last, hw)!);
            MessageBox.Show(text + (last is null ? "" : "\n\nO código (installId) é aleatório e nasce neste PC; ele só é criado se você participar."), "O que será enviado ao ranking", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Management.ManagementException or AggregateException)
        {
            SetStatus("Não foi possível ler o hardware para mostrar o que seria enviado.", "WarnBrush");
        }
    }

    private async Task ForgetRankingAsync()
    {
        bool ok = await RankingService.ForgetAsync();
        _rankingNote = "";
        BuildRanking();
        SetStatus(ok ? "Seus dados foram apagados do ranking e este PC ganhou um código novo." : "Não foi possível apagar agora (sem internet, ou nada foi enviado ainda). Tente de novo mais tarde.", ok ? "OkBrush" : "WarnBrush");
    }

    private async Task SubmitAndShowAsync(BenchmarkResult result)
    {
        if (!RankingService.OptedIn) return;
        var standing = await RankingService.SubmitBenchAsync(result);
        string gpu = result.GpuName is { Length: > 0 } name && result.Gpu3DScore > 0 ? name : "a sua placa";
        _rankingNote = RankingService.Describe(standing, gpu);
        Dispatcher.Invoke(BuildRanking);
    }
}
