using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>Esqueleto comum das páginas de ferramentas: título, descrição, botões, status, conteúdo e painel de progresso.</summary>
public abstract class ToolPageBase : UserControl
{
    private readonly TextBlock _status = new() { Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap };
    protected readonly StackPanel Buttons = new() { Orientation = Orientation.Horizontal };
    protected readonly StackPanel Body = new() { Margin = new Thickness(0, 16, 0, 0) };
    protected readonly ProgressPanel Progress = new() { Visibility = Visibility.Collapsed };
    private readonly ScrollViewer _main;

    protected CancellationTokenSource? Cts;

    protected ToolPageBase(string title, string description)
    {
        var root = new StackPanel { Margin = new Thickness(40, 32, 40, 32) };
        root.Children.Add(new TextBlock { Text = title, FontSize = 28, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock
        {
            Text = description,
            Margin = new Thickness(0, 4, 0, 18),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("MutedBrush"),
        });
        Buttons.Orientation = Orientation.Horizontal;
        root.Children.Add(Buttons);
        root.Children.Add(_status);
        root.Children.Add(Body);

        _main = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
        Progress.Background = Brush("OverlayBrush");
        Progress.StopRequested += (_, _) => Cts?.Cancel();
        Progress.CloseRequested += (_, _) =>
        {
            _main.Visibility = Visibility.Visible;
            OnProgressClosed();
        };

        var grid = new Grid();
        grid.Children.Add(_main);
        grid.Children.Add(Progress);
        Content = grid;
    }

    protected virtual void OnProgressClosed() { }

    protected Brush Brush(string key) => (Brush)FindResource(key);

    protected Button AddButton(string text, RoutedEventHandler click, bool primary = true)
    {
        var button = new Button
        {
            Content = text,
            Style = (Style)FindResource(primary ? "PrimaryButton" : "GhostButton"),
            Margin = new Thickness(Buttons.Children.Count == 0 ? 0 : 12, 0, 0, 0),
        };
        button.Click += click;
        Buttons.Children.Add(button);
        return button;
    }

    protected void SetStatus(string text, string brush = "MutedBrush")
    {
        _status.Text = text;
        _status.Foreground = Brush(brush);
    }

    protected void ShowProgress(string title, params string[] steps)
    {
        _main.Visibility = Visibility.Collapsed;
        Progress.Begin(title, steps);
        Cts = new CancellationTokenSource();
    }

    protected TextBlock Heading(string text) => new()
    {
        Text = text,
        FontSize = 15,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 10, 0, 8),
    };

    protected TextBlock Muted(string text, double size = 12) => new()
    {
        Text = text,
        FontSize = size,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Brush("MutedBrush"),
    };

    protected Border CardOf(UIElement child) => new()
    {
        Style = (Style)FindResource("Card"),
        Margin = new Thickness(0, 0, 0, 8),
        Padding = new Thickness(16, 12, 16, 12),
        Child = child,
    };

    /// <summary>Linha "título + detalhe" com um controle à direita (botão ou interruptor).</summary>
    protected Border Row(string title, string detail, UIElement? right = null, string detailBrush = "MutedBrush")
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (detail.Length > 0)
            text.Children.Add(new TextBlock { Text = detail, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Brush(detailBrush) });
        grid.Children.Add(text);
        if (right is not null)
        {
            Grid.SetColumn(right, 1);
            if (right is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(right);
        }
        return CardOf(grid);
    }

    protected Button SmallButton(string text, RoutedEventHandler click, bool primary = false)
    {
        var button = new Button { Content = text, Style = (Style)FindResource(primary ? "PrimaryButton" : "GhostButton"), Margin = new Thickness(12, 0, 0, 0) };
        button.Click += click;
        return button;
    }

    protected CheckBox Check(string text, string detail = "")
    {
        var content = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
        content.Children.Add(new TextBlock { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (detail.Length > 0) content.Children.Add(Muted(detail));
        return new CheckBox { Content = content, Foreground = Brush("TextBrush"), VerticalContentAlignment = VerticalAlignment.Top };
    }

    protected static void Open(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show("Não foi possível abrir o endereço: " + ex.Message);
        }
    }
}
