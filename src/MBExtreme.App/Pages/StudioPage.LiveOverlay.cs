using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MBExtreme.Core.Recording;
using MBExtreme.UI.Recording;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Logo do canal numa camada desenhada pelo programa: barra de tamanho em pixels, transparência, margem, 9 pontos ou arrastar na prévia.
/// Com o estúdio no ar a camada é redesenhada e entra no vídeo na hora (sem "Aplicar agora", sem reiniciar).
/// </summary>
public sealed partial class StudioPage
{
    private readonly DispatcherTimer _overlayTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Border _logoGhost = new() { BorderThickness = new Thickness(1.5), Cursor = Cursors.SizeAll, Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly Image _logoGhostImage = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private BitmapSource? _logoArt;
    private string? _logoArtKey, _overlaySignature;
    private bool _draggingLogo;
    private Point _dragOffset;

    private void InitLiveOverlay()
    {
        _studio.OverlayPainter = PaintOverlay;
        _logoGhost.BorderBrush = Brush("AccentBrush");
        _logoGhost.Child = _logoGhostImage;
        _logoGhost.ToolTip = "Arraste o logo para onde quiser. A live recebe a mudança na hora.";
        _logoGhost.MouseLeftButtonDown += (_, e) =>
        {
            _draggingLogo = true;
            _dragOffset = e.GetPosition(_logoGhost);
            _logoGhost.CaptureMouse();
            e.Handled = true;
        };
        _logoGhost.MouseMove += (_, e) =>
        {
            if (!_draggingLogo || _preview.ActualWidth < 10 || _preview.ActualHeight < 10) return;
            var p = e.GetPosition(_preview);
            double cx = (p.X - _dragOffset.X + _logoGhost.ActualWidth / 2) / _preview.ActualWidth;
            double cy = (p.Y - _dragOffset.Y + _logoGhost.ActualHeight / 2) / _preview.ActualHeight;
            _s.StudioLogoCenterX = Math.Round(Math.Clamp(cx, 0, 1), 4);
            _s.StudioLogoCenterY = Math.Round(Math.Clamp(cy, 0, 1), 4);
            if (_s.StudioLogoAnchor < 0) _s.StudioLogoAnchor = CurrentLogo().Anchor;
            UpdateLogoGhost();
        };
        _logoGhost.MouseLeftButtonUp += (_, _) =>
        {
            if (!_draggingLogo) return;
            _draggingLogo = false;
            _logoGhost.ReleaseMouseCapture();
            _s.Save();
        };
        System.Windows.Controls.Panel.SetZIndex(_logoGhost, 50);
        _previewHost.Children.Add(_logoGhost);
        _overlayTimer.Tick += (_, _) => OverlayTick();
        Loaded += (_, _) => { _studio.OverlayPainter = PaintOverlay; _overlayTimer.Start(); };
        Unloaded += (_, _) => _overlayTimer.Stop();
    }

    /// <summary>Onde e de que tamanho o logo fica agora (quem nunca mexeu continua com o canto e a % de antes).</summary>
    private LogoPlacement CurrentLogo() => _s.StudioLogoAnchor < 0
        ? LogoPlacement.FromCorner(_s.StudioLogoCorner, _s.StudioLogoPercent) with { OpacityPercent = _s.StudioLogoOpacity, MarginPx = _s.StudioLogoMarginPx }
        : new LogoPlacement(_s.StudioLogoAnchor, _s.StudioLogoWidthPx, _s.StudioLogoMarginPx, _s.StudioLogoOpacity, _s.StudioLogoCenterX, _s.StudioLogoCenterY);

    /// <summary>O logo aparece agora? Ligado e, com várias cenas, se a cena no ar mostra o logo.</summary>
    private bool LogoShownNow()
    {
        if (!_s.StudioLogoOn) return false;
        var scene = _airScene ?? CurrentScene;
        return scene is null || scene.Items.Count == 0 || scene.Items.Any(i => i.Kind == MBExtreme.Core.Settings.StudioSceneItemKind.Logo);
    }

    private BitmapSource? LogoArt()
    {
        string key = LayersKey();
        if (_logoArt is null || _logoArtKey != key)
        {
            try { _logoArt = OverlayArt.RenderLogo(Profile()); }
            catch (Exception ex) when (ex is System.IO.IOException or NotSupportedException or InvalidOperationException or ArgumentException) { _logoArt = null; }
            _logoArtKey = key;
        }
        return _logoArt;
    }

    /// <summary>Desenha a camada (só o logo, por enquanto) no tamanho do vídeo, em BGRA transparente.</summary>
    private byte[]? PaintOverlay(int width, int height)
    {
        var buffer = new byte[width * height * 4];
        if (!LogoShownNow() || LogoArt() is not { } art) return buffer;
        var place = CurrentLogo();
        var (x, y, w, h) = place.Rect(width, height, art.PixelWidth, art.PixelHeight);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushOpacity(place.Opacity);
            dc.DrawImage(art, new Rect(x, y, w, h));
            dc.Pop();
        }
        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.CopyPixels(buffer, width * 4, 0);
        LiveOverlayCanvas.Unpremultiply(buffer);
        return buffer;
    }

    private void OverlayTick()
    {
        UpdateLogoGhost();
        if (!_studio.IsRunning || _studio.Config is not { LiveOverlay: true }) return;
        var p = CurrentLogo();
        string sig = string.Join("|", LogoShownNow(), p, LayersKey());
        if (sig == _overlaySignature) return;
        _overlaySignature = sig;
        _studio.RefreshOverlay();
    }

    /// <summary>O logo de verdade por cima da prévia, no lugar e tamanho em que vai sair (arrastável).</summary>
    private void UpdateLogoGhost()
    {
        // no ar sem a prévia fluida, a prévia já é o vídeo final (com o logo): não desenha de novo por cima
        bool showsFinal = _studio.IsRunning && !_screenFeedShowing;
        bool show = _s.StudioLiveOverlay && !showsFinal && LogoShownNow() && _preview.Source is not null && _preview.ActualWidth > 40 && LogoArt() is not null;
        _logoGhost.Visibility = Vis(show);
        if (!show || _logoArt is not { } art) return;
        if (!ReferenceEquals(_logoGhostImage.Source, art)) _logoGhostImage.Source = art;
        int fw = (int)Math.Max(2, _preview.ActualWidth), fh = (int)Math.Max(2, _preview.ActualHeight);
        var place = CurrentLogo();
        // a prévia é menor que o vídeo: as medidas em pixels (de um vídeo 1920) encolhem na mesma proporção
        var (x, y, w, h) = place.Rect(fw, fh, art.PixelWidth, art.PixelHeight);
        _logoGhost.Width = w;
        _logoGhost.Height = h;
        _logoGhost.Margin = new Thickness(x, y, 0, 0);
        _logoGhostImage.Opacity = place.Opacity;
    }

    /// <summary>Editor do logo: tamanho em pixels, transparência, margem, os 9 pontos e voltar ao ponto depois de arrastar.</summary>
    private StackPanel LogoEditor()
    {
        var panel = new StackPanel();
        if (_s.StudioLogoAnchor < 0)
        {
            // primeira vez no editor novo: guarda o canto e o tamanho de antes como ponto e pixels
            var old = LogoPlacement.FromCorner(_s.StudioLogoCorner, _s.StudioLogoPercent);
            _s.StudioLogoAnchor = old.Anchor;
            _s.StudioLogoWidthPx = old.WidthPx;
        }
        panel.Children.Add(PxSlider("Tamanho", 40, 800, _s.StudioLogoWidthPx, "px de largura", v => _s.StudioLogoWidthPx = (int)v));
        panel.Children.Add(PxSlider("Transparência", 10, 100, _s.StudioLogoOpacity, "%", v => _s.StudioLogoOpacity = (int)v));
        panel.Children.Add(PxSlider("Margem da borda", 0, 200, _s.StudioLogoMarginPx, "px", v => _s.StudioLogoMarginPx = (int)v));

        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Width = 102, HorizontalAlignment = HorizontalAlignment.Left };
        var cells = new List<Border>();
        void Paint()
        {
            for (int i = 0; i < cells.Count; i++)
            {
                bool on = _s.StudioLogoCenterX is null && _s.StudioLogoAnchor == i;
                cells[i].Background = on ? Brush("AccentBrush") : Brush("SurfaceBrush");
            }
        }
        for (int i = 0; i < 9; i++)
        {
            int anchor = i;
            var cell = new Border { Height = 22, Margin = new Thickness(2), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), BorderBrush = Brush("BorderBrush"), Cursor = Cursors.Hand,
                ToolTip = new[] { "Em cima à esquerda", "Em cima no meio", "Em cima à direita", "No meio à esquerda", "Centro", "No meio à direita", "Embaixo à esquerda", "Embaixo no meio", "Embaixo à direita" }[i] };
            cell.MouseLeftButtonUp += (_, _) => { _s.StudioLogoAnchor = anchor; _s.StudioLogoCenterX = _s.StudioLogoCenterY = null; Paint(); UpdateLogoGhost(); _s.Save(); };
            cells.Add(cell);
            grid.Children.Add(cell);
        }
        Paint();
        var where = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        where.Children.Add(grid);
        where.Children.Add(new TextBlock
        {
            Text = "Escolha um dos 9 pontos ou arraste o logo direto na prévia: ele encosta na posição exata que você soltar.",
            TextWrapping = TextWrapping.Wrap, Width = 260, Margin = new Thickness(12, 0, 0, 0), FontSize = 11.5, Foreground = Brush("MutedBrush"), VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(Field("Posição", where, 140));
        return panel;
    }

    /// <summary>Barra com o valor ao lado, que salva e redesenha a prévia enquanto arrasta.</summary>
    private UIElement PxSlider(string label, double min, double max, double value, string unit, Action<double> changed)
    {
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        var text = new TextBlock { Width = 110, Text = $"{value:0} {unit}", VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("AccentBrush"), FontWeight = FontWeights.SemiBold };
        DockPanel.SetDock(text, Dock.Right);
        row.Children.Add(text);
        var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), TickFrequency = 1, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        System.Windows.Automation.AutomationProperties.SetName(slider, label);
        slider.ValueChanged += (_, e) => { text.Text = $"{e.NewValue:0} {unit}"; changed(e.NewValue); UpdateLogoGhost(); };
        slider.LostMouseCapture += (_, _) => _s.Save();
        row.Children.Add(slider);
        return Field(label, row, 140);
    }
}
