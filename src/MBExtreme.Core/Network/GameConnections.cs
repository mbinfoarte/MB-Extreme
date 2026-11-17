using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace MBExtreme.Core.Network;

/// <summary>Servidor remoto com o qual um jogo está falando. Weight = quantidade (conexões ou bytes), só para ordenar.</summary>
public sealed record RemoteEndpoint(IPAddress Address, int Port, string Protocol, long Weight);

public static class GameEndpoints
{
    /// <summary>Endereço da internet (não é do seu roteador, da sua rede local nem do próprio PC).</summary>
    public static bool IsPublic(IPAddress a)
    {
        if (IPAddress.IsLoopback(a) || a.Equals(IPAddress.Any) || a.Equals(IPAddress.Broadcast)) return false;
        var b = a.GetAddressBytes();
        if (b.Length != 4) return false;
        return !(b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254)
            || (b[0] == 100 && b[1] is >= 64 and <= 127) || b[0] >= 224);
    }

    /// <summary>Escolhe até <paramref name="count"/> servidores diferentes, os mais usados primeiro.</summary>
    public static IReadOnlyList<RemoteEndpoint> Top(IEnumerable<RemoteEndpoint> all, int count) =>
        all.Where(e => IsPublic(e.Address))
           .GroupBy(e => e.Address.ToString())
           .Select(g => g.OrderByDescending(e => e.Weight).First() with { Weight = g.Sum(e => e.Weight) })
           .OrderByDescending(e => e.Weight).Take(count).ToList();
}

/// <summary>Conexões TCP abertas por um programa, lidas da tabela do próprio Windows (sem injetar nada no jogo).</summary>
[SupportedOSPlatform("windows")]
public static class GameConnections
{
    private const int AfInet = 2, TcpTableOwnerPidAll = 5, Established = 5;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int family, int tableClass, uint reserved);

    public sealed record TcpRow(uint State, IPAddress Local, int LocalPort, IPAddress Remote, int RemotePort, int Pid);

    /// <summary>Lê a tabela MIB_TCPTABLE_OWNER_PID: quantidade de linhas e, para cada uma, estado, endereço local, porta local, endereço remoto, porta remota e PID (portas em ordem de rede).</summary>
    public static IReadOnlyList<TcpRow> ParseTcpTable(byte[] buffer)
    {
        var rows = new List<TcpRow>();
        if (buffer.Length < 4) return rows;
        int count = BitConverter.ToInt32(buffer, 0);
        for (int i = 0; i < count; i++)
        {
            int at = 4 + i * 24;
            if (at + 24 > buffer.Length) break;
            uint state = BitConverter.ToUInt32(buffer, at);
            var local = new IPAddress(new[] { buffer[at + 4], buffer[at + 5], buffer[at + 6], buffer[at + 7] });
            int localPort = (buffer[at + 8] << 8) | buffer[at + 9];
            var remote = new IPAddress(new[] { buffer[at + 12], buffer[at + 13], buffer[at + 14], buffer[at + 15] });
            int remotePort = (buffer[at + 16] << 8) | buffer[at + 17];
            int pid = BitConverter.ToInt32(buffer, at + 20);
            rows.Add(new TcpRow(state, local, localPort, remote, remotePort, pid));
        }
        return rows;
    }

    public static IReadOnlyList<TcpRow> ReadTcpTable()
    {
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, AfInet, TcpTableOwnerPidAll, 0);
        for (int attempt = 0; attempt < 4 && size > 0; attempt++)
        {
            IntPtr mem = Marshal.AllocHGlobal(size);
            try
            {
                uint rc = GetExtendedTcpTable(mem, ref size, false, AfInet, TcpTableOwnerPidAll, 0);
                if (rc == 0)
                {
                    var buffer = new byte[size];
                    Marshal.Copy(mem, buffer, 0, size);
                    return ParseTcpTable(buffer);
                }
                if (rc != 122) break; // 122 = buffer pequeno: tenta de novo com o novo tamanho
            }
            finally { Marshal.FreeHGlobal(mem); }
        }
        return Array.Empty<TcpRow>();
    }

    public static IReadOnlyList<RemoteEndpoint> TcpEndpointsOf(IReadOnlySet<int> pids) =>
        ReadTcpTable().Where(r => r.State == Established && pids.Contains(r.Pid) && r.RemotePort > 0)
            .Select(r => new RemoteEndpoint(r.Remote, r.RemotePort, "TCP", 1)).ToList();

    public static IReadOnlySet<int> PidsOf(string exeName)
    {
        string name = Path.GetFileNameWithoutExtension(exeName);
        try { return Process.GetProcessesByName(name).Select(p => p.Id).ToHashSet(); }
        catch (InvalidOperationException) { return new HashSet<int>(); }
    }
}

/// <summary>
/// Vê para quais servidores o jogo manda e recebe pacotes UDP (a tabela do Windows não mostra isso), pelos eventos de rede do próprio Windows (ETW).
/// Não injeta nada no jogo; precisa de administrador. Em qualquer falha, simplesmente não lista nada.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class UdpEndpointWatcher : IDisposable
{
    private const string SessionName = "MBExtremeNet";
    private static readonly Guid KernelNetwork = new("7DD42A49-5329-4832-8DFD-43D979153A88");
    private const int UdpSendV4 = 42, UdpRecvV4 = 43;
    private readonly object _lock = new();
    private readonly Dictionary<(int Pid, string Address, int Port), long> _bytes = new();
    private TraceEventSession? _session;
    private Task? _worker;

    public bool IsRunning => _session is not null;

    /// <summary>Os valores vêm em ordem de rede: o endereço guardado como inteiro e a porta com os bytes trocados.</summary>
    public static (IPAddress Address, int Port) Decode(uint rawAddress, ushort rawPort)
    {
        var bytes = BitConverter.GetBytes(rawAddress);
        return (new IPAddress(bytes), ((rawPort & 0xFF) << 8) | (rawPort >> 8));
    }

    public void Start()
    {
        if (_session is not null) return;
        try { TraceEventSession.GetActiveSession(SessionName)?.Stop(); }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException) { }

        var session = new TraceEventSession(SessionName) { StopOnDispose = true };
        session.EnableProvider(KernelNetwork);
        session.Source.AllEvents += OnEvent;
        _session = session;
        _worker = Task.Run(() => { try { session.Source.Process(); } catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { } });
    }

    private void OnEvent(TraceEvent data)
    {
        try
        {
            if (data.ProviderGuid != KernelNetwork) return;
            int id = (ushort)data.ID;
            if (id is not (UdpSendV4 or UdpRecvV4)) return;

            int pid = data.PayloadByName("PID") is { } p ? Convert.ToInt32(p) : data.ProcessID;
            long size = data.PayloadByName("size") is { } s ? Convert.ToInt64(s) : 0;
            // no envio o servidor é o destino; no recebimento é a origem
            object? addr = data.PayloadByName(id == UdpSendV4 ? "daddr" : "saddr");
            object? port = data.PayloadByName(id == UdpSendV4 ? "dport" : "sport");
            if (addr is null || port is null) return;
            var (ip, number) = Decode(Convert.ToUInt32(addr), Convert.ToUInt16(port));
            lock (_lock)
            {
                var key = (pid, ip.ToString(), number);
                _bytes[key] = _bytes.GetValueOrDefault(key) + Math.Max(size, 1);
            }
        }
        catch (Exception ex) when (ex is InvalidCastException or OverflowException or FormatException or ArgumentException) { }
    }

    public IReadOnlyList<RemoteEndpoint> EndpointsOf(IReadOnlySet<int> pids)
    {
        lock (_lock)
            return _bytes.Where(kv => pids.Contains(kv.Key.Pid))
                .Select(kv => new RemoteEndpoint(IPAddress.Parse(kv.Key.Address), kv.Key.Port, "UDP", kv.Value)).ToList();
    }

    public void Stop()
    {
        var session = _session;
        _session = null;
        session?.Dispose();
        try { _worker?.Wait(2000); } catch (AggregateException) { }
        _worker = null;
    }

    public void Dispose() => Stop();
}
