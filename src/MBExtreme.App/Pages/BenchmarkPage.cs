using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MBExtreme.Core.Benchmark;
using MBExtreme.Core.Progress;

namespace MBExtreme.UI.Pages;

/// <summary>Benchmark de CPU, memória, disco e GPU 3D, com histórico para comparar antes e depois de uma mudança.</summary>
public sealed class BenchmarkPage : ToolPageBase
{
    private readonly StackPanel _results = new();
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
        IProgress<ProgressEvent> progress = new Progress<ProgressEvent>(Progress.Report);
        try
        {
            var result = await Task.Run(() => BenchmarkService.Run(progress, token, options));
            BenchmarkHistory.Add(result);
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
        _results.Children.Add(Heading("Último resultado"));
        var big = new StackPanel();
        big.Children.Add(new TextBlock { Text = last.Score.ToString(), FontSize = 44, FontWeight = FontWeights.Bold });
        big.Children.Add(Muted($"{last.When:dd/MM/yyyy HH:mm} · {last.Threads} threads" + (last.Label is { Length: > 0 } l ? $" · {l}" : "")));
        _results.Children.Add(CardOf(big));

        _results.Children.Add(Row("CPU, 1 núcleo", $"{last.CpuSingleMops:0.0} milhões de operações por segundo"));
        _results.Children.Add(Row("CPU, todos os núcleos", $"{last.CpuMultiMops:0.0} milhões de operações por segundo"));
        if (last.CpuSimdGflops > 0)
            _results.Children.Add(Row("CPU vetorial, criptografia e compressão",
                $"{last.CpuSimdGflops:0.0} GFLOPS · AES {last.CryptoMBps:0} MB/s · compressão {last.CompressMBps:0} MB/s"));
        _results.Children.Add(Row("Memória",
            $"Leitura {Fmt(last.MemoryReadGBps)} · escrita {Fmt(last.MemoryWriteGBps)} · cópia {last.MemoryGBps:0.0} GB/s" +
            (last.MemoryLatencyNs > 0 ? $"\nLatência {last.MemoryLatencyNs:0} ns (acesso aleatório em memória grande)" : "")));
        if (last.LatencyCurve is { Count: > 0 } curve)
            _results.Children.Add(Row("Curva de latência (cache e RAM)",
                string.Join("  ·  ", curve.Select(p => $"{(p.SizeKb >= 1024 ? p.SizeKb / 1024 + " MB" : p.SizeKb + " KB")}: {p.Nanoseconds:0.0} ns"))));
        if (last.DiskWriteMBps > 0)
            _results.Children.Add(Row("Disco", $"Gravação {last.DiskWriteMBps:0} MB/s · leitura {last.DiskReadMBps:0} MB/s (a leitura pode vir do cache do Windows)"));
        if (last.Gpu3DScore > 0)
            _results.Children.Add(Row($"GPU 3D: {last.GpuName}", $"Pontuação {last.Gpu3DScore}" +
                (last.GpuScenes is { Count: > 0 } sc ? "\n" + string.Join(" · ", sc.Select(s => $"{s.Scene} {s.Fps:0} FPS")) : "")));

        else if (last.GpuName is { Length: > 0 } gpuNote)
            _results.Children.Add(Row("GPU 3D", gpuNote, null, "WarnBrush"));

        if (history.Count > 1)
        {
            _results.Children.Add(Heading("Histórico"));
            for (int i = history.Count - 1; i >= 0; i--)
            {
                var item = history[i];
                string delta = i > 0 ? $" ({item.Score - history[i - 1].Score:+0;-0;0} vs anterior)" : "";
                _results.Children.Add(Row($"{item.Score}{delta}" + (item.Label is { Length: > 0 } lb ? " · " + lb : ""), $"{item.When:dd/MM/yyyy HH:mm}"));
            }
        }
    }

    private static string Fmt(double value) => value > 0 ? value.ToString("0.0") : "—";
}
