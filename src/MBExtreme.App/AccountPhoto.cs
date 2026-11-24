using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;

namespace MBExtreme.UI;

/// <summary>
/// Foto da conta do site (a do Google, por exemplo), que vem assinada dentro da licença. Fica guardada no PC e é usada quando a pessoa
/// não escolheu uma foto própria no programa. Só baixa de endereços https conhecidos, até 2 MB, e só se for imagem de verdade.
/// </summary>
internal static class AccountPhoto
{
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme");
    private static string FilePath => Path.Combine(Folder, "avatar-conta.png");
    private static string SourcePath => Path.Combine(Folder, "avatar-conta.url");
    private static Task? _running;

    /// <summary>Hosts aceitos para a foto da conta.</summary>
    public static bool IsAllowed(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps &&
        (u.Host.EndsWith(".googleusercontent.com", StringComparison.OrdinalIgnoreCase) ||
         u.Host.Equals("firebasestorage.googleapis.com", StringComparison.OrdinalIgnoreCase) ||
         u.Host.Equals("storage.googleapis.com", StringComparison.OrdinalIgnoreCase));

    public static BitmapImage? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
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

    /// <summary>Foto a mostrar: a escolhida no programa ou, se não houver, a da conta.</summary>
    public static BitmapImage? Current() => ProfileStore.Load() ?? Load();

    /// <summary>Baixa a foto se o endereço mudou. <paramref name="onReady"/> é chamado (na thread da tela) quando houver foto nova.</summary>
    public static void Sync(string? url, Action onReady)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            Remove();
            return;
        }
        if (!IsAllowed(url) || _running is { IsCompleted: false }) return;
        try { if (File.Exists(SourcePath) && File.ReadAllText(SourcePath) == url && File.Exists(FilePath)) return; }
        catch (IOException) { }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        _running = Task.Run(async () =>
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 2_000_000) return;
                byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                if (bytes.Length is 0 or > 2_000_000) return;

                using var ms = new MemoryStream(bytes);
                var source = new BitmapImage();
                source.BeginInit();
                source.CacheOption = BitmapCacheOption.OnLoad;
                source.StreamSource = ms;
                source.DecodePixelWidth = 256;
                source.EndInit();
                source.Freeze();

                int side = Math.Min(source.PixelWidth, source.PixelHeight);
                if (side < 16) return;
                var square = new CroppedBitmap(source, new System.Windows.Int32Rect((source.PixelWidth - side) / 2, (source.PixelHeight - side) / 2, side, side));
                Directory.CreateDirectory(Folder);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(square));
                using (var stream = File.Create(FilePath)) encoder.Save(stream);
                File.WriteAllText(SourcePath, url);
                dispatcher?.BeginInvoke(onReady);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or NotSupportedException
                                           or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                // sem foto da conta: ficam as iniciais
            }
        });
    }

    public static void Remove()
    {
        try
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
            if (File.Exists(SourcePath)) File.Delete(SourcePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
