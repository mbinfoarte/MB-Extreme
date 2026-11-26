using System.Net;
using System.Text.Json;

namespace MBExtreme.Core.NetDiag;

// Central de Rede (Otimizar › Central de Rede): regras sem acesso ao Windows, para testar.
// Medir antes de recomendar; nada inventado: o que não foi medido aparece como "Não testado" ou "Não aplicável".

// ======================================================================== Wi-Fi

/// <summary>Uma rede Wi-Fi vista pela placa (um ponto de acesso). Sinal em dBm, frequência em MHz.</summary>
public sealed record WifiBss(string Ssid, string Bssid, int FrequencyMhz, int Rssi, string Security, bool Connected = false);

/// <summary>Leitura do Wi-Fi: a conexão atual (se houver) e todas as redes vistas.</summary>
public sealed record WifiScan(IReadOnlyList<WifiBss> Networks, double? RxMbps = null, double? TxMbps = null)
{
    public WifiBss? Current => Networks.FirstOrDefault(n => n.Connected);
}

/// <summary>Resultado da análise do Wi-Fi: nota, melhor canal por banda e o que foi encontrado.</summary>
public sealed record WifiAnalysis(int Score, IReadOnlyDictionary<string, int> BestChannel, IReadOnlyList<Finding> Findings);

public static class WifiRules
{
    public const string B24 = "2,4 GHz", B5 = "5 GHz", B6 = "6 GHz";

    public static string Band(int mhz) => mhz < 3000 ? B24 : mhz < 5925 ? B5 : B6;

    public static int Channel(int mhz) => mhz switch
    {
        2484 => 14,
        < 3000 => (mhz - 2407) / 5,
        < 5925 => (mhz - 5000) / 5,
        _ => (mhz - 5950) / 5,
    };

    public static string Quality(int rssi) => rssi >= -50 ? "Excelente" : rssi >= -60 ? "Bom" : rssi >= -70 ? "Regular" : rssi >= -80 ? "Fraco" : "Muito fraco";

    /// <summary>Peso de uma rede vizinha no canal: quanto mais forte, mais atrapalha (0 a 1).</summary>
    private static double Weight(int rssi) => Math.Clamp((rssi + 95) / 45.0, 0.05, 1);

    /// <summary>
    /// Ocupação de cada canal da banda pelas redes vizinhas (a sua não conta). Em 2,4 GHz um canal pega os 4 vizinhos de cada lado
    /// (por isso só 1, 6 e 11 não se sobrepõem); em 5 e 6 GHz conta só o próprio canal.
    /// </summary>
    public static IReadOnlyDictionary<int, double> Occupancy(IReadOnlyList<WifiBss> networks, string band)
    {
        var channels = band == B24 ? Enumerable.Range(1, 13) : networks.Where(n => Band(n.FrequencyMhz) == band).Select(n => Channel(n.FrequencyMhz)).Distinct();
        var occ = channels.ToDictionary(c => c, _ => 0.0);
        foreach (var n in networks.Where(n => !n.Connected && Band(n.FrequencyMhz) == band))
        {
            int ch = Channel(n.FrequencyMhz);
            if (band == B24)
                for (int c = 1; c <= 13; c++) { int d = Math.Abs(c - ch); if (d <= 4) occ[c] += Weight(n.Rssi) * (1 - d / 5.0); }
            else occ[ch] = occ.GetValueOrDefault(ch) + Weight(n.Rssi);
        }
        return occ;
    }

    private static readonly int[] Free24 = { 1, 6, 11 };
    private static readonly int[] Free5 = { 36, 40, 44, 48, 149, 153, 157, 161 };

    /// <summary>Canal menos ocupado da banda (2,4 GHz: só 1, 6 ou 11; 5 GHz: canais sem radar). Em empate, fica no atual.</summary>
    public static int BestChannel(IReadOnlyList<WifiBss> networks, string band, int? current = null)
    {
        var occ = Occupancy(networks, band);
        var candidates = band == B24 ? Free24 : band == B5 ? Free5 : occ.Keys.DefaultIfEmpty(current ?? 37).ToArray();
        double Score(int c) => occ.GetValueOrDefault(c) - (c == current ? 0.05 : 0);
        return candidates.OrderBy(Score).ThenBy(c => c).First();
    }

    public static WifiAnalysis Analyze(WifiScan scan)
    {
        var findings = new List<Finding>();
        var best = new Dictionary<string, int>();
        var cur = scan.Current;
        foreach (string band in new[] { B24, B5, B6 })
            if (scan.Networks.Any(n => Band(n.FrequencyMhz) == band))
                best[band] = BestChannel(scan.Networks, band, cur is not null && Band(cur.FrequencyMhz) == band ? Channel(cur.FrequencyMhz) : null);

        if (cur is null)
        {
            findings.Add(new Finding("WIFI-000", FindingSeverity.Info, FindingStatus.NaoTestado, 1, "Sem Wi-Fi conectado",
                new[] { $"{scan.Networks.Count} rede(s) por perto." }, Array.Empty<string>(), "Se o PC usa cabo, está ótimo: cabo é sempre mais estável para jogar."));
            return new WifiAnalysis(0, best, findings);
        }

        string myBand = Band(cur.FrequencyMhz);
        int myCh = Channel(cur.FrequencyMhz);
        int score = 100;

        // sinal
        if (cur.Rssi < -70)
        {
            score -= cur.Rssi < -80 ? 40 : 25;
            findings.Add(new Finding("WIFI-001", cur.Rssi < -80 ? FindingSeverity.Problema : FindingSeverity.Atencao, FindingStatus.Confirmado, 0.9,
                $"Sinal {Quality(cur.Rssi).ToLowerInvariant()} ({cur.Rssi} dBm)", new[] { $"Medido na placa: {cur.Rssi} dBm em {cur.Ssid}." },
                new[] { "O sinal varia com portas, paredes e pessoas no caminho." },
                "Aproxime o PC do roteador, tire obstáculos ou use cabo/repetidor."));
        }

        // vizinhos no mesmo canal (ou sobrepostos em 2,4 GHz)
        var occ = Occupancy(scan.Networks, myBand);
        double mine = occ.GetValueOrDefault(myCh);
        var same = scan.Networks.Where(n => !n.Connected && Band(n.FrequencyMhz) == myBand &&
                                            (myBand == B24 ? Math.Abs(Channel(n.FrequencyMhz) - myCh) <= 4 : Channel(n.FrequencyMhz) == myCh)).ToList();
        if (best.TryGetValue(myBand, out int bestCh) && bestCh != myCh && mine - occ.GetValueOrDefault(bestCh) >= 0.8 && same.Count >= 2)
        {
            score -= Math.Min(30, (int)(mine * 8));
            findings.Add(new Finding("WIFI-002", mine >= 2.5 ? FindingSeverity.Problema : FindingSeverity.Atencao, FindingStatus.Provavel, 0.75,
                $"Canal {myCh} lotado ({same.Count} redes vizinhas)",
                new[] { $"Ocupação do canal {myCh}: {mine:0.0}; do canal {bestCh}: {occ.GetValueOrDefault(bestCh):0.0}.", "Vizinhas: " + string.Join(", ", same.OrderByDescending(n => n.Rssi).Take(4).Select(n => $"{(n.Ssid.Length > 0 ? n.Ssid : "oculta")} (ch {Channel(n.FrequencyMhz)}, {n.Rssi} dBm)")) },
                new[] { "Redes vizinhas paradas não atrapalham tanto quanto as que estão transmitindo muito." },
                $"No roteador, troque o canal de {myCh} para {bestCh}."));
        }

        // está em 2,4 GHz mas a mesma rede existe em 5 GHz
        if (myBand == B24 && scan.Networks.Any(n => n.Ssid == cur.Ssid && Band(n.FrequencyMhz) != B24 && n.Rssi >= -70))
        {
            score -= 10;
            findings.Add(new Finding("WIFI-003", FindingSeverity.Atencao, FindingStatus.Confirmado, 0.85, "Conectado em 2,4 GHz com 5 GHz disponível",
                new[] { $"A rede {cur.Ssid} também aparece em 5 GHz com sinal bom." }, new[] { "Em 5 GHz o alcance é menor: longe do roteador o 2,4 GHz pode ser melhor." },
                "Conecte na rede de 5 GHz: mais velocidade e menos interferência para jogar."));
        }

        if (cur.Security is "Aberta" or "WEP")
        {
            score -= 15;
            findings.Add(new Finding("WIFI-004", FindingSeverity.Problema, FindingStatus.Confirmado, 0.95, $"Rede sem proteção ({cur.Security})",
                new[] { $"{cur.Ssid} usa {cur.Security}." }, Array.Empty<string>(), "No roteador, use WPA2 ou WPA3 com senha forte."));
        }

        if (findings.Count == 0)
            findings.Add(new Finding("WIFI-OK", FindingSeverity.Info, FindingStatus.Confirmado, 0.8, "Wi-Fi em boas condições",
                new[] { $"{cur.Ssid}: {cur.Rssi} dBm ({Quality(cur.Rssi).ToLowerInvariant()}), canal {myCh} em {myBand}." }, Array.Empty<string>(),
                "Nada a fazer. Para jogar, cabo continua sendo o mais estável."));
        return new WifiAnalysis(Math.Clamp(score, 0, 100), best, findings);
    }
}

// ======================================================================== conflitos de IP

/// <summary>Um par IP ↔ MAC visto numa varredura da rede local (tabela ARP).</summary>
public sealed record ArpSeen(string Ip, string Mac, int Pass);

/// <summary>IP em conflito: o mesmo endereço respondendo com placas diferentes.</summary>
public sealed record IpConflict(string Ip, IReadOnlyList<string> Macs, double Confidence, string Evidence, string Counter);

public static class IpConflictRules
{
    public static string NormalizeMac(string mac) => new string(mac.Where(Uri.IsHexDigit).Select(char.ToUpperInvariant).ToArray()) is { Length: 12 } h
        ? string.Join(":", Enumerable.Range(0, 6).Select(i => h.Substring(i * 2, 2))) : "";

    private static bool Ignore(string mac) => mac.Length == 0 || mac == "FF:FF:FF:FF:FF:FF" || mac.StartsWith("01:00:5E") || mac.StartsWith("33:33") || mac == "00:00:00:00:00:00";

    /// <summary>
    /// Conflitos a partir de varreduras repetidas: o mesmo IP com MACs diferentes. <paramref name="ownConflictEvents"/> = avisos de
    /// conflito do próprio Windows (evento 4199) para o IP deste PC: é a prova mais forte.
    /// </summary>
    public static IReadOnlyList<IpConflict> Find(IEnumerable<ArpSeen> seen, string? ownIp = null, int ownConflictEvents = 0)
    {
        var list = new List<IpConflict>();
        foreach (var g in seen.Select(s => s with { Mac = NormalizeMac(s.Mac) }).Where(s => !Ignore(s.Mac)).GroupBy(s => s.Ip))
        {
            var macs = g.Select(s => s.Mac).Distinct().ToList();
            if (macs.Count < 2) continue;
            bool flips = g.GroupBy(s => s.Pass).Count() > 1;
            list.Add(new IpConflict(g.Key, macs, flips ? 0.7 : 0.55,
                $"O IP {g.Key} respondeu com {macs.Count} placas diferentes" + (flips ? " em varreduras seguidas." : "."),
                "Celular com MAC aleatório ou um aparelho que trocou de placa (cabo ↔ Wi-Fi) também muda o MAC."));
        }
        if (ownIp is not null && ownConflictEvents > 0 && list.All(c => c.Ip != ownIp))
            list.Add(new IpConflict(ownIp, Array.Empty<string>(), 0.95, $"O Windows registrou {ownConflictEvents} aviso(s) de conflito do IP deste PC (evento 4199).",
                "Avisos antigos podem ser de outra rede (Wi-Fi do trabalho, hotel)."));
        return list.OrderByDescending(c => c.Confidence).ToList();
    }

    /// <summary>Mesmo MAC em vários IPs (normal em roteador com várias redes; num aparelho comum pode indicar IP fixo e DHCP misturados).</summary>
    public static IReadOnlyList<(string Mac, IReadOnlyList<string> Ips)> MultiIp(IEnumerable<ArpSeen> seen) =>
        seen.Select(s => s with { Mac = NormalizeMac(s.Mac) }).Where(s => !Ignore(s.Mac)).GroupBy(s => s.Mac)
            .Select(g => (g.Key, (IReadOnlyList<string>)g.Select(s => s.Ip).Distinct().ToList())).Where(x => x.Item2.Count > 1).ToList();
}

// ======================================================================== DNS

public sealed record DnsSuiteResult(IReadOnlyList<DnsCheck> Resolvers, DnsCheck? First, DnsCheck? Cached, bool? Hijack, int? CacheEntries, IReadOnlyList<Finding> Findings)
{
    public DnsCheck? Fastest => Resolvers.Where(r => r.Ok && r.Ms is not null).OrderBy(r => r.Ms).FirstOrDefault();
}

public static class DnsSuite
{
    public static readonly IReadOnlyList<(string Name, string Ip)> Public = new[] { ("Cloudflare", "1.1.1.1"), ("Google", "8.8.8.8"), ("Quad9", "9.9.9.9") };
    public const string TestName = "www.google.com";

    /// <summary>Testes de DNS (só leitura): servidor da placa e públicos, cache do PC e se um nome que não existe é "desviado".</summary>
    public static async Task<DnsSuiteResult> RunAsync(INetProbe probe, IReadOnlyList<string> nicDns, int? cacheEntries, CancellationToken ct)
    {
        var first = await probe.ResolveSystemAsync(TestName, ct).ConfigureAwait(false);
        var cached = await probe.ResolveSystemAsync(TestName, ct).ConfigureAwait(false);
        var list = new List<DnsCheck>();
        foreach (string ip in nicDns.Where(d => IPAddress.TryParse(d, out var a) && a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).Distinct().Take(3))
        {
            var r = await probe.ResolveViaAsync(ip, TestName, ct).ConfigureAwait(false);
            string name = Public.FirstOrDefault(p => p.Ip == ip).Name ?? (ip.StartsWith("192.168.") || ip.StartsWith("10.") ? "Roteador" : "DNS da placa");
            list.Add(r with { Resolver = $"{name} ({ip})" });
        }
        foreach (var (name, ip) in Public.Where(p => !nicDns.Contains(p.Ip)))
        {
            var r = await probe.ResolveViaAsync(ip, TestName, ct).ConfigureAwait(false);
            list.Add(r with { Resolver = $"{name} ({ip})" });
        }
        var bogus = await probe.ResolveSystemAsync($"mbx-{Guid.NewGuid():N}.invalid", ct).ConfigureAwait(false);
        return Evaluate(list, first, cached, bogus.Ok, cacheEntries);
    }

    public static DnsSuiteResult Evaluate(IReadOnlyList<DnsCheck> resolvers, DnsCheck? first, DnsCheck? cached, bool? hijack, int? cacheEntries)
    {
        var f = new List<Finding>();
        if (first is { Ok: false })
            f.Add(new Finding("DNS-101", FindingSeverity.Problema, FindingStatus.Confirmado, 0.9, "O DNS do PC não respondeu",
                new[] { first.Detail }, new[] { "Se nenhum servidor responde, o problema é a internet, não o DNS." }, "Use Reparação › Limpar cache DNS ou troque o DNS."));
        if (first is { Ok: true, Ms: > 0 } && cached is { Ok: true, Ms: { } c } && c > first.Ms * 0.8 && c > 5)
            f.Add(new Finding("DNS-102", FindingSeverity.Atencao, FindingStatus.Provavel, 0.6, "Cache de DNS do PC não ajudou",
                new[] { $"1ª consulta {first.Ms:0} ms, 2ª {c:0} ms (a 2ª devia ser quase instantânea)." }, new[] { "O serviço de cache do DNS pode estar desligado de propósito." },
                "Limpe o cache DNS na Reparação e teste de novo."));
        if (hijack == true)
            f.Add(new Finding("DNS-103", FindingSeverity.Atencao, FindingStatus.Confirmado, 0.85, "Nomes que não existem estão sendo desviados",
                new[] { "Um endereço inventado (.invalid) devolveu um IP: a operadora ou o roteador redireciona erros para uma página deles." },
                new[] { "Alguns antivírus e redes corporativas fazem isso de propósito." }, "Use um DNS público (Cloudflare 1.1.1.1 ou Google 8.8.8.8)."));
        var ok = resolvers.Where(r => r.Ok && r.Ms is not null).OrderBy(r => r.Ms).ToList();
        var nic = resolvers.Where(r => r.Ok && r.Ms is not null && !Public.Any(p => r.Resolver.Contains(p.Ip))).ToList();
        if (ok.Count > 0 && nic.Count > 0 && nic.Min(r => r.Ms!.Value) > ok[0].Ms!.Value * 2 && nic.Min(r => r.Ms!.Value) - ok[0].Ms!.Value > 15)
            f.Add(new Finding("DNS-104", FindingSeverity.Atencao, FindingStatus.Confirmado, 0.8, $"Há DNS mais rápido: {ok[0].Resolver}",
                new[] { $"DNS atual {nic.Min(r => r.Ms):0} ms × {ok[0].Resolver} {ok[0].Ms:0} ms." }, new[] { "O tempo do DNS varia com o horário; repita o teste." },
                "Troque o DNS na aba DNS (dá para voltar)."));
        foreach (var r in resolvers.Where(r => !r.Ok))
            f.Add(new Finding("DNS-105", FindingSeverity.Info, FindingStatus.Confirmado, 0.7, $"{r.Resolver} não respondeu", new[] { r.Detail },
                new[] { "Algumas redes bloqueiam DNS externo de propósito." }, "Nada a fazer se o DNS do PC está funcionando."));
        if (f.Count == 0)
            f.Add(new Finding("DNS-OK", FindingSeverity.Info, FindingStatus.Confirmado, 0.8, "DNS funcionando bem",
                new[] { ok.Count > 0 ? $"Mais rápido: {ok[0].Resolver} em {ok[0].Ms:0} ms." : "Sem medição de servidores." }, Array.Empty<string>(), "Nada a fazer."));
        return new DnsSuiteResult(resolvers, first, cached, hijack, cacheEntries, f);
    }
}

// ======================================================================== rota

public static class RouteLabels
{
    public static bool IsPrivate(string ip) => IPAddress.TryParse(ip, out var a) && a.GetAddressBytes() is var b && b.Length == 4 &&
        (b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168));

    public static bool IsCgnat(string ip) => IPAddress.TryParse(ip, out var a) && a.GetAddressBytes() is var b && b.Length == 4 && b[0] == 100 && b[1] >= 64 && b[1] <= 127;

    /// <summary>Rótulo de cada salto: Rede local, Provedor (até 3 saltos públicos depois da casa), Internet e Destino.</summary>
    public static IReadOnlyList<string> Label(IReadOnlyList<string?> hops)
    {
        var labels = new string[hops.Count];
        int firstPublic = -1;
        bool isp = false;   // depois do primeiro salto da operadora (CGNAT ou público), endereço privado é a rede interna dela
        for (int i = 0; i < hops.Count; i++)
        {
            string? ip = hops[i];
            if (i == hops.Count - 1 && ip is not null) { labels[i] = "Destino"; continue; }
            if (ip is null) { labels[i] = "Sem resposta"; continue; }
            if (IsPrivate(ip) && !isp) { labels[i] = i == 0 ? "Rede local (roteador)" : "Rede local"; continue; }
            isp = true;
            if (IsCgnat(ip) || IsPrivate(ip)) { labels[i] = "Provedor"; continue; }
            if (firstPublic < 0) firstPublic = i;
            labels[i] = i - firstPublic < 3 ? "Provedor" : "Internet";
        }
        return labels;
    }
}

// ======================================================================== visão geral

public sealed record SummaryRow(string Area, string State, string Text, FindingSeverity Level);

public sealed record NetOverview(string Headline, string Sub, FindingSeverity Level, int Quality, string QualityText, IReadOnlyList<SummaryRow> Rows, IReadOnlyList<string> Recommendations);

public static class NetOverviewRules
{
    public static string QualityText(int q) => q >= 90 ? "Excelente" : q >= 75 ? "Boa" : q >= 50 ? "Regular" : "Ruim";

    /// <summary>Resumo da Visão geral a partir do diagnóstico (e da análise do Wi-Fi, se a conexão for por Wi-Fi).</summary>
    public static NetOverview From(NetDiagReport r, WifiAnalysis? wifi = null)
    {
        var e = r.Evidence;
        var rows = new List<SummaryRow>();
        SummaryRow Row(string area, FindingSeverity lvl, string text, string? state = null) =>
            new(area, state ?? (lvl switch { FindingSeverity.Problema => "Problema", FindingSeverity.Atencao => "Atenção", _ => "OK" }), text, lvl);

        if (e.Gateway is { } gw)
            rows.Add(gw.AllLost ? Row("Conectividade local", FindingSeverity.Problema, $"O roteador ({e.GatewayAddress}) não respondeu.")
                : Row("Conectividade local", gw.LossPct >= 2 ? FindingSeverity.Atencao : FindingSeverity.Info, $"Roteador {e.GatewayAddress}: {gw.Median:0.#} ms, perda {gw.LossPct:0.#}%."));
        else rows.Add(new SummaryRow("Conectividade local", "Não testado", "Sem roteador na placa principal.", FindingSeverity.Atencao));

        var ext = e.External.Values.Where(v => !v.AllLost).ToList();
        if (e.External.Count == 0) rows.Add(new SummaryRow("Acesso à internet", "Não testado", "A internet não foi testada.", FindingSeverity.Atencao));
        else if (ext.Count == 0) rows.Add(Row("Acesso à internet", FindingSeverity.Problema, "Nenhum servidor da internet respondeu."));
        else rows.Add(Row("Acesso à internet", ext.Max(v => v.LossPct) >= 2 ? FindingSeverity.Atencao : FindingSeverity.Info,
            $"Resposta {ext.Min(v => v.Median):0} ms, perda {ext.Max(v => v.LossPct):0.#}%."));

        if (e.DnsSystem.Count == 0) rows.Add(new SummaryRow("DNS", "Não testado", "DNS não testado.", FindingSeverity.Atencao));
        else rows.Add(e.DnsSystem.All(d => d.Ok) ? Row("DNS", FindingSeverity.Info, $"Resolução funcionando ({string.Join(" / ", e.Primary?.Dns.Take(2) ?? Array.Empty<string>())}).")
            : Row("DNS", FindingSeverity.Problema, "O DNS do PC falhou em " + e.DnsSystem.Count(d => !d.Ok) + " teste(s)."));

        if (e.Hops.Count == 0) rows.Add(new SummaryRow("Rota", "Não testado", "Caminho não traçado.", FindingSeverity.Info));
        else
        {
            var last = e.Hops[^1];
            rows.Add(Row("Rota", last.Rtt.LossPct >= 5 ? FindingSeverity.Atencao : FindingSeverity.Info,
                $"{e.Hops.Count} saltos até o destino" + (last.Rtt.AllLost ? ", destino sem resposta." : $", perda no destino {last.Rtt.LossPct:0.#}%.")));
        }

        if (ext.Count > 0)
        {
            double med = ext.Min(v => v.Median ?? 999), jit = ext.Max(v => v.Jitter ?? 0);
            var lvl = med > 120 || jit > 30 ? FindingSeverity.Problema : med > 60 || jit > 15 ? FindingSeverity.Atencao : FindingSeverity.Info;
            rows.Add(Row("Desempenho", lvl, $"Latência {med:0} ms, variação {jit:0.#} ms.", lvl == FindingSeverity.Info ? "Bom" : null));
        }

        if (e.Primary is { IsWifi: true })
            rows.Add(wifi is null ? new SummaryRow("Wi-Fi / Interferência", "Não testado", "Abra a aba Wi-Fi para analisar.", FindingSeverity.Info)
                : Row("Wi-Fi / Interferência", wifi.Findings.Max(f => f.Severity), $"Nota {wifi.Score}/100 · {wifi.Findings[0].Title}."));
        else rows.Add(new SummaryRow("Wi-Fi / Interferência", "Não aplicável", "Conexão via cabo (Ethernet).", FindingSeverity.Info));

        var all = r.Findings.Concat(wifi?.Findings ?? Array.Empty<Finding>()).ToList();
        int q = 100 - all.Sum(f => f.Severity switch { FindingSeverity.Problema => 30, FindingSeverity.Atencao => 10, _ => 0 });
        if (ext.Count > 0) q -= (int)Math.Min(20, ext.Max(v => v.LossPct) * 4) + (ext.Min(v => v.Median ?? 0) > 80 ? 10 : 0);
        q = Math.Clamp(q, 0, 100);
        var worst = rows.Count == 0 ? FindingSeverity.Info : rows.Max(x => x.Level);
        string head = worst switch { FindingSeverity.Problema => "Problema na conexão", FindingSeverity.Atencao => "Conexão com alertas", _ => "Conexão estável" };
        string sub = worst switch
        {
            FindingSeverity.Problema => "Veja abaixo onde está a falha e o que fazer.",
            FindingSeverity.Atencao => "Funciona, mas há pontos para melhorar.",
            _ => "Rede funcionando normalmente. Sem falhas críticas detectadas.",
        };
        var recs = all.Where(f => f.Severity != FindingSeverity.Info).OrderByDescending(f => f.Severity).Select(f => f.Next).Distinct().Take(4).ToList();
        if (recs.Count == 0) recs.Add("Nenhuma ação obrigatória. Rode o diagnóstico de vez em quando para acompanhar.");
        return new NetOverview(head, sub, worst, q, QualityText(q), rows, recs);
    }
}

// ======================================================================== reparação

public enum RepairKind { FlushDns, RenewDhcp, ResetWinsock, ResetTcpIp }

public sealed record RepairAction(RepairKind Kind, string Title, string What, string Impact, string ImpactText, bool NeedsRestart);

public static class NetRepairCatalog
{
    public static IReadOnlyList<RepairAction> All { get; } = new RepairAction[]
    {
        new(RepairKind.FlushDns, "Limpar cache DNS", "Apaga os endereços guardados no PC e força uma consulta nova.", "Baixo", "Não muda sua conexão, só limpa o cache.", false),
        new(RepairKind.RenewDhcp, "Renovar o IP (DHCP)", "Devolve o endereço IP e pede um novo ao roteador.", "Médio", "A internet cai por alguns segundos.", false),
        new(RepairKind.ResetWinsock, "Redefinir Winsock", "Restaura o catálogo de rede do Windows, que programas e VPNs às vezes estragam.", "Médio", "Pede reiniciar o PC.", true),
        new(RepairKind.ResetTcpIp, "Redefinir TCP/IP", "Volta a configuração de rede do Windows ao padrão.", "Alto", "Apaga IP fixo e ajustes manuais da placa. Pede reiniciar.", true),
    };

    public static RepairAction Get(RepairKind k) => All.First(a => a.Kind == k);
}

// ======================================================================== histórico

/// <summary>Histórico dos diagnósticos (um JSON por rodada) em %AppData%\MBExtreme\rede\historico, até 200 arquivos.</summary>
public static class NetHistory
{
    public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "rede", "historico");

    public static string? Save(NetDiagReport r, string? folder = null)
    {
        folder ??= DefaultFolder;
        try
        {
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, $"rede-{r.StartedUtc.ToLocalTime():yyyyMMdd-HHmmss}.json");
            File.WriteAllText(file, NetDiagReportWriter.ToJson(r));
            foreach (string old in Directory.GetFiles(folder, "rede-*.json").OrderByDescending(f => f, StringComparer.Ordinal).Skip(200)) File.Delete(old);
            return file;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public static IReadOnlyList<(string File, NetDiagReport Report)> List(int max = 50, string? folder = null)
    {
        folder ??= DefaultFolder;
        var list = new List<(string, NetDiagReport)>();
        if (!Directory.Exists(folder)) return list;
        foreach (string file in Directory.GetFiles(folder, "rede-*.json").OrderByDescending(f => f, StringComparer.Ordinal).Take(max))
        {
            try { if (NetDiagReportWriter.FromJson(File.ReadAllText(file)) is { } r) list.Add((file, r)); }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return list;
    }
}
