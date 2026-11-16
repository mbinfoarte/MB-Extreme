using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MBExtreme.Core.Display;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Monitor e Hz: cada monitor com as taxas que aceita na resolução atual. Troca num clique; se a imagem sumir, volta sozinha em 15 s.
/// Avisa quando o monitor está abaixo da taxa máxima (comum depois de formatar ou trocar cabo).
/// </summary>
public sealed class MonitorPage : ToolPageBase
{
    private readonly StackPanel _monitors = new();

    public MonitorPage() : base("Monitor e Hz", "Troque a taxa de atualização sem abrir o painel da placa de vídeo. Mais Hz = imagem mais fluida em jogos e no Windows.")
    {
        Body.Children.Add(_monitors);
        Body.Children.Add(Heading("Programa fluido"));
        var match = Check("Animações e gráficos do MB no Hz do monitor", "Em 144 ou 165 Hz o programa acompanha a tela (o padrão do Windows para programas é 60). Vale ao abrir o MB de novo.");
        match.IsChecked = AppServices.Settings.MatchMonitorHz;
        match.Click += (_, _) => { AppServices.Settings.MatchMonitorHz = match.IsChecked == true; AppServices.Settings.Save(); };
        Body.Children.Add(match);
        var animations = Check("Animações ligadas", "Desligue em PC mais fraco: menus e cartões aparecem na hora, sem efeito.");
        animations.IsChecked = AppServices.Settings.Animations;
        animations.Click += (_, _) => { AppServices.Settings.Animations = animations.IsChecked == true; AppServices.Settings.Save(); };
        Body.Children.Add(animations);
        Loaded += (_, _) => Fill();
    }

    private void Fill()
    {
        _monitors.Children.Clear();
        var list = NativeDisplay.Monitors();
        if (list.Count == 0)
        {
            _monitors.Children.Add(Muted("Não consegui ler os monitores agora."));
            return;
        }
        var grid = TileGrid(Array.Empty<UIElement>(), 2, 480);
        int n = 0;
        foreach (var mon in list)
        {
            n++;
            var m = mon;
            var shape = new Border
            {
                Width = m.Vertical ? 46 : 110, Height = m.Vertical ? 80 : 46, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(2),
                BorderBrush = Brush("AccentBrush"), Background = Brush("SurfaceBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0),
                Child = new TextBlock { Text = $"{m.CurrentHz} Hz", FontSize = 11, Foreground = Brush("AccentBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            var info = new StackPanel();
            info.Children.Add(new TextBlock { Text = $"Monitor {n}{(m.Primary ? " (principal)" : "")} · {m.Name}", FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            info.Children.Add(Muted($"{m.Width} × {m.Height}{(m.Vertical ? " · em pé" : "")}", 11.5));
            var chips = new WrapPanel { Margin = new Thickness(0, 6, 0, 4) };
            foreach (int hz in m.Rates.Count > 0 ? m.Rates : new[] { m.CurrentHz })
            {
                int rate = hz;
                var b = new Button { Content = $"{hz} Hz", Style = (Style)FindResource(Math.Abs(hz - m.CurrentHz) <= 1 ? "PrimaryButton" : "GhostButton"), Padding = new Thickness(10, 3, 10, 3), MinHeight = 28, FontSize = 12, Margin = new Thickness(0, 0, 6, 6) };
                b.Click += (_, _) => Change(m, rate);
                chips.Children.Add(b);
            }
            info.Children.Add(chips);
            info.Children.Add(m.BelowMax
                ? new TextBlock { Text = $"⚠ Aceita {m.MaxHz} Hz e está em {m.CurrentHz} Hz. Clique em {m.MaxHz} Hz para subir.", Foreground = Brush("WarnBrush"), FontSize = 12, TextWrapping = TextWrapping.Wrap }
                : new TextBlock { Text = "✓ Na taxa máxima do monitor", Foreground = Brush("OkBrush"), FontSize = 12 });
            var row = new DockPanel();
            DockPanel.SetDock(shape, Dock.Left);
            row.Children.Add(shape);
            row.Children.Add(info);
            grid.Children.Add(CardOf(row));
        }
        _monitors.Children.Add(grid);
        _monitors.Children.Add(Muted("Não aparece a taxa que o monitor promete? Confira o cabo (DisplayPort ou HDMI 2.0/2.1) e o driver da placa de vídeo.", 11.5));
    }

    /// <summary>Troca e pergunta se a imagem ficou boa; sem resposta em 15 s, volta para a taxa de antes.</summary>
    private void Change(MonitorRates m, int hz)
    {
        if (Math.Abs(hz - m.CurrentHz) <= 1) return;
        int old = m.CurrentHz;
        int code = NativeDisplay.SetHz(m.DeviceName, hz);
        if (code != 0)
        {
            SetStatus($"O Windows não aceitou {hz} Hz neste monitor (código {code}).", "BadBrush");
            return;
        }
        var window = new Window
        {
            Title = "Manter esta taxa?", Width = 380, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize, Topmost = true, Background = Brush("SurfaceBrush"), Owner = Window.GetWindow(this),
        };
        var panel = new StackPanel { Margin = new Thickness(18) };
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brush("TextBrush"), FontSize = 13 };
        panel.Children.Add(text);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        bool keep = false;
        var yes = new Button { Content = "Manter", Style = (Style)FindResource("PrimaryButton"), Padding = new Thickness(16, 5, 16, 5) };
        yes.Click += (_, _) => { keep = true; window.Close(); };
        var no = new Button { Content = "Voltar", Style = (Style)FindResource("GhostButton"), Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(8, 0, 0, 0) };
        no.Click += (_, _) => window.Close();
        buttons.Children.Add(yes);
        buttons.Children.Add(no);
        panel.Children.Add(buttons);
        window.Content = panel;
        int left = 15;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        void Paint() => text.Text = $"O monitor está em {hz} Hz. A imagem está boa?\n\nVolta para {old} Hz em {left} s se você não responder.";
        Paint();
        timer.Tick += (_, _) => { if (--left <= 0) window.Close(); else Paint(); };
        timer.Start();
        window.ShowDialog();
        timer.Stop();
        if (!keep)
        {
            NativeDisplay.SetHz(m.DeviceName, old);
            SetStatus($"Voltou para {old} Hz.");
        }
        else SetStatus($"Monitor em {hz} Hz.", "OkBrush");
        Fill();
    }
}
