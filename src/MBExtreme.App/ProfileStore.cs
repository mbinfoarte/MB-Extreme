using System.IO;
using System.Windows.Media.Imaging;

namespace MBExtreme.UI;

/// <summary>Foto do cliente, guardada no próprio PC (nunca enviada). Fica reduzida a 256 px em %AppData%\MBExtreme\avatar.png.</summary>
internal static class ProfileStore
{
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "avatar.png");

    public static BitmapImage? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // não trava o arquivo
            image.UriSource = new Uri(FilePath);
            image.DecodePixelWidth = 128;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Copia a foto escolhida, reduzida e quadrada (recorte central).</summary>
    public static void Save(string sourcePath)
    {
        var source = new BitmapImage();
        source.BeginInit();
        source.CacheOption = BitmapCacheOption.OnLoad;
        source.UriSource = new Uri(sourcePath);
        source.DecodePixelWidth = 512;
        source.EndInit();

        int side = Math.Min(source.PixelWidth, source.PixelHeight);
        var square = new CroppedBitmap(source, new System.Windows.Int32Rect((source.PixelWidth - side) / 2, (source.PixelHeight - side) / 2, side, side));
        var scaled = new TransformedBitmap(square, new System.Windows.Media.ScaleTransform(256.0 / side, 256.0 / side));

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(scaled));
        using var stream = File.Create(FilePath);
        encoder.Save(stream);
    }

    public static void Remove()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
