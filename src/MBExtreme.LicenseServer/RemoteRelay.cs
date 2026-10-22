using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MBExtreme.Core.Licensing;

namespace MBExtreme.LicenseServer;

/// <summary>
/// Sinalização e retransmissão do acesso remoto. O servidor só repassa bytes já cifrados entre o cliente e o técnico:
/// não vê a tela nem o teclado. Registra quem pediu acesso, quando, se foi aceito e quanto tráfego passou.
/// </summary>
public sealed class RemoteRelay
{
    private const int MaxMessageBytes = 6 * 1024 * 1024;
    private static readonly TimeSpan AcceptTimeout = TimeSpan.FromSeconds(60);

    private readonly Db _db;
    private readonly string _publicKey;
    private readonly HashSet<string> _technicians;
    private readonly TimeSpan _maxSession;
    private readonly ConcurrentDictionary<string, ClientConnection> _clients = new();

    private sealed class ClientConnection
    {
        public required WebSocket Socket { get; init; }
        public SemaphoreSlim SendGate { get; } = new(1, 1);
        public TaskCompletionSource<bool>? Pending { get; set; }
        public volatile WebSocket? Peer;
        public TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public long Bytes;

        public async Task SendAsync(ReadOnlyMemory<byte> data, WebSocketMessageType type, CancellationToken stop)
        {
            await SendGate.WaitAsync(stop);
            try
            {
                if (Socket.State == WebSocketState.Open) await Socket.SendAsync(data, type, true, stop);
            }
            finally
            {
                SendGate.Release();
            }
        }
    }

    public RemoteRelay(Db db, string publicKey, IEnumerable<string> technicianEmails, TimeSpan? maxSession = null)
    {
        _db = db;
        _publicKey = publicKey;
        _technicians = new HashSet<string>(technicianEmails.Select(e => e.Trim().ToLowerInvariant()).Where(e => e.Length > 0));
        _maxSession = maxSession ?? TimeSpan.FromHours(4);
    }

    public bool RemoteEnabled => _technicians.Count > 0;

    public static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    public bool IsOnline(string id) => _clients.TryGetValue(id, out var c) && c.Socket.State == WebSocketState.Open;

    /// <summary>E-mail do técnico se o token de licença é válido e a conta está autorizada; senão nulo.</summary>
    public string? Technician(string? bearer)
    {
        if (string.IsNullOrWhiteSpace(bearer)) return null;
        string token = bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? bearer[7..].Trim() : bearer.Trim();
        var payload = LicenseToken.Verify(token, _publicKey);
        if (payload is null || payload.ExpiresAt < DateTimeOffset.UtcNow) return null;
        string email = payload.Email.ToLowerInvariant();
        return _technicians.Contains(email) ? email : null;
    }

    // ------------------------------------------------------------ cliente (computador que pede suporte)

    public async Task RunClientAsync(WebSocket socket, string id, CancellationToken stop)
    {
        var connection = new ClientConnection { Socket = socket };
        if (_clients.TryGetValue(id, out var old)) await CloseQuietly(old.Socket, "substituído");
        _clients[id] = connection;
        _db.TouchRemoteDevice(id);
        try
        {
            while (socket.State == WebSocketState.Open && !stop.IsCancellationRequested)
            {
                var received = await ReceiveAsync(socket, MaxMessageBytes, stop);
                if (received is null) break;
                var (type, data) = received.Value;
                if (type == WebSocketMessageType.Binary)
                {
                    // dentro da sessão: repassa ao técnico, ainda cifrado
                    var peer = connection.Peer;
                    if (peer is { State: WebSocketState.Open })
                    {
                        Interlocked.Add(ref connection.Bytes, data.Length);
                        await peer.SendAsync(data, WebSocketMessageType.Binary, true, stop);
                    }
                    continue;
                }

                string kind = ParseType(Encoding.UTF8.GetString(data));
                if (kind is "accept" or "reject") connection.Pending?.TrySetResult(kind == "accept");
                else if (kind == "end") break;
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or IOException) { }
        finally
        {
            _clients.TryRemove(new KeyValuePair<string, ClientConnection>(id, connection));
            connection.Pending?.TrySetResult(false);
            connection.Closed.TrySetResult();
            await CloseQuietly(socket, "fim");
        }
    }

    // ------------------------------------------------------------ técnico

    public async Task RunTechnicianAsync(WebSocket tech, string id, string technician, CancellationToken stop)
    {
        long audit = _db.StartRemoteAudit(id, technician);
        ClientConnection? client = null;
        bool accepted = false;
        string result = "";
        try
        {
            if (!_clients.TryGetValue(id, out client) || client.Socket.State != WebSocketState.Open)
            {
                await SendTextAsync(tech, new { type = "offline" }, stop);
                result = "offline";
                return;
            }
            if (client.Peer is not null || client.Pending is not null)
            {
                await SendTextAsync(tech, new { type = "busy" }, stop);
                result = "ocupado";
                client = null;
                return;
            }

            var pending = client.Pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await client.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new { type = "request", technician = DisplayName(technician) }), WebSocketMessageType.Text, stop);
            await SendTextAsync(tech, new { type = "waiting" }, stop);

            var winner = await Task.WhenAny(pending.Task, Task.Delay(AcceptTimeout, stop));
            if (winner != pending.Task || !pending.Task.Result)
            {
                bool timeout = winner != pending.Task;
                await SendTextAsync(tech, new { type = timeout ? "timeout" : "rejected" }, stop);
                await client.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new { type = "closed" }), WebSocketMessageType.Text, stop);
                result = timeout ? "sem resposta" : "recusado";
                client.Pending = null;
                return;
            }

            accepted = true;
            client.Pending = null;
            client.Peer = tech;
            await SendTextAsync(tech, new { type = "accepted" }, stop);
            await client.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new { type = "started" }), WebSocketMessageType.Text, stop);

            using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop);
            limit.CancelAfter(_maxSession);
            var toClient = PumpToClient(tech, client, limit.Token);
            await Task.WhenAny(toClient, client.Closed.Task, Task.Delay(Timeout.Infinite, limit.Token).ContinueWith(_ => { }));
            limit.Cancel();
            result = "encerrado";
            await SendTextAsync(tech, new { type = "ended" }, CancellationToken.None);
            if (client.Socket.State == WebSocketState.Open)
                await client.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new { type = "ended" }), WebSocketMessageType.Text, CancellationToken.None);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or IOException)
        {
            result = result.Length == 0 ? "erro de conexão" : result;
        }
        finally
        {
            if (client is not null)
            {
                client.Peer = null;
                if (!accepted && client.Pending is not null) client.Pending = null;
            }
            _db.FinishRemoteAudit(audit, accepted, client is null ? 0 : Interlocked.Read(ref client.Bytes), result.Length == 0 ? "encerrado" : result);
            await CloseQuietly(tech, "fim");
        }
    }

    private static string DisplayName(string email) => email.Split('@')[0];

    private static async Task PumpToClient(WebSocket from, ClientConnection to, CancellationToken stop)
    {
        try
        {
            while (from.State == WebSocketState.Open && to.Socket.State == WebSocketState.Open && !stop.IsCancellationRequested)
            {
                var message = await ReceiveAsync(from, MaxMessageBytes, stop);
                if (message is null) return;
                if (message.Value.Type != WebSocketMessageType.Binary) continue;   // controle em texto não passa pelo túnel
                Interlocked.Add(ref to.Bytes, message.Value.Data.Length);
                await to.SendAsync(message.Value.Data, WebSocketMessageType.Binary, stop);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or IOException) { }
    }

    private static string ParseType(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    private static Task SendTextAsync(WebSocket socket, object message, CancellationToken stop) =>
        socket.State == WebSocketState.Open
            ? socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(message), WebSocketMessageType.Text, true, stop)
            : Task.CompletedTask;

    private static async Task<(WebSocketMessageType Type, byte[] Data)?> ReceiveAsync(WebSocket socket, int max, CancellationToken stop)
    {
        var buffer = new byte[16 * 1024];
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, stop);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            stream.Write(buffer, 0, result.Count);
            if (stream.Length > max)
            {
                await CloseQuietly(socket, "mensagem grande demais", WebSocketCloseStatus.MessageTooBig);
                return null;
            }
        } while (!result.EndOfMessage);
        return (result.MessageType, stream.ToArray());
    }

    private static async Task CloseQuietly(WebSocket socket, string reason, WebSocketCloseStatus status = WebSocketCloseStatus.NormalClosure)
    {
        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await socket.CloseAsync(status, reason, new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException) { }
    }
}
