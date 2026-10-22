namespace MBExtreme.Core.Recording;

/// <summary>Predefinição de qualidade do estúdio: o que a maioria das pessoas precisa escolher.</summary>
public sealed record StudioPreset(string Name, string Description, int ResolutionHeight, int Fps, int BitrateMbps);

public static class StudioPresets
{
    public static readonly StudioPreset Light = new("Leve", "720p a 30 fps. Pesa pouco; bom para PC fraco ou internet lenta.", 720, 30, 4);
    public static readonly StudioPreset Balanced = new("Equilibrado", "1080p a 60 fps. Recomendado para a maioria.", 1080, 60, 8);
    public static readonly StudioPreset Quality = new("Qualidade", "1440p a 60 fps com taxa alta. Só para gravar (a transmissão limita a 8 Mbps).", 1440, 60, 20);

    public static IReadOnlyList<StudioPreset> All { get; } = new[] { Light, Balanced, Quality };

    /// <summary>Predefinição sugerida para o nível do PC (0 = fraco a 4 = muito forte, como no Assistente de jogo).</summary>
    public static StudioPreset ForTier(int tier) => tier <= 1 ? Light : Balanced;

    /// <summary>Ao transmitir, a taxa nunca passa do limite aceito pelas plataformas.</summary>
    public static int StreamBitrate(StudioPreset preset) => Math.Min(preset.BitrateMbps, 8);
}

public enum StreamHealthLevel { Good, Warning, Bad }

public sealed record StreamHealth(StreamHealthLevel Level, string Message);

public static class StreamHealthRules
{
    /// <summary>Avalia a transmissão com os números da própria sessão (quadros perdidos e taxa real contra a alvo).</summary>
    public static StreamHealth Evaluate(double fps, double targetFps, double bitrateKbps, double targetKbps, long droppedFrames, long totalFrames, bool disconnected)
    {
        if (disconnected) return new(StreamHealthLevel.Bad, "A conexão com a plataforma caiu. Confira a internet e a chave de transmissão.");
        double dropRatio = totalFrames <= 0 ? 0 : (double)droppedFrames / totalFrames;
        if (dropRatio > 0.05) return new(StreamHealthLevel.Bad, $"{dropRatio:P0} dos quadros foram perdidos. Baixe a resolução ou escolha a predefinição Leve.");
        if (targetFps > 0 && fps > 0 && fps < targetFps * 0.85) return new(StreamHealthLevel.Warning, $"O PC está gerando {fps:0} fps de {targetFps:0}. Feche programas pesados ou baixe a resolução.");
        if (targetKbps > 0 && bitrateKbps > 0 && bitrateKbps < targetKbps * 0.5) return new(StreamHealthLevel.Warning, "A taxa enviada está bem abaixo da configurada: cena parada ou internet limitando o envio.");
        if (dropRatio > 0.01) return new(StreamHealthLevel.Warning, $"{dropRatio:P1} dos quadros perdidos. Fique de olho.");
        return new(StreamHealthLevel.Good, "Transmissão estável.");
    }
}

public static class PreviewRules
{
    /// <summary>Imagem de pré-visualização quase toda preta (BGRA): jogos em DirectX pelo modo GDI costumam sair assim.</summary>
    public static bool IsMostlyBlack(ReadOnlySpan<byte> bgra, int threshold = 12, double ratio = 0.98)
    {
        int pixels = bgra.Length / 4;
        if (pixels == 0) return false;
        int dark = 0;
        for (int i = 0; i + 3 < bgra.Length; i += 4)
            if (bgra[i] <= threshold && bgra[i + 1] <= threshold && bgra[i + 2] <= threshold) dark++;
        return (double)dark / pixels >= ratio;
    }

    /// <summary>Tamanho da pré-visualização mantendo a proporção, com a largura máxima dada.</summary>
    public static (int Width, int Height) Fit(int sourceWidth, int sourceHeight, int maxWidth)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0) return (maxWidth, maxWidth * 9 / 16);
        double scale = Math.Min(1.0, (double)maxWidth / sourceWidth);
        return (Math.Max(16, (int)(sourceWidth * scale)), Math.Max(16, (int)(sourceHeight * scale)));
    }
}
