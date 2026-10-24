using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MBExtreme.Core.Recording;
using MBExtreme.UI.Recording;

namespace MBExtreme.UI.Pages;

/// <summary>Aba "Vertical": vídeo para Shorts, Reels e TikTok com a tela dividida em duas partes e legendas automáticas em tempo real.</summary>
public sealed partial class StudioPage
{
    private readonly StackPanel _tabVertical = new();
    private readonly Border _verticalMock = new() { Width = 150, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 8), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(2) };
    private readonly TextBlock _speechStatus = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly ProgressBar _speechProgress = new() { Height = 6, Minimum = 0, Maximum = 1, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 4) };
    private readonly TextBlock _captionNow = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private CancellationTokenSource? _speechDownload;

    private static readonly int[] TopPercents = { 35, 45, 55, 65, 75 };
    private static readonly int[] Zooms = { 100, 150, 200, 250 };

    /// <summary>Lê e grava os dados de uma das partes (cima ou baixo) nas configurações.</summary>
    private sealed class PaneRef
    {
        private readonly MBExtreme.Core.Settings.AppSettings _s;
        private readonly bool _top;

        public PaneRef(MBExtreme.Core.Settings.AppSettings s, bool top) { _s = s; _top = top; }

        public int Kind { get => _top ? _s.StudioVerticalTopKind : _s.StudioVerticalBottomKind; set { if (_top) _s.StudioVerticalTopKind = value; else _s.StudioVerticalBottomKind = value; } }

        public int Fit { get => _top ? _s.StudioVerticalTopFit : _s.StudioVerticalBottomFit; set { if (_top) _s.StudioVerticalTopFit = value; else _s.StudioVerticalBottomFit = value; } }

        public int FocusX { get => _top ? _s.StudioVerticalTopFocusX : _s.StudioVerticalBottomFocusX; set { if (_top) _s.StudioVerticalTopFocusX = value; else _s.StudioVerticalBottomFocusX = value; } }

        public int FocusY { get => _top ? _s.StudioVerticalTopFocusY : _s.StudioVerticalBottomFocusY; set { if (_top) _s.StudioVerticalTopFocusY = value; else _s.StudioVerticalBottomFocusY = value; } }

        public int Zoom { get => _top ? _s.StudioVerticalTopZoom : _s.StudioVerticalBottomZoom; set { if (_top) _s.StudioVerticalTopZoom = value; else _s.StudioVerticalBottomZoom = value; } }

        public string? Source { get => _top ? _s.StudioVerticalTopSource : _s.StudioVerticalBottomSource; set { if (_top) _s.StudioVerticalTopSource = value; else _s.StudioVerticalBottomSource = value; } }

        public VerticalPane ToPane() => new((PaneKind)Math.Clamp(Kind, 0, 2), Fit == 1 ? PaneFit.Fit : PaneFit.Fill, FocusX, FocusY, Zoom, Source);
    }

    /// <summary>Quadro vertical escolhido, ou null com o modo desligado.</summary>
    private VerticalLayout? CurrentVertical() => _s.StudioVerticalOn ? CurrentVerticalLayout() : null;

    /// <summary>Cópia vertical gravada junto com o vídeo normal (só com a gravação ligada e o modo vertical principal desligado).</summary>
    private VerticalLayout? CurrentVerticalCopy() => !_s.StudioVerticalOn && _s.StudioVerticalCopy && _outRecord ? CurrentVerticalLayout() : null;

    private VerticalLayout CurrentVerticalLayout()
    {
        var f = VerticalFormat.ByIndex(_s.StudioVerticalFormat);
        return new VerticalLayout(f.Width, f.Height, _s.StudioVerticalSplit, _s.StudioVerticalTopPercent, new PaneRef(_s, true).ToPane(), new PaneRef(_s, false).ToPane(), _s.StudioVerticalDivider);
    }

    private CaptionConfig? CurrentCaptions() => _s.StudioCaptionOn
        ? new CaptionConfig(_s.StudioCaptionSource == 0, new[] { 4, 6, 8 }[Math.Clamp(_s.StudioCaptionSizeIndex, 0, 2)], (CaptionPosition)Math.Clamp(_s.StudioCaptionPosition, 0, 3),
            (CaptionColor)Math.Clamp(_s.StudioCaptionColor, 0, 2), _s.StudioCaptionUpper)
        : null;

    private void BuildVertical()
    {
        _tabVertical.Children.Clear();
        foreach (var box in new FrameworkElement[] { _verticalMock, _speechStatus, _speechProgress, _captionNow }) Detach(box);

        _tabVertical.Children.Add(Muted("Grava, transmite e salva o replay já no formato vertical (Shorts, Reels, TikTok): a tela é montada em tempo real, com o mesmo áudio, câmeras, filtros, canal e Pix das outras abas. Sem edição depois.", 11));
        var on = new CheckBox { Content = "Usar vídeo vertical", IsChecked = _s.StudioVerticalOn, FontWeight = FontWeights.SemiBold, Foreground = Brush("TextBrush"), Margin = new Thickness(0, 6, 0, 4) };
        on.Click += (_, _) => { _s.StudioVerticalOn = on.IsChecked == true; BuildVertical(); UpdatePlan(); };
        _tabVertical.Children.Add(on);

        var copy = new CheckBox { Content = "Gravar também uma cópia vertical, ao mesmo tempo que o vídeo normal", IsChecked = _s.StudioVerticalCopy, IsEnabled = !_s.StudioVerticalOn, Foreground = Brush("TextBrush"), Margin = new Thickness(0, 2, 0, 2) };
        copy.Click += (_, _) => { _s.StudioVerticalCopy = copy.IsChecked == true; BuildVertical(); };
        _tabVertical.Children.Add(copy);
        _tabVertical.Children.Add(Muted(_s.StudioVerticalOn
            ? "A cópia vale quando o vídeo principal é o normal (horizontal)."
            : "Você grava normalmente e, no mesmo momento, sai um segundo arquivo vertical (\"-vertical\") com o enquadramento escolhido abaixo, sem editar depois. Só vale com a gravação ligada; usa mais a placa de vídeo (dois codificadores) e o replay e a transmissão continuam no vídeo normal.", 11));

        if (_s.StudioVerticalOn || _s.StudioVerticalCopy)
        {
            var formats = VerticalFormat.All;
            int fi = Math.Clamp(_s.StudioVerticalFormat, 0, formats.Count - 1);
            _tabVertical.Children.Add(Field("Formato", Chips(formats.Select(f => f.Name).ToList(), fi, i => { _s.StudioVerticalFormat = i; BuildVertical(); UpdatePlan(); }), 110));
            _tabVertical.Children.Add(Field("", Muted(formats[fi].Note + " A resolução da aba Vídeo não vale no modo vertical. Na transmissão, a plataforma limita o tamanho (ex.: Twitch até 1080 de largura).", 11), 110));

            var split = new CheckBox { Content = "Dividir a tela em duas partes (uma em cima e outra embaixo)", IsChecked = _s.StudioVerticalSplit, Foreground = Brush("TextBrush"), Margin = new Thickness(0, 4, 0, 4) };
            split.Click += (_, _) => { _s.StudioVerticalSplit = split.IsChecked == true; BuildVertical(); };
            _tabVertical.Children.Add(split);
            if (_s.StudioVerticalSplit)
            {
                _tabVertical.Children.Add(Field("Parte de cima", Chips(TopPercents.Select(p => p + "%").ToList(), Math.Max(0, Array.IndexOf(TopPercents, _s.StudioVerticalTopPercent)), i => { _s.StudioVerticalTopPercent = TopPercents[i]; RefreshVerticalMock(); }), 110));
                var divider = new CheckBox { Content = "Linha amarela entre as duas partes", IsChecked = _s.StudioVerticalDivider, Foreground = Brush("TextBrush") };
                divider.Click += (_, _) => { _s.StudioVerticalDivider = divider.IsChecked == true; RefreshVerticalMock(); };
                _tabVertical.Children.Add(Field("Divisória", divider, 110));
            }

            _tabVertical.Children.Add(PaneCard(_s.StudioVerticalSplit ? "Parte de cima" : "Imagem", new PaneRef(_s, true)));
            if (_s.StudioVerticalSplit) _tabVertical.Children.Add(PaneCard("Parte de baixo", new PaneRef(_s, false)));
            _tabVertical.Children.Add(Muted("Como vai ficar (esquema):", 11));
            _tabVertical.Children.Add(_verticalMock);
            RefreshVerticalMock();
        }

        BuildCaptionSection();
    }

    private Border PaneCard(string title, PaneRef pane)
    {
        var card = new StackPanel();
        card.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Foreground = Brush("TextBrush"), Margin = new Thickness(0, 0, 0, 4) });
        card.Children.Add(Field("Conteúdo", Chips(new[] { "Tela ou janela", "Câmera", "Imagem ou vídeo" }, Math.Clamp(pane.Kind, 0, 2), i => { pane.Kind = i; BuildVertical(); }), 110));
        switch (pane.Kind)
        {
            case 1:
                card.Children.Add(Field("Câmera", PaneCameraChoice(pane), 110));
                break;
            case 2:
                var row = new WrapPanel();
                var pick = SmallButton("Escolher arquivo...", (_, _) => PickPaneFile(pane));
                pick.Margin = new Thickness(0, 0, 8, 4);
                row.Children.Add(pick);
                row.Children.Add(Muted(string.IsNullOrEmpty(pane.Source) ? "Nenhum arquivo. Vídeo curto repete sem parar (ex.: gameplay de fundo)." : Path.GetFileName(pane.Source), 11));
                card.Children.Add(Field("Arquivo", row, 110));
                break;
        }

        card.Children.Add(Field("Enquadramento", Chips(new[] { "Preencher (corta as bordas)", "Inteira, com fundo desfocado" }, Math.Clamp(pane.Fit, 0, 1), i => { pane.Fit = i; BuildVertical(); }), 110));
        if (pane.Fit == 0)
        {
            card.Children.Add(Field("Lado", Chips(new[] { "Esquerda", "Centro", "Direita" }, pane.FocusX < 25 ? 0 : pane.FocusX > 75 ? 2 : 1, i => { pane.FocusX = new[] { 0, 50, 100 }[i]; }), 110));
            card.Children.Add(Field("Altura", Chips(new[] { "Em cima", "Meio", "Embaixo" }, pane.FocusY < 25 ? 0 : pane.FocusY > 75 ? 2 : 1, i => { pane.FocusY = new[] { 0, 50, 100 }[i]; }), 110));
            card.Children.Add(Field("Aproximar", Chips(Zooms.Select(z => z + "%").ToList(), Math.Max(0, Array.IndexOf(Zooms, pane.Zoom)), i => pane.Zoom = Zooms[i]), 110));
        }

        var border = CardOf(card);
        border.Padding = new Thickness(12, 8, 12, 6);
        return border;
    }

    private UIElement PaneCameraChoice(PaneRef pane)
    {
        if (_cameraList.Count == 0)
        {
            var row = new WrapPanel();
            var find = SmallButton("Procurar câmeras", async (_, _) => { await FindCamerasAsync(); BuildVertical(); });
            find.Margin = new Thickness(0, 0, 8, 4);
            row.Children.Add(find);
            row.Children.Add(Muted(pane.Source is { Length: > 0 } ? pane.Source : _ffmpegPath is null ? "Instale o FFmpeg (aba Avançado)." : "Nenhuma câmera listada ainda.", 11));
            return row;
        }

        var names = _cameraList.ToList();
        if (pane.Source is { Length: > 0 } saved && !names.Contains(saved)) names.Insert(0, saved + " (não encontrada)");
        int selected = pane.Source is null ? -1 : names.FindIndex(n => n == pane.Source || n == pane.Source + " (não encontrada)");
        if (selected < 0) { pane.Source = names[0]; selected = 0; }
        return Chips(names, selected, i => pane.Source = names[i].Replace(" (não encontrada)", ""));
    }

    private void PickPaneFile(PaneRef pane)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Imagem ou vídeo (*.png;*.jpg;*.gif;*.mp4;*.mov;*.mkv;*.webm)|*.png;*.jpg;*.jpeg;*.gif;*.mp4;*.mov;*.mkv;*.webm",
            Title = "Escolha a imagem ou o vídeo",
        };
        if (dialog.ShowDialog() != true) return;
        if (!VerticalPane.IsValidFile(dialog.FileName))
        {
            SetStatus("Esse arquivo tem caracteres que o FFmpeg não aceita no nome. Renomeie ou copie para outra pasta.", "WarnBrush");
            return;
        }
        pane.Source = dialog.FileName;
        BuildVertical();
    }

    /// <summary>Desenho simples das duas partes na proporção do formato escolhido.</summary>
    private void RefreshVerticalMock()
    {
        var layout = _s.StudioVerticalOn || _s.StudioVerticalCopy ? CurrentVerticalLayout() : null;
        if (layout is null) return;
        _verticalMock.BorderBrush = Brush("AccentBrush");
        _verticalMock.Background = Brush("BgBrush");
        _verticalMock.Height = _verticalMock.Width * layout.Height / layout.Width;
        string Name(VerticalPane p) => p.Kind switch { PaneKind.Camera => "Câmera", PaneKind.File => "Arquivo", _ => "Tela" } + (p.Fit == PaneFit.Fit && p.Kind == PaneKind.Screen ? "\n(inteira)" : "");
        var grid = new Grid { ClipToBounds = true };
        double top = layout.Split ? layout.TopHeight / (double)layout.Height : 1;
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(top, GridUnitType.Star) });
        if (layout.Split) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1 - top, GridUnitType.Star) });
        Panel(grid, 0, Name(layout.Top), layout.Top.Kind);
        if (layout.Split)
        {
            Panel(grid, 1, Name(layout.Bottom), layout.Bottom.Kind);
            if (layout.Divider) grid.Children.Add(new Border { Height = 3, Background = Brush("AccentBrush"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, _verticalMock.Height * top - 1.5, 0, 0) });
        }
        if (_s.StudioCaptionOn)
        {
            double y = _s.StudioCaptionPosition switch { 1 => 0.5, 2 => 0.14, 3 => layout.Split ? top : 0.5, _ => 0.74 };
            grid.Children.Add(new TextBlock { Text = "legenda ao vivo", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, _verticalMock.Height * y - 7, 0, 0) });
        }
        _verticalMock.Child = grid;
    }

    private void Panel(Grid grid, int row, string text, PaneKind kind)
    {
        var cell = new Border
        {
            Background = kind switch { PaneKind.Camera => new SolidColorBrush(Color.FromRgb(0x2A, 0x3A, 0x55)), PaneKind.File => new SolidColorBrush(Color.FromRgb(0x3A, 0x2A, 0x4A)), _ => new SolidColorBrush(Color.FromRgb(0x25, 0x40, 0x2F)) },
            Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 11, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        Grid.SetRow(cell, row);
        grid.Children.Add(cell);
    }

    // ---------------------------------------------------------------- legendas

    private void BuildCaptionSection()
    {
        _tabVertical.Children.Add(Heading("Legendas automáticas em tempo real"));
        _tabVertical.Children.Add(Muted("O que você fala vira legenda no próprio vídeo (na gravação, na transmissão e no replay), sem editar depois. O reconhecimento de voz roda no seu PC, sem internet e sem enviar o áudio para ninguém; em português, funciona melhor com voz clara e microfone próximo.", 11));
        var on = new CheckBox { Content = "Colocar legendas no vídeo", IsChecked = _s.StudioCaptionOn, FontWeight = FontWeights.SemiBold, Foreground = Brush("TextBrush"), Margin = new Thickness(0, 6, 0, 4) };
        on.Click += (_, _) => { _s.StudioCaptionOn = on.IsChecked == true; BuildVertical(); };
        _tabVertical.Children.Add(on);
        if (!_s.StudioCaptionOn) return;

        _tabVertical.Children.Add(Field("Quem fala", Chips(new[] { "Meu microfone", "Som do PC (vídeos, convidados)" }, Math.Clamp(_s.StudioCaptionSource, 0, 1), i => _s.StudioCaptionSource = i), 110));
        _tabVertical.Children.Add(Field("Tamanho", Chips(new[] { "Pequena", "Média", "Grande" }, Math.Clamp(_s.StudioCaptionSizeIndex, 0, 2), i => _s.StudioCaptionSizeIndex = i), 110));
        _tabVertical.Children.Add(Field("Posição", Chips(new[] { "Embaixo", "No meio", "Em cima", "Na divisória" }, Math.Clamp(_s.StudioCaptionPosition, 0, 3), i => { _s.StudioCaptionPosition = i; RefreshVerticalMock(); }), 110));
        _tabVertical.Children.Add(Field("Cor", Chips(new[] { "Branca", "Amarela", "Verde" }, Math.Clamp(_s.StudioCaptionColor, 0, 2), i => _s.StudioCaptionColor = i), 110));
        var upper = new CheckBox { Content = "TUDO EM MAIÚSCULAS (estilo Shorts)", IsChecked = _s.StudioCaptionUpper, Foreground = Brush("TextBrush") };
        upper.Click += (_, _) => _s.StudioCaptionUpper = upper.IsChecked == true;
        _tabVertical.Children.Add(Field("Letras", upper, 110));

        var model = new StackPanel();
        model.Children.Add(_speechStatus);
        model.Children.Add(_speechProgress);
        var download = SmallButton("Baixar o modelo de voz (cerca de 32 MB)", async (_, _) => await DownloadSpeechAsync());
        download.Visibility = Vis(!SpeechInstaller.IsInstalled);
        download.HorizontalAlignment = HorizontalAlignment.Left;
        model.Children.Add(download);
        _tabVertical.Children.Add(Field("Modelo de voz", model, 110));
        RefreshSpeechStatus();

        _tabVertical.Children.Add(Field("No ar agora", _captionNow, 110));
        _tabVertical.Children.Add(Muted("A legenda acompanha o que você diz com atraso de cerca de um segundo e some depois de alguns segundos de silêncio. Dá para ligar e desligar com o estúdio no ar (a captura reinicia por alguns segundos).", 11));
    }

    private void RefreshSpeechStatus()
    {
        bool ok = SpeechInstaller.IsInstalled;
        _speechStatus.Text = ok ? "Modelo de voz em português instalado." : "O modelo de voz em português ainda não foi baixado (uma vez só, depois funciona sem internet).";
        _speechStatus.Foreground = Brush(ok ? "OkBrush" : "WarnBrush");
    }

    private async Task DownloadSpeechAsync()
    {
        if (_speechDownload is not null) return;
        _speechDownload = new CancellationTokenSource();
        _speechProgress.Visibility = Visibility.Visible;
        _speechStatus.Text = "Baixando o modelo de voz...";
        _speechStatus.Foreground = Brush("MutedBrush");
        try
        {
            await SpeechInstaller.DownloadAsync(new Progress<double>(p => _speechProgress.Value = p), _speechDownload.Token);
            SetStatus("Modelo de voz instalado. As legendas já podem ser usadas.", "OkBrush");
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or IOException or InvalidDataException or TaskCanceledException or UnauthorizedAccessException)
        {
            SetStatus("Não foi possível baixar o modelo de voz: " + ex.Message, "BadBrush");
        }
        finally
        {
            _speechDownload.Dispose();
            _speechDownload = null;
            BuildVertical();
        }
    }

    /// <summary>Chamado a cada atualização da tela: mostra o texto que está na legenda neste momento.</summary>
    private void RefreshCaptionNow()
    {
        if (_studio.CaptionNow is { } now)
            _captionNow.Text = string.Join("\n", new[] { now.Line1, now.Line2 }.Where(l => l.Trim().Length > 0)) is { Length: > 0 } text ? text : "(em silêncio)";
        else
            _captionNow.Text = "O estúdio não está com legendas ligadas.";
    }
}
