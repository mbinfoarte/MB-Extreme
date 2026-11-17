using System.Windows;
using System.Windows.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>Monitor de FPS (estilo MSI Afterburner) com overlay sobre jogos em janela ou tela cheia sem borda.</summary>
public sealed class FpsPage : ToolPageBase
{
    private readonly TextBlock _big = new() { FontSize = 54, FontWeight = FontWeights.Bold, Text = "-- FPS" };
    private readonly TextBlock _detail = new() { FontSize = 15, TextWrapping = TextWrapping.Wrap };
    private readonly Button _monitor;
    private readonly Button _overlay;

    public FpsPage() : base("Monitor de FPS",
        "Mede os quadros por segundo do jogo que estiver rodando e mostra um painel por cima dele. Atalho para ligar/desligar o painel: Ctrl + Shift + F10. Funciona com jogos em janela ou tela cheia sem borda (DirectX); em tela cheia exclusiva o painel pode não aparecer, mas a medida continua no aplicativo.")
    {
        _monitor = AddButton("Iniciar monitor", (_, _) => ToggleMonitor());
        _overlay = AddButton("Mostrar painel", (_, _) => ToggleOverlay(), false);

        Body.Children.Add(Heading("Posição do painel"));
        Body.Children.Add(Choice(new[] { "Topo esquerdo", "Topo direito", "Base esquerda", "Base direita" }, AppServices.Overlay.Corner, i =>
        {
            AppServices.Overlay.Corner = i;
            if (AppServices.Overlay.OverlayVisible) AppServices.Overlay.ShowOverlay();
        }));

        var card = new StackPanel();
        card.Children.Add(_big);
        card.Children.Add(_detail);
        Body.Children.Add(Heading("Agora"));
        Body.Children.Add(CardOf(card));
        Body.Children.Add(Muted("Inicie o monitor e abra o jogo. Para gravar o desempenho, use Jogos > Gravação de tela."));

        Loaded += (_, _) =>
        {
            AppServices.Overlay.Changed += OnChanged;
            OnChanged(this, EventArgs.Empty);
        };
        Unloaded += (_, _) => AppServices.Overlay.Changed -= OnChanged;
    }

    private void ToggleMonitor()
    {
        try
        {
            if (AppServices.Overlay.IsMonitoring) AppServices.Overlay.StopMonitoring();
            else AppServices.Overlay.StartMonitoring();
            SetStatus("");
        }
        catch (Exception ex)
        {
            SetStatus("Não foi possível iniciar o monitor (precisa de administrador): " + ex.Message, "BadBrush");
        }
    }

    private void ToggleOverlay()
    {
        try
        {
            AppServices.Overlay.ToggleOverlay();
            SetStatus("");
        }
        catch (Exception ex)
        {
            SetStatus("Não foi possível mostrar o painel: " + ex.Message, "BadBrush");
        }
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        var overlay = AppServices.Overlay;
        _monitor.Content = overlay.IsMonitoring ? "Parar monitor" : "Iniciar monitor";
        _overlay.Content = overlay.OverlayVisible ? "Esconder painel" : "Mostrar painel";

        var reading = overlay.Last;
        if (reading is null)
        {
            _big.Text = "-- FPS";
            _detail.Text = overlay.IsMonitoring
                ? "Aguardando um jogo. Abra o jogo e mova a câmera."
                : "Monitor parado.";
            return;
        }

        _big.Text = $"{reading.Fps:0} FPS";
        _detail.Text = $"{overlay.LastProcess} · tempo de quadro {reading.FrameTimeMs:0.0} ms · 1% pior {reading.Low1PercentFps:0} FPS";
    }
}
