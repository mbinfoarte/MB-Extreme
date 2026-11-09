namespace MBExtreme.Core.Wallpaper;

/// <summary>Como a imagem preenche a tela (mesmas opções do Windows).</summary>
public enum WallpaperFit { Fill, Fit, Stretch, Tile, Center, Span }

/// <summary>Um monitor ligado, com a resolução que ele está usando.</summary>
public sealed record WallpaperMonitor(string Id, string Name, int Width, int Height, int X, int Y, bool Primary)
{
    public string Resolution => Width > 0 && Height > 0 ? $"{Width} × {Height}" : "—";
}

public static class WallpaperFitText
{
    public static string Label(WallpaperFit fit) => fit switch
    {
        WallpaperFit.Fill => "Preencher",
        WallpaperFit.Fit => "Ajustar",
        WallpaperFit.Stretch => "Esticar",
        WallpaperFit.Tile => "Lado a lado",
        WallpaperFit.Center => "Centralizar",
        WallpaperFit.Span => "Esticar nos monitores",
        _ => fit.ToString(),
    };

    /// <summary>Código da posição no Windows (DESKTOP_WALLPAPER_POSITION): Center=0, Tile=1, Stretch=2, Fit=3, Fill=4, Span=5.</summary>
    public static int Position(WallpaperFit fit) => fit switch
    {
        WallpaperFit.Center => 0,
        WallpaperFit.Tile => 1,
        WallpaperFit.Stretch => 2,
        WallpaperFit.Fit => 3,
        WallpaperFit.Fill => 4,
        WallpaperFit.Span => 5,
        _ => 4,
    };

    /// <summary>(WallpaperStyle, TileWallpaper) do registro, para o modo antigo (SystemParametersInfo), quando o COM não existe.</summary>
    public static (string Style, string Tile) LegacyRegistry(WallpaperFit fit) => fit switch
    {
        WallpaperFit.Center => ("0", "0"),
        WallpaperFit.Tile => ("0", "1"),
        WallpaperFit.Stretch => ("2", "0"),
        WallpaperFit.Fit => ("6", "0"),
        WallpaperFit.Fill => ("10", "0"),
        WallpaperFit.Span => ("22", "0"),
        _ => ("10", "0"),
    };
}
