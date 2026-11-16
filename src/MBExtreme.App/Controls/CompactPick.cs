using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MBExtreme.Core.Apps;

namespace MBExtreme.UI.Controls;

/// <summary>
/// Peças compactas das listas de programas (Instalar, Atualizar, Desinstalador): cartão de 44 px com caixinha pequena,
/// ícone do programa, nome e descrição numa linha, selo à direita; busca com lupa; ícones da lista ou do próprio executável.
/// </summary>
internal static class CompactPick
{
    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);
    private static readonly Brush OnFill = Frozen(Color.FromArgb(0x22, 0xFF, 0xC4, 0x00));
    private static readonly Brush OnBorder = Frozen(Color.FromRgb(0xFF, 0xC4, 0x00));

    private static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    /// <summary>Cartão com caixinha. Clicar em qualquer parte marca/desmarca; fica amarelo quando marcado.</summary>
    public static Border Card(CheckBox box, FrameworkElement icon, string title, string detail, TextBlock? tag = null)
    {
        UiTree.Free(box);
        if (tag is not null) UiTree.Free(tag);
        box.VerticalAlignment = VerticalAlignment.Center;
        box.LayoutTransform = new ScaleTransform(0.82, 0.82);
        box.Margin = new Thickness(0, 0, 6, 0);
        System.Windows.Automation.AutomationProperties.SetName(box, "Marcar " + title);
        icon.Width = icon.Height = 24;
        icon.Margin = new Thickness(0, 0, 8, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;

        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(box, Dock.Left);
        row.Children.Add(box);
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        if (tag is not null)
        {
            tag.VerticalAlignment = VerticalAlignment.Center;
            tag.Margin = new Thickness(6, 0, 0, 0);
            DockPanel.SetDock(tag, Dock.Right);
            row.Children.Add(tag);
        }
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        if (detail.Length > 0)
            text.Children.Add(new TextBlock { Text = detail, FontSize = 11, Foreground = Res("MutedBrush"), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = detail });
        row.Children.Add(text);

        var card = new Border
        {
            Height = 44, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Padding = new Thickness(8, 0, 10, 0),
            Margin = new Thickness(0, 0, 6, 6), Child = row, Cursor = System.Windows.Input.Cursors.Hand,
        };
        void Paint()
        {
            bool on = box.IsChecked == true;
            card.Background = on ? OnFill : Res("SurfaceBrush");
            card.BorderBrush = on ? OnBorder : Res("BorderBrush");
            card.Opacity = box.IsEnabled ? 1 : 0.55;
        }
        box.Checked += (_, _) => Paint();
        box.Unchecked += (_, _) => Paint();
        box.IsEnabledChanged += (_, _) => Paint();
        card.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is not CheckBox && box.IsEnabled) box.IsChecked = box.IsChecked != true; };
        Paint();
        return card;
    }

    /// <summary>Selo pequeno à direita do cartão ("Instalado", "Nova versão", "Store"...).</summary>
    public static TextBlock Tag(string text, string brushKey = "MutedBrush") =>
        new() { Text = text, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Res(brushKey) };

    /// <summary>Grade que se ajusta à largura (cartões de pelo menos <paramref name="minWidth"/>).</summary>
    public static System.Windows.Controls.Primitives.UniformGrid Grid(int max = 6, double minWidth = 240)
    {
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, VerticalAlignment = VerticalAlignment.Top };
        grid.SizeChanged += (_, e) =>
        {
            int cols = Math.Clamp((int)(e.NewSize.Width / minWidth), 1, max);
            if (grid.Columns != cols) grid.Columns = cols;
        };
        return grid;
    }

    /// <summary>Campo de busca com lupa e texto de exemplo (some quando a pessoa digita).</summary>
    public static (Border Box, TextBox Input) Search(string placeholder, double width = 260)
    {
        var input = new TextBox
        {
            BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = Res("TextBrush"), FontSize = 12.5,
            VerticalContentAlignment = VerticalAlignment.Center, CaretBrush = Res("TextBrush"),
        };
        System.Windows.Automation.AutomationProperties.SetName(input, placeholder);
        var hint = new TextBlock { Text = placeholder, FontSize = 12.5, Foreground = Res("MutedBrush"), IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0) };
        input.TextChanged += (_, _) => hint.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var layer = new Grid();
        layer.Children.Add(hint);
        layer.Children.Add(input);
        var row = new DockPanel();
        var lens = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12, Foreground = Res("MutedBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(lens, Dock.Left);
        row.Children.Add(lens);
        row.Children.Add(layer);
        var box = new Border
        {
            Width = width, Height = 32, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Padding = new Thickness(10, 0, 10, 0),
            Background = Res("SurfaceBrush"), BorderBrush = Res("BorderBrush"), Child = row, VerticalAlignment = VerticalAlignment.Center,
        };
        box.MouseLeftButtonDown += (_, _) => input.Focus();
        return (box, input);
    }

    /// <summary>Botão compacto (32 px) para as barras de ação.</summary>
    public static Button Button(string text, bool primary = false)
    {
        var b = new Button { Content = text, Style = (Style)Application.Current.FindResource(primary ? "PrimaryButton" : "GhostButton"), Padding = new Thickness(12, 5, 12, 5), FontSize = 12.5, MinHeight = 32, Margin = new Thickness(6, 0, 0, 0) };
        return b;
    }

    private static readonly Dictionary<string, Geometry> Shapes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ícone de um programa da lista (desenho da marca na cor dela) ou as iniciais num quadrado colorido.</summary>
    public static FrameworkElement CatalogIcon(string id, string title)
    {
        var tile = new Border { CornerRadius = new CornerRadius(6), Background = Frozen(Color.FromRgb(0x1E, 0x1E, 0x23)) };
        if (AppIcons.Known.TryGetValue(id, out var known))
        {
            if (!Shapes.TryGetValue(id, out var shape))
            {
                try { shape = Geometry.Parse("F1 " + known.Path); shape.Freeze(); }
                catch (FormatException) { shape = Geometry.Empty; }
                Shapes[id] = shape;
            }
            if (!shape.IsEmpty())
            {
                var color = (Color)ColorConverter.ConvertFromString(known.Color);
                double light = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;   // marca escura some no fundo escuro
                tile.Child = new System.Windows.Shapes.Path
                {
                    Data = shape, Stretch = Stretch.Uniform, Width = 15, Height = 15, Fill = Frozen(light < 0.22 ? Colors.White : color),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                };
                return tile;
            }
        }
        return Initials(id, title);
    }

    /// <summary>Iniciais do programa num quadrado colorido (cor fixa por nome).</summary>
    public static Border Initials(string key, string title) => new()
    {
        CornerRadius = new CornerRadius(6), Background = Frozen((Color)ColorConverter.ConvertFromString(AppIcons.FallbackColor(key))),
        Child = new TextBlock
        {
            Text = AppIcons.Initials(title), FontSize = 9.5, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        },
    };

    private static readonly Dictionary<string, ImageSource?> ExeIcons = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ícone tirado do próprio programa no Windows (carrega em segundo plano; até lá ficam as iniciais).</summary>
    public static FrameworkElement ProgramIcon(string? iconPath, string title)
    {
        var host = new Grid();
        host.Children.Add(Initials(title, title));
        if (string.IsNullOrWhiteSpace(iconPath)) return host;
        _ = LoadAsync(host, iconPath);
        return host;
    }

    private static async Task LoadAsync(Grid host, string path)
    {
        ImageSource? source;
        if (!ExeIcons.TryGetValue(path, out source))
        {
            source = await Task.Run(() =>
            {
                try
                {
                    if (!System.IO.File.Exists(path)) return null;
                    using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                    if (icon is null) return null;
                    var bitmap = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    bitmap.Freeze();
                    return (ImageSource)bitmap;
                }
                catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { return null; }
            });
            ExeIcons[path] = source;
        }
        if (source is null) return;
        host.Children.Clear();
        host.Children.Add(new Image { Source = source, Stretch = Stretch.Uniform });
    }
}
