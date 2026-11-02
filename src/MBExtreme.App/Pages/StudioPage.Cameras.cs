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

    /// <summary>Só o gravador antigo (modo compatível) prende a câmera; o estúdio usa o relé e a prévia continua durante a live.</summary>
    private bool CameraBusyWithRecording => AppServices.Recorder.IsRecording;

    private readonly Dictionary<StudioCameraSetting, string> _relaySignatures = new();
    private readonly Dictionary<StudioCameraSetting, (WriteableBitmap Bitmap, byte[] Pixels, long Shown, CameraRelay Relay)> _camBitmaps = new();
    private readonly Dictionary<StudioCameraSetting, ImageBrush> _liveBrushes = new();
    private readonly Dictionary<StudioCameraSetting, TextBlock> _camNotes = new();
    private readonly Dictionary<StudioCameraSetting, string> _failedSignature = new();
    private readonly System.Windows.Threading.DispatcherTimer _camTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };

    private ImageBrush LiveBrush(StudioCameraSetting cam)
    {
        if (!_liveBrushes.TryGetValue(cam, out var brush)) _liveBrushes[cam] = brush = new ImageBrush { Stretch = Stretch.Uniform };
        return brush;
    }

    /// <summary>
    /// Mantém aberta (pelo relé) cada câmera ligada, e todas na aba Câmeras. A mesma câmera aberta serve a prévia e a gravação/live,
    /// então ela não fecha nem trava ao começar a live. Quando algo muda (câmera, filtro, espelho), reabre com o novo ajuste.
    /// </summary>
    private void SyncCameraFeeds()
    {
        bool allowed = IsLoaded && !CameraBusyWithRecording && _ffmpegPath is not null;
        var wanted = new Dictionary<StudioCameraSetting, string>();
        if (allowed)
            foreach (var cam in _s.StudioCameras)
                if (CameraOverlay.IsValidName(cam.Name) && (cam.On || _settingsTab == 4))
                    wanted[cam] = cam.Name + "|" + (PresetFilter(cam.Preset, cam.Mirror).ToChain() ?? "");

        foreach (var (cam, signature) in _relaySignatures.ToList())
        {
            if (wanted.TryGetValue(cam, out var want) && want == signature) continue;
            _relaySignatures.Remove(cam);
            _camBitmaps.Remove(cam);
            string name = signature[..signature.IndexOf('|')];
            if (!wanted.TryGetValue(cam, out var next) || !next.StartsWith(name + "|", StringComparison.Ordinal))
                AppServices.Cameras.Release(name, this);   // outra câmera ou desligada: solta (fecha se a live não estiver usando)
        }

        foreach (var (cam, signature) in wanted)
        {
            if (_relaySignatures.ContainsKey(cam)) continue;
            if (_failedSignature.TryGetValue(cam, out var failed) && failed == signature) continue;   // já falhou assim: espera o usuário mudar algo ou pedir de novo
            _relaySignatures[cam] = signature;
            if (_camNotes.TryGetValue(cam, out var starting)) starting.Text = "Abrindo a câmera...";
            string ffmpeg = _ffmpegPath!, name = cam.Name!;
            string? chain = PresetFilter(cam.Preset, cam.Mirror).ToChain();
            _ = Task.Run(() =>
            {
                try { AppServices.Cameras.Acquire(ffmpeg, name, chain, this); }   // a primeira vez pergunta os modos da câmera (~1 s)
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    Dispatcher.BeginInvoke(new Action(() => ShowCameraFailure(cam, signature, ex.Message)));
                }
            });
        }
        if (_relaySignatures.Count > 0 && !_camTimer.IsEnabled) _camTimer.Start();
        if (_relaySignatures.Count == 0) _camTimer.Stop();
    }

    /// <summary>Desenha a última imagem de cada câmera (até 30 vezes por segundo, só quando chega imagem nova).</summary>
    private void DrawCameraFrames()
    {
        foreach (var (cam, signature) in _relaySignatures)
        {
            string name = signature[..signature.IndexOf('|')];
            var relay = AppServices.Cameras.Find(name);
            if (relay is null) continue;
            if (!relay.IsRunning && !relay.HasFrames && relay.LastError is { } error)
            {
                ShowCameraFailure(cam, signature, error);
                continue;
            }
            if (relay.LatestFrame is not { } frame) continue;
            long sequence = relay.Sequence;
            int step = relay.Width >= 800 ? 2 : 1;
            var (w, h) = Yuv420Preview.OutputSize(relay.Width, relay.Height, step);
            if (!_camBitmaps.TryGetValue(cam, out var entry) || !ReferenceEquals(entry.Relay, relay) || entry.Bitmap.PixelWidth != w || entry.Bitmap.PixelHeight != h)
                entry = (new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null), new byte[w * h * 4], -1, relay);
            if (entry.Shown == sequence) continue;
            Yuv420Preview.ToBgra(frame, relay.Width, relay.Height, step, entry.Pixels);
            entry.Bitmap.WritePixels(new Int32Rect(0, 0, w, h), entry.Pixels, w * 4, 0);
            bool first = entry.Shown < 0;
            _camBitmaps[cam] = entry with { Shown = sequence };
            if (!first) continue;
            LiveBrush(cam).ImageSource = entry.Bitmap;
            _camStills[name] = entry.Bitmap;
            if (_camNotes.TryGetValue(cam, out var note)) note.Text = "Câmera ao vivo.";
            UpdateGhosts();
        }
    }

    private void ShowCameraFailure(StudioCameraSetting cam, string signature, string message)
    {
        if (!_relaySignatures.TryGetValue(cam, out var current) || current != signature) return;
        _failedSignature[cam] = signature;
        _relaySignatures.Remove(cam);
        _camBitmaps.Remove(cam);
        if (cam.Name is not null)
        {
            _camStills.Remove(cam.Name);
            AppServices.Cameras.Release(cam.Name, this);
        }
        LiveBrush(cam).ImageSource = null;
        if (_camNotes.TryGetValue(cam, out var note)) note.Text = CameraSource.Explain(cam.Name ?? "", message) + " (clique em \"Tentar de novo\")";
        UpdateGhosts();
    }

    /// <summary>Pede de novo a imagem de uma câmera que falhou.</summary>
    private void RetryCamera(StudioCameraSetting cam)
    {
        _failedSignature.Remove(cam);
        if (_relaySignatures.Remove(cam) && cam.Name is not null) AppServices.Cameras.Release(cam.Name, this);
        _camBitmaps.Remove(cam);
        SyncCameraFeeds();
    }

    private readonly Dictionary<string, long> _camFrameShown = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Com a gravação ou a live no ar, a câmera é do FFmpeg da gravação: ele mesmo manda uma cópia pequena de cada câmera,
    /// e é ela que aparece nos quadros das câmeras (sem isso a imagem ficaria parada).
    /// </summary>
    private void RefreshCameraFramesFromSession()
    {
        if (!_studio.IsRunning || _studio.Session.Request is not { } request || request.CameraPreviewPipes.Count == 0) return;   // com o relé, a prévia já vem dele
        for (int i = 0; i < request.Cameras.Count; i++)
        {
            if (_studio.Session.CameraFrame(i) is not { } frame) continue;
            string name = request.Cameras[i].DeviceName;
            if (_camFrameShown.TryGetValue(name, out long shown) && shown == frame.Number) continue;
            _camFrameShown[name] = frame.Number;
            if (JpegImage(frame.Jpeg) is not { } image) continue;
            var cam = _s.StudioCameras.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            if (cam is null) continue;
            LiveBrush(cam).ImageSource = image;
            _camStills[name] = image;
            if (_camNotes.TryGetValue(cam, out var note)) note.Text = "Câmera ao vivo (na gravação).";
        }
    }

    private void StopAllFeeds()
    {
        _camTimer.Stop();
        AppServices.Cameras.ReleaseAll(this);   // a câmera continua aberta se a live estiver usando
        _relaySignatures.Clear();
        _camBitmaps.Clear();
    }

    /// <summary>Retrato (se já existir) para o retângulo da câmera na pré-visualização.</summary>
    private Brush? CameraGhostBrush(int cameraIndex)
    {
        if (cameraIndex < 0 || cameraIndex >= _s.StudioCameras.Count) return null;
        string? name = _s.StudioCameras[cameraIndex].Name;
        return name is not null && _camStills.ContainsKey(name) ? LiveBrush(_s.StudioCameras[cameraIndex]) : null;
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
