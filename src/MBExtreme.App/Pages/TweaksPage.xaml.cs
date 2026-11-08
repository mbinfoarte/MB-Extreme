using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MBExtreme.Core.Progress;
using MBExtreme.Core.Tweaks;

namespace MBExtreme.UI.Pages;

/// <summary>Lista os ajustes de uma ou mais categorias, com caixa de marcação, risco e aviso.</summary>
public partial class TweaksPage : UserControl
{
    private readonly TweakCategory[] _categories;
    private readonly Dictionary<Tweak, CheckBox> _boxes = new();
    private CancellationTokenSource? _cts;
    private readonly List<(Tweak Tweak, Border Row, TextBlock? Header)> _rows = new();
    private readonly List<ToggleButton> _filterChips = new();
    private TweakGoal _goal = TweakGoal.None;

    public TweaksPage(string title, string subtitle, params TweakCategory[] categories)
    {
        InitializeComponent();
        TitleText.Text = title;
        SubtitleText.Text = subtitle;
        _categories = categories;

        ProgressView.StopRequested += (_, _) => _cts?.Cancel();
        ProgressView.CloseRequested += (_, _) =>
        {
            MainView.Visibility = Visibility.Visible;
            BuildRows();
        };
        Loaded += (_, _) => BuildRows();
        BuildFilters();
    }

    /// <summary>Chips por objetivo (só na tela com várias categorias; nas telas de uma categoria fica só a busca).</summary>
    private void BuildFilters()
    {
        FilterHost.Children.Clear();
        _filterChips.Clear();
        if (_categories.Length <= 1) return;
        foreach (var (goal, title) in TweakFilter.Goals)
        {
            var chip = new ToggleButton
            {
                Style = (Style)FindResource("ChipToggle"), Content = title, IsChecked = goal == _goal,
                Margin = new Thickness(0, 0, 8, 6), Tag = goal,
            };
            chip.Click += (_, _) =>
            {
                _goal = (TweakGoal)chip.Tag;
                foreach (var c in _filterChips) c.IsChecked = (TweakGoal)c.Tag == _goal;
                ApplyFilter();
            };
            _filterChips.Add(chip);
            FilterHost.Children.Add(chip);
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    /// <summary>Mostra só as linhas que passam no objetivo e na busca; esconde o título da categoria que ficou vazia.</summary>
    private void ApplyFilter()
    {
        string search = SearchBox.Text;
        var visibleHeaders = new HashSet<TextBlock>();
        int shown = 0;
        foreach (var (tweak, row, header) in _rows)
        {
            bool visible = TweakFilter.Matches(tweak, _goal, search);
            row.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (visible) { shown++; if (header is not null) visibleHeaders.Add(header); }
        }
        foreach (var header in _rows.Select(r => r.Header).OfType<TextBlock>().Distinct())
            header.Visibility = visibleHeaders.Contains(header) ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = shown == 0 && _rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool? SafeIsApplied(Tweak tweak)
    {
        try
        {
            return tweak.IsApplied();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException
                                       or InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private int _buildVersion;

    private async void BuildRows()
    {
        // Ler o estado de cada ajuste pode chamar o powercfg; isso roda fora da thread da interface.
        int version = ++_buildVersion;
        // na tela com todas as categorias, os ajustes Pro (desempenho, jogos e rede) só aparecem para quem tem o plano Pro
        var source = _categories.Length > 1 ? AppServices.AllowedTweaks() : AppServices.Tweaks;
        var candidates = source.Where(t => _categories.Contains(t.Category)).ToList();
        // ajustes de uma marca de placa de vídeo só aparecem em PCs com essa marca
        var states = await Task.Run(() => candidates.Where(t => t.IsAvailable).Select(t => (Tweak: t, Applied: SafeIsApplied(t))).ToList());
        if (version != _buildVersion) return;

        RowsHost.Children.Clear();
        _boxes.Clear();
        _rows.Clear();
        CounterText.Text = TweakFilter.Counter(states.Count(x => x.Applied == true), states.Count);
        CounterText.ToolTip = "Otimizações desta tela que já estão aplicadas no PC.";
        if (_categories.Length > 1 && source.Count < AppServices.Tweaks.Count)
            RowsHost.Children.Add(new TextBlock
            {
                Text = "Os ajustes de desempenho, jogos e rede fazem parte do plano Pro e aparecem aqui quando você assina.",
                FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 8), Foreground = (Brush)FindResource("WarnBrush"),
            });

        TweakCategory? lastCategory = null;
        TextBlock? currentHeader = null;
        foreach (var (tweak, applied) in states.OrderBy(x => Array.IndexOf(_categories, x.Tweak.Category)))
        {
            // tela "Otimizações" (todas as categorias): um título por assunto
            if (_categories.Length > 1 && tweak.Category != lastCategory)
            {
                lastCategory = tweak.Category;
                currentHeader = new TextBlock
                {
                    Text = CategoryTitle(tweak.Category), FontSize = 15, FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)), Margin = new Thickness(2, 14, 0, 8),
                };
                RowsHost.Children.Add(currentHeader);
            }
            var content = BuildContent(tweak);
            content.Margin = new Thickness(8, 0, 0, 0);

            var box = new CheckBox
            {
                IsChecked = applied == true,
                Content = content,
                VerticalContentAlignment = VerticalAlignment.Top,
                Foreground = (Brush)FindResource("TextBrush"),
            };

            var row = new Border
            {
                Style = (Style)FindResource("Card"),
                Margin = new Thickness(0, 0, 0, 10),
                Child = box,
            };
            RowsHost.Children.Add(row);
            _boxes[tweak] = box;
            _rows.Add((tweak, row, currentHeader));
        }

        EmptyText = new TextBlock
        {
            Text = "Nenhuma otimização com esse filtro ou essa busca.", FontSize = 13, Margin = new Thickness(2, 8, 0, 8),
            Foreground = (Brush)FindResource("MutedBrush"), Visibility = Visibility.Collapsed,
        };
        RowsHost.Children.Add(EmptyText);
        ApplyFilter();
    }

    private TextBlock EmptyText { get; set; } = new();

    private static string CategoryTitle(TweakCategory category) => category switch
    {
        TweakCategory.Performance => "Desempenho",
        TweakCategory.Gaming => "Jogos e placa de vídeo",
        TweakCategory.Visual => "Efeitos visuais",
        TweakCategory.Privacy => "Privacidade, Iniciar e barra de tarefas",
        TweakCategory.Network => "Rede",
        TweakCategory.Storage => "Armazenamento",
        TweakCategory.Boot => "Inicialização e desligamento",
        TweakCategory.Peripherals => "Mouse e teclado",
        _ => category.ToString(),
    };

    private StackPanel BuildContent(Tweak tweak)
    {
        var panel = new StackPanel();

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock { Text = tweak.Title, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(Badge(RiskText(tweak.Risk), RiskBrush(tweak.Risk)));
        if (tweak.RequiresRestart) header.Children.Add(Badge("reiniciar", "MutedBrush"));
        if (tweak.Badge.Length > 0) header.Children.Add(Badge(tweak.Badge, "AccentBrush"));

        string more = string.Join("\n", new[] { tweak.Details, tweak.CompatibilityText }.Where(x => x.Length > 0));
        TextBlock? details = null;
        if (more.Length > 0)
        {
            var detailLine = Line(more, "MutedBrush", 12);
            detailLine.Visibility = Visibility.Collapsed;
            details = detailLine;
            var info = new Button
            {
                Content = "i", Width = 22, Height = 22, Padding = new Thickness(0), Margin = new Thickness(10, 0, 0, 0),
                FontWeight = FontWeights.Bold, FontSize = 12, Cursor = System.Windows.Input.Cursors.Hand,
                Style = (Style)FindResource("GhostButton"), ToolTip = "Como funciona", VerticalAlignment = VerticalAlignment.Center,
            };
            info.Click += (_, e) =>
            {
                detailLine.Visibility = detailLine.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                e.Handled = true;
            };
            header.Children.Add(info);
        }
        panel.Children.Add(header);

        panel.Children.Add(Line(tweak.Description, "TextBrush", 13));
        if (tweak.Effect.Length > 0) panel.Children.Add(Line("Efeito: " + tweak.Effect, "MutedBrush", 12));
        if (tweak.Warning.Length > 0) panel.Children.Add(Line("Atenção: " + tweak.Warning, "WarnBrush", 12));
        if (details is not null) panel.Children.Add(details);
        return panel;
    }

    private TextBlock Line(string text, string brush, double size) => new()
    {
        Text = text,
        FontSize = size,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 4, 0, 0),
        Foreground = (Brush)FindResource(brush),
    };

    private Border Badge(string text, string brush) => new()
    {
        Margin = new Thickness(10, 0, 0, 0),
        Padding = new Thickness(8, 2, 8, 2),
        CornerRadius = new CornerRadius(10),
        Background = (Brush)FindResource("Surface2Brush"),
        Child = new TextBlock { Text = text, FontSize = 11, Foreground = (Brush)FindResource(brush) },
    };

    private static string RiskText(TweakRisk risk) => risk switch
    {
        TweakRisk.Low => "risco baixo",
        TweakRisk.Medium => "risco médio",
        _ => "risco alto",
    };

    private static string RiskBrush(TweakRisk risk) => risk switch
    {
        TweakRisk.Low => "OkBrush",
        TweakRisk.Medium => "WarnBrush",
        _ => "BadBrush",
    };

    /// <summary>Recomendados, Seguros (risco baixo), Moderados (médio), Avançados (alto), Todos ou Nenhum.</summary>
    private void RecommendedButton_Click(object sender, RoutedEventArgs e)
    {
        string tag = (sender as FrameworkElement)?.Tag as string ?? "Recommended";
        if (tag == "None")
        {
            foreach (var (_, box) in _boxes) box.IsChecked = false;
            StatusText.Text = "Nada marcado.";
            StatusText.Foreground = (Brush)FindResource("MutedBrush");
            return;
        }
        var group = Enum.Parse<TweakGroup>(tag);
        int marked = 0;
        foreach (var (tweak, box) in _boxes)
            if (TweakSelection.InGroup(tweak, group) && box.IsEnabled) { box.IsChecked = true; marked++; }
        StatusText.Text = $"{TweakSelection.Title(group)}: {marked} marcado(s). Clique em Aplicar alterações para confirmar.";
        StatusText.Foreground = (Brush)FindResource("MutedBrush");
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        var work = new List<TweakWork>();
        var snapshot = _boxes.ToList();
        var currentStates = await Task.Run(() => snapshot.ToDictionary(p => p.Key, p => SafeIsApplied(p.Key) == true));
        foreach (var (tweak, box) in snapshot)
        {
            bool wanted = box.IsChecked == true;
            bool current = currentStates[tweak];
            if (wanted && !current) work.Add(new TweakWork(tweak, true));
            else if (!wanted && current && tweak.HasBackup) work.Add(new TweakWork(tweak, false));
        }

        if (work.Count == 0)
        {
            StatusText.Text = "Nenhuma alteração para aplicar.";
            StatusText.Foreground = (Brush)FindResource("MutedBrush");
            return;
        }

        var risky = work.Where(w => w.Apply && w.Tweak.Risk == TweakRisk.High).ToList();
        if (risky.Count > 0)
        {
            string message = "Você marcou ajustes de risco alto:\n\n" +
                             string.Join("\n\n", risky.Select(w => $"• {w.Tweak.Title}\n  {w.Tweak.Warning}")) +
                             "\n\nDeseja continuar?";
            if (MessageBox.Show(message, "Confirmar ajustes de risco alto", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
        }

        await RunAsync("Aplicando ajustes", work);
    }

    private async void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        var work = _boxes.Keys.Where(t => t.HasBackup).Select(t => new TweakWork(t, false)).ToList();
        if (work.Count == 0)
        {
            StatusText.Text = "Não há nada para restaurar nesta página.";
            StatusText.Foreground = (Brush)FindResource("MutedBrush");
            return;
        }

        string message = $"Restaurar os valores originais de {work.Count} ajuste(s)?";
        if (MessageBox.Show(message, "Restaurar originais", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        await RunAsync("Restaurando valores originais", work);
    }

    private async Task RunAsync(string title, IReadOnlyList<TweakWork> work)
    {
        MainView.Visibility = Visibility.Collapsed;
        ProgressView.Begin(title, new[] { "Ajustes" });

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var progress = new Progress<ProgressEvent>(ProgressView.Report);

        try
        {
            var outcomes = await Task.Run(() => TweakRunner.Run(work, progress, token));
            int ok = outcomes.Count(o => o.Success);
            var failed = outcomes.Where(o => !o.Success).ToList();
            bool restart = outcomes.Any(o => o.Success && o.Applied && o.Tweak.RequiresRestart);

            string summary = failed.Count == 0 ? $"{ok} ajuste(s) concluído(s)" : $"{ok} concluído(s), {failed.Count} com falha";
            var lines = failed.Take(4).Select(f => $"{f.Tweak.Title}: {f.Error}").ToList();
            if (restart) lines.Add("Reinicie o PC (ou saia e entre na conta) para concluir.");
            ProgressView.Finish(summary, failed.Count == 0, string.Join("\n", lines));
        }
        catch (OperationCanceledException)
        {
            ProgressView.Finish("Interrompido. Os ajustes já feitos continuam aplicados e podem ser restaurados.", false);
        }
        catch (Exception ex)
        {
            ProgressView.Finish("Não foi possível concluir.", false, ex.Message);
        }
    }
}
