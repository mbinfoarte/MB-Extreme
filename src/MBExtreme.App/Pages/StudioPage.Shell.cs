using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MBExtreme.Core.Recording;

namespace MBExtreme.UI.Pages;

/// <summary>
/// A moldura da tela do estúdio, igual à maquete aprovada: topo com título, REC e AO VIVO; a prévia (ou Prévia | Transição | No ar
/// no modo estúdio) com a linha de informações embaixo; e, na coluna da direita, o tempo da gravação.
/// </summary>
public sealed partial class StudioPage
{
    private readonly TextBlock _timerText = new() { FontSize = 28, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12), Visibility = Visibility.Collapsed };
    private readonly StackPanel _capturePanel = new();
    private readonly TextBlock _infoLine = new() { FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly Button _captureButton = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _badgeRec = new() { Visibility = Visibility.Collapsed, CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _badgeLive = new() { Visibility = Visibility.Collapsed, CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _previewLabel = new() { FontSize = 13, Margin = new Thickness(0, 0, 0, 6), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly StackPanel _transitionColumn = new() { Width = 140, Margin = new Thickness(12, 26, 12, 0), VerticalAlignment = VerticalAlignment.Top };
    private readonly Border _verticalWrap = new() { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 0, 0, 0) };
    private Grid? _views;
    private int _previewLayout = 2;
    private Window? _chatWindow;

    private FrameworkElement BuildTopBar()
    {
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 14), LastChildFill = true };
        var back = new Button
        {
            Content = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14 },
            Style = (Style)FindResource("GhostButton"), ToolTip = "Voltar", Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        back.Padding = new Thickness(12, 8, 12, 8);
        back.Click += (_, _) => MainWindow.Current?.GoBack();
        DockPanel.SetDock(back, Dock.Left);
        bar.Children.Add(back);

        var title = new TextBlock { Text = "Estúdio", FontSize = 24, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(title, Dock.Left);
        bar.Children.Add(title);

        var recRow = new StackPanel { Orientation = Orientation.Horizontal };
        recRow.Children.Add(new System.Windows.Shapes.Ellipse { Width = 10, Height = 10, Fill = Frozen(Color.FromRgb(0xFF, 0x4D, 0x4D)), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        recRow.Children.Add(new TextBlock { Text = "REC", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Frozen(Color.FromRgb(0xFF, 0x6B, 0x6B)) });
        _badgeRec.Background = Frozen(Color.FromRgb(0x3A, 0x12, 0x14));
        _badgeRec.Child = recRow;
        DockPanel.SetDock(_badgeRec, Dock.Left);
        bar.Children.Add(_badgeRec);

        _badgeLive.Background = Yellow;
        _badgeLive.Child = new TextBlock { Text = "AO VIVO", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Ink };
        DockPanel.SetDock(_badgeLive, Dock.Left);
        bar.Children.Add(_badgeLive);

        bar.Children.Add(new TextBlock { Text = "Tudo que você muda aqui vale na hora.", FontSize = 13, Foreground = Brush("MutedBrush"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
        return bar;
    }

    private void RefreshTopBar()
    {
        var request = _studio.IsRunning ? _studio.Session.Request : null;
        _badgeRec.Visibility = Vis(request?.RecordFile is not null || AppServices.Recorder.IsRecording);
        _badgeLive.Visibility = Vis(request?.Stream is not null);
        _timerText.Visibility = Vis(_studio.IsRunning || AppServices.Recorder.IsRecording);
    }

    // ---------------------------------------------------------------- prévia

    private FrameworkElement BuildPreviewArea()
    {
        var panel = new StackPanel();

        // cabeçalho: "Prévia", Horizontal / Vertical / Os dois e Modo estúdio
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var right = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(Chips(new[] { "Horizontal", "Vertical", "Os dois" }, _previewLayout, i => { _previewLayout = i; RefreshStudioLayout(); }));
        _studioMode.Margin = new Thickness(0, 2, 0, 5);
        _studioMode.Click += (_, _) =>
        {
            _s.StudioSceneStudioMode = !_s.StudioSceneStudioMode;
            _s.Save();
            RebuildScenesUi();
        };
        _studioMode.ToolTip = "Ligado: clicar numa cena só mostra ela na prévia para você ajeitar; ela vai ao ar quando você clicar em Transição. Desligado: a cena entra no ar na hora.";
        right.Children.Add(_studioMode);
        DockPanel.SetDock(right, Dock.Right);
        head.Children.Add(right);
        head.Children.Add(new TextBlock { Text = "Prévia", FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(head);

        // Prévia | Transição | No ar | Vertical
        _views = new Grid();
        _views.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _views.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _views.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _views.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var previewColumn = new StackPanel();
        _previewLabel.Foreground = Brush("MutedBrush");
        previewColumn.Children.Add(_previewLabel);
        _previewHost.HorizontalAlignment = HorizontalAlignment.Center;   // do tamanho da imagem: os retângulos das câmeras ficam nos cantos certos
        previewColumn.Children.Add(_previewHost);
        _views.Children.Add(previewColumn);

        _transition.Style = (Style)FindResource("PrimaryButton");
        _transition.HorizontalAlignment = HorizontalAlignment.Stretch;
        _transition.Margin = new Thickness(0, 0, 0, 8);
        _transition.Click += async (_, _) => await TransitionAsync();
        _transitionColumn.Children.Add(_transition);
        var kinds = Chips(new[] { "Cortar", "Esmaecer 300 ms" }, Math.Clamp(_s.StudioSceneTransition, 0, 1), i => { _s.StudioSceneTransition = i; _s.Save(); });
        kinds.Width = 140;
        _transitionColumn.Children.Add(kinds);
        var help = Help("Monte a cena na Prévia sem o público ver. Clique em Transição para ela ir ao ar. Cortar troca na hora; Esmaecer passa de uma para a outra aos poucos.");
        help.HorizontalAlignment = HorizontalAlignment.Center;
        _transitionColumn.Children.Add(help);
        Grid.SetColumn(_transitionColumn, 1);
        _views.Children.Add(_transitionColumn);

        var program = BuildProgramBox();
        Grid.SetColumn(program, 2);
        _views.Children.Add(program);

        _verticalLive.VerticalAlignment = VerticalAlignment.Top;
        _verticalWrap.Child = _verticalLive;
        Grid.SetColumn(_verticalWrap, 3);
        _views.Children.Add(_verticalWrap);
        panel.Children.Add(_views);

        // linha de informações (como na maquete) e, à direita, o que está sendo capturado
        var info = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        _captureButton.Style = (Style)FindResource("GhostButton");
        _captureButton.Padding = new Thickness(10, 3, 10, 3);
        _captureButton.Click += (_, _) => ShowEditor("O que capturar", _capturePanel, RefreshCaptureButton, 560);
        DockPanel.SetDock(_captureButton, Dock.Right);
        info.Children.Add(_captureButton);
        _infoLine.Foreground = Brush("MutedBrush");
        info.Children.Add(_infoLine);
        panel.Children.Add(info);

        panel.Children.Add(_previewNote);
        Detach(_health);
        Detach(_warnings);
        _health.FontSize = 12;
        _warnings.FontSize = 12;
        panel.Children.Add(_health);
        panel.Children.Add(_warnings);
        return panel;
    }

    /// <summary>Mostra a prévia sozinha ou, no modo estúdio, Prévia | Transição | No ar; e o vertical conforme Horizontal / Vertical / Os dois.</summary>
    private void RefreshStudioLayout()
    {
        if (_views is null) return;
        bool studio = _s.StudioSceneStudioMode;
        _views.ColumnDefinitions[2].Width = studio ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        _transitionColumn.Visibility = Vis(studio);
        _programBox.Visibility = Vis(studio);
        _previewLabel.Visibility = Vis(studio);
        _previewLabel.Inlines.Clear();
        _previewLabel.Inlines.Add(new System.Windows.Documents.Run("Prévia: "));
        _previewLabel.Inlines.Add(new System.Windows.Documents.Run(CurrentScene.Name) { FontWeight = FontWeights.SemiBold, Foreground = Brush("TextBrush") });
        _previewHost.Visibility = Vis(studio || _previewLayout != 1);
        _verticalWrap.Visibility = Vis(!studio && _previewLayout != 0);
        _transition.IsEnabled = _studio.IsRunning && _airScene != CurrentScene;
    }

    private void RefreshCaptureButton()
    {
        string what = _sourceTab == 0
            ? (_displayList.Count > 0 ? _displayList[Math.Clamp(_displayIndex, 0, _displayList.Count - 1)].Title : "Tela principal")
            : _windows.Selected >= 0 && _windows.Selected < _windowList.Count ? _windowList[_windows.Selected].Title : "escolha a janela";
        if (what.Length > 40) what = what[..40] + "…";
        _captureButton.Content = "Captura: " + what;
        _captureButton.ToolTip = "Trocar a tela ou a janela capturada";
    }

    /// <summary>Resolução, quadros, codificador e, com o estúdio ligado, a taxa e os quadros perdidos.</summary>
    private void RefreshInfoLine()
    {
        var session = _studio.Session;
        if (session.IsRunning && session.Request is { } r)
        {
            var p = session.Progress;
            var elapsed = DateTime.Now - session.StartedAt;
            _timerText.Text = $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
            _infoLine.Text = $"{r.OutputHeight}p · {p.Fps:0} fps      {r.Encoder.Title}      {p.BitrateKbps / 1000:0.0} Mbps      Quadros perdidos: {p.DroppedFrames}";
            return;
        }

        string res = Resolutions[Math.Clamp(_s.StudioResolution, 0, Resolutions.Length - 1)].Name;
        string encoder = _encoders.Count > 0 ? _encoders[Math.Min(_encoderIndex, _encoders.Count - 1)].Title : "codificador: detectar em Programa › Avançado";
        _infoLine.Text = $"{res} · {_s.StudioFps} fps      {encoder}";
        if (AppServices.Recorder.IsRecording)
        {
            var e = DateTime.Now - AppServices.Recorder.StartedAt;
            _timerText.Text = $"{(int)e.TotalHours:00}:{e.Minutes:00}:{e.Seconds:00}";
        }
    }

    // ---------------------------------------------------------------- chat e arquivos

    /// <summary>O chat ao vivo numa janela própria (dá para deixar ao lado do jogo ou em outro monitor).</summary>
    private void ShowChatWindow()
    {
        if (_chatPanel is null) return;
        if (_chatWindow is { } open) { open.Activate(); return; }
        Detach(_chatPanel);
        var host = new Border { Padding = new Thickness(12), Child = _chatPanel };
        _chatWindow = new Window
        {
            Title = "Chat ao vivo", Owner = Window.GetWindow(this), Width = 420, Height = 640, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("BgBrush"), Foreground = Brush("TextBrush"), Content = host,
        };
        _chatWindow.Closed += (_, _) => { host.Child = null; _chatWindow = null; };
        _chatWindow.Show();
    }

    private FrameworkElement BuildFilesHeader()
    {
        var head = new DockPanel { Margin = new Thickness(0, 22, 0, 6) };
        var open = SmallButton("Abrir pasta", (_, _) => OpenFolder());
        open.Padding = new Thickness(10, 3, 10, 3);
        open.FontSize = 12;
        DockPanel.SetDock(open, Dock.Right);
        head.Children.Add(open);
        head.Children.Add(new TextBlock { Text = "Gravações e replays", FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        return head;
    }
}
