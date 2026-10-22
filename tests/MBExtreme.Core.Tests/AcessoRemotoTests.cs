using System.Security.Cryptography;
using MBExtreme.Core.Remote;
using Xunit;

namespace MBExtreme.Core.Tests;

public class AcessoRemotoTests
{
    private static (SecureChannel Client, SecureChannel Tech) Pair(string clientPassword, string techPassword)
    {
        using var client = new RemoteHandshake(RemoteRole.Client, clientPassword);
        using var tech = new RemoteHandshake(RemoteRole.Technician, techPassword);
        byte[] helloC = client.CreateHello(), helloT = tech.CreateHello();
        return (client.Complete(helloT), tech.Complete(helloC));
    }

    [Fact]
    public void IdTemNoveDigitosEFormata()
    {
        for (int i = 0; i < 200; i++)
        {
            string id = RemoteIds.NewId();
            Assert.Equal(9, id.Length);
            Assert.NotEqual('0', id[0]);
        }
        Assert.Equal("123 456 789", RemoteIds.FormatId("123456789"));
    }

    [Theory]
    [InlineData("123 456 789", "123456789")]
    [InlineData("123-456-789", "123456789")]
    [InlineData("123456789", "123456789")]
    [InlineData("12345678", null)]
    [InlineData("023456789", null)]
    [InlineData("12a456789", null)]
    [InlineData(null, null)]
    public void LeOIdComOuSemSeparadores(string? input, string? expected) => Assert.Equal(expected, RemoteIds.ParseId(input));

    [Fact]
    public void SenhaTemTamanhoEAlfabetoSemCaracteresParecidos()
    {
        string p = RemoteIds.NewPassword();
        Assert.Equal(RemoteIds.PasswordLength, p.Length);
        Assert.DoesNotContain(p, c => "AEIOU01".Contains(c));
        Assert.Equal("ABCDE-FGHJK", RemoteIds.FormatPassword("ABCDEFGHJK"));
        Assert.Equal("ABCDEFGHJK", RemoteIds.NormalizePassword("abcde-fghjk "));
    }

    [Fact]
    public void ComAMesmaSenhaOsDoisLadosTrocamMensagens()
    {
        var (client, tech) = Pair("ABCDE-FGHJK", "abcdefghjk");
        byte[] sealedMessage = client.Seal(new byte[] { 1, 2, 3 });
        Assert.Equal(new byte[] { 1, 2, 3 }, tech.Open(sealedMessage));
        Assert.Equal(new byte[] { 9, 9 }, client.Open(tech.Seal(new byte[] { 9, 9 })));
    }

    [Fact]
    public void SenhaErradaFalhaNoAcordo()
    {
        using var client = new RemoteHandshake(RemoteRole.Client, "AAAAAAAAAA");
        using var tech = new RemoteHandshake(RemoteRole.Technician, "BBBBBBBBBB");
        Assert.Throws<RemoteHandshakeException>(() => client.Complete(tech.CreateHello()));
        Assert.Throws<RemoteHandshakeException>(() => tech.Complete(client.CreateHello()));
    }

    [Fact]
    public void ServidorNoMeioSemASenhaNaoConsegue()
    {
        // o servidor tenta se passar pelo técnico com outra senha
        using var client = new RemoteHandshake(RemoteRole.Client, "CORRETA234");
        using var attacker = new RemoteHandshake(RemoteRole.Technician, "PALPITE234");
        Assert.Throws<RemoteHandshakeException>(() => client.Complete(attacker.CreateHello()));
    }

    [Fact]
    public void MensagemDoMesmoPapelNaoValeComoDoOutro()
    {
        using var a = new RemoteHandshake(RemoteRole.Client, "SENHA23456");
        using var b = new RemoteHandshake(RemoteRole.Client, "SENHA23456");
        Assert.Throws<RemoteHandshakeException>(() => a.Complete(b.CreateHello()));
    }

    [Fact]
    public void RepeticaoEAdulteracaoSaoRecusadas()
    {
        var (client, tech) = Pair("SENHA23456", "SENHA23456");
        byte[] m1 = client.Seal(new byte[] { 1 });
        byte[] m2 = client.Seal(new byte[] { 2 });
        tech.Open(m2);
        Assert.Throws<CryptographicException>(() => tech.Open(m1));   // fora de ordem
        Assert.Throws<CryptographicException>(() => tech.Open(m2));   // repetida

        byte[] m3 = client.Seal(new byte[] { 3, 3, 3 });
        m3[10] ^= 0xFF;
        Assert.ThrowsAny<CryptographicException>(() => tech.Open(m3));  // adulterada
        Assert.Throws<CryptographicException>(() => tech.Open(new byte[5]));
    }

    [Fact]
    public void MensagemNaoVoltaParaQuemEnviou()
    {
        var (client, _) = Pair("SENHA23456", "SENHA23456");
        Assert.ThrowsAny<CryptographicException>(() => client.Open(client.Seal(new byte[] { 1 })));
    }

    [Fact]
    public void MensagensSaemDoFormatoEEntramDeVolta()
    {
        var frame = Assert.IsType<FrameMessage>(RemoteMessages.Decode(RemoteMessages.Encode(new FrameMessage(1920, 1080, new byte[] { 0xFF, 0xD8, 1, 2 }))));
        Assert.Equal((1920, 1080), (frame.ScreenWidth, frame.ScreenHeight));
        var mouse = Assert.IsType<MouseMessage>(RemoteMessages.Decode(RemoteMessages.Encode(new MouseMessage(MouseAction.Down, MouseButton.Right, 100, 200, -120))));
        Assert.Equal((MouseAction.Down, MouseButton.Right, 100, 200, -120), (mouse.Action, mouse.Button, mouse.X, mouse.Y, mouse.Wheel));
        var key = Assert.IsType<KeyMessage>(RemoteMessages.Decode(RemoteMessages.Encode(new KeyMessage(0x41, true, false))));
        Assert.Equal((0x41, true, false), (key.VirtualKey, key.Down, key.Extended));
        Assert.Equal(ControlCode.End, Assert.IsType<ControlMessage>(RemoteMessages.Decode(RemoteMessages.Encode(new ControlMessage(ControlCode.End)))).Code);
    }

    [Fact]
    public void MensagensInvalidasSaoRecusadas()
    {
        Assert.Null(RemoteMessages.Decode(Array.Empty<byte>()));
        Assert.Null(RemoteMessages.Decode(new byte[] { 99 }));
        Assert.Null(RemoteMessages.Decode(new byte[] { 2, 9, 0, 0, 0, 0, 0, 0, 0 }));   // ação inexistente
        Assert.Null(RemoteMessages.Decode(new byte[] { 2, 0, 0, 0, 0 }));               // curta
        Assert.Null(RemoteMessages.Decode(new byte[] { 3, 0, 0, 1, 0 }));               // tecla 0
        Assert.Null(RemoteMessages.Decode(new byte[] { 3, 1, 0, 1, 7 }));               // flag inválida
        Assert.Null(RemoteMessages.Decode(new byte[] { 1, 0, 1, 0, 1 }));               // quadro sem imagem
        Assert.Throws<ArgumentException>(() => RemoteMessages.Encode(new FrameMessage(1, 1, Array.Empty<byte>())));
    }

    [Fact]
    public void PosicaoNormalizadaViraPixelDentroDaTela()
    {
        Assert.Equal((0, 0), RemoteMessages.ToScreen(0, 0, 1920, 1080));
        Assert.Equal((1919, 1079), RemoteMessages.ToScreen(65535, 65535, 1920, 1080));
        var (x, y) = RemoteMessages.ToScreen(32768, 32768, 1920, 1080);
        Assert.InRange(x, 958, 962);
        Assert.InRange(y, 538, 542);
        Assert.InRange(RemoteMessages.Normalize(500, 1000), 32766, 32768);
        Assert.Equal(0, RemoteMessages.Normalize(10, 0));
    }

    [Fact]
    public void TeclaWindowsNaoPodeSerEnviada()
    {
        Assert.False(RemoteMessages.IsKeyAllowed(0x5B));
        Assert.False(RemoteMessages.IsKeyAllowed(0));
        Assert.True(RemoteMessages.IsKeyAllowed(0x41));
    }
}
