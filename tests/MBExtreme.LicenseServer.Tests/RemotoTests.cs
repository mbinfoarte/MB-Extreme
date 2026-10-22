using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MBExtreme.Core.Licensing;
using MBExtreme.Core.Remote;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MBExtreme.LicenseServer.Tests;

public class RemotoTests : IClassFixture<ServerFixture>
{
    private readonly ServerFixture _server;

    public RemotoTests(ServerFixture server) => _server = server;

    private string Token(string email)
    {
        var key = new SigningKey(Path.Combine(_server.Dir, "signing-key.pem"));
        var payload = new LicensePayload { Subject = "1", Email = email, Name = "T", Plan = "pro", Device = "x", IssuedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) };
        return LicenseToken.Sign(payload, key.Key);
    }

    private async Task<(string Id, string Secret)> RegisterAsync(string device = "pc-de-teste-0001")
    {
        _ = _server.Server;   // garante que o servidor foi iniciado (a chave de assinatura existe)
        var client = _server.CreateClient();
        var response = await client.PostAsJsonAsync("/v1/remote/register", new { deviceId = device });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("id").GetString()!, body.GetProperty("secret").GetString()!);
    }

    private static async Task<JsonElement> ReadText(WebSocket socket)
    {
        var buffer = new byte[64 * 1024];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await socket.ReceiveAsync(buffer, cts.Token);
        Assert.Equal(WebSocketMessageType.Text, result.MessageType);
        return JsonSerializer.Deserialize<JsonElement>(Encoding.UTF8.GetString(buffer, 0, result.Count));
    }

    private static async Task<byte[]> ReadBinary(WebSocket socket)
    {
        var buffer = new byte[64 * 1024];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await socket.ReceiveAsync(buffer, cts.Token);
        Assert.Equal(WebSocketMessageType.Binary, result.MessageType);
        return buffer.AsSpan(0, result.Count).ToArray();
    }

    private static Task SendText(WebSocket socket, string json) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);

    private Task<WebSocket> ConnectClient(string id, string secret) =>
        _server.Server.CreateWebSocketClient().ConnectAsync(new Uri($"ws://localhost/v1/remote/ws/client?id={id}&secret={Uri.EscapeDataString(secret)}"), CancellationToken.None);

    private Task<WebSocket> ConnectTech(string id, string token)
    {
        var ws = _server.Server.CreateWebSocketClient();
        ws.ConfigureRequest = r => r.Headers["Authorization"] = "Bearer " + token;
        return ws.ConnectAsync(new Uri($"ws://localhost/v1/remote/ws/tech?id={id}"), CancellationToken.None);
    }

    [Fact]
    public async Task RegistraIdDeNoveDigitosEMantemUmPorComputador()
    {
        var (id1, _) = await RegisterAsync("pc-um-aaaaaaaa");
        var (id2, _) = await RegisterAsync("pc-dois-bbbbbbb");
        var (id1b, _) = await RegisterAsync("pc-um-aaaaaaaa");
        Assert.Equal(9, id1.Length);
        Assert.NotEqual(id1, id2);
        Assert.NotEqual(id1, id1b);   // o mesmo PC trocou de ID, o antigo deixa de existir
    }

    [Fact]
    public async Task RegistroRecusaIdentificadorCurto()
    {
        var response = await _server.CreateClient().PostAsJsonAsync("/v1/remote/register", new { deviceId = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ClienteComSegredoErradoNaoConecta()
    {
        var (id, _) = await RegisterAsync("pc-segredo-1111");
        await Assert.ThrowsAnyAsync<Exception>(() => ConnectClient(id, "segredo-errado"));
    }

    [Fact]
    public async Task TecnicoSemPermissaoNaoConecta()
    {
        var (id, secret) = await RegisterAsync("pc-perm-22222");
        using var client = await ConnectClient(id, secret);
        await Assert.ThrowsAnyAsync<Exception>(() => ConnectTech(id, Token("curioso@exemplo.com")));
        await Assert.ThrowsAnyAsync<Exception>(() => ConnectTech(id, "mbx1.lixo.lixo"));
    }

    [Fact]
    public async Task TecnicoRecebeOfflineQuandoOClienteNaoEstaConectado()
    {
        var (id, _) = await RegisterAsync("pc-offline-3333");
        using var tech = await ConnectTech(id, Token("tecnico@exemplo.com"));
        Assert.Equal("offline", (await ReadText(tech)).GetProperty("type").GetString());
    }

    [Fact]
    public async Task ClienteRecusaEOTecnicoRecebeRecusado()
    {
        var (id, secret) = await RegisterAsync("pc-recusa-4444");
        using var client = await ConnectClient(id, secret);
        using var tech = await ConnectTech(id, Token("tecnico@exemplo.com"));
        var request = await ReadText(client);
        Assert.Equal("request", request.GetProperty("type").GetString());
        Assert.Equal("T", request.GetProperty("technician").GetString());
        await SendText(client, """{"type":"reject"}""");
        Assert.Equal("waiting", (await ReadText(tech)).GetProperty("type").GetString());
        Assert.Equal("rejected", (await ReadText(tech)).GetProperty("type").GetString());
    }

    [Fact]
    public async Task SessaoCompletaComAcordoDeChaveETroca()
    {
        var (id, secret) = await RegisterAsync("pc-sessao-5555");
        string password = RemoteIds.NewPassword();
        using var client = await ConnectClient(id, secret);
        using var tech = await ConnectTech(id, Token("tecnico@exemplo.com"));

        Assert.Equal("request", (await ReadText(client)).GetProperty("type").GetString());
        await SendText(client, """{"type":"accept"}""");
        Assert.Equal("waiting", (await ReadText(tech)).GetProperty("type").GetString());
        Assert.Equal("accepted", (await ReadText(tech)).GetProperty("type").GetString());
        Assert.Equal("started", (await ReadText(client)).GetProperty("type").GetString());

        using var hc = new RemoteHandshake(RemoteRole.Client, password);
        using var ht = new RemoteHandshake(RemoteRole.Technician, RemoteIds.FormatPassword(password));
        await client.SendAsync(hc.CreateHello(), WebSocketMessageType.Binary, true, CancellationToken.None);
        await tech.SendAsync(ht.CreateHello(), WebSocketMessageType.Binary, true, CancellationToken.None);
        using var channelClient = hc.Complete(await ReadBinary(client));
        using var channelTech = ht.Complete(await ReadBinary(tech));

        var frame = RemoteMessages.Encode(new FrameMessage(1920, 1080, new byte[] { 0xFF, 0xD8, 7, 7, 7 }));
        await client.SendAsync(channelClient.Seal(frame), WebSocketMessageType.Binary, true, CancellationToken.None);
        var received = Assert.IsType<FrameMessage>(RemoteMessages.Decode(channelTech.Open(await ReadBinary(tech))));
        Assert.Equal(1920, received.ScreenWidth);

        await tech.SendAsync(channelTech.Seal(RemoteMessages.Encode(new MouseMessage(MouseAction.Move, MouseButton.None, 100, 200, 0))),
            WebSocketMessageType.Binary, true, CancellationToken.None);
        Assert.IsType<MouseMessage>(RemoteMessages.Decode(channelClient.Open(await ReadBinary(client))));

        await tech.CloseAsync(WebSocketCloseStatus.NormalClosure, "fim", CancellationToken.None);
        Assert.Equal("ended", (await ReadText(client)).GetProperty("type").GetString());

        // auditoria
        var audit = await _server.CreateClient().SendAsync(new HttpRequestMessage(HttpMethod.Get, "/v1/remote/audit")
        { Headers = { { "Authorization", "Bearer " + Token("admin@exemplo.com") } } });
        audit.EnsureSuccessStatusCode();
        var rows = await audit.Content.ReadFromJsonAsync<JsonElement>();
        var mine = rows.EnumerateArray().First(r => r.GetProperty("clientId").GetString() == id);
        Assert.True(mine.GetProperty("accepted").GetBoolean());
        Assert.True(mine.GetProperty("bytes").GetInt64() > 0);
    }

    [Fact]
    public async Task AuditoriaExigeTecnico()
    {
        _ = _server.Server;
        Assert.Equal(HttpStatusCode.Forbidden, (await _server.CreateClient().GetAsync("/v1/remote/audit")).StatusCode);
    }

    private sealed class FakeScreen : IScreenSource
    {
        private int _n;
        public FrameMessage? Capture() => new(1280, 720, new byte[] { 0xFF, 0xD8, (byte)(++_n % 250) });
    }

    private sealed class FakeAudio : IAudioSource
    {
        public event Action<byte[]>? Chunk;
        private Timer? _timer;
        public void Start() => _timer = new Timer(_ => Chunk?.Invoke(new byte[882]), null, 0, 20);
        public void Dispose() => _timer?.Dispose();
    }

    private sealed class FakeInput : IInputSink
    {
        public List<MouseMessage> Mouse { get; } = new();
        public List<KeyMessage> Keys { get; } = new();
        void IInputSink.Mouse(MouseMessage message, int w, int h) { lock (Mouse) Mouse.Add(message); }
        void IInputSink.Key(KeyMessage message) { lock (Keys) Keys.Add(message); }
    }

    private static async Task WaitFor(Func<bool> condition, int seconds = 10)
    {
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition() && DateTime.UtcNow < end) await Task.Delay(25);
        Assert.True(condition(), "tempo esgotado");
    }

    [Fact]
    public async Task AgenteETecnicoConversamDeVerdadePeloServidor()
    {
        var (id, secret) = await RegisterAsync("pc-agente-6666");
        var input = new FakeInput();
        string? asked = null;
        var agent = new RemoteAgent(new FakeScreen(), input, tech => { asked = tech; return Task.FromResult(true); }, framesPerSecond: 20) { RequirePassword = true };
        using var stopAll = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        using var clientSocket = await ConnectClient(id, secret);
        var agentTask = agent.RunAsync(clientSocket, stopAll.Token);

        using var techSocket = await ConnectTech(id, Token("tecnico@exemplo.com"));
        using var technician = new RemoteTechnician();
        int frames = 0;
        technician.FrameReceived += f => { if (f.ScreenWidth == 1280) Interlocked.Increment(ref frames); };
        var techTask = technician.RunAsync(techSocket, RemoteIds.FormatPassword(agent.Password), stopAll.Token);

        await WaitFor(() => technician.Connected && Volatile.Read(ref frames) >= 3);
        Assert.Equal("T", asked);
        Assert.True(agent.InSession);

        await technician.SendAsync(new MouseMessage(MouseAction.Down, MouseButton.Left, 1000, 2000, 0));
        await technician.SendAsync(new KeyMessage(0x41, true, false));
        await technician.SendAsync(new KeyMessage(0x5B, true, false));   // tecla Windows: o cliente ignora
        await technician.SendAsync(new ControlMessage(ControlCode.End));
        await WaitFor(() => !agent.InSession);

        lock (input.Mouse) Assert.Single(input.Mouse);
        lock (input.Keys) Assert.Equal(new[] { 0x41 }, input.Keys.Select(k => k.VirtualKey).ToArray());
        stopAll.Cancel();
    }

    [Fact]
    public async Task SenhaErradaDoTecnicoEncerraASessaoSemMostrarATela()
    {
        var (id, secret) = await RegisterAsync("pc-senha-7777");
        var input = new FakeInput();
        var agent = new RemoteAgent(new FakeScreen(), input, _ => Task.FromResult(true), framesPerSecond: 20) { RequirePassword = true };
        using var stopAll = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var clientSocket = await ConnectClient(id, secret);
        var agentTask = agent.RunAsync(clientSocket, stopAll.Token);

        using var techSocket = await ConnectTech(id, Token("tecnico@exemplo.com"));
        using var technician = new RemoteTechnician();
        int frames = 0;
        technician.FrameReceived += _ => Interlocked.Increment(ref frames);
        string? last = null;
        technician.StateChanged += s => last = s;
        await technician.RunAsync(techSocket, "SENHAERRADA", stopAll.Token);

        Assert.Equal(0, frames);
        Assert.False(technician.Connected);
        Assert.Contains("senha", last, StringComparison.OrdinalIgnoreCase);
        await WaitFor(() => !agent.InSession);
        stopAll.Cancel();
    }

    [Fact]
    public async Task SoComIdSemSenhaOsDoisLadosVeemOMesmoCodigoERecebemAudio()
    {
        var (id, secret) = await RegisterAsync("pc-soid-8888");
        var agent = new RemoteAgent(new FakeScreen(), new FakeInput(), _ => Task.FromResult(true), framesPerSecond: 20, audioFactory: () => new FakeAudio());
        using var stopAll = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var clientSocket = await ConnectClient(id, secret);
        _ = agent.RunAsync(clientSocket, stopAll.Token);

        using var techSocket = await ConnectTech(id, Token("tecnico@exemplo.com"));
        using var technician = new RemoteTechnician();
        int audioChunks = 0, frames = 0;
        technician.AudioReceived += a => { if (a.SampleRate == 22050) Interlocked.Increment(ref audioChunks); };
        technician.FrameReceived += _ => Interlocked.Increment(ref frames);
        _ = technician.RunAsync(techSocket, null, stopAll.Token);

        await WaitFor(() => technician.Connected && Volatile.Read(ref audioChunks) >= 3 && Volatile.Read(ref frames) >= 2);
        Assert.NotNull(technician.SecurityCode);
        Assert.Equal(agent.SecurityCode, technician.SecurityCode);
        Assert.Matches(@"^\d{3} \d{3}$", technician.SecurityCode!);
        stopAll.Cancel();
    }

    [Fact]
    public void SemListaDeTecnicosQualquerContaComLicencaValidaPodeConectar()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mbx-relay-" + Guid.NewGuid().ToString("N"));
        try
        {
            var key = new SigningKey(Path.Combine(dir, "k.pem"));
            var db = new Db(Path.Combine(dir, "d.db"));
            var open = new RemoteRelay(db, key.PublicKey, Array.Empty<string>());
            var restricted = new RemoteRelay(db, key.PublicKey, new[] { "so@este.com" });
            string Sign(string email, string name, DateTimeOffset exp) => LicenseToken.Sign(new LicensePayload { Subject = "1", Email = email, Name = name, Plan = "free", Device = "x", IssuedAt = DateTimeOffset.UtcNow, ExpiresAt = exp }, key.Key);

            var ok = open.Technician("Bearer " + Sign("maria@x.com", "Maria Silva", DateTimeOffset.UtcNow.AddDays(1)));
            Assert.Equal(("maria@x.com", "Maria Silva"), ok);
            Assert.Equal("joao", open.Technician(Sign("joao@x.com", "", DateTimeOffset.UtcNow.AddDays(1)))!.Value.Name);
            Assert.Null(open.Technician(Sign("maria@x.com", "M", DateTimeOffset.UtcNow.AddDays(-1))));   // vencida
            Assert.Null(open.Technician("mbx1.falso.falso"));
            Assert.Null(open.Technician(null));
            Assert.Null(restricted.Technician(Sign("maria@x.com", "M", DateTimeOffset.UtcNow.AddDays(1))));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
