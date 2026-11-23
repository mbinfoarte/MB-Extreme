using System.Net;

namespace MBExtreme.Core.NetDiag;

/// <summary>
/// Motor de regras determinístico (sem IA): olha só o que foi medido e devolve hipóteses com evidência a favor, contra e o próximo teste.
/// Nunca afirma causa confirmada sem medição que a sustente; nunca recomenda mudar a rede sozinho.
/// </summary>
public static class NetDiagRules
{
    /// <summary>Limites usados nas regras (documentados no relatório).</summary>
    public const double GatewayLossPct = 5, GatewayMedianWiredMs = 10, GatewayMedianWifiMs = 30, GatewayJitterMs = 15;

    public static IReadOnlyList<Finding> Evaluate(NetDiagEvidence e)
    {
        var f = new List<Finding>();
        var p = e.Primary;

        if (p is null)
        {
            f.Add(new Finding("NET-IF-001", FindingSeverity.Problema, FindingStatus.Confirmado, 0.95, "Nenhuma placa de rede conectada com roteador",
                new[] { "Nenhuma interface ligada com gateway padrão." }, Array.Empty<string>(),
                "Confira o cabo de rede ou se o Wi-Fi está conectado; veja se a placa não está desativada no Windows."));
            return f;
        }

        if (p.Ipv4.Any(ip => ip.StartsWith("169.254.", StringComparison.Ordinal)))
            f.Add(new Finding("NET-IP-002", FindingSeverity.Problema, FindingStatus.Confirmado, 0.9, "O PC não recebeu endereço do roteador (APIPA 169.254)",
                new[] { $"{p.Name} está com {string.Join(", ", p.Ipv4)}: endereço automático que o Windows dá quando o DHCP não responde." },
                Array.Empty<string>(), "Reinicie o roteador e confira o cabo ou o Wi-Fi; depois renove o IP (ação B, com confirmação)."));

        if (p.Dns.Count == 0)
            f.Add(new Finding("NET-DNS-003", FindingSeverity.Problema, FindingStatus.Confirmado, 0.85, "Nenhum servidor DNS configurado",
                new[] { $"{p.Name} não tem DNS: nomes de sites não são traduzidos." }, Array.Empty<string>(), "Confira a configuração de IP da placa (DHCP ou DNS manual)."));

        if (e.GatewayAddress is { } gw && !InSameSubnet(p, gw))
            f.Add(new Finding("NET-GW-004", FindingSeverity.Atencao, FindingStatus.Provavel, 0.6, "Gateway fora da faixa de endereços da placa",
                new[] { $"Gateway {gw} não está na mesma sub-rede de {string.Join(", ", p.Ipv4)}." },
                new[] { "Algumas redes (ponto a ponto, VPN) funcionam assim de propósito." }, "Confira se o IP foi digitado à mão errado."));

        if (p.IsEthernet && p.SpeedBps is > 0 and <= 100_000_000)
            f.Add(new Finding("DR-003", FindingSeverity.Atencao, FindingStatus.Provavel, 0.6,
                $"Cabo de rede conectado a {p.SpeedBps / 1_000_000} Mbps",
                new[] { $"A placa negociou {p.SpeedBps / 1_000_000} Mbps; placas e roteadores atuais costumam negociar 1000 Mbps." },
                new[] { "Se o roteador ou a placa forem de 100 Mbps, isso é o normal deles." },
                "Troque o cabo (categoria 5e ou 6) e a porta do roteador; não force a velocidade na placa."));

        // conectividade em ordem: gateway → IP externo → DNS → HTTP
        bool gwOk = e.Gateway is { AllLost: false };
        bool extOk = e.External.Values.Any(r => !r.AllLost);
        bool extTested = e.External.Count > 0;

        if (e.Gateway is { AllLost: true } && e.Loopback is { AllLost: false })
            f.Add(new Finding("NET-GW-005", FindingSeverity.Problema, FindingStatus.Provavel, extOk ? 0.4 : 0.8, "O roteador não respondeu",
                new[] { $"Ping ao gateway {e.GatewayAddress}: {e.Gateway.Text}.", "A pilha de rede do PC respondeu normal (loopback)." },
                extOk ? new[] { "A internet respondeu: o roteador pode só estar bloqueando ping." } : Array.Empty<string>(),
                p.IsWifi ? "Aproxime-se do roteador ou teste por cabo; reinicie o roteador." : "Confira o cabo e as luzes da porta; reinicie o roteador."));
        else if (e.Gateway is { AllLost: false } g)
        {
            double medLimit = p.IsWifi ? GatewayMedianWifiMs : GatewayMedianWiredMs;
            var ev = new List<string>();
            if (g.LossPct >= GatewayLossPct) ev.Add($"Perda de {g.LossPct:0.#}% até o roteador.");
            if (g.Median > medLimit) ev.Add($"Mediana de {g.Median:0.#} ms até o roteador (esperado abaixo de {medLimit:0} ms {(p.IsWifi ? "no Wi-Fi" : "no cabo")}).");
            if (g.Jitter > GatewayJitterMs) ev.Add($"Variação de {g.Jitter:0.#} ms até o roteador.");
            if (ev.Count > 0)
                f.Add(new Finding("DR-002", FindingSeverity.Problema, FindingStatus.Provavel, ev.Count >= 2 ? 0.75 : 0.55,
                    "Instabilidade entre o PC e o roteador (rede local)", ev,
                    new[] { "Uma medição curta pode pegar um pico momentâneo: repita o teste para confirmar." },
                    p.IsWifi ? "Teste por cabo para comparar; no Wi-Fi, veja o sinal e o canal (aba Rede local / Wi-Fi)." : "Troque o cabo e a porta do roteador e teste de novo."));
        }

        if (gwOk && extTested && !extOk)
            f.Add(new Finding("NET-WAN-006", FindingSeverity.Problema, FindingStatus.Provavel, 0.75, "O roteador responde, mas a internet não",
                new[] { $"Gateway respondeu ({e.Gateway!.Text}).", "Nenhum endereço externo respondeu: " + string.Join("; ", e.External.Select(x => $"{x.Key} {x.Value.Text}")) },
                new[] { "Algumas redes bloqueiam ping para fora; o teste de página (HTTP) ajuda a confirmar." },
                "Veja as luzes do modem/ONU, reinicie modem e roteador e, se continuar, fale com a operadora."));

        // DR-001: DNS
        bool sysDnsTested = e.DnsSystem.Count > 0, sysDnsOk = e.DnsSystem.Any(d => d.Ok);
        bool altDnsOk = e.DnsAlternate.Any(d => d.Ok);
        bool vpn = e.Interfaces.Any(i => i.Up && i.IsVpn);
        if (extOk && sysDnsTested && !sysDnsOk && altDnsOk)
            f.Add(new Finding("DR-001", FindingSeverity.Problema, FindingStatus.Provavel, vpn ? 0.5 : 0.8, "Possível falha no DNS configurado",
                new[] { "A internet respondeu por endereço IP.", "O DNS do PC não traduziu os nomes testados.", "Um DNS alternativo traduziu os mesmos nomes." },
                vpn ? new[] { "Há VPN ligada: ela pode usar um DNS próprio (DNS dividido). Não troque o DNS sem checar a VPN." } : Array.Empty<string>(),
                "Limpe o cache de DNS (ação A) e teste de novo; se continuar, avalie trocar o DNS na aba DNS, com volta."));
        else if (extOk && sysDnsTested && !sysDnsOk && !altDnsOk)
            f.Add(new Finding("NET-DNS-007", FindingSeverity.Atencao, FindingStatus.Indeterminado, 0.4, "Nenhum DNS respondeu, mas a internet responde por IP",
                new[] { "DNS do PC e DNS alternativo falharam." },
                new[] { "Firewall, antivírus ou rede da empresa podem bloquear DNS externo." },
                "Teste em outro aparelho na mesma rede; veja se há bloqueio de firewall."));
        else if (sysDnsOk)
        {
            var slow = e.DnsSystem.Where(d => d.Ok && d.Ms > 200).ToList();
            if (slow.Count > 0 && slow.Count == e.DnsSystem.Count(d => d.Ok))
                f.Add(new Finding("NET-DNS-008", FindingSeverity.Atencao, FindingStatus.Provavel, 0.5, "DNS respondendo devagar",
                    new[] { $"Respostas entre {slow.Min(d => d.Ms):0} e {slow.Max(d => d.Ms):0} ms." },
                    new[] { "A primeira consulta de um nome é sempre mais lenta (sem cache). DNS mais rápido não deixa o download mais rápido." },
                    "Compare os DNS na aba DNS."));
        }

        if (e.Http is { Redirected: true })
            f.Add(new Finding("NET-HTTP-009", FindingSeverity.Atencao, FindingStatus.Provavel, 0.7, "Possível página de login da rede (portal cativo)",
                new[] { $"O teste de página foi redirecionado ({e.Http.Detail})." }, new[] { "Alguns proxies de empresa também redirecionam." },
                "Abra o navegador e faça o login da rede (hotel, aeroporto, empresa)."));
        else if (e.Http is { Ok: false } h && extOk && sysDnsOk)
            f.Add(new Finding("NET-HTTP-010", FindingSeverity.Atencao, FindingStatus.Indeterminado, 0.4, "Internet e DNS respondem, mas a página de teste não abriu",
                new[] { h.Detail }, new[] { "Proxy, firewall ou antivírus podem bloquear só esse endereço." }, "Abra um site no navegador para conferir."));

        // DR-005: perda só em salto do meio
        if (e.Hops.Count >= 2)
        {
            var last = e.Hops[^1];
            var mid = e.Hops.Take(e.Hops.Count - 1).Where(x => x.Rtt.LossPct >= 50).ToList();
            if (mid.Count > 0 && last.Rtt.LossPct < 20)
                f.Add(new Finding("DR-005", FindingSeverity.Info, FindingStatus.Provavel, 0.7, "Salto do caminho que não responde ping (normal)",
                    mid.Select(x => $"Salto {x.Ttl} ({x.Address ?? "sem resposta"}): perda {x.Rtt.LossPct:0}%.").Append($"O destino respondeu com perda {last.Rtt.LossPct:0}%.").ToList(),
                    Array.Empty<string>(), "Isso costuma ser limite de resposta do equipamento, não falha. Só preocupa se a perda seguir até o destino."));
        }

        if (!f.Any(x => x.Severity != FindingSeverity.Info))
            f.Insert(0, new Finding("NET-OK-000", FindingSeverity.Info, FindingStatus.Provavel, 0.7, "Nenhum problema encontrado nos testes feitos",
                new[] { "Roteador, internet, DNS e página de teste responderam dentro dos limites." },
                new[] { "Problemas que vêm e vão podem não aparecer numa medição curta." },
                "Se o problema voltar, rode o diagnóstico na hora em que ele acontecer."));

        return f.OrderByDescending(x => x.Severity).ThenByDescending(x => x.Confidence).ToList();
    }

    /// <summary>O gateway está na mesma sub-rede IPv4 de algum endereço da placa?</summary>
    public static bool InSameSubnet(NetIface p, string gateway)
    {
        if (!IPAddress.TryParse(gateway, out var gw) || gw.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return true;
        for (int i = 0; i < p.Ipv4.Count && i < p.Ipv4Prefix.Count; i++)
            if (IPAddress.TryParse(p.Ipv4[i], out var ip) && NetScope.SameNetwork(ip, gw, p.Ipv4Prefix[i])) return true;
        return p.Ipv4.Count == 0;
    }
}
