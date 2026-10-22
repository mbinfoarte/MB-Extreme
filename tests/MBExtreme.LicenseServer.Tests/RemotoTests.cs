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
        Assert.Equal("tecnico", request.GetProperty("technician").GetString());
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
        { Headers = { { "Authorization", "Bearer " + Token("tecnico@exemplo.com") } } });
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
}
