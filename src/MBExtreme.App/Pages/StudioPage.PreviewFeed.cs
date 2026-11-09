using System.Windows.Media;
using System.Windows.Media.Imaging;
using MBExtreme.Core.Recording;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Prévia da tela pela placa de vídeo: um FFmpeg leve (ddagrab, 60 quadros por segundo, até Full HD) no lugar de copiar a tela pelo GDI.
/// Vale para a tela inteira e para o recorte de uma janela de jogo; o modo "conteúdo da janela" usa o Windows.Graphics.Capture
/// (gfxcapture) e só cai na cópia pelo GDI se o FFmpeg não tiver esse filtro.
/// </summary>
public sealed partial class StudioPage
{
    private MBExtreme.UI.Recording.RawFrameFeed? _screenFeed;
    private string? _screenFeedFailed;
    private bool _screenFeedShowing;
    private int _screenFeedTick;

    /// <summary>True quando a prévia da tela está vindo do FFmpeg (então não precisa copiar a tela).</summary>
    private bool SyncScreenFeed()
    {
        if (++_screenFeedTick % 5 != 1 && _screenFeed is not null) return _screenFeedShowing;   // confere a origem a cada meio segundo

        string? signature = null;
        IReadOnlyList<string>? args = null;
        (int W, int H) size = default;
        bool allowed = IsLoaded && _ffmpegPath is not null && !_studio.IsRunning && !AppServices.Recorder.IsRecording;
        if (allowed && ScreenFeedTarget() is { } target)
        {
            var source = target.Crop ?? _displayList.First(d => d.Index == target.Output).Bounds;
            double shown = Math.Max(_preview.ActualWidth, 320) * VisualTreeHelper.GetDpi(_preview).DpiScaleX;
            size = ScreenPreview.Size(shown, source.Width, source.Height);
            args = ScreenPreview.RawArgs(target.Output, target.Crop, CurrentFilter().ToChain(), size.W, size.H);
            signature = string.Join(" ", args);
        }
        else if (allowed && WindowFeedTarget() is { } window)
        {
            // "conteúdo da janela": pelo Windows.Graphics.Capture, na placa de vídeo (se o FFmpeg não tiver, volta para a cópia pelo GDI)
            double shown = Math.Max(_preview.ActualWidth, 320) * VisualTreeHelper.GetDpi(_preview).DpiScaleX;
            size = ScreenPreview.Size(shown, Math.Max(2, window.Area.Width), Math.Max(2, window.Area.Height));
            args = ScreenPreview.WindowArgs(window.Handle.ToInt64(), CurrentFilter().ToChain(), size.W, size.H);
            signature = string.Join(" ", args);
        }

        if (signature is null || signature == _screenFeedFailed)
        {
            StopScreenFeed();
            return false;
        }
        if (_screenFeed?.Signature == signature) return _screenFeedShowing;

        StopScreenFeed();
        var feed = new MBExtreme.UI.Recording.RawFrameFeed(signature, size.W, size.H);
        feed.Frame += () => ShowScreenFrame(feed);
        feed.Failed += _ =>
        {
            if (!ReferenceEquals(_screenFeed, feed)) return;
            _screenFeedFailed = signature;   // esta placa/versão não deu: volta para a cópia pelo GDI
            StopScreenFeed();
        };
        _screenFeed = feed;
        feed.Start(_ffmpegPath!, args!);
        return _screenFeedShowing;
    }

    private void ShowScreenFrame(MBExtreme.UI.Recording.RawFrameFeed feed)
    {
        if (!ReferenceEquals(_screenFeed, feed) || _studio.IsRunning) return;
        var image = feed.Bitmap;
        bool first = !_screenFeedShowing || !ReferenceEquals(_preview.Source, image);
        _screenFeedShowing = true;
        if (first)
        {
            _preview.Source = image;
            _previewNote.Text = "É assim que o vídeo vai sair (ao vivo, pela placa de vídeo).";
            _previewNote.Foreground = Brush("MutedBrush");
            UpdateGhosts();
        }
        _lastFrame = image;
        UpdateVerticalLive();
    }

    private void StopScreenFeed()
    {
        _screenFeed?.Dispose();
        _screenFeed = null;
        _screenFeedShowing = false;
    }

    /// <summary>Janela capturada pelo conteúdo (modo GDI na gravação): a prévia vem dela pelo Windows.Graphics.Capture.</summary>
    private (IntPtr Handle, PixelRect Area)? WindowFeedTarget()
    {
        if (SceneGrabTarget() is { } own)
            return own.Problem is null && own.Gdi is { } handle && handle != IntPtr.Zero ? (handle, own.Area) : null;
        if (_sourceTab != 0 && _windowMode == 1 && _windows.Selected >= 0 && _windows.Selected < _windowList.Count)
        {
            var w = _windowList[_windows.Selected];
            return w.Handle != IntPtr.Zero ? (w.Handle, w.Bounds) : null;
        }
        return null;
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
