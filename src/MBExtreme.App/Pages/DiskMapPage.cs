using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MBExtreme.Core.Storage;

namespace MBExtreme.UI.Pages;

/// <summary>Mapa do disco: blocos coloridos proporcionais ao tamanho; clique numa pasta para entrar, Voltar para subir. Só lê.</summary>
public sealed class DiskMapPage : UserControl
{
    private static readonly Dictionary<FileKind, Color> Colors = new()
    {
        [FileKind.Video] = Color.FromRgb(0xE0, 0x4F, 0x5F), [FileKind.Image] = Color.FromRgb(0x3F, 0xA7, 0xD6), [FileKind.Music] = Color.FromRgb(0xA0, 0x6C, 0xD5),
        [FileKind.Document] = Color.FromRgb(0xF2, 0xA3, 0x3A), [FileKind.Archive] = Color.FromRgb(0xE8, 0x7B, 0x2C), [FileKind.Program] = Color.FromRgb(0x3D, 0xB8, 0x7A),
        [FileKind.System] = Color.FromRgb(0x5B, 0x7F, 0xD8), [FileKind.Other] = Color.FromRgb(0x8E, 0x8E, 0x96),
    };

    private readonly WrapPanel _drives = new();
    private readonly Canvas _canvas = new() { ClipToBounds = true, Background = Brushes.Transparent };
    private readonly StackPanel _side = new();
    private readonly TextBlock _path = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _status = new() { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly WrapPanel _legend = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly Stack<MapNode> _trail = new();
    private MapNode? _current;
    private CancellationTokenSource? _cts;

    public DiskMapPage()
    {
        var muted = (Brush)Application.Current.FindResource("MutedBrush");
        _status.Foreground = muted;
        var root = new Grid { Margin = new Thickness(40, 28, 40, 24) };
        for (int i = 0; i < 5; i++) root.RowDefinitions.Add(new RowDefinition { Height = i == 3 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        var head = new StackPanel();
        head.Children.Add(new TextBlock { Text = "Mapa do disco", FontSize = 28, FontWeight = FontWeights.SemiBold });
        head.Children.Add(new TextBlock { Text = "Descubra quais pastas e arquivos estão enchendo o disco. Clique num disco, depois clique num bloco para entrar e use Voltar para subir. Esta tela só lê: nada é apagado ou mudado.", Foreground = muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10) });
        root.Children.Add(head);
        Grid.SetRow(_drives, 1);
        root.Children.Add(_drives);

        var bar = new Grid { Margin = new Thickness(0, 8, 0, 8) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition());
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var back = Button("‹ Voltar", () => { if (_trail.Count > 0) Show(_trail.Pop()); });
        bar.Children.Add(back);
        _path.Margin = new Thickness(10, 0, 10, 0);
        Grid.SetColumn(_path, 1);
        bar.Children.Add(_path);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(Button("Escolher pasta...", ChooseFolder));
        buttons.Children.Add(Button("Abrir no Explorador", () => { if (_current is not null) OpenExplorer(_current.Path); }));
        Grid.SetColumn(buttons, 2);
        bar.Children.Add(buttons);
        Grid.SetRow(bar, 2);
        root.Children.Add(bar);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition());
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        var mapBorder = new Border { CornerRadius = new CornerRadius(10), BorderBrush = (Brush)Application.Current.FindResource("BorderBrush"), BorderThickness = new Thickness(1), Child = _canvas, MinHeight = 320 };
        body.Children.Add(mapBorder);
        var sideScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _side, Margin = new Thickness(12, 0, 0, 0) };
        Grid.SetColumn(sideScroll, 1);
        body.Children.Add(sideScroll);
        Grid.SetRow(body, 3);
        root.Children.Add(body);

        var foot = new StackPanel();
        foot.Children.Add(_legend);
        foot.Children.Add(_status);
        Grid.SetRow(foot, 4);
        root.Children.Add(foot);

        Content = root;
        _canvas.SizeChanged += (_, _) => Draw();
        Loaded += (_, _) => { if (_drives.Children.Count == 0) FillDrives(); };
        foreach (var (kind, color) in Colors)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0) };
            item.Children.Add(new Rectangle { Width = 10, Height = 10, RadiusX = 2, RadiusY = 2, Fill = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center });
            item.Children.Add(new TextBlock { Text = " " + DiskMap.KindName(kind), FontSize = 11.5, Foreground = muted });
            _legend.Children.Add(item);
        }
    }

    private static Button Button(string text, Action click)
    {
        var b = new Button { Content = text, Style = (Style)Application.Current.FindResource("GhostButton"), Padding = new Thickness(12, 5, 12, 5), FontSize = 12.5, Margin = new Thickness(0, 0, 6, 0) };
        b.Click += (_, _) => click();
        return b;
    }

    private void FillDrives()
    {
        _drives.Children.Clear();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady || d.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                double used = 1 - (double)d.AvailableFreeSpace / Math.Max(1, d.TotalSize);
                var content = new StackPanel { Width = 170 };
                content.Children.Add(new TextBlock { Text = $"{d.Name.TrimEnd('\\')} {d.VolumeLabel}", FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                content.Children.Add(new ProgressBar { Value = used * 100, Height = 4, Margin = new Thickness(0, 4, 0, 4) });
                content.Children.Add(new TextBlock { Text = $"{d.AvailableFreeSpace / 1e9:0.#} GB livres de {d.TotalSize / 1e9:0.#} GB", FontSize = 11, Foreground = (Brush)Application.Current.FindResource("MutedBrush") });
                string root = d.RootDirectory.FullName;
                var b = new Button { Style = (Style)Application.Current.FindResource("ToolTileButton"), Content = content, Margin = new Thickness(0, 0, 8, 0) };
                b.Click += async (_, _) => await ScanAsync(root);
                _drives.Children.Add(b);
            }
            catch (IOException) { }
        }
        _status.Text = "Clique num disco para ler. O disco do Windows pode levar de 1 a 5 minutos.";
    }

    private void ChooseFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Pasta para mapear" };
        if (dialog.ShowDialog() == true) _ = ScanAsync(dialog.FolderName);
    }

    private async Task ScanAsync(string root)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _trail.Clear();
        _canvas.Children.Clear();
        _side.Children.Clear();
        _path.Text = root;
        var clock = Stopwatch.StartNew();
        _status.Text = "Lendo " + root + "...";
        try
        {
            var node = await Task.Run(() => DiskMap.Scan(root, new Progress<string>(p => _status.Text = "Lendo " + p), token));
            Show(node);
            _status.Text = $"{node.Files:N0} arquivos · {Size(node.Size)} · lido em {clock.Elapsed.TotalSeconds:0} s" + (node.Denied ? " · algumas pastas do sistema não deixam ler (normal)" : "");
        }
        catch (OperationCanceledException) { }
    }

    private void Show(MapNode node)
    {
        _current = node;
        _path.Text = $"{node.Path}   ·   {Size(node.Size)}";
        Draw();
        _side.Children.Clear();
        _side.Children.Add(new TextBlock { Text = "Maiores itens", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        foreach (var child in node.Children.Take(40))
        {
            double share = node.Size > 0 ? 100.0 * child.Size / node.Size : 0;
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 6), Cursor = child.IsFile ? null : Cursors.Hand, Background = Brushes.Transparent };
            var top = new Grid();
            top.Children.Add(new TextBlock { Text = (child.IsFile ? "" : "▸ ") + child.Name, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 70, 0) });
            top.Children.Add(new TextBlock { Text = Size(child.Size), FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right });
            row.Children.Add(top);
            row.Children.Add(new ProgressBar { Value = share, Height = 3, Margin = new Thickness(0, 2, 0, 0), Foreground = new SolidColorBrush(Colors[child.MainKind]) });
            row.Children.Add(new TextBlock { Text = $"{share:0.#}% · {child.Files:N0} arquivo(s) · mais comum: {DiskMap.KindName(child.MainKind)}", FontSize = 10.5, Foreground = (Brush)Application.Current.FindResource("MutedBrush") });
            if (!child.IsFile) { var target = child; row.MouseLeftButtonUp += (_, _) => Enter(target); }
            _side.Children.Add(row);
        }
    }

    private void Enter(MapNode child)
    {
        if (child.IsFile || _current is null) return;
        _trail.Push(_current);
        Show(child);
    }

    private void Draw()
    {
        _canvas.Children.Clear();
        if (_current is null || _canvas.ActualWidth < 10) return;
        var items = _current.Children.Where(c => c.Size > 0).ToList();
        var rects = DiskMap.Layout(items.Select(c => c.Size).ToList(), new MapRect(0, 0, _canvas.ActualWidth, _canvas.ActualHeight));
        for (int i = 0; i < items.Count; i++)
        {
            var r = rects[i];
            if (r.W < 2 || r.H < 2) continue;
            var node = items[i];
            var color = Colors[node.MainKind];
            var block = new Border
            {
                Width = r.W - 2, Height = r.H - 2, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.FromArgb(0xD8, color.R, color.G, color.B)),
                ToolTip = $"{node.Name}\n{Size(node.Size)} · {node.Files:N0} arquivo(s)", Cursor = node.IsFile ? null : Cursors.Hand,
            };
            if (r.W > 60 && r.H > 28)
            {
                var label = new StackPanel { Margin = new Thickness(6, 4, 4, 0) };
                label.Children.Add(new TextBlock { Text = node.Name, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis });
                label.Children.Add(new TextBlock { Text = Size(node.Size), FontSize = 10.5, Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)) });
                block.Child = label;
            }
            var target = node;
            block.MouseLeftButtonUp += (_, _) => Enter(target);
            Canvas.SetLeft(block, r.X + 1);
            Canvas.SetTop(block, r.Y + 1);
            _canvas.Children.Add(block);
        }
    }

    private static void OpenExplorer(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static string Size(long bytes) => bytes switch
    {
        >= 1L << 40 => $"{bytes / (double)(1L << 40):0.0} TB",
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        _ => $"{bytes / 1024.0:0} KB",
    };
}
