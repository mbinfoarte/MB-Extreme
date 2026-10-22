using System.IO;
using System.Windows;
using System.Windows.Controls;
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
        var candidates = AppServices.Tweaks.Where(t => _categories.Contains(t.Category)).ToList();
        // ajustes de uma marca de placa de vídeo só aparecem em PCs com essa marca
        var states = await Task.Run(() => candidates.Where(t => t.IsAvailable).Select(t => (Tweak: t, Applied: SafeIsApplied(t))).ToList());
        if (version != _buildVersion) return;

        RowsHost.Children.Clear();
        _boxes.Clear();

        foreach (var (tweak, applied) in states)
        {
            var content = BuildContent(tweak);
            content.Margin = new Thickness(8, 0, 0, 0);

            var box = new CheckBox
            {
                IsChecked = applied == true,
                Content = content,
                VerticalContentAlignment = VerticalAlignment.Top,
                Foreground = (Brush)FindResource("TextBrush"),
            };

            RowsHost.Children.Add(new Border
            {
                Style = (Style)FindResource("Card"),
                Margin = new Thickness(0, 0, 0, 10),
                Child = box,
            });
            _boxes[tweak] = box;
        }
    }

    private StackPanel BuildContent(Tweak tweak)
    {
        var panel = new StackPanel();

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock { Text = tweak.Title, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(Badge(RiskText(tweak.Risk), RiskBrush(tweak.Risk)));
        if (tweak.RequiresRestart) header.Children.Add(Badge("reiniciar", "MutedBrush"));
        panel.Children.Add(header);

        panel.Children.Add(Line(tweak.Description, "TextBrush", 13));
        if (tweak.Effect.Length > 0) panel.Children.Add(Line("Efeito: " + tweak.Effect, "MutedBrush", 12));
        if (tweak.Warning.Length > 0) panel.Children.Add(Line("Atenção: " + tweak.Warning, "WarnBrush", 12));
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

    private void RecommendedButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var (tweak, box) in _boxes)
            if (tweak.Recommended) box.IsChecked = true;
        StatusText.Text = "Recomendados marcados. Clique em Aplicar alterações para confirmar.";
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
