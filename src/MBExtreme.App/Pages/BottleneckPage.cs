using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Bottleneck;

namespace MBExtreme.UI.Pages;

/// <summary>Monitor de gargalos: mede CPU, GPU, RAM, disco e temperatura enquanto você joga e diz o que limita o desempenho.</summary>
public sealed class BottleneckPage : ToolPageBase
{
    private static readonly int[] Targets = { 30, 60, 120, 144, 240 };
    private readonly Button _toggle;
    private readonly TextBlock _live = new() { FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Text = "Parado" };
    private readonly TextBlock _liveDetail = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _note = new() { Width = 420, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 6) };
    private readonly StackPanel _report = new();
    private readonly StackPanel _history = new();

    public BottleneckPage() : base("Monitor de gargalos",
        "Mede o que limita o seu FPS: a placa de vídeo, o processador, a memória, o disco ou o calor. Clique em Iniciar, jogue por alguns minutos e clique em Parar para ver o relatório. Precisa de administrador para medir os quadros. Nada é enviado: as sessões ficam só neste PC.")
    {
        _toggle = AddButton("Iniciar medição", async (_, _) => await ToggleAsync());

        Body.Children.Add(Heading("Meta de FPS"));
        Body.Children.Add(Choice(Targets.Select(t => t + " fps").ToList(), Math.Max(0, Array.IndexOf(Targets, AppServices.Settings.BottleneckTargetFps)), i =>
        {
            AppServices.Settings.BottleneckTargetFps = Targets[i];
            AppServices.Settings.Save();
        }));

        Body.Children.Add(Heading("Agora"));
        var live = new StackPanel();
        live.Children.Add(_live);
        live.Children.Add(_liveDetail);
        Body.Children.Add(CardOf(live));

        Body.Children.Add(Muted("Opções gráficas usadas nesta sessão (opcional, ajuda a lembrar qual configuração rodou melhor):"));
        Body.Children.Add(_note);

        Body.Children.Add(_report);

        var share = new CheckBox
        {
            Content = "Ajudar a melhorar as recomendações: enviar resumos anônimos das sessões (jogo, faixa do hardware e resultado). Desligado por padrão.",
            IsChecked = AppServices.Settings.ShareAnonymousStats,
            Foreground = Brush("TextBrush"),
            Margin = new Thickness(0, 10, 0, 0),
        };
        share.Click += (_, _) =>
        {
            AppServices.Settings.ShareAnonymousStats = share.IsChecked == true;
            AppServices.Settings.Save();
        };
        Body.Children.Add(share);
        Body.Children.Add(Muted("Nunca enviamos nome, e-mail, endereço IP guardado, lista de programas ou arquivos. O envio de dados só será ativado quando o servidor de estatísticas existir; por enquanto o resumo fica só neste PC.", 11));

        Body.Children.Add(Heading("Sessões anteriores"));
        Body.Children.Add(_history);

        Loaded += (_, _) =>
        {
            AppServices.Bottleneck.Changed += OnChanged;
            OnChanged(this, EventArgs.Empty);
            RenderHistory();
        };
        Unloaded += (_, _) => AppServices.Bottleneck.Changed -= OnChanged;
    }

    private async Task ToggleAsync()
    {
        var c = AppServices.Bottleneck;
        if (!c.IsRecording)
        {
            try
            {
                c.Start();
                _report.Children.Clear();
                SetStatus("Medindo. Abra o jogo e jogue normalmente.", "OkBrush");
            }
            catch (Exception ex)
            {
                SetStatus("Não foi possível iniciar (precisa de administrador): " + ex.Message, "BadBrush");
            }
            return;
        }

        var report = await c.StopAsync(_note.Text);
        SetStatus("Medição encerrada.", "OkBrush");
        if (report is not null) ShowReport(report);
        RenderHistory();
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var c = AppServices.Bottleneck;
            _toggle.Content = c.IsRecording ? "Parar e ver relatório" : "Iniciar medição";
            if (!c.IsRecording)
            {
                _live.Text = "Parado";
                _liveDetail.Text = "";
                return;
            }

            var fps = AppServices.Overlay.Last;
            _live.Text = c.CurrentLabel ?? "Aguardando um jogo...";
            _liveDetail.Text = fps is null
                ? "Abra o jogo e mova a câmera para o monitor detectar os quadros."
                : $"{c.CurrentGame} · {fps.Fps:0} fps · 1% pior {fps.Low1PercentFps:0} fps · {c.SampleCount} medidas";
        }));
    }

    private void ShowReport(BottleneckReport r)
    {
        _report.Children.Clear();
        _report.Children.Add(Heading($"Relatório: {r.Game}"));
        var summary = new StackPanel();
        summary.Children.Add(new TextBlock { Text = BottleneckAnalyzer.Label(r.Primary), FontSize = 20, FontWeight = FontWeights.SemiBold });
        summary.Children.Add(Muted($"{r.Duration:mm\\:ss} medidos · FPS médio {r.AverageFps:0} · pior {r.WorstFps:0} · 1% pior {r.Low1PercentFps:0} · {r.Stutters} segundo(s) com travadas · meta {r.TargetFps:0} fps"));
        _report.Children.Add(CardOf(summary));

        _report.Children.Add(Heading("Tempo gasto em cada limite"));
        foreach (var kind in new[] { BottleneckKind.Gpu, BottleneckKind.Cpu, BottleneckKind.Memory, BottleneckKind.Disk, BottleneckKind.Thermal, BottleneckKind.Balanced })
        {
            double pct = r.Percent.GetValueOrDefault(kind);
            var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = pct, Height = 8, Margin = new Thickness(0, 4, 0, 0) };
            var item = new StackPanel();
            item.Children.Add(new TextBlock { Text = $"{BottleneckAnalyzer.Label(kind)}: {pct:0}%", FontSize = 13 });
            item.Children.Add(bar);
            _report.Children.Add(CardOf(item));
        }

        _report.Children.Add(Heading("O que fazer"));
        foreach (string line in r.Advice) _report.Children.Add(CardOf(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap }));

        if (AppServices.Bottleneck.History.Suggestion(r.Game) is { } suggestion)
            _report.Children.Add(CardOf(new TextBlock { Text = suggestion, TextWrapping = TextWrapping.Wrap, Foreground = Brush("OkBrush") }));
    }

    private void RenderHistory()
    {
        _history.Children.Clear();
        var recent = AppServices.Bottleneck.History.Recent(10);
        if (recent.Count == 0) { _history.Children.Add(Muted("Nenhuma sessão medida ainda.")); return; }
        foreach (var r in recent)
            _history.Children.Add(Row($"{r.Game} · {r.Date:dd/MM HH:mm}",
                $"{r.AverageFps:0} fps · 1% {r.Low1PercentFps:0} · {r.Primary} · {r.DurationSeconds / 60} min" + (r.Settings is null ? "" : $" · {r.Settings}")));
    }
}
