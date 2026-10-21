using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MBExtreme.Core.Dns;
using MBExtreme.Core.Memory;
using MBExtreme.Core.Progress;

namespace MBExtreme.UI.Pages;

public partial class HomePage : UserControl
{
    private CancellationTokenSource? _cts;

    public HomePage()
    {
        InitializeComponent();
        ProgressView.StopRequested += (_, _) => _cts?.Cancel();
        ProgressView.CloseRequested += (_, _) =>
        {
            MainView.Visibility = Visibility.Visible;
            RefreshHealth();
        };
        Loaded += (_, _) => RefreshHealth();
    }

    private static string LevelText(MemoryLevel level) => level switch
    {
        MemoryLevel.Good => "boa",
        MemoryLevel.NeedsCleaning => "precisa de limpeza",
        _ => "crítica",
    };

    private void RefreshHealth()
    {
        var status = MemoryService.GetStatus();
        (string text, string brush) = status.Level switch
        {
            MemoryLevel.Good => ("Saúde do PC: boa", "OkBrush"),
            MemoryLevel.NeedsCleaning => ("Saúde do PC: atenção", "WarnBrush"),
            _ => ("Saúde do PC: crítica", "BadBrush"),
        };
        HealthText.Text = text;
        HealthText.Foreground = (Brush)FindResource(brush);
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        MainView.Visibility = Visibility.Collapsed;
        ProgressView.Begin("Análise rápida", new[] { "Memória", "DNS" });

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            ProgressView.Report(new ProgressEvent("Memória", 5, "Analisando a memória"));
            var memory = MemoryService.GetStatus();

            var dnsProgress = new Progress<ProgressEvent>(p =>
                ProgressView.Report(new ProgressEvent("DNS", 20 + p.Percent * 0.8, p.Message)));
            var domains = DnsBenchmark.DefaultDomains.Take(3).ToList();
            var results = await DnsBenchmark.RunAsync(
                DnsProviders.Defaults, domains, 1, TimeSpan.FromSeconds(2), dnsProgress, token);

            var best = results.FirstOrDefault(r => r.AverageMs is not null);
            string dns = best is null
                ? "nenhum servidor DNS respondeu."
                : $"o mais rápido foi {best.Provider.Name} ({best.AverageMs:0} ms).";
            string summary = $"Memória {LevelText(memory.Level)} ({memory.FreePercent:0}% livre); {dns}";

            SummaryText.Text = summary;
            SummaryCard.Visibility = Visibility.Visible;
            ProgressView.Finish("Análise concluída", true, summary);
        }
        catch (OperationCanceledException)
        {
            ProgressView.Finish("Análise interrompida.", false);
        }
        catch (Exception ex)
        {
            ProgressView.Finish("Não foi possível concluir a análise.", false, ex.Message);
        }
    }
}
