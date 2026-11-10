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
    private readonly Border _programBox = new() { Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _programTitle = new() { FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 6) };
    private readonly Border _programFrame = new() { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(2), ClipToBounds = true, MinHeight = 120 };
    private readonly TextBlock _programEmpty = new() { Text = "Fora do ar", FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private long _programShown;

    private FrameworkElement BuildProgramBox()
    {
        var panel = new StackPanel();
        _programTitle.Foreground = Brush("MutedBrush");
        panel.Children.Add(_programTitle);
        _programEmpty.Foreground = Brush("MutedBrush");
        _programFrame.Background = Frozen(Color.FromRgb(0x1C, 0x1C, 0x20));
        _programFrame.Child = new Grid { Children = { _programEmpty, _programImage } };
        panel.Children.Add(_programFrame);
        _programBox.Child = panel;
        RefreshProgramTitle();
        _programBox.ToolTip = "O que está saindo agora na gravação e na live (imagem reduzida). A prévia ao lado mostra a cena escolhida.";
        return _programBox;
    }

    /// <summary>Mostra a última imagem do que está no ar (o FFmpeg manda umas 8 por segundo).</summary>
    private void RefreshProgramView()
    {
        bool running = _studio.IsRunning;
        _programFrame.BorderBrush = running ? Frozen(Color.FromRgb(0xFF, 0x4D, 0x4D)) : Yellow;
        RefreshProgramTitle();
        bool rehearsal = running && _studio.Session.Request is { Rehearsal: true };
        bool show = _s.StudioProgramView || rehearsal;   // no ensaio a imagem final aparece sempre
        var frame = running && show ? _studio.Session.ProgramFrame : null;
        if (frame is null)
        {
            _programImage.Source = null;
            _programEmpty.Text = running ? (show ? "Carregando a imagem..." : "Imagem \"No ar\" desligada (Programa › Avançado)") : "Fora do ar";
            _programEmpty.Visibility = Visibility.Visible;
            return;
        }
        _programEmpty.Visibility = Visibility.Collapsed;
        if (frame is not { } f || f.Number == _programShown) return;
        _programShown = f.Number;
        if (JpegImage(f.Jpeg) is not { } image) return;   // um quadro ruim: espera o próximo
        _programImage.Source = image;
        if (!_s.StudioSceneStudioMode)
        {
            // sem o modo estúdio, a prévia grande é o próprio vídeo que está saindo: nada de copiar a tela do jogo durante a gravação
            _preview.Source = image;
            _lastFrame = image;
            _previewNote.Text = rehearsal
                ? "Ensaio: é exatamente assim que a live vai sair. Nada está sendo transmitido nem gravado."
                : "Ao vivo: é exatamente o que está sendo gravado e transmitido.";
            _previewNote.Foreground = Brush("MutedBrush");
        }
    }

    private static BitmapSource? JpegImage(byte[] jpeg)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(jpeg);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or InvalidOperationException) { return null; }
    }

    private void RefreshProgramTitle()
    {
        _programTitle.Inlines.Clear();
        _programTitle.Inlines.Add(new System.Windows.Documents.Run("No ar: "));
        string name = _studio.IsRunning ? (_airScene ?? CurrentScene).Name : "—";
        _programTitle.Inlines.Add(new System.Windows.Documents.Run(name) { FontWeight = FontWeights.SemiBold, Foreground = Yellow });
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
