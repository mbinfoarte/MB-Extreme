using System.IO;
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
        _title = new TextBlock { Text = title, FontSize = 28, FontWeight = FontWeights.SemiBold };
        root.Children.Add(_title);
        _description = new TextBlock
        {
            Text = description,
            Margin = new Thickness(0, 4, 0, 18),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("MutedBrush"),
        };
        root.Children.Add(_description);
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

    private readonly TextBlock _title;
    private readonly TextBlock _description;

    /// <summary>Para páginas que desenham o próprio topo: esconde o título grande, a descrição e a linha de botões.</summary>
    protected void HideHeader()
    {
        _title.Visibility = Visibility.Collapsed;
        _description.Visibility = Visibility.Collapsed;
        Buttons.Visibility = Visibility.Collapsed;
    }

    private bool _loadingNow;

    /// <summary>Evita duas cargas ao mesmo tempo (a página volta à tela enquanto a leitura anterior ainda roda).</summary>
    protected async Task RunOnce(Func<Task> load)
    {
        if (_loadingNow) return;
        _loadingNow = true;
        try
        {
            await load();
        }
        finally
        {
            _loadingNow = false;
        }
    }

    /// <summary>Copia texto para a área de transferência; outro programa pode estar usando-a, o que não deve derrubar o app.</summary>
    protected bool TryCopy(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            SetStatus("A área de transferência está em uso por outro programa. Tente de novo.", "WarnBrush");
            return false;
        }
    }

    protected Brush Brush(string key) => (Brush)FindResource(key);

    /// <summary>
    /// Tira o elemento de onde ele estava (painel, borda ou conteúdo) para poder colocá-lo num lugar novo.
    /// No WPF um elemento só pode ter um pai: remontar a tela sem isto derruba o programa.
    /// </summary>
    protected static T Free<T>(T element) where T : FrameworkElement => UiTree.Free(element);

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

    /// <summary>Botões "Marcar todos" e "Desmarcar todos" para qualquer lista de caixas de seleção (as desabilitadas ficam como estão).</summary>
    protected (Button All, Button None) AddSelectionButtons(Func<IEnumerable<CheckBox>> boxes, string allText = "Marcar todos", string noneText = "Desmarcar todos")
    {
        var all = AddButton(allText, (_, _) => { foreach (var box in boxes().Where(b => b.IsEnabled)) box.IsChecked = true; }, false);
        var none = AddButton(noneText, (_, _) => { foreach (var box in boxes().Where(b => b.IsEnabled)) box.IsChecked = false; }, false);
        return (all, none);
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

    protected Border CardOf(UIElement child)
    {
        // Telas que se remontam reaproveitam painéis guardados: solta o filho do pai antigo
        // para não dar "O elemento especificado já é o filho lógico de outro elemento".
        if (child is FrameworkElement fe) UiTree.Free(fe);
        return new()
        {
            Style = (Style)FindResource("Card"),
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(16, 12, 16, 12),
            Child = child,
        };
    }

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

    /// <summary>Grupo de botões de escolha única (o escolhido fica amarelo).</summary>
    protected StackPanel Choice(IReadOnlyList<string> options, int selected, Action<int> onChange)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
        var buttons = new List<Button>();
        void Paint(int index)
        {
            for (int i = 0; i < buttons.Count; i++)
                buttons[i].Style = (Style)FindResource(i == index ? "PrimaryButton" : "GhostButton");
        }

        for (int i = 0; i < options.Count; i++)
        {
            int index = i;
            var button = new Button { Content = options[i], Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0) };
            button.Click += (_, _) =>
            {
                Paint(index);
                onChange(index);
            };
            buttons.Add(button);
            panel.Children.Add(button);
        }

        Paint(selected);
        return panel;
    }

    private CheckBox? _recordOption;

    /// <summary>Caixa "Gravar a tela durante o teste". Use <see cref="BeginRecordingIfChecked"/> e <see cref="EndRecording"/> ao redor do teste.</summary>
    protected CheckBox RecordOption()
    {
        _recordOption = new CheckBox
        {
            Content = "Gravar a tela durante o teste (salva em Vídeos\\MB Extreme)",
            Foreground = Brush("TextBrush"),
            Margin = new Thickness(0, 14, 0, 0),
        };
        return _recordOption;
    }

    protected string? BeginRecordingIfChecked(string label)
    {
        if (_recordOption?.IsChecked != true || AppServices.Recorder.IsRecording) return null;
        try
        {
            return AppServices.Recorder.Start(label, 15, 0.75);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            SetStatus("Não foi possível gravar: " + ex.Message, "WarnBrush");
            return null;
        }
    }

    protected void EndRecording(string? file)
    {
        if (file is null) return;
        AppServices.Recorder.Stop();
        SetStatus("Gravação salva em " + file, "OkBrush");
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
