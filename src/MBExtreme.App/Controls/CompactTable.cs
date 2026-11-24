using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace MBExtreme.UI.Controls;

/// <summary>Coluna da <see cref="CompactTable"/>: título, largura (em pixels ou 0 = ocupa o resto) e alinhamento.</summary>
internal sealed record TableColumn(string Title, double Width = 0, HorizontalAlignment Align = HorizontalAlignment.Left);

/// <summary>Uma linha: caixinha (opcional), ícone (opcional) e uma célula por coluna (texto ou peça pronta).</summary>
internal sealed record TableRow(CheckBox? Box, FrameworkElement? Icon, IReadOnlyList<object> Cells, string? ToolTip = null);

/// <summary>
/// Lista em colunas com linhas finas (30 px), no padrão das listas de Programas: cabeçalho que ordena ao clicar, caixinha pequena,
/// ícone, linha amarela quando marcada e linhas alternadas bem discretas. Usada em Processos, Serviços, Inicialização e afins.
/// </summary>
internal sealed class CompactTable : Border
{
    private static readonly Brush OnFill = Frozen(Color.FromArgb(0x22, 0xFF, 0xC4, 0x00));
    private static readonly Brush OnBorder = Frozen(Color.FromRgb(0xFF, 0xC4, 0x00));
    private static readonly Brush Alt = Frozen(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF));
    private static readonly Brush HeadFill = Frozen(Color.FromRgb(0x17, 0x17, 0x1B));

    private readonly IReadOnlyList<TableColumn> _columns;
    private readonly bool _checks, _icons;
    private readonly StackPanel _rows = new();
    private readonly List<TextBlock> _heads = new();
    private int _sortColumn = -1;
    private bool _descending;

    /// <summary>Clicou no título de uma coluna: (coluna, decrescente). A página ordena e chama <see cref="SetRows"/> de novo.</summary>
    public event Action<int, bool>? SortRequested;

    public CompactTable(IReadOnlyList<TableColumn> columns, bool checks = true, bool icons = true)
    {
        _columns = columns;
        _checks = checks;
        _icons = icons;
        CornerRadius = new CornerRadius(10);
        BorderThickness = new Thickness(1);
        BorderBrush = Res("BorderBrush");
        Background = Res("SurfaceBrush");
        ClipToBounds = true;
        var root = new StackPanel();
        root.Children.Add(Header());
        root.Children.Add(_rows);
        Child = root;
    }

    private static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    private Grid NewGrid()
    {
        var grid = new Grid();
        if (_checks) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        if (_icons) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        foreach (var c in _columns)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = c.Width > 0 ? new GridLength(c.Width) : new GridLength(1, GridUnitType.Star), MinWidth = c.Width > 0 ? 0 : 120 });
        return grid;
    }

    private int First => (_checks ? 1 : 0) + (_icons ? 1 : 0);

    private Border Header()
    {
        var grid = NewGrid();
        for (int i = 0; i < _columns.Count; i++)
        {
            int index = i;
            var text = new TextBlock
            {
                Text = _columns[i].Title.ToUpperInvariant(), FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Res("MutedBrush"),
                HorizontalAlignment = _columns[i].Align, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0),
                Cursor = Cursors.Hand, ToolTip = "Ordenar",
            };
            text.MouseLeftButtonUp += (_, _) =>
            {
                _descending = _sortColumn == index ? !_descending : index > 0;   // números começam do maior
                _sortColumn = index;
                PaintHeads();
                SortRequested?.Invoke(index, _descending);
            };
            Grid.SetColumn(text, First + i);
            grid.Children.Add(text);
            _heads.Add(text);
        }
        return new Border { Height = 30, Background = HeadFill, Padding = new Thickness(8, 0, 8, 0), Child = grid, BorderBrush = Res("BorderBrush"), BorderThickness = new Thickness(0, 0, 0, 1) };
    }

    private void PaintHeads()
    {
        for (int i = 0; i < _heads.Count; i++)
        {
            bool on = i == _sortColumn;
            _heads[i].Text = _columns[i].Title.ToUpperInvariant() + (on ? (_descending ? "  ▼" : "  ▲") : "");
            _heads[i].Foreground = on ? OnBorder : Res("MutedBrush");
        }
    }

    /// <summary>Troca as linhas (mantém a ordem pedida pela página).</summary>
    public void SetRows(IEnumerable<TableRow> rows)
    {
        _rows.Children.Clear();
        int n = 0;
        foreach (var row in rows) _rows.Children.Add(Line(row, n++));
        if (n == 0)
            _rows.Children.Add(new TextBlock { Text = "Nada para mostrar.", Foreground = Res("MutedBrush"), FontSize = 12, Margin = new Thickness(14, 10, 14, 10) });
    }

    private Border Line(TableRow row, int index)
    {
        var grid = NewGrid();
        if (_checks && row.Box is { } box)
        {
            UiTree.Free(box);
            box.VerticalAlignment = VerticalAlignment.Center;
            box.LayoutTransform = new ScaleTransform(0.8, 0.8);
            Grid.SetColumn(box, 0);
            grid.Children.Add(box);
        }
        if (_icons && row.Icon is { } icon)
        {
            UiTree.Free(icon);
            icon.Width = icon.Height = 18;
            icon.VerticalAlignment = VerticalAlignment.Center;
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetColumn(icon, _checks ? 1 : 0);
            grid.Children.Add(icon);
        }
        for (int i = 0; i < _columns.Count && i < row.Cells.Count; i++)
        {
            FrameworkElement cell = row.Cells[i] switch
            {
                FrameworkElement element => UiTree.Free(element),
                var value => new TextBlock
                {
                    Text = value?.ToString() ?? "", FontSize = i == 0 ? 12.5 : 12, FontWeight = i == 0 ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = i == 0 ? Res("TextBrush") : Res("MutedBrush"), TextTrimming = TextTrimming.CharacterEllipsis,
                },
            };
            cell.HorizontalAlignment = _columns[i].Align;
            cell.VerticalAlignment = VerticalAlignment.Center;
            cell.Margin = new Thickness(6, 0, 6, 0);
            Grid.SetColumn(cell, First + i);
            grid.Children.Add(cell);
        }

        var line = new Border
        {
            MinHeight = 30, Padding = new Thickness(8, 2, 8, 2), Child = grid, ToolTip = row.ToolTip,
            BorderThickness = new Thickness(2, 0, 0, 0), BorderBrush = Brushes.Transparent,
        };
        Brush rest = index % 2 == 1 ? Alt : Brushes.Transparent;
        void Paint()
        {
            bool on = row.Box?.IsChecked == true;
            line.Background = on ? OnFill : rest;
            line.BorderBrush = on ? OnBorder : Brushes.Transparent;
        }
        if (row.Box is { } b)
        {
            b.Checked += (_, _) => Paint();
            b.Unchecked += (_, _) => Paint();
            line.Cursor = b.IsEnabled ? Cursors.Hand : null;
            line.MouseLeftButtonUp += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject d && FindParent<ButtonBase>(d) is not null) return;   // botões da linha
                if (e.OriginalSource is not CheckBox && b.IsEnabled) b.IsChecked = b.IsChecked != true;
            };
        }
        Paint();
        return line;
    }

    private static T? FindParent<T>(DependencyObject d) where T : DependencyObject
    {
        for (var p = d; p is not null; p = p is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(p) : LogicalTreeHelper.GetParent(p))
            if (p is T t) return t;
        return null;
    }

    // ---------------------------------------------------------------- peças das células

    /// <summary>Selo colorido (ex.: "Pode desligar" em verde).</summary>
    public static Border Badge(string text, string brushKey)
    {
        var brush = Res(brushKey);
        var fill = brush is SolidColorBrush s ? Frozen(Color.FromArgb(0x1E, s.Color.R, s.Color.G, s.Color.B)) : Res("Surface2Brush");
        return new Border
        {
            CornerRadius = new CornerRadius(999), Padding = new Thickness(8, 1, 8, 2), Background = fill,
            Child = new TextBlock { Text = text, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = brush },
        };
    }

    /// <summary>Ícone fino da Segoe MDL2 num quadradinho (para linhas sem ícone do programa).</summary>
    public static Border Glyph(string glyph, string brushKey = "AccentBrush") => new()
    {
        CornerRadius = new CornerRadius(5), Background = Frozen(Color.FromRgb(0x1E, 0x1E, 0x23)),
        Child = new TextBlock
        {
            Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 10.5, Foreground = Res(brushKey),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        },
    };

    /// <summary>Número com barrinha (uso de CPU/memória), alinhado à direita.</summary>
    public static FrameworkElement Meter(string text, double fraction, string brushKey = "AccentBrush")
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(new TextBlock { Text = text, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, Foreground = Res("TextBrush") });
        var track = new Grid { Height = 3, Width = 60, Margin = new Thickness(0, 2, 0, 0) };
        track.Children.Add(new Border { Background = Res("BorderBrush"), CornerRadius = new CornerRadius(2) });
        track.Children.Add(new Border
        {
            Background = Res(brushKey), CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left,
            Width = 60 * Math.Clamp(fraction, 0, 1),
        });
        panel.Children.Add(track);
        return panel;
    }
}
