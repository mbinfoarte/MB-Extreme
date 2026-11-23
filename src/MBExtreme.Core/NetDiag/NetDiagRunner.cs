using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MBExtreme.Core.NetDiag;

/// <summary>
/// Contrato dos coletores (arquitetura hexagonal): o diagnóstico só conversa com esta interface. A versão real usa as APIs do
/// Windows/.NET; os testes usam uma falsa, sem rede.
/// </summary>
public interface INetProbe
{
    IReadOnlyList<NetIface> Interfaces();
    Task<RttStats> PingAsync(string address, int count, int timeoutMs, CancellationToken ct);
    Task<DnsCheck> ResolveSystemAsync(string name, CancellationToken ct);
    Task<DnsCheck> ResolveViaAsync(string resolver, string name, CancellationToken ct);
    Task<HttpCheck> HttpAsync(string url, string expectedBody, CancellationToken ct);
    Task<IReadOnlyList<HopResult>> TraceAsync(string address, int maxHops, int perHop, CancellationToken ct);
}

/// <summary>Opções da rodada. Tudo é leitura: nenhuma opção muda a rede.</summary>
public sealed record NetDiagOptions(int Pings = 10, bool Trace = true, int MaxHops = 20);

/// <summary>Roteiro do diagnóstico (RF-04): pilha local → roteador → internet por IP → DNS → página → caminho. Cada etapa tem tempo limite.</summary>
public sealed class NetDiagRunner(INetProbe probe)
{
    public async Task<NetDiagReport> RunAsync(NetDiagOptions options, IProgress<string>? progress, CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;
        var e = new NetDiagEvidence();
        var notTested = new List<string>();

        progress?.Report("Lendo as placas de rede...");
        e.Interfaces = probe.Interfaces().ToList();
        e.Primary = PickPrimary(e.Interfaces);
        e.GatewayAddress = e.Primary?.Gateways.FirstOrDefault(g => IPAddress.TryParse(g, out var a) && a.AddressFamily == AddressFamily.InterNetwork)
            ?? e.Primary?.Gateways.FirstOrDefault();

        progress?.Report("Testando a pilha de rede do PC...");
        e.Loopback = await probe.PingAsync("127.0.0.1", 3, 1000, ct);

        if (e.Primary is null)
        {
            notTested.AddRange(new[] { "Roteador, internet, DNS e caminho: não há placa conectada." });
            return Finish();
        }

        if (e.GatewayAddress is { } gw)
        {
            progress?.Report($"Testando o roteador ({gw})...");
            e.Gateway = await probe.PingAsync(gw, options.Pings, 1500, ct);
        }
        else notTested.Add("Roteador: a placa não tem gateway.");

        foreach (string target in NetScope.ExternalTargets)
        {
            progress?.Report($"Testando a internet ({target})...");
            e.External[target] = await probe.PingAsync(target, options.Pings, 2000, ct);
        }

        progress?.Report("Testando o DNS...");
        foreach (string name in NetScope.DnsNames) e.DnsSystem.Add(await probe.ResolveSystemAsync(name, ct));
        if (e.DnsSystem.Any(d => !d.Ok))
            foreach (string name in NetScope.DnsNames) e.DnsAlternate.Add(await probe.ResolveViaAsync(NetScope.AlternateResolver, name, ct));
        else notTested.Add("DNS alternativo: não precisou (o DNS do PC respondeu).");

        progress?.Report("Abrindo a página de teste...");
        e.Http = await probe.HttpAsync(NetScope.HttpProbeUrl, NetScope.HttpProbeBody, ct);

        if (options.Trace && e.External.Values.Any(r => !r.AllLost))
        {
            e.TraceTarget = NetScope.ExternalTargets[0];
            progress?.Report($"Traçando o caminho até {e.TraceTarget}...");
            e.Hops = (await probe.TraceAsync(e.TraceTarget, options.MaxHops, 3, ct)).ToList();
        }
        else notTested.Add(options.Trace ? "Caminho: a internet não respondeu por IP." : "Caminho: desligado nas opções.");

        notTested.Add("Wi-Fi (canais e redes vizinhas), velocidade da rede local e captura de pacotes: próximas fases.");
        return Finish();

        NetDiagReport Finish() => new(NetDiagReport.CurrentSchema, started, DateTimeOffset.UtcNow, Environment.MachineName, e, NetDiagRules.Evaluate(e), notTested);
    }

    /// <summary>A placa usada para a internet: ligada, com gateway; cabo antes de Wi-Fi; VPN e virtual por último.</summary>
    public static NetIface? PickPrimary(IReadOnlyList<NetIface> list) =>
        list.Where(i => i.Up && i.Gateways.Count > 0)
            .OrderBy(i => i.IsVpn || i.IsVirtual ? 2 : i.IsEthernet ? 0 : 1)
            .FirstOrDefault();
}

/// <summary>Coletor real: APIs do .NET (NetworkInterface, Ping, Dns, sockets e HTTP), sem ler a saída de comandos e sem administrador.</summary>
public sealed class SystemNetProbe : INetProbe
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(8) };

    public IReadOnlyList<NetIface> Interfaces()
    {
        var list = new List<NetIface>();
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (n.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            IPInterfaceProperties p;
            try { p = n.GetIPProperties(); }
            catch (NetworkInformationException) { continue; }
            var v4 = p.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).ToList();
            string desc = n.Description;
            string kind = n.NetworkInterfaceType switch
            {
                NetworkInterfaceType.Wireless80211 => "Wi-Fi",
                NetworkInterfaceType.Ppp or NetworkInterfaceType.Tunnel => "VPN",
                NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT => IsVpnName(desc) ? "VPN" : "Ethernet",
                _ => IsVpnName(desc) ? "VPN" : "Outra",
            };
            bool isVirtual = desc.Contains("Virtual", StringComparison.OrdinalIgnoreCase) || desc.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)
                || desc.Contains("VMware", StringComparison.OrdinalIgnoreCase) || desc.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase);
            bool dhcp = false; string? dhcpServer = null; int mtu = 0;
            try
            {
                var v4p = p.GetIPv4Properties();
                if (v4p is not null) { dhcp = v4p.IsDhcpEnabled; mtu = v4p.Mtu; }
                if (OperatingSystem.IsWindows()) dhcpServer = p.DhcpServerAddresses.FirstOrDefault()?.ToString();
            }
            catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException) { }
            list.Add(new NetIface(
                n.Name, desc, kind, n.OperationalStatus == OperationalStatus.Up, FormatMac(n.GetPhysicalAddress()), Math.Max(0, n.Speed),
                v4.Select(a => a.Address.ToString()).ToList(), v4.Select(a => a.PrefixLength).ToList(),
                p.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6).Select(a => a.Address.ToString()).ToList(),
                p.GatewayAddresses.Select(g => g.Address).Where(a => !a.Equals(IPAddress.Any) && !a.Equals(IPAddress.IPv6Any)).Select(a => a.ToString()).ToList(),
                p.DnsAddresses.Select(a => a.ToString()).ToList(), dhcp, dhcpServer, mtu, isVirtual));
        }
        return list;
    }

    private static bool IsVpnName(string d) =>
        d.Contains("VPN", StringComparison.OrdinalIgnoreCase) || d.Contains("TAP-", StringComparison.OrdinalIgnoreCase)
        || d.Contains("WireGuard", StringComparison.OrdinalIgnoreCase) || d.Contains("Wintun", StringComparison.OrdinalIgnoreCase);

    private static string FormatMac(PhysicalAddress mac)
    {
        var b = mac.GetAddressBytes();
        return b.Length == 0 ? "" : string.Join("-", b.Select(x => x.ToString("X2")));
    }

    public async Task<RttStats> PingAsync(string address, int count, int timeoutMs, CancellationToken ct)
    {
        var rtts = new List<double>();
        using var ping = new Ping();
        for (int i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var reply = await ping.SendPingAsync(address, timeoutMs);
                if (reply.Status == IPStatus.Success) rtts.Add(reply.RoundtripTime);
            }
            catch (PingException) { }
            if (i < count - 1) await Task.Delay(200, ct);
        }
        return RttStats.From(count, rtts);
    }

    public async Task<DnsCheck> ResolveSystemAsync(string name, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(5000);
            var addrs = await System.Net.Dns.GetHostAddressesAsync(name, limit.Token);
            return new DnsCheck("DNS do PC", name, addrs.Length > 0, clock.Elapsed.TotalMilliseconds, addrs.Length > 0 ? "" : "sem endereços");
        }
        catch (SocketException ex) { return new DnsCheck("DNS do PC", name, false, null, ex.SocketErrorCode == SocketError.HostNotFound ? "nome não encontrado" : "falhou: " + ex.SocketErrorCode); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new DnsCheck("DNS do PC", name, false, null, "sem resposta em 5 s"); }
    }

    public async Task<DnsCheck> ResolveViaAsync(string resolver, string name, CancellationToken ct)
    {
        ushort id = (ushort)Random.Shared.Next(1, ushort.MaxValue);
        var query = MBExtreme.Core.Dns.DnsPacket.BuildQuery(id, name);
        using var udp = new UdpClient();
        var clock = Stopwatch.StartNew();
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(3000);
            await udp.SendAsync(query, new IPEndPoint(IPAddress.Parse(resolver), 53), limit.Token);
            var r = await udp.ReceiveAsync(limit.Token);
            bool ok = MBExtreme.Core.Dns.DnsPacket.IsSuccessResponse(r.Buffer, r.Buffer.Length, id);
            return new DnsCheck(resolver, name, ok, clock.Elapsed.TotalMilliseconds, ok ? "" : "resposta com erro");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new DnsCheck(resolver, name, false, null, "sem resposta em 3 s"); }
        catch (SocketException ex) { return new DnsCheck(resolver, name, false, null, "falhou: " + ex.SocketErrorCode); }
    }

    public async Task<HttpCheck> HttpAsync(string url, string expectedBody, CancellationToken ct)
    {
        try
        {
            using var r = await Http.GetAsync(url, ct);
            int code = (int)r.StatusCode;
            if (code is >= 300 and < 400)
                return new HttpCheck(url, false, code, true, "redirecionado para " + (r.Headers.Location?.Host ?? "outro endereço"));
            string body = await r.Content.ReadAsStringAsync(ct);
            bool ok = r.IsSuccessStatusCode && body.Contains(expectedBody, StringComparison.Ordinal);
            return new HttpCheck(url, ok, code, !ok && r.IsSuccessStatusCode, ok ? "abriu" : r.IsSuccessStatusCode ? "a página veio diferente do esperado" : $"erro HTTP {code}");
        }
        catch (HttpRequestException ex) { return new HttpCheck(url, false, null, false, "não abriu: " + ex.Message); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return new HttpCheck(url, false, null, false, "sem resposta em 8 s"); }
    }

    public async Task<IReadOnlyList<HopResult>> TraceAsync(string address, int maxHops, int perHop, CancellationToken ct)
    {
        var hops = new List<HopResult>();
        using var ping = new Ping();
        var data = new byte[32];
        for (int ttl = 1; ttl <= maxHops; ttl++)
        {
            var rtts = new List<double>();
            string? hopAddr = null;
            bool reached = false;
            for (int i = 0; i < perHop; i++)
            {
                ct.ThrowIfCancellationRequested();
                var clock = Stopwatch.StartNew();
                try
                {
                    var reply = await ping.SendPingAsync(address, 2000, data, new PingOptions(ttl, true));
                    if (reply.Status is IPStatus.TtlExpired or IPStatus.Success)
                    {
                        hopAddr = reply.Address?.ToString();
                        rtts.Add(reply.Status == IPStatus.Success ? reply.RoundtripTime : clock.Elapsed.TotalMilliseconds);
                        reached |= reply.Status == IPStatus.Success;
                    }
                }
                catch (PingException) { }
            }
            hops.Add(new HopResult(ttl, hopAddr, RttStats.From(perHop, rtts)));
            if (reached) break;
        }
        return hops;
    }
}
