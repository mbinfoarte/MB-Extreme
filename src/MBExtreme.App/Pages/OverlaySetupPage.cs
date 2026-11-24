using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MBExtreme.UI.Overlay;

namespace MBExtreme.UI.Pages;

/// <summary>Contador na tela do jogo: onde aparece, o que mostra, estilos prontos e aparência (com prévia).</summary>
public sealed class OverlaySetupPage : ToolPageBase
{
    private static readonly (string Key, string Title)[] Items =
    {
        ("fps", "FPS"), ("low1", "1% pior"), ("low01", "0,1% pior"), ("frametime", "Tempo de quadro (ms)"), ("graph", "Gráfico do tempo de quadro"),
        ("cpu", "CPU % e °C"), ("gpu", "GPU % e °C"), ("gpuclock", "Clock da GPU"), ("vram", "Memória da placa de vídeo"), ("ram", "Memória do PC"),
        ("power", "Consumo (W)"), ("ping", "Ping"), ("clock", "Hora"),
    };

    private readonly Border _preview = new() { CornerRadius = new CornerRadius(12), Height = 300, ClipToBounds = true };
    private readonly Grid _previewGrid = new();

    public OverlaySetupPage() : base("Contador na tela do jogo",
        "Igual ao RivaTuner/Afterburner, mas por cima do jogo (sem injetar nada, sem risco com antitrapaça). Funciona em janela e em tela cheia sem bordas.")
    {
        var s = AppServices.Settings;
        AddButton(AppServices.Overlay.OverlayVisible ? "Esconder agora" : "Mostrar agora", (_, _) => { AppServices.Overlay.ToggleOverlay(); }, true);

        var left = new StackPanel();
        left.Children.Add(Heading("Prévia"));
        _preview.Background = new LinearGradientBrush(Color.FromRgb(0x21, 0x45, 0x2F), Color.FromRgb(0x0E, 0x1A, 0x12), 60);
        _preview.Child = _previewGrid;
        left.Children.Add(_preview);
        var styles = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var (name, items, horizontal) in new[]
        {
            ("Compacto", new[] { "fps", "low1", "frametime", "graph", "cpu", "gpu", "vram" }, false),
            ("Completo", Items.Select(i => i.Key).ToArray(), false),
            ("Só FPS", new[] { "fps" }, false),
            ("Barra horizontal", new[] { "fps", "low1", "cpu", "gpu", "ram" }, true),
        })
        {
            var b = SmallButton(name, (_, _) => { s.OverlayItems = items.ToList(); s.OverlayHorizontal = horizontal; Save(); MainWindow.Current?.Reopen("overlay-setup", $"Estilo {name} aplicado."); });
            b.Margin = new Thickness(0, 0, 8, 6);
            styles.Children.Add(b);
        }
        left.Children.Add(styles);

        var right = new StackPanel();
        right.Children.Add(Heading("Onde aparece"));
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Width = 150, HorizontalAlignment = HorizontalAlignment.Left };
        for (int i = 0; i < 9; i++)
        {
            int pos = i;
            var cell = new Button { Height = 30, Margin = new Thickness(0, 0, 6, 6), Style = (Style)FindResource(s.OverlayPosition == i ? "PrimaryButton" : "GhostButton"), ToolTip = PositionName(i) };
            cell.Click += (_, _) => { s.OverlayPosition = pos; Save(); MainWindow.Current?.Reopen("overlay-setup", "Posição: " + PositionName(pos)); };
            grid.Children.Add(cell);
        }
        right.Children.Add(grid);

        right.Children.Add(Heading("O que mostrar"));
        var checks = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        foreach (var (key, title) in Items)
        {
            string k = key;
            var c = new CheckBox { Content = title, IsChecked = s.OverlayItems.Contains(key), Foreground = Brush("TextBrush"), Margin = new Thickness(0, 0, 8, 6) };
            c.Click += (_, _) => { if (c.IsChecked == true) { if (!s.OverlayItems.Contains(k)) s.OverlayItems.Add(k); } else s.OverlayItems.Remove(k); Save(); };
            checks.Children.Add(c);
        }
        right.Children.Add(checks);

        right.Children.Add(Heading("Aparência"));
        right.Children.Add(PadUi.SliderRow("Tamanho", 60, 180, s.OverlayScale, v => $"{v:0}%", v => { s.OverlayScale = (int)v; Save(); }));
        right.Children.Add(PadUi.SliderRow("Fundo", 0, 100, s.OverlayOpacity, v => $"{v:0}%", v => { s.OverlayOpacity = (int)v; Save(); }));
        var colors = new WrapPanel { Margin = new Thickness(0, 4, 0, 6) };
        colors.Children.Add(new TextBlock { Text = "Cor do texto  ", VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5 });
        foreach (string hex in new[] { "#FFFFFF", "#FFC400", "#3DDC97", "#6CB6FF", "#FF5A5F" })
        {
            string h = hex;
            var sw = new Button { Width = 24, Height = 24, Margin = new Thickness(0, 0, 8, 0), Background = new SolidColorBrush(FpsOverlayWindow.ParseColor(hex, Colors.White)), BorderThickness = new Thickness(s.OverlayColor == hex ? 2 : 0), BorderBrush = Brushes.White, ToolTip = hex };
            sw.Click += (_, _) => { s.OverlayColor = h; Save(); MainWindow.Current?.Reopen("overlay-setup"); };
            colors.Children.Add(sw);
        }
        right.Children.Add(colors);
        var fpsColors = new CheckBox { Content = "Cor do FPS pela faixa (verde 60+, amarelo 30+, vermelho)", IsChecked = s.OverlayFpsColors, Foreground = Brush("TextBrush") };
        fpsColors.Click += (_, _) => { s.OverlayFpsColors = fpsColors.IsChecked == true; Save(); };
        right.Children.Add(fpsColors);
        right.Children.Add(Muted("Atalho em qualquer jogo: Ctrl + Shift + F10 mostra e esconde. Em tela cheia exclusiva o Windows pode esconder o contador; use \"tela cheia sem bordas\" no jogo.", 11.5));

        var cols = new Grid();
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cols.Children.Add(CardOf(left));
        var rc = CardOf(right);
        Grid.SetColumn(rc, 2);
        cols.Children.Add(rc);
        Body.Children.Add(cols);
        DrawPreview();
    }

    private static string PositionName(int i) => new[] { "canto de cima à esquerda", "meio de cima", "canto de cima à direita", "meio da esquerda", "centro", "meio da direita", "canto de baixo à esquerda", "meio de baixo", "canto de baixo à direita" }[i];

    private void Save()
    {
        AppServices.Settings.Save();
        AppServices.Overlay.Reapply();
        DrawPreview();
    }

    /// <summary>Prévia com valores de exemplo, no lugar e com a aparência escolhidos.</summary>
    private void DrawPreview()
    {
        var s = AppServices.Settings;
        var text = new SolidColorBrush(FpsOverlayWindow.ParseColor(s.OverlayColor, Colors.White));
        Panel body = s.OverlayHorizontal ? new WrapPanel() : new StackPanel();
        void Line(string label, string value, Color color)
        {
            var t = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 12, Margin = new Thickness(0, 1, s.OverlayHorizontal ? 12 : 0, 0) };
            t.Inlines.Add(new System.Windows.Documents.Run(label + " ") { Foreground = new SolidColorBrush(color), FontWeight = FontWeights.SemiBold });
            t.Inlines.Add(new System.Windows.Documents.Run(value) { Foreground = text });
            body.Children.Add(t);
        }
        var muted = Color.FromRgb(0xA1, 0xA1, 0xAA);
        if (s.OverlayItems.Contains("fps")) Line("FPS", "112", Color.FromRgb(0xFF, 0xC4, 0x00));
        if (s.OverlayItems.Contains("low1")) Line("1%", "58", muted);
        if (s.OverlayItems.Contains("low01")) Line("0,1%", "41", muted);
        if (s.OverlayItems.Contains("frametime")) Line("FT", "8,9 ms", muted);
        if (s.OverlayItems.Contains("cpu")) Line("CPU", "54% 71°C", Color.FromRgb(0x6C, 0xB6, 0xFF));
        if (s.OverlayItems.Contains("gpu")) Line("GPU", "97% 66°C", Color.FromRgb(0x3D, 0xDC, 0x97));
        if (s.OverlayItems.Contains("gpuclock")) Line("CLK", "2610 MHz", Color.FromRgb(0x3D, 0xDC, 0x97));
        if (s.OverlayItems.Contains("vram")) Line("VRAM", "7,1/12 GB", Color.FromRgb(0x3D, 0xDC, 0x97));
        if (s.OverlayItems.Contains("ram")) Line("RAM", "61%", Color.FromRgb(0xC0, 0x84, 0xFC));
        if (s.OverlayItems.Contains("power")) Line("W", "286", muted);
        if (s.OverlayItems.Contains("ping")) Line("PING", "12 ms", muted);
        if (s.OverlayItems.Contains("clock")) Line("", "21:40", muted);
        var card = new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 6, 10, 7), Margin = new Thickness(12), Child = body,
            Background = new SolidColorBrush(Color.FromArgb((byte)(s.OverlayOpacity * 255 / 100), 0, 0, 0)),
            LayoutTransform = new ScaleTransform(s.OverlayScale / 130.0, s.OverlayScale / 130.0),
            HorizontalAlignment = (s.OverlayPosition % 3) switch { 0 => HorizontalAlignment.Left, 1 => HorizontalAlignment.Center, _ => HorizontalAlignment.Right },
            VerticalAlignment = (s.OverlayPosition / 3) switch { 0 => VerticalAlignment.Top, 1 => VerticalAlignment.Center, _ => VerticalAlignment.Bottom },
        };
        _previewGrid.Children.Clear();
        _previewGrid.Children.Add(new TextBlock { Text = "imagem do jogo", Foreground = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        _previewGrid.Children.Add(card);
    }
}
