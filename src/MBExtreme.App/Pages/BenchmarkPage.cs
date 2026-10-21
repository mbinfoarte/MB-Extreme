using System.IO;
using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Benchmark;
using MBExtreme.Core.Progress;

namespace MBExtreme.UI.Pages;

/// <summary>Benchmark de CPU, memória e disco com histórico para comparar antes e depois de uma mudança.</summary>
public sealed class BenchmarkPage : ToolPageBase
{
    private readonly StackPanel _results = new();

    public BenchmarkPage() : base("Benchmark",
        "Mede CPU (1 núcleo e todos), memória e disco em cerca de 20 segundos. A pontuação é relativa: use para comparar este mesmo PC antes e depois de uma otimização. Feche outros programas para um resultado melhor.")
    {
        AddButton("Iniciar benchmark", async (_, _) => await RunAsync());
        Body.Children.Add(RecordOption());
        Body.Children.Add(_results);
        Loaded += (_, _) => RenderHistory();
    }

    private async Task RunAsync()
    {
        string? recording = BeginRecordingIfChecked("benchmark");
        ShowProgress("Benchmark", BenchmarkService.Steps);
        var token = Cts!.Token;
        IProgress<ProgressEvent> progress = new Progress<ProgressEvent>(Progress.Report);
        try
        {
            var result = await Task.Run(() => BenchmarkService.Run(progress, token));
            BenchmarkHistory.Add(result);
            Progress.Finish($"Pontuação: {result.Score}", true,
                $"1 núcleo {result.CpuSingleMops:0} · todos {result.CpuMultiMops:0} · memória {result.MemoryGBps:0.0} GB/s · disco {result.DiskWriteMBps:0}/{result.DiskReadMBps:0} MB/s");
        }
        catch (OperationCanceledException)
        {
            Progress.Finish("Interrompido.", false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Progress.Finish("Não foi possível concluir.", false, ex.Message);
        }
        finally
        {
            EndRecording(recording);
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
        big.Children.Add(Muted($"{last.When:dd/MM/yyyy HH:mm} · {last.Threads} threads"));
        _results.Children.Add(CardOf(big));

        _results.Children.Add(Row("CPU, 1 núcleo", $"{last.CpuSingleMops:0.0} milhões de operações por segundo"));
        _results.Children.Add(Row("CPU, todos os núcleos", $"{last.CpuMultiMops:0.0} milhões de operações por segundo"));
        _results.Children.Add(Row("Memória", $"{last.MemoryGBps:0.0} GB/s"));
        _results.Children.Add(Row("Disco", $"Gravação {last.DiskWriteMBps:0} MB/s · leitura {last.DiskReadMBps:0} MB/s (a leitura pode vir do cache do Windows)"));

        if (history.Count > 1)
        {
            _results.Children.Add(Heading("Histórico"));
            for (int i = history.Count - 1; i >= 0; i--)
            {
                var item = history[i];
                string delta = i > 0 ? $" ({(item.Score - history[i - 1].Score):+0;-0;0} vs anterior)" : "";
                _results.Children.Add(Row($"{item.Score}{delta}", $"{item.When:dd/MM/yyyy HH:mm}"));
            }
        }
    }
}
