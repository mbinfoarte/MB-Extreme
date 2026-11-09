using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MBExtreme.Core.Recording;

namespace MBExtreme.UI.Controls;

/// <summary>Chat da transmissão dentro do programa, com as mensagens pagas em destaque e uma lista de perguntas para responder.</summary>
internal sealed class ChatPanel : Border
{
    private const int MaxMessages = 300;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    private readonly Func<(StreamService Service, string Id, string Key)> _source;
    private readonly ChatFeed _feed = new();
    private readonly QuestionBoard _board = new();
    private readonly List<ChatMessage> _all = new();
    private readonly StackPanel _messages = new();
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _questions = new();
    private readonly TextBlock _state = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
    private readonly TextBlock _questionTitle = new() { FontSize = 13, FontWeight = FontWeights.SemiBold };
    private readonly Button _connect;
    private readonly Dispatcher _ui = Application.Current.Dispatcher;
    private int _mode;

    public ChatPanel(Func<(StreamService Service, string Id, string Key)> source)
    {
        _source = source;
        Style = (Style)Application.Current.FindResource("Card");
        Padding = new Thickness(14, 10, 14, 10);
        Margin = new Thickness(0, 8, 0, 0);

        var root = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new TextBlock { Text = "Chat e perguntas", FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        _connect = Button("Conectar ao chat", (_, _) => Toggle(), true);
        Grid.SetColumn(_connect, 1);
        head.Children.Add(_connect);
        root.Children.Add(head);

        _state.Foreground = Resource("MutedBrush");
        root.Children.Add(_state);

        var filters = new WrapPanel();
        var modes = new[] { "Todas", "Pagas", "Perguntas e pagas" };
        var buttons = new List<Button>();
        for (int i = 0; i < modes.Length; i++)
        {
            int index = i;
            var b = Button(modes[i], (_, _) => { _mode = index; PaintFilters(buttons); Rebuild(); });
            b.Margin = new Thickness(0, 0, 6, 4);
            buttons.Add(b);
            filters.Children.Add(b);
        }
        PaintFilters(buttons);
        root.Children.Add(filters);

        _scroll = new ScrollViewer { Height = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _messages };
        root.Children.Add(new Border { Background = Resource("SurfaceBrush"), BorderBrush = Resource("BorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(6), Child = _scroll });

        var qHead = new Grid { Margin = new Thickness(0, 10, 0, 4) };
        qHead.ColumnDefinitions.Add(new ColumnDefinition());
        qHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        qHead.Children.Add(_questionTitle);
        var qButtons = new StackPanel { Orientation = Orientation.Horizontal };
        qButtons.Children.Add(Button("Copiar lista", (_, _) => { if (_board.Items.Count > 0) { try { Clipboard.SetText(_board.ToText()); } catch (System.Runtime.InteropServices.ExternalException) { } } }));
        qButtons.Children.Add(Button("Limpar respondidas", (_, _) => { _board.ClearAnswered(); RebuildQuestions(); }));
        Grid.SetColumn(qButtons, 1);
        qHead.Children.Add(qButtons);
        root.Children.Add(qHead);
        root.Children.Add(_questions);
        Child = root;

        _feed.Message += m => _ui.BeginInvoke(new Action(() => Add(m)));
        _feed.Status += (text, ok) => _ui.BeginInvoke(new Action(() => SetState(text, ok)));
        Unloaded += (_, _) => { };   // a conexão segue em segundo plano ao trocar de página
        RebuildQuestions();
        SetState("Desconectado. Informe o canal ou a live na aba Transmissão e conecte.", false);
    }

    private static Brush Resource(string key) => (Brush)Application.Current.FindResource(key);

    private static Button Button(string text, RoutedEventHandler click, bool primary = false)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 3, 10, 3), FontSize = 11.5, Margin = new Thickness(8, 0, 0, 0), Style = (Style)Application.Current.FindResource(primary ? "PrimaryButton" : "GhostButton") };
        b.Click += click;
        return b;
    }

    private void PaintFilters(List<Button> buttons)
    {
        for (int i = 0; i < buttons.Count; i++) buttons[i].Style = (Style)Application.Current.FindResource(i == _mode ? "PrimaryButton" : "GhostButton");
        foreach (var b in buttons) { b.Padding = new Thickness(10, 3, 10, 3); b.FontSize = 11.5; }
    }

    public bool IsConnected => _feed.IsRunning;

    public void Connect()
    {
        var (service, id, key) = _source();
        switch (service)
        {
            case StreamService.Twitch:
                if (ChatLinks.TwitchLogin(id) is not { } login) { SetState("Informe o nome do seu canal da Twitch (aba Transmissão).", false); return; }
                _feed.StartTwitch(login);
                break;
            case StreamService.YouTube:
                if (ChatLinks.YouTubeVideoId(id) is not { } video) { SetState("Informe o código ou o endereço da live do YouTube (aba Transmissão).", false); return; }
                if (!LiveStatsService.IsValidApiKey(key)) { SetState("Informe a chave de API do YouTube (aba Estatísticas). Sem ela, use \"Abrir o chat\" para ver numa janela.", false); return; }
                _feed.StartYouTube(Http, video, key.Trim());
                SetState("Conectando ao chat do YouTube...", true);
                break;
            default:
                SetState("Facebook e outros servidores não liberam o chat por aqui. Use \"Abrir o chat\" ou o painel da plataforma.", false);
                return;
        }

        _connect.Content = "Desconectar";
        _connect.Style = (Style)Application.Current.FindResource("GhostButton");
    }

    public void Disconnect()
    {
        _feed.Stop();
        _connect.Content = "Conectar ao chat";
        _connect.Style = (Style)Application.Current.FindResource("PrimaryButton");
        SetState("Desconectado.", false);
    }

    private void Toggle()
    {
        if (_feed.IsRunning) Disconnect(); else Connect();
    }

    private void SetState(string text, bool ok)
    {
        _state.Text = text;
        _state.Foreground = Resource(ok ? "OkBrush" : "MutedBrush");
    }

    private void Add(ChatMessage message)
    {
        _all.Add(message);
        if (_all.Count > MaxMessages) _all.RemoveAt(0);
        if (!ChatRules.Filter(new[] { message }, _mode).Any()) return;
        bool atEnd = _scroll.VerticalOffset >= _scroll.ScrollableHeight - 8;
        _messages.Children.Add(Row(message));
        while (_messages.Children.Count > MaxMessages) _messages.Children.RemoveAt(0);
        if (atEnd) _scroll.ScrollToEnd();
    }

    private void Rebuild()
    {
        _messages.Children.Clear();
        foreach (var m in ChatRules.Filter(_all, _mode).TakeLast(MaxMessages)) _messages.Children.Add(Row(m));
        _scroll.ScrollToEnd();
    }

    private UIElement Row(ChatMessage m)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5 };
        Brush authorBrush = Resource("TextBrush");
        if (m.Color is { } c)
        {
            try { authorBrush = (Brush)new BrushConverter().ConvertFromString(c)!; }
            catch (FormatException) { }
        }
        text.Inlines.Add(new System.Windows.Documents.Run(m.Author) { FontWeight = FontWeights.Bold, Foreground = authorBrush });
        if (m.Amount is { } amount) text.Inlines.Add(new System.Windows.Documents.Run($"  [{amount}]") { FontWeight = FontWeights.SemiBold, Foreground = Resource("AccentBrush") });
        text.Inlines.Add(new System.Windows.Documents.Run(": " + m.Text) { Foreground = Resource(m.Kind is ChatKind.Raid or ChatKind.Notice ? "MutedBrush" : "TextBrush") });
        grid.Children.Add(text);

        var ask = Button("＋ Pergunta", (_, _) => { if (_board.Add(m)) RebuildQuestions(); });
        ask.Padding = new Thickness(6, 1, 6, 1);
        ask.FontSize = 10.5;
        ask.ToolTip = "Guarda esta mensagem na lista de perguntas para responder";
        Grid.SetColumn(ask, 1);
        grid.Children.Add(ask);

        var border = new Border { Child = grid, Padding = new Thickness(6, 2, 6, 2), CornerRadius = new CornerRadius(6) };
        if (m.IsPaid)
        {
            border.BorderBrush = Resource("AccentBrush");
            border.BorderThickness = new Thickness(2, 0, 0, 0);
            border.Background = new SolidColorBrush(Color.FromArgb(40, 255, 196, 0));
        }
        else if (ChatRules.IsQuestion(m.Text)) border.BorderBrush = Resource("BorderBrush");
        return border;
    }

    private void RebuildQuestions()
    {
        _questions.Children.Clear();
        _questionTitle.Text = _board.Items.Count == 0 ? "Perguntas escolhidas" : $"Perguntas escolhidas ({_board.Pending} para responder)";
        if (_board.Items.Count == 0)
        {
            _questions.Children.Add(new TextBlock { Text = "Clique em \"＋ Pergunta\" numa mensagem para guardá-la aqui. As pagas ficam em destaque no chat.", FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Foreground = Resource("MutedBrush") });
            return;
        }

        // as pagas primeiro, depois as outras na ordem em que entraram; respondidas por último
        foreach (var item in _board.Items.OrderBy(i => i.Answered).ThenByDescending(i => i.Message.IsPaid))
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var done = new CheckBox { IsChecked = item.Answered, ToolTip = "Respondida", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            string id = item.Message.Id;
            done.Click += (_, _) => { _board.SetAnswered(id, done.IsChecked == true); RebuildQuestions(); };
            grid.Children.Add(done);
            var text = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center,
                Foreground = Resource(item.Answered ? "MutedBrush" : "TextBrush"),
                TextDecorations = item.Answered ? TextDecorations.Strikethrough : null,
            };
            text.Inlines.Add(new System.Windows.Documents.Run(item.Message.Author) { FontWeight = FontWeights.Bold });
            if (item.Message.Amount is { } amount) text.Inlines.Add(new System.Windows.Documents.Run($" [{amount}]") { Foreground = Resource("AccentBrush") });
            text.Inlines.Add(new System.Windows.Documents.Run(": " + item.Message.Text));
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            var remove = Button("Remover", (_, _) => { _board.Remove(id); RebuildQuestions(); });
            remove.Padding = new Thickness(6, 1, 6, 1);
            remove.FontSize = 10.5;
            Grid.SetColumn(remove, 2);
            grid.Children.Add(remove);
            _questions.Children.Add(grid);
        }
    }
}
