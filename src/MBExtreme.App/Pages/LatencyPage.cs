using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Latency;

namespace MBExtreme.UI.Pages;

/// <summary>Diagnóstico de latência: encontra o driver que causa micro-travadas medindo DPC e ISR do sistema.</summary>
public sealed class LatencyPage : ToolPageBase
{
    private static readonly int[] Seconds = { 30, 60, 120, 300 };
    private int _secondsIndex = 1;
    private readonly Button _start;
    private readonly TextBlock _live = new() { Margin = new Thickness(0, 10, 0, 0) };
    private readonly StackPanel _results = new();

    public LatencyPage() : base("Diagnóstico de latência",
        "Micro-travadas, áudio estourando e quedas de quadro quase sempre vêm de um driver que ocupa a CPU por tempo demais (rotinas DPC/ISR). Esta página mede o sistema por alguns minutos e mostra qual driver é o pior. Só leitura: nada é alterado. Enquanto mede, use o PC normalmente ou jogue.")
    {
        _start = AddButton("Iniciar medição", async (_, _) => await RunAsync());
        Body.Children.Add(Heading("Duração"));
        Body.Children.Add(Choice(Seconds.Select(s => s < 60 ? s + " s" : s / 60 + " min").ToList(), _secondsIndex, i => _secondsIndex = i));
        Body.Children.Add(_live);
        Body.Children.Add(_results);

        double? timer = LatencyMonitor.TimerResolutionMilliseconds();
        if (timer is { } t)
            Body.Children.Add(Row("Resolução do temporizador do Windows",
                $"{t:0.0##} ms agora. Programas e jogos podem pedir uma resolução mais fina (até 0,5 ms) e isso aumenta o consumo de energia. O MB Extreme não altera este valor: testes e a documentação da Microsoft não mostram ganho comprovado em mexer nisso."));
    }

    private async Task RunAsync()
    {
        _start.IsEnabled = false;
        _results.Children.Clear();
        SetStatus("Medindo... clique em Parar na janela de progresso ou aguarde o fim.");
        ShowProgress("Diagnóstico de latência", "Medição");
        var token = Cts!.Token;
        var duration = TimeSpan.FromSeconds(Seconds[_secondsIndex]);
        try
        {
            void OnTick(TimeSpan elapsed, long count)
            {
                var message = new MBExtreme.Core.Progress.ProgressEvent("Medição", 100 * elapsed.TotalSeconds / duration.TotalSeconds,
                    $"{elapsed:mm\\:ss} · {count:N0} eventos medidos");
                Dispatcher.BeginInvoke(new Action(() => Progress.Report(message)));
            }

            var (top, events) = await Task.Run(() =>
            {
                var list = LatencyMonitor.Run(duration, OnTick, token, out long total);
                return (list, total);
            });

            Progress.Finish("Medição concluída", true, $"{events:N0} eventos analisados.");
            Show(top);
        }
        catch (OperationCanceledException)
        {
            Progress.Finish("Interrompido.", false);
        }
        catch (Exception ex)
        {
            Progress.Finish("Não foi possível medir", false,
                "O Windows só permite uma medição de kernel por vez (feche outros medidores, como o LatencyMon) e ela exige administrador. " + ex.Message);
        }
        finally
        {
            _start.IsEnabled = true;
        }
    }

    private void Show(IReadOnlyList<DriverLatency> top)
    {
        _results.Children.Clear();
        if (top.Count == 0)
        {
            _results.Children.Add(Muted("Nenhum evento registrado."));
            return;
        }

        var worst = top[0];
        string verdict = DpcIsrAggregator.Verdict(worst.MaxMicroseconds);
        SetStatus($"Pior pico: {worst.Module} com {worst.MaxMicroseconds:0} µs ({verdict}). Referência: abaixo de 1000 µs é bom.",
            verdict is "ótimo" or "bom" ? "OkBrush" : "WarnBrush");

        _results.Children.Add(Heading("Drivers com os maiores picos"));
        foreach (var d in top)
        {
            string v = DpcIsrAggregator.Verdict(d.MaxMicroseconds);
            string brush = v switch { "ótimo" => "OkBrush", "bom" => "OkBrush", "atenção" => "WarnBrush", _ => "BadBrush" };
            string detail = $"{d.Kind} · pico {d.MaxMicroseconds:0} µs · média {d.AverageMicroseconds:0.0} µs · {d.Count:N0} chamadas · {d.TotalMilliseconds:0.0} ms no total";
            if (v is "atenção" or "ruim") detail += "\n→ " + DpcIsrAggregator.Advice(d.Module);
            _results.Children.Add(Row($"{d.Module} ({v})", detail, null, brush));
        }
    }
}
