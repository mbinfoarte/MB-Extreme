using System.Globalization;

namespace MBExtreme.Core.Recording;

/// <summary>Ajustes de imagem (cor, nitidez e ruído) aplicados no FFmpeg. Servem para a tela inteira e para cada câmera.</summary>
public sealed record ImageFilter(double Brightness = 0, double Contrast = 1, double Saturation = 1, double Gamma = 1, double Sharpness = 0, int Denoise = 0, bool Mirror = false)
{
    public static readonly ImageFilter None = new();

    public bool IsNeutral => Math.Abs(Brightness) < 0.005 && Math.Abs(Contrast - 1) < 0.005 && Math.Abs(Saturation - 1) < 0.005 && Math.Abs(Gamma - 1) < 0.005
                             && Sharpness < 0.05 && Denoise <= 0 && !Mirror;

    /// <summary>Limites que o FFmpeg aceita sem estragar a imagem.</summary>
    public ImageFilter Clamped() => new(Math.Clamp(Brightness, -0.3, 0.3), Math.Clamp(Contrast, 0.5, 1.8), Math.Clamp(Saturation, 0.0, 2.0),
        Math.Clamp(Gamma, 0.6, 1.8), Math.Clamp(Sharpness, 0, 2), Math.Clamp(Denoise, 0, 3), Mirror);

    private static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Trecho do filtro do FFmpeg (já convertendo para o formato certo e voltando a BGRA), ou null se não há nada a fazer.</summary>
    public string? ToChain()
    {
        var f = Clamped();
        if (f.IsNeutral) return null;
        var parts = new List<string>();
        if (f.Mirror) parts.Add("hflip");
        bool color = Math.Abs(f.Brightness) >= 0.005 || Math.Abs(f.Contrast - 1) >= 0.005 || Math.Abs(f.Saturation - 1) >= 0.005 || Math.Abs(f.Gamma - 1) >= 0.005;
        if (color || f.Sharpness >= 0.05 || f.Denoise > 0) parts.Add("format=yuv420p");
        if (f.Denoise > 0) parts.Add(f.Denoise switch { 1 => "hqdn3d=2:1.5:3:2.5", 2 => "hqdn3d=4:3:6:4.5", _ => "hqdn3d=6:4:9:6" });
        if (color) parts.Add($"eq=brightness={N(f.Brightness)}:contrast={N(f.Contrast)}:saturation={N(f.Saturation)}:gamma={N(f.Gamma)}");
        if (f.Sharpness >= 0.05) parts.Add($"unsharp=luma_msize_x=5:luma_msize_y=5:luma_amount={N(f.Sharpness)}:chroma_msize_x=5:chroma_msize_y=5:chroma_amount=0");
        parts.Add("format=bgra");
        return string.Join(",", parts);
    }

    public static IReadOnlyList<(string Name, string Description, ImageFilter Filter)> Presets { get; } = new (string, string, ImageFilter)[]
    {
        ("Original", "Sem ajustes.", None),
        ("Natural", "Um pouco mais de contraste e cor, nitidez leve.", new ImageFilter(0, 1.05, 1.08, 1, 0.4)),
        ("Vívida", "Cores fortes e imagem mais viva; boa para jogos coloridos.", new ImageFilter(0.02, 1.12, 1.3, 1, 0.6)),
        ("Cinema", "Mais contraste, cores calmas e tons um pouco escuros.", new ImageFilter(0, 1.15, 0.9, 0.95, 0.3)),
        ("Nítida", "Realça os detalhes e tira um pouco do chiado da imagem.", new ImageFilter(0, 1.06, 1.0, 1, 1.2, 1)),
    };
}

public enum OverlayKind { Intro, LowerThird, Logo }

/// <summary>Uma camada de identidade do canal (entrada, faixa com o nome ou logo), já desenhada em PNG.</summary>
public sealed record OverlayLayer(OverlayKind Kind, string ImagePath, CameraCorner Corner, int WidthPercent, double StartSeconds, double EveryMinutes, double ShowSeconds)
{
    public int ClampedPercent => Math.Clamp(WidthPercent, 4, 60);

    public double Show => Math.Clamp(ShowSeconds, 2, 60);
}
