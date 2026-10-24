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
using MBExtreme.UI.Recording;

namespace MBExtreme.UI.Pages;

/// <summary>Estúdio: grava a tela ou uma janela, transmite ao vivo e guarda os melhores momentos (replay), com som do PC e microfone.</summary>
public sealed class StudioPage : ToolPageBase
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
    private readonly Border _ghost = new() { Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private readonly StackPanel _displayHost = new();
    private readonly StackPanel _cameraPick = new();
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
        Body.Children.Add(_config);
        BuildSource();
        BuildTiles();
        BuildSettings();
        BuildEngine();
        BuildVideo();
        BuildAudio();
        BuildCamera();
        BuildStream();
        BuildReplaySettings();
        BuildShortcuts();
        BuildCompat();

        var filesTitle = Heading("Gravações e replays");
        filesTitle.FontSize = 13;
        Body.Children.Add(filesTitle);
        Body.Children.Add(_files);

        _studio.Notice += OnNotice;
        _timer.Tick += (_, _) => Tick();
        _previewTimer.Tick += async (_, _) => await UpdatePreviewAsync();
        Loaded += async (_, _) =>
        {
            _timer.Start();
            _previewTimer.Start();
            RefreshFiles();
            RefreshState();
            await InitAsync();
        };
        Unloaded += (_, _) =>
        {
            _timer.Stop();
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
            if (_studio.IsRunning || AppServices.Recorder.IsRecording) return;
            toggle();
            RefreshTiles();
            UpdatePlan();
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

        _ghost.Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
        _ghost.BorderBrush = Brush("AccentBrush");
        _ghost.BorderThickness = new Thickness(2);
        _ghost.CornerRadius = new CornerRadius(4);
        _ghost.Child = new TextBlock { Text = "Câmera", FontSize = 11, Foreground = Brush("AccentBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _previewHost.Children.Add(_preview);
        _previewHost.Children.Add(_ghost);
        _preview.SizeChanged += (_, _) => UpdateGhost();

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

    /// <summary>Retângulo tracejado que mostra onde a câmera vai aparecer (só enquanto a câmera estiver ligada).</summary>
    private void UpdateGhost()
    {
        bool show = _s.StudioCameraOn && _preview.Source is not null && _preview.ActualWidth > 40;
        _ghost.Visibility = Vis(show);
        if (!show) return;

        double width = _preview.ActualWidth * Math.Clamp(_s.StudioCameraPercent, 10, 40) / 100.0;
        _ghost.Width = Math.Max(24, width);
        _ghost.Height = Math.Max(14, width * 9 / 16);
        double margin = Math.Max(4, _preview.ActualWidth / 80);
        _ghost.Margin = new Thickness(margin);
        int corner = Math.Clamp(_s.StudioCameraCorner, 0, 3);
        _ghost.HorizontalAlignment = corner is 0 or 2 ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        _ghost.VerticalAlignment = corner is 0 or 1 ? VerticalAlignment.Top : VerticalAlignment.Bottom;
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
        if (_previewBusy || _studio.IsRunning || AppServices.Recorder.IsRecording) return;
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
                    UpdateGhost();
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
                UpdateGhost();
                return;
            }

            _preview.Source = frame.Image;
            _previewNote.Text = frame.MostlyBlack
                ? "A imagem saiu preta. Em jogos, use \"Recorte da tela\"; em programas comuns, \"Conteúdo da janela\"."
                : "É assim que o vídeo vai sair (imagem reduzida, atualizada várias vezes por segundo).";
            _previewNote.Foreground = Brush(frame.MostlyBlack ? "WarnBrush" : "MutedBrush");
            UpdateGhost();
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
        var tabs = new[] { _tabVideo, _tabAudio, _tabCamera, _tabStream, _tabReplay, _tabShortcuts, _tabAdvanced };
        _config.Children.Add(Chips(new[] { "Vídeo", "Áudio", "Câmera", "Transmissão", "Replay", "Atalhos", "Avançado" }, _settingsTab, i =>
        {
            _settingsTab = i;
            for (int t = 0; t < tabs.Length; t++) tabs[t].Visibility = Vis(t == i);
            if (i == 3) UpdatePlan();
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
        left.Children.Add(Chips(Volumes.Select(v => v.Name).ToList(), NearestVolume(_s.StudioSystemVolume), i => _s.StudioSystemVolume = Volumes[i].Value));
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
        right.Children.Add(Chips(Volumes.Select(v => v.Name).ToList(), NearestVolume(_s.StudioMicVolume), i => _s.StudioMicVolume = Volumes[i].Value));
        right.Children.Add(Muted("Microfone:", 11));
        right.Children.Add(_mics);
        var rightCard = CardOf(right);
        rightCard.Margin = new Thickness(6, 0, 0, 0);
        rightCard.Padding = new Thickness(10, 8, 10, 8);
        Grid.SetColumn(rightCard, 1);
        columns.Children.Add(rightCard);
        _tabAudio.Children.Add(columns);

        _tabAudio.Children.Add(Field("Qualidade do áudio", Chips(AudioKbpsOptions.Select(k => k + " kbps").ToList(),
            Math.Max(0, Array.IndexOf(AudioKbpsOptions, _s.StudioAudioKbps)), i => { _s.StudioAudioKbps = AudioKbpsOptions[i]; UpdatePlan(); })));
        _tabAudio.Children.Add(Muted("160 kbps é o ideal para a maioria. Ao transmitir, a plataforma limita (Twitch até 160 kbps). Se desmarcar os dois, o vídeo sai sem áudio.", 11));
        RefreshAudioDevices();
    }

    private void BuildCamera()
    {
        var on = Check("Mostrar a minha câmera no vídeo", "Aparece num canto da gravação, do replay e da transmissão, como nas lives.");
        on.IsChecked = _s.StudioCameraOn;
        on.Click += (_, _) => { _s.StudioCameraOn = on.IsChecked == true; UpdateGhost(); };
        _tabCamera.Children.Add(on);

        var pick = new StackPanel();
        _cameraPick.Children.Clear();
        pick.Children.Add(_cameraPick);
        var find = SmallButton("Procurar câmeras", async (_, _) => await FindCamerasAsync());
        find.Margin = new Thickness(0, 2, 0, 4);
        find.HorizontalAlignment = HorizontalAlignment.Left;
        pick.Children.Add(find);
        _tabCamera.Children.Add(Field("Câmera", pick));

        _tabCamera.Children.Add(Field("Posição", Chips(CornerNames, Math.Clamp(_s.StudioCameraCorner, 0, 3), i => { _s.StudioCameraCorner = i; UpdateGhost(); })));
        _tabCamera.Children.Add(Field("Tamanho", Chips(CameraPercents.Select(p => p + "% da largura").ToList(),
            Math.Max(0, Array.IndexOf(CameraPercents, _s.StudioCameraPercent)), i => { _s.StudioCameraPercent = CameraPercents[i]; UpdateGhost(); })));
        var frame = Check("Moldura amarela em volta da câmera");
        frame.IsChecked = _s.StudioCameraFrame;
        frame.Click += (_, _) => _s.StudioCameraFrame = frame.IsChecked == true;
        _tabCamera.Children.Add(Field("Moldura", frame));
        _cameraNote.Foreground = Brush("MutedBrush");
        _tabCamera.Children.Add(_cameraNote);
        _cameraNote.Text = "O retângulo \"Câmera\" na pré-visualização mostra onde ela vai ficar. A câmera precisa estar livre (feche outros programas que a usam). Para usar uma câmera virtual (OBS, celular), ela aparece na lista como qualquer outra.";
        RefreshCameraChips();
    }

    private void RefreshCameraChips()
    {
        _cameraPick.Children.Clear();
        if (_cameraList.Count == 0)
        {
            _cameraPick.Children.Add(Muted(_ffmpegPath is null ? "Instale o FFmpeg (aba Avançado) para procurar câmeras." : "Clique em \"Procurar câmeras\".", 12));
            return;
        }

        int selected = Math.Max(0, _cameraList.ToList().IndexOf(_s.StudioCameraName ?? ""));
        if (_s.StudioCameraName is null || !_cameraList.Contains(_s.StudioCameraName)) _s.StudioCameraName = _cameraList[selected];
        _cameraPick.Children.Add(Chips(_cameraList.ToList(), selected, i => _s.StudioCameraName = _cameraList[i]));
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
        RefreshCameraChips();
        SetStatus(_cameraList.Count == 0 ? "Nenhuma câmera encontrada. Conecte uma e tente de novo." : $"{_cameraList.Count} câmera(s) encontrada(s).", _cameraList.Count == 0 ? "WarnBrush" : "OkBrush");
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
        _chatId.Text = _s.StudioChatId;
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

    /// <summary>Atualiza o cartão "padrões da plataforma" e o resumo do que será enviado, com as configurações atuais.</summary>
    private void UpdatePlan()
    {
        var service = CurrentService();
        var spec = PlatformSpecs.For(service);
        int height = StudioCommand.FitOutput(CurrentArea().Width, CurrentArea().Height, Resolutions[Math.Clamp(_s.StudioResolution, 0, Resolutions.Length - 1)].Height).Height;
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
        RefreshCameraChips();
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

        // ---- origem
        CaptureSource? source;
        string? game = null;
        var displays = _displayList.Count > 0 ? _displayList : new[] { new DisplayInfo(0, "", new PixelRect(0, 0, ScreenInfo.Primary.Width, ScreenInfo.Primary.Height), true) };
        if (_sourceTab == 0)
        {
            int index = Math.Clamp(_displayIndex, 0, displays.Count - 1);
            source = CaptureRules.ForDisplay(displays[index]);
        }
        else
        {
            if (_windows.Selected < 0 || _windows.Selected >= _windowList.Count)
            {
                SetStatus("Escolha a janela que será capturada.", "WarnBrush");
                return;
            }

            var chosen = _windowList[_windows.Selected];
            var current = await Task.Run(() => CaptureCatalog.Windows().FirstOrDefault(w => w.Handle == chosen.Handle));
            if (current is null)
            {
                SetStatus("A janela escolhida foi fechada ou minimizada. Atualize a lista.", "WarnBrush");
                RefreshWindows();
                return;
            }

            game = current.ProcessName;
            source = CaptureRules.ForWindow(current, displays, gdi: _windowMode == 1);
            if (source is null)
            {
                SetStatus("A janela está fora da área visível das telas.", "WarnBrush");
                return;
            }
        }

        // ---- codificador
        var encoder = _encoders[Math.Min(_encoderIndex, _encoders.Count - 1)];

        // ---- transmissão
        StreamTarget? target = null;
        if (_outStream)
        {
            var h264 = encoder.Codec == VideoCodec.H264 ? encoder : _encoders.FirstOrDefault(e => e.Codec == VideoCodec.H264);
            if (h264 is null)
            {
                SetStatus("A transmissão exige um codificador H.264, que não foi encontrado na sua placa.", "BadBrush");
                return;
            }
            encoder = h264;

            target = new StreamTarget((StreamService)Math.Clamp(_s.StudioStreamService, 0, 3), _customUrl.Text, _key.Password);
            // se a chave foi colada dentro do endereço, o endereço não é gravado
            if (target.PathSecret is null) _s.StudioCustomUrl = target.CustomUrl;
            if (MessageBox.Show($"Começar a transmitir ao vivo para {target.DisplayName}?\n\nQuem estiver assistindo verá exatamente o que está sendo capturado.",
                    "Transmissão ao vivo", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        }

        // ---- câmera
        CameraOverlay? camera = null;
        if (_s.StudioCameraOn)
        {
            if (!CameraOverlay.IsValidName(_s.StudioCameraName))
            {
                SetStatus("Escolha a câmera na aba Câmera (clique em Procurar câmeras) ou desligue a opção.", "WarnBrush");
                return;
            }
            camera = new CameraOverlay(_s.StudioCameraName!, (CameraCorner)Math.Clamp(_s.StudioCameraCorner, 0, 3), _s.StudioCameraPercent, _s.StudioCameraFrame);
        }

        // ---- áudio
        AudioConfig? audio = null;
        if (_s.StudioSystemAudio || _s.StudioMicAudio)
        {
            _s.StudioOutputId = _outputs.Selected > 0 && _outputs.Selected - 1 < _outList.Count ? _outList[_outputs.Selected - 1].Id : null;
            _s.StudioMicId = _mics.Selected > 0 && _mics.Selected - 1 < _micList.Count ? _micList[_mics.Selected - 1].Id : null;
            audio = new AudioConfig(_s.StudioSystemAudio, _s.StudioOutputId, (float)_s.StudioSystemVolume,
                _s.StudioMicAudio, _s.StudioMicId, (float)_s.StudioMicVolume);
        }

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

        var config = new StudioConfig(source, game, Resolutions[Math.Clamp(_s.StudioResolution, 0, Resolutions.Length - 1)].Height,
            _s.StudioFps, encoder, _s.StudioBitrateMbps, _outRecord, _s.StudioMp4, target, _outReplay, _s.StudioReplaySeconds, _s.StudioReplayBeep, audio,
            _s.StudioStreamCapMbps, _s.StudioAudioKbps, (EncoderQuality)Math.Clamp(_s.StudioEncoderQuality, 0, 2), camera);

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
        _config.IsEnabled = !running;
        _live.Visibility = Vis(_studio.IsRunning);
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
