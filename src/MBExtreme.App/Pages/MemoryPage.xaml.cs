using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MBExtreme.Core.Memory;
using MBExtreme.Core.Progress;
using MBExtreme.Core.Settings;

namespace MBExtreme.UI.Pages;

public partial class MemoryPage : UserControl
{
    private const double BytesPerGb = 1024.0 * 1024 * 1024;

    private readonly AppSettings _settings = AppServices.Settings;
    private CancellationTokenSource? _cts;

    public MemoryPage()
    {
        InitializeComponent();
        ProgressView.StopRequested += (_, _) => _cts?.Cancel();
        ProgressView.CloseRequested += (_, _) =>
        {
            MainView.Visibility = Visibility.Visible;
            RefreshStatus();
        };
        Loaded += (_, _) =>
        {
            RefreshStatus();
            RefreshAllowlist();
        };
    }

    private void RefreshStatus()
    {
        var status = MemoryService.GetStatus();
        UsedText.Text = $"{status.UsedBytes / BytesPerGb:0.0} GB";
        FreeText.Text = $"{status.AvailableBytes / BytesPerGb:0.0} GB";
        TotalText.Text = $"{status.TotalBytes / BytesPerGb:0.#} GB";

        (string text, string brush) = status.Level switch
        {
            MemoryLevel.Good => ("Memória em boas condições", "OkBrush"),
            MemoryLevel.NeedsCleaning => ("Precisa de limpeza", "WarnBrush"),
            _ => ("Memória crítica", "BadBrush"),
        };
        LevelText.Text = text;
        LevelDot.Fill = (Brush)FindResource(brush);
    }

    private void RefreshAllowlist()
    {
        AllowList.Children.Clear();
        foreach (var name in _settings.MemoryAllowlist)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            row.Children.Add(new TextBlock { Text = name, Width = 240, VerticalAlignment = VerticalAlignment.Center });

            var remove = new Button { Content = "Remover", Style = (Style)FindResource("GhostButton"), Padding = new Thickness(12, 4, 12, 4) };
            string captured = name;
            remove.Click += (_, _) =>
            {
                _settings.MemoryAllowlist.Remove(captured);
                _settings.Save();
                RefreshAllowlist();
            };
            row.Children.Add(remove);
            AllowList.Children.Add(row);
        }
    }

    private void AddToAllowlist()
    {
        string name = AllowBox.Text.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (name.Length == 0) return;

        if (!_settings.MemoryAllowlist.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            _settings.MemoryAllowlist.Add(name);
            _settings.Save();
        }

        AllowBox.Clear();
        RefreshAllowlist();
    }

    private void AddAllowButton_Click(object sender, RoutedEventArgs e) => AddToAllowlist();

    private void AllowBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddToAllowlist();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshStatus();

    private async void CleanButton_Click(object sender, RoutedEventArgs e)
    {
        MainView.Visibility = Visibility.Collapsed;
        ProgressView.Begin("Limpeza de memória", new[] { "Processos", "Cache" });

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var progress = new Progress<ProgressEvent>(ProgressView.Report);
        var allowlist = _settings.MemoryAllowlist.ToArray();

        try
        {
            var result = await Task.Run(() => MemoryService.Clean(allowlist, progress, token));
            string summary = $"{result.FreedBytes / (1024.0 * 1024):0} MB liberados";
            string detail = result.StandbyPurged
                ? $"{result.ProcessesTrimmed} processos reduzidos e lista de espera esvaziada."
                : $"{result.ProcessesTrimmed} processos reduzidos. Cache não esvaziado: {result.Error}";
            ProgressView.Finish(summary, true, detail);
        }
        catch (OperationCanceledException)
        {
            ProgressView.Finish("Limpeza interrompida.", false);
        }
        catch (Exception ex)
        {
            ProgressView.Finish("Não foi possível concluir a limpeza.", false, ex.Message);
        }
    }
}
