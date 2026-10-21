using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MBExtreme.Core.Dns;
using MBExtreme.Core.Progress;

namespace MBExtreme.UI.Pages;

public partial class DnsPage : UserControl
{
    private CancellationTokenSource? _cts;
    private IReadOnlyList<DnsResult> _results = Array.Empty<DnsResult>();

    public DnsPage()
    {
        InitializeComponent();
        ProgressView.StopRequested += (_, _) => _cts?.Cancel();
        ProgressView.CloseRequested += (_, _) =>
        {
            MainView.Visibility = Visibility.Visible;
            ShowResults();
        };
        Loaded += (_, _) => RefreshCurrent();
    }

    private void RefreshCurrent() => CurrentText.Text = DnsApplier.DescribeCurrent();

    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        MainView.Visibility = Visibility.Collapsed;
        ProgressView.Begin("Teste de servidores DNS", new[] { "Servidores DNS" });

        _cts = new CancellationTokenSource();
        var progress = new Progress<ProgressEvent>(ProgressView.Report);

        try
        {
            _results = await DnsBenchmark.RunAsync(
                DnsProviders.Defaults, DnsBenchmark.DefaultDomains, 2, TimeSpan.FromSeconds(2), progress, _cts.Token);

            var best = _results.FirstOrDefault(r => r.AverageMs is not null);
            string detail = best is null
                ? "Nenhum servidor respondeu. Verifique a conexão com a internet."
                : $"Mais rápido: {best.Provider.Name} ({best.AverageMs:0} ms).";
            LastTestText.Text = $"Última medição: {DateTime.Now:dd/MM/yyyy - HH:mm}";
            ProgressView.Finish($"{_results.Count} servidores testados", true, detail);
        }
        catch (OperationCanceledException)
        {
            ProgressView.Finish("Teste interrompido.", false);
        }
        catch (Exception ex)
        {
            ProgressView.Finish("Não foi possível concluir o teste.", false, ex.Message);
        }
    }

    private void ShowResults()
    {
        ResultsList.Children.Clear();
        ResultsCard.Visibility = _results.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        bool first = true;
        foreach (var result in _results)
        {
            bool isBest = first && result.AverageMs is not null;
            first = false;

            var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var info = new StackPanel();
            info.Children.Add(new TextBlock
            {
                Text = isBest ? $"{result.Provider.Name}  ⚡ mais rápido" : result.Provider.Name,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
            });
            info.Children.Add(new TextBlock
            {
                Text = $"{result.Provider.Primary} / {result.Provider.Secondary} · {result.Provider.Note}",
                Foreground = (Brush)FindResource("MutedBrush"),
                FontSize = 12,
            });
            grid.Children.Add(info);

            var time = new TextBlock
            {
                Text = result.AverageMs is double ms ? $"{ms:0} ms" : "sem resposta",
                Foreground = (Brush)FindResource(result.AverageMs is null ? "BadBrush" : "OkBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(time, 1);
            grid.Children.Add(time);

            var provider = result.Provider;
            var choose = new Button
            {
                Content = isBest ? "Escolher o mais rápido" : "Escolher",
                Style = (Style)FindResource(isBest ? "PrimaryButton" : "GhostButton"),
                Padding = new Thickness(14, 6, 14, 6),
                IsEnabled = result.AverageMs is not null,
            };
            choose.Click += async (_, _) => await ApplyAsync(provider);
            Grid.SetColumn(choose, 2);
            grid.Children.Add(choose);

            ResultsList.Children.Add(grid);
        }
    }

    private async Task ApplyAsync(DnsProvider provider)
    {
        try
        {
            await DnsApplier.ApplyAsync(provider);
            SetStatus($"DNS alterado para {provider.Name}.", "OkBrush");
        }
        catch (Exception ex)
        {
            SetStatus($"Não foi possível alterar o DNS: {ex.Message}", "BadBrush");
        }

        RefreshCurrent();
    }

    private async void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await DnsApplier.RestoreAsync();
            SetStatus("DNS voltou para automático (padrão do Windows).", "OkBrush");
        }
        catch (Exception ex)
        {
            SetStatus($"Não foi possível restaurar o DNS: {ex.Message}", "BadBrush");
        }

        RefreshCurrent();
    }

    private void SetStatus(string text, string brush)
    {
        StatusText.Text = text;
        StatusText.Foreground = (Brush)FindResource(brush);
    }
}
