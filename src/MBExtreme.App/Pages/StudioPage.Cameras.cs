using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MBExtreme.Core.Recording;
using MBExtreme.Core.Settings;

namespace MBExtreme.UI.Pages;

/// <summary>Imagem de teste das câmeras (dispositivos do Windows ou câmera do celular pela rede).</summary>
public sealed partial class StudioPage
{
    /// <summary>Última imagem recebida de cada câmera, para mostrar nos cartões e nos retângulos da pré-visualização.</summary>
    private readonly Dictionary<string, BitmapSource> _camStills = new();

    private bool CameraBusyWithRecording => _studio.IsRunning || AppServices.Recorder.IsRecording;

    /// <summary>Tira uma imagem da câmera e mostra no cartão; se falhar, diz o motivo.</summary>
    private async Task TestCameraAsync(StudioCameraSetting cam, Image target, TextBlock note)
    {
        string? source = cam.Name;
        if (string.IsNullOrWhiteSpace(source) || !CameraOverlay.IsValidName(source))
        {
            note.Text = "Escolha a câmera (ou o endereço do celular) para ver a imagem.";
            return;
        }

        if (_camStills.TryGetValue(source, out var cached)) target.Source = cached;
        if (CameraBusyWithRecording)
        {
            note.Text = "A câmera está sendo usada pela gravação ou transmissão agora; mostrando a última imagem. Pare para testar de novo.";
            return;
        }

        _ffmpegPath ??= await Task.Run(FfmpegRecorder.Locate);
        if (_ffmpegPath is null) { note.Text = "Instale o FFmpeg (aba Avançado) para ver a câmera."; return; }

        note.Text = "Abrindo a câmera...";
        string ffmpeg = _ffmpegPath;
        string png = Path.Combine(Path.GetTempPath(), "mbx-cam-" + Guid.NewGuid().ToString("N") + ".png");
        var (ok, error) = await Task.Run(() => GrabStill(ffmpeg, source, png));
        try
        {
            if (ok && File.Exists(png))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(png);
                bitmap.EndInit();
                bitmap.Freeze();
                _camStills[source] = bitmap;
                target.Source = bitmap;
                note.Text = "Câmera funcionando. A imagem é um retrato de agora; a gravação mostra o vídeo em movimento.";
                UpdateGhosts();
            }
            else
            {
                note.Text = CameraSource.Explain(source, error);
            }
        }
        finally
        {
            try { if (File.Exists(png)) File.Delete(png); } catch (IOException) { }
        }
    }

    private static (bool Ok, string Error) GrabStill(string ffmpeg, string source, string png)
    {
        var info = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (string a in CameraSource.StillArgs(source, png)) info.ArgumentList.Add(a);
        try
        {
            using var process = Process.Start(info);
            if (process is null) return (false, "não foi possível iniciar o FFmpeg");
            var error = process.StandardError.ReadToEndAsync();
            process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(15000))
            {
                try { process.Kill(true); } catch (InvalidOperationException) { }
                return (false, "timed out");
            }
            return (process.ExitCode == 0, error.GetAwaiter().GetResult());
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>Retrato (se já existir) para o retângulo da câmera na pré-visualização.</summary>
    private Brush? CameraGhostBrush(int cameraIndex)
    {
        if (cameraIndex < 0 || cameraIndex >= _s.StudioCameras.Count) return null;
        string? name = _s.StudioCameras[cameraIndex].Name;
        return name is not null && _camStills.TryGetValue(name, out var still) ? new ImageBrush(still) { Stretch = Stretch.UniformToFill } : null;
    }

    /// <summary>Cartão da câmera do celular: aceita o endereço de vídeo que um aplicativo de câmera por Wi-Fi mostra.</summary>
    private UIElement BuildPhoneCameraCard()
    {
        var card = new StackPanel();
        card.Children.Add(new TextBlock { Text = "Usar o celular como câmera", FontSize = 14, FontWeight = FontWeights.SemiBold });
        card.Children.Add(Muted("1) No celular, abra um aplicativo de câmera por Wi-Fi (ex.: IP Webcam, DroidCam ou o que preferir) e inicie o servidor de vídeo. " +
            "2) Copie o endereço que ele mostra (começa com http:// ou rtsp://). 3) Cole abaixo, com o celular e o PC na mesma rede Wi-Fi.", 11.5));

        var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var add = SmallButton("Adicionar", (_, _) => { });
        DockPanel.SetDock(add, Dock.Right);
        add.Margin = new Thickness(8, 0, 0, 0);
        var box = new TextBox { Padding = new Thickness(6, 4, 6, 4), ToolTip = "Exemplo: http://192.168.0.15:8080/video" };
        row.Children.Add(add);
        row.Children.Add(box);
        card.Children.Add(row);
        var note = Muted("", 11.5);
        card.Children.Add(note);
        add.Click += (_, _) =>
        {
            string url = box.Text.Trim();
            if (!CameraSource.IsValidStream(url)) { note.Text = "Endereço inválido. Ele precisa começar com http://, https:// ou rtsp:// e não ter espaços."; return; }
            if (_s.StudioCameras.Count >= StudioCommand.MaxCameras) { note.Text = $"Já há {StudioCommand.MaxCameras} câmeras. Remova uma antes."; return; }
            if (_s.StudioCameras.Any(c => c.Name == url)) { note.Text = "Essa câmera já foi adicionada."; return; }
            int[] corners = { 3, 1, 2, 0 };
            _s.StudioCameras.Add(new StudioCameraSetting { Name = url, Corner = corners[_s.StudioCameras.Count % corners.Length], Percent = _s.StudioCameras.Count == 0 ? 22 : 15 });
            RebuildCameras();
            UpdateGhosts();
        };
        card.Children.Add(Muted("Por cabo USB: em alguns celulares Android recentes, ao conectar o cabo aparece a opção \"Webcam\" nas opções de USB; o Windows então enxerga o celular como uma câmera comum, e ele aparece em \"Procurar câmeras\". Se o seu não tiver, use o Wi-Fi.", 11.5));
        var border = CardOf(card);
        border.Padding = new Thickness(12, 10, 12, 8);
        return border;
    }
}
