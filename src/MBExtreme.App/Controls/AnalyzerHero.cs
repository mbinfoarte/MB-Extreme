using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MBExtreme.Core.Diagnostics;
using MBExtreme.Core.Operations;
using MBExtreme.Core.Windows;
using MBExtreme.UI.Pages;

namespace MBExtreme.UI.Controls;

/// <summary>Anel de progresso desenhado na hora (nota de saúde, uso de memória e de disco).</summary>
internal sealed class RingGauge : FrameworkElement
{
    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(nameof(Percent), typeof(double), typeof(RingGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, null, (_, v) => Math.Clamp((double)v, 0, 100)));

    /// <summary>Quanto do anel está cheio (0 a 100). Pode ser animado.</summary>
    public double Percent { get => (double)GetValue(PercentProperty); set => SetValue(PercentProperty, value); }
    public Brush Fill { get => _fill; set { _fill = value; InvalidateVisual(); } }
    private Brush _fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00));
    public double Thickness { get; set; } = 10;

    /// <summary>Muda na hora, cancelando uma animação em andamento (senão ela seguraria o valor antigo).</summary>
    public void Jump(double value)
    {
        BeginAnimation(PercentProperty, null);
        Percent = value;
    }

    /// <summary>Anima até o valor (ou muda na hora, com as animações desligadas).</summary>
    public void AnimateTo(double value) => Motion.To(this, PercentProperty, value);

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= Thickness * 2) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double r = size / 2 - Thickness / 2;
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), Thickness), center, r, r);
        double sweep = 360 * Percent / 100;
        if (sweep <= 0.1) return;
        var pen = new Pen(Fill, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (sweep >= 359.9) { dc.DrawEllipse(null, pen, center, r, r); return; }
        double start = -90, end = start + sweep;
        Point P(double deg) => new(center.X + r * Math.Cos(deg * Math.PI / 180), center.Y + r * Math.Sin(deg * Math.PI / 180));
        var fig = new PathFigure { StartPoint = P(start), IsClosed = false };
        fig.Segments.Add(new ArcSegment(P(end), new Size(r, r), 0, sweep > 180, SweepDirection.Clockwise, true));
        dc.DrawGeometry(null, pen, new PathGeometry(new[] { fig }));
    }
}

/// <summary>
/// Topo do Painel: botão redondo "ANALISAR"; durante a análise os itens aparecem um a um (pode parar); no fim, a nota de saúde (0 a 100)
/// e, em cada item, "Corrigir" (marcado para o Corrigir tudo), "Resolver" (corrige só ele ou abre a tela certa) e "Ignorar".
/// </summary>
internal sealed class AnalyzerHero : UserControl
{
    private static readonly Brush Yellow = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00));
    private readonly RingGauge _ring = new() { Width = 132, Height = 132, Thickness = 9 };
    private readonly System.Windows.Shapes.Ellipse _halo = new() { Width = 132, Height = 132, StrokeThickness = 2, Opacity = 0 };
    private readonly Grid _center = new();
    private readonly TextBlock _title = new() { FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _subtitle = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
    private readonly StackPanel _rows = new() { Margin = new Thickness(0, 10, 0, 0) };
    private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
    private readonly List<(CheckBox Box, Finding Finding)> _boxes = new();
    private CancellationTokenSource? _cts;
    private DiagnosticReport? _report;
    private bool _busy;

    public AnalyzerHero()
    {
        _subtitle.Foreground = Res("MutedBrush");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var ringHost = new Grid { Width = 132, Height = 132, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
        _halo.Stroke = Yellow;
        ringHost.Children.Add(_halo);
        ringHost.Children.Add(_ring);
        ringHost.Children.Add(_center);
        grid.Children.Add(ringHost);
        var right = new StackPanel();
        right.Children.Add(_title);
        right.Children.Add(_subtitle);
        right.Children.Add(_rows);
        right.Children.Add(_actions);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        Content = new Border
        {
            CornerRadius = new CornerRadius(16), Padding = new Thickness(22, 20, 22, 20), BorderThickness = new Thickness(1),
            BorderBrush = Res("BorderBrush"),
            Background = new LinearGradientBrush(Color.FromRgb(0x17, 0x15, 0x0C), Color.FromRgb(0x10, 0x10, 0x12), 0),
            Child = grid,
        };
        ShowIdle();
    }

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    /// <summary>Brilho em volta do anel que pulsa (parado: chama para o ANALISAR; trabalhando: mostra atividade).</summary>
    private void Halo(bool on)
    {
        if (!on || !Motion.Enabled)
        {
            _halo.BeginAnimation(OpacityProperty, null);
            _halo.Opacity = 0;
            Motion.Stop(_halo);
            return;
        }
        var fade = new System.Windows.Media.Animation.DoubleAnimation(0.1, 0.55, new Duration(TimeSpan.FromSeconds(1.4)))
        { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever };
        _halo.BeginAnimation(OpacityProperty, fade);
        Motion.Breathe(_halo, 1.1, 1.4);
    }

    private void ShowIdle()
    {
        Motion.Stop(_ring);
        Motion.Spin(_ring, 9);   // degradê girando devagar
        Halo(true);
        _ring.Jump(100);
        _ring.Fill = new LinearGradientBrush(Color.FromRgb(0xFF, 0xDD, 0x33), Color.FromRgb(0xFF, 0x8A, 0x00), 45);
        _center.Children.Clear();
        var button = new Button { Width = 104, Height = 104, Cursor = Cursors.Hand, ToolTip = "Analisa o PC sem mudar nada (menos de um minuto)" };
        button.Template = RoundTemplate();
        var label = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        label.Children.Add(new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brushes.Black });
        label.Children.Add(new TextBlock { Text = "ANALISAR", FontWeight = FontWeights.Bold, FontSize = 13, Margin = new Thickness(0, 4, 0, 0), Foreground = Brushes.Black });
        button.Content = label;
        System.Windows.Automation.AutomationProperties.SetName(button, "Analisar o PC");
        button.Click += async (_, _) => await AnalyzeAsync();
        _center.Children.Add(button);
        Motion.Breathe(button, 1.04, 1.6);

        _title.Text = "Vamos deixar seu PC em dia?";
        _subtitle.Text = "Um clique confere arquivos inúteis, programas que abrem com o Windows, ajustes, segurança, discos, drivers, espaço livre e Windows Update. Nada é alterado durante a análise.";
        _rows.Children.Clear();
        _actions.Children.Clear();
        var last = DiagnosticPage.History.Load().FirstOrDefault();
        if (last is not null)
            _actions.Children.Add(Muted($"Última análise: {last.Started.ToLocalTime():dd/MM HH:mm} · {last.Critical + last.Problems} problema(s), {last.Warnings} atenção"));
    }

    private static ControlTemplate RoundTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(52));
        border.SetValue(Border.BackgroundProperty, Application.Current.FindResource("ButtonBrush"));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        return template;
    }

    private static TextBlock Muted(string text) => new() { Text = text, FontSize = 11.5, Foreground = Res("MutedBrush"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };

    private Button Action(string text, bool primary, Func<Task> click, string? tip = null)
    {
        var b = new Button
        {
            Content = text, Style = (Style)Application.Current.FindResource(primary ? "PrimaryButton" : "GhostButton"),
            Padding = new Thickness(14, 6, 14, 6), FontSize = 12.5, Margin = new Thickness(0, 0, 8, 0), ToolTip = tip,
        };
        b.Click += async (_, _) => await click();
        return b;
    }

    /// <summary>Análise rápida, com os itens aparecendo conforme cada verificação termina.</summary>
    public async Task AnalyzeAsync()
    {
        if (_busy) return;
        _busy = true;
        _cts = new CancellationTokenSource();
        _rows.Children.Clear();
        _actions.Children.Clear();
        _boxes.Clear();
        _center.Children.Clear();
        _center.Children.Add(new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 34, Foreground = Yellow, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        _ring.Fill = Yellow;
        _ring.Jump(0);
        _title.Text = "Analisando seu PC...";
        _subtitle.Text = "Leva menos de um minuto. Nada é alterado durante a análise.";
        var current = Muted("Começando...");
        _rows.Children.Add(current);
        var stop = Action("Parar análise", false, () => { _cts?.Cancel(); return Task.CompletedTask; });
        _actions.Children.Add(stop);

        var checks = WindowsChecks.All(AppServices.AllowedTweaks());
        int total = checks.Count(c => c.Depth == CheckDepth.Quick);
        int done = 0;
        var token = _cts.Token;
        var progress = new Progress<DiagnosticProgress>(p => current.Text = $"Conferindo: {p.Title}...");
        void OnCheck(CheckRun run) => Dispatcher.BeginInvoke(new System.Action(() =>
        {
            done++;
            _ring.AnimateTo(100.0 * done / Math.Max(1, total));
            var row = CheckRow(run, interactive: false);
            _rows.Children.Insert(Math.Max(0, _rows.Children.Count - 1), row);
            Motion.Enter(row, fromY: 6);
        }));
        try
        {
            var report = await Task.Run(() => DiagnosticPage.Runner.Run(checks, false, progress, token, forceFresh: true, onCheck: OnCheck));
            if (!report.Cancelled) DiagnosticPage.History.Add(report);
            _report = report;
        }
        finally
        {
            _busy = false;
        }
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);   // deixa as linhas pendentes entrarem
        ShowResult();
    }

    private ISet<string> Ignored => new HashSet<string>(AppServices.Settings.IgnoredFindings);

    private void ShowResult()
    {
        if (_report is null) { ShowIdle(); return; }
        var ignored = Ignored;
        int score = HealthScore.Of(_report.Findings, ignored);
        var color = HealthScore.Level(score) switch { 2 => Res("OkBrush"), 1 => Yellow, _ => Res("BadBrush") };
        _ring.Fill = color;
        Motion.Stop(_ring);
        Halo(false);
        _ring.Jump(0);
        _ring.AnimateTo(score);   // o anel enche até a nota
        _center.Children.Clear();
        var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var number = new TextBlock { Text = "0", FontSize = 38, FontWeight = FontWeights.Bold, Foreground = color, HorizontalAlignment = HorizontalAlignment.Center };
        center.Children.Add(number);
        Motion.CountUp(number, 0, score);
        center.Children.Add(new TextBlock { Text = "saúde do PC", FontSize = 11, Foreground = Res("MutedBrush"), HorizontalAlignment = HorizontalAlignment.Center });
        _center.Children.Add(center);
        System.Windows.Automation.AutomationProperties.SetName(_ring, $"Saúde do PC: {score} de 100");

        var open = _report.Findings.Where(f => f.IsProblem && !ignored.Contains(f.Id)).ToList();
        _title.Text = _report.Cancelled ? "Análise parada antes do fim" : HealthScore.Headline(score);
        _subtitle.Text = open.Count == 0
            ? $"Nada para resolver. Última análise: hoje às {_report.Finished.ToLocalTime():HH:mm}."
            : $"{open.Count} item(ns) para resolver. Os que têm a caixa \"Corrigir\" o MB Extreme resolve sozinho no \"Corrigir tudo\"; os outros abrem a tela certa pelo botão Resolver. Última análise: hoje às {_report.Finished.ToLocalTime():HH:mm}.";

        _rows.Children.Clear();
        _boxes.Clear();
        foreach (var run in _report.Checks) _rows.Children.Add(CheckRow(run, interactive: true));
        Motion.Cascade(_rows, 30);

        _actions.Children.Clear();
        if (_boxes.Count > 0) _actions.Children.Add(Action("Corrigir tudo", true, () => FixAsync(_boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Finding).ToList())));
        _actions.Children.Add(Action("Analisar de novo", false, AnalyzeAsync));
        _actions.Children.Add(Action("Análise profunda", false, () => { MainWindow.Current?.Go("diagnose", autoStart: true, deep: true); return Task.CompletedTask; },
            "Imagem do Windows, arquivos do sistema e disco, só lendo (10 a 40 minutos). Abre no Painel inteligente."));
        Motion.Cascade(_actions, 50);
        _actions.Children.Add(Action("Ver histórico", false, () => { MainWindow.Current?.Go("diagnose"); return Task.CompletedTask; }));
        if (ignored.Count > 0)
            _actions.Children.Add(Action($"Mostrar ignorados ({ignored.Count})", false, () =>
            {
                AppServices.Settings.IgnoredFindings.Clear();
                AppServices.Settings.Save();
                ShowResult();
                return Task.CompletedTask;
            }));
    }

    /// <summary>Uma linha por verificação: ícone, nome, o que achou e, no fim, as ações de cada achado.</summary>
    private UIElement CheckRow(CheckRun run, bool interactive)
    {
        var panel = new StackPanel();
        var ignored = Ignored;
        var findings = run.Findings.Where(f => f.Severity >= Severity.Info).ToList();
        if (findings.Count == 0)
        {
            bool notVerified = run.Status is CheckStatus.Unavailable or CheckStatus.Error or CheckStatus.Cancelled;
            panel.Children.Add(Line(notVerified ? "–" : "", notVerified ? Res("MutedBrush") : Res("OkBrush"), run.Title,
                notVerified ? DiagnosticText.StatusText(run.Status) + (run.Note.Length > 0 ? " — " + run.Note : "") : "Nenhum problema encontrado",
                Res("MutedBrush"), null));
            return panel;
        }
        foreach (var f in findings)
        {
            bool isIgnored = ignored.Contains(f.Id);
            var (glyph, brush) = isIgnored ? ("", Res("MutedBrush"))
                : f.Severity switch
                {
                    Severity.Critical or Severity.Problem => ("", Res("BadBrush")),
                    Severity.Warning => ("", Yellow),
                    _ => ("", Res("MutedBrush")),
                };
            FrameworkElement? actions = interactive && !isIgnored ? Actions(f) : null;
            panel.Children.Add(Line(glyph, brush, run.Title, f.Title + (isIgnored ? " (ignorado)" : ""), isIgnored ? Res("MutedBrush") : brush, actions, f.Detail));
        }
        return panel;
    }

    private static Grid Line(string glyph, Brush glyphBrush, string title, string detail, Brush detailBrush, FrameworkElement? actions, string? tip = null)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new TextBlock { Text = glyph, FontFamily = glyph.Length == 1 && glyph[0] > '' ? new FontFamily("Segoe MDL2 Assets") : null, FontSize = 12, Foreground = glyphBrush, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) });
        var text = new StackPanel { ToolTip = tip };
        text.Children.Add(new TextBlock { Text = title, FontSize = 12.5, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock { Text = detail, FontSize = 11.5, Foreground = detailBrush, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(text, 1);
        g.Children.Add(text);
        if (actions is not null) { Grid.SetColumn(actions, 2); g.Children.Add(actions); }
        return g;
    }

    private FrameworkElement Actions(Finding f)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (f.Fix?.IsAutomatic == true)
        {
            var box = new CheckBox { Content = "Corrigir", IsChecked = f.IsProblem, Foreground = Res("TextBrush"), FontSize = 11.5, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(box, "Corrigir no Corrigir tudo: " + f.Title);
            _boxes.Add((box, f));
            row.Children.Add(box);
        }
        if (f.Fix is { } fix)
        {
            var resolve = Small("Resolver", async () =>
            {
                if (fix.IsAutomatic) await FixAsync(new[] { f });
                else if (fix.Page is { } page) MainWindow.Current?.Go(page);
            });
            resolve.ToolTip = fix.Text;
            System.Windows.Automation.AutomationProperties.SetName(resolve, "Resolver: " + f.Title);
            row.Children.Add(resolve);
        }
        var ignore = Small("Ignorar", () =>
        {
            if (!AppServices.Settings.IgnoredFindings.Contains(f.Id)) AppServices.Settings.IgnoredFindings.Add(f.Id);
            AppServices.Settings.Save();
            ShowResult();
            return Task.CompletedTask;
        });
        ignore.ToolTip = "Não mostrar mais como problema (dá para voltar em \"Mostrar ignorados\").";
        System.Windows.Automation.AutomationProperties.SetName(ignore, "Ignorar: " + f.Title);
        row.Children.Add(ignore);
        return row;
    }

    private static Button Small(string text, Func<Task> click)
    {
        var b = new Button { Content = text, Style = (Style)Application.Current.FindResource("SideFooterButton"), Margin = new Thickness(4, 0, 0, 0), FontSize = 11.5 };
        b.Click += async (_, _) => await click();
        return b;
    }

    /// <summary>Corrige pela fila, uma coisa de cada vez, com ponto de restauração antes; depois analisa de novo.</summary>
    private async Task FixAsync(IReadOnlyList<Finding> chosen)
    {
        if (_busy) return;
        if (chosen.Count == 0) { _subtitle.Text = "Marque \"Corrigir\" em pelo menos um item."; return; }
        var operations = chosen.SelectMany(f => f.Fix!.Operations!()).ToList();
        if (operations.Count == 0) { await AnalyzeAsync(); return; }
        string list = string.Join("\n", operations.Select(o => "• " + o.Info.Title + (o.Info.Reversible ? " (com volta)" : " (sem volta)")));
        if (MessageBox.Show($"O MB Extreme vai fazer, uma de cada vez:\n\n{list}\n\nAntes, cria um ponto de restauração do Windows. Continuar?",
                "Corrigir", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _busy = true;
        _actions.Children.Clear();
        Motion.Stop(_ring);
        Motion.Spin(_ring, 1.6);
        Halo(true);
        _title.Text = "Corrigindo...";
        _subtitle.Text = "Criando um ponto de restauração...";
        try
        {
            if (chosen.Any(f => f.Fix!.ChangesSystem))
                await Task.Run(() => RestorePointService.TryCreate("MB Extreme - antes das correções do Painel"));
            var results = await Task.Run(() => OperationQueue.Shared.Run(operations, new Progress<string>(t => _subtitle.Text = t), CancellationToken.None));
            bool reboot = results.Any(r => r.State == OperationState.RebootRequired);
            if (reboot) MessageBox.Show("Pronto. Reinicie o computador para terminar algumas correções.", "Corrigir", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "Corrigir", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _busy = false;
        }
        await AnalyzeAsync();
    }
}
