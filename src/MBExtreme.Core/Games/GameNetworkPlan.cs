namespace MBExtreme.Core.Games;

/// <summary>O que "Otimizar rede deste jogo" faz sem servidor próprio: ajustes de baixo risco do Windows e política de QoS do jogo. Trocar o caminho dos pacotes precisa de servidor (veja docs/REDE-PRIVADA-JOGOS.md).</summary>
public static class GameNetworkPlan
{
    public static readonly IReadOnlyList<string> TweakIds = new[] { "net-throttling", "net-qos-limit", "net-adapter-power-off" };

    public static IReadOnlyList<SettingRow> Rows(IReadOnlyDictionary<string, bool?> tweakStates, IReadOnlyDictionary<string, string> titles, bool? qosActive, bool hasExe)
    {
        var rows = new List<SettingRow>();
        foreach (string id in TweakIds)
        {
            if (!titles.TryGetValue(id, out string? title)) continue;
            bool? on = tweakStates.GetValueOrDefault(id);
            rows.Add(new SettingRow("Rede", title, on switch { true => "Ligado", false => "Desligado", _ => "Não foi possível ler" }, "Ligado",
                on == true ? RowState.Ok : on == false ? RowState.Different : RowState.Info, "", on == false));
        }

        if (hasExe)
            rows.Add(new SettingRow("Rede", "Prioridade de rede do jogo (QoS, DSCP 46)",
                qosActive switch { true => "Ligada", false => "Desligada", _ => "Não foi possível ler" }, "Ligada",
                qosActive == true ? RowState.Ok : qosActive == false ? RowState.Different : RowState.Info,
                "Marca os pacotes do jogo como prioritários. Só ajuda se o seu roteador respeitar a marcação (a maioria dos provedores ignora), então o ganho é pequeno. Reversível.", qosActive == false));

        rows.Add(new SettingRow("Rede", "Rota por servidor próprio", "Não configurada", "Precisa de servidor", RowState.Info,
            "Trocar o caminho dos pacotes até o servidor do jogo só é possível passando por um servidor de repasse (como ExitLag/NoPing fazem). Veja docs/REDE-PRIVADA-JOGOS.md. Use a aba Rede para ver onde está o atraso.", false));
        return rows;
    }
}
