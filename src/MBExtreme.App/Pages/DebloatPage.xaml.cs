using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MBExtreme.Core.Apps;
using MBExtreme.Core.Progress;
using MBExtreme.Core.Windows;

namespace MBExtreme.UI.Pages;

public partial class DebloatPage : UserControl
{
    private readonly Dictionary<BloatApp, CheckBox> _boxes = new();
    private CancellationTokenSource? _cts;

    public DebloatPage()
    {
        InitializeComponent();
        ProgressView.StopRequested += (_, _) => _cts?.Cancel();
        ProgressView.CloseRequested += (_, _) =>
        {
            MainView.Visibility = Visibility.Visible;
            Reload();
        };
        Loaded += (_, _) => Reload();
    }

    private void Reload()
    {
        RowsHost.Children.Clear();
        _boxes.Clear();

        IReadOnlyList<string> installed;
        try
        {
            installed = AppRemovalService.InstalledPackageNames();
        }
        catch (Exception ex)
        {
            SetStatus($"Não foi possível listar os aplicativos: {ex.Message}", "BadBrush");
            return;
        }

        SetStatus("", "MutedBrush");
        foreach (var level in new[] { AppRemovalLevel.Safe, AppRemovalLevel.Caution, AppRemovalLevel.Dangerous })
        {
            RowsHost.Children.Add(new TextBlock
            {
                Text = LevelTitle(level),
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 10, 0, 8),
                Foreground = (Brush)FindResource(LevelBrush(level)),
            });

            foreach (var app in BloatCatalog.All.Where(a => a.Level == level))
            {
                bool present = AppRemovalService.IsInstalled(app, installed);
                var content = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
                content.Children.Add(new TextBlock
                {
                    Text = present ? app.Title : app.Title + "  (não instalado)",
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                });
                content.Children.Add(new TextBlock
                {
                    Text = app.Note,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (Brush)FindResource(level == AppRemovalLevel.Dangerous ? "WarnBrush" : "MutedBrush"),
                });

                var box = new CheckBox
                {
                    Content = content,
                    IsEnabled = present,
                    VerticalContentAlignment = VerticalAlignment.Top,
                    Foreground = (Brush)FindResource("TextBrush"),
                };
                RowsHost.Children.Add(new Border
                {
                    Style = (Style)FindResource("Card"),
                    Margin = new Thickness(0, 0, 0, 8),
                    Padding = new Thickness(16, 12, 16, 12),
                    Child = box,
                });
                _boxes[app] = box;
            }
        }
    }

    private static string LevelTitle(AppRemovalLevel level) => level switch
    {
        AppRemovalLevel.Safe => "Seguros de remover",
        AppRemovalLevel.Caution => "Remova só se tiver certeza",
        _ => "Perigosos: quebram recursos do Windows e de jogos",
    };

    private static string LevelBrush(AppRemovalLevel level) => level switch
    {
        AppRemovalLevel.Safe => "OkBrush",
        AppRemovalLevel.Caution => "WarnBrush",
        _ => "BadBrush",
    };

    private void SetStatus(string text, string brush)
    {
        StatusText.Text = text;
        StatusText.Foreground = (Brush)FindResource(brush);
    }

    private void SelectSafeButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var (app, box) in _boxes)
            box.IsChecked = app.Level == AppRemovalLevel.Safe && box.IsEnabled;
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var box in _boxes.Values) box.IsChecked = false;
    }

    private async void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = _boxes.Where(p => p.Value.IsChecked == true && p.Value.IsEnabled).Select(p => p.Key).ToList();
        if (selected.Count == 0)
        {
            SetStatus("Selecione ao menos um aplicativo.", "MutedBrush");
            return;
        }

        string list = string.Join("\n", selected.Select(a => "• " + a.Title));
        var dangerous = selected.Where(a => a.Level == AppRemovalLevel.Dangerous).ToList();
        string warning = dangerous.Count > 0
            ? "\n\nATENÇÃO: " + string.Join(", ", dangerous.Select(a => a.Title)) +
              " quebra recursos do Windows e de jogos e é difícil de reverter."
            : "";
        var answer = MessageBox.Show(
            $"Remover {selected.Count} aplicativo(s)?\n\n{list}{warning}",
            "Confirmar remoção",
            MessageBoxButton.YesNo,
            dangerous.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        await RunAsync(selected);
    }

    private async Task RunAsync(IReadOnlyList<BloatApp> apps)
    {
        MainView.Visibility = Visibility.Collapsed;
        ProgressView.Begin("Removendo aplicativos", new[] { "Ponto de restauração", "Aplicativos" });

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var progress = new Progress<ProgressEvent>(ProgressView.Report);

        try
        {
            var (removed, failures, restoreError) = await Task.Run(() => Remove(apps, progress, token));

            string summary = failures.Count == 0 ? $"{removed} aplicativo(s) removido(s)" : $"{removed} removido(s), {failures.Count} com falha";
            var lines = failures.Take(4).ToList();
            if (restoreError is not null) lines.Insert(0, "Ponto de restauração não criado: " + restoreError);
            ProgressView.Finish(summary, failures.Count == 0, string.Join("\n", lines));
        }
        catch (OperationCanceledException)
        {
            ProgressView.Finish("Interrompido. Os aplicativos já removidos não voltam sozinhos.", false);
        }
        catch (Exception ex)
        {
            ProgressView.Finish("Não foi possível concluir.", false, ex.Message);
        }
    }

    private static (int Removed, List<string> Failures, string? RestoreError) Remove(
        IReadOnlyList<BloatApp> apps, IProgress<ProgressEvent> progress, CancellationToken token)
    {
        progress.Report(new ProgressEvent("Ponto de restauração", 5, "Criando ponto de restauração do Windows"));
        var (_, restoreError) = RestorePointService.TryCreate("MB Extreme - remover aplicativos");

        var failures = new List<string>();
        int removed = 0;
        for (int i = 0; i < apps.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var app = apps[i];
            progress.Report(new ProgressEvent("Aplicativos", 15 + 85.0 * i / apps.Count, $"Removendo {app.Title}"));
            try
            {
                AppRemovalService.Remove(app);
                removed++;
            }
            catch (InvalidOperationException ex)
            {
                failures.Add($"{app.Title}: {ex.Message}");
            }
        }

        progress.Report(new ProgressEvent("Aplicativos", 100, "Concluído"));
        return (removed, failures, restoreError);
    }
}
