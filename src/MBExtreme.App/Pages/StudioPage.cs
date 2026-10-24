using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MBExtreme.Core.Apps;
using MBExtreme.Core.Network;
using MBExtreme.Core.Progress;
using MBExtreme.Core.Recording;
using MBExtreme.Core.Settings;
using MBExtreme.UI.Controls;
using MBExtreme.UI.Recording;

namespace MBExtreme.UI.Pages;

/// <summary>Estúdio: grava a tela ou uma janela, transmite ao vivo e guarda os melhores momentos (replay), com som do PC e microfone.</summary>
public sealed partial class StudioPage : ToolPageBase
{
    private static readonly (string Name, int Height)[] Resolutions =
        { ("Original", 0), ("1080p", 1080), ("1440p", 1440), ("4K 2160p", 2160), ("720p", 720) };
    private static readonly int[] FpsOptions = { 30, 60 };
    private static readonly int[] BitrateOptions = { 0, 8, 12, 20, 35, 50 };
    private static readonly int[] ReplayOptions = { 30, 60, 120, 300 };
    private static readonly int[] AudioKbpsOptions = { 96, 128, 160, 192, 256, 320 };
    private static readonly int[] CapOptions = { 0, 3, 4, 5, 6, 8, 10, 15, 20 };
    private static readonly int[] CameraPercents = { 15, 20, 25, 30 };
    private static readonly string[] CornerNames = { "↖ Superior esquerdo", "↗ Superior direito", "↙ Inferior esquerdo", "↘ Inferior direito" };
    private static readonly (string Name, double Value)[] Volumes = { ("Mudo", 0), ("50%", 0.5), ("100%", 1), ("150%", 1.5) };
    private static readonly int[] AviRates = { 10, 15, 30 };
    private static readonly double[] AviScales = { 1.0, 0.75, 0.5 };

    private readonly AppSettings _s = AppServices.Settings;
    private readonly StudioController _studio = AppServices.Studio;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    private readonly Button _start;
    private readonly Button _stop;
    private readonly Button _saveReplay;
    private readonly StackPanel _config = new();
    private readonly StackPanel _tabVideo = new(), _tabAudio = new(), _tabCamera = new(), _tabStream = new(), _tabReplay = new(), _tabShortcuts = new(), _tabAdvanced = new();
    private readonly Border _tileRecord = new(), _tileStream = new(), _tileReplay = new();
    private readonly Dictionary<Border, (string Kind, List<Shape> Strokes, List<Shape> Fills)> _tileIcons = new();
    private readonly NetThroughput.Sampler _net = new();
    private (double Up, double Down) _rate;
    private readonly TextBlock _netNow = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock _planText = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock _specText = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly TextBlock _hotkeyNote = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _uploadResult = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly TextBlock _cameraNote = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11 };
    private readonly TextBlock _liveNet = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly StackPanel _liveButtons = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Grid _previewHost = new() { HorizontalAlignment = HorizontalAlignment.Left };
    private readonly List<Border> _ghosts = new();
    private readonly StackPanel _tabPix = new(), _tabStats = new(), _tabChannel = new(), _tabImage = new();
    private ChatPanel? _chatPanel;
    private readonly TextBox _chName = new() { Width = 260, HorizontalAlignment = HorizontalAlignment.Left, MaxLength = 40 };
    private readonly TextBox _chPresenter = new() { Width = 260, HorizontalAlignment = HorizontalAlignment.Left, MaxLength = 40 };
    private readonly TextBox _chTagline = new() { Width = 360, HorizontalAlignment = HorizontalAlignment.Left, MaxLength = 60 };
    private readonly System.Windows.Controls.Image _introPreview = new() { Height = 70, HorizontalAlignment = HorizontalAlignment.Left, Stretch = Stretch.Uniform };
    private readonly System.Windows.Controls.Image _lowerPreview = new() { Height = 44, HorizontalAlignment = HorizontalAlignment.Left, Stretch = Stretch.Uniform };
    private readonly System.Windows.Controls.Image _logoPreview = new() { Height = 56, HorizontalAlignment = HorizontalAlignment.Left, Stretch = Stretch.Uniform };
    private readonly System.Windows.Controls.Image _pixOwnPreview = new() { Height = 150, HorizontalAlignment = HorizontalAlignment.Left, Stretch = Stretch.Uniform };
    private static readonly string OverlayFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "overlays");
    private readonly LiveStatsService _stats = new(new HttpClient { Timeout = TimeSpan.FromSeconds(12) });
    private readonly ViewerHistory _viewers = new(120);
    private readonly LiveChart _viewerChart = new(60, Color.FromRgb(0xFF, 0xC4, 0x00)) { Height = 70, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Border _statsCard = new() { Visibility = Visibility.Collapsed };
    private readonly TextBlock _statsViewers = new() { FontSize = 26, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _statsLine = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
    private readonly TextBlock _statsTitle = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _statsError = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
    private readonly TextBlock _statsTest = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly DispatcherTimer _statsTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly PasswordBox _statsKey = new() { Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _statsId = new() { Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _twitchId = new() { Width = 300, HorizontalAlignment = HorizontalAlignment.Left, MaxLength = 64 };
    private readonly PasswordBox _twitchSecret = new() { Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
    private bool _statsBusy, _hasStats, _syncingId, _wasStreaming;
    private readonly TextBox _pixKey = new() { Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _pixName = new() { Width = 220, HorizontalAlignment = HorizontalAlignment.Left, MaxLength = 25 };
    private readonly TextBox _pixCity = new() { Width = 160, HorizontalAlignment = HorizontalAlignment.Left, MaxLength = 15 };
    private readonly TextBox _pixAmount = new() { Width = 100, HorizontalAlignment = HorizontalAlignment.Left, MaxLength = 10 };
    private readonly TextBox _pixNote = new() { Width = 260, HorizontalAlignment = HorizontalAlignment.Left, MaxLength = 40 };
    private readonly TextBlock _pixStatus = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly System.Windows.Controls.Image _pixPreview = new() { Width = 170, HorizontalAlignment = HorizontalAlignment.Left, Stretch = Stretch.Uniform };
    private string? _pixPayload;
    private readonly Border _pending = new() { Visibility = Visibility.Collapsed };
    private readonly TextBlock _pendingText = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly Button _pendingButton = new() { Content = "Aplicar agora", Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _muteMic = new() { Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(11, 4, 11, 4), FontSize = 12 };
    private readonly Button _muteSystem = new() { Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(11, 4, 11, 4), FontSize = 12 };
    private readonly TextBlock _autoClipState = new() { FontSize = 12, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
    private bool _applying, _restartWarned;
    private int _tickCount, _previewTick;
    private static readonly string PixImagePath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "pix-qr.png");
    private readonly StackPanel _displayHost = new();
    private readonly TextBox _chatId = new() { Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
    private IReadOnlyList<string> _cameraList = Array.Empty<string>();
    private bool _keyLoaded, _uploadBusy, _wasRunning;
    private int _displayIndex;
    private int _fitMbps;
    private readonly TextBlock _tileRecordState = new(), _tileStreamState = new(), _tileReplayState = new();
    private readonly System.Windows.Controls.Image _preview = new() { Stretch = Stretch.Uniform, MaxHeight = 220, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _previewNote = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool _previewBusy;
    private readonly TextBlock _health = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _engineStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _qualityEncoder = new();
    private readonly StackPanel _displayPanel = new();
    private readonly StackPanel _windowPanel = new();
    private readonly StackPanel _streamPanel = new();
    private readonly StackPanel _replayPanel = new();
    private readonly StackPanel _recordPanel = new();
    private readonly StackPanel _live = new() { Visibility = Visibility.Collapsed };
    private readonly TextBlock _liveText = new() { TextWrapping = TextWrapping.Wrap, FontSize = 14 };
    private readonly TextBlock _replayText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _warnings = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _level = new() { Minimum = 0, Maximum = 100, Height = 8, Margin = new Thickness(0, 8, 0, 0) };
    private readonly StackPanel _files = new();
    private readonly PickList _windows;
    private readonly PickList _mics;
    private readonly PickList _outputs;
    private readonly TextBox _customUrl = new() { Width = 420, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
    private readonly PasswordBox _key = new() { Width = 420, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
    private readonly CheckBox _remember;

    private IReadOnlyList<EncoderOption> _encoders = Array.Empty<EncoderOption>();
    private IReadOnlyList<DisplayInfo> _displayList = Array.Empty<DisplayInfo>();
    private IReadOnlyList<WindowInfo> _windowList = Array.Empty<WindowInfo>();
    private IReadOnlyList<AudioDevice> _micList = Array.Empty<AudioDevice>();
    private IReadOnlyList<AudioDevice> _outList = Array.Empty<AudioDevice>();
    private string? _ffmpegPath;

    private bool _outRecord = true, _outStream, _outReplay;
    private int _sourceTab;          // 0 = tela, 1 = janela
    private int _windowMode;         // 0 = recorte da tela, 1 = conteúdo da janela (GDI)
    private int _encoderIndex;
    private int _aviRate = 1, _aviScale = 1;
    private int _settingsTab;
    private int _tier = 2;

    public StudioPage() : base("Gravação e transmissão",
        "Estúdio completo: grave a tela inteira ou só a janela do jogo, transmita ao vivo (Twitch, YouTube, Facebook ou outro servidor), ponha a sua câmera no canto e guarde os melhores momentos com o replay. A captura e a codificação usam a placa de vídeo (NVIDIA, AMD ou Intel). Os vídeos ficam em Vídeos\\MB Extreme.")
    {
        _start = AddButton("Iniciar", async (_, _) => await BeginAsync());
        _stop = AddButton("Parar", async (_, _) => await EndAsync(), false);
        _saveReplay = AddButton("Salvar replay", async (_, _) => await _studio.SaveReplayAsync(), false);
        AddButton("Abrir pasta", (_, _) => OpenFolder(), false);

        _windows = new PickList(this, 130);
        _mics = new PickList(this, 110);
        _outputs = new PickList(this, 110);
        _remember = new CheckBox { Content = "Lembrar a chave neste PC (protegida pelo Windows, só este usuário lê)", Foreground = Brush("TextBrush"), Margin = new Thickness(0, 8, 0, 0) };

        Body.Children.Add(_live);
        BuildLive();
        BuildStatsCard();
        _chatPanel = new ChatPanel(() => (CurrentService(), _statsId.Text, _statsKey.Password));
        Body.Children.Add(_chatPanel);
        Body.Children.Add(_config);
        BuildSource();
        BuildTiles();
        BuildSettings();
        BuildEngine();
        BuildVideo();
        MigrateCamera();
        BuildVertical();
        BuildImage();
        BuildChannel();
        BuildAudio();
        RebuildCameras();
        BuildPix();
        BuildStream();
        BuildStatsTab();
        BuildReplaySettings();
        BuildShortcuts();
        BuildCompat();

        var filesTitle = Heading("Gravações e replays");
        filesTitle.FontSize = 13;
        Body.Children.Add(filesTitle);
        Body.Children.Add(_files);

        _studio.Notice += OnNotice;
        _timer.Tick += (_, _) => Tick();
        _statsTimer.Tick += async (_, _) =>
        {
            if (_s.StudioStatsOn && _studio.IsRunning && _studio.Config?.Stream is not null) await RefreshStatsAsync(false);
        };
        _previewTimer.Tick += async (_, _) => await UpdatePreviewAsync();
        Loaded += async (_, _) =>
        {
            _timer.Start();
            _statsTimer.Start();
            _previewTimer.Start();
            RefreshFiles();
            RefreshState();
            await InitAsync();
        };
        Unloaded += (_, _) =>
        {
            _timer.Stop();
            _statsTimer.Stop();
            _previewTimer.Stop();
            _studio.Notice -= OnNotice;
        };
    }

    // ---------------------------------------------------------------- construção da tela

    private void BuildLive()
    {
        _live.Children.Add(Heading("Ao vivo"));
        var panel = new StackPanel();
        panel.Children.Add(_liveText);
        panel.Children.Add(_level);
        panel.Children.Add(_health);
        panel.Children.Add(_liveNet);
        panel.Children.Add(_replayText);
        panel.Children.Add(_warnings);
        panel.Children.Add(_liveButtons);
        _live.Children.Add(CardOf(panel));
        _warnings.Foreground = Brush("WarnBrush");
        _replayText.Foreground = Brush("MutedBrush");
        _replayText.Margin = new Thickness(0, 6, 0, 0);
        _warnings.Margin = new Thickness(0, 6, 0, 0);
        _liveNet.Foreground = Brush("MutedBrush");
        _liveNet.FontSize = 12;
        var chat = SmallButton("Abrir o chat", (_, _) => OpenChat(), false);
        chat.Margin = new Thickness(0, 0, 8, 0);
        _liveButtons.Children.Add(chat);

        // áudio ao vivo: vale na hora, sem reiniciar
        var audio = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        _muteMic.Click += (_, _) => ToggleMute(true, _studio.GetVolume(true) > 0.001f);
        _muteSystem.Click += (_, _) => ToggleMute(false, _studio.GetVolume(false) > 0.001f);
        audio.Children.Add(_muteMic);
        audio.Children.Add(_muteSystem);
        panel.Children.Add(audio);
        _autoClipState.Margin = new Thickness(0, 6, 0, 0);

        // mudanças que exigem reiniciar a captura
        _pending.Style = (Style)FindResource("Card");
        _pending.BorderBrush = Brush("WarnBrush");
        _pending.Padding = new Thickness(12, 8, 12, 8);
        _pending.Margin = new Thickness(0, 8, 0, 0);
        _pendingText.Foreground = Brush("WarnBrush");
        _pendingButton.Style = (Style)FindResource("PrimaryButton");
        _pendingButton.Click += async (_, _) => await ApplyAsync();
        var pendingRow = new Grid();
        pendingRow.ColumnDefinitions.Add(new ColumnDefinition());
        pendingRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pendingRow.Children.Add(_pendingText);
        Grid.SetColumn(_pendingButton, 1);
        pendingRow.Children.Add(_pendingButton);
        _pending.Child = pendingRow;
        panel.Children.Add(_pending);
    }

    /// <summary>Botões de escolha única, pequenos e que quebram de linha (o estúdio tem muitas opções).</summary>
    private WrapPanel Chips(IReadOnlyList<string> options, int selected, Action<int> onChange)
    {
        var panel = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        var buttons = new List<Button>();
        void Paint(int index)
        {
            for (int i = 0; i < buttons.Count; i++) buttons[i].Style = (Style)FindResource(i == index ? "PrimaryButton" : "GhostButton");
            foreach (var b in buttons) { b.Padding = new Thickness(10, 3, 10, 3); b.FontSize = 12; }
        }

        for (int i = 0; i < options.Count; i++)
        {
            int index = i;
            var button = new Button { Content = options[i], Margin = new Thickness(0, 0, 5, 5) };
            button.Click += (_, _) => { Paint(index); onChange(index); };
            buttons.Add(button);
            panel.Children.Add(button);
        }
        Paint(selected);
        return panel;
    }

    /// <summary>Tira o elemento de onde ele estava (um elemento só pode ter um pai; as abas são remontadas quando uma escolha muda).</summary>
    private static void Detach(FrameworkElement element)
    {
        switch (element.Parent)
        {
            case Panel panel: panel.Children.Remove(element); break;
            case Decorator decorator: decorator.Child = null; break;
            case ContentControl control: control.Content = null; break;
        }
    }

    /// <summary>Uma linha de configuração: o nome à esquerda e as opções ao lado (gasta menos altura que o nome em cima).</summary>
    private static Grid Field(UIElement label, UIElement content, double labelWidth = 150)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        if (label is FrameworkElement fe) fe.Margin = new Thickness(0, 5, 8, 0);
        grid.Children.Add(label);
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);
        return grid;
    }

    private Grid Field(string label, UIElement content, double labelWidth = 150) => Field(Muted(label, 12), content, labelWidth);

    // ---- três blocos: Replay, Gravar, Transmitir (com ícone)

    private void BuildTiles()
    {
        var column = new StackPanel();
        column.Children.Add(MakeTile(_tileReplay, _tileReplayState, "replay", "Replay instantâneo", "Guarda os últimos segundos", () => { _outReplay = !_outReplay; }));
        column.Children.Add(MakeTile(_tileRecord, _tileRecordState, "record", "Gravar", "Salva em Vídeos\\MB Extreme", () => { _outRecord = !_outRecord; }));
        column.Children.Add(MakeTile(_tileStream, _tileStreamState, "stream", "Transmitir ao vivo", "Twitch, YouTube, Facebook ou RTMP", () => { _outStream = !_outStream; }));
        Grid.SetColumn(column, 1);
        _top.Children.Add(column);
        RefreshTiles();
    }

    /// <summary>Ícones desenhados em vetor (não dependem de fonte): replay = seta de volta com relógio, gravar = círculo de gravação, transmitir = ponto com ondas.</summary>
    private (FrameworkElement Icon, List<Shape> Strokes, List<Shape> Fills) BuildIcon(string kind)
    {
        var strokes = new List<Shape>();
        var fills = new List<Shape>();
        var canvas = new Canvas { Width = 24, Height = 24 };

        void Line(string data)
        {
            var path = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(data), StrokeThickness = 1.8, Fill = Brushes.Transparent,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            };
            strokes.Add(path);
            canvas.Children.Add(path);
        }

        void Dot(string data)
        {
            var path = new System.Windows.Shapes.Path { Data = Geometry.Parse(data) };
            fills.Add(path);
            canvas.Children.Add(path);
        }

        switch (kind)
        {
            case "replay":
                Line("M3.6,12 A8.4,8.4 0 1 0 6.1,6");
                Line("M3.2,3.4 L3.2,8.2 L8,8.2");
                Line("M12,7.6 L12,12 L15.2,13.8");
                break;
            case "record":
                Line("M12,3 A9,9 0 1 1 11.99,3 Z");
                Dot("M12,7.5 A4.5,4.5 0 1 1 11.99,7.5 Z");
                break;
            default:
                Dot("M12,10 A2,2 0 1 1 11.99,10 Z");
                Line("M8.5,8.5 A5,5 0 0 0 8.5,15.5");
                Line("M15.5,8.5 A5,5 0 0 1 15.5,15.5");
                Line("M5.6,5.6 A9,9 0 0 0 5.6,18.4");
                Line("M18.4,5.6 A9,9 0 0 1 18.4,18.4");
                break;
        }

        return (new Viewbox { Width = 30, Height = 30, Child = canvas, VerticalAlignment = VerticalAlignment.Center }, strokes, fills);
    }

    private Border MakeTile(Border tile, TextBlock state, string kind, string title, string hint, Action toggle)
    {
        tile.Style = (Style)FindResource("Card");
        tile.Margin = new Thickness(0, 0, 0, 6);
        tile.Padding = new Thickness(12, 7, 12, 7);
        tile.Cursor = System.Windows.Input.Cursors.Hand;

        var (icon, strokes, fills) = BuildIcon(kind);
        _tileIcons[tile] = (kind, strokes, fills);
        icon.Margin = new Thickness(0, 0, 12, 0);

        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold });
        text.Children.Add(Muted(hint, 11));
        state.FontSize = 12;
        state.FontWeight = FontWeights.SemiBold;
        state.Margin = new Thickness(0, 2, 0, 0);
        text.Children.Add(state);

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(icon);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        tile.Child = row;
        tile.MouseLeftButtonUp += (_, _) =>
        {
            if (AppServices.Recorder.IsRecording) return;
            toggle();
            RefreshTiles();
            UpdatePlan();
            UpdateAutoClipText();
        };
        return tile;
    }

    private void RefreshTiles()
    {
        void Paint(Border tile, TextBlock state, bool on)
        {
            state.Text = on ? "● Ligado" : "Desligado";
            state.Foreground = Brush(on ? "OkBrush" : "MutedBrush");
            tile.BorderBrush = Brush(on ? "AccentBrush" : "BorderBrush");
            tile.BorderThickness = new Thickness(on ? 2 : 1);
            if (!_tileIcons.TryGetValue(tile, out var icon)) return;
            foreach (var shape in icon.Strokes) shape.Stroke = Brush(on ? "AccentBrush" : "MutedBrush");
            foreach (var shape in icon.Fills) shape.Fill = Brush(on ? (icon.Kind == "record" ? "BadBrush" : "AccentBrush") : "MutedBrush");
        }

        Paint(_tileRecord, _tileRecordState, _outRecord);
        Paint(_tileStream, _tileStreamState, _outStream);
        Paint(_tileReplay, _tileReplayState, _outReplay);
    }

    // ---- origem com pré-visualização

    private Grid _top = new();

    private void BuildSource()
    {
        // pré-visualização à esquerda (com a escolha do que capturar logo abaixo) e os três blocos à direita
        _top = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        _top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
        _top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 250 });

        _preview.MaxHeight = 230;
        _previewNote.Foreground = Brush("MutedBrush");
        _previewNote.FontSize = 11;

        _previewHost.Children.Add(_preview);
        _preview.SizeChanged += (_, _) => UpdateGhosts();

        var box = new StackPanel();
        box.Children.Add(_previewHost);
        box.Children.Add(_previewNote);

        // o que capturar: botões pequenos embaixo da imagem
        var capture = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        capture.Children.Add(new TextBlock { Text = "Capturar:", FontWeight = FontWeights.SemiBold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 5) });
        capture.Children.Add(Chips(new[] { "Tela inteira", "Janela de um programa" }, _sourceTab, i =>
        {
            _sourceTab = i;
            _displayPanel.Visibility = Vis(i == 0);
            _windowPanel.Visibility = Vis(i == 1);
            if (i == 1) RefreshWindows();
            UpdatePlan();
        }));
        box.Children.Add(capture);

        _displayPanel.Children.Add(_displayHost);
        _displayPanel.Visibility = Vis(_sourceTab == 0);
        box.Children.Add(_displayPanel);

        _windowPanel.Visibility = Vis(_sourceTab == 1);
        _windowPanel.Children.Add(_windows);
        var windowRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        var refresh = SmallButton("Atualizar lista", (_, _) => RefreshWindows());
        refresh.Margin = new Thickness(0, 0, 10, 4);
        refresh.Padding = new Thickness(10, 3, 10, 3);
        refresh.FontSize = 12;
        windowRow.Children.Add(refresh);
        windowRow.Children.Add(new TextBlock { Text = "Modo:", FontSize = 12, Foreground = Brush("MutedBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 4) });
        windowRow.Children.Add(Chips(new[] { "Recorte da tela (jogos)", "Conteúdo da janela" }, _windowMode, i => _windowMode = i));
        _windowPanel.Children.Add(windowRow);
        box.Children.Add(_windowPanel);

        var previewCard = CardOf(box);
        previewCard.Padding = new Thickness(10, 8, 10, 6);
        previewCard.Margin = new Thickness(0, 0, 10, 0);
        _top.Children.Add(previewCard);

        _config.Children.Add(_top);
        RefreshDisplays();
    }

    /// <summary>Retângulos sobre a pré-visualização que mostram onde cada câmera e o QR do Pix vão aparecer.</summary>
    private void UpdateGhosts()
    {
        var items = new List<(string Label, int Corner, double Percent, double Aspect)>();
        for (int i = 0; i < _s.StudioCameras.Count; i++)
            if (_s.StudioCameras[i].On) items.Add(($"Câmera {i + 1}", _s.StudioCameras[i].Corner, _s.StudioCameras[i].Percent, 9.0 / 16));
        if (_s.StudioPixOn) items.Add(("Pix", _s.StudioPixCorner, _s.StudioPixPercent, 1.25));

        bool visible = _preview.Source is not null && _preview.ActualWidth > 40;
        while (_ghosts.Count > items.Count)
        {
            _previewHost.Children.Remove(_ghosts[^1]);
            _ghosts.RemoveAt(_ghosts.Count - 1);
        }
        while (_ghosts.Count < items.Count)
        {
            var ghost = new Border
            {
                IsHitTestVisible = false, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(2),
                Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
                Child = new TextBlock { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            _ghosts.Add(ghost);
            _previewHost.Children.Add(ghost);
        }

        for (int i = 0; i < items.Count; i++)
        {
            var (label, corner, percent, aspect) = items[i];
            corner = Math.Clamp(corner, 0, 3);
            bool overlaps = items.Where((o, j) => j != i && Math.Clamp(o.Corner, 0, 3) == corner).Any();
            var ghost = _ghosts[i];
            ghost.Visibility = Vis(visible);
            ghost.BorderBrush = Brush(overlaps ? "WarnBrush" : "AccentBrush");
            ghost.ToolTip = overlaps ? "Está no mesmo canto de outra camada: uma vai cobrir a outra." : null;
            if (ghost.Child is TextBlock text)
            {
                text.Text = label;
                text.Foreground = Brush(overlaps ? "WarnBrush" : "AccentBrush");
            }

            double width = _preview.ActualWidth * Math.Clamp(percent, 8, 40) / 100.0;
            ghost.Width = Math.Max(24, width);
            ghost.Height = Math.Max(14, width * aspect);
            ghost.Margin = new Thickness(Math.Max(4, _preview.ActualWidth / 80));
            ghost.HorizontalAlignment = corner is 0 or 2 ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            ghost.VerticalAlignment = corner is 0 or 1 ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        }
    }

    private PixelRect CurrentArea()
    {
        if (_sourceTab == 0)
        {
            var displays = _displayList;
            return displays.Count == 0 ? new PixelRect(0, 0, ScreenInfo.Primary.Width, ScreenInfo.Primary.Height)
                                       : displays[Math.Clamp(_displayIndex, 0, displays.Count - 1)].Bounds;
        }

        return _windows.Selected >= 0 && _windows.Selected < _windowList.Count
            ? _windowList[_windows.Selected].Bounds
            : new PixelRect(0, 0, ScreenInfo.Primary.Width, ScreenInfo.Primary.Height);
    }

    private async Task UpdatePreviewAsync()
    {
        if (_previewBusy || AppServices.Recorder.IsRecording) return;
        // com o estúdio ligado a imagem atualiza só uma vez por segundo, para pesar pouco no jogo
        if (_studio.IsRunning && ++_previewTick % 4 != 0) return;
        _previewBusy = true;
        try
        {
            PixelRect area;
            IntPtr? gdi = null;
            if (_sourceTab == 0)
            {
                area = CurrentArea();
            }
            else
            {
                if (_windows.Selected < 0 || _windows.Selected >= _windowList.Count)
                {
                    _preview.Source = null;
                    _previewNote.Text = "Escolha uma janela para ver a pré-visualização.";
                    UpdateGhosts();
                    return;
                }
                var w = _windowList[_windows.Selected];
                area = w.Bounds;
                if (_windowMode == 1) gdi = w.Handle;
            }

            var frame = await Task.Run(() => MBExtreme.UI.Recording.CapturePreview.Grab(area, gdi));
            if (frame is null)
            {
                _preview.Source = null;
                _previewNote.Text = "Não foi possível mostrar a pré-visualização desta origem.";
                UpdateGhosts();
                return;
            }

            _preview.Source = frame.Image;
            _previewNote.Text = frame.MostlyBlack
                ? "A imagem saiu preta. Em jogos, use \"Recorte da tela\"; em programas comuns, \"Conteúdo da janela\"."
                : "É assim que o vídeo vai sair (imagem reduzida, atualizada várias vezes por segundo).";
            _previewNote.Foreground = Brush(frame.MostlyBlack ? "WarnBrush" : "MutedBrush");
            UpdateGhosts();
        }
        finally
        {
            _previewBusy = false;
        }
    }

    // ---- configurações em abas

    private void BuildSettings()
    {
        var title = new TextBlock { Text = "Configurações", FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 2) };
        _config.Children.Add(title);
        var tabs = new[] { _tabVideo, _tabVertical, _tabImage, _tabAudio, _tabCamera, _tabChannel, _tabPix, _tabStream, _tabStats, _tabReplay, _tabShortcuts, _tabAdvanced };
        _config.Children.Add(Chips(new[] { "Vídeo", "Vertical", "Imagem", "Áudio", "Câmeras", "Canal", "Pix", "Transmissão", "Estatísticas", "Replay", "Atalhos", "Avançado" }, _settingsTab, i =>
        {
            _settingsTab = i;
            for (int t = 0; t < tabs.Length; t++) tabs[t].Visibility = Vis(t == i);
            if (tabs[i] == _tabStream) UpdatePlan();
        }));
        for (int t = 0; t < tabs.Length; t++)
        {
            tabs[t].Visibility = Vis(t == _settingsTab);
            _config.Children.Add(tabs[t]);
        }
    }

    private void BuildEngine()
    {
        _tabAdvanced.Children.Add(Heading("Programa de gravação"));
        _tabAdvanced.Children.Add(CardOf(_engineStatus));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(SmallButton("Instalar FFmpeg", async (_, _) => await InstallFfmpegAsync(), true));
        buttons.Children.Add(SmallButton("Detectar codificadores da minha placa", async (_, _) => await DetectAsync()));
        _tabAdvanced.Children.Add(buttons);
    }

    private void BuildVideo()
    {
        Detach(_qualityEncoder);
        _tabVideo.Children.Clear();
        var suggested = StudioPresets.ForTier(_tier);
        var names = StudioPresets.All.Select(p => p.Name + (p == suggested ? " (sugerida)" : "")).ToList();
        int current = StudioPresets.All.ToList().FindIndex(p =>
            Resolutions[Math.Clamp(_s.StudioResolution, 0, Resolutions.Length - 1)].Height == p.ResolutionHeight && _s.StudioFps == p.Fps);
        _tabVideo.Children.Add(Field("Predefinição", Chips(names, current, i =>
        {
            var preset = StudioPresets.All[i];
            _s.StudioResolution = Math.Max(0, Array.FindIndex(Resolutions, r => r.Height == preset.ResolutionHeight));
            _s.StudioFps = preset.Fps;
            _s.StudioBitrateMbps = preset.BitrateMbps;
            BuildVideo();
            UpdatePlan();
        })));
        if (current >= 0) _tabVideo.Children.Add(Field("", Muted(StudioPresets.All[current].Description, 11)));

        _tabVideo.Children.Add(Field("Resolução", Chips(Resolutions.Select(r => r.Name).ToList(), Math.Clamp(_s.StudioResolution, 0, Resolutions.Length - 1), i => { _s.StudioResolution = i; UpdatePlan(); })));
        _tabVideo.Children.Add(Field("Quadros por segundo", Chips(FpsOptions.Select(f => f + " fps").ToList(), Math.Max(0, Array.IndexOf(FpsOptions, _s.StudioFps)), i => { _s.StudioFps = FpsOptions[i]; UpdatePlan(); })));
        _tabVideo.Children.Add(Field("Taxa de vídeo", Chips(BitrateOptions.Select(b => b == 0 ? "Auto" : b + " Mbps").ToList(),
            Math.Max(0, Array.IndexOf(BitrateOptions, _s.StudioBitrateMbps)), i => { _s.StudioBitrateMbps = BitrateOptions[i]; UpdatePlan(); })));
        _tabVideo.Children.Add(Field("Formato do arquivo", Chips(new[] { "MKV (mais seguro)", "MP4" }, _s.StudioMp4 ? 1 : 0, i => _s.StudioMp4 = i == 1)));
        _tabVideo.Children.Add(Field("Codificador (codec)", _qualityEncoder));
        _tabVideo.Children.Add(Field("Esforço do codificador", Chips(new[] { "Rápido (leve no jogo)", "Equilibrado", "Qualidade" }, Math.Clamp(_s.StudioEncoderQuality, 0, 2), i => _s.StudioEncoderQuality = i)));
        _tabVideo.Children.Add(Muted("Ao transmitir, a resolução, os quadros e a taxa seguem o padrão da plataforma (aba Transmissão); a gravação e o replay usam o mesmo vídeo.", 11));
        if (_encoders.Count > 0) BuildEncoderChoice();
    }

    private void BuildAudio()
    {
        var columns = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.ColumnDefinitions.Add(new ColumnDefinition());

        var system = Check("Som do PC (jogo, música, Discord...)");
        system.IsChecked = _s.StudioSystemAudio;
        system.Click += (_, _) => _s.StudioSystemAudio = system.IsChecked == true;
        var left = new StackPanel();
        left.Children.Add(system);
        left.Children.Add(Muted("Saída de som:", 11));
        left.Children.Add(_outputs);
        var leftCard = CardOf(left);
        leftCard.Margin = new Thickness(0, 0, 6, 0);
        leftCard.Padding = new Thickness(10, 8, 10, 8);
        columns.Children.Add(leftCard);

        var mic = Check("Microfone (sua voz)");
        mic.IsChecked = _s.StudioMicAudio;
        mic.Click += (_, _) => _s.StudioMicAudio = mic.IsChecked == true;
        var right = new StackPanel();
        right.Children.Add(mic);
        right.Children.Add(Muted("Microfone:", 11));
        right.Children.Add(_mics);
        var rightCard = CardOf(right);
        rightCard.Margin = new Thickness(6, 0, 0, 0);
        rightCard.Padding = new Thickness(10, 8, 10, 8);
        Grid.SetColumn(rightCard, 1);
        columns.Children.Add(rightCard);
        _tabAudio.Children.Add(columns);
        _tabAudio.Children.Add(new AudioMixerPanel(_s, _studio,
            () => _outputs.Selected > 0 && _outputs.Selected - 1 < _outList.Count ? _outList[_outputs.Selected - 1].Id : null,
            () => _mics.Selected > 0 && _mics.Selected - 1 < _micList.Count ? _micList[_mics.Selected - 1].Id : null,
            KnownGames));

        _tabAudio.Children.Add(Field("Voz do microfone", Chips(new[] { "Natural", "Reduzir ruído", "Voz de estúdio" }, Math.Clamp(_s.StudioVoiceEffect, 0, 2), i =>
        {
            _s.StudioVoiceEffect = i;
            _studio.SetVoice((VoiceEffect)i);   // vale na hora, mesmo com o estúdio no ar
        })));
        _tabAudio.Children.Add(Muted("Reduzir ruído corta ronco de ventilador e chiado e abaixa o som entre as falas; Voz de estúdio ainda realça a voz e nivela o volume. Não é a inteligência artificial do NVIDIA Broadcast: para ruído forte (teclado alto, vozes ao fundo) o resultado é mais simples. Eco da sala não é removido.", 11));
        _tabAudio.Children.Add(Field("Qualidade do áudio", Chips(AudioKbpsOptions.Select(k => k + " kbps").ToList(),
            Math.Max(0, Array.IndexOf(AudioKbpsOptions, _s.StudioAudioKbps)), i => { _s.StudioAudioKbps = AudioKbpsOptions[i]; UpdatePlan(); })));
        _tabAudio.Children.Add(Muted("160 kbps é o ideal para a maioria. Ao transmitir, a plataforma limita (Twitch até 160 kbps). Se desmarcar os dois, o vídeo sai sem áudio.", 11));
        RefreshAudioDevices();
    }

    /// <summary>Quem usava a opção de uma câmera só passa a ter uma câmera na lista nova.</summary>
    private void MigrateCamera()
    {
        if (_s.StudioCameras.Count == 0 && _s.StudioCameraName is { Length: > 0 } name)
        {
            _s.StudioCameras.Add(new StudioCameraSetting { Name = name, Corner = _s.StudioCameraCorner, Percent = _s.StudioCameraPercent, Frame = _s.StudioCameraFrame, On = _s.StudioCameraOn });
            _s.StudioCameraName = null;
            _s.StudioCameraOn = false;
            _s.Save();
        }
    }

    private void RebuildCameras()
    {
        _tabCamera.Children.Clear();
        var head = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
        var find = SmallButton("Procurar câmeras", async (_, _) => await FindCamerasAsync());
        find.Margin = new Thickness(0, 0, 8, 4);
        head.Children.Add(find);
        var add = SmallButton("+ Adicionar câmera", (_, _) => AddCamera());
        add.Margin = new Thickness(0, 0, 8, 4);
        add.IsEnabled = _s.StudioCameras.Count < StudioCommand.MaxCameras;
        head.Children.Add(add);
        head.Children.Add(Muted($"Até {StudioCommand.MaxCameras} câmeras ao mesmo tempo, cada uma num canto. Cada câmera física só pode aparecer uma vez.", 11));
        _tabCamera.Children.Add(head);

        if (_s.StudioCameras.Count == 0)
            _tabCamera.Children.Add(Muted("Nenhuma câmera adicionada. Clique em \"Procurar câmeras\" e depois em \"+ Adicionar câmera\".", 12));

        for (int i = 0; i < _s.StudioCameras.Count; i++)
        {
            int index = i;
            var cam = _s.StudioCameras[i];
            var card = new StackPanel();

            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition());
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var on = new CheckBox { Content = $"Câmera {i + 1} ligada", IsChecked = cam.On, FontWeight = FontWeights.SemiBold, Foreground = Brush("TextBrush") };
            on.Click += (_, _) => { cam.On = on.IsChecked == true; UpdateGhosts(); };
            top.Children.Add(on);
            var remove = SmallButton("Remover", (_, _) => { _s.StudioCameras.RemoveAt(index); RebuildCameras(); UpdateGhosts(); });
            remove.Padding = new Thickness(10, 3, 10, 3);
            remove.FontSize = 11.5;
            Grid.SetColumn(remove, 1);
            top.Children.Add(remove);
            card.Children.Add(top);

            card.Children.Add(Field("Dispositivo", CameraChoice(cam), 110));
            card.Children.Add(Field("Posição", Chips(CornerNames, Math.Clamp(cam.Corner, 0, 3), c => { cam.Corner = c; UpdateGhosts(); }), 110));
            card.Children.Add(Field("Tamanho", Chips(CameraPercents.Select(p => p + "%").ToList(), Math.Max(0, Array.IndexOf(CameraPercents, cam.Percent)), c => { cam.Percent = CameraPercents[c]; UpdateGhosts(); }), 110));
            var frame = new CheckBox { Content = "Moldura amarela em volta", IsChecked = cam.Frame, Foreground = Brush("TextBrush") };
            frame.Click += (_, _) => cam.Frame = frame.IsChecked == true;
            card.Children.Add(Field("Moldura", frame, 110));
            card.Children.Add(Field("Qualidade da imagem", Chips(ImageFilter.Presets.Select(p => p.Name).ToList(), Math.Clamp(cam.Preset, 0, ImageFilter.Presets.Count - 1), c => cam.Preset = c), 110));
            var mirror = new CheckBox { Content = "Espelhar (como um espelho)", IsChecked = cam.Mirror, Foreground = Brush("TextBrush") };
            mirror.Click += (_, _) => cam.Mirror = mirror.IsChecked == true;
            card.Children.Add(Field("Espelho", mirror, 110));

            var border = CardOf(card);
            border.Padding = new Thickness(12, 8, 12, 6);
            _tabCamera.Children.Add(border);
        }

        _cameraNote.Foreground = Brush("MutedBrush");
        (_cameraNote.Parent as Panel)?.Children.Remove(_cameraNote);
        _cameraNote.Text = "Os retângulos \"Câmera 1, 2...\" na pré-visualização mostram onde cada uma vai ficar (amarelo-escuro = dois no mesmo canto). A câmera precisa estar livre: feche outros programas que a usam. Câmeras virtuais (OBS, celular) aparecem na lista como qualquer outra. Dá para ligar e desligar câmeras com a transmissão no ar (a captura reinicia por alguns segundos).";
        _tabCamera.Children.Add(_cameraNote);
    }

    private UIElement CameraChoice(StudioCameraSetting cam)
    {
        if (_cameraList.Count == 0)
            return Muted(_ffmpegPath is null ? "Instale o FFmpeg (aba Avançado) para procurar câmeras." : cam.Name is { Length: > 0 } ? cam.Name + " (clique em Procurar câmeras)" : "Clique em \"Procurar câmeras\".", 12);
        var names = _cameraList.ToList();
        if (cam.Name is { Length: > 0 } saved && !names.Contains(saved)) names.Insert(0, saved + " (não encontrada)");
        int selected = cam.Name is null ? -1 : Math.Max(0, names.FindIndex(n => n == cam.Name || n == cam.Name + " (não encontrada)"));
        if (selected < 0) { cam.Name = names[0]; selected = 0; }
        return Chips(names, selected, i => cam.Name = names[i].Replace(" (não encontrada)", ""));
    }

    private void AddCamera()
    {
        if (_s.StudioCameras.Count >= StudioCommand.MaxCameras) return;
        string? name = _cameraList.FirstOrDefault(n => _s.StudioCameras.All(c => c.Name != n));
        int[] corners = { 3, 1, 2, 0 };
        _s.StudioCameras.Add(new StudioCameraSetting { Name = name, Corner = corners[_s.StudioCameras.Count % corners.Length], Percent = _s.StudioCameras.Count == 0 ? 22 : 15 });
        RebuildCameras();
        UpdateGhosts();
        if (name is null) SetStatus("Clique em \"Procurar câmeras\" para listar as câmeras do PC.", "WarnBrush");
    }

    private async Task FindCamerasAsync()
    {
        _ffmpegPath ??= await Task.Run(FfmpegRecorder.Locate);
        if (_ffmpegPath is null)
        {
            SetStatus("Instale o FFmpeg primeiro (aba Avançado).", "WarnBrush");
            return;
        }

        string path = _ffmpegPath;
        SetStatus("Procurando câmeras...");
        _cameraList = await Task.Run(() => FfmpegRecorder.ListCameras(path));
        RebuildCameras();
        SetStatus(_cameraList.Count == 0 ? "Nenhuma câmera encontrada. Conecte uma e tente de novo." : $"{_cameraList.Count} câmera(s) encontrada(s).", _cameraList.Count == 0 ? "WarnBrush" : "OkBrush");
    }

    // ---------------------------------------------------------------- estatísticas da transmissão

    private void BuildStatsCard()
    {
        var panel = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new TextBlock { Text = "Estatísticas da transmissão", FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var refresh = SmallButton("Atualizar", async (_, _) => await RefreshStatsAsync(true));
        refresh.Padding = new Thickness(10, 3, 10, 3);
        refresh.FontSize = 11.5;
        var open = SmallButton("Abrir o painel da plataforma", (_, _) => OpenManagePage());
        open.Padding = new Thickness(10, 3, 10, 3);
        open.FontSize = 11.5;
        buttons.Children.Add(refresh);
        buttons.Children.Add(open);
        Grid.SetColumn(buttons, 1);
        head.Children.Add(buttons);
        panel.Children.Add(head);

        panel.Children.Add(_statsViewers);
        _statsLine.Foreground = Brush("MutedBrush");
        _statsTitle.Foreground = Brush("TextBrush");
        _statsError.Foreground = Brush("WarnBrush");
        _viewerChart.FormatMax = v => $"{v:0}";
        panel.Children.Add(_statsLine);
        panel.Children.Add(_viewerChart);
        panel.Children.Add(_statsTitle);
        panel.Children.Add(_statsError);

        _statsCard.Style = (Style)FindResource("Card");
        _statsCard.Padding = new Thickness(14, 10, 14, 10);
        _statsCard.Margin = new Thickness(0, 8, 0, 0);
        _statsCard.Child = panel;
        Body.Children.Add(_statsCard);
    }

    private void BuildStatsTab()
    {
        _tabStats.Children.Clear();
        foreach (var box in new FrameworkElement[] { _statsKey, _statsId, _twitchId, _twitchSecret, _statsTest }) Detach(box);

        var on = Check("Mostrar as estatísticas da transmissão", "Espectadores agora, visualizações, curtidas, comentários, pico e média, com um gráfico, na tela do estúdio.");
        on.IsChecked = _s.StudioStatsOn;
        on.Click += (_, _) => { _s.StudioStatsOn = on.IsChecked == true; UpdateStatsVisibility(); };
        _tabStats.Children.Add(on);
        _tabStats.Children.Add(Muted("Os números vêm das APIs oficiais das plataformas, com uma chave do SEU canal (grátis). Ficam sem login e sem enviar nada seu para nós.", 11));

        _syncingId = true;
        _statsId.Text = _s.StudioChatId;
        _syncingId = false;
        _statsId.TextChanged -= StatsIdChanged;
        _statsId.TextChanged += StatsIdChanged;
        _tabStats.Children.Add(Field("Canal ou live", _statsId, 140));
        _tabStats.Children.Add(Muted("YouTube: código ou endereço da live (aparece depois de v= ou no painel de transmissão). Twitch: nome do seu canal. É o mesmo campo do chat, na aba Transmissão.", 11));

        // YouTube
        _tabStats.Children.Add(Heading("YouTube"));
        if (SecretStore.Unprotect(_s.StudioYouTubeKeyProtected) is { } savedKey) _statsKey.Password = savedKey;
        _statsKey.PasswordChanged -= StatsSecretChanged;
        _statsKey.PasswordChanged += StatsSecretChanged;
        _tabStats.Children.Add(Field("Chave de API", _statsKey, 140));
        var how = SmallButton("Como criar a chave (Google Cloud)", (_, _) => OpenWeb("https://console.cloud.google.com/apis/credentials"));
        how.Margin = new Thickness(0, 0, 0, 4);
        _tabStats.Children.Add(Field("", how, 140));
        _tabStats.Children.Add(Muted("Crie um projeto, ative a \"YouTube Data API v3\" e gere uma chave de API. O limite grátis (10.000 unidades por dia) dá para consultar a cada 15 segundos o dia inteiro. A chave fica guardada protegida pelo Windows, só para o seu usuário.", 11));

        // Twitch
        _tabStats.Children.Add(Heading("Twitch"));
        _twitchId.Text = _s.StudioTwitchClientId;
        _twitchId.TextChanged -= TwitchIdChanged;
        _twitchId.TextChanged += TwitchIdChanged;
        _tabStats.Children.Add(Field("Client ID", _twitchId, 140));
        if (SecretStore.Unprotect(_s.StudioTwitchSecretProtected) is { } savedSecret) _twitchSecret.Password = savedSecret;
        _twitchSecret.PasswordChanged -= StatsSecretChanged;
        _twitchSecret.PasswordChanged += StatsSecretChanged;
        _tabStats.Children.Add(Field("Segredo (Client Secret)", _twitchSecret, 140));
        var twitchHow = SmallButton("Como criar o aplicativo (Twitch Developers)", (_, _) => OpenWeb("https://dev.twitch.tv/console/apps"));
        twitchHow.Margin = new Thickness(0, 0, 0, 4);
        _tabStats.Children.Add(Field("", twitchHow, 140));
        _tabStats.Children.Add(Muted("Registre um aplicativo (categoria \"Application Integration\"), copie o Client ID e gere um segredo. A Twitch informa espectadores, título e jogo; não informa visualizações totais por esta consulta.", 11));

        var test = SmallButton("Testar agora", async (_, _) => await RefreshStatsAsync(true));
        test.Margin = new Thickness(0, 10, 0, 0);
        test.HorizontalAlignment = HorizontalAlignment.Left;
        _tabStats.Children.Add(test);
        _statsTest.Foreground = Brush("MutedBrush");
        _tabStats.Children.Add(_statsTest);

        _tabStats.Children.Add(Heading("Configurações da transmissão"));
        _tabStats.Children.Add(Muted("Título, categoria, privacidade, latência e atraso da live são mudados no painel da própria plataforma: o botão \"Abrir o painel da plataforma\" leva direto para ele. O MB Extreme mostra aqui o título e a categoria atuais. Mudá-los de dentro do programa exigiria entrar na sua conta do Google ou da Twitch com um aplicativo aprovado por eles, o que ainda não existe.", 11));
        var manage = SmallButton("Abrir o painel da plataforma", (_, _) => OpenManagePage());
        manage.HorizontalAlignment = HorizontalAlignment.Left;
        manage.Margin = new Thickness(0, 4, 0, 0);
        _tabStats.Children.Add(manage);
        _tabStats.Children.Add(Muted("Facebook e outros servidores: a plataforma só libera estatísticas para aplicativos aprovados por ela; abra o painel para ver.", 11));
    }

    private void StatsIdChanged(object? sender, TextChangedEventArgs e)
    {
        if (_syncingId) return;
        _syncingId = true;
        _s.StudioChatId = _statsId.Text.Trim();
        _chatId.Text = _statsId.Text;
        _syncingId = false;
    }

    private void TwitchIdChanged(object? sender, TextChangedEventArgs e) => _s.StudioTwitchClientId = _twitchId.Text.Trim();

    private void StatsSecretChanged(object sender, RoutedEventArgs e)
    {
        try
        {
            _s.StudioYouTubeKeyProtected = _statsKey.Password.Length > 0 ? SecretStore.Protect(_statsKey.Password) : null;
            _s.StudioTwitchSecretProtected = _twitchSecret.Password.Length > 0 ? SecretStore.Protect(_twitchSecret.Password) : null;
        }
        catch (CryptographicException)
        {
            SetStatus("Não foi possível guardar a chave com segurança; ela vale só até fechar o programa.", "WarnBrush");
        }
    }

    private void UpdateStatsVisibility()
    {
        bool streaming = _studio.IsRunning && _studio.Config?.Stream is not null;
        _statsCard.Visibility = Vis(_s.StudioStatsOn && (streaming || _hasStats));
    }

    private async Task RefreshStatsAsync(bool manual)
    {
        if (_statsBusy) return;
        var service = CurrentService();
        if (service is StreamService.Facebook or StreamService.Custom)
        {
            if (manual) ShowStatsError("Esta plataforma não libera estatísticas por aqui. Use \"Abrir o painel da plataforma\".");
            return;
        }

        _statsBusy = true;
        try
        {
            var result = service == StreamService.YouTube
                ? await _stats.QueryYouTubeAsync(_statsId.Text, _statsKey.Password, CancellationToken.None)
                : await _stats.QueryTwitchAsync(_statsId.Text, _twitchId.Text, _twitchSecret.Password, CancellationToken.None);
            if (result.Stats is { } stats) ShowStats(stats);
            else ShowStatsError(result.Error ?? "Não foi possível consultar.");
        }
        finally
        {
            _statsBusy = false;
        }
    }

    private static string Count(long? value) => value is { } v ? v.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")) : "—";

    private void ShowStats(LiveStats stats)
    {
        _hasStats = true;
        _statsError.Visibility = Visibility.Collapsed;
        if (stats.IsLive && stats.Viewers is { } now)
        {
            _viewers.Add(now);
            _viewerChart.Update(_viewers.Values.ToList());
            _statsViewers.Text = $"● {Count(now)} espectador{(now == 1 ? "" : "es")} agora";
            _statsViewers.FontSize = 26;
            _statsViewers.Foreground = Brush("OkBrush");
        }
        else
        {
            _statsViewers.Text = stats.IsLive ? "● No ar (a plataforma ainda não informou espectadores)" : "Fora do ar na plataforma";
            _statsViewers.FontSize = 16;
            _statsViewers.Foreground = Brush(stats.IsLive ? "OkBrush" : "MutedBrush");
        }

        var parts = new List<string>();
        if (stats.TotalViews is not null) parts.Add($"{Count(stats.TotalViews)} visualizações");
        if (stats.Likes is not null) parts.Add($"{Count(stats.Likes)} curtidas");
        if (stats.Comments is not null) parts.Add($"{Count(stats.Comments)} comentários");
        if (_viewers.Values.Count > 0) parts.Add($"pico {Count(_viewers.Peak)} · média {_viewers.Average:0}");
        if (stats.StartedAt is { } started && stats.IsLive) parts.Add($"no ar há {DateTime.Now - started:hh\\:mm\\:ss}");
        _statsLine.Text = string.Join("  ·  ", parts);

        string title = stats.Title.Length == 0 ? "" : stats.Title + (stats.Category.Length > 0 ? " · " + stats.Category : "");
        _statsTitle.Text = title;
        _statsTitle.Visibility = Vis(title.Length > 0);
        _statsTest.Text = "✓ " + stats.Platform + " respondeu" + (stats.IsLive ? " (ao vivo)." : " (canal fora do ar).");
        _statsTest.Foreground = Brush("OkBrush");
        UpdateStatsVisibility();
    }

    private void ShowStatsError(string text)
    {
        _statsError.Text = "⚠ " + text;
        _statsError.Visibility = Vis(_hasStats || _studio.IsRunning);
        _statsTest.Text = "⚠ " + text;
        _statsTest.Foreground = Brush("WarnBrush");
        if (!_hasStats && _studio.IsRunning && _studio.Config?.Stream is not null)
        {
            _statsViewers.Text = "Estatísticas indisponíveis";
            _statsViewers.Foreground = Brush("MutedBrush");
            _statsLine.Text = "";
            _statsTitle.Visibility = Visibility.Collapsed;
            _statsCard.Visibility = Vis(_s.StudioStatsOn);
            _statsError.Visibility = Visibility.Visible;
        }
    }

    private void OpenManagePage()
    {
        string? url = LiveStatsService.ManageUrl(CurrentService(), _statsId.Text);
        if (url is null) { SetStatus("Este servidor não tem painel conhecido. Abra o site da plataforma onde você transmite.", "WarnBrush"); return; }
        OpenWeb(url);
    }

    private static void OpenWeb(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Win32Exception) { }
    }

    // ---------------------------------------------------------------- imagem (filtros)

    private ImageFilter CurrentFilter() => new ImageFilter(_s.StudioFilterBrightness, _s.StudioFilterContrast, _s.StudioFilterSaturation, _s.StudioFilterGamma, _s.StudioFilterSharpness, _s.StudioFilterDenoise, false, _s.StudioFilterVignette).Clamped();

    private static ImageFilter PresetFilter(int preset, bool mirror)
    {
        var f = ImageFilter.Presets[Math.Clamp(preset, 0, ImageFilter.Presets.Count - 1)].Filter;
        return f with { Mirror = mirror };
    }

    private IEnumerable<string> KnownGames()
    {
        var games = new List<string>(AppServices.Boost.Settings.GameProcesses);
        if (_studio.Config?.GameName is { Length: > 0 } running) games.Add(running);
        return games;
    }

    private void BuildImage()
    {
        _tabImage.Children.Clear();
        _tabImage.Children.Add(Muted("Ajustes de cor e nitidez da tela ou janela capturada, aplicados antes dos textos e das câmeras. Eles usam o processador (a captura passa a ser redimensionada e tratada por software); em PC fraco prefira \"Original\".", 11));

        int current = ImageFilter.Presets.ToList().FindIndex(p => Math.Abs(p.Filter.Brightness - _s.StudioFilterBrightness) < 0.001 && Math.Abs(p.Filter.Contrast - _s.StudioFilterContrast) < 0.001
            && Math.Abs(p.Filter.Saturation - _s.StudioFilterSaturation) < 0.001 && Math.Abs(p.Filter.Gamma - _s.StudioFilterGamma) < 0.001
            && Math.Abs(p.Filter.Sharpness - _s.StudioFilterSharpness) < 0.001 && p.Filter.Denoise == _s.StudioFilterDenoise && p.Filter.Vignette == _s.StudioFilterVignette);
        _tabImage.Children.Add(Field("Predefinição", Chips(ImageFilter.Presets.Select(p => p.Name).ToList(), current, i =>
        {
            var f = ImageFilter.Presets[i].Filter;
            _s.StudioFilterBrightness = f.Brightness; _s.StudioFilterContrast = f.Contrast; _s.StudioFilterSaturation = f.Saturation;
            _s.StudioFilterGamma = f.Gamma; _s.StudioFilterSharpness = f.Sharpness; _s.StudioFilterDenoise = f.Denoise; _s.StudioFilterVignette = f.Vignette;
            BuildImage();
        })));
        if (current >= 0) _tabImage.Children.Add(Field("", Muted(ImageFilter.Presets[current].Description, 11)));

        double[] bright = { -0.1, -0.05, 0, 0.05, 0.1 };
        double[] contrast = { 0.9, 1.0, 1.1, 1.2, 1.3 };
        double[] saturation = { 0.7, 1.0, 1.2, 1.4, 1.7 };
        double[] sharp = { 0, 0.4, 0.8, 1.2, 1.6 };
        _tabImage.Children.Add(Field("Brilho", Levels(bright, _s.StudioFilterBrightness, v => _s.StudioFilterBrightness = v, v => v == 0 ? "normal" : v > 0 ? $"+{v * 100:0}" : $"{v * 100:0}")));
        _tabImage.Children.Add(Field("Contraste", Levels(contrast, _s.StudioFilterContrast, v => _s.StudioFilterContrast = v, v => $"{v:0.0}")));
        _tabImage.Children.Add(Field("Cores (saturação)", Levels(saturation, _s.StudioFilterSaturation, v => _s.StudioFilterSaturation = v, v => $"{v:0.0}")));
        _tabImage.Children.Add(Field("Nitidez", Levels(sharp, _s.StudioFilterSharpness, v => _s.StudioFilterSharpness = v, v => v == 0 ? "desligada" : $"{v:0.0}")));
        _tabImage.Children.Add(Field("Reduzir chiado", Chips(new[] { "Desligado", "Leve", "Médio", "Forte" }, Math.Clamp(_s.StudioFilterDenoise, 0, 3), i => _s.StudioFilterDenoise = i)));
        _tabImage.Children.Add(Field("Vinheta (escurece os cantos)", Chips(new[] { "Desligada", "Leve", "Média", "Forte" }, Math.Clamp(_s.StudioFilterVignette, 0, 3), i => _s.StudioFilterVignette = i)));
        _tabImage.Children.Add(Muted("Para as câmeras, escolha a qualidade e o espelho na aba Câmeras (cada câmera tem o seu). Desfocar o fundo, enquadramento automático e contato visual exigem reconhecer a pessoa na imagem (inteligência artificial) e ainda não existem aqui. Quem tem placa NVIDIA pode usar o NVIDIA Broadcast: a \"câmera virtual\" dele aparece na lista de câmeras como qualquer outra.", 11));
    }

    /// <summary>Botões com alguns níveis de um ajuste; o mais próximo do valor atual fica marcado.</summary>
    private WrapPanel Levels(double[] values, double current, Action<double> set, Func<double, string> label)
    {
        int nearest = 0;
        for (int i = 1; i < values.Length; i++) if (Math.Abs(values[i] - current) < Math.Abs(values[nearest] - current)) nearest = i;
        return Chips(values.Select(label).ToList(), nearest, i => set(values[i]));
    }

    // ---------------------------------------------------------------- canal (identidade e camadas)

    private ChannelProfile Profile() => new(_s.StudioChannelName, _s.StudioPresenter, _s.StudioTagline, _s.StudioChannelLogo, _s.StudioChannelTheme);

    private static string OverlayPath(string name) => System.IO.Path.Combine(OverlayFolder, name);

    private void BuildChannel()
    {
        _tabChannel.Children.Clear();
        foreach (var box in new FrameworkElement[] { _chName, _chPresenter, _chTagline, _introPreview, _lowerPreview, _logoPreview }) Detach(box);

        _tabChannel.Children.Add(Muted("Cadastre o canal uma vez: o nome, quem apresenta, uma frase e o logo. Com isso o programa desenha a entrada, a faixa com o nome e a marca d'água que aparecem por cima do vídeo, com as cores do canal.", 11));
        _chName.Text = _s.StudioChannelName;
        _chPresenter.Text = _s.StudioPresenter;
        _chTagline.Text = _s.StudioTagline;
        foreach (var box in new[] { _chName, _chPresenter, _chTagline }) { box.TextChanged -= ChannelChanged; box.TextChanged += ChannelChanged; }
        _tabChannel.Children.Add(Field("Nome do canal", _chName, 140));
        _tabChannel.Children.Add(Field("Apresentador", _chPresenter, 140));
        _tabChannel.Children.Add(Field("Frase do canal", _chTagline, 140));

        var logoRow = new WrapPanel();
        var pick = SmallButton("Escolher o logo...", (_, _) => PickLogo());
        pick.Margin = new Thickness(0, 0, 8, 4);
        logoRow.Children.Add(pick);
        var clear = SmallButton("Tirar o logo", (_, _) => { _s.StudioChannelLogo = null; RefreshChannelPreview(); });
        clear.Margin = new Thickness(0, 0, 8, 4);
        logoRow.Children.Add(clear);
        _tabChannel.Children.Add(Field("Logo", logoRow, 140));
        _tabChannel.Children.Add(Field("Cor do canal", Chips(OverlayArt.Themes.Select(t => t.Name).ToList(), Math.Clamp(_s.StudioChannelTheme, 0, OverlayArt.Themes.Length - 1), i => { _s.StudioChannelTheme = i; RefreshChannelPreview(); }), 140));

        // ---- entrada
        _tabChannel.Children.Add(Heading("Entrada do canal"));
        var intro = new CheckBox { Content = "Mostrar o cartão de abertura quando a transmissão começar", IsChecked = _s.StudioIntroOn, Foreground = Brush("TextBrush") };
        intro.Click += (_, _) => _s.StudioIntroOn = intro.IsChecked == true;
        _tabChannel.Children.Add(intro);
        int[] introStart = { 1, 3, 5 };
        int[] introShow = { 5, 8, 12 };
        _tabChannel.Children.Add(Field("Aparece depois de", Chips(introStart.Select(v => v + " s").ToList(), Math.Max(0, Array.IndexOf(introStart, _s.StudioIntroStart)), i => _s.StudioIntroStart = introStart[i]), 140));
        _tabChannel.Children.Add(Field("Fica na tela por", Chips(introShow.Select(v => v + " s").ToList(), Math.Max(0, Array.IndexOf(introShow, _s.StudioIntroSeconds)), i => _s.StudioIntroSeconds = introShow[i]), 140));
        _tabChannel.Children.Add(_introPreview);

        // ---- faixa do apresentador
        _tabChannel.Children.Add(Heading("Faixa com o nome"));
        var lower = new CheckBox { Content = "Mostrar a faixa com o nome do apresentador e do canal", IsChecked = _s.StudioLowerOn, Foreground = Brush("TextBrush") };
        lower.Click += (_, _) => _s.StudioLowerOn = lower.IsChecked == true;
        _tabChannel.Children.Add(lower);
        int[] lowerStart = { 5, 15, 30, 60 };
        int[] lowerEvery = { 0, 5, 10, 20 };
        int[] lowerShow = { 6, 8, 12 };
        _tabChannel.Children.Add(Field("Primeira vez", Chips(lowerStart.Select(v => $"{v} s").ToList(), Math.Max(0, Array.IndexOf(lowerStart, _s.StudioLowerStart)), i => _s.StudioLowerStart = lowerStart[i]), 140));
        _tabChannel.Children.Add(Field("Repetir", Chips(lowerEvery.Select(v => v == 0 ? "Só uma vez" : $"a cada {v} min").ToList(), Math.Max(0, Array.IndexOf(lowerEvery, _s.StudioLowerEveryMinutes)), i => _s.StudioLowerEveryMinutes = lowerEvery[i]), 140));
        _tabChannel.Children.Add(Field("Fica na tela por", Chips(lowerShow.Select(v => v + " s").ToList(), Math.Max(0, Array.IndexOf(lowerShow, _s.StudioLowerSeconds)), i => _s.StudioLowerSeconds = lowerShow[i]), 140));
        _tabChannel.Children.Add(Field("Canto", Chips(CornerNames, Math.Clamp(_s.StudioLowerCorner, 0, 3), i => _s.StudioLowerCorner = i), 140));
        _tabChannel.Children.Add(_lowerPreview);

        // ---- marca d'água
        _tabChannel.Children.Add(Heading("Marca d'água (logo)"));
        var logo = new CheckBox { Content = "Deixar o logo sempre num canto do vídeo", IsChecked = _s.StudioLogoOn, Foreground = Brush("TextBrush") };
        logo.Click += (_, _) => _s.StudioLogoOn = logo.IsChecked == true;
        _tabChannel.Children.Add(logo);
        int[] logoSizes = { 5, 8, 12 };
        _tabChannel.Children.Add(Field("Canto", Chips(CornerNames, Math.Clamp(_s.StudioLogoCorner, 0, 3), i => _s.StudioLogoCorner = i), 140));
        _tabChannel.Children.Add(Field("Tamanho", Chips(logoSizes.Select(v => v + "%").ToList(), Math.Max(0, Array.IndexOf(logoSizes, _s.StudioLogoPercent)), i => _s.StudioLogoPercent = logoSizes[i]), 140));
        _tabChannel.Children.Add(_logoPreview);

        _tabChannel.Children.Add(Muted("A entrada e a faixa seguem o relógio da transmissão (por exemplo, a faixa volta sozinha de 10 em 10 minutos); mostrar na hora, por botão, ainda exige reiniciar a captura. Para ver o resultado, ligue as opções e use \"Aplicar agora\" ou inicie a transmissão. Aparecem na gravação e no replay também.", 11));
        RefreshChannelPreview();
    }

    private void ChannelChanged(object? sender, TextChangedEventArgs e)
    {
        _s.StudioChannelName = _chName.Text.Trim();
        _s.StudioPresenter = _chPresenter.Text.Trim();
        _s.StudioTagline = _chTagline.Text.Trim();
        RefreshChannelPreview();
    }

    private void RefreshChannelPreview()
    {
        try
        {
            var profile = Profile();
            _introPreview.Source = OverlayArt.RenderIntro(profile);
            _lowerPreview.Source = OverlayArt.RenderLowerThird(profile);
            _logoPreview.Source = OverlayArt.RenderLogo(profile);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException) { }
    }

    private void PickLogo()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Imagens (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp", Title = "Escolha o logo do canal" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            _s.StudioChannelLogo = OverlayArt.Import(dialog.FileName, OverlayPath("channel-logo.png"));
            RefreshChannelPreview();
            SetStatus("Logo do canal guardado.", "OkBrush");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            SetStatus("Não foi possível usar essa imagem: " + ex.Message, "BadBrush");
        }
    }

    /// <summary>Desenha as camadas ligadas e devolve a lista para o FFmpeg (a escrita em disco só ao iniciar ou aplicar).</summary>
    private List<OverlayLayer> BuildLayers(bool write)
    {
        var layers = new List<OverlayLayer>();
        var profile = Profile();
        if (_s.StudioIntroOn)
        {
            if (write) OverlayArt.SavePng(OverlayArt.RenderIntro(profile), OverlayPath("intro.png"));
            layers.Add(new OverlayLayer(OverlayKind.Intro, OverlayPath("intro.png"), CameraCorner.TopLeft, 46, _s.StudioIntroStart, 0, _s.StudioIntroSeconds));
        }
        if (_s.StudioLowerOn)
        {
            if (write) OverlayArt.SavePng(OverlayArt.RenderLowerThird(profile), OverlayPath("lower.png"));
            layers.Add(new OverlayLayer(OverlayKind.LowerThird, OverlayPath("lower.png"), (CameraCorner)Math.Clamp(_s.StudioLowerCorner, 0, 3), 38, _s.StudioLowerStart, _s.StudioLowerEveryMinutes, _s.StudioLowerSeconds));
        }
        if (_s.StudioLogoOn)
        {
            if (write) OverlayArt.SavePng(OverlayArt.RenderLogo(profile), OverlayPath("logo.png"));
            layers.Add(new OverlayLayer(OverlayKind.Logo, OverlayPath("logo.png"), (CameraCorner)Math.Clamp(_s.StudioLogoCorner, 0, 3), _s.StudioLogoPercent, 0, 0, 0));
        }
        return layers;
    }

    private string LayersKey()
    {
        long logoSize = _s.StudioChannelLogo is { } l && File.Exists(l) ? new FileInfo(l).Length : 0;
        return string.Join("|", _s.StudioChannelName, _s.StudioPresenter, _s.StudioTagline, _s.StudioChannelTheme, logoSize);
    }

    // ---------------------------------------------------------------- Pix

    private static readonly int[] PixPercents = { 10, 14, 18, 22 };
    private static readonly int[] PixEvery = { 0, 3, 5, 10 };
    private static readonly int[] PixSeconds = { 10, 20, 30 };

    private void BuildPix()
    {
        _tabPix.Children.Clear();
        foreach (var box in new FrameworkElement[] { _pixStatus, _pixPreview, _pixOwnPreview }) Detach(box);

        var on = Check("Mostrar o QR do Pix na transmissão", "O QR aparece num canto do vídeo (na transmissão, na gravação e no replay) para o público apoiar a live.");
        on.IsChecked = _s.StudioPixOn;
        on.Click += (_, _) => { _s.StudioPixOn = on.IsChecked == true; UpdateGhosts(); };
        _tabPix.Children.Add(on);

        _tabPix.Children.Add(Field("Origem do QR", Chips(new[] { "Gerar pela minha chave Pix", "Usar o QR do meu banco" }, _s.StudioPixUseOwn ? 1 : 0, i => { _s.StudioPixUseOwn = i == 1; BuildPix(); }), 130));
        if (_s.StudioPixUseOwn)
        {
            var pickOwn = SmallButton("Escolher a imagem do QR...", (_, _) => PickPixImage());
            pickOwn.Margin = new Thickness(0, 0, 8, 4);
            _tabPix.Children.Add(Field("Imagem", pickOwn, 130));
            _pixNote.Text = _s.StudioPixNote;
            Detach(_pixNote);
            _pixNote.TextChanged -= PixChanged;
            _pixNote.TextChanged += PixChanged;
            _tabPix.Children.Add(Field("Legenda", _pixNote, 130));
            _tabPix.Children.Add(Muted("Use o QR que o seu banco gera (do aplicativo, o do Pix \"Receber\" ou o de cobrança fixa). O programa só coloca uma moldura com o nome do canal e a legenda; ele não altera nem lê o QR, e quem paga confere o nome do recebedor no banco. Escolha um QR que não expira (o dinâmico deixa de valer).", 11));
            AddPixPositionFields();
            RefreshPix();
            return;
        }

        _pixKey.Text = _s.StudioPixKey;
        _pixName.Text = _s.StudioPixName;
        _pixCity.Text = _s.StudioPixCity;
        _pixAmount.Text = _s.StudioPixAmount > 0 ? _s.StudioPixAmount.ToString("0.00", System.Globalization.CultureInfo.CurrentCulture) : "";
        _pixNote.Text = _s.StudioPixNote;
        foreach (var box in new[] { _pixKey, _pixName, _pixCity, _pixAmount, _pixNote }) { Detach(box); box.TextChanged -= PixChanged; box.TextChanged += PixChanged; }

        _tabPix.Children.Add(Field("Chave Pix", _pixKey, 130));
        _tabPix.Children.Add(Field("Nome de quem recebe", _pixName, 130));
        _tabPix.Children.Add(Field("Cidade", _pixCity, 130));
        _tabPix.Children.Add(Field("Valor (R$, opcional)", _pixAmount, 130));
        _tabPix.Children.Add(Field("Mensagem", _pixNote, 130));
        AddPixPositionFields();

        var actions = new WrapPanel { Margin = new Thickness(0, 6, 0, 4) };
        var copy = SmallButton("Copiar \"Pix copia e cola\"", (_, _) =>
        {
            if (_pixPayload is null) { SetStatus("Preencha a chave, o nome e a cidade primeiro.", "WarnBrush"); return; }
            if (TryCopy(_pixPayload)) SetStatus("Código Pix copiado. Cole no chat ou na descrição da live.", "OkBrush");
        });
        copy.Margin = new Thickness(0, 0, 8, 4);
        actions.Children.Add(copy);
        var save = SmallButton("Salvar imagem do QR...", (_, _) => SavePixImage());
        save.Margin = new Thickness(0, 0, 8, 4);
        actions.Children.Add(save);
        _tabPix.Children.Add(actions);

        _pixStatus.Margin = new Thickness(0, 2, 0, 4);
        _tabPix.Children.Add(_pixStatus);
        _tabPix.Children.Add(_pixPreview);
        _tabPix.Children.Add(Muted("Este é o Pix estático: o dinheiro cai direto na sua conta, sem intermediário e sem taxa do MB Extreme. Em compensação o programa NÃO fica sabendo quando alguém paga (o banco é quem avisa), então não há alerta automático na tela com o nome e o valor do apoiador. Esse alerta, como nos serviços de \"live pix\", exige um provedor de pagamentos que confirme cada Pix e um servidor para avisar a transmissão; está descrito em docs/ESTUDIO-GRAVACAO-TRANSMISSAO.md.", 11));
        RefreshPix();
    }

    private void AddPixPositionFields()
    {
        _tabPix.Children.Add(Field("Posição", Chips(CornerNames, Math.Clamp(_s.StudioPixCorner, 0, 3), i => { _s.StudioPixCorner = i; UpdateGhosts(); }), 130));
        _tabPix.Children.Add(Field("Tamanho", Chips(PixPercents.Select(p => p + "%").ToList(), Math.Max(0, Array.IndexOf(PixPercents, _s.StudioPixPercent)), i => { _s.StudioPixPercent = PixPercents[i]; UpdateGhosts(); }), 130));
        _tabPix.Children.Add(Field("Aparecer", Chips(PixEvery.Select(m => m == 0 ? "Sempre" : $"a cada {m} min").ToList(), Math.Max(0, Array.IndexOf(PixEvery, _s.StudioPixEveryMinutes)), i => _s.StudioPixEveryMinutes = PixEvery[i]), 130));
        _tabPix.Children.Add(Field("Por quanto tempo", Chips(PixSeconds.Select(m => m + " s").ToList(), Math.Max(0, Array.IndexOf(PixSeconds, _s.StudioPixShowSeconds)), i => _s.StudioPixShowSeconds = PixSeconds[i]), 130));
    }

    private void PickPixImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Imagens (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp", Title = "Escolha a imagem do QR do seu banco" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            _s.StudioPixOwnImage = OverlayArt.Import(dialog.FileName, OverlayPath("pix-banco.png"));
            RefreshPix();
            SetStatus("QR do banco guardado.", "OkBrush");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            SetStatus("Não foi possível usar essa imagem: " + ex.Message, "BadBrush");
        }
    }

    private void PixChanged(object? sender, TextChangedEventArgs e) => RefreshPix();

    private void RefreshPix()
    {
        if (_s.StudioPixUseOwn)
        {
            _s.StudioPixNote = _pixNote.Text.Trim();
            try
            {
                if (_s.StudioPixOwnImage is { } own && File.Exists(own))
                {
                    _pixOwnPreview.Source = OverlayArt.RenderPixOwn(own, _s.StudioChannelName, _s.StudioPixNote);
                    Detach(_pixOwnPreview);
                    if (!_tabPix.Children.Contains(_pixOwnPreview)) _tabPix.Children.Add(_pixOwnPreview);
                    _pixOwnPreview.Visibility = Visibility.Visible;
                    _pixPayload = null;
                    return;
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException) { }

            _pixOwnPreview.Visibility = Visibility.Collapsed;
            return;
        }

        _s.StudioPixKey = _pixKey.Text.Trim();
        _s.StudioPixName = _pixName.Text.Trim();
        _s.StudioPixCity = _pixCity.Text.Trim();
        _s.StudioPixNote = _pixNote.Text.Trim();
        string amountText = _pixAmount.Text.Trim().Replace(',', '.');
        decimal amount = 0;
        bool amountOk = amountText.Length == 0 || decimal.TryParse(amountText, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out amount);
        _s.StudioPixAmount = amountOk ? amount : 0;

        try
        {
            if (!amountOk) throw new ArgumentException("Valor inválido. Use números, como 10,00.");
            if (_s.StudioPixKey.Length == 0) throw new ArgumentException("Digite a chave Pix para gerar o QR.");
            _pixPayload = PixPayload.Build(_s.StudioPixKey, _s.StudioPixName, _s.StudioPixCity, amount > 0 ? amount : null, _s.StudioPixNote);
            _pixPreview.Source = PixImage.Render(_pixPayload, string.IsNullOrWhiteSpace(_s.StudioPixNote) ? "Apoie via Pix" : _s.StudioPixNote, PixPayload.Clean(_s.StudioPixName, 25));
            _pixPreview.Visibility = Visibility.Visible;
            _pixStatus.Text = "✓ QR gerado" + (amount > 0 ? $" para R$ {amount:0.00}" : " (quem paga escolhe o valor)") + ". Confira o recebedor no aplicativo do banco ao testar.";
            _pixStatus.Foreground = Brush("OkBrush");
        }
        catch (ArgumentException ex)
        {
            _pixPayload = null;
            _pixPreview.Source = null;
            _pixPreview.Visibility = Visibility.Collapsed;
            _pixStatus.Text = _s.StudioPixKey.Length == 0 ? ex.Message : "⚠ " + ex.Message;
            _pixStatus.Foreground = Brush(_s.StudioPixKey.Length == 0 ? "MutedBrush" : "WarnBrush");
        }
    }

    private void SavePixImage()
    {
        if (_pixPayload is null || _pixPreview.Source is not System.Windows.Media.Imaging.BitmapSource image) { SetStatus("Preencha a chave, o nome e a cidade primeiro.", "WarnBrush"); return; }
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Imagem PNG (*.png)|*.png", FileName = "pix-qr.png", Title = "Salvar o QR do Pix" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            PixImage.SavePng(image, dialog.FileName);
            SetStatus("QR salvo em " + dialog.FileName, "OkBrush");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("Não foi possível salvar: " + ex.Message, "BadBrush");
        }
    }

    private static readonly StreamService[] ServiceOrder = { StreamService.Twitch, StreamService.YouTube, StreamService.Facebook, StreamService.Custom };

    private StreamService CurrentService() => (StreamService)Math.Clamp(_s.StudioStreamService, 0, 3);

    private void BuildStream()
    {
        foreach (var element in new FrameworkElement[] { _specText, _netNow, _planText, _uploadResult, _chatId, _key, _remember, _customUrl })
            Detach(element);
        _tabStream.Children.Clear();
        _tabStream.Children.Add(Muted("Para transmitir, ligue o bloco \"Transmitir ao vivo\" no alto da tela. A codificação é feita na placa de vídeo, por isso pesa pouco no jogo.", 11));
        var service = CurrentService();
        int selected = Math.Max(0, Array.IndexOf(ServiceOrder, service));
        _tabStream.Children.Add(Field("Plataforma", Chips(new[] { "Twitch", "YouTube", "Facebook", "Outro servidor (RTMP)" }, selected, i =>
        {
            _s.StudioStreamService = (int)ServiceOrder[i];
            // o limite de banda escolhido pode ser maior que o máximo da nova plataforma
            int max = PlatformSpecs.For(ServiceOrder[i]).MaxVideoKbps / 1000;
            if (_s.StudioStreamCapMbps > max) _s.StudioStreamCapMbps = 0;
            BuildStream();
        })));

        // padrões da plataforma
        _specText.Foreground = Brush("TextBrush");
        _tabStream.Children.Add(CardOf(_specText));

        // limite de banda
        var spec = PlatformSpecs.For(service);
        var caps = CapOptions.Where(c => c == 0 || c * 1000 <= spec.MaxVideoKbps).ToList();
        _tabStream.Children.Add(Field("Limite de banda", Chips(caps.Select(c => c == 0 ? "Padrão da plataforma" : c + " Mbps").ToList(),
            Math.Max(0, caps.IndexOf(_s.StudioStreamCapMbps)), i => { _s.StudioStreamCapMbps = caps[i]; UpdatePlan(); })));
        var measure = new WrapPanel();
        var measureButton = SmallButton("Medir meu envio de internet", async (_, _) => await MeasureUploadAsync());
        measureButton.Margin = new Thickness(0, 0, 8, 4);
        measure.Children.Add(measureButton);
        var useFit = SmallButton("Usar a taxa sugerida", (_, _) =>
        {
            if (_fitMbps <= 0) return;
            _s.StudioStreamCapMbps = Math.Min(_fitMbps, spec.MaxVideoKbps / 1000);
            BuildStream();
        });
        useFit.Margin = new Thickness(0, 0, 8, 4);
        useFit.Visibility = Vis(_fitMbps > 0);
        measure.Children.Add(useFit);
        _tabStream.Children.Add(Field("", measure));
        _uploadResult.Foreground = Brush("MutedBrush");
        _tabStream.Children.Add(Field("", _uploadResult));

        // uso da internet e plano
        _netNow.Foreground = Brush("MutedBrush");
        _netNow.FontSize = 12;
        _tabStream.Children.Add(Field("Internet agora", _netNow));
        _planText.FontSize = 12;
        _tabStream.Children.Add(Field("Vai transmitir", _planText));

        // destino e chave
        _customUrl.Text = _s.StudioCustomUrl;
        _customUrl.Visibility = Vis(service == StreamService.Custom);
        _tabStream.Children.Add(_customUrl);
        _tabStream.Children.Add(Muted("Chave de transmissão (Twitch: painel do criador > Configurações > Transmissão; YouTube: Transmitir ao vivo > Chave; Facebook: Produtor de transmissões ao vivo). Kick e outras: use \"Outro servidor\" com o endereço que a plataforma informa. Quem tem a chave transmite no seu canal: não mostre nem compartilhe.", 11));
        if (!_keyLoaded)
        {
            _keyLoaded = true;
            if (SecretStore.Unprotect(_s.StudioProtectedKey) is { } saved)
            {
                _key.Password = saved;
                _remember.IsChecked = true;
            }
        }
        _tabStream.Children.Add(_key);
        _tabStream.Children.Add(_remember);

        // chat
        var chatRow = new WrapPanel();
        _syncingId = true;
        _chatId.Text = _s.StudioChatId;
        _syncingId = false;
        _chatId.TextChanged -= ChatIdChanged;
        _chatId.TextChanged += ChatIdChanged;
        _chatId.Margin = new Thickness(0, 0, 8, 4);
        chatRow.Children.Add(_chatId);
        var openChat = SmallButton("Abrir o chat", (_, _) => OpenChat());
        openChat.Margin = new Thickness(0, 0, 0, 4);
        chatRow.Children.Add(openChat);
        _tabStream.Children.Add(Field(Muted("Chat da transmissão", 12), chatRow));
        _tabStream.Children.Add(Muted(ChatLinks.Supports(service)
            ? ChatLinks.Prompt(service) + ". O chat abre numa janela própria, que você pode deixar ao lado do jogo ou em outro monitor."
            : ChatLinks.Prompt(service) + ".", 11));
        _tabStream.Children.Add(Muted("A transmissão usa H.264, taxa constante e quadro-chave a cada 2 s. A chave nunca aparece em mensagens nem em arquivos de relatório.", 11));
        UpdatePlan();
    }

    private void ChatIdChanged(object? sender, TextChangedEventArgs e)
    {
        if (_syncingId) return;
        _syncingId = true;
        _statsId.Text = _chatId.Text;
        _s.StudioChatId = _chatId.Text.Trim();
        _syncingId = false;
    }

    /// <summary>Atualiza o cartão "padrões da plataforma" e o resumo do que será enviado, com as configurações atuais.</summary>
    private void UpdatePlan()
    {
        var service = CurrentService();
        var spec = PlatformSpecs.For(service);
        int height = CurrentVertical()?.Width ?? StudioCommand.FitOutput(CurrentArea().Width, CurrentArea().Height, Resolutions[Math.Clamp(_s.StudioResolution, 0, Resolutions.Length - 1)].Height).Height;
        var plan = StreamPlan.Build(service, height, _s.StudioFps, _s.StudioBitrateMbps, _s.StudioStreamCapMbps, _s.StudioAudioKbps);
        var range = PlatformSpecs.BitrateRange(plan.Height, plan.Fps);

        _specText.Text =
            $"Padrão da {spec.Name}: até {spec.MaxHeight}p a {spec.MaxFps} fps · taxa de vídeo até {spec.MaxVideoKbps / 1000.0:0.#} Mbps · quadro-chave a cada {spec.KeyframeSeconds} s\n" +
            $"{spec.Video} · áudio {spec.RecommendedAudioKbps} kbps (máx. {spec.MaxAudioKbps})\n" +
            $"Para {plan.Height}p a {plan.Fps} fps, o recomendado é {range.Min / 1000.0:0.#} a {range.Max / 1000.0:0.#} Mbps.\n" +
            spec.Note;

        var text = new System.Text.StringBuilder();
        text.Append($"{plan.Height}p · {plan.Fps} fps · vídeo {plan.VideoMbps:0.#} Mbps + áudio {plan.AudioKbps} kbps. ");
        text.Append($"Precisa de pelo menos {plan.RequiredUploadKbps / 1000.0:0.#} Mbps de envio livre na internet.");
        foreach (string note in plan.Notes) text.Append('\n').Append("• ").Append(note);
        _planText.Text = text.ToString();
        _planText.Foreground = Brush(plan.Notes.Count > 0 ? "WarnBrush" : "TextBrush");
    }

    private async Task MeasureUploadAsync()
    {
        if (_uploadBusy) return;
        _uploadBusy = true;
        _uploadResult.Text = "Medindo o envio por alguns segundos (usa a internet, não feche)...";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            double? mbps = await UploadTest.RunAsync(http, 6, CancellationToken.None);
            if (mbps is null)
            {
                _uploadResult.Text = "Não foi possível medir (sem internet ou o teste público não respondeu). Tente de novo mais tarde.";
                return;
            }

            _fitMbps = StreamPlan.FitToUpload(mbps.Value, _s.StudioAudioKbps);
            _uploadResult.Text = $"Seu envio: {mbps.Value:0.#} Mbps. Taxa sugerida para não travar o jogo nem a rede da casa: até {_fitMbps} Mbps (70% do envio, menos o áudio).";
            BuildStream();
        }
        finally
        {
            _uploadBusy = false;
        }
    }

    private void OpenChat()
    {
        var service = CurrentService();
        string? url = ChatLinks.Url(service, _chatId.Text);
        if (url is null)
        {
            SetStatus(ChatLinks.Supports(service) ? ChatLinks.Prompt(service) + " (confira o que digitou)." : ChatLinks.Prompt(service) + ".", "WarnBrush");
            return;
        }

        _s.StudioChatId = _chatId.Text.Trim();
        _s.Save();
        try
        {
            // janela de aplicativo do Edge (sem abas nem barra de endereço), do tamanho de um chat
            Process.Start(new ProcessStartInfo("msedge.exe") { UseShellExecute = true, Arguments = $"--app=\"{url}\" --window-size=400,640" });
        }
        catch (Win32Exception)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Win32Exception ex) { SetStatus("Não foi possível abrir o chat: " + ex.Message, "BadBrush"); }
        }
    }

    private void BuildReplaySettings()
    {
        _tabReplay.Children.Add(Field("Quanto tempo guardar", Chips(ReplayOptions.Select(r => r < 60 ? r + " s" : r / 60 + " min").ToList(),
            Math.Max(0, Array.IndexOf(ReplayOptions, _s.StudioReplaySeconds)), i => _s.StudioReplaySeconds = ReplayOptions[i])));
        var beep = new CheckBox { Content = "Tocar um aviso sonoro quando o clipe for salvo", IsChecked = _s.StudioReplayBeep, Foreground = Brush("TextBrush") };
        beep.Click += (_, _) => _s.StudioReplayBeep = beep.IsChecked == true;
        _tabReplay.Children.Add(Field("Aviso", beep));
        _tabReplay.Children.Add(Muted("O replay fica só no disco enquanto o estúdio está ligado: os pedaços são apagados quando você para. Quanto maior o tempo, mais espaço em disco.", 11));

        var auto = Check("Salvar os lances automaticamente (pelo som do jogo)", "Quando a torcida e a narração sobem de repente (gol, jogada decisiva), o clipe é salvo sozinho, sem apertar nada.");
        auto.IsChecked = _s.StudioAutoClip;
        auto.Margin = new Thickness(0, 12, 0, 4);
        auto.Click += (_, _) => { _s.StudioAutoClip = auto.IsChecked == true; UpdateAutoClipText(); };
        _tabReplay.Children.Add(auto);
        _tabReplay.Children.Add(Field("Sensibilidade", Chips(new[] { "Baixa (só lances muito fortes)", "Média", "Alta (pega mais, erra mais)" }, Math.Clamp(_s.StudioAutoClipSensitivity, 0, 2), i => { _s.StudioAutoClipSensitivity = i; UpdateAutoClipText(); })));
        int[] afters = { 3, 6, 10 };
        _tabReplay.Children.Add(Field("Salvar depois do lance", Chips(afters.Select(a => a + " s").ToList(), Math.Max(0, Array.IndexOf(afters, _s.StudioAutoClipAfterSeconds)), i => _s.StudioAutoClipAfterSeconds = afters[i])));
        var toast = new CheckBox { Content = "Mostrar um aviso por cima do jogo quando um clipe for salvo", IsChecked = _s.StudioClipToast, Foreground = Brush("TextBrush") };
        toast.Click += (_, _) => _s.StudioClipToast = toast.IsChecked == true;
        _tabReplay.Children.Add(Field("Aviso na tela", toast));
        Detach(_autoClipState);
        _autoClipState.Foreground = Brush("MutedBrush");
        _tabReplay.Children.Add(_autoClipState);
        _tabReplay.Children.Add(Muted("Como funciona: o programa compara o volume do jogo agora com o volume normal da partida. É uma estimativa pelo SOM, não uma leitura do que acontece na tela: pode salvar um momento barulhento que não era gol e deixar passar um lance silencioso. Para não perder nada, o atalho de salvar replay continua valendo. O clipe tem o tamanho do tempo guardado acima e esta opção vale na hora, mesmo com a transmissão no ar. Precisa do Replay ligado e do som do PC ativo; o seu microfone não conta.", 11));
        UpdateAutoClipText();
    }

    private void UpdateAutoClipText()
    {
        string[] levels = { "baixa", "média", "alta" };
        _autoClipState.Text = !_s.StudioAutoClip
            ? "Lances automáticos: desligados."
            : $"Lances automáticos: ligados (sensibilidade {levels[Math.Clamp(_s.StudioAutoClipSensitivity, 0, 2)]})." + (_outReplay ? "" : " Ligue o bloco Replay instantâneo para valer.");
    }

    private void BuildShortcuts()
    {
        _tabShortcuts.Children.Add(Muted("Funcionam com o jogo em tela cheia e com o MB Extreme minimizado. Usam Ctrl + Shift ou Ctrl + Alt com F5 a F12, combinações que o Windows e quase todos os jogos não usam.", 11));
        var actions = new[]
        {
            (StudioAction.SaveReplay, "Salva os últimos segundos como um clipe (precisa do Replay ligado)."),
            (StudioAction.ToggleRecord, "Liga a gravação com a última origem usada; aperte de novo para parar."),
            (StudioAction.ToggleStream, "Liga a transmissão com o último destino usado; aperte de novo para parar."),
        };
        foreach (var (action, help) in actions)
        {
            var act = action;
            var current = HotkeyBinding.Parse(Stored(act));
            int modIndex = current?.Modifier == HotkeyModifier.CtrlAlt ? 1 : 0;
            var keys = new List<string> { "Desligado" };
            for (int f = HotkeyBinding.MinKey; f <= HotkeyBinding.MaxKey; f++) keys.Add("F" + f);
            int keyIndex = current is null ? 0 : current.FKey - HotkeyBinding.MinKey + 1;

            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = StudioHotkeys.Title(act), FontWeight = FontWeights.SemiBold, FontSize = 13 });
            panel.Children.Add(Muted(help, 11));
            var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            int mod = modIndex, key = keyIndex;
            void Apply()
            {
                string value = key == 0 ? "" : new HotkeyBinding(mod == 1 ? HotkeyModifier.CtrlAlt : HotkeyModifier.CtrlShift, HotkeyBinding.MinKey + key - 1).Stored;
                Store(act, value);
                _s.Save();
                _studio.ApplyHotkeys(_s);
                ShowHotkeyNote();
            }
            row.Children.Add(Chips(new[] { "Ctrl + Shift", "Ctrl + Alt" }, mod, i => { mod = i; Apply(); }));
            row.Children.Add(new Border { Width = 14 });
            row.Children.Add(Chips(keys, key, i => { key = i; Apply(); }));
            panel.Children.Add(row);
            var card = CardOf(panel);
            card.Padding = new Thickness(12, 8, 12, 4);
            card.Margin = new Thickness(0, 0, 0, 6);
            _tabShortcuts.Children.Add(card);
        }

        _tabShortcuts.Children.Add(_hotkeyNote);
        ShowHotkeyNote();
    }

    private string Stored(StudioAction action) => action switch
    {
        StudioAction.SaveReplay => _s.StudioHotkeySave,
        StudioAction.ToggleRecord => _s.StudioHotkeyRecord,
        _ => _s.StudioHotkeyStream,
    };

    private void Store(StudioAction action, string value)
    {
        switch (action)
        {
            case StudioAction.SaveReplay: _s.StudioHotkeySave = value; break;
            case StudioAction.ToggleRecord: _s.StudioHotkeyRecord = value; break;
            default: _s.StudioHotkeyStream = value; break;
        }
    }

    private void ShowHotkeyNote()
    {
        var failures = _studio.HotkeyFailures;
        _hotkeyNote.FontSize = 12;
        if (failures.Count == 0)
        {
            _hotkeyNote.Text = "Atalhos ativos.";
            _hotkeyNote.Foreground = Brush("OkBrush");
            return;
        }

        _hotkeyNote.Text = "Não foi possível ativar: " + string.Join("; ", failures) + ". Escolha outra combinação.";
        _hotkeyNote.Foreground = Brush("WarnBrush");
    }

    private void BuildCompat()
    {
        _tabAdvanced.Children.Add(Heading("Modo compatível (sem FFmpeg)"));
        _tabAdvanced.Children.Add(Muted("Grava a tela inteira em AVI sem instalar nada e sem áudio. Mais pesado e limitado; use só se o modo principal não funcionar no seu PC.", 11));
        _tabAdvanced.Children.Add(Field("Quadros", Chips(AviRates.Select(r => r + " fps").ToList(), _aviRate, i => _aviRate = i)));
        _tabAdvanced.Children.Add(Field("Tamanho", Chips(AviScales.Select(s => $"{s:0%}").ToList(), _aviScale, i => _aviScale = i)));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var aviStart = SmallButton("Gravar em modo compatível", (_, _) => ToggleAvi(), false);
        aviStart.Margin = new Thickness(0);
        row.Children.Add(aviStart);
        _tabAdvanced.Children.Add(row);
    }

    // ---------------------------------------------------------------- inicialização e dados

    private static Visibility Vis(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private static int NearestVolume(double v)
    {
        int best = 0;
        for (int i = 1; i < Volumes.Length; i++)
            if (Math.Abs(Volumes[i].Value - v) < Math.Abs(Volumes[best].Value - v)) best = i;
        return best;
    }

    private async Task InitAsync()
    {
        try
        {
            var hw = await AppServices.Hardware();
            int tier = MBExtreme.Core.Games.GameAdvisor.Tier(hw);
            if (tier != _tier) { _tier = tier; BuildVideo(); }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Management.ManagementException or AggregateException) { }
        _ffmpegPath = await Task.Run(FfmpegRecorder.Locate);
        RebuildCameras();
        if (_ffmpegPath is null)
        {
            _engineStatus.Text = "FFmpeg não encontrado. Ele é o programa livre que faz a captura e a codificação. Clique em Instalar FFmpeg (instala pelo winget, o instalador oficial da Microsoft).";
            return;
        }

        _engineStatus.Text = "FFmpeg encontrado. Clique em \"Detectar codificadores da minha placa\" para ver o que ela suporta (leva alguns segundos).";
        if (_encoders.Count == 0 && AppServices.CachedEncoders is { Count: > 0 } cached) _encoders = cached;
        if (_encoders.Count > 0) BuildEncoderChoice();
    }

    private async Task InstallFfmpegAsync()
    {
        ShowProgress("Instalando o FFmpeg", "Instalação");
        IProgress<ProgressEvent> progress = new Progress<ProgressEvent>(Progress.Report);
        var (ok, message) = await Task.Run(() =>
        {
            progress.Report(new ProgressEvent("Instalação", 20, "Instalando pelo winget (Gyan.FFmpeg)"));
            return WingetService.Install("Gyan.FFmpeg");
        });
        Progress.Finish(ok ? "FFmpeg instalado." : "Não foi possível instalar.", ok, message);
    }

    protected override async void OnProgressClosed()
    {
        RefreshFiles();
        await InitAsync();
    }

    private async Task DetectAsync()
    {
        _ffmpegPath ??= await Task.Run(FfmpegRecorder.Locate);
        if (_ffmpegPath is null)
        {
            SetStatus("Instale o FFmpeg primeiro.", "WarnBrush");
            return;
        }

        SetStatus("Testando os codificadores da placa de vídeo...");
        string path = _ffmpegPath;
        _encoders = await Task.Run(() => FfmpegRecorder.ProbeEncoders(path));
        AppServices.CachedEncoders = _encoders;
        SetStatus(_encoders.Count == 0 ? "Nenhum codificador de GPU funcionou. Atualize o driver da placa de vídeo." : $"{_encoders.Count} codificador(es) disponível(is).",
            _encoders.Count == 0 ? "WarnBrush" : "OkBrush");
        BuildEncoderChoice();
    }

    private void BuildEncoderChoice()
    {
        _qualityEncoder.Children.Clear();
        if (_encoders.Count == 0) return;
        _engineStatus.Text = "Pronto: FFmpeg e codificadores encontrados.";
        _encoderIndex = Math.Min(_encoderIndex, _encoders.Count - 1);
        _qualityEncoder.Children.Add(Chips(_encoders.Select(e => e.Title).ToList(), _encoderIndex, i => _encoderIndex = i));

        // codecs que a placa não tem (AV1 só existe em placas mais novas): avisa em vez de esconder
        var makers = _encoders.Select(e => e.Kind).Distinct().ToList();
        var missing = new List<string>();
        foreach (var kind in makers)
            foreach (var codec in new[] { VideoCodec.H264, VideoCodec.Hevc, VideoCodec.Av1 })
                if (!_encoders.Any(e => e.Kind == kind && e.Codec == codec))
                    missing.Add(new EncoderOption(codec, kind, "").Title);
        if (missing.Count > 0)
        {
            var note = Muted("Sem suporte nesta placa ou neste FFmpeg: " + string.Join(", ", missing) + ".", 11);
            _qualityEncoder.Children.Add(note);
        }
    }

    private void RefreshDisplays()
    {
        _displayList = CaptureCatalog.Displays();
        var names = _displayList.Count > 0
            ? _displayList.Select(d => d.Title).ToList()
            : new List<string> { $"Tela principal · {ScreenInfo.Primary.Width}×{ScreenInfo.Primary.Height}" };
        _displayIndex = Math.Clamp(_displayIndex, 0, names.Count - 1);
        _displayHost.Children.Clear();
        _displayHost.Children.Add(Chips(names, _displayIndex, i => { _displayIndex = i; UpdatePlan(); }));
    }

    private void RefreshWindows()
    {
        _windowList = CaptureCatalog.Windows();
        _windows.SetItems(_windowList.Select(w => w.DisplayName).ToList(), _windowList.Count > 0 ? 0 : -1);
        if (_windowList.Count == 0) SetStatus("Nenhuma janela encontrada. Abra o programa e atualize a lista.", "WarnBrush");
    }

    private void RefreshAudioDevices()
    {
        _outList = AudioDevices.Outputs();
        _micList = AudioDevices.Microphones();
        var outs = new List<string> { "Padrão do Windows" };
        outs.AddRange(_outList.Select(d => d.Name));
        _outputs.SetItems(outs, Math.Max(0, _outList.ToList().FindIndex(d => d.Id == _s.StudioOutputId) + 1));

        var mics = new List<string> { "Padrão do Windows" };
        mics.AddRange(_micList.Select(d => d.Name));
        _mics.SetItems(mics, Math.Max(0, _micList.ToList().FindIndex(d => d.Id == _s.StudioMicId) + 1));
    }

    // ---------------------------------------------------------------- iniciar e parar

    private async Task BeginAsync()
    {
        if (_studio.IsRunning || AppServices.Recorder.IsRecording) return;
        if (_ffmpegPath is null || _encoders.Count == 0)
        {
            SetStatus("Instale o FFmpeg e detecte os codificadores antes de começar.", "WarnBrush");
            return;
        }
        if (!_outRecord && !_outStream && !_outReplay)
        {
            SetStatus("Marque pelo menos uma opção: gravar, transmitir ou replay.", "WarnBrush");
            return;
        }

        // a janela escolhida pode ter mudado de lugar ou fechado desde a lista
        if (_sourceTab == 1 && !await RefreshChosenWindowAsync()) return;

        var (config, error) = BuildConfig(write: true);
        if (config is null)
        {
            if (error is not null) SetStatus(error, "WarnBrush");
            return;
        }

        var target = config.Stream;
        if (target is not null)
        {
            if (MessageBox.Show($"Começar a transmitir ao vivo para {target.DisplayName}?\n\nQuem estiver assistindo verá exatamente o que está sendo capturado.",
                    "Transmissão ao vivo", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        }

        PersistSettings(target);
        var source = config.Source;

        _start.IsEnabled = false;
        SetStatus("Iniciando...");
        try
        {
            await _studio.StartAsync(_ffmpegPath, config);
            var parts = new List<string>();
            if (_outRecord) parts.Add("gravando");
            if (_outStream) parts.Add("transmitindo para " + target!.DisplayName);
            if (_outReplay) parts.Add("replay ligado");
            SetStatus($"{char.ToUpper(parts[0][0])}{parts[0][1..]}{(parts.Count > 1 ? ", " + string.Join(", ", parts.Skip(1)) : "")} · {source.Description}", "OkBrush");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or Win32Exception)
        {
            SetStatus("Não foi possível iniciar: " + StudioCommand.MaskSecrets(ex.Message, target), "BadBrush");
        }

        RefreshState();
    }

    /// <summary>Atualiza a janela escolhida (posição e título) e avisa se ela sumiu.</summary>
    private async Task<bool> RefreshChosenWindowAsync()
    {
        if (_windows.Selected < 0 || _windows.Selected >= _windowList.Count)
        {
            SetStatus("Escolha a janela que será capturada.", "WarnBrush");
            return false;
        }

        var chosen = _windowList[_windows.Selected];
        var current = await Task.Run(() => CaptureCatalog.Windows().FirstOrDefault(w => w.Handle == chosen.Handle));
        if (current is null)
        {
            SetStatus("A janela escolhida foi fechada ou minimizada. Atualize a lista e escolha de novo.", "WarnBrush");
            RefreshWindows();
            return false;
        }

        var list = _windowList.ToList();
        list[_windows.Selected] = current;
        _windowList = list;
        return true;
    }

    /// <summary>Monta a configuração a partir do que está na tela. <paramref name="write"/> grava a imagem do QR do Pix (só ao iniciar ou aplicar, para não escrever em disco a cada segundo).</summary>
    private (StudioConfig? Config, string? Error) BuildConfig(bool write)
    {
        if (_ffmpegPath is null || _encoders.Count == 0) return (null, "Instale o FFmpeg e detecte os codificadores antes de começar.");
        if (!_outRecord && !_outStream && !_outReplay) return (null, "Marque pelo menos uma opção: gravar, transmitir ou replay.");

        // ---- origem
        CaptureSource? source;
        string? game = null;
        var displays = _displayList.Count > 0 ? _displayList : new[] { new DisplayInfo(0, "", new PixelRect(0, 0, ScreenInfo.Primary.Width, ScreenInfo.Primary.Height), true) };
        if (_sourceTab == 0)
        {
            source = CaptureRules.ForDisplay(displays[Math.Clamp(_displayIndex, 0, displays.Count - 1)]);
        }
        else
        {
            if (_windows.Selected < 0 || _windows.Selected >= _windowList.Count) return (null, "Escolha a janela que será capturada.");
            var window = _windowList[_windows.Selected];
            game = window.ProcessName;
            source = CaptureRules.ForWindow(window, displays, gdi: _windowMode == 1);
            if (source is null) return (null, "A janela está fora da área visível das telas.");
        }

        // ---- codificador
        var encoder = _encoders[Math.Min(_encoderIndex, _encoders.Count - 1)];

        // ---- transmissão
        StreamTarget? target = null;
        if (_outStream)
        {
            var h264 = encoder.Codec == VideoCodec.H264 ? encoder : _encoders.FirstOrDefault(e => e.Codec == VideoCodec.H264);
            if (h264 is null) return (null, "A transmissão exige um codificador H.264, que não foi encontrado na sua placa.");
            encoder = h264;
            target = new StreamTarget((StreamService)Math.Clamp(_s.StudioStreamService, 0, 3), _customUrl.Text, _key.Password);
        }

        // ---- câmeras
        var cameras = new List<CameraOverlay>();
        foreach (var cam in _s.StudioCameras.Where(c => c.On))
        {
            if (!CameraOverlay.IsValidName(cam.Name)) return (null, "Escolha a câmera na aba Câmeras (clique em Procurar câmeras) ou desligue-a.");
            var camFilter = PresetFilter(cam.Preset, cam.Mirror);
            cameras.Add(new CameraOverlay(cam.Name!, (CameraCorner)Math.Clamp(cam.Corner, 0, 3), cam.Percent, cam.Frame, camFilter.IsNeutral ? null : camFilter));
        }

        // ---- Pix
        PixOverlay? pix = null;
        string? pixPayload = null;
        if (_s.StudioPixOn)
        {
            if (_s.StudioPixUseOwn)
            {
                if (_s.StudioPixOwnImage is not { } own || !File.Exists(own)) return (null, "O Pix está ligado para usar o QR do seu banco, mas a imagem não foi escolhida (aba Pix).");
                pixPayload = "proprio:" + new FileInfo(own).Length + ":" + _s.StudioChannelName + ":" + _s.StudioPixNote;
                if (write)
                {
                    try { OverlayArt.SavePng(OverlayArt.RenderPixOwn(own, _s.StudioChannelName, _s.StudioPixNote), PixImagePath); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return (null, "Não foi possível preparar a imagem do Pix: " + ex.Message); }
                }
            }
            else
            {
                if (_pixPayload is null) return (null, "O QR do Pix está ligado mas faltam dados (aba Pix): chave, nome e cidade. Preencha ou desligue.");
                pixPayload = _pixPayload;
                if (write)
                {
                    try { PixImage.SavePng(PixImage.Render(_pixPayload, string.IsNullOrWhiteSpace(_s.StudioPixNote) ? "Apoie via Pix" : _s.StudioPixNote, PixPayload.Clean(_s.StudioPixName, 25)), PixImagePath); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return (null, "Não foi possível preparar a imagem do Pix: " + ex.Message); }
                }
            }

            pix = new PixOverlay(PixImagePath, (CameraCorner)Math.Clamp(_s.StudioPixCorner, 0, 3), _s.StudioPixPercent, _s.StudioPixEveryMinutes, _s.StudioPixShowSeconds);
        }

        // ---- identidade do canal e filtro de imagem
        List<OverlayLayer> layers;
        try { layers = BuildLayers(write); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { return (null, "Não foi possível preparar as camadas do canal: " + ex.Message); }
        var imageFilter = CurrentFilter();

        // ---- vídeo vertical e legendas
        var vertical = CurrentVertical();
        if (vertical?.Validate() is { } verticalProblem) return (null, verticalProblem);
        var verticalCopy = CurrentVerticalCopy();
        if (verticalCopy?.Validate() is { } copyProblem) return (null, copyProblem);
        var captions = CurrentCaptions();
        if (captions is not null && !SpeechInstaller.IsInstalled) return (null, "Baixe o modelo de voz na aba Vertical para usar as legendas.");

        // ---- áudio
        AudioConfig? audio = null;
        if (_s.StudioSystemAudio || _s.StudioMicAudio)
        {
            string? outputId = _outputs.Selected > 0 && _outputs.Selected - 1 < _outList.Count ? _outList[_outputs.Selected - 1].Id : null;
            string? micId = _mics.Selected > 0 && _mics.Selected - 1 < _micList.Count ? _micList[_mics.Selected - 1].Id : null;
            audio = new AudioConfig(_s.StudioSystemAudio, outputId, (float)_s.StudioSystemVolume, _s.StudioMicAudio, micId, (float)_s.StudioMicVolume, (VoiceEffect)Math.Clamp(_s.StudioVoiceEffect, 0, 2));
        }

        var config = new StudioConfig(source, game, Resolutions[Math.Clamp(_s.StudioResolution, 0, Resolutions.Length - 1)].Height,
            _s.StudioFps, encoder, _s.StudioBitrateMbps, _outRecord, _s.StudioMp4, target, _outReplay, _s.StudioReplaySeconds, _s.StudioReplayBeep, audio,
            _s.StudioStreamCapMbps, _s.StudioAudioKbps, (EncoderQuality)Math.Clamp(_s.StudioEncoderQuality, 0, 2), cameras, pix, pixPayload,
            imageFilter.IsNeutral ? null : imageFilter, layers, LayersKey(), vertical, captions, verticalCopy);
        return (config, null);
    }

    /// <summary>Guarda o que precisa ficar para a próxima vez (nunca a chave de transmissão em texto: só protegida pelo Windows).</summary>
    private void PersistSettings(StreamTarget? target)
    {
        if (target is not null && target.PathSecret is null) _s.StudioCustomUrl = target.CustomUrl;
        _s.StudioOutputId = _outputs.Selected > 0 && _outputs.Selected - 1 < _outList.Count ? _outList[_outputs.Selected - 1].Id : null;
        _s.StudioMicId = _mics.Selected > 0 && _mics.Selected - 1 < _micList.Count ? _micList[_mics.Selected - 1].Id : null;
        _s.StudioChatId = _chatId.Text.Trim();
        _s.StudioRememberKey = _remember.IsChecked == true;
        try
        {
            _s.StudioProtectedKey = _s.StudioRememberKey && _key.Password.Length > 0 ? SecretStore.Protect(_key.Password) : null;
        }
        catch (CryptographicException)
        {
            _s.StudioProtectedKey = null;
            SetStatus("Não foi possível guardar a chave com segurança; ela não será lembrada.", "WarnBrush");
        }
        _s.Save();
    }

    // ---------------------------------------------------------------- mudar com o estúdio ligado

    /// <summary>Compara o que está na tela com o que está no ar e mostra a barra "Aplicar agora" quando há diferença.</summary>
    private void UpdatePending()
    {
        if (!_studio.IsRunning || _studio.Config is not { } running || _applying)
        {
            _pending.Visibility = Visibility.Collapsed;
            return;
        }

        var (wanted, error) = BuildConfig(write: false);
        if (wanted is null)
        {
            _pendingText.Text = "Há uma mudança que ainda não dá para aplicar: " + (error ?? "confira as configurações") + " A captura segue como estava.";
            _pendingButton.IsEnabled = false;
            _pending.Visibility = Visibility.Visible;
            return;
        }

        bool different = StudioController.Signature(wanted) != StudioController.Signature(running);
        _pendingText.Text = "Você mudou configurações que só entram no ar reiniciando a captura. Aplicar agora faz a transmissão reconectar por alguns segundos e a gravação continuar em um arquivo novo; o replay recomeça vazio.";
        _pendingButton.IsEnabled = true;
        _pending.Visibility = Vis(different);
    }

    private async Task ApplyAsync()
    {
        if (!_studio.IsRunning || _applying) return;
        var (config, error) = BuildConfig(write: true);
        if (config is null)
        {
            SetStatus(error ?? "Não foi possível aplicar.", "WarnBrush");
            return;
        }

        if (!_restartWarned)
        {
            var ask = MessageBox.Show(
                "Para aplicar, o MB Extreme reinicia a captura:\n\n• a transmissão sai do ar por alguns segundos e reconecta sozinha;\n• a gravação continua num arquivo novo (o anterior fica salvo);\n• o replay instantâneo recomeça vazio.\n\nMudar o volume do microfone e do som do PC e mutar não precisa reiniciar. Aplicar agora?",
                "Aplicar mudanças ao vivo", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (ask != MessageBoxResult.Yes) return;
            _restartWarned = true;
        }

        PersistSettings(config.Stream);
        _applying = true;
        _pending.Visibility = Visibility.Collapsed;
        SetStatus("Aplicando as mudanças...");
        try
        {
            await _studio.RestartAsync(config);
            SetStatus("Mudanças aplicadas. " + config.Source.Description, "OkBrush");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or Win32Exception)
        {
            SetStatus("Não foi possível aplicar (o estúdio parou): " + StudioCommand.MaskSecrets(ex.Message, config.Stream), "BadBrush");
        }
        finally
        {
            _applying = false;
        }

        RefreshState();
        RefreshFiles();
    }

    private void ToggleMute(bool microphone, bool mute)
    {
        if (!_studio.IsRunning) return;
        float configured = (float)(microphone ? _s.StudioMicVolume : _s.StudioSystemVolume);
        _studio.SetVolume(microphone, mute ? 0f : Math.Max(configured, 0.05f));
        RefreshMuteButtons();
    }

    private void RefreshMuteButtons()
    {
        bool running = _studio.IsRunning;
        bool mic = running && _studio.Session.HasMicrophone;
        bool sys = running && _studio.Session.HasSystemAudio;
        _muteMic.Visibility = Vis(mic);
        _muteSystem.Visibility = Vis(sys);
        if (mic)
        {
            bool muted = _studio.GetVolume(true) <= 0.001f;
            _muteMic.Content = muted ? "Microfone: mudo (clique para ligar)" : "Microfone: ligado (clique para mutar)";
            _muteMic.Style = (Style)FindResource(muted ? "GhostButton" : "PrimaryButton");
        }
        if (sys)
        {
            bool muted = _studio.GetVolume(false) <= 0.001f;
            _muteSystem.Content = muted ? "Som do PC: mudo (clique para ligar)" : "Som do PC: ligado (clique para mutar)";
            _muteSystem.Style = (Style)FindResource(muted ? "GhostButton" : "PrimaryButton");
        }
    }

    private async Task EndAsync()
    {
        _stop.IsEnabled = false;
        if (AppServices.Recorder.IsRecording)
        {
            string? avi = AppServices.Recorder.Stop();
            if (avi is not null) SetStatus("Gravação salva em " + avi, "OkBrush");
        }
        else
        {
            SetStatus("Finalizando o arquivo...");
            string? file = await Task.Run(_studio.Stop);
            SetStatus(file is null ? "Estúdio encerrado." : "Gravação salva em " + file, "OkBrush");
        }

        RefreshState();
        RefreshFiles();
    }

    private void ToggleAvi()
    {
        try
        {
            if (AppServices.Recorder.IsRecording) { _ = EndAsync(); return; }
            if (_studio.IsRunning) { SetStatus("Pare o estúdio antes de usar o modo compatível.", "WarnBrush"); return; }
            string file = AppServices.Recorder.Start("tela", AviRates[_aviRate], AviScales[_aviScale]);
            SetStatus("Gravando em " + file);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            SetStatus("Não foi possível gravar: " + ex.Message, "BadBrush");
        }

        RefreshState();
    }

    private void OnNotice(string message, bool ok)
    {
        SetStatus(message, ok ? "OkBrush" : "WarnBrush");
        RefreshFiles();
    }

    // ---------------------------------------------------------------- estado em tempo real

    private void RefreshState()
    {
        bool running = _studio.IsRunning || AppServices.Recorder.IsRecording;
        _start.IsEnabled = !running;
        _stop.IsEnabled = running;
        _saveReplay.IsEnabled = _studio.IsRunning && _studio.Config?.Replay == true;
        // com o estúdio ligado as configurações continuam editáveis: o que exige reiniciar aparece na barra "Aplicar agora"
        if (!running) _pending.Visibility = Visibility.Collapsed;
        RefreshMuteButtons();
        if (_s.StudioCaptionOn && _settingsTab == 1) RefreshCaptionNow();
        _live.Visibility = Vis(_studio.IsRunning);
        bool streaming = _studio.IsRunning && _studio.Config?.Stream is not null;
        if (streaming && !_wasStreaming) { _viewers.Reset(); _hasStats = false; if (_s.StudioStatsOn) _ = RefreshStatsAsync(false); }
        if (streaming && !_wasStreaming && _s.StudioChatAuto && _chatPanel is { IsConnected: false }) _chatPanel.Connect();
        if (!streaming && _wasStreaming && _chatPanel is { IsConnected: true }) _chatPanel.Disconnect();
        _wasStreaming = streaming;
        UpdateStatsVisibility();
        _liveButtons.Visibility = Vis(_studio.Config?.Stream is not null && _studio.IsRunning);
        _wasRunning = running;
    }

    private void Tick()
    {
        _rate = _net.Sample();
        _netNow.Text = $"↑ {_rate.Up:0.0} Mbps enviando · ↓ {_rate.Down:0.0} Mbps recebendo (toda a internet deste PC)";

        // um atalho pode ter ligado ou parado o estúdio sem passar por esta tela
        bool nowRunning = _studio.IsRunning || AppServices.Recorder.IsRecording;
        if (nowRunning != _wasRunning)
        {
            RefreshState();
            RefreshFiles();
        }

        if (++_tickCount % 2 == 0) UpdatePending();
        var session = _studio.Session;
        if (session.EndedUnexpectedly)
        {
            string? last = session.LastMessage;
            _studio.Stop();
            SetStatus("O estúdio terminou sozinho" + (last is null ? "." : ": " + last), "WarnBrush");
            RefreshState();
            RefreshFiles();
            return;
        }

        if (session.IsRunning)
        {
            var p = session.Progress;
            var elapsed = DateTime.Now - session.StartedAt;
            string text = $"{elapsed:hh\\:mm\\:ss} · {p.Fps:0} fps · {p.BitrateKbps / 1000:0.0} Mbps · {p.TotalBytes / 1_048_576.0:0} MB";
            if (p.DroppedFrames > 0) text += $" · {p.DroppedFrames} quadros perdidos";
            if (session.Request?.Stream is { } stream) text = (session.StreamDropped ? "⚠ Transmissão com problema · " : $"● AO VIVO em {stream.DisplayName} · ") + text;
            _liveText.Text = text;
            _liveNet.Text = $"Internet agora: ↑ {_rate.Up:0.0} Mbps enviando · ↓ {_rate.Down:0.0} Mbps recebendo" +
                            (session.Request?.Stream is null ? "" : $" · a transmissão usa cerca de {p.BitrateKbps / 1000:0.0} Mbps do envio");
            if (session.Request?.Stream is not null)
            {
                var health = StreamHealthRules.Evaluate(p.Fps, _studio.Config?.Fps ?? 0, p.BitrateKbps, (_studio.Config?.BitrateMbps ?? 0) * 1000.0, p.DroppedFrames, (long)(p.Fps * p.Elapsed.TotalSeconds) + p.DroppedFrames, session.StreamDropped);
                _health.Text = health.Message;
                _health.Foreground = Brush(health.Level switch { StreamHealthLevel.Good => "OkBrush", StreamHealthLevel.Warning => "WarnBrush", _ => "BadBrush" });
                _health.Visibility = Visibility.Visible;
            }
            else _health.Visibility = Visibility.Collapsed;
            _liveText.Foreground = Brush(session.StreamDropped ? "WarnBrush" : "TextBrush");
            _level.Visibility = Vis(session.HasAudio);
            _level.Value = Math.Min(100, session.AudioPeak * 100);

            if (_studio.Config?.Replay == true && session.ReplayPath is { } folder)
            {
                int ready = ReplayBuffer.SelectSegments(ReplayBuffer.ListSegments(folder), _studio.Config.ReplaySeconds).Count * StudioCommand.SegmentSeconds;
                _replayText.Text = $"Replay pronto: {ready} s de {_studio.Config.ReplaySeconds} s" + (_studio.HotkeyActive && HotkeyBinding.Parse(_s.StudioHotkeySave) is { } hk ? $" · {hk.Text} salva o clipe" : "");
                _replayText.Visibility = Visibility.Visible;
            }
            else _replayText.Visibility = Visibility.Collapsed;

            var warnings = session.Warnings;
            _warnings.Text = string.Join("\n", warnings.TakeLast(3));
            _warnings.Visibility = Vis(warnings.Count > 0);
            return;
        }

        var avi = AppServices.Recorder;
        if (avi.IsRecording)
            SetStatus($"Gravando (modo compatível) {DateTime.Now - avi.StartedAt:mm\\:ss} · {avi.CurrentBytes / 1_048_576.0:0} MB");
    }

    // ---------------------------------------------------------------- arquivos

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(StudioController.Folder);
            Open(StudioController.Folder);
        }
        catch (IOException ex)
        {
            SetStatus(ex.Message, "BadBrush");
        }
    }

    private static readonly string[] VideoExtensions = { ".avi", ".mkv", ".mp4" };

    private void RefreshFiles()
    {
        _files.Children.Clear();
        var files = new List<string>();
        try
        {
            foreach (string folder in new[] { StudioController.Folder, StudioController.ClipsFolder })
                if (Directory.Exists(folder))
                    files.AddRange(Directory.GetFiles(folder).Where(f => VideoExtensions.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant())));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        if (files.Count == 0)
        {
            _files.Children.Add(Muted("Nada gravado ainda."));
            return;
        }

        foreach (string file in files.OrderByDescending(f => File.GetCreationTime(f)).Take(20))
        {
            var info = new FileInfo(file);
            bool clip = info.Name.StartsWith("Replay_", StringComparison.OrdinalIgnoreCase);
            _files.Children.Add(FileRow(file, info, clip));
        }
    }

    /// <summary>Linha enxuta: nome, tamanho e data numa linha só, com botões pequenos.</summary>
    private Border FileRow(string file, FileInfo info, bool clip)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12 };
        text.Inlines.Add(new System.Windows.Documents.Run((clip ? "🎬 " : "") + info.Name) { FontWeight = FontWeights.SemiBold });
        text.Inlines.Add(new System.Windows.Documents.Run($"   {info.Length / 1_048_576.0:0.0} MB · {info.CreationTime:dd/MM/yyyy HH:mm}") { Foreground = Brush("MutedBrush") });
        grid.Children.Add(text);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, action, primary) in new (string, Action, bool)[] { ("Abrir", () => Open(file), true), ("Excluir", () => Delete(file), false) })
        {
            var button = new Button { Content = label, Style = (Style)FindResource(primary ? "PrimaryButton" : "GhostButton"), Padding = new Thickness(10, 3, 10, 3), FontSize = 11, Margin = new Thickness(6, 0, 0, 0) };
            var act = action;
            button.Click += (_, _) => act();
            buttons.Children.Add(button);
        }
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        return new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 0, 4), Child = grid };
    }

    private void Delete(string file)
    {
        if (MessageBox.Show($"Excluir {System.IO.Path.GetFileName(file)}?\n\nO arquivo é apagado de vez.", "Excluir vídeo",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("Não foi possível excluir: " + ex.Message, "BadBrush");
        }

        RefreshFiles();
    }

    // ---------------------------------------------------------------- lista de escolha

    /// <summary>Lista rolável de opções com uma escolhida (amarela).</summary>
    private sealed class PickList : ScrollViewer
    {
        private readonly StackPanel _items = new();
        private readonly FrameworkElement _owner;
        private readonly List<Button> _buttons = new();

        public PickList(FrameworkElement owner, double maxHeight)
        {
            _owner = owner;
            MaxHeight = maxHeight;
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            Content = _items;
            Margin = new Thickness(0, 6, 0, 0);
        }

        public int Selected { get; private set; } = -1;

        public event Action<int>? Picked;

        public void SetItems(IReadOnlyList<string> names, int selected)
        {
            _items.Children.Clear();
            _buttons.Clear();
            for (int i = 0; i < names.Count; i++)
            {
                int index = i;
                var button = new Button
                {
                    Content = new TextBlock { Text = names[i], TextTrimming = TextTrimming.CharacterEllipsis },
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 0, 0, 3),
                    Padding = new Thickness(10, 4, 10, 4),
                    FontSize = 12,
                };
                button.Click += (_, _) => Select(index, true);
                _buttons.Add(button);
                _items.Children.Add(button);
            }

            Select(names.Count == 0 ? -1 : Math.Clamp(selected, 0, names.Count - 1), false);
        }

        private void Select(int index, bool notify)
        {
            Selected = index;
            for (int i = 0; i < _buttons.Count; i++)
                _buttons[i].Style = (Style)_owner.FindResource(i == index ? "PrimaryButton" : "GhostButton");
            if (notify) Picked?.Invoke(index);
        }
    }
}
