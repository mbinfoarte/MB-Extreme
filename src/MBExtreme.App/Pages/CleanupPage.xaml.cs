using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MBExtreme.Core.Cleanup;
using MBExtreme.Core.Memory;
using MBExtreme.Core.Progress;
using MBExtreme.Core.Settings;

namespace MBExtreme.UI.Pages;

/// <summary>Escaneia o que pode ser limpo, mostra o resultado e limpa o que a pessoa marcar.</summary>
public partial class CleanupPage : UserControl
{
    private readonly Dictionary<CleanupItem, CheckBox> _boxes = new();
    private CheckBox? _ramBox;
    private IReadOnlyList<CleanupItem>? _items;
    private CancellationTokenSource? _cts;

    public CleanupPage()
    {
        InitializeComponent();
        ProgressView.StopRequested += (_, _) => _cts?.Cancel();
        ProgressView.CloseRequested += (_, _) =>
        {
            MainView.Visibility = Visibility.Visible;
            BuildResults();
        };
    }

    private static string Format(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0} KB",
        _ => $"{bytes} B",
    };

    private async void ScanButton_Click(object sender, RoutedEventArgs e) => await ScanAsync();

    /// <summary>Chamado pelo painel para começar a varredura assim que a página abre.</summary>
    public async void StartScan() => await ScanAsync();

    private async Task ScanAsync()
    {
        MainView.Visibility = Visibility.Collapsed;
        ProgressView.Begin("Analisando o computador", new[] { "Procurar" });

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        IProgress<ProgressEvent> progress = new Progress<ProgressEvent>(ProgressView.Report);
        var categories = CleanupCatalog.Create();

        try
        {
            _items = await Task.Run(() => CleanupService.ScanAll(categories, progress, token));
            long total = _items.Sum(i => i.Bytes);
            int withFiles = _items.Count(i => i.Files > 0);
            ProgressView.Finish($"{Format(total)} podem ser liberados",
                true, $"{withFiles} categorias com arquivos. Revise a lista e clique em Limpar selecionados.");
        }
        catch (OperationCanceledException)
        {
            _items = null;
            ProgressView.Finish("Análise interrompida.", false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _items = null;
            ProgressView.Finish("Não foi possível concluir a análise.", false, ex.Message);
        }
    }

    private void BuildResults()
    {
        RowsHost.Children.Clear();
        _boxes.Clear();
        _ramBox = null;
        CleanButton.Visibility = Visibility.Collapsed;
        if (_items is null)
        {
            SummaryText.Text = "";
            return;
        }

        var memory = MemoryService.GetStatus();
        _ramBox = new CheckBox
        {
            IsChecked = true,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = RowContent("Memória RAM",
                "Reduz o uso de memória dos programas abertos e esvazia o cache da RAM.",
                $"{100 - memory.FreePercent:0}% em uso",
                "Os programas abertos continuam funcionando."),
        };
        _ramBox.Checked += (_, _) => UpdateSummary();
        _ramBox.Unchecked += (_, _) => UpdateSummary();
        RowsHost.Children.Add(Card(_ramBox));

        foreach (var item in _items)
        {
            var box = new CheckBox
            {
                IsChecked = item.Files > 0 && item.Category.DefaultSelected,
                IsEnabled = item.Files > 0,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = RowContent(item.Category.Title, item.Category.Description,
                    item.Files > 0 ? $"{Format(item.Bytes)} · {item.Files} arquivos" : "nada encontrado",
                    item.Category.Note),
            };
            box.Checked += (_, _) => UpdateSummary();
            box.Unchecked += (_, _) => UpdateSummary();
            _boxes[item] = box;
            RowsHost.Children.Add(Card(box));
        }

        CleanButton.Visibility = Visibility.Visible;
        UpdateSummary();
    }

    private Border Card(UIElement child) => new()
    {
        Style = (Style)FindResource("Card"),
        Padding = new Thickness(18, 14, 18, 14),
        Margin = new Thickness(0, 0, 0, 10),
        Child = child,
    };

    private Grid RowContent(string title, string description, string size, string note)
    {
        var grid = new Grid { Margin = new Thickness(8, 0, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel();
        left.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold });
        left.Children.Add(new TextBlock
        {
            Text = description, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("MutedBrush"),
        });
        if (note.Length > 0)
            left.Children.Add(new TextBlock
            {
                Text = note, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0),
                Foreground = (Brush)FindResource("MutedBrush"),
            });
        grid.Children.Add(left);

        var right = new TextBlock
        {
            Text = size, FontSize = 13, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0),
        };
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    private void UpdateSummary()
    {
        long bytes = _boxes.Where(p => p.Value.IsChecked == true).Sum(p => p.Key.Bytes);
        int count = _boxes.Count(p => p.Value.IsChecked == true);
        string ram = _ramBox?.IsChecked == true ? " e a memória RAM" : "";
        SummaryText.Text = $"Selecionado: {Format(bytes)} em {count} categoria(s){ram}.";
    }

    private async void CleanButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = _boxes.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToList();
        bool ram = _ramBox?.IsChecked == true;
        if (selected.Count == 0 && !ram)
        {
            SummaryText.Text = "Marque ao menos um item.";
            return;
        }

        MainView.Visibility = Visibility.Collapsed;
        ProgressView.Begin("Limpando o computador", new[] { "Arquivos", "Memória" });

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        IProgress<ProgressEvent> progress = new Progress<ProgressEvent>(ProgressView.Report);
        var allowlist = AppSettings.Load().MemoryAllowlist.ToArray();

        try
        {
            var result = await Task.Run(() =>
            {
                long freed = 0;
                int deleted = 0, skipped = 0;
                for (int i = 0; i < selected.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    progress.Report(new ProgressEvent("Arquivos", 85.0 * i / Math.Max(1, selected.Count),
                        "Limpando " + selected[i].Category.Title));
                    var r = CleanupService.Clean(selected[i].Category, DateTime.UtcNow, token);
                    freed += r.FreedBytes;
                    deleted += r.FilesDeleted;
                    skipped += r.FilesSkipped;
                }

                CleanResult? memory = null;
                if (ram)
                {
                    progress.Report(new ProgressEvent("Memória", 88, "Liberando memória RAM"));
                    memory = MemoryService.Clean(allowlist, null, token);
                }

                progress.Report(new ProgressEvent("Memória", 100, "Concluído"));
                return (freed, deleted, skipped, memory);
            });

            long total = result.freed + (result.memory?.FreedBytes ?? 0);
            var lines = new List<string> { $"{result.deleted} arquivos apagados." };
            if (result.skipped > 0) lines.Add($"{result.skipped} arquivos estavam em uso e foram mantidos.");
            if (result.memory is { } m)
                lines.Add(m.StandbyPurged
                    ? $"Memória: {Format(m.FreedBytes)} liberados e cache esvaziado."
                    : $"Memória: {Format(m.FreedBytes)} liberados. {m.Error}");
            ProgressView.Finish($"{Format(total)} liberados", true, string.Join("\n", lines));
            _items = null;
        }
        catch (OperationCanceledException)
        {
            ProgressView.Finish("Limpeza interrompida. O que já foi apagado não volta.", false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ProgressView.Finish("Não foi possível concluir a limpeza.", false, ex.Message);
        }
    }
}
