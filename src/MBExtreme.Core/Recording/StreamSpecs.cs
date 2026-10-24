namespace MBExtreme.Core.Recording;

/// <summary>Padrões de transmissão de uma plataforma (valores de referência das próprias plataformas; podem mudar, confira no painel delas).</summary>
public sealed record PlatformSpec(
    StreamService Service,
    string Name,
    int MaxHeight,
    int MaxFps,
    int MaxVideoKbps,
    int KeyframeSeconds,
    int RecommendedAudioKbps,
    int MaxAudioKbps,
    string Video,
    string Note);

public static class PlatformSpecs
{
    public static readonly PlatformSpec Twitch = new(StreamService.Twitch, "Twitch", 1080, 60, 6000, 2, 160, 160,
        "H.264, taxa constante (CBR), AAC", "Até 1080p a 60 fps com 6 Mbps; acima disso quem assiste pode travar.");

    public static readonly PlatformSpec YouTube = new(StreamService.YouTube, "YouTube", 2160, 60, 51000, 2, 128, 384,
        "H.264, taxa constante, AAC estéreo", "Aceita até 4K a 60 fps; a faixa recomendada sobe com a resolução.");

    public static readonly PlatformSpec Facebook = new(StreamService.Facebook, "Facebook", 1080, 30, 6000, 2, 128, 256,
        "H.264, taxa constante, AAC estéreo", "Até 1080p; use 30 fps para a transmissão ficar estável.");

    public static readonly PlatformSpec Custom = new(StreamService.Custom, "Outro servidor", 2160, 60, 20000, 2, 160, 320,
        "H.264, taxa constante, AAC", "Siga o que o servidor informa (Kick, TikTok, servidor próprio...).");

    public static PlatformSpec For(StreamService service) => service switch
    {
        StreamService.Twitch => Twitch,
        StreamService.YouTube => YouTube,
        StreamService.Facebook => Facebook,
        _ => Custom,
    };

    /// <summary>Faixa de taxa recomendada (kbps) para a resolução e os quadros por segundo (referência: tabela de codificação do YouTube).</summary>
    public static (int Min, int Max) BitrateRange(int height, int fps)
    {
        bool high = fps > 30;
        return height switch
        {
            <= 720 => high ? (4500, 7500) : (2500, 5000),
            <= 1080 => high ? (6000, 9000) : (3000, 6000),
            <= 1440 => high ? (9000, 18000) : (6000, 13000),
            _ => high ? (20000, 51000) : (13000, 34000),
        };
    }
}

/// <summary>O que a transmissão vai usar de verdade: já respeita o limite da plataforma, o limite escolhido e a internet disponível.</summary>
public sealed record StreamPlan(int Height, int Fps, int VideoKbps, int AudioKbps, int RequiredUploadKbps, IReadOnlyList<string> Notes)
{
    public double VideoMbps => VideoKbps / 1000.0;

    /// <summary>Monta o plano. <paramref name="requestedMbps"/> 0 = automático; <paramref name="userCapMbps"/> 0 = só o limite da plataforma.</summary>
    public static StreamPlan Build(StreamService service, int sourceHeight, int fps, int requestedMbps, int userCapMbps, int audioKbps)
    {
        var spec = PlatformSpecs.For(service);
        var notes = new List<string>();

        int height = sourceHeight <= 0 ? spec.MaxHeight : sourceHeight;
        if (height > spec.MaxHeight)
        {
            notes.Add($"A {spec.Name} aceita até {spec.MaxHeight}p: a imagem será reduzida para {spec.MaxHeight}p ao transmitir.");
            height = spec.MaxHeight;
        }

        int rate = fps;
        if (rate > spec.MaxFps)
        {
            notes.Add($"A {spec.Name} funciona melhor a {spec.MaxFps} fps: a transmissão usará {spec.MaxFps} fps.");
            rate = spec.MaxFps;
        }

        var (min, max) = PlatformSpecs.BitrateRange(height, rate);
        int video = requestedMbps > 0 ? requestedMbps * 1000 : Math.Min(max, (min * 2 + max) / 3);
        if (video > spec.MaxVideoKbps)
        {
            video = spec.MaxVideoKbps;
            notes.Add($"Taxa limitada a {spec.MaxVideoKbps / 1000.0:0.#} Mbps, o máximo da {spec.Name}.");
        }

        if (userCapMbps > 0 && video > userCapMbps * 1000)
        {
            video = userCapMbps * 1000;
            notes.Add($"Taxa limitada a {userCapMbps} Mbps pelo limite de banda que você escolheu.");
        }

        if (video < min)
            notes.Add($"{video / 1000.0:0.#} Mbps é pouco para {height}p a {rate} fps (o recomendado é a partir de {min / 1000.0:0.#} Mbps). Baixe a resolução ou aumente o limite.");

        int audio = Math.Clamp(audioKbps <= 0 ? spec.RecommendedAudioKbps : audioKbps, 64, spec.MaxAudioKbps);
        if (audioKbps > spec.MaxAudioKbps) notes.Add($"Áudio limitado a {spec.MaxAudioKbps} kbps, o máximo da {spec.Name}.");

        return new StreamPlan(height, rate, video, audio, RequiredUpload(video, audio), notes);
    }

    /// <summary>Envio mínimo da internet: o vídeo e o áudio mais 25% de folga, arredondado para cima em centenas de kbps.</summary>
    public static int RequiredUpload(int videoKbps, int audioKbps) => (int)Math.Ceiling((videoKbps + audioKbps) * 1.25 / 100.0) * 100;

    /// <summary>Taxa de vídeo (Mbps inteiros) que cabe no envio medido, deixando 30% de folga para o jogo e o resto da casa.</summary>
    public static int FitToUpload(double uploadMbps, int audioKbps)
    {
        double usable = uploadMbps * 0.7 - audioKbps / 1000.0;
        return Math.Max(1, (int)Math.Floor(usable));
    }
}
