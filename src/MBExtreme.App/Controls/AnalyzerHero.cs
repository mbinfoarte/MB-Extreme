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
    /// <summary>Pontinho que gira em volta do anel depois da análise (o anel "vivo").</summary>
    private readonly Grid _orbit = new() { Width = 132, Height = 132, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private Border _full = null!;
    private readonly System.Windows.Threading.DispatcherTimer _collapseTimer = new() { Interval = TimeSpan.FromSeconds(6) };
    private int _score;
    private Brush _scoreBrush = Brushes.Transparent;

    /// <summary>Recolhido: só o anel pequeno com a nota (clicar abre de novo).</summary>
    public bool Collapsed { get; private set; }

    /// <summary>Avisa o Painel para reorganizar os avisos quando o cartão recolhe ou abre.</summary>
    public event Action<bool>? CollapsedChanged;
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
        _orbit.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 8, Height = 8, Fill = new SolidColorBrush(Color.FromRgb(0xB9, 0xF6, 0xD9)), VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0.5, 0, 0),
        });
        ringHost.Children.Add(_orbit);
        ringHost.Children.Add(_center);
        grid.Children.Add(ringHost);
        var right = new StackPanel();
        right.Children.Add(_title);
        right.Children.Add(_subtitle);
        right.Children.Add(_rows);
        right.Children.Add(_actions);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        _full = new Border
        {
            CornerRadius = new CornerRadius(16), Padding = new Thickness(22, 20, 22, 20), BorderThickness = new Thickness(1),
            BorderBrush = Res("BorderBrush"),
            Background = new LinearGradientBrush(Color.FromRgb(0x17, 0x15, 0x0C), Color.FromRgb(0x10, 0x10, 0x12), 0),
            Child = grid,
        };
        Content = _full;
        // recolhe sozinho alguns segundos depois de uma análise sem nada para resolver (não enquanto o mouse está em cima)
        _collapseTimer.Tick += (_, _) =>
        {
            if (IsMouseOver) return;
            _collapseTimer.Stop();
            SetCollapsed(true);
        };
        Unloaded += (_, _) => _collapseTimer.Stop();
        ShowIdle();
    }

    /// <summary>Recolhe (só o anel pequeno com a nota) ou abre a análise completa.</summary>
    public void SetCollapsed(bool collapsed)
    {
        _collapseTimer.Stop();
        if (collapsed && _report is null) return;
        if (Collapsed == collapsed) return;
        Collapsed = collapsed;
        if (collapsed)
        {
            var compact = CompactView();
            Content = compact;
            Motion.Enter(compact, fromY: -6);
        }
        else
        {
            Content = _full;
            Motion.Enter(_full, fromY: -6);
        }
        CollapsedChanged?.Invoke(collapsed);
    }

    /// <summary>O selo recolhido: anel pequeno com a nota, um pontinho girando e um brilho que pulsa devagar.</summary>
    private FrameworkElement CompactView()
    {
        var ring = new RingGauge { Width = 50, Height = 50, Thickness = 5, Fill = _scoreBrush };
        ring.Jump(_score);
        var orbit = new Grid { Width = 50, Height = 50, IsHitTestVisible = false };
        orbit.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 5, Height = 5, Fill = new SolidColorBrush(Color.FromRgb(0xB9, 0xF6, 0xD9)), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center,
        });
        Motion.Spin(orbit, 5);
        var halo = new System.Windows.Shapes.Ellipse { Width = 50, Height = 50, Stroke = _scoreBrush, StrokeThickness = 2, Opacity = 0.4 };
        if (Motion.Enabled)
        {
            halo.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0.05, 0.6, new Duration(TimeSpan.FromSeconds(1.3)))
            { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
            Motion.Breathe(halo, 1.18, 1.3);
        }
        var ringHost = new Grid { Width = 50, Height = 50 };
        ringHost.Children.Add(halo);
        ringHost.Children.Add(ring);
        ringHost.Children.Add(orbit);
        ringHost.Children.Add(new TextBlock { Text = _score.ToString(), FontSize = 15, FontWeight = FontWeights.Bold, Foreground = _scoreBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0) };
        text.Children.Add(new TextBlock { Text = _score >= 90 ? "PC em dia" : HealthScore.Headline(_score), FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Res("TextBrush") });
        string when = _report is null ? "" : $"análise às {_report.Finished.ToLocalTime():HH:mm} · ";
        text.Children.Add(new TextBlock { Text = when + "abrir", FontSize = 11, Foreground = Res("MutedBrush") });

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(ringHost);
        row.Children.Add(text);
        var button = new Button { Cursor = Cursors.Hand, Content = row, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, ToolTip = "Abrir a análise" };
        button.Template = PillTemplate();
        System.Windows.Automation.AutomationProperties.SetName(button, $"Saúde do PC {_score}. Abrir a análise");
        button.Click += (_, _) => SetCollapsed(false);
        return button;
    }

    private static ControlTemplate PillTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(999));
        border.SetValue(Border.BackgroundProperty, Application.Current.FindResource("Surface2Brush"));
        border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x1E, 0x4A, 0x36)));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.PaddingProperty, new Thickness(6, 6, 16, 6));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        return template;
    }

    private void Orbit(bool on)
    {
        if (on && Motion.Enabled)
        {
            _orbit.Visibility = Visibility.Visible;
            Motion.Spin(_orbit, 6);
        }
        else
        {
            Motion.Stop(_orbit);
            _orbit.Visibility = Visibility.Collapsed;
        }
    }

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    /// <summary>Brilho em volta do anel que pulsa (parado: chama para o ANALISAR; trabalhando: mostra atividade).</summary>
    private void Halo(bool on, Brush? color = null)
    {
        _halo.Stroke = color ?? Yellow;
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
        Orbit(false);
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
        SetCollapsed(false);
        _busy = true;
        Orbit(false);
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
        _score = score;
        _scoreBrush = color;
        Motion.Stop(_ring);
        Halo(true, color);        // brilho da cor da nota, pulsando devagar
        Orbit(true);
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
        // o que pede ação fica em linhas inteiras (com Corrigir/Resolver/Ignorar); o resto vira 3 colunas curtas
        var compact = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var run in _report.Checks)
        {
            if (NeedsAction(run, ignored)) _rows.Children.Add(CheckRow(run, interactive: true));
            else compact.Children.Add(CompactRow(run, ignored));
        }
        if (compact.Children.Count > 0) _rows.Children.Add(compact);
        Motion.Cascade(_rows, 30);

        _actions.Children.Clear();
        if (_boxes.Count > 0) _actions.Children.Add(Action("Corrigir tudo", true, () => FixAsync(_boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Finding).ToList())));
        _actions.Children.Add(Action("Analisar de novo", false, AnalyzeAsync));
        _actions.Children.Add(Action("Análise profunda", false, () => { MainWindow.Current?.Go("diagnose", autoStart: true, deep: true); return Task.CompletedTask; },
            "Imagem do Windows, arquivos do sistema e disco, só lendo (10 a 40 minutos). Abre no Painel inteligente."));
        Motion.Cascade(_actions, 50);
        _actions.Children.Add(Action("Ver histórico", false, () => { MainWindow.Current?.Go("diagnose"); return Task.CompletedTask; }));
        if (!_report.Cancelled)
            _actions.Children.Add(Action("Recolher", false, () => { SetCollapsed(true); return Task.CompletedTask; }, "Deixa só o anel da saúde no topo do Painel."));
        if (open.Count == 0 && !_report.Cancelled) _collapseTimer.Start();
        if (ignored.Count > 0)
            _actions.Children.Add(Action($"Mostrar ignorados ({ignored.Count})", false, () =>
            {
                AppServices.Settings.IgnoredFindings.Clear();
                AppServices.Settings.Save();
                ShowResult();
                return Task.CompletedTask;
            }));
    }

    private static bool NeedsAction(CheckRun run, ISet<string> ignored) =>
        run.Findings.Any(f => f.Severity >= Severity.Info && !ignored.Contains(f.Id));

    /// <summary>Verificação sem nada para fazer: bolinha (verde ok, cinza ignorado ou não verificado), nome e uma linha curta.</summary>
    private static UIElement CompactRow(CheckRun run, ISet<string> ignored)
    {
        bool anyIgnored = run.Findings.Any(f => f.Severity >= Severity.Info && ignored.Contains(f.Id));
        bool notVerified = run.Status is CheckStatus.Unavailable or CheckStatus.Error or CheckStatus.Cancelled;
        string detail = anyIgnored ? string.Join(" · ", run.Findings.Where(f => ignored.Contains(f.Id)).Select(f => f.Title)) + " (ignorado)"
            : notVerified ? DiagnosticText.StatusText(run.Status) : "Tudo certo";
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 8, Height = 8, Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center,
            Fill = anyIgnored || notVerified ? new SolidColorBrush(Color.FromRgb(0x52, 0x52, 0x5B)) : Res("OkBrush"),
        };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = run.Title, FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock
        {
            Text = detail, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Res("MutedBrush"),
            FontStyle = anyIgnored ? FontStyles.Italic : FontStyles.Normal,
        });
        var dock = new DockPanel();
        DockPanel.SetDock(dot, Dock.Left);
        dock.Children.Add(dot);
        dock.Children.Add(text);
        return new Border
        {
            Background = Res("Surface2Brush"), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 8, 6),
            Child = dock, ToolTip = run.Title + "\n" + detail,
        };
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
        _collapseTimer.Stop();
        Orbit(false);
        Motion.Stop(_ring);
        Motion.Spin(_ring, 1.6);
        Halo(true);
        _title.Text = "Corrigindo...";
        _subtitle.Text = "Criando um ponto de restauração...";
        try
        {
            if (chosen.Any(f => f.Fix!.ChangesSystem))
                await Task.Run(() => RestorePointService.TryCreate("MB Extreme - antes das correções do Painel"));
            var results = await Task.Run(() => OperationQueue.Shared.Run(operations, UiProgress.Text(t => _subtitle.Text = t), CancellationToken.None));
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
