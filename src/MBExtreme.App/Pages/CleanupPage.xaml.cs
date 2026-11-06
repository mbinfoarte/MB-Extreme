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
        SelectAllButton.Visibility = Visibility.Collapsed;
        SelectNoneButton.Visibility = Visibility.Collapsed;
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
                    item.Category.Note, item.Files > 0 ? () => ShowPreview(item.Category) : null),
            };
            box.Checked += (_, _) => UpdateSummary();
            box.Unchecked += (_, _) => UpdateSummary();
            _boxes[item] = box;
            RowsHost.Children.Add(Card(box));
        }

        RowsHost.Children.Add(_previousHost);
        _ = ShowPreviousWindowsAsync();
        RowsHost.Children.Add(BuildExclusions());

        CleanButton.Visibility = Visibility.Visible;
        SelectAllButton.Visibility = Visibility.Visible;
        SelectNoneButton.Visibility = Visibility.Visible;
        UpdateSummary();
    }

    private readonly StackPanel _previousHost = new();

    private Brush Muted => (Brush)FindResource("MutedBrush");

    /// <summary>Instalação anterior do Windows: só mede. Apagar é com a Limpeza de Disco do próprio Windows (o jeito oficial e seguro).</summary>
    private async Task ShowPreviousWindowsAsync()
    {
        _previousHost.Children.Clear();
        string drive = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd('\\');
        var found = await Task.Run(() => MBExtreme.Core.Storage.DiskMaintenance.FindPrevious(drive));
        if (found.Count == 0) return;
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = $"Instalações anteriores do Windows: {Format(found.Sum(f => f.Bytes))}", FontSize = 14, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock
        {
            Text = string.Join(", ", found.Select(f => f.Path)) + ". Servem para voltar à versão anterior do Windows nos primeiros dias depois de uma atualização grande. " +
                   "Para apagar com segurança, use a Limpeza de Disco do Windows > \"Limpar arquivos do sistema\" > \"Instalações anteriores do Windows\".",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Muted,
        });
        var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var open = new Button { Content = "Abrir a Limpeza de Disco do Windows", Style = (Style)FindResource("GhostButton"), Margin = new Thickness(0, 0, 10, 0) };
        open.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cleanmgr.exe", "/d " + drive) { UseShellExecute = true }); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { SummaryText.Text = "Não foi possível abrir a Limpeza de Disco: " + ex.Message; }
        };
        var components = new Button { Content = "Limpar componentes antigos das atualizações", Style = (Style)FindResource("GhostButton") };
        components.Click += (_, _) => MainWindow.Current?.Go("repair");
        components.ToolTip = "Abre o Reparo do Windows: use \"Limpar componentes antigos do Windows\" (DISM).";
        row.Children.Add(open);
        row.Children.Add(components);
        panel.Children.Add(row);
        _previousHost.Children.Add(Card(panel));
    }

    /// <summary>Pastas que a limpeza nunca toca, escolhidas por você (valem também para a limpeza semanal).</summary>
    private UIElement BuildExclusions()
    {
        var settings = AppServices.Settings;
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Pastas que nunca são limpas", FontSize = 14, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock
        {
            Text = "Arquivos dentro destas pastas ficam de fora em todas as categorias, inclusive na limpeza semanal.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Muted,
        });
        if (settings.CleanupExcluded.Count == 0)
            panel.Children.Add(new TextBlock { Text = "Nenhuma.", FontSize = 12, Foreground = Muted, Margin = new Thickness(0, 4, 0, 0) });
        foreach (string folder in settings.CleanupExcluded.ToList())
        {
            var line = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var remove = new Button { Content = "Remover", Style = (Style)FindResource("GhostButton"), Padding = new Thickness(10, 2, 10, 2) };
            System.Windows.Automation.AutomationProperties.SetName(remove, "Remover " + folder + " das pastas protegidas");
            remove.Click += (_, _) => { settings.CleanupExcluded.Remove(folder); SaveExclusions(); };
            DockPanel.SetDock(remove, Dock.Right);
            line.Children.Add(remove);
            line.Children.Add(new TextBlock { Text = folder, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            panel.Children.Add(line);
        }
        var add = new Button { Content = "Adicionar pasta...", Style = (Style)FindResource("GhostButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        add.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Pasta que a limpeza nunca deve tocar" };
            if (dialog.ShowDialog() == true) AddExclusion(dialog.FolderName);
        };
        panel.Children.Add(add);
        return Card(panel);
    }

    private void AddExclusion(string folder)
    {
        var settings = AppServices.Settings;
        if (!settings.CleanupExcluded.Contains(folder, StringComparer.OrdinalIgnoreCase)) settings.CleanupExcluded.Add(folder);
        SaveExclusions();
    }

    private void SaveExclusions()
    {
        AppServices.Settings.Save();
        CleanupService.Excluded = AppServices.Settings.CleanupExcluded.ToList();
        _ = ScanAsync();   // os tamanhos mudam: procura de novo
    }

    /// <summary>Mostra os maiores arquivos que a categoria apagaria (até 300), antes de limpar.</summary>
    private async void ShowPreview(CleanupCategory category)
    {
        var files = await Task.Run(() => CleanupService.Preview(category, DateTime.UtcNow));
        var list = new StackPanel();
        foreach (var (path, bytes, modified) in files)
        {
            var line = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
            var info = new TextBlock { Text = $"{Format(bytes)} · {modified:dd/MM/yyyy}", Foreground = Muted, FontSize = 11.5, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(info, Dock.Right);
            var keep = new Button { Content = "Nunca limpar esta pasta", Style = (Style)FindResource("GhostButton"), Padding = new Thickness(8, 1, 8, 1), FontSize = 11, Margin = new Thickness(12, 0, 0, 0) };
            string folder = Path.GetDirectoryName(path) ?? path;
            keep.Click += (s, _) => { AddExclusion(folder); ((Button)s).Content = "Protegida"; ((Button)s).IsEnabled = false; };
            DockPanel.SetDock(keep, Dock.Right);
            line.Children.Add(keep);
            line.Children.Add(info);
            line.Children.Add(new TextBlock { Text = path, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = path, VerticalAlignment = VerticalAlignment.Center });
            list.Children.Add(line);
        }
        if (files.Count == 0) list.Children.Add(new TextBlock { Text = "Nada para limpar nesta categoria agora.", Foreground = Muted });
        var root = new DockPanel { Margin = new Thickness(18) };
        var head = new TextBlock
        {
            Text = $"{category.Title}: {(files.Count >= 300 ? "os 300 maiores" : files.Count + "")} arquivo(s) que seriam apagados. Arquivos em uso ficam onde estão.",
            TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10),
        };
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);
        root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        new Window
        {
            Title = "Arquivos que seriam limpos", Width = 980, Height = 600, Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = root,
            Background = (Brush)FindResource("BgBrush"), Foreground = (Brush)FindResource("TextBrush"),
        }.ShowDialog();
    }

    private Border Card(UIElement child) => new()
    {
        Style = (Style)FindResource("Card"),
        Padding = new Thickness(18, 14, 18, 14),
        Margin = new Thickness(0, 0, 0, 10),
        Child = child,
    };

    private Grid RowContent(string title, string description, string size, string note, Action? preview = null)
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

        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        right.Children.Add(new TextBlock { Text = size, FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right });
        if (preview is not null)
        {
            var see = new Button { Content = "Ver arquivos", Style = (Style)FindResource("GhostButton"), Padding = new Thickness(8, 1, 8, 1), FontSize = 11.5, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
            System.Windows.Automation.AutomationProperties.SetName(see, "Ver os arquivos de " + title);
            see.Click += (_, e) => { e.Handled = true; preview(); };
            right.Children.Add(see);
        }
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

    private void SelectAllButton_Click(object sender, RoutedEventArgs e) => SetAll(true);

    private void SelectNoneButton_Click(object sender, RoutedEventArgs e) => SetAll(false);

    private void SetAll(bool value)
    {
        foreach (var box in _boxes.Values.Where(b => b.IsEnabled)) box.IsChecked = value;
        if (_ramBox is not null) _ramBox.IsChecked = value;
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
                    MBExtreme.Core.Operations.OperationJournal.Shared.Record(
                        new MBExtreme.Core.Operations.OperationInfo("cleanup." + selected[i].Category.Title, MBExtreme.Core.Operations.OperationKind.Cleanup, "Limpeza: " + selected[i].Category.Title),
                        MBExtreme.Core.Operations.OperationResult.Ok($"{r.FilesDeleted} arquivo(s), {r.FreedBytes / (1024 * 1024)} MB liberados; {r.FilesSkipped} em uso ficaram."), DateTimeOffset.Now);
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
