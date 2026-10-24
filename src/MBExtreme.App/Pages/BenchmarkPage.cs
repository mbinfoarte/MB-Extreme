using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MBExtreme.Core.Benchmark;
using MBExtreme.Core.Fps;
using MBExtreme.Core.Games;
using MBExtreme.Core.Progress;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>Benchmark de CPU, memória, disco e GPU 3D, com histórico para comparar antes e depois de uma mudança.</summary>
public sealed class BenchmarkPage : ToolPageBase
{
    private readonly StackPanel _results = new();
    private readonly StackPanel _rankingPanel = new();
    private static string _rankingNote = "";
    private readonly CheckBox _disk = new() { Content = "Incluir disco (grava um arquivo temporário de 256 MB)", IsChecked = true };
    private readonly CheckBox _gpu = new() { Content = "Incluir GPU 3D (cenas em Direct3D)", IsChecked = true };
    private readonly TextBox _label = new() { Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Image _preview = new() { Stretch = Stretch.Uniform, MaxHeight = 380 };
    private readonly Border _previewCard = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 12, 0, 0), CornerRadius = new CornerRadius(14), ClipToBounds = true, Background = System.Windows.Media.Brushes.Black };
    private readonly List<Button> _buttons = new();
    private WriteableBitmap? _bitmap;
    private int _mode;

    public BenchmarkPage() : base("Benchmark",
        "Mede CPU (1 núcleo, todos, instruções vetoriais, criptografia e compressão), memória (banda e latência), disco e GPU em 3D. A pontuação é relativa: use para comparar este mesmo PC antes e depois de uma mudança. Feche outros programas para um resultado melhor.")
    {
        _buttons.Add(AddButton("Iniciar benchmark", async (_, _) => await RunAsync()));
        _buttons.Add(AddButton("GPU 3D ao vivo", async (_, _) => await RunGpuLiveAsync(), false));
        _buttons.Add(AddButton("Render da CPU", async (_, _) => await RunRenderAsync(), false));
        _buttons.Add(AddButton("CPU por núcleo", async (_, _) => await PerCoreAsync(), false));
        AddButton("Antes e depois", (_, _) => MainWindow.Current?.Go("compare"), false);
        _previewCard.Child = _preview;

        _disk.Foreground = Brush("TextBrush");
        _gpu.Foreground = Brush("TextBrush");
        Body.Children.Add(Heading("Duração"));
        Body.Children.Add(Choice(new[] { "Completo (~35 s)", "Rápido (~15 s)" }, _mode, i => _mode = i));
        Body.Children.Add(_disk);
        Body.Children.Add(_gpu);
        Body.Children.Add(Heading("Nome desta medição (opcional)"));
        Body.Children.Add(_label);
        Body.Children.Add(Muted("Ex.: \"Antes do perfil Jogo\". O nome aparece no histórico e em Antes e depois."));
        Body.Children.Add(RecordOption());
        Body.Children.Add(CardOf(_rankingPanel));
        BuildRanking();
        Body.Children.Add(_previewCard);
        Body.Children.Add(_results);
        Loaded += (_, _) => RenderHistory();
    }

    private async Task RunAsync()
    {
        var options = new BenchmarkOptions(_disk.IsChecked == true && _mode == 0, _gpu.IsChecked == true, _mode == 1,
            string.IsNullOrWhiteSpace(_label.Text) ? null : _label.Text.Trim());
        string? recording = BeginRecordingIfChecked("benchmark");
        ShowProgress("Benchmark", BenchmarkService.StepsFor(options));
        var token = Cts!.Token;

        // monitoramento durante o teste: sensores a cada segundo e o instante em que começa cada etapa (como no gráfico do 3DMark)
        var clock = Stopwatch.StartNew();
        var phases = new List<PhaseMark>();
        string? lastStep = null;
        var recorder = new SensorRecorder();
        using var stopSampling = new CancellationTokenSource();
        IProgress<ProgressEvent> progress = new Progress<ProgressEvent>(e =>
        {
            Progress.Report(e);
            if (e.Step != lastStep) { lastStep = e.Step; phases.Add(new PhaseMark(e.Step, clock.Elapsed.TotalSeconds)); }
        });
        var sampler = Task.Run(async () =>
        {
            try
            {
                var sensors = await AppServices.Sensors;
                while (!stopSampling.IsCancellationRequested)
                {
                    recorder.Add(sensors.Read());
                    await Task.Delay(1000, stopSampling.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is InvalidOperationException or AggregateException) { }
        });
        try
        {
            var result = await Task.Run(() => BenchmarkService.Run(progress, token, options));
            stopSampling.Cancel();
            await sampler;
            result = result with { Monitoring = recorder.Tracks(), Phases = phases };
            BenchmarkHistory.Add(result);
            _ = SubmitAndShowAsync(result);
            Progress.Finish($"Pontuação: {result.Score}", true,
                $"1 núcleo {result.CpuSingleMops:0} · todos {result.CpuMultiMops:0} · memória {result.MemoryGBps:0.0} GB/s · latência {result.MemoryLatencyNs:0} ns" +
                (result.Gpu3DScore > 0 ? $" · GPU {result.Gpu3DScore}" : ""));
        }
        catch (OperationCanceledException)
        {
            Progress.Finish("Interrompido.", false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Progress.Finish("Não foi possível concluir.", false, ex.Message);
        }
        finally
        {
            EndRecording(recording);
        }
    }

    /// <summary>Roda só o benchmark 3D da GPU mostrando a imagem sendo renderizada.</summary>
    private async Task RunGpuLiveAsync()
    {
        foreach (var b in _buttons) b.IsEnabled = false;
        _previewCard.Visibility = Visibility.Visible;
        Cts = new CancellationTokenSource();
        var token = Cts.Token;
        SetStatus("Benchmark 3D da GPU em andamento...");
        string? recording = BeginRecordingIfChecked("benchmark-gpu");
        IProgress<ProgressEvent> progress = new Progress<ProgressEvent>(e => SetStatus($"GPU 3D: {e.Message} ({e.Percent:0}%)"));
        try
        {
            var gpu = await Task.Run(() => Gpu3DBenchmark.Run(progress, "GPU", 0, 100, ShowFrame, token, _mode == 1 ? 3 : 6));
            var result = new BenchmarkResult(DateTime.Now, Environment.ProcessorCount, 0, 0, 0, 0, 0, gpu.Score,
                Label: string.IsNullOrWhiteSpace(_label.Text) ? null : _label.Text.Trim(),
                GpuName: gpu.GpuName, Gpu3DScore: gpu.Score, GpuScenes: gpu.Scenes);
            BenchmarkHistory.Add(result);
            _ = SubmitAndShowAsync(result);
            SetStatus($"GPU {gpu.GpuName}: pontuação {gpu.Score}", "OkBrush");
            RenderHistory();
        }
        catch (OperationCanceledException)
        {
            SetStatus("Interrompido.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetStatus("Não foi possível rodar o benchmark 3D: " + ex.Message + " (atualize o driver da placa de vídeo)", "BadBrush");
        }
        finally
        {
            EndRecording(recording);
            foreach (var b in _buttons) b.IsEnabled = true;
        }
    }

    /// <summary>Teste de renderização da CPU: desenha uma cena por blocos (1 núcleo e depois todos) e mostra a imagem aparecendo.</summary>
    private async Task RunRenderAsync()
    {
        foreach (var b in _buttons) b.IsEnabled = false;
        _previewCard.Visibility = Visibility.Visible;
        Cts = new CancellationTokenSource();
        var token = Cts.Token;
        string? recording = BeginRecordingIfChecked("benchmark-render");
        try
        {
            NewRenderBitmap();
            Action<double> single = p => Dispatcher.BeginInvoke(new Action(() => SetStatus($"Render da CPU, 1 núcleo: {p:0}%")));
            Action<double> multi = p => Dispatcher.BeginInvoke(new Action(() => SetStatus($"Render da CPU, todos os núcleos: {p:0}%")));
            SetStatus("Render da CPU, 1 núcleo...");
            var one = await Task.Run(() => CpuRenderBenchmark.Run(1, 6, token, ShowTile, single));
            NewRenderBitmap();
            SetStatus($"Render da CPU, {Environment.ProcessorCount} linhas de execução...");
            var all = await Task.Run(() => CpuRenderBenchmark.Run(Environment.ProcessorCount, 8, token, ShowTile, multi));
            var result = new BenchmarkResult(DateTime.Now, Environment.ProcessorCount, 0, 0, 0, 0, 0, 0,
                Label: string.IsNullOrWhiteSpace(_label.Text) ? null : _label.Text.Trim(), RenderSingleScore: one.Score, RenderMultiScore: all.Score);
            BenchmarkHistory.Add(result);
            SetStatus($"Render da CPU: 1 núcleo {one.Score} pts · todos os núcleos {all.Score} pts ({all.MegaRaysPerSecond:0.0} milhões de raios por segundo).", "OkBrush");
            RenderHistory();
        }
        catch (OperationCanceledException)
        {
            SetStatus("Interrompido.");
        }
        finally
        {
            EndRecording(recording);
            foreach (var b in _buttons) b.IsEnabled = true;
        }
    }

    private void NewRenderBitmap() => Dispatcher.Invoke(() =>
    {
        _bitmap = new WriteableBitmap(CpuRenderBenchmark.DefaultWidth, CpuRenderBenchmark.DefaultHeight, 96, 96, PixelFormats.Bgra32, null);
        _preview.Source = _bitmap;
    });

    private void ShowTile(RenderTile tile) => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (_bitmap is null) return;
        _bitmap.WritePixels(new Int32Rect(tile.X, tile.Y, tile.Width, tile.Height), tile.Bgra, tile.Width * 4, 0);
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

    private async Task PerCoreAsync()
    {
        ShowProgress("CPU por núcleo", "Núcleos");
        var token = Cts!.Token;
        IProgress<ProgressEvent> progress = new Progress<ProgressEvent>(Progress.Report);
        try
        {
            var cores = await Task.Run(() => BenchmarkService.PerCore(progress, "Núcleos", token));
            double best = cores.Max(c => c.Mops);
            double worst = cores.Min(c => c.Mops);
            string lines = string.Join("\n", cores.Select(c =>
                $"Núcleo {c.Core + 1,2}: {c.Mops,6:0.0} Mop/s  {new string('█', (int)Math.Round(c.Mops / best * 24))}"));
            Progress.Finish($"Mais forte {best:0.0} · mais fraco {worst:0.0} Mop/s (diferença {(best - worst) / best * 100:0.#}%)", true, lines);
        }
        catch (OperationCanceledException)
        {
            Progress.Finish("Interrompido.", false);
        }
    }

    protected override void OnProgressClosed() => RenderHistory();

    // ---------------------------------------------------------------- ranking anônimo (opcional)

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
            _rankingPanel.Children.Add(new TextBlock { Text = _rankingNote, FontSize = 13, Foreground = Brush("AccentBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
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

    private static readonly Color Orange = Color.FromRgb(0xFF, 0x9A, 0x1F);

    private void RenderHistory()
    {
        _results.Children.Clear();
        var history = BenchmarkHistory.Load();
        if (history.Count == 0)
        {
            _results.Children.Add(Muted("Nenhum teste feito ainda."));
            return;
        }

        var last = history[^1];
        var scored = history.Where(h => h.Score > 0).ToList();
        int shown = last.Score > 0 ? last.Score : last.RenderMultiScore;
        bool isRenderOnly = last.Score <= 0 && last.RenderMultiScore > 0;

        // ---- nota grande, como o anel do 3DMark
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        var ring = new Grid { Width = 160, Height = 160, HorizontalAlignment = HorizontalAlignment.Left };
        ring.Children.Add(new System.Windows.Shapes.Ellipse { Stroke = new SolidColorBrush(Orange), StrokeThickness = 9, Margin = new Thickness(5) });
        var scoreText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        scoreText.Children.Add(new TextBlock { Text = shown.ToString("N0"), FontSize = 40, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center });
        scoreText.Children.Add(new TextBlock { Text = isRenderOnly ? "pts de render" : "pontos", FontSize = 12, Foreground = Brush("MutedBrush"), HorizontalAlignment = HorizontalAlignment.Center });
        ring.Children.Add(scoreText);
        header.Children.Add(ring);

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        string headline;
        string headlineBrush = "MutedBrush";
        if (scored.Count > 0 && last.Score > 0)
        {
            int best = scored.Max(h => h.Score);
            if (last.Score >= best) { headline = scored.Count > 1 ? "Seu melhor resultado até agora" : "Primeira medição guardada"; headlineBrush = "OkBrush"; }
            else
            {
                double below = (best - last.Score) * 100.0 / best;
                headline = $"{below:0.#}% abaixo do seu melhor resultado";
                headlineBrush = below < 5 ? "MutedBrush" : "WarnBrush";
            }
        }
        else headline = isRenderOnly ? $"1 núcleo: {last.RenderSingleScore:N0} pts · todos os núcleos: {last.RenderMultiScore:N0} pts" : "Medição parcial";
        info.Children.Add(new TextBlock { Text = headline, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Foreground = Brush(headlineBrush) });
        info.Children.Add(Muted($"{last.When:dd/MM/yyyy HH:mm} · {last.Threads} threads" + (last.Label is { Length: > 0 } l ? $" · {l}" : "")));
        if (scored.Count > 0)
        {
            var stats = new UniformGrid { Columns = 3, Margin = new Thickness(0, 10, 0, 0) };
            void Stat(string title, string value, string color) => stats.Children.Add(new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = title, FontSize = 12, Foreground = Brush("MutedBrush") },
                    new TextBlock { Text = value, FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Brush(color) },
                },
            });
            Stat("Esta medição", last.Score > 0 ? last.Score.ToString("N0") : "—", "AccentBrush");
            Stat("Média das suas", ((int)scored.Average(h => h.Score)).ToString("N0"), "TextBrush");
            Stat("Melhor das suas", scored.Max(h => h.Score).ToString("N0"), "OkBrush");
            info.Children.Add(stats);
        }
        header.Children.Add(info);
        Grid.SetColumn(info, 1);
        _results.Children.Add(CardOf(header));

        // ---- notas por parte, como "Graphics score" e "CPU score"
        var parts = new UniformGrid { Columns = 3 };
        void Part(string title, string value, string detail)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = title, FontSize = 12, Foreground = Brush("MutedBrush") });
            stack.Children.Add(new TextBlock { Text = value, FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Brush("AccentBrush") });
            if (detail.Length > 0) stack.Children.Add(new TextBlock { Text = detail, FontSize = 11, Foreground = Brush("MutedBrush"), TextWrapping = TextWrapping.Wrap });
            var card = CardOf(stack);
            card.Margin = new Thickness(0, 0, 8, 8);
            card.Padding = new Thickness(12, 8, 12, 8);
            parts.Children.Add(card);
        }
        if (last.CpuSingleMops > 0) Part("CPU, 1 núcleo", $"{last.CpuSingleMops:0.#}", "milhões de operações por segundo");
        if (last.CpuMultiMops > 0) Part("CPU, todos os núcleos", $"{last.CpuMultiMops:0.#}", $"{last.Threads} linhas de execução");
        if (last.RenderMultiScore > 0) Part("Render da CPU", $"{last.RenderMultiScore:N0} pts", $"1 núcleo: {last.RenderSingleScore:N0} pts");
        if (last.MemoryGBps > 0) Part("Memória", $"{last.MemoryGBps:0.#} GB/s", last.MemoryLatencyNs > 0 ? $"latência {last.MemoryLatencyNs:0} ns" : "");
        if (last.DiskWriteMBps > 0) Part("Disco", $"{last.DiskWriteMBps:0} MB/s", $"gravação · leitura {last.DiskReadMBps:0} MB/s");
        if (last.Gpu3DScore > 0) Part("GPU 3D", last.Gpu3DScore.ToString("N0"), last.GpuName ?? "");
        if (parts.Children.Count > 0) _results.Children.Add(parts);

        // ---- seu sistema e nível para jogos (preenchidos quando o inventário do hardware chegar)
        var system = new StackPanel();
        _results.Children.Add(CardOf(system));
        _ = FillSystemAsync(system);

        // ---- monitoramento durante o teste, com as etapas marcadas
        if (last.Monitoring is { Count: > 0 } tracks) _results.Children.Add(MonitoringCard(tracks, last.Phases));

        // ---- ranking das suas medições, como o do Cinebench (a atual em laranja)
        if (scored.Count > 1) _results.Children.Add(RankingCard(scored, last));

        // ---- detalhes
        _results.Children.Add(Heading("Detalhes"));
        if (last.CpuSimdGflops > 0)
            _results.Children.Add(Row("CPU vetorial, criptografia e compressão",
                $"{last.CpuSimdGflops:0.0} GFLOPS · AES {last.CryptoMBps:0} MB/s · compressão {last.CompressMBps:0} MB/s"));
        if (last.MemoryGBps > 0)
            _results.Children.Add(Row("Memória",
                $"Leitura {Fmt(last.MemoryReadGBps)} · escrita {Fmt(last.MemoryWriteGBps)} · cópia {last.MemoryGBps:0.0} GB/s" +
                (last.MemoryLatencyNs > 0 ? $"\nLatência {last.MemoryLatencyNs:0} ns (acesso aleatório em memória grande)" : "")));
        if (last.LatencyCurve is { Count: > 0 } curve)
            _results.Children.Add(Row("Curva de latência (cache e RAM)",
                string.Join("  ·  ", curve.Select(p => $"{(p.SizeKb >= 1024 ? p.SizeKb / 1024 + " MB" : p.SizeKb + " KB")}: {p.Nanoseconds:0.0} ns"))));
        if (last.DiskWriteMBps > 0)
            _results.Children.Add(Row("Disco", $"Gravação {last.DiskWriteMBps:0} MB/s · leitura {last.DiskReadMBps:0} MB/s (a leitura pode vir do cache do Windows)"));
        if (last.Gpu3DScore > 0 && last.GpuScenes is { Count: > 0 } sc)
            _results.Children.Add(Row($"GPU 3D: {last.GpuName}", string.Join(" · ", sc.Select(x => $"{x.Scene} {x.Fps:0} FPS"))));
        else if (last.Gpu3DScore <= 0 && last.GpuName is { Length: > 0 } gpuNote)
            _results.Children.Add(Row("GPU 3D", gpuNote, null, "WarnBrush"));

        if (history.Count > 1)
        {
            _results.Children.Add(Heading("Histórico"));
            for (int i = history.Count - 1; i >= 0; i--)
            {
                var item = history[i];
                string delta = i > 0 && item.Score > 0 && history[i - 1].Score > 0 ? $" ({item.Score - history[i - 1].Score:+0;-0;0} vs anterior)" : "";
                int value = item.Score > 0 ? item.Score : item.RenderMultiScore;
                _results.Children.Add(Row($"{value}{delta}" + (item.Label is { Length: > 0 } lb ? " · " + lb : ""), $"{item.When:dd/MM/yyyy HH:mm}" + (item.Score <= 0 && item.RenderMultiScore > 0 ? " · render da CPU" : "")));
            }
        }
    }

    private async Task FillSystemAsync(StackPanel panel)
    {
        try
        {
            var hw = await AppServices.Hardware();
            panel.Children.Add(new TextBlock { Text = "Seu sistema", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            var grid = new UniformGrid { Columns = 2 };
            void Item(string title, string value) => grid.Children.Add(new StackPanel
            {
                Margin = new Thickness(0, 0, 8, 6),
                Children =
                {
                    new TextBlock { Text = title, FontSize = 11, Foreground = Brush("MutedBrush") },
                    new TextBlock { Text = value, FontSize = 13, TextWrapping = TextWrapping.Wrap },
                },
            });
            Item("Processador", $"{hw.Cpu.Name.Trim()} ({hw.Cpu.Cores} núcleos, {hw.Cpu.Threads} threads)");
            Item("Placa de vídeo", string.Join(" + ", hw.Gpus.Select(g => g.Name)));
            Item("Memória", $"{hw.TotalMemoryBytes / 1024.0 / 1024 / 1024:0.#} GB" + (hw.Memory.FirstOrDefault() is { ConfiguredMhz: > 0 } m ? $" a {m.ConfiguredMhz} MHz ({m.Type})" : ""));
            Item("Windows", $"{hw.Windows.Edition} {hw.Windows.DisplayVersion} (build {hw.Windows.Build})");
            panel.Children.Add(grid);
            AddStanding(panel, hw);

            var advice = GameAdvisor.Recommend(hw);
            panel.Children.Add(new TextBlock { Text = $"Nível estimado para jogos: {advice.Tier}", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2), Foreground = Brush("AccentBrush") });
            panel.Children.Add(Muted(advice.Headline + (advice.Lines.Count > 0 ? "  " + advice.Lines[0] : "") + " (estimativa pelo hardware, não é uma medição de FPS)", 11));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Management.ManagementException or AggregateException)
        {
            panel.Children.Add(Muted("Não foi possível ler os dados do sistema."));
        }
    }

    private static HardwareIndex? _index;
    private static bool _indexLoaded;

    /// <summary>Posição do seu processador e da sua placa entre os modelos do Blender Open Data (se o índice veio junto do app).</summary>
    private void AddStanding(StackPanel panel, MBExtreme.Core.Hardware.HardwareInventory hw)
    {
        if (!_indexLoaded)
        {
            _indexLoaded = true;
            _index = HardwareIndex.Load(Path.Combine(AppContext.BaseDirectory, "hardware-index.json"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "hardware-index.json"));
        }
        if (_index is null) return;

        var lines = new List<string>();
        if (_index.Cpu(hw.Cpu.Name) is { } cpu) lines.Add($"Processador: mais rápido que {cpu.BeatsPercent:0}% dos {cpu.Models} modelos do índice");
        var gpu = hw.Gpus.Select(g => _index.Gpu(g.Name)).FirstOrDefault(x => x is not null);
        if (gpu is not null) lines.Add($"Placa de vídeo: mais rápida que {gpu.BeatsPercent:0}% dos {gpu.Models} modelos do índice");
        if (lines.Count == 0)
        {
            panel.Children.Add(Muted("Seu processador e sua placa não estão no índice do Blender Open Data (modelos novos ou raros).", 11));
            return;
        }
        panel.Children.Add(new TextBlock { Text = "Seu lugar entre os modelos (Blender Open Data)", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) });
        foreach (var line in lines) panel.Children.Add(new TextBlock { Text = line, FontSize = 13, Foreground = Brush("AccentBrush") });
        panel.Children.Add(Muted($"Medido em renderização (Blender Cycles), por modelo e não por quantidade de usuários; serve de referência, não de nota de jogos. Dados públicos (CC0) de {_index.Data.GeneratedAt:dd/MM/yyyy}.", 11));
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
        stack.Children.Add(Muted("A medição atual aparece em laranja. Não há comparação com outros PCs: a nota só vale para comparar este computador com ele mesmo.", 11));
        int top = scored.Max(h => h.Score);
        int position = 1;
        foreach (var item in scored.OrderByDescending(h => h.Score).Take(8))
        {
            bool mine = item.When == current.When;
            var row = new Grid { Height = 26, Margin = new Thickness(0, 4, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.02, item.Score / (double)top), GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0, 1 - item.Score / (double)top), GridUnitType.Star) });
            row.Children.Add(new Border { Background = new SolidColorBrush(mine ? Orange : Color.FromRgb(0x2F, 0x6F, 0xB5)), CornerRadius = new CornerRadius(3) });
            string name = item.Label is { Length: > 0 } ? item.Label : item.When.ToString("dd/MM HH:mm");
            var text = new TextBlock { Text = $"{position++}. {name}    {item.Score:N0}", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), FontWeight = mine ? FontWeights.SemiBold : FontWeights.Normal };
            Grid.SetColumnSpan(text, 2);
            row.Children.Add(text);
            stack.Children.Add(row);
        }
        return CardOf(stack);
    }

    private static string Fmt(double value) => value > 0 ? value.ToString("0.0") : "—";
}
