using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MBExtreme.Core.Recording;
using MBExtreme.Core.Settings;

namespace MBExtreme.UI.Pages;

/// <summary>A tela "No ar" (o vídeo final, como sai para a live), a escolha da tela ou janela de cada cena e as opções das cenas.</summary>
public sealed partial class StudioPage
{
    private readonly Image _programImage = new() { Stretch = Stretch.Uniform };
    private readonly Border _programBox = new() { Visibility = Visibility.Collapsed, Width = 340, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _programTitle = new() { FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private long _programShown;

    private FrameworkElement BuildProgramBox()
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        head.Children.Add(new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Fill = Brushes.Red, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(_programTitle);
        var panel = new StackPanel();
        panel.Children.Add(head);
        panel.Children.Add(new Border { CornerRadius = new CornerRadius(6), BorderBrush = Brushes.Red, BorderThickness = new Thickness(2), Child = _programImage, ClipToBounds = true });
        _programBox.Child = panel;
        _programBox.ToolTip = "O que está saindo agora na gravação e na live (imagem reduzida). A prévia ao lado mostra a cena escolhida.";
        return _programBox;
    }

    /// <summary>Mostra a última imagem do que está no ar (o FFmpeg manda umas 8 por segundo).</summary>
    private void RefreshProgramView()
    {
        var frame = _studio.IsRunning && _s.StudioProgramView ? _studio.Session.ProgramFrame : null;
        _programBox.Visibility = Vis(frame is not null);
        if (frame is not { } f || f.Number == _programShown) return;
        _programShown = f.Number;
        _programTitle.Text = "No ar" + (_airScene is { } air ? " · " + air.Name : "");
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(f.Jpeg);
            image.EndInit();
            image.Freeze();
            _programImage.Source = image;
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or InvalidOperationException) { }   // um quadro ruim: espera o próximo
    }

    // ---------------------------------------------------------------- tela ou janela de cada cena

    private FrameworkElement CaptureEditor(StudioSceneItem item)
    {
        var panel = new StackPanel();
        void Rebuild()
        {
            panel.Children.Clear();
            panel.Children.Add(Muted("Cada cena pode mostrar uma tela ou uma janela diferente. As outras fontes (câmeras, Pix, camadas) ficam por cima.", 11.5));
            panel.Children.Add(Field("Mostrar", Chips(new[] { "A escolha de baixo da prévia", "Uma tela", "Uma janela (jogos)", "Uma janela (conteúdo)" }, Math.Clamp(item.CaptureMode, 0, 3), i =>
            {
                item.CaptureMode = i;
                Rebuild();
            }), 90));

            if (item.CaptureMode == 1)
            {
                RefreshDisplays();
                var names = _displayList.Count > 0 ? _displayList.Select(d => d.Title).ToList() : new List<string> { "Tela principal" };
                item.Display = Math.Clamp(item.Display, 0, names.Count - 1);
                panel.Children.Add(Field("Tela", Chips(names, item.Display, i => item.Display = i), 90));
            }
            else if (item.CaptureMode is 2 or 3)
            {
                var windows = CaptureCatalog.Windows();
                _windowCache = (DateTime.UtcNow, windows);
                var list = new PickList(this, 220);
                int selected = windows.ToList().FindIndex(w => string.Equals(w.ProcessName, item.WindowProcess, StringComparison.OrdinalIgnoreCase) && w.Title == item.WindowTitle);
                if (selected < 0) selected = windows.ToList().FindIndex(w => string.Equals(w.ProcessName, item.WindowProcess, StringComparison.OrdinalIgnoreCase));
                list.SetItems(windows.Select(w => w.DisplayName).ToList(), selected);
                list.Picked += i =>
                {
                    if (i < 0 || i >= windows.Count) return;
                    item.WindowProcess = windows[i].ProcessName;
                    item.WindowTitle = windows[i].Title;
                };
                if (selected < 0 && windows.Count > 0 && item.WindowProcess is null) { item.WindowProcess = windows[0].ProcessName; item.WindowTitle = windows[0].Title; }
                var refresh = SmallButton("Atualizar a lista", (_, _) => Rebuild());
                refresh.HorizontalAlignment = HorizontalAlignment.Left;
                panel.Children.Add(Field("Janela", new StackPanel { Children = { list, refresh } }, 90));
                panel.Children.Add(Muted(item.CaptureMode == 2
                    ? "Recorte da tela onde a janela está: o melhor para jogos. Se a janela mudar de lugar, use Atualizar ou reinicie."
                    : "Conteúdo da janela: funciona com a janela coberta, mas jogos costumam sair pretos.", 11));
                if (item.WindowProcess is not null && windows.All(w => !string.Equals(w.ProcessName, item.WindowProcess, StringComparison.OrdinalIgnoreCase)))
                    panel.Children.Add(Muted($"A janela escolhida antes ({item.WindowProcess}) não está aberta agora.", 11.5));
            }
        }

        Rebuild();
        return panel;
    }

    // ---------------------------------------------------------------- opções das cenas (Programa › Avançado)

    private void BuildSceneOptions()
    {
        _tabAdvanced.Children.Add(Heading("Cenas"));
        var live = Check("Trocar de cena sem reiniciar", $"Todas as cenas (até {StudioCommand.MaxLiveScenes}) ficam montadas ao mesmo tempo e a troca é na hora, sem a live sair do ar. Pesa um pouco mais no processador; em PC fraco, desligue (a troca passa a reiniciar a captura por alguns segundos).");
        live.IsChecked = _s.StudioScenesLive;
        live.Click += (_, _) => { _s.StudioScenesLive = live.IsChecked == true; _s.Save(); };
        _tabAdvanced.Children.Add(live);
        var program = Check("Mostrar a imagem \"No ar\"", "Uma cópia pequena do vídeo final ao lado da prévia, para conferir o que o público está vendo.");
        program.IsChecked = _s.StudioProgramView;
        program.Click += (_, _) => { _s.StudioProgramView = program.IsChecked == true; _s.Save(); };
        _tabAdvanced.Children.Add(program);
        _tabAdvanced.Children.Add(Muted("O vídeo vertical (Shorts) tem montagem própria: com ele ligado, trocar de cena reinicia a captura.", 11));
    }
}
