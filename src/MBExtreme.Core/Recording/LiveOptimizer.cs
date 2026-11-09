namespace MBExtreme.Core.Recording;

/// <summary>O que foi medido da internet e do PC antes da live.</summary>
public sealed record LiveProbe(double? UploadMbps, double PingMs, double JitterMs, double LossPercent, bool Responded, bool OnWifi, int PcTier);

public enum AdviceLevel { Good, Warning, Bad }

/// <summary>Recomendação pronta para aplicar: resolução, quadros e limite de taxa, com os motivos e dicas em português.</summary>
public sealed record LiveAdvice(AdviceLevel Level, int Height, int Fps, int CapMbps, int VideoMbps, IReadOnlyList<string> Reasons, IReadOnlyList<string> Tips);

/// <summary>
/// Escolhe as configurações da live que cabem na internet medida. Regra de ouro: a taxa de vídeo fica bem abaixo do envio livre
/// (a internet de casa oscila), e quanto pior a conexão até a plataforma (perda, jitter), mais folga se deixa.
/// </summary>
public static class LiveOptimizer
{
    private static readonly (int Height, int Fps)[] Ladder = { (1080, 60), (1080, 30), (720, 60), (720, 30) };

    public static LiveAdvice Recommend(LiveProbe probe, StreamService service, int audioKbps)
    {
        var spec = PlatformSpecs.For(service);
        var reasons = new List<string>();
        var tips = new List<string>();
        var level = AdviceLevel.Good;
        void Worse(AdviceLevel l) { if (l > level) level = l; }

        if (probe.UploadMbps is not { } upload || upload <= 0)
        {
            reasons.Add("Não consegui medir o envio da internet; usei o conjunto mais leve e seguro (720p a 30 fps, 3 Mbps).");
            tips.Add("Rode o teste de novo com a internet livre (feche downloads, nuvem e outros usos).");
            return new LiveAdvice(AdviceLevel.Warning, 720, 30, 3, 3, reasons, tips);
        }

        // folga conforme a qualidade da ligação até a plataforma
        double share = 0.7;
        if (probe.Responded)
        {
            if (probe.LossPercent >= 3 || probe.JitterMs >= 30) { share = 0.45; Worse(AdviceLevel.Bad); }
            else if (probe.LossPercent >= 1 || probe.JitterMs >= 15 || probe.PingMs >= 150) { share = 0.55; Worse(AdviceLevel.Warning); }
        }
        if (probe.OnWifi) share = Math.Min(share, 0.55);

        double usable = upload * share - audioKbps / 1000.0;
        int cap = Math.Max(1, (int)Math.Floor(usable));
        cap = Math.Min(cap, spec.MaxVideoKbps / 1000);

        // maior degrau que o PC e a plataforma aguentam e que cabe na taxa
        int maxHeight = probe.PcTier <= 1 ? 720 : spec.MaxHeight;
        int maxFps = probe.PcTier <= 0 ? 30 : spec.MaxFps;
        (int Height, int Fps) pick = Ladder[^1];
        foreach (var step in Ladder)
        {
            if (step.Height > maxHeight || step.Fps > maxFps) continue;
            if (PlatformSpecs.BitrateRange(step.Height, step.Fps).Min <= cap * 1000) { pick = step; break; }
        }

        var range = PlatformSpecs.BitrateRange(pick.Height, pick.Fps);
        int video = Math.Clamp(cap * 1000, range.Min, Math.Max(range.Min, range.Max)) / 1000;
        video = Math.Max(1, Math.Min(video, cap));

        reasons.Add($"Seu envio medido: {upload:0.#} Mbps. Para a live não travar, uso só {share:P0} dele: até {cap} Mbps de vídeo.");
        reasons.Add($"Com isso, a melhor qualidade segura é {pick.Height}p a {pick.Fps} fps, a {video} Mbps.");
        if (probe.Responded)
            reasons.Add($"Até {spec.Name}: ping {probe.PingMs:0} ms, oscilação {probe.JitterMs:0.#} ms, perda {probe.LossPercent:0.#}%.");
        else
            reasons.Add($"O servidor da {spec.Name} não respondeu ao teste de ping (isso é comum); a decisão usou só a velocidade de envio.");

        if (cap * 1000 < PlatformSpecs.BitrateRange(720, 30).Min) { Worse(AdviceLevel.Bad); tips.Add("O envio da sua internet é baixo demais para uma live boa. Contrate mais envio (upload) ou transmita só em momentos de pouco uso."); }
        if (probe.OnWifi) tips.Add("Você está no Wi-Fi: cabo de rede é o que mais resolve travadas de live. Se não der, use 5 GHz perto do roteador.");
        if (probe.LossPercent >= 1) tips.Add("Há perda de pacotes até a plataforma: teste fora do horário de pico e com cabo; se continuar, fale com o provedor.");
        if (probe.JitterMs >= 15) tips.Add("A oscilação do ping está alta: feche downloads, atualizações, nuvem e streaming de vídeo em outros aparelhos durante a live.");
        if (probe.PcTier <= 1) tips.Add("Seu PC é mais simples: a live fica limitada a 720p para o jogo não perder FPS. Use o codificador da placa de vídeo (NVENC, AMF ou Quick Sync).");
        tips.Add("Dentro do jogo, limite o FPS ao que o monitor mostra: o PC sobra e a codificação da live não disputa a placa com o jogo.");
        tips.Add("Na plataforma, deixe o atraso normal ou baixo (não o ultrabaixo): ele dá mais margem para a internet oscilar sem travar quem assiste.");
        return new LiveAdvice(level, pick.Height, pick.Fps, cap, video, reasons, tips);
    }
}
