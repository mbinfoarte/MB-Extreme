using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using MBExtreme.Core.Network;
using MBExtreme.Core.Operations;
using MBExtreme.Core.Windows;

namespace MBExtreme.Core.NetDiag;

/// <summary>Leituras do Windows para a Central de Rede. Tudo é leitura, menos <see cref="Repair"/>, que só roda depois da confirmação.</summary>
[SupportedOSPlatform("windows")]
public static class NetCenterProbe
{
    // ---------------------------------------------------------------- Wi-Fi (WLAN API, não depende do idioma do Windows)

    public static WifiScan? ScanWifi()
    {
        if (WlanOpenHandle(2, IntPtr.Zero, out _, out IntPtr h) != 0) return null;
        var nets = new List<WifiBss>();
        double? rx = null, tx = null;
        try
        {
            if (WlanEnumInterfaces(h, IntPtr.Zero, out IntPtr ifList) != 0) return null;
            try
            {
                int count = Marshal.ReadInt32(ifList);
                for (int i = 0; i < count; i++)
                {
                    IntPtr item = ifList + 8 + i * 532;
                    var guid = Marshal.PtrToStructure<Guid>(item);
                    WlanScan(h, ref guid, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);   // pede uma varredura nova (o resultado vem da última)
                    string? connected = null;
                    if (WlanQueryInterface(h, ref guid, 7 /* current_connection */, IntPtr.Zero, out _, out IntPtr conn, out _) == 0)
                    {
                        try
                        {
                            if (Marshal.ReadInt32(conn) == 1 /* connected */)
                            {
                                var b = new byte[6]; Marshal.Copy(conn + 560, b, 0, 6);
                                connected = BitConverter.ToString(b).Replace('-', ':');
                                rx = Marshal.ReadInt32(conn + 580) / 1000.0; tx = Marshal.ReadInt32(conn + 584) / 1000.0;
                            }
                        }
                        finally { WlanFreeMemory(conn); }
                    }
                    var security = Security(h, guid);
                    if (WlanGetNetworkBssList(h, ref guid, IntPtr.Zero, 0 /* any */, false, IntPtr.Zero, out IntPtr bss) != 0) continue;
                    try
                    {
                        int n = Marshal.ReadInt32(bss + 4);
                        int size = Marshal.SizeOf<WlanBssEntry>();
                        for (int k = 0; k < n; k++)
                        {
                            var e = Marshal.PtrToStructure<WlanBssEntry>(bss + 8 + k * size);
                            string ssid = Encoding.UTF8.GetString(e.Ssid, 0, (int)Math.Min(32, e.SsidLength));
                            string mac = BitConverter.ToString(e.Bssid).Replace('-', ':');
                            string sec = security.TryGetValue(ssid, out var s) ? s : (e.Capability & 0x10) != 0 ? "Protegida" : "Aberta";
                            nets.Add(new WifiBss(ssid, mac, (int)(e.ChCenterFrequency / 1000), e.Rssi, sec, mac == connected));
                        }
                    }
                    finally { WlanFreeMemory(bss); }
                }
            }
            finally { WlanFreeMemory(ifList); }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return null; }
        finally { WlanCloseHandle(h, IntPtr.Zero); }
        return new WifiScan(nets.GroupBy(n => n.Bssid).Select(g => g.First()).OrderByDescending(n => n.Connected).ThenByDescending(n => n.Rssi).ToList(), rx, tx);
    }

    private static Dictionary<string, string> Security(IntPtr h, Guid guid)
    {
        var map = new Dictionary<string, string>();
        if (WlanGetAvailableNetworkList(h, ref guid, 0, IntPtr.Zero, out IntPtr list) != 0) return map;
        try
        {
            int n = Marshal.ReadInt32(list);
            for (int i = 0; i < n; i++)
            {
                IntPtr item = list + 8 + i * 628;
                int len = Marshal.ReadInt32(item + 512);
                var ssidBytes = new byte[32]; Marshal.Copy(item + 516, ssidBytes, 0, 32);
                string ssid = Encoding.UTF8.GetString(ssidBytes, 0, Math.Clamp(len, 0, 32));
                bool secured = Marshal.ReadInt32(item + 608) != 0;
                int auth = Marshal.ReadInt32(item + 612);
                map[ssid] = !secured ? "Aberta" : auth switch { 1 or 2 => "WEP", 3 or 4 => "WPA", 6 or 7 => "WPA2", >= 8 and <= 11 => "WPA3", _ => "Protegida" };
            }
        }
        finally { WlanFreeMemory(list); }
        return map;
    }

    // ---------------------------------------------------------------- rede local e conflitos

    /// <summary>Duas varreduras da rede local (com 4 s entre elas) e os avisos de conflito do Windows. Só leitura.</summary>
    public static async Task<(IReadOnlyList<LanDevice> Devices, IReadOnlyList<IpConflict> Conflicts, IReadOnlyList<(string Mac, IReadOnlyList<string> Ips)> MultiIp)> ScanLanAsync(
        IProgress<int>? progress, CancellationToken ct)
    {
        var (ip, _, _) = LocalNetwork.LocalAddress();
        if (ip is null) return (Array.Empty<LanDevice>(), Array.Empty<IpConflict>(), Array.Empty<(string, IReadOnlyList<string>)>());
        string? gateway = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(n => n.GetIPProperties().GatewayAddresses).Select(g => g.Address).FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.ToString();
        var first = await LocalNetwork.ScanAsync(ip, gateway, new Progress<int>(p => progress?.Report(p / 2)), ct).ConfigureAwait(false);
        await Task.Delay(4000, ct).ConfigureAwait(false);
        var second = await LocalNetwork.ScanAsync(ip, gateway, new Progress<int>(p => progress?.Report(50 + p / 2)), ct).ConfigureAwait(false);
        var seen = first.Select(d => new ArpSeen(d.Ip, d.Mac, 1)).Concat(second.Select(d => new ArpSeen(d.Ip, d.Mac, 2))).Where(s => !string.IsNullOrEmpty(s.Mac)).ToList();
        var conflicts = IpConflictRules.Find(seen, ip, OwnConflictEvents());
        var merged = second.Concat(first.Where(f => second.All(s => s.Ip != f.Ip))).ToList();
        return (merged, conflicts, IpConflictRules.MultiIp(seen));
    }

    /// <summary>Avisos de "conflito de endereço IP" (Tcpip 4199) dos últimos 7 dias no log do Windows. O XML não muda com o idioma.</summary>
    public static int OwnConflictEvents()
    {
        var r = Shell.Run("wevtutil.exe", "qe", "System", "/q:*[System[(EventID=4199) and TimeCreated[timediff(@SystemTime) <= 604800000]]]", "/c:20", "/f:xml");
        return r.ExitCode == 0 ? Regex.Matches(r.Output, "<Event ").Count : 0;
    }

    // ---------------------------------------------------------------- DNS

    /// <summary>Quantos nomes estão no cache de DNS do Windows agora (ou null se não deu para ler).</summary>
    public static int? DnsCacheEntries()
    {
        try
        {
            if (!DnsGetCacheDataTable(out IntPtr p)) return null;
            int n = 0;
            for (IntPtr cur = p; cur != IntPtr.Zero && n < 100_000; cur = Marshal.ReadIntPtr(cur)) n++;
            return n;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or AccessViolationException) { return null; }
    }

    // ---------------------------------------------------------------- reparação

    /// <summary>
    /// Roda uma correção (depois da confirmação na tela): guarda a foto da rede antes, executa, registra no diário e devolve o resultado.
    /// Nenhuma delas tem "desfazer" automático; a foto mostra como estava (IP, DNS, gateway) para refazer à mão se precisar.
    /// </summary>
    public static (bool Ok, string Message, string? Snapshot) Repair(RepairKind kind, NetDiagEvidence? before)
    {
        var action = NetRepairCatalog.Get(kind);
        string? snap = null;
        try
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "rede", "antes");
            Directory.CreateDirectory(folder);
            snap = Path.Combine(folder, $"antes-{kind}-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            var ifaces = before?.Interfaces ?? new SystemNetProbe().Interfaces().ToList();
            File.WriteAllText(snap, System.Text.Json.JsonSerializer.Serialize(new { quando = DateTimeOffset.Now, placas = ifaces }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { snap = null; }

        (bool ok, string msg) = kind switch
        {
            RepairKind.FlushDns => DnsFlushResolverCache() ? (true, "Cache DNS limpo.") : (false, "O Windows não limpou o cache."),
            RepairKind.RenewDhcp => Renew(),
            RepairKind.ResetWinsock => Run("netsh.exe", "winsock", "reset") is var w && w ? (true, "Winsock redefinido. Reinicie o PC para terminar.") : (false, "Não deu: abra o MB como administrador."),
            RepairKind.ResetTcpIp => Run("netsh.exe", "int", "ip", "reset") is var t && t ? (true, "TCP/IP redefinido. Reinicie o PC para terminar.") : (false, "Não deu: abra o MB como administrador."),
            _ => (false, "Ação desconhecida."),
        };
        OperationJournal.Shared.Record(new OperationInfo("net.repair." + kind, OperationKind.Repair, "Rede: " + action.Title),
            ok ? new OperationResult(action.NeedsRestart ? OperationState.RebootRequired : OperationState.Succeeded, msg + (snap is null ? "" : " Foto de antes: " + snap))
               : OperationResult.Fail(msg), DateTimeOffset.Now);
        return (ok, msg, snap);
    }

    private static (bool, string) Renew()
    {
        bool rel = Run("ipconfig.exe", "/release");
        bool ren = Run("ipconfig.exe", "/renew");
        return ren ? (true, "IP renovado.") : (false, rel ? "Devolveu o IP mas não recebeu outro: confira o roteador." : "Não deu: abra o MB como administrador.");
    }

    private static bool Run(string file, params string[] args) => Shell.Run(file, args).ExitCode == 0;

    // ---------------------------------------------------------------- Windows

    /// <summary>Tamanho de uma rede na lista do Windows (WLAN_BSS_ENTRY = 360 bytes); os testes conferem.</summary>
    public static int BssEntrySize => Marshal.SizeOf<WlanBssEntry>();

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanBssEntry
    {
        public uint SsidLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Ssid;
        public uint PhyId;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public byte[] Bssid;
        public int BssType, PhyType, Rssi;
        public uint LinkQuality;
        public byte InRegDomain;
        public ushort BeaconPeriod;
        public ulong Timestamp, HostTimestamp;
        public ushort Capability;
        public uint ChCenterFrequency;
        public uint RateSetLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 126)] public ushort[] Rates;
        public uint IeOffset, IeSize;
    }

    [DllImport("wlanapi.dll")] private static extern int WlanOpenHandle(uint version, IntPtr reserved, out uint negotiated, out IntPtr handle);
    [DllImport("wlanapi.dll")] private static extern int WlanCloseHandle(IntPtr handle, IntPtr reserved);
    [DllImport("wlanapi.dll")] private static extern int WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll")] private static extern int WlanScan(IntPtr handle, ref Guid iface, IntPtr ssid, IntPtr ie, IntPtr reserved);
    [DllImport("wlanapi.dll")] private static extern int WlanQueryInterface(IntPtr handle, ref Guid iface, int opcode, IntPtr reserved, out int size, out IntPtr data, out int type);
    [DllImport("wlanapi.dll")] private static extern int WlanGetNetworkBssList(IntPtr handle, ref Guid iface, IntPtr ssid, int bssType, bool security, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll")] private static extern int WlanGetAvailableNetworkList(IntPtr handle, ref Guid iface, int flags, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(IntPtr memory);
    [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")] private static extern bool DnsFlushResolverCache();
    [DllImport("dnsapi.dll", EntryPoint = "DnsGetCacheDataTable")] private static extern bool DnsGetCacheDataTable(out IntPtr table);
}
