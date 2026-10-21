using System.IO;
using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Benchmark;
using MBExtreme.Core.Progress;

namespace MBExtreme.UI.Pages;

/// <summary>Antes e depois: mede rápido, aplica uma mudança, mede de novo e mostra a diferença real no seu PC.</summary>
public sealed class ComparePage : ToolPageBase
{
    private readonly TextBox _label = new() { Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel _list = new();
    private readonly StackPanel _result = new();
    private BenchmarkResult? _before;
    private BenchmarkResult? _after;
    private readonly List<(BenchmarkResult Item, Button Before, Button After)> _rows = new();

    public ComparePage() : base("Antes e depois",
        "A forma honesta de saber se um ajuste valeu a pena: faça uma medição rápida, aplique a mudança (perfil, ajuste, BIOS...), meça de novo e compare. Diferenças pequenas (até 3%) são variação normal entre rodadas e contam como \"igual\".")
    {
        AddButton("Medir agora (rápido)", async (_, _) => await MeasureAsync());
        AddButton("Comparar os dois escolhidos", (_, _) => Compare(), false);

        Body.Children.Add(Heading("Nome da medição"));
        Body.Children.Add(_label);
        Body.Children.Add(Muted("Ex.: \"Antes do perfil Jogo\" e depois \"Depois do perfil Jogo\". Medição rápida: CPU, memória e latência (cerca de 15 segundos)."));
        Body.Children.Add(Heading("Escolha a base e o depois"));
        Body.Children.Add(_list);
        Body.Children.Add(_result);
        Loaded += (_, _) => Reload();
    }

    private void Reload()
    {
        _list.Children.Clear();
        _rows.Clear();
        var history = BenchmarkHistory.Load();
        if (history.Count == 0)
        {
            _list.Children.Add(Muted("Nenhuma medição ainda. Clique em \"Medir agora\"."));
            return;
        }

        foreach (var item in history.Reverse().Take(10))
        {
            var before = SmallButton("Base", (_, _) => Pick(item, true));
            var after = SmallButton("Depois", (_, _) => Pick(item, false));
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(before);
            panel.Children.Add(after);
            _rows.Add((item, before, after));
            _list.Children.Add(Row($"{item.Score}" + (item.Label is { Length: > 0 } l ? " · " + l : ""), $"{item.When:dd/MM/yyyy HH:mm}", panel));
        }

        Paint();
    }

    private void Pick(BenchmarkResult item, bool asBefore)
    {
        if (asBefore) _before = item;
        else _after = item;
        Paint();
    }

    private void Paint()
    {
        foreach (var (item, before, after) in _rows)
        {
            before.Style = (Style)FindResource(ReferenceEquals(item, _before) || (_before is not null && item.When == _before.When) ? "PrimaryButton" : "GhostButton");
            after.Style = (Style)FindResource(ReferenceEquals(item, _after) || (_after is not null && item.When == _after.When) ? "PrimaryButton" : "GhostButton");
        }
    }

    private async Task MeasureAsync()
    {
        var options = new BenchmarkOptions(Disk: false, Gpu3D: false, Quick: true,
            Label: string.IsNullOrWhiteSpace(_label.Text) ? null : _label.Text.Trim());
        ShowProgress("Medição rápida", BenchmarkService.StepsFor(options));
        var token = Cts!.Token;
        IProgress<ProgressEvent> progress = new Progress<ProgressEvent>(Progress.Report);
        try
        {
            var result = await Task.Run(() => BenchmarkService.Run(progress, token, options));
            BenchmarkHistory.Add(result);
            Progress.Finish($"Pontuação: {result.Score}", true, "Medição salva. Escolha-a como Base ou Depois.");
        }
        catch (OperationCanceledException)
        {
            Progress.Finish("Interrompido.", false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            Progress.Finish("Não foi possível medir.", false, ex.Message);
        }
    }

    protected override void OnProgressClosed() => Reload();

    private void Compare()
    {
        _result.Children.Clear();
        if (_before is null || _after is null)
        {
            SetStatus("Escolha uma medição como Base e outra como Depois.", "WarnBrush");
            return;
        }

        var deltas = BenchmarkComparison.Compare(_before, _after);
        SetStatus(BenchmarkComparison.Summary(deltas));
        _result.Children.Add(Heading("Diferenças"));
        foreach (var d in deltas)
        {
            string arrow = d.Verdict switch { DeltaVerdict.Better => "▲ melhor", DeltaVerdict.Worse => "▼ pior", _ => "= igual" };
            string brush = d.Verdict switch { DeltaVerdict.Better => "OkBrush", DeltaVerdict.Worse => "BadBrush", _ => "MutedBrush" };
            _result.Children.Add(Row(d.Name, $"{d.Before:0.##} → {d.After:0.##} {d.Unit}  ({d.Percent:+0.#;-0.#;0}%)  {arrow}", null, brush));
        }
    }
}
