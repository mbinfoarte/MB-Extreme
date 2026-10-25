namespace MBExtreme.Core.Recording;

/// <summary>Configuração leve para a live: usa o codificador da placa de vídeo e poupa o processador e a GPU para o jogo.</summary>
public sealed record LightSetup(int EncoderIndex, int Height, int Fps, int QualityIndex, int CapMbps, IReadOnlyList<string> Notes);

public static class LiveLight
{
    /// <summary>Melhor codificador H.264 para transmitir: placa NVIDIA, depois AMD, depois Intel; o processador só se não houver nada melhor.</summary>
    public static int BestEncoderIndex(IReadOnlyList<EncoderOption> encoders)
    {
        int best = -1, bestScore = -1;
        for (int i = 0; i < encoders.Count; i++)
        {
            var e = encoders[i];
            int score = (e.Kind switch { EncoderKind.Nvenc => 40, EncoderKind.Amf => 30, EncoderKind.Qsv => 20, _ => 0 })
                      + (e.Codec == VideoCodec.H264 ? 5 : 0);   // as plataformas de live aceitam H.264
            if (score > bestScore) { best = i; bestScore = score; }
        }
        return best;
    }

    /// <summary>Escolhe um conjunto que pesa pouco: 1080p só em PC forte, 30 quadros em PC fraco, esforço "rápido" do codificador.</summary>
    public static LightSetup Plan(IReadOnlyList<EncoderOption> encoders, int pcTier, StreamService service, int currentHeight, int currentFps)
    {
        var notes = new List<string>();
        int index = BestEncoderIndex(encoders);
        if (index < 0) { notes.Add("Nenhum codificador foi detectado ainda: clique em \"Detectar codificadores da minha placa\"."); index = 0; }
        else if (encoders[index].Kind == EncoderKind.Software)
            notes.Add("Só há codificador por processador (x264): ele pesa muito no jogo. Atualize o driver da placa de vídeo e detecte de novo.");
        else
            notes.Add($"Codificador escolhido: {encoders[index].Title} (usa o chip de vídeo da placa, quase sem pesar no jogo).");

        var spec = PlatformSpecs.For(service);
        int height = pcTier >= 3 ? Math.Min(1080, spec.MaxHeight) : 720;
        if (currentHeight > 0 && currentHeight < height) height = currentHeight;   // nunca sobe a resolução que o usuário já baixou
        int fps = pcTier >= 3 ? Math.Min(currentFps <= 0 ? 60 : currentFps, spec.MaxFps) : 30;
        int cap = height >= 1080 ? (fps >= 60 ? 6 : 5) : (fps >= 60 ? 5 : 4);
        cap = Math.Min(cap, spec.MaxVideoKbps / 1000);

        notes.Add($"{height}p a {fps} fps, taxa até {cap} Mbps e esforço do codificador \"Rápido\": é o que menos pesa e ainda fica bom para live.");
        notes.Add("Dica: filtros de imagem, cópia vertical e legendas em tempo real usam o processador; deixe desligados na live se sentir peso.");
        return new LightSetup(index, height, fps, 0, cap, notes);
    }
}
