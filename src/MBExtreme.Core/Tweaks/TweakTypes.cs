using System.Text.RegularExpressions;

namespace MBExtreme.Core.Tweaks;

/// <summary>Grupo por peça da tela Otimizações (CPU, GPU, Memória...), em vez da categoria interna.</summary>
public enum TweakType { Cpu, Gpu, Memory, Energy, Network, Storage, Visual, Privacy, Peripherals, Boot, System }

/// <summary>Níveis da tela Otimizações: Básico (risco baixo), Avançado (baixo e médio) e Extreme (tudo).</summary>
public enum TweakLevel { Basic, Advanced, Extreme }

public static class TweakTypes
{
    public static IReadOnlyList<(TweakType Type, string Title, string Glyph)> All { get; } = new[]
    {
        (TweakType.Cpu, "Processador (CPU)", ""),
        (TweakType.Gpu, "Placa de vídeo (GPU) e jogos", ""),
        (TweakType.Memory, "Memória (RAM)", ""),
        (TweakType.Energy, "Energia", ""),
        (TweakType.Network, "Rede e internet", ""),
        (TweakType.Storage, "Armazenamento (SSD e HD)", ""),
        (TweakType.Boot, "Inicialização e desligamento", ""),
        (TweakType.Visual, "Efeitos visuais", ""),
        (TweakType.Peripherals, "Mouse e teclado", ""),
        (TweakType.Privacy, "Privacidade e Windows", ""),
        (TweakType.System, "Sistema", ""),
    };

    /// <summary>Tipo de um ajuste pelo assunto (categoria) e pelas palavras do título e da descrição.</summary>
    public static TweakType Of(TweakCategory category, string text)
    {
        string t = text.ToLowerInvariant();
        switch (category)
        {
            case TweakCategory.Network: return TweakType.Network;
            case TweakCategory.Storage: return TweakType.Storage;
            case TweakCategory.Visual: return TweakType.Visual;
            case TweakCategory.Privacy: return TweakType.Privacy;
            case TweakCategory.Peripherals: return TweakType.Peripherals;
            case TweakCategory.Boot: return TweakType.Boot;
        }
        if (Regex.IsMatch(t, "gpu|placa de vídeo|vídeo|hags|nvidia|amd|radeon|directx|shader|jogo|game|fps|tela cheia|fullscreen")) return TweakType.Gpu;
        if (Regex.IsMatch(t, "energia|plano de|hibern|suspens|bateria|estacionamento|parking|desempenho máximo")) return TweakType.Energy;
        if (Regex.IsMatch(t, @"memória|\bram\b|paginação|pagefile|cache")) return TweakType.Memory;
        if (Regex.IsMatch(t, "cpu|processador|núcleo|prioridade|timer|agendador|scheduler|interrup")) return TweakType.Cpu;
        return TweakType.System;
    }

    /// <summary>O ajuste entra neste nível? Básico = risco baixo; Avançado = baixo e médio; Extreme = todos.</summary>
    public static bool InLevel(TweakRisk risk, TweakLevel level) => level switch
    {
        TweakLevel.Basic => risk == TweakRisk.Low,
        TweakLevel.Advanced => risk is TweakRisk.Low or TweakRisk.Medium,
        _ => true,
    };
}
