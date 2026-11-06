using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MBExtreme.Core.Diagnostics;
using MBExtreme.Core.Licensing;
using MBExtreme.Core.Operations;
using MBExtreme.Core.Windows;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Painel inteligente: análise rápida (só leitura, menos de um minuto) e profunda (DISM, SFC e disco em modo "só verificar"),
/// com etapa e tempo decorrido. Você escolhe o que corrigir; as correções rodam pela fila, uma de cada vez, com ponto de restauração antes,
/// e no fim uma nova análise rápida mostra o antes e o depois.
/// </summary>
public sealed class DiagnosticPage : ToolPageBase
{
    private static readonly DiagnosticRunner Runner = new();
    private static readonly DiagnosticHistory History = new();
    private readonly Button _quick, _deep, _stop, _export, _fixSelected, _fixAll;
    private readonly TextBlock _progress = new() { FontSize = 13, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly List<(CheckBox Box, Finding Finding)> _boxes = new();
    private CancellationTokenSource? _cts;
    private DiagnosticReport? _report;
    private bool _busy;

    public DiagnosticPage() : base("Painel inteligente",
        "Analisa o computador sem mudar nada. Depois você escolhe o que corrigir: o programa explica cada item, cria um ponto de restauração antes e faz uma correção de cada vez.")
    {
        _quick = AddButton("Análise rápida", async (_, _) => await AnalyzeAsync(deep: false, fresh: true));
        _deep = AddButton("Análise profunda", async (_, _) => await AnalyzeAsync(deep: true, fresh: false), false);
        _stop = AddButton("Parar", (_, _) => _cts?.Cancel(), false);
        _export = AddButton("Exportar relatório", (_, _) => Export(), false);
        _fixSelected = AddButton("Corrigir selecionados", async (_, _) => await FixAsync(all: false), false);
        _fixAll = AddButton("Corrigir tudo", async (_, _) => await FixAsync(all: true), false);
        _deep.ToolTip = "Verifica a imagem do Windows (DISM), os arquivos do sistema (SFC) e o disco do Windows, só lendo. Pode levar de 10 a 30 minutos.";
        RefreshButtons();
        Loaded += (_, _) =>
        {
            if (_report is null && Runner.Last is { } last) Show(last, null);
            else if (_report is null) ShowHistory();
        };
    }

    /// <summary>Começa a análise rápida sozinho (botão "Analisar o PC" do Início).</summary>
    public async Task StartAsync() { if (!_busy) await AnalyzeAsync(deep: false, fresh: true); }

    private IReadOnlyList<MBExtreme.Core.Tweaks.Tweak> AllowedTweaks()
    {
        var license = AppServices.License;
        bool pro = !license.Config.Enforce || LicenseExpiry.IsPro(license.Status);
        return pro ? AppServices.Tweaks : AppServices.Tweaks.Where(t => !LicenseExpiry.ProTweakCategories.Contains(t.Category)).ToList();
    }

    private void RefreshButtons()
    {
        _quick.IsEnabled = _deep.IsEnabled = !_busy;
        _stop.IsEnabled = _busy;
        _export.IsEnabled = !_busy && _report is not null;
        bool fixable = !_busy && _report is not null && _report.Findings.Any(f => f.Fix?.IsAutomatic == true);
        _fixSelected.IsEnabled = _fixAll.IsEnabled = fixable;
    }

    private async Task AnalyzeAsync(bool deep, bool fresh, DiagnosticReport? before = null)
    {
        if (_busy) return;
        _busy = true;
        _cts = new CancellationTokenSource();
        RefreshButtons();
        Body.Children.Clear();
        Body.Children.Add(_progress);
        _progress.Text = deep ? "Começando a análise profunda..." : "Começando a análise rápida...";
        _progress.Foreground = Brush("TextBrush");
        SetStatus(deep ? "Análise profunda: pode levar de 10 a 30 minutos. Nada é alterado no Windows." : "Análise rápida: só leitura, nada é alterado.");
        var progress = new Progress<DiagnosticProgress>(p =>
            _progress.Text = $"Etapa {p.Step} de {p.Total}: {p.Title}  ·  {DiagnosticText.Duration(p.Elapsed)}");
        var checks = WindowsChecks.All(AllowedTweaks());
        var token = _cts.Token;
        try
        {
            var report = await Task.Run(() => Runner.Run(checks, deep, progress, token, fresh));
            if (!report.Cancelled) History.Add(report);
            _report = report;
            Show(report, before);
        }
        finally
        {
            _busy = false;
            RefreshButtons();
        }
    }

    private void Show(DiagnosticReport report, DiagnosticReport? before)
    {
        _report = report;
        Body.Children.Clear();
        _boxes.Clear();

        int critical = report.Count(Severity.Critical), problems = report.Count(Severity.Problem), warnings = report.Count(Severity.Warning);
        string headline = critical + problems + warnings == 0 ? "Tudo certo: nenhum problema encontrado."
            : $"{critical + problems} problema(s) e {warnings} ponto(s) de atenção.";
        var summary = new StackPanel();
        summary.Children.Add(new TextBlock { Text = headline, FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        summary.Children.Add(Muted($"Análise {(report.Deep ? "profunda" : "rápida")} de {report.Started.ToLocalTime():dd/MM HH:mm} em {DiagnosticText.Duration(report.Took)}"
                                   + (report.Cancelled ? " — parada antes do fim; o que faltou aparece como \"não verificado\"." : ".")));
        if (before is not null)
            summary.Children.Add(new TextBlock
            {
                Text = $"Antes: {before.Problems} item(ns) com problema ou atenção  →  Depois: {report.Problems}",
                FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brush(report.Problems < before.Problems ? "OkBrush" : "WarnBrush"), Margin = new Thickness(0, 6, 0, 0),
            });
        Body.Children.Add(CardOf(summary));

        var findings = report.Findings;
        if (findings.Count > 0)
        {
            Body.Children.Add(Heading("O que foi encontrado"));
            Body.Children.Add(Muted("Marque o que quer que o programa corrija. Itens com \"Abrir\" você resolve na página indicada. Nada é corrigido sem você pedir.", 11.5));
            foreach (var finding in findings) Body.Children.Add(FindingCard(finding));
        }

        var notVerified = report.Checks.Where(c => c.Status is CheckStatus.Unavailable or CheckStatus.Error or CheckStatus.Cancelled).ToList();
        if (notVerified.Count > 0)
        {
            Body.Children.Add(Heading("Não verificado"));
            Body.Children.Add(Muted("Não é problema do seu PC: são verificações que este Windows não permite ou que não chegaram a rodar.", 11.5));
            foreach (var c in notVerified) Body.Children.Add(Row(c.Title, $"{DiagnosticText.StatusText(c.Status)} — {c.Note}"));
        }

        var verified = report.Checks.Where(c => c.Status is CheckStatus.Done or CheckStatus.Reused && c.Findings.Count == 0).ToList();
        if (verified.Count > 0)
        {
            Body.Children.Add(Heading("Sem problemas"));
            Body.Children.Add(Muted(string.Join("  ·  ", verified.Select(c => "✔ " + c.Title)), 12));
        }

        if (!report.Deep)
            Body.Children.Add(Muted("Para ir mais fundo (imagem do Windows, arquivos do sistema e disco), use \"Análise profunda\". Ela reaproveita esta análise rápida.", 11.5));

        SetStatus(headline, critical + problems > 0 ? "WarnBrush" : "OkBrush");
        RefreshButtons();
    }

    private UIElement FindingCard(Finding finding)
    {
        var (tag, brush) = finding.Severity switch
        {
            Severity.Critical => ("CRÍTICO", "BadBrush"),
            Severity.Problem => ("Problema", "BadBrush"),
            Severity.Warning => ("Atenção", "WarnBrush"),
            _ => ("Informação", "MutedBrush"),
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new Border
        {
            Child = new TextBlock { Text = tag, FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Brush(brush) },
            BorderBrush = Brush(brush), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 0, 6, 1), Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        head.Children.Add(new TextBlock { Text = finding.Title, FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        text.Children.Add(head);
        text.Children.Add(Muted(finding.Detail));
        if (finding.Fix?.IsAutomatic == true)
        {
            var box = new CheckBox { Content = finding.Fix.Text, IsChecked = finding.Severity >= Severity.Warning, Foreground = Brush("TextBrush"), Margin = new Thickness(0, 6, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetName(box, finding.Fix.Text + ": " + finding.Title);
            _boxes.Add((box, finding));
            text.Children.Add(box);
        }
        grid.Children.Add(text);
        if (finding.Fix?.Page is { } page)
        {
            var open = SmallButton("Abrir", (_, _) => MainWindow.Current?.Go(page));
            System.Windows.Automation.AutomationProperties.SetName(open, "Abrir a página para " + finding.Title);
            Grid.SetColumn(open, 1);
            open.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(open);
        }
        return CardOf(grid);
    }

    private async Task FixAsync(bool all)
    {
        if (_busy || _report is null) return;
        var chosen = (all ? _boxes : _boxes.Where(b => b.Box.IsChecked == true)).Select(b => b.Finding).ToList();
        if (chosen.Count == 0) { SetStatus("Marque pelo menos um item para corrigir.", "WarnBrush"); return; }
        var operations = chosen.SelectMany(f => f.Fix!.Operations!()).ToList();
        if (operations.Count == 0) { SetStatus("Não há nada a corrigir nos itens marcados (talvez já tenha sido corrigido).", "OkBrush"); return; }

        bool irreversible = operations.Any(o => !o.Info.Reversible);
        string list = string.Join("\n", operations.Select(o => "• " + o.Info.Title + (o.Info.Reversible ? " (com volta)" : " (sem volta)")));
        if (MessageBox.Show($"O programa vai fazer, uma de cada vez:\n\n{list}\n\nAntes, cria um ponto de restauração do Windows." +
                            (irreversible ? "\nItens \"sem volta\" (limpeza, reparos) não podem ser desfeitos pela Central Desfazer." : "") + "\n\nContinuar?",
                "Corrigir", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        var before = _report;
        _busy = true;
        _cts = new CancellationTokenSource();
        RefreshButtons();
        Body.Children.Clear();
        Body.Children.Add(Heading("Corrigindo"));
        var rows = new Dictionary<IOperation, TextBlock>();
        foreach (var op in operations)
        {
            var line = new TextBlock { Text = "◻  " + op.Info.Title + " — na fila", FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2), Foreground = Brush("MutedBrush") };
            rows[op] = line;
            Body.Children.Add(line);
        }
        Body.Children.Add(_progress);
        void OnChanged(QueueItem item) => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!rows.TryGetValue(item.Operation, out var line)) return;
            (string mark, string brush) = item.State switch
            {
                OperationState.Running => ("⏳", "TextBrush"),
                OperationState.Succeeded => ("✔", "OkBrush"),
                OperationState.RebootRequired => ("✔", "WarnBrush"),
                OperationState.Skipped => ("–", "MutedBrush"),
                OperationState.Pending => ("◻", "MutedBrush"),
                _ => ("✖", "BadBrush"),
            };
            line.Text = $"{mark}  {item.Operation.Info.Title} — {item.Message}";
            line.Foreground = Brush(brush);
        }));

        IReadOnlyList<QueueItem> results;
        var token = _cts.Token;
        OperationQueue.Shared.Changed += OnChanged;
        try
        {
            SetStatus("Criando um ponto de restauração...");
            if (chosen.Any(f => f.Fix!.ChangesSystem))
            {
                var (ok, error) = await Task.Run(() => RestorePointService.TryCreate("MB Extreme - antes das correções do Painel"));
                if (!ok) _progress.Text = "Ponto de restauração não criado (" + error + "). As correções seguem; os ajustes têm volta pela Central Desfazer.";
            }
            SetStatus("Corrigindo, um item de cada vez...");
            results = await Task.Run(() => OperationQueue.Shared.Run(operations, new Progress<string>(t => _progress.Text = t), token));
        }
        catch (InvalidOperationException ex)
        {
            SetStatus(ex.Message, "WarnBrush");
            return;
        }
        finally
        {
            OperationQueue.Shared.Changed -= OnChanged;
            _busy = false;
            RefreshButtons();
        }

        int ok2 = results.Count(r => r.State is OperationState.Succeeded or OperationState.RebootRequired or OperationState.Skipped);
        int failed = results.Count(r => r.State == OperationState.Failed);
        int cancelled = results.Count(r => r.State == OperationState.Cancelled);
        bool reboot = results.Any(r => r.State == OperationState.RebootRequired);
        SetStatus($"{ok2} feito(s), {failed} com erro, {cancelled} cancelado(s).{(reboot ? " Reinicie o computador para terminar." : "")} Conferindo de novo...",
            failed == 0 ? "OkBrush" : "WarnBrush");
        await AnalyzeAsync(deep: false, fresh: true, before: before);   // o "depois": nova análise rápida
    }

    private void ShowHistory()
    {
        Body.Children.Clear();
        var history = History.Load();
        Body.Children.Add(CardOf(Muted("Clique em \"Análise rápida\" para começar. Leva menos de um minuto e não muda nada no Windows.", 13)));
        if (history.Count == 0) return;
        Body.Children.Add(Heading("Análises anteriores"));
        foreach (var h in history.Take(10))
            Body.Children.Add(Row($"{h.Started.ToLocalTime():dd/MM/yyyy HH:mm} · análise {(h.Deep ? "profunda" : "rápida")}",
                $"{h.Critical + h.Problems} problema(s), {h.Warnings} atenção · {DiagnosticText.Duration(h.Took)}" + (h.Titles.Count > 0 ? "\n" + string.Join(" · ", h.Titles.Take(5)) : "")));
    }

    private void Export()
    {
        if (_report is null) return;
        string folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string path = Path.Combine(folder, $"MB Extreme - análise {DateTime.Now:yyyy-MM-dd HHmm}.txt");
        try
        {
            File.WriteAllText(path, DiagnosticText.Report(_report, Environment.MachineName, Environment.OSVersion.VersionString));
            SetStatus("Relatório salvo em " + path, "OkBrush");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("Não foi possível salvar o relatório: " + ex.Message, "BadBrush");
        }
    }
}
