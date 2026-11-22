using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MBExtreme.Core.Recording;

namespace MBExtreme.UI.Pages;

/// <summary>Replays: galeria dos lances salvos, em miniaturas agrupadas por jogo (o jogo vem da janela que estava aberta no lance).</summary>
public sealed partial class StudioPage
{
    private readonly StackPanel _replayGallery = new();

    private void BuildReplayGallery()
    {
        _replayGallery.Children.Clear();
        var clips = ReplayGallery.List(StudioController.ClipsFolder);
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var open = SmallButton("Abrir pasta", (_, _) => OpenFolder(StudioController.ClipsFolder));
        open.Padding = new Thickness(10, 3, 10, 3);
        DockPanel.SetDock(open, Dock.Right);
        head.Children.Add(open);
        string hotkey = _s.StudioHotkeySave is { Length: > 0 } h ? h : "o atalho de Salvar replay";
        head.Children.Add(new TextBlock
        {
            Text = clips.Count == 0 ? "SEUS LANCES · nenhum ainda" : $"SEUS LANCES · {clips.Count} clipe(s) · {clips.Sum(c => c.Bytes) / 1_073_741_824.0:0.0} GB",
            FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brush("AccentBrush"), VerticalAlignment = VerticalAlignment.Center,
        });
        _replayGallery.Children.Add(head);
        if (clips.Count == 0)
        {
            _replayGallery.Children.Add(Muted($"Ligue o Replay, jogue e aperte {hotkey} (ou o botão Lance no widget) para salvar os últimos segundos. Os lances aparecem aqui, separados por jogo.", 11.5));
            return;
        }
        foreach (var (game, list) in ReplayGallery.ByGame(clips).Take(8))
        {
            _replayGallery.Children.Add(new TextBlock { Text = $"{game}  ·  {list.Count} lance(s)", FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 6) });
            var row = new WrapPanel();
            foreach (var clip in list.Take(12)) row.Children.Add(ClipCard(clip));
            _replayGallery.Children.Add(row);
        }
    }

    private Border ClipCard(ReplayClip clip)
    {
        var image = new Image { Width = 176, Height = 99, Stretch = Stretch.UniformToFill };
        var stack = new StackPanel();
        stack.Children.Add(new Border { Width = 176, Height = 99, Background = Brush("Surface2Brush"), Child = image, ClipToBounds = true });
        stack.Children.Add(new TextBlock { Text = clip.At.ToString("dd/MM HH:mm"), FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 6, 8, 0) });
        stack.Children.Add(new TextBlock { Text = $"{clip.Bytes / 1_048_576.0:0} MB", FontSize = 10.5, Foreground = Brush("MutedBrush"), Margin = new Thickness(8, 0, 8, 6) });
        var card = new Border
        {
            Child = stack, Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1),
            BorderBrush = Brush("BorderBrush"), Background = Brush("SurfaceBrush"), ClipToBounds = true, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = Path.GetFileName(clip.Path),
        };
        card.MouseLeftButtonUp += (_, _) => OpenPath(clip.Path);
        // clique direito: as opções do lance
        var menu = new ContextMenu();
        void Item(string text, Action act) { var i = new MenuItem { Header = text }; i.Click += (_, _) => act(); menu.Items.Add(i); }
        Item("Assistir", () => OpenPath(clip.Path));
        Item("Mostrar na pasta", () => { try { Process.Start("explorer.exe", "/select,\"" + clip.Path + "\""); } catch (System.ComponentModel.Win32Exception) { } });
        Item("Virar vídeo curto (Shorts)", () => MainWindow.Current?.Go("shorts"));
        Item("Apagar", () =>
        {
            if (MessageBox.Show("Mandar este lance para a Lixeira?", "Replays", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try { RecycleBin.Delete(clip.Path); BuildReplayGallery(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException) { SetStatus("Não foi possível apagar: " + ex.Message, "BadBrush"); }
        });
        card.ContextMenu = menu;
        _ = LoadThumbAsync(clip.Path, image);
        return card;
    }

    private async Task LoadThumbAsync(string clip, Image target)
    {
        string thumb = ReplayGallery.ThumbPath(clip);
        if (!File.Exists(thumb) && _ffmpegPath is { } ffmpeg)
        {
            await Task.Run(() =>
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(thumb)!);
                    var info = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                    foreach (var a in ReplayGallery.ThumbArgs(clip, thumb)) info.ArgumentList.Add(a);
                    using var p = Process.Start(info);
                    p?.StandardError.ReadToEnd();
                    p?.WaitForExit(8000);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException) { }
            });
        }
        if (!File.Exists(thumb)) return;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(thumb);
            bitmap.DecodePixelWidth = 352;
            bitmap.EndInit();
            bitmap.Freeze();
            target.Source = bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException) { }
    }

    private static void OpenPath(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    private static void OpenFolder(string folder)
    {
        try { Directory.CreateDirectory(folder); Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException) { }
    }
}
