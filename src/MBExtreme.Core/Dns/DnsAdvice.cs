namespace MBExtreme.Core.Dns;

/// <summary>Para que cada DNS é melhor (jogos, navegação, streaming, proteção), pela velocidade medida e pelo jeito de cada serviço.</summary>
public static class DnsAdvice
{
    /// <summary>
    /// Etiquetas de uso de um resultado. Jogos = o mais rápido da medição; Navegação = rápido (até 1,5× o melhor) e respondeu tudo;
    /// Streaming = Google ou o do provedor (mandam a sua região ao YouTube e à Netflix, que entregam o vídeo do servidor mais perto);
    /// Proteção = os que bloqueiam golpes e anúncios.
    /// </summary>
    public static IReadOnlyList<string> Uses(DnsResult result, IReadOnlyList<DnsResult> all)
    {
        var uses = new List<string>();
        var answered = all.Where(r => r.AverageMs is not null).ToList();
        if (result.AverageMs is not double ms || answered.Count == 0) return uses;
        double best = answered.Min(r => r.AverageMs!.Value);
        bool reliable = result.Attempts > 0 && result.Successes == result.Attempts;
        if (Math.Abs(ms - best) < 0.01) uses.Add("Jogos");
        if (reliable && ms <= best * 1.5 + 3) uses.Add("Navegação");
        string name = result.Provider.Name;
        if (name.StartsWith("Google", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Provedor", StringComparison.OrdinalIgnoreCase)) uses.Add("Streaming");
        if (name.StartsWith("Quad9", StringComparison.OrdinalIgnoreCase) || name.StartsWith("AdGuard", StringComparison.OrdinalIgnoreCase)) uses.Add("Proteção");
        return uses;
    }

    public static string Explain(string use) => use switch
    {
        "Jogos" => "menor tempo de resposta agora",
        "Navegação" => "rápido e respondeu todas as consultas",
        "Streaming" => "indica a sua região ao YouTube, Netflix e Twitch",
        "Proteção" => "bloqueia sites de golpe" ,
        _ => "",
    };
}
