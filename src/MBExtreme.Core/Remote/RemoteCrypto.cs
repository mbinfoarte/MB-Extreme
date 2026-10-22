using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace MBExtreme.Core.Remote;

public enum RemoteRole : byte { Client = 1, Technician = 2 }

public sealed class RemoteHandshakeException : Exception
{
    public RemoteHandshakeException(string message) : base(message) { }
}

/// <summary>
/// Acordo de chave entre o computador do cliente e o técnico, por cima do servidor de retransmissão.
/// Cada lado envia sua chave pública ECDH (P-256) autenticada por HMAC com a senha da sessão, que só o cliente e o técnico conhecem.
/// Assim o servidor não consegue se colocar no meio sem saber a senha. A chave da sessão sai do ECDH misturado com a senha.
/// Limite conhecido: um servidor malicioso poderia tentar adivinhar a senha offline a partir do HMAC; a senha tem ~49 bits e o custo do PBKDF2 é alto.
/// Evolução prevista: trocar por um PAKE (SPAKE2/OPAQUE).
/// </summary>
public sealed class RemoteHandshake : IDisposable
{
    private const byte HelloType = 1;
    private const int NonceLength = 16;
    private const int MacLength = 32;
    private const int PasswordIterations = 200_000;
    private static readonly byte[] PasswordSalt = Encoding.UTF8.GetBytes("mbx-remote-pw-v1");

    private readonly ECDiffieHellman _ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
    private readonly byte[] _passwordKey;
    private readonly RemoteRole _role;
    private readonly byte[] _nonce = RandomNumberGenerator.GetBytes(NonceLength);
    private byte[]? _publicKey;

    public RemoteHandshake(RemoteRole role, string password)
    {
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("Senha vazia.", nameof(password));
        _role = role;
        _passwordKey = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(RemoteIds.NormalizePassword(password)), PasswordSalt, PasswordIterations, HashAlgorithmName.SHA256, 32);
    }

    /// <summary>Mensagem a enviar ao outro lado: tipo, nonce, chave pública e HMAC.</summary>
    public byte[] CreateHello()
    {
        _publicKey ??= _ecdh.ExportSubjectPublicKeyInfo();
        var message = new byte[1 + NonceLength + _publicKey.Length + MacLength];
        message[0] = HelloType;
        _nonce.CopyTo(message, 1);
        _publicKey.CopyTo(message, 1 + NonceLength);
        Mac(_passwordKey, _role, _nonce, _publicKey).CopyTo(message, 1 + NonceLength + _publicKey.Length);
        return message;
    }

    /// <summary>Confere a mensagem do outro lado e cria o canal cifrado. Falha se a senha não bater.</summary>
    public SecureChannel Complete(byte[] peerHello)
    {
        if (peerHello.Length < 1 + NonceLength + 60 + MacLength || peerHello.Length > 400 || peerHello[0] != HelloType)
            throw new RemoteHandshakeException("Mensagem de conexão inválida.");

        var peerRole = _role == RemoteRole.Client ? RemoteRole.Technician : RemoteRole.Client;
        var peerNonce = peerHello.AsSpan(1, NonceLength).ToArray();
        var peerKey = peerHello.AsSpan(1 + NonceLength, peerHello.Length - 1 - NonceLength - MacLength).ToArray();
        var peerMac = peerHello.AsSpan(peerHello.Length - MacLength).ToArray();

        if (!CryptographicOperations.FixedTimeEquals(Mac(_passwordKey, peerRole, peerNonce, peerKey), peerMac))
            throw new RemoteHandshakeException("A senha da sessão não confere.");
        if (CryptographicOperations.FixedTimeEquals(peerNonce, _nonce))
            throw new RemoteHandshakeException("Mensagem repetida.");

        byte[] secret;
        try
        {
            using var peer = ECDiffieHellman.Create();
            peer.ImportSubjectPublicKeyInfo(peerKey, out _);
            secret = _ecdh.DeriveRawSecretAgreement(peer.PublicKey);
        }
        catch (CryptographicException)
        {
            throw new RemoteHandshakeException("Chave do outro lado inválida.");
        }

        _publicKey ??= _ecdh.ExportSubjectPublicKeyInfo();
        byte[] clientNonce = _role == RemoteRole.Client ? _nonce : peerNonce;
        byte[] techNonce = _role == RemoteRole.Client ? peerNonce : _nonce;
        byte[] info = Encoding.UTF8.GetBytes("mbx-remote-v1").Concat(clientNonce).Concat(techNonce).ToArray();
        byte[] key = HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, 32, _passwordKey, info);
        CryptographicOperations.ZeroMemory(secret);
        return new SecureChannel(key, _role);
    }

    private static byte[] Mac(byte[] key, RemoteRole role, byte[] nonce, byte[] publicKey)
    {
        using var hmac = new HMACSHA256(key);
        hmac.TransformBlock(Encoding.UTF8.GetBytes("mbx-hello"), 0, 9, null, 0);
        hmac.TransformBlock(new[] { (byte)role }, 0, 1, null, 0);
        hmac.TransformBlock(nonce, 0, nonce.Length, null, 0);
        hmac.TransformFinalBlock(publicKey, 0, publicKey.Length);
        return hmac.Hash!;
    }

    public void Dispose()
    {
        _ecdh.Dispose();
        CryptographicOperations.ZeroMemory(_passwordKey);
    }
}

/// <summary>Canal cifrado (AES-256-GCM) com contador por sentido: repetição ou reordenação de mensagens é recusada.</summary>
public sealed class SecureChannel : IDisposable
{
    private readonly AesGcm _aes;
    private readonly RemoteRole _role;
    private ulong _sendCounter;
    private ulong _lastReceived;
    private bool _anyReceived;

    internal SecureChannel(byte[] key, RemoteRole role)
    {
        _aes = new AesGcm(key, 16);
        _role = role;
    }

    public const int Overhead = 8 + 16;

    /// <summary>Cifra: [contador 8 bytes][texto cifrado][etiqueta 16 bytes].</summary>
    public byte[] Seal(ReadOnlySpan<byte> plain)
    {
        ulong counter = ++_sendCounter;
        var output = new byte[8 + plain.Length + 16];
        BinaryPrimitives.WriteUInt64BigEndian(output, counter);
        _aes.Encrypt(Nonce(_role, counter), plain, output.AsSpan(8, plain.Length), output.AsSpan(8 + plain.Length, 16), new[] { (byte)_role });
        return output;
    }

    public byte[] Open(ReadOnlySpan<byte> sealedMessage)
    {
        if (sealedMessage.Length < Overhead) throw new CryptographicException("Mensagem curta demais.");
        ulong counter = BinaryPrimitives.ReadUInt64BigEndian(sealedMessage);
        if (_anyReceived && counter <= _lastReceived) throw new CryptographicException("Mensagem repetida ou fora de ordem.");
        var peer = _role == RemoteRole.Client ? RemoteRole.Technician : RemoteRole.Client;
        int length = sealedMessage.Length - Overhead;
        var plain = new byte[length];
        _aes.Decrypt(Nonce(peer, counter), sealedMessage.Slice(8, length), sealedMessage.Slice(8 + length, 16), plain, new[] { (byte)peer });
        _lastReceived = counter;
        _anyReceived = true;
        return plain;
    }

    private static byte[] Nonce(RemoteRole role, ulong counter)
    {
        var nonce = new byte[12];
        nonce[0] = (byte)role;
        BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), counter);
        return nonce;
    }

    public void Dispose() => _aes.Dispose();
}
