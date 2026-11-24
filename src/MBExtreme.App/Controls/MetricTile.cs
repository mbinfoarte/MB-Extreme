using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MBExtreme.UI.Controls;

/// <summary>Cartão com título, valor atual e gráfico animado do histórico.</summary>
public sealed class MetricTile : Border
{
    private const int Length = 60;

    private readonly List<double> _history = new();
    private readonly TextBlock _value = new() { FontSize = 26, FontWeight = FontWeights.SemiBold, Text = "—" };
    private readonly TextBlock _detail = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly LiveChart _chart;

    public MetricTile(string title, Color color, double maximum = 0, Func<double, string>? formatMax = null)
    {
        Style = (Style)Application.Current.FindResource("Card");
        Margin = new Thickness(0, 0, 0, 12);

        var muted = (Brush)Application.Current.FindResource("MutedBrush");
        _detail.Foreground = muted;
        _chart = new LiveChart(Length, color) { Height = 78, Margin = new Thickness(0, 8, 0, 0), Maximum = maximum };
        if (formatMax is not null) _chart.FormatMax = formatMax;

        var header = new Grid();
        header.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold });
        _value.HorizontalAlignment = HorizontalAlignment.Right;
        header.Children.Add(_value);

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(_detail);
        stack.Children.Add(_chart);
        Child = stack;
    }

    /// <summary>Mostra o valor e acrescenta ao gráfico. Valor nulo: sensor indisponível.</summary>
    public void Push(double? value, string text, string detail = "")
    {
        _value.Text = value is null ? "—" : text;
        _detail.Text = detail;
        if (value is null) return;
        _history.Add(value.Value);
        if (_history.Count > Length) _history.RemoveAt(0);
        _chart.Update(_history.ToList());
    }
}

/// <summary>Barra única de uso: verde é o espaço livre; a parte usada fica laranja e passa a vermelha quando está quase cheio.</summary>
public static class UsageBar
{
    public static Grid Create(double usedPercent, double height = 12)
    {
        usedPercent = Math.Clamp(usedPercent, 0, 100);
        var usedColor = usedPercent >= 85 ? Color.FromRgb(0xFF, 0x5A, 0x5F) : Color.FromRgb(0xFF, 0xA9, 0x4D);
        var freeColor = Color.FromRgb(0x3D, 0xDC, 0x97);

        var grid = new Grid { Height = height, Margin = new Thickness(0, 6, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(usedPercent, 0.001), GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(100 - usedPercent, 0.001), GridUnitType.Star) });

        var used = new Border { Background = new SolidColorBrush(usedColor), CornerRadius = new CornerRadius(height / 2, 0, 0, height / 2), Visibility = usedPercent <= 0 ? Visibility.Collapsed : Visibility.Visible };
        var free = new Border { Background = new SolidColorBrush(freeColor), CornerRadius = new CornerRadius(0, height / 2, height / 2, 0) };
        if (usedPercent <= 0) free.CornerRadius = new CornerRadius(height / 2);
        if (usedPercent >= 100) { used.CornerRadius = new CornerRadius(height / 2); free.Visibility = Visibility.Collapsed; }
        Grid.SetColumn(free, 1);
        grid.Children.Add(used);
        grid.Children.Add(free);
        return grid;
    }

    /// <summary>Barra fina para uso por núcleo e similares.</summary>
    public static Grid Thin(double percent, Color color)
    {
        percent = Math.Clamp(percent, 0, 100);
        var grid = new Grid { Height = 6, Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(percent, 0.001), GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(100 - percent, 0.001), GridUnitType.Star) });
        var track = new Border { Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x2A)), CornerRadius = new CornerRadius(3) };
        Grid.SetColumnSpan(track, 2);
        grid.Children.Add(track);
        var fill = new Border { Background = new SolidColorBrush(color), CornerRadius = new CornerRadius(3) };
        Grid.SetColumn(fill, 0);
        grid.Children.Add(fill);
        return grid;
    }
}
