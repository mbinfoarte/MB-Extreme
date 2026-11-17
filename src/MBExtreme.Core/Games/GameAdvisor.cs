using MBExtreme.Core.Drivers;
using MBExtreme.Core.Hardware;

namespace MBExtreme.Core.Games;

public sealed record GameAdvice(string Tier, string Headline, IReadOnlyList<string> Lines);

/// <summary>
/// Sugestões de configuração dentro do jogo conforme o seu hardware. A maior parte do ganho em PC fraco vem daqui
/// (resolução, texturas, sombras), não de ajustes do Windows. São pontos de partida: ajuste conforme o jogo.
/// </summary>
public static class GameAdvisor
{
    public static int Tier(HardwareInventory hw)
    {
        var gpu = hw.Gpus.OrderByDescending(g => g.VramBytes).FirstOrDefault();
        double vramGb = (gpu?.VramBytes ?? 0) / 1024.0 / 1024 / 1024;
        string name = gpu?.Name ?? "";
        bool integrated = name.Contains("UHD", StringComparison.OrdinalIgnoreCase) || name.Contains("Iris", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Vega", StringComparison.OrdinalIgnoreCase) && vramGb <= 2 || name.Contains("Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Radeon Graphics", StringComparison.OrdinalIgnoreCase);

        int tier = integrated || vramGb < 2.5 ? 0 : vramGb < 5 ? 1 : vramGb < 9 ? 2 : vramGb < 14 ? 3 : 4;
        double ramGb = hw.TotalMemoryBytes / 1024.0 / 1024 / 1024;
        if (ramGb > 0 && ramGb < 7.5 && tier > 0) tier--;
        return tier;
    }

    public static GameAdvice Recommend(HardwareInventory hw)
    {
        int tier = Tier(hw);
        var gpu = hw.Gpus.OrderByDescending(g => g.VramBytes).FirstOrDefault();
        string gpuName = gpu?.Name ?? "placa de vídeo";
        string[] tierNames = { "Básico", "Entrada", "Intermediário", "Avançado", "Topo" };
        string[] targets =
        {
            "720p a 900p, predefinição Baixa",
            "1080p, predefinição Baixa ou Média",
            "1080p em Alta, ou 1440p em Média",
            "1440p em Alta, ou 4K em Média com upscaler",
            "4K em Alta/Ultra",
        };

        var lines = new List<string> { $"Alvo sugerido: {targets[tier]}." };
        double vramGb = (gpu?.VramBytes ?? 0) / 1024.0 / 1024 / 1024;
        if (vramGb > 0)
            lines.Add($"Memória de vídeo: {vramGb:0.#} GB. Se o jogo travar ao virar a câmera, diminua a qualidade das texturas primeiro: é o que mais usa VRAM.");

        if (tier <= 1)
            lines.Add("Desligue ou reduza: sombras, reflexos, oclusão ambiente, anti-aliasing pesado (MSAA), efeitos de partículas e distância de visão. Mantenha texturas médias se a VRAM aguentar.");
        else if (tier == 2)
            lines.Add("Sombras e oclusão ambiente em Média costumam dar o melhor equilíbrio. Evite ray tracing.");
        else
            lines.Add("Ative ray tracing só nos jogos em que a taxa de quadros continua boa; ele custa muito caro.");

        string upscaler = gpu?.Vendor switch
        {
            GpuVendor.Nvidia when gpuName.Contains("RTX", StringComparison.OrdinalIgnoreCase) => "DLSS (qualidade ou equilibrado). Em RTX 40/50 há também geração de quadros, que exige o HAGS ligado.",
            GpuVendor.Amd => "FSR. Em RX 7000/9000 existe geração de quadros (FSR 3).",
            GpuVendor.Intel when gpuName.Contains("Arc", StringComparison.OrdinalIgnoreCase) => "XeSS.",
            _ => "FSR (funciona em qualquer placa de vídeo moderna).",
        };
        lines.Add($"Upscaler: use {upscaler} Em PC fraco, o modo desempenho dele dá o maior ganho de FPS.");

        int hz = hw.RefreshRateHz;
        lines.Add($"Taxa de quadros: sua tela está a {hz} Hz. Com G-SYNC/FreeSync limite o jogo a uns 3 FPS abaixo ({hz - 3}) e use o limitador do próprio jogo (menor latência). Sem VRR, limite em {hz} e use V-Sync desligado se aceitar o rasgo de imagem.");

        if (gpu?.Vendor == GpuVendor.Nvidia)
            lines.Add("Latência: ative NVIDIA Reflex nos jogos que oferecem. Ele ajuda mais quando a GPU está no limite.");
        else if (gpu?.Vendor == GpuVendor.Amd)
            lines.Add("Latência: ative AMD Anti-Lag nos jogos compatíveis. Ele ajuda mais quando a GPU está no limite.");

        if (hw.Cpu.Cores <= 4)
            lines.Add($"Processador com {hw.Cpu.Cores} núcleos: jogos com muitos personagens podem limitar o FPS pela CPU. Reduzir resolução não ajuda nesse caso; reduza população, distância de objetos e simulação.");

        double ramGb = hw.TotalMemoryBytes / 1024.0 / 1024 / 1024;
        if (ramGb > 0 && ramGb < 15)
            lines.Add($"Memória RAM de {ramGb:0.#} GB: feche navegador e launchers antes de jogar (o Modo Jogo faz isso) e instale o jogo no SSD.");

        lines.Add("Jogo no SSD: a troca de HD para SSD é a que mais encurta tempos de carregamento (NVMe x SATA muda pouco).");

        string headline = tier switch
        {
            0 => $"{gpuName}: PC básico para jogos. Foque em baixar a resolução e os efeitos.",
            1 => $"{gpuName}: PC de entrada. Dá para jogar bem em 1080p com ajustes.",
            2 => $"{gpuName}: PC intermediário. 1080p em Alta é o ponto ideal.",
            3 => $"{gpuName}: PC avançado. 1440p é o ponto ideal.",
            _ => $"{gpuName}: PC topo de linha. Jogue em 4K.",
        };
        return new GameAdvice(tierNames[tier], headline, lines);
    }
}
