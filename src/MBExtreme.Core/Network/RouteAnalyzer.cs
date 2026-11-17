namespace MBExtreme.Core.Network;

/// <summary>Resumo de um salto do caminho até o servidor.</summary>
public sealed record HopSummary(int Hop, string Address, double Average, double LossPercent, double Jitter, int Received);

public enum FindingLevel { Info, Warn, Bad }

public sealed record RouteFinding(FindingLevel Level, string Title, string Detail);

/// <summary>Resultado completo da análise de rota até um servidor de jogo.</summary>
public sealed record RouteDiagnosis(string Host, int Samples, double Average, double Min, double Max, double Jitter, double LossPercent,
    IReadOnlyList<HopSummary> Hops, IReadOnlyList<RouteFinding> Findings, string Verdict);

/// <summary>Interpreta ping e caminho salto a salto para dizer onde está o atraso. Só lê números; não muda nada no PC.</summary>
public static class RouteAnalyzer
{
    public static HopSummary Summarize(HopStats s) => new(s.Hop, s.Address, s.Average, s.LossPercent, s.Jitter, s.Received);

    public static string Verdict(double average, double jitter, double loss)
    {
        if (loss >= 3 || jitter >= 25 || average >= 150) return "Ruim";
        if (loss >= 1 || jitter >= 10 || average >= 100) return "Regular";
        if (average <= 30 && jitter <= 5 && loss < 0.5) return "Excelente";
        return "Bom";
    }

    public static IReadOnlyList<RouteFinding> Analyze(IReadOnlyList<HopSummary> hops, double destAverage, double destLoss, double destJitter)
    {
        var list = new List<RouteFinding>();

        if (destLoss >= 3) list.Add(new(FindingLevel.Bad, "Perda de pacotes alta", $"{destLoss:0.#}% dos pacotes se perdem até o servidor. Isso causa travadas e teleportes mesmo com ping baixo. Teste com cabo no lugar do Wi-Fi e fora do horário de pico."));
        else if (destLoss >= 1) list.Add(new(FindingLevel.Warn, "Alguma perda de pacotes", $"{destLoss:0.#}% de perda até o servidor. Se aparecer sempre, vale testar cabo de rede e falar com o provedor."));

        if (destJitter >= 25) list.Add(new(FindingLevel.Bad, "Oscilação (jitter) muito alta", $"O ping varia {destJitter:0.#} ms entre um pacote e outro, o que dá \"picos\" de lag."));
        else if (destJitter >= 10) list.Add(new(FindingLevel.Warn, "Oscilação (jitter) alta", $"O ping varia {destJitter:0.#} ms entre um pacote e outro."));

        var first = hops.FirstOrDefault(h => h.Received > 0);
        if (first is not null)
        {
            if (first.Average >= 15) list.Add(new(FindingLevel.Bad, "Problema na sua rede local", $"O primeiro salto (seu roteador, {first.Address}) responde em {first.Average:0.#} ms. Em cabo deveria ser menos de 2 ms. Veja Wi-Fi fraco, cabo ruim ou roteador sobrecarregado."));
            else if (first.Average >= 5) list.Add(new(FindingLevel.Warn, "Rede local um pouco lenta", $"O primeiro salto ({first.Address}) responde em {first.Average:0.#} ms. Em cabo costuma ser menos de 2 ms; no Wi-Fi é normal ser maior."));
        }

        // saltos que aumentam o atraso e continuam altos até o destino (não só um roteador que demora a responder ao ping)
        var answered = hops.Where(h => h.Received > 0).ToList();
        for (int i = 1; i < answered.Count; i++)
        {
            double jump = answered[i].Average - answered[i - 1].Average;
            bool sustained = destAverage >= answered[i].Average * 0.9;
            if (jump < 40 || !sustained) continue;
            var level = jump >= 80 ? FindingLevel.Bad : FindingLevel.Warn;
            list.Add(new(level, $"Salto de {jump:0} ms no caminho", $"O atraso sobe de {answered[i - 1].Average:0} para {answered[i].Average:0} ms entre o salto {answered[i - 1].Hop} ({answered[i - 1].Address}) e o {answered[i].Hop} ({answered[i].Address}) e continua alto até o destino. Esse trecho pode ser uma rota longa (por exemplo, o provedor passando por outra cidade). Só o provedor ou um servidor de rota diferente muda isso."));
            break;
        }

        foreach (var h in hops.Where(h => h.LossPercent >= 20 && h.Hop > 1).Take(2))
        {
            if (destLoss < 1)
                list.Add(new(FindingLevel.Info, $"Salto {h.Hop} não responde ao teste", $"{h.Address} perdeu {h.LossPercent:0}% dos testes, mas o destino não perde pacotes. Normalmente o roteador só ignora o ping e isso não afeta o jogo."));
        }

        if (list.Count == 0) list.Add(new(FindingLevel.Info, "Nada de errado encontrado", "Ping, oscilação e perda estão dentro do esperado. Um servidor de rota diferente só ajudaria se o caminho do seu provedor for muito mais longo do que o necessário."));
        return list;
    }
}
