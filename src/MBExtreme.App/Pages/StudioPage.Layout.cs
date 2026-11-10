using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MBExtreme.Core.Recording;
using MBExtreme.Core.Settings;

namespace MBExtreme.UI.Pages;

/// <summary>Organização da tela do estúdio: três ícones grandes (Gravar, Transmitir, Programa), cada um com as suas abas, cartões com "Editar" e janelas pequenas no lugar de listas longas.</summary>
public sealed partial class StudioPage
{
    private static readonly SolidColorBrush Yellow = Frozen(Color.FromRgb(0xFF, 0xC4, 0x00));
    private static readonly SolidColorBrush Ink = Frozen(Color.FromRgb(0x0B, 0x0B, 0x0C));

    private readonly StackPanel _tabExtras = new(), _tabAccounts = new(), _tabPixCard = new();
    private readonly Button _bigStart = new() { Height = 42, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _rehearsalButton = new() { Content = "Ensaio", Height = 42, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 8), Padding = new Thickness(18, 0, 18, 0) };
    private Action<int>? _selectTab;
    private bool _startBusy, _accountsLoaded;

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    // ---------------------------------------------------------------- três ícones grandes e as abas de cada um

    private void BuildSettings()
    {
        var tabs = new[] { _tabVideo, _tabVertical, _tabImage, _tabAudio, _tabCamera, _tabChannel, _tabPixCard, _tabStream, _tabStats, _tabReplay, _tabShortcuts, _tabAdvanced, _tabExtras, _tabAccounts };
        string[] names = { "Vídeo", "Vertical", "Imagem", "Áudio", "Câmeras", "Canal", "Pix", "Conexão", "Estatísticas", "Replay", "Atalhos", "Avançado", "Extras", "Contas e chaves" };
        var groups = new (string Label, string Icon, int[] Tabs)[]
        {
            ("Gravar", "record", new[] { 4, 3, 0, 2, 1, 9 }),
            ("Transmitir", "stream", new[] { 5, 6, 8, 7, 12 }),
            ("Programa", "gear", new[] { 13, 10, 11 }),
        };
        var lastTab = groups.Select(g => g.Tabs[0]).ToArray();
        _settingsTab = 4;

        var bigRow = new Grid { Margin = new Thickness(0, 14, 0, 8) };
        var subRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        BuildScenes();
        var bigs = new List<(Border Tile, TextBlock Label, List<Shape> Strokes, List<Shape> Fills)>();

        void Show(int tab)
        {
            _settingsTab = tab;
            int group = Math.Max(0, Array.FindIndex(groups, g => g.Tabs.Contains(tab)));
            lastTab[group] = tab;
            for (int t = 0; t < tabs.Length; t++) tabs[t].Visibility = Vis(t == tab);

            for (int i = 0; i < bigs.Count; i++)
            {
                bool on = i == group;
                var (tile, label, strokes, fills) = bigs[i];
                tile.Background = on ? Yellow : Brush("SurfaceBrush");
                tile.BorderBrush = on ? Yellow : Brush("BorderBrush");
                label.Foreground = on ? Ink : Brush("TextBrush");
                foreach (var shape in strokes) shape.Stroke = on ? Ink : Brush("MutedBrush");
                foreach (var shape in fills) shape.Fill = on ? Ink : Brush("MutedBrush");
            }

            subRow.Children.Clear();
            foreach (int id in groups[group].Tabs)
            {
                int target = id;
                var chip = new Button { Content = names[id], Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(12, 4, 12, 4), FontSize = 12.5 };
                chip.Style = (Style)FindResource(id == tab ? "PrimaryButton" : "GhostButton");
                chip.Padding = new Thickness(12, 4, 12, 4);
                chip.Click += (_, _) => Show(target);
                subRow.Children.Add(chip);
            }

            if (tabs[tab] == _tabStream) UpdatePlan();
            if (tab == 3) RefreshLimiterNow();
            if (tab == 6) BuildPixCard();
            if (tab == 12) BuildExtras();
        }

        for (int i = 0; i < groups.Length; i++)
        {
            int group = i;
            bigRow.ColumnDefinitions.Add(new ColumnDefinition());
            var (icon, strokes, fills) = BuildIcon(groups[i].Icon);
            // ícone ao lado do nome, menor: os três botões ocupam uma faixa baixa
            icon.Margin = new Thickness(0, 0, 8, 0);
            if (icon is Viewbox box) { box.Width = 20; box.Height = 20; }
            var label = new TextBlock { Text = groups[i].Label, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(icon);
            content.Children.Add(label);
            var tile = new Border
            {
                CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(10, 7, 10, 7), MinHeight = 40,
                Margin = new Thickness(i == 0 ? 0 : 5, 0, i == groups.Length - 1 ? 0 : 5, 0), Cursor = System.Windows.Input.Cursors.Hand, Child = content,
            };
            tile.MouseLeftButtonUp += (_, _) => Show(lastTab[group]);
            Grid.SetColumn(tile, i);
            bigRow.Children.Add(tile);
            bigs.Add((tile, label, strokes, fills));
        }

        _config.Children.Add(bigRow);
        // o painel do ícone escolhido num cartão só, como na maquete: as abas em cima e o conteúdo embaixo
        var panel = new StackPanel();
        subRow.Margin = new Thickness(0, 0, 0, 10);
        panel.Children.Add(subRow);
        foreach (var tab in tabs) panel.Children.Add(tab);
        var panelCard = CardOf(panel);
        panelCard.Padding = new Thickness(14);
        panelCard.CornerRadius = new CornerRadius(12);
        _config.Children.Add(panelCard);
        _selectTab = Show;
        Show(_settingsTab);
    }

    // ---------------------------------------------------------------- botão grande Iniciar / Parar

    private UIElement BuildBigStart()
    {
        _bigStart.Click += async (_, _) =>
        {
            if (_startBusy) return;
            _startBusy = true;
            try
            {
                if (_studio.IsRunning || AppServices.Recorder.IsRecording) await EndAsync();
                else await BeginAsync();
            }
            finally
            {
                _startBusy = false;
                RefreshBigStart();
            }
        };
        RefreshBigStart();

        // Ensaio: ao lado do Iniciar, testa a live completa sem mandar nada
        _rehearsalButton.Style = (Style)FindResource("GhostButton");
        _rehearsalButton.ToolTip = "Ensaio: monta a live inteira (cenas, câmeras, sobreposições, transições e codificador) e mostra a imagem final, sem transmitir nem gravar.";
        _rehearsalButton.Click += async (_, _) =>
        {
            if (_startBusy) return;
            _startBusy = true;
            try
            {
                if (_studio.IsRunning && _studio.Session.Request is { Rehearsal: true }) await GoLiveFromRehearsalAsync();
                else await BeginRehearsalAsync();
            }
            finally { _startBusy = false; RefreshBigStart(); }
        };
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(_bigStart);
        Grid.SetColumn(_rehearsalButton, 1);
        row.Children.Add(_rehearsalButton);
        return row;
    }

    private void RefreshBigStart()
    {
        bool running = _studio.IsRunning || AppServices.Recorder.IsRecording;
        _bigStart.Style = (Style)FindResource(running ? "GhostButton" : "PrimaryButton");
        _bigStart.Content = running ? "■  Parar" : "●  Iniciar";
        _bigStart.Foreground = running ? Brush("BadBrush") : Ink;
        _bigStart.BorderBrush = running ? Brush("BadBrush") : Yellow;
        _bigStart.ToolTip = running ? "Para a gravação e a transmissão e salva o arquivo." : "Começa o que estiver ligado logo abaixo: gravar, transmitir e replay.";
        bool rehearsing = _studio.IsRunning && _studio.Session.Request is { Rehearsal: true };
        if (rehearsing)
        {
            _bigStart.Content = "■  Parar o ensaio";
            _bigStart.ToolTip = "Encerra o ensaio. Nada foi transmitido nem gravado.";
        }
        _rehearsalButton.IsEnabled = !running || rehearsing;
        _rehearsalButton.Content = rehearsing ? "●  Entrar no ar" : "Ensaio";
        _rehearsalButton.Style = (Style)FindResource(rehearsing ? "PrimaryButton" : "GhostButton");
        _rehearsalButton.ToolTip = rehearsing
            ? "Passa o que está no ensaio para o público: começa a gravar, transmitir e o replay que estiverem ligados (a captura reinicia por um instante)."
            : "Ensaio: monta a live inteira (cenas, câmeras, sobreposições, transições e codificador) e mostra a imagem final, sem transmitir nem gravar.";
    }

    // ---------------------------------------------------------------- janelas pequenas, cartões e ajuda

    /// <summary>Abre uma janela pequena com o conteúdo (as configurações longas ficam aqui, e não na tela principal). As mudanças valem na hora.</summary>
    private void ShowEditor(string title, FrameworkElement content, Action? closed = null, double width = 620)
    {
        Detach(content);
        var owner = Window.GetWindow(this);
        var root = new DockPanel { Margin = new Thickness(20, 16, 20, 16) };
        var header = new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        var done = new Button { Content = "Pronto", Style = (Style)FindResource("PrimaryButton"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0), IsDefault = true };
        DockPanel.SetDock(done, Dock.Bottom);
        root.Children.Add(done);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = content };
        root.Children.Add(scroll);

        var window = new Window
        {
            Title = title, Owner = owner, WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Width = width, SizeToContent = SizeToContent.Height, MaxHeight = Math.Max(420, (owner?.ActualHeight ?? 800) * 0.9),
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Background = Brush("BgBrush"), Foreground = Brush("TextBrush"), Content = root,
        };
        done.Click += (_, _) => window.Close();
        window.Closed += (_, _) =>
        {
            scroll.Content = null;   // o mesmo conteúdo pode abrir de novo depois
            _s.Save();
            SyncScene();   // ligou ou desligou algo na janela: a cena escolhida lembra
            closed?.Invoke();
        };
        window.ShowDialog();
    }

    /// <summary>Cartão curto: título, "Ligado/Desligado", uma frase e um botão (Editar, Configurar, Abrir).</summary>
    private Border SummaryCard(string title, bool? on, string text, string action, Action open, double width = 250)
    {
        var panel = new StackPanel();
        var head = new DockPanel();
        if (on is bool isOn)
        {
            var pill = new Border
            {
                CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 1, 7, 1), VerticalAlignment = VerticalAlignment.Center,
                Background = isOn ? Yellow : Brush("BorderBrush"),
                Child = new TextBlock { Text = isOn ? "Ligado" : "Desligado", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = isOn ? Ink : Brush("MutedBrush") },
            };
            DockPanel.SetDock(pill, Dock.Right);
            head.Children.Add(pill);
        }
        head.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        panel.Children.Add(head);
        var desc = Muted(text, 11.5);
        desc.Margin = new Thickness(0, 4, 0, 8);
        desc.MinHeight = 30;
        panel.Children.Add(desc);
        var button = SmallButton(action, (_, _) => open());
        button.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(button);

        var card = CardOf(panel);
        card.Width = width;
        card.Padding = new Thickness(12, 10, 12, 10);
        card.Margin = new Thickness(0, 0, 10, 10);
        card.CornerRadius = new CornerRadius(12);
        return card;
    }

    private Border SummaryCard(string title, bool? on, string text, FrameworkElement editor, string action, Action? after) =>
        SummaryCard(title, on, text, action, () => ShowEditor(title, editor, after));

    /// <summary>Botão "?" com a explicação longa (no lugar dos parágrafos que ocupavam a tela).</summary>
    private Button Help(string text)
    {
        var button = new Button
        {
            Content = "?", Width = 24, Height = 24, Padding = new Thickness(0), FontSize = 12, FontWeight = FontWeights.Bold,
            Style = (Style)FindResource("GhostButton"), ToolTip = new ToolTip { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 } },
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 4),
        };
        button.Width = 24;
        button.Height = 24;
        button.Padding = new Thickness(0);
        ToolTipService.SetShowDuration(button, 60000);
        button.Click += (_, _) => MessageBox.Show(text, "Ajuda", MessageBoxButton.OK, MessageBoxImage.Information);
        return button;
    }

    /// <summary>Quadradinho 2x2 para escolher o canto da tela.</summary>
    private Grid CornerPicker(int corner, Action<int> picked)
    {
        var grid = new Grid { Width = 52, Height = 36, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Canto da tela" };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        var cells = new List<Border>();
        void Paint(int selected)
        {
            for (int i = 0; i < cells.Count; i++) cells[i].Background = i == selected ? Yellow : Brush("BorderBrush");
        }

        for (int i = 0; i < 4; i++)
        {
            int index = i;
            var cell = new Border { Margin = new Thickness(1.5), CornerRadius = new CornerRadius(3), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = CornerNames[i] };
            cell.MouseLeftButtonUp += (_, _) => { Paint(index); picked(index); };
            Grid.SetColumn(cell, i % 2);
            Grid.SetRow(cell, i / 2);
            grid.Children.Add(cell);
            cells.Add(cell);
        }
        Paint(Math.Clamp(corner, 0, 3));
        return grid;
    }

    // ---------------------------------------------------------------- Pix (cartão) e Extras

    private void BuildPixCard()
    {
        _tabPixCard.Children.Clear();
        string where = CornerNames[Math.Clamp(_s.StudioPixCorner, 0, 3)];
        string text = (_s.StudioPixUseOwn ? "QR do seu banco" : _s.StudioPixKey.Length > 0 ? "QR gerado pela sua chave" : "Falta preencher a chave Pix")
                      + $" · {where}" + (_s.StudioPixEveryMinutes == 0 ? ", sempre na tela." : $", a cada {_s.StudioPixEveryMinutes} min.");
        _tabPixCard.Children.Add(SummaryCard("Pix na live", _s.StudioPixOn, text, _tabPix, "Configurar", () => { BuildPix(); UpdateGhosts(); }));
        if (_tabPixCard.Children[0] is Border card) card.Width = 420;
    }

    private void BuildExtras()
    {
        _tabExtras.Children.Clear();
        var cards = new WrapPanel();

        var chat = new StackPanel();
        var auto = new CheckBox { Content = "Abrir e conectar o chat sozinho quando a live começar", IsChecked = _s.StudioChatAuto, Foreground = Brush("TextBrush") };
        auto.Click += (_, _) => _s.StudioChatAuto = auto.IsChecked == true;
        chat.Children.Add(auto);
        var chatHere = SmallButton("Abrir o chat aqui no programa", (_, _) => { Window.GetWindow(chat)?.Close(); ShowChatWindow(); }, true);
        chatHere.HorizontalAlignment = HorizontalAlignment.Left;
        chatHere.Margin = new Thickness(0, 8, 0, 0);
        chat.Children.Add(chatHere);
        var openChat = SmallButton("Abrir no navegador", (_, _) => OpenChat());
        openChat.HorizontalAlignment = HorizontalAlignment.Left;
        openChat.Margin = new Thickness(0, 6, 0, 0);
        chat.Children.Add(openChat);
        chat.Children.Add(Muted("O canal ou a live do chat ficam em Transmitir › Conexão.", 11));
        cards.Children.Add(SummaryCard("Chat ao vivo", _s.StudioChatAuto, "Mostra o chat e destaca as perguntas para você responder.", chat, "Configurar", BuildExtras));

        cards.Children.Add(SummaryCard("Legendas ao vivo", _s.StudioCaptionOn, "Escreve na tela o que você fala, em tempo real.", "Configurar", () => _selectTab?.Invoke(1)));
        cards.Children.Add(SummaryCard("Clipes automáticos", _s.StudioAutoClip, "Salva sozinho os melhores lances pelo som da partida.", "Configurar", () => _selectTab?.Invoke(9)));
        cards.Children.Add(SummaryCard("Vídeos curtos (Shorts)", null, "Corta trechos de um vídeo longo em vídeos verticais.", "Abrir", () => MainWindow.Current?.Go("shorts")));
        cards.Children.Add(SummaryCard("Enviar vídeo ao YouTube", YouTubeConnected, YouTubeConnected ? "Manda uma gravação ou um clipe direto para o seu canal." : "Conecte a conta uma vez em Programa › Contas e chaves.", "Enviar", () => OpenUploadDialog()));
        _tabExtras.Children.Add(cards);
    }

    // ---------------------------------------------------------------- Programa › Contas e chaves

    private void BuildAccounts()
    {
        _tabAccounts.Children.Clear();
        foreach (var box in new FrameworkElement[] { _key, _remember, _statsKey, _twitchId, _twitchSecret }) Detach(box);

        var service = CurrentService();
        var keyHead = new WrapPanel();
        keyHead.Children.Add(new TextBlock { Text = $"Chave de transmissão · {PlatformSpecs.For(service).Name}", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 8, 4), VerticalAlignment = VerticalAlignment.Center });
        keyHead.Children.Add(Help("Twitch: painel do criador › Configurações › Transmissão. YouTube: Transmitir ao vivo › Chave. Facebook: Produtor de transmissões ao vivo. " +
            "Kick e outras: escolha \"Outro servidor\" em Transmitir › Conexão e use o endereço que a plataforma informa. A chave nunca aparece em mensagens nem em relatórios."));
        _tabAccounts.Children.Add(keyHead);
        if (!_keyLoaded)
        {
            _keyLoaded = true;
            if (SecretStore.Unprotect(_s.StudioProtectedKey) is { } saved)
            {
                _key.Password = saved;
                _remember.IsChecked = true;
            }
        }
        _tabAccounts.Children.Add(_key);
        _tabAccounts.Children.Add(_remember);
        _tabAccounts.Children.Add(Muted("Quem tem a chave transmite no seu canal: não mostre nem compartilhe.", 11));

        var statsHead = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        statsHead.Children.Add(new TextBlock { Text = "Chaves das estatísticas (grátis)", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 4), VerticalAlignment = VerticalAlignment.Center });
        statsHead.Children.Add(Help("Servem só para ler quantas pessoas assistem e as visualizações, pelas APIs oficiais. Ficam guardadas protegidas pelo Windows, só para o seu usuário, e não vão para nenhum servidor nosso."));
        _tabAccounts.Children.Add(statsHead);

        if (!_accountsLoaded)
        {
            _accountsLoaded = true;
            if (SecretStore.Unprotect(_s.StudioYouTubeKeyProtected) is { } savedKey) _statsKey.Password = savedKey;
            if (SecretStore.Unprotect(_s.StudioTwitchSecretProtected) is { } savedSecret) _twitchSecret.Password = savedSecret;
            _twitchId.Text = _s.StudioTwitchClientId;
        }

        // YouTube
        _statsKey.PasswordChanged -= StatsSecretChanged;
        _statsKey.PasswordChanged += StatsSecretChanged;
        _tabAccounts.Children.Add(Field("YouTube · chave de API", _statsKey, 170));
        var how = SmallButton("Como criar a chave (Google Cloud)", (_, _) => OpenWeb("https://console.cloud.google.com/apis/credentials"));
        how.Margin = new Thickness(0, 0, 8, 4);
        how.ToolTip = "Crie um projeto, ative a \"YouTube Data API v3\" e gere uma chave de API. O limite grátis dá para consultar a cada 15 segundos o dia inteiro.";
        _tabAccounts.Children.Add(Field("", how, 170));

        // Twitch
        _twitchId.TextChanged -= TwitchIdChanged;
        _twitchId.TextChanged += TwitchIdChanged;
        _tabAccounts.Children.Add(Field("Twitch · Client ID", _twitchId, 170));
        _twitchSecret.PasswordChanged -= StatsSecretChanged;
        _twitchSecret.PasswordChanged += StatsSecretChanged;
        _tabAccounts.Children.Add(Field("Twitch · segredo", _twitchSecret, 170));
        var twitchHow = SmallButton("Como criar o aplicativo (Twitch Developers)", (_, _) => OpenWeb("https://dev.twitch.tv/console/apps"));
        twitchHow.Margin = new Thickness(0, 0, 8, 4);
        twitchHow.ToolTip = "Registre um aplicativo (categoria \"Application Integration\"), copie o Client ID e gere um segredo. A Twitch informa espectadores, mas não as visualizações totais.";
        _tabAccounts.Children.Add(Field("", twitchHow, 170));

        AddYouTubeAccount(_tabAccounts);
    }
}
