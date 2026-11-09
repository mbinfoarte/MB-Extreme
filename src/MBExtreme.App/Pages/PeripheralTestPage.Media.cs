using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MBExtreme.Core.Devices;
using MBExtreme.Core.Recording;

namespace MBExtreme.UI.Pages;

/// <summary>Testes de tela (pixels mortos), alto-falantes (esquerdo e direito), microfone (nível e gravação) e webcam (foto de teste).</summary>
public sealed partial class PeripheralTestPage
{
    private MicTester? _mic;
    private CancellationTokenSource? _mediaCts;

    private void StopMedia()
    {
        _mediaCts?.Cancel();
        _mic?.Dispose();
        _mic = null;
    }

    // ---------------------------------------------------------------- tela

    private static readonly (Color Color, string Name)[] ScreenColors =
    {
        (Colors.Black, "preto"), (Colors.White, "branco"), (Colors.Red, "vermelho"), (Color.FromRgb(0, 255, 0), "verde"), (Colors.Blue, "azul"), (Color.FromRgb(128, 128, 128), "cinza"),
    };

    private void BuildScreen()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Pixels mortos e manchas", FontSize = 15, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(Muted("A tela inteira fica de uma cor só (preto, branco, vermelho, verde, azul e cinza). Procure pontinhos de outra cor (pixel morto ou travado), manchas e vazamento de luz nas bordas. Clique ou aperte Espaço para trocar de cor; Esc para sair."));
        panel.Children.Add(SmallButton("Começar o teste da tela", (_, _) => ScreenTest(), true));
        _host.Children.Add(CardOf(panel));
    }

    private static void ScreenTest()
    {
        int index = 0;
        var label = new TextBlock { FontSize = 14, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 30) };
        var window = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, WindowState = WindowState.Maximized, Topmost = true,
            Background = new SolidColorBrush(ScreenColors[0].Color), Cursor = Cursors.None, Content = new Grid { Children = { label } },
        };
        void Apply()
        {
            window.Background = new SolidColorBrush(ScreenColors[index].Color);
            label.Text = $"{ScreenColors[index].Name} ({index + 1} de {ScreenColors.Length}) · clique ou Espaço = próxima · Esc = sair";
            label.Foreground = index == 1 ? Brushes.DarkGray : Brushes.Gray;
        }
        void Next() { index++; if (index >= ScreenColors.Length) window.Close(); else Apply(); }
        window.MouseLeftButtonUp += (_, _) => Next();
        window.KeyDown += (_, e) => { if (e.Key == Key.Escape) window.Close(); else if (e.Key is Key.Space or Key.Right or Key.Enter) Next(); };
        Apply();
        window.ShowDialog();
    }

    // ---------------------------------------------------------------- alto-falantes

    private void BuildSpeakers()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Alto-falantes e fones", FontSize = 15, FontWeight = FontWeights.SemiBold });
        var device = Muted("Saída de som: lendo...");
        panel.Children.Add(device);
        panel.Children.Add(Muted("Um tom curto toca só no lado escolhido. Se tocar do lado errado, os fios ou a configuração estão trocados; se não tocar, confira o volume e a saída padrão do Windows."));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(SmallButton("◀ Esquerdo", async (_, _) => await ToneAsync(-1), true));
        buttons.Children.Add(SmallButton("Os dois", async (_, _) => await ToneAsync(0), true));
        buttons.Children.Add(SmallButton("Direito ▶", async (_, _) => await ToneAsync(1), true));
        buttons.Children.Add(SmallButton("Configurações de som do Windows", (_, _) => Open("ms-settings:sound")));
        panel.Children.Add(buttons);
        _host.Children.Add(CardOf(panel));
        _ = Task.Run(AudioTests.DefaultOutputName).ContinueWith(t => Dispatcher.BeginInvoke(new Action(() => device.Text = "Saída de som: " + t.Result)));
    }

    private async Task ToneAsync(float pan)
    {
        _mediaCts?.Cancel();
        _mediaCts = new CancellationTokenSource();
        var token = _mediaCts.Token;
        try { await Task.Run(() => AudioTests.PlayTone(pan, TimeSpan.FromSeconds(1.5), token)); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            SetStatus("Não foi possível tocar o som: " + ex.Message, "BadBrush");
        }
    }

    // ---------------------------------------------------------------- microfone

    private void BuildMic()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Microfone", FontSize = 15, FontWeight = FontWeights.SemiBold });
        var device = Muted("Microfone: lendo...");
        panel.Children.Add(device);
        var meter = new ProgressBar { Height = 14, Maximum = 1, Margin = new Thickness(0, 10, 0, 4) };
        System.Windows.Automation.AutomationProperties.SetName(meter, "Nível do microfone");
        panel.Children.Add(meter);
        var hint = Muted("Fale alguma coisa: a barra deve subir. Se não mexer, confira se o microfone certo está como padrão e se o Windows deixa os apps usarem o microfone.");
        panel.Children.Add(hint);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(SmallButton("Gravar 5 segundos e ouvir", async (_, _) => await RecordAsync(hint), true));
        buttons.Children.Add(SmallButton("Privacidade do microfone", (_, _) => Open("ms-settings:privacy-microphone")));
        buttons.Children.Add(SmallButton("Configurações de som do Windows", (_, _) => Open("ms-settings:sound")));
        panel.Children.Add(buttons);
        _host.Children.Add(CardOf(panel));
        _ = Task.Run(AudioTests.DefaultInputName).ContinueWith(t => Dispatcher.BeginInvoke(new Action(() => device.Text = "Microfone: " + t.Result)));
        try
        {
            _mic = new MicTester();
            double shown = 0;
            _mic.Level += level => Dispatcher.BeginInvoke(new Action(() =>
            {
                shown = Math.Max(level, shown * 0.85);   // cai devagar, como os medidores
                meter.Value = shown;
            }));
            _mic.Start();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
        {
            hint.Text = "Não foi possível abrir o microfone: " + ex.Message;
            hint.Foreground = Brush("BadBrush");
        }
    }

    private async Task RecordAsync(TextBlock hint)
    {
        if (_mic is null) return;
        _mediaCts?.Cancel();
        _mediaCts = new CancellationTokenSource();
        var token = _mediaCts.Token;
        var mic = _mic;
        hint.Text = "Gravando 5 segundos... fale normalmente.";
        try
        {
            await Task.Run(() => mic.RecordAndPlay(TimeSpan.FromSeconds(5), token));
            hint.Text = "Tocou a gravação? Se o som saiu baixo, abafado ou com chiado, ajuste o volume do microfone nas configurações de som.";
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ObjectDisposedException)
        {
            hint.Text = "Não foi possível gravar: " + ex.Message;
        }
    }

    // ---------------------------------------------------------------- webcam

    private void BuildWebcam()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Webcams encontradas", FontSize = 15, FontWeight = FontWeights.SemiBold });
        var list = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
        var image = new Image { MaxHeight = 360, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        list.Children.Add(Muted("Procurando câmeras..."));
        panel.Children.Add(list);
        panel.Children.Add(image);
        panel.Children.Add(Muted("A foto de teste mostra se a câmera liga e a qualidade da imagem. Se aparecer \"em uso\", feche outros programas que usam a câmera (chamadas de vídeo, OBS).", 11.5));
        panel.Children.Add(SmallButton("Privacidade da câmera", (_, _) => Open("ms-settings:privacy-webcam")));
        _host.Children.Add(CardOf(panel));
        _ = LoadCamerasAsync(list, image);
    }

    private async Task LoadCamerasAsync(StackPanel list, Image image)
    {
        string? ffmpeg = await Task.Run(FfmpegRecorder.Locate);
        if (ffmpeg is null) { list.Children.Clear(); list.Children.Add(Muted("O motor de vídeo ainda está sendo preparado. Abra a tela de novo em alguns segundos.")); return; }
        var cameras = await Task.Run(() => FfmpegRecorder.ListCameras(ffmpeg));
        list.Children.Clear();
        if (cameras.Count == 0) { list.Children.Add(Muted("Nenhuma webcam encontrada. Conecte uma e abra esta aba de novo.")); return; }
        foreach (var name in cameras)
        {
            string camera = name;
            list.Children.Add(Row("  " + name, "Funcionando para o Windows", SmallButton("Tirar foto de teste", async (_, _) => await StillAsync(ffmpeg, camera, image), true)));
        }
    }

    private async Task StillAsync(string ffmpeg, string camera, Image image)
    {
        string file = Path.Combine(Path.GetTempPath(), $"mbx-webcam-{Environment.ProcessId}.png");
        SetStatus("Ligando a câmera...");
        var (code, _, error) = await Task.Run(() =>
        {
            var info = new System.Diagnostics.ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var a in CameraSource.StillArgs(camera, file, 960)) info.ArgumentList.Add(a);
            using var p = System.Diagnostics.Process.Start(info)!;
            string err = p.StandardError.ReadToEnd();
            if (!p.WaitForExit(15000)) { try { p.Kill(); } catch (InvalidOperationException) { } }
            return (p.HasExited ? p.ExitCode : -1, "", err);
        });
        if (code != 0 || !File.Exists(file))
        {
            SetStatus("A câmera não entregou imagem" + (error.Contains("busy", StringComparison.OrdinalIgnoreCase) || error.Contains("in use", StringComparison.OrdinalIgnoreCase) ? " (em uso por outro programa)." : "."), "BadBrush");
            return;
        }
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(file);
        bitmap.EndInit();
        image.Source = bitmap;
        try { File.Delete(file); } catch (IOException) { }
        SetStatus($"Câmera funcionando: imagem de {bitmap.PixelWidth} × {bitmap.PixelHeight}.", "OkBrush");
    }
}
