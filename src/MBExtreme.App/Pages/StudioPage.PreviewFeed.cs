using System.Windows.Media.Imaging;
using MBExtreme.Core.Recording;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Prévia da tela pela placa de vídeo: um FFmpeg leve (ddagrab, 20 quadros por segundo, 640 px) no lugar de copiar a tela pelo GDI a cada 0,1 s.
/// Vale para a tela inteira e para o recorte de uma janela de jogo; o modo "conteúdo da janela" continua pelo GDI.
/// </summary>
public sealed partial class StudioPage
{
    private MBExtreme.UI.Recording.CameraLiveFeed? _screenFeed;
    private string? _screenFeedFailed;
    private bool _screenFeedShowing;
    private int _screenFeedTick;
    private (int W, int H) _screenFeedSize;

    /// <summary>True quando a prévia da tela está vindo do FFmpeg (então não precisa copiar a tela).</summary>
    private bool SyncScreenFeed()
    {
        if (++_screenFeedTick % 5 != 1 && _screenFeed is not null) return _screenFeedShowing;   // confere a origem a cada meio segundo

        string? signature = null;
        IReadOnlyList<string>? args = null;
        bool allowed = IsLoaded && _ffmpegPath is not null && !_studio.IsRunning && !AppServices.Recorder.IsRecording;
        if (allowed && ScreenFeedTarget() is { } target)
        {
            args = ScreenPreview.Args(target.Output, target.Crop, CurrentFilter().ToChain());
            signature = string.Join(" ", args);
        }

        if (signature is null || signature == _screenFeedFailed)
        {
            StopScreenFeed();
            return false;
        }
        if (_screenFeed?.Signature == signature) return _screenFeedShowing;

        StopScreenFeed();
        var feed = new MBExtreme.UI.Recording.CameraLiveFeed(signature);
        feed.Frame += image => ShowScreenFrame(feed, image);
        feed.Failed += _ =>
        {
            if (!ReferenceEquals(_screenFeed, feed)) return;
            _screenFeedFailed = signature;   // esta placa/versão não deu: volta para a cópia pelo GDI
            StopScreenFeed();
        };
        _screenFeed = feed;
        feed.StartArgs(_ffmpegPath!, args!);
        return _screenFeedShowing;
    }

    private void ShowScreenFrame(MBExtreme.UI.Recording.CameraLiveFeed feed, BitmapSource image)
    {
        if (!ReferenceEquals(_screenFeed, feed) || _studio.IsRunning) return;
        _screenFeedShowing = true;
        _preview.Source = image;
        _lastFrame = image;
        UpdateVerticalLive();
        var size = (image.PixelWidth, image.PixelHeight);
        if (size != _screenFeedSize)
        {
            _screenFeedSize = size;
            _previewNote.Text = "É assim que o vídeo vai sair (imagem reduzida, ao vivo).";
            _previewNote.Foreground = Brush("MutedBrush");
            UpdateGhosts();
        }
    }

    private void StopScreenFeed()
    {
        _screenFeed?.Dispose();
        _screenFeed = null;
        _screenFeedShowing = false;
        _screenFeedSize = default;
    }

    /// <summary>A tela (saída da placa) e o recorte que a prévia deve mostrar; null quando a origem é o conteúdo de uma janela (GDI) ou não existe.</summary>
    private (int Output, PixelRect? Crop)? ScreenFeedTarget()
    {
        PixelRect area;
        if (SceneGrabTarget() is { } own)
        {
            if (own.Problem is not null || own.Gdi is not null) return null;
            area = own.Area;
        }
        else if (_sourceTab == 0) area = CurrentArea();
        else if (_windowMode == 0 && _windows.Selected >= 0 && _windows.Selected < _windowList.Count) area = _windowList[_windows.Selected].Bounds;
        else return null;

        if (_displayList.Count == 0) return null;
        int output = ScreenPreview.DisplayFor(_displayList, area);
        if (output < 0) return null;
        var display = _displayList.First(d => d.Index == output);
        return (output, ScreenPreview.CropWithin(display.Bounds, area));
    }
}
