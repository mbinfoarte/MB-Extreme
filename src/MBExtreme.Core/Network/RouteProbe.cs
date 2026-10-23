namespace MBExtreme.Core.Network;

/// <summary>Mede o ping até um servidor e o caminho salto a salto, e monta o diagnóstico. Usa só ping e TCPing; não instala nada.</summary>
public static class RouteProbe
{
    public static async Task<RouteDiagnosis> RunAsync(string host, int tcpPort, int pingCount, int traceRounds, IProgress<string>? progress, CancellationToken token)
    {
        var dest = new HopStats { Hop = 0, Address = host };
        progress?.Report("Medindo o ping...");
        for (int i = 0; i < pingCount; i++)
        {
            token.ThrowIfCancellationRequested();
            dest.Add(await NetAnalyzer.PingAsync(host, 1500));
            await Task.Delay(150, token);
        }

        bool tcpFallback = false;
        if (dest.Received == 0 && tcpPort > 0)
        {
            // muitos servidores de jogo bloqueiam ping; mede o tempo para abrir uma conexão TCP
            progress?.Report("O servidor não responde ao ping; medindo por TCP...");
            tcpFallback = true;
            dest = new HopStats { Hop = 0, Address = host };
            for (int i = 0; i < pingCount; i++)
            {
                token.ThrowIfCancellationRequested();
                dest.Add(await NetAnalyzer.TcpPingAsync(host, tcpPort, 1500, token));
                await Task.Delay(150, token);
            }
        }

        progress?.Report("Mapeando o caminho...");
        IReadOnlyList<HopStats> hops = Array.Empty<HopStats>();
        int rounds = 0;
        using var traceCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        try
        {
            await NetAnalyzer.TraceAsync(host, 30, h =>
            {
                hops = h;
                if (++rounds >= traceRounds) traceCts.Cancel();
                progress?.Report($"Mapeando o caminho ({Math.Min(rounds, traceRounds)}/{traceRounds})...");
            }, traceCts.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { /* terminou as rodadas */ }

        var summaries = hops.Select(RouteAnalyzer.Summarize).ToList();
        double avg = dest.Average, jitter = dest.Jitter, loss = dest.LossPercent;
        var findings = RouteAnalyzer.Analyze(summaries, avg, loss, jitter).ToList();
        if (dest.Received == 0)
            findings.Insert(0, new RouteFinding(FindingLevel.Warn, "O servidor não respondeu", "Nem ao ping nem por TCP. O endereço pode estar bloqueando testes; a análise do caminho ainda mostra os saltos."));
        else if (tcpFallback)
            findings.Insert(0, new RouteFinding(FindingLevel.Info, "Medido por TCP", "O servidor não responde ao ping, então o tempo mostrado é o de abrir uma conexão TCP (próximo do ping real)."));

        return new RouteDiagnosis(host, dest.Sent, avg, dest.BestOrZero, dest.Worst, jitter, loss, summaries, findings,
            dest.Received == 0 ? "Sem resposta" : RouteAnalyzer.Verdict(avg, jitter, loss));
    }
}
