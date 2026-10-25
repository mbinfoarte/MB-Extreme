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

    private readonly Dictionary<StudioCameraSetting, MBExtreme.UI.Recording.CameraLiveFeed> _feeds = new();
    private readonly Dictionary<StudioCameraSetting, ImageBrush> _liveBrushes = new();
    private readonly Dictionary<StudioCameraSetting, TextBlock> _camNotes = new();
    private readonly Dictionary<StudioCameraSetting, string> _failedSignature = new();
    private bool _feedsSuspended;

    private ImageBrush LiveBrush(StudioCameraSetting cam)
    {
        if (!_liveBrushes.TryGetValue(cam, out var brush)) _liveBrushes[cam] = brush = new ImageBrush { Stretch = Stretch.Uniform };
        return brush;
    }

    /// <summary>
    /// Mantém o vídeo ao vivo de cada câmera ligada (e de todas, na aba Câmeras) enquanto a câmera está livre. Quando algo muda
    /// (câmera, filtro, espelho), o vídeo é reaberto na hora com o novo ajuste.
    /// </summary>
    private void SyncCameraFeeds()
    {
        bool allowed = IsLoaded && !_feedsSuspended && !CameraBusyWithRecording && _ffmpegPath is not null;
        var wanted = new Dictionary<StudioCameraSetting, string>();
        if (allowed)
            foreach (var cam in _s.StudioCameras)
                if (CameraOverlay.IsValidName(cam.Name) && (cam.On || _settingsTab == 4))
                    wanted[cam] = cam.Name + "|" + (PresetFilter(cam.Preset, cam.Mirror).ToChain() ?? "");

        foreach (var cam in _feeds.Keys.ToList())
        {
            if (wanted.ContainsKey(cam)) continue;
            _feeds[cam].Dispose();
            _feeds.Remove(cam);
        }

        foreach (var (cam, signature) in wanted)
        {
            if (_feeds.TryGetValue(cam, out var current))
            {
                if (current.Signature == signature) continue;
                current.Dispose();
                _feeds.Remove(cam);
            }
            if (_failedSignature.TryGetValue(cam, out var failed) && failed == signature) continue; // já falhou assim: espera o usuário mudar algo ou pedir de novo

            var feed = new MBExtreme.UI.Recording.CameraLiveFeed(signature);
            feed.Frame += image =>
            {
                LiveBrush(cam).ImageSource = image;
                if (cam.Name is not null) _camStills[cam.Name] = image;
                if (_camNotes.TryGetValue(cam, out var note)) note.Text = "Câmera ao vivo.";
            };
            feed.Failed += message =>
            {
                _failedSignature[cam] = signature;
                if (cam.Name is not null) _camStills.Remove(cam.Name);
                LiveBrush(cam).ImageSource = null;
                if (_camNotes.TryGetValue(cam, out var note)) note.Text = CameraSource.Explain(cam.Name ?? "", message) + " (clique em \"Tentar de novo\")";
                UpdateGhosts();
            };
            _feeds[cam] = feed;
            if (_camNotes.TryGetValue(cam, out var starting)) starting.Text = "Abrindo a câmera...";
            feed.Start(_ffmpegPath!, cam.Name!, PresetFilter(cam.Preset, cam.Mirror).ToChain());
        }
    }

    /// <summary>Solta as câmeras (para a gravação poder usá-las) e espera o Windows liberar os dispositivos.</summary>
    private async Task ReleaseCameraFeedsAsync()
    {
        _feedsSuspended = true;
        var running = _feeds.Values.ToList();
        _feeds.Clear();
        if (running.Count == 0) return;
        await Task.Run(() => { foreach (var feed in running) feed.StopAndWait(); });
        await Task.Delay(300);   // margem para o Windows liberar o dispositivo
    }

    private void StopAllFeeds()
    {
        foreach (var feed in _feeds.Values) feed.Dispose();
        _feeds.Clear();
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
