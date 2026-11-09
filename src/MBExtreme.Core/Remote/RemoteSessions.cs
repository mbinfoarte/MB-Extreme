using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MBExtreme.Core.Remote;

/// <summary>Captura a tela do computador do cliente (implementação do Windows fica no aplicativo).</summary>
public interface IScreenSource
{
    /// <summary>Quadro atual em JPEG, ou nulo se não foi possível capturar.</summary>
    FrameMessage? Capture();
}

/// <summary>Aplica mouse e teclado do técnico no computador do cliente.</summary>
public interface IInputSink
{
    void Mouse(MouseMessage message, int screenWidth, int screenHeight);

    void Key(KeyMessage message);
}

internal static class WebSocketHelpers
{
    public static async Task<(WebSocketMessageType Type, byte[] Data)?> ReceiveAsync(WebSocket socket, CancellationToken stop)
    {
        var buffer = new byte[32 * 1024];
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, stop);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            stream.Write(buffer, 0, result.Count);
            if (stream.Length > 6 * 1024 * 1024) return null;
        } while (!result.EndOfMessage);
        return (result.MessageType, stream.ToArray());
    }

    public static string TextType(byte[] data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            return doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    public static string? TextField(byte[] data, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            return doc.RootElement.TryGetProperty(name, out var t) ? t.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static Task SendText(WebSocket socket, string type, CancellationToken stop) =>
        socket.State == WebSocketState.Open
            ? socket.SendAsync(Encoding.UTF8.GetBytes($"{{\"type\":\"{type}\"}}"), WebSocketMessageType.Text, true, stop)
            : Task.CompletedTask;
}

/// <summary>
/// Lado do cliente (quem pede suporte). Fica ouvindo o servidor; quando um técnico pede acesso, pergunta ao usuário e,
/// se aceito, troca chaves e transmite a tela cifrada, aplicando só o que vier do técnico dentro do canal seguro.
/// </summary>
public sealed class RemoteAgent
{
    private readonly IScreenSource _screen;
    private readonly IInputSink _input;
    private readonly Func<string, Task<bool>> _askAccept;
    private readonly int _fps;
    private readonly Func<IAudioSource?>? _audioFactory;
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    /// <param name="audioFactory">Cria a captura de som a cada sessão (nulo = sem áudio).</param>
    public RemoteAgent(IScreenSource screen, IInputSink input, Func<string, Task<bool>> askAccept, int framesPerSecond = 8, Func<IAudioSource?>? audioFactory = null)
    {
        _screen = screen;
        _input = input;
        _askAccept = askAccept;
        _fps = Math.Clamp(framesPerSecond, 1, 30);
        _audioFactory = audioFactory;
        Password = RemoteIds.NewPassword();
    }

    /// <summary>Exigir também a senha da sessão. Desligado: basta o ID e o aceite, e os dois lados conferem o código de segurança.</summary>
    public bool RequirePassword { get; set; }

    /// <summary>Código de segurança da sessão atual (aparece nos dois lados).</summary>
    public string? SecurityCode { get; private set; }

    public event Action<string>? SecurityCodeReady;

    /// <summary>Senha da sessão atual. Muda sozinha ao fim de cada sessão (uso único).</summary>
    public string Password { get; private set; }

    public bool InSession { get; private set; }

    public string? Technician { get; private set; }

    public event Action<string>? StateChanged;

    public event Action? SessionEnded;

    public async Task RunAsync(WebSocket socket, CancellationToken stop)
    {
        StateChanged?.Invoke("Aguardando o técnico.");
        while (socket.State == WebSocketState.Open && !stop.IsCancellationRequested)
        {
            var message = await WebSocketHelpers.ReceiveAsync(socket, stop);
            if (message is null) break;
            if (message.Value.Type != WebSocketMessageType.Text) continue;
            switch (WebSocketHelpers.TextType(message.Value.Data))
            {
                case "request":
                    string tech = WebSocketHelpers.TextField(message.Value.Data, "technician") ?? "técnico";
                    Technician = tech;
                    bool accepted = await _askAccept(tech);
                    await WebSocketHelpers.SendText(socket, accepted ? "accept" : "reject", stop);
                    StateChanged?.Invoke(accepted ? "Conectando com o técnico..." : "Pedido recusado.");
                    break;
                case "started":
                    await StreamAsync(socket, stop);
                    break;
                case "closed":
                    StateChanged?.Invoke("Aguardando o técnico.");
                    break;
            }
        }

        StateChanged?.Invoke("Desconectado do servidor.");
    }

    private async Task StreamAsync(WebSocket socket, CancellationToken stop)
    {
        InSession = true;
        using var end = CancellationTokenSource.CreateLinkedTokenSource(stop);
        RemoteHandshake? handshake = null;
        SecureChannel? channel = null;
        IAudioSource? audio = null;
        try
        {
            handshake = new RemoteHandshake(RemoteRole.Client, RequirePassword ? Password : null);
            await socket.SendAsync(handshake.CreateHello(), WebSocketMessageType.Binary, true, end.Token);

            var hello = await WebSocketHelpers.ReceiveAsync(socket, end.Token);
            if (hello is null || hello.Value.Type != WebSocketMessageType.Binary) return;
            try { channel = handshake.Complete(hello.Value.Data); }
            catch (RemoteHandshakeException)
            {
                StateChanged?.Invoke("A senha informada pelo técnico não confere. Sessão recusada.");
                await WebSocketHelpers.SendText(socket, "end", stop);
                return;
            }

            SecurityCode = channel.SecurityCode;
            SecurityCodeReady?.Invoke(channel.SecurityCode);
            StateChanged?.Invoke($"{Technician} está acessando o seu computador.");
            var sender = SendFramesAsync(socket, channel, end.Token);
            audio = StartAudio(socket, channel, end.Token);
            int lastW = 1, lastH = 1;
            while (socket.State == WebSocketState.Open && !end.IsCancellationRequested)
            {
                var received = await WebSocketHelpers.ReceiveAsync(socket, end.Token);
                if (received is null) break;
                if (received.Value.Type == WebSocketMessageType.Text)
                {
                    if (WebSocketHelpers.TextType(received.Value.Data) is "ended" or "closed") break;
                    continue;
                }

                byte[] plain;
                try { plain = channel.Open(received.Value.Data); }
                catch (CryptographicException) { continue; }   // mensagem adulterada, repetida ou fora de ordem: ignora

                switch (RemoteMessages.Decode(plain))
                {
                    case MouseMessage mouse:
                        (lastW, lastH) = _lastScreen;
                        _input.Mouse(mouse, lastW, lastH);
                        break;
                    case KeyMessage key when RemoteMessages.IsKeyAllowed(key.VirtualKey):
                        _input.Key(key);
                        break;
                    case ControlMessage { Code: ControlCode.End }:
                        end.Cancel();
                        break;
                }
            }

            end.Cancel();
            try { await sender; } catch (OperationCanceledException) { }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
            // sessão interrompida
        }
        finally
        {
            audio?.Dispose();
            channel?.Dispose();
            handshake?.Dispose();
            InSession = false;
            Technician = null;
            SecurityCode = null;
            Password = RemoteIds.NewPassword();
            StateChanged?.Invoke("Sessão encerrada. Aguardando o técnico.");
            SessionEnded?.Invoke();
        }
    }

    private (int Width, int Height) _lastScreen = (1, 1);

    /// <summary>Cifra e envia. O WebSocket não aceita dois envios ao mesmo tempo, e o contador da cifra precisa seguir a ordem de envio.</summary>
    private async Task SendSecureAsync(WebSocket socket, SecureChannel channel, byte[] plain, CancellationToken stop)
    {
        await _sendGate.WaitAsync(stop);
        try
        {
            if (socket.State != WebSocketState.Open) return;
            byte[] sealedMessage = channel.Seal(plain);
            await socket.SendAsync(sealedMessage, WebSocketMessageType.Binary, true, stop);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private IAudioSource? StartAudio(WebSocket socket, SecureChannel channel, CancellationToken stop)
    {
        IAudioSource? source = null;
        try
        {
            source = _audioFactory?.Invoke();
            if (source is null) return null;
            source.Chunk += pcm =>
            {
                if (stop.IsCancellationRequested || pcm.Length == 0) return;
                for (int offset = 0; offset < pcm.Length; offset += 8192)
                {
                    int n = Math.Min(8192, pcm.Length - offset);
                    n -= n % 2;
                    if (n <= 0) break;
                    var part = pcm.AsSpan(offset, n).ToArray();
                    _ = SendSecureAsync(socket, channel, RemoteMessages.Encode(new AudioMessage(AudioConverter.TargetRate, part)), stop).ContinueWith(t => { _ = t.Exception; });
                }
            };
            source.Start();
            return source;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            source?.Dispose();
            return null;   // sem placa de som: a sessão segue só com imagem
        }
    }

    private async Task SendFramesAsync(WebSocket socket, SecureChannel channel, CancellationToken stop)
    {
        byte[]? previous = null;
        var lastSent = DateTime.UtcNow;
        var interval = TimeSpan.FromMilliseconds(1000.0 / _fps);
        while (!stop.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            var started = DateTime.UtcNow;
            var frame = _screen.Capture();
            if (frame is not null)
            {
                _lastScreen = (frame.ScreenWidth, frame.ScreenHeight);
                bool same = previous is not null && previous.AsSpan().SequenceEqual(frame.Jpeg);
                if (!same || DateTime.UtcNow - lastSent > TimeSpan.FromSeconds(2))
                {
                    previous = frame.Jpeg;
                    lastSent = DateTime.UtcNow;
                    await SendSecureAsync(socket, channel, RemoteMessages.Encode(frame), stop);
                }
            }

            var wait = interval - (DateTime.UtcNow - started);
            if (wait > TimeSpan.Zero) await Task.Delay(wait, stop);
        }
    }
}

/// <summary>Lado do técnico: pede acesso, aguarda o aceite do cliente, confere a senha e recebe a tela.</summary>
public sealed class RemoteTechnician : IDisposable
{
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private WebSocket? _socket;
    private SecureChannel? _channel;

    public event Action<string>? StateChanged;

    public event Action<FrameMessage>? FrameReceived;

    public event Action<AudioMessage>? AudioReceived;

    /// <summary>Código de segurança da sessão (igual ao que o cliente vê).</summary>
    public string? SecurityCode { get; private set; }

    public bool Connected => _channel is not null;

    /// <summary>Roda a sessão inteira. Devolve quando o cliente recusa, a senha falha ou a conexão acaba.</summary>
    public async Task RunAsync(WebSocket socket, string? password, CancellationToken stop)
    {
        _socket = socket;
        using var handshake = new RemoteHandshake(RemoteRole.Technician, password);
        StateChanged?.Invoke("Conectando ao servidor...");
        try
        {
            while (socket.State == WebSocketState.Open && !stop.IsCancellationRequested)
            {
                var message = await WebSocketHelpers.ReceiveAsync(socket, stop);
                if (message is null) break;
                var (type, data) = message.Value;

                if (type == WebSocketMessageType.Text)
                {
                    switch (WebSocketHelpers.TextType(data))
                    {
                        case "offline": StateChanged?.Invoke("O computador do cliente não está online. Peça para ele abrir o MB Extreme e tocar em Pedir suporte."); return;
                        case "busy": StateChanged?.Invoke("O cliente já está em atendimento."); return;
                        case "waiting": StateChanged?.Invoke("Aguardando o cliente aceitar..."); break;
                        case "rejected": StateChanged?.Invoke("O cliente recusou o acesso."); return;
                        case "timeout": StateChanged?.Invoke("O cliente não respondeu a tempo."); return;
                        case "accepted":
                            StateChanged?.Invoke("Aceito. Trocando chaves...");
                            await socket.SendAsync(handshake.CreateHello(), WebSocketMessageType.Binary, true, stop);
                            break;
                        case "ended": StateChanged?.Invoke("Sessão encerrada."); return;
                    }
                    continue;
                }

                if (_channel is null)
                {
                    try { _channel = handshake.Complete(data); }
                    catch (RemoteHandshakeException ex)
                    {
                        StateChanged?.Invoke(ex.Message + " Confira a senha com o cliente.");
                        return;
                    }
                    SecurityCode = _channel.SecurityCode;
                    StateChanged?.Invoke("Conectado.");
                    continue;
                }

                byte[] plain;
                try { plain = _channel.Open(data); }
                catch (CryptographicException) { continue; }
                switch (RemoteMessages.Decode(plain))
                {
                    case FrameMessage frame: FrameReceived?.Invoke(frame); break;
                    case AudioMessage sound: AudioReceived?.Invoke(sound); break;
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException) { }
        finally
        {
            _channel?.Dispose();
            _channel = null;
            SecurityCode = null;
        }
    }

    public async Task SendAsync(object message, CancellationToken stop = default)
    {
        var socket = _socket;
        var channel = _channel;
        if (socket is not { State: WebSocketState.Open } || channel is null) return;
        byte[] plain = message switch
        {
            MouseMessage m => RemoteMessages.Encode(m),
            KeyMessage k => RemoteMessages.Encode(k),
            ControlMessage c => RemoteMessages.Encode(c),
            _ => throw new ArgumentException("Mensagem não suportada."),
        };
        // um envio por vez: o WebSocket não aceita envios simultâneos e o contador da cifra segue a ordem
        await _sendGate.WaitAsync(stop);
        try
        {
            if (socket.State != WebSocketState.Open) return;
            await socket.SendAsync(channel.Seal(plain), WebSocketMessageType.Binary, true, stop);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    public void Dispose() => _channel?.Dispose();
}
