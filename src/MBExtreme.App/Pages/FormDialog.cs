using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MBExtreme.UI.Pages;

/// <summary>Janela de confirmação simples (modal) com campos, um resumo do que vai acontecer e, se preciso, uma frase para digitar antes de continuar.</summary>
internal sealed class FormDialog : Window
{
    private readonly StackPanel _body = new();
    private readonly TextBlock _error;
    private readonly Button _ok;
    private TextBox? _typed;
    private string _phrase = "";

    public FormDialog(Window? owner, string title, string description, string okText = "Continuar", bool danger = false)
    {
        Title = title;
        Owner = owner;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        SizeToContent = SizeToContent.Height;
        Width = 560;
        MaxHeight = 760;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = Brush("BgBrush");
        Foreground = Brush("TextBrush");

        var root = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        root.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new TextBlock { Text = description, Margin = new Thickness(0, 6, 0, 10), TextWrapping = TextWrapping.Wrap, Foreground = Brush("MutedBrush") });
        root.Children.Add(_body);

        _error = new TextBlock { Foreground = Brush("BadBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        root.Children.Add(_error);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = "Cancelar", Style = (Style)Application.Current.FindResource("GhostButton"), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        _ok = new Button { Content = okText, Style = (Style)Application.Current.FindResource(danger ? "GhostButton" : "PrimaryButton"), IsDefault = false };
        if (danger) _ok.Foreground = Brush("BadBrush");
        _ok.Click += (_, _) =>
        {
            string? problem = Validator?.Invoke();
            if (problem is not null)
            {
                _error.Text = problem;
                _error.Visibility = Visibility.Visible;
                return;
            }

            Confirmed = true;
            Close();
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(_ok);
        root.Children.Add(buttons);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
    }

    public bool Confirmed { get; private set; }

    /// <summary>Confere os campos antes de fechar; devolve a mensagem do problema ou null.</summary>
    public Func<string?>? Validator { get; set; }

    private static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);

    public void Note(string text, string brush = "MutedBrush", double size = 12)
    {
        _body.Children.Add(new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = Brush(brush), Margin = new Thickness(0, 4, 0, 4) });
    }

    /// <summary>Texto em fonte de código (o comando que será executado).</summary>
    public TextBlock Command(string text)
    {
        var block = new TextBlock { Text = text, FontFamily = new FontFamily("Consolas"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = Brush("MutedBrush") };
        _body.Children.Add(new Border
        {
            Background = Brush("SurfaceBrush"), BorderBrush = Brush("BorderBrush"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 6, 0, 4), Child = block,
        });
        return block;
    }

    public TextBlock Summary(string text, string brush = "TextBrush")
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brush(brush), Margin = new Thickness(0, 6, 0, 4) };
        _body.Children.Add(block);
        return block;
    }

    /// <summary>Escolha única entre botões. Devolve uma função que lê a opção escolhida.</summary>
    public Func<int> Choice(string label, IReadOnlyList<string> options, int selected, Action<int>? changed = null)
    {
        _body.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Brush("MutedBrush"), Margin = new Thickness(0, 8, 0, 2) });
        var panel = new WrapPanel();
        var buttons = new List<Button>();
        int current = Math.Clamp(selected, 0, Math.Max(0, options.Count - 1));
        void Paint()
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                buttons[i].Style = (Style)Application.Current.FindResource(i == current ? "PrimaryButton" : "GhostButton");
                buttons[i].Padding = new Thickness(10, 3, 10, 3);
                buttons[i].FontSize = 12;
            }
        }

        for (int i = 0; i < options.Count; i++)
        {
            int index = i;
            var button = new Button { Content = options[i], Margin = new Thickness(0, 0, 5, 5) };
            button.Click += (_, _) => { current = index; Paint(); changed?.Invoke(index); };
            buttons.Add(button);
            panel.Children.Add(button);
        }

        Paint();
        _body.Children.Add(panel);
        return () => current;
    }

    public Func<string> Text(string label, string initial, int maxLength = 64, Action<string>? changed = null)
    {
        _body.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Brush("MutedBrush"), Margin = new Thickness(0, 8, 0, 2) });
        var box = new TextBox { Text = initial, MaxLength = maxLength, HorizontalAlignment = HorizontalAlignment.Left, Width = 260 };
        if (changed is not null) box.TextChanged += (_, _) => changed(box.Text);
        _body.Children.Add(box);
        return () => box.Text.Trim();
    }

    /// <summary>Só libera o botão depois que a pessoa digita a frase (para operações que apagam dados).</summary>
    public void RequireTyped(string phrase, string prompt)
    {
        _phrase = phrase;
        _body.Children.Add(new TextBlock { Text = prompt, FontSize = 12, Foreground = Brush("WarnBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 2) });
        _typed = new TextBox { HorizontalAlignment = HorizontalAlignment.Left, Width = 160, MaxLength = 24 };
        _typed.TextChanged += (_, _) => _ok.IsEnabled = Matches();
        _body.Children.Add(_typed);
        _ok.IsEnabled = false;
    }

    private bool Matches() => _typed is not null && string.Equals(_typed.Text.Trim(), _phrase, StringComparison.OrdinalIgnoreCase);
}
