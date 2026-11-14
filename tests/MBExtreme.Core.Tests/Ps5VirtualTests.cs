using System.Net;
using System.Net.Sockets;
using System.Text;
using MBExtreme.Core.Gamepads;
using Xunit;

namespace MBExtreme.Core.Tests;

public class Ps5VirtualTests
{
    [Fact]
    public void Pacote_do_DualSense_virtual_tem_33_bytes_no_formato_do_VIIPER()
    {
        var s = PadState.Empty with
        {
            LX = 1, LY = -1, RX = 0, RY = 0.5, L2 = 1, R2 = 0.2,
            Buttons = PadButtons.Cross | PadButtons.Options | PadButtons.Up | PadButtons.Right | PadButtons.Mute,
            GyroPitch = 10, GyroYaw = -5, GyroRoll = 0, AccelX = 0, AccelY = 1, AccelZ = 0,
        };
        var b = ViiperWire.DualSense(s);
        Assert.Equal(33, b.Length);
        Assert.Equal(127, (sbyte)b[0]);
        Assert.Equal(-127, (sbyte)b[1]);
        Assert.Equal(0, (sbyte)b[2]);
        Assert.Equal(64, (sbyte)b[3]);
        uint buttons = BitConverter.ToUInt32(b, 4);
        Assert.Equal(0x20u | 0x2000u | 0x40000u | 0x400u, buttons);   // ✕, Options, Mudo e L2 apertado até o fundo
        Assert.Equal(0x01 | 0x08, b[8]);                              // cima + direita
        Assert.Equal(255, b[9]);
        Assert.Equal(51, b[10]);
        Assert.Equal(164, BitConverter.ToInt16(b, 21));               // 10 °/s × 16,384
        Assert.Equal(-82, BitConverter.ToInt16(b, 23));
        Assert.Equal(8192, BitConverter.ToInt16(b, 29));
        // sem acelerômetro: controle deitado (−1 g no Z)
        Assert.Equal(-8192, BitConverter.ToInt16(ViiperWire.DualSense(PadState.Empty), 31));
    }

    [Fact]
    public void Retorno_erros_e_tipos()
    {
        var f = ViiperWire.ParseFeedback(new byte[] { 10, 200, 1, 2, 3, 4 })!.Value;
        Assert.Equal((10, 200, 1, 2, 3, 4), (f.RumbleSmall, f.RumbleLarge, f.Red, f.Green, f.Blue, f.PlayerLeds));
        Assert.Null(ViiperWire.ParseFeedback(new byte[] { 1, 2 }));
        var ex = Assert.Throws<InvalidOperationException>(() => ViiperWire.Parse("""{"status":404,"title":"Not Found","detail":"bus 9"}"""));
        Assert.Contains("Not Found", ex.Message);
        Assert.Equal(3, ViiperWire.Parse("""{"busId":3}""").GetProperty("busId").GetInt32());
        Assert.Equal("dualsense", ViiperWire.DeviceType(VirtualTarget.DualSense));
        Assert.Equal("dualsenseedge", ViiperWire.DeviceType(VirtualTarget.DualSenseEdge));
        Assert.Equal("bus/1/add {\"type\":\"dualsense\"}\0", Encoding.UTF8.GetString(ViiperWire.Request("bus/1/add", "{\"type\":\"dualsense\"}")));
    }

    [Fact]
    public void Prontidao_depende_do_controle_escolhido()
    {
        var onlyViiper = EmulationDrivers.Check(_ => false, () => true, () => true);
        Assert.True(EmulationDrivers.ReadyFor(onlyViiper, VirtualTarget.DualSense));
        Assert.False(EmulationDrivers.ReadyFor(onlyViiper, VirtualTarget.Xbox360));
        var onlyVigem = EmulationDrivers.Check(n => n == "ViGEmBus", () => true, () => false);
        Assert.False(EmulationDrivers.ReadyFor(onlyVigem, VirtualTarget.DualSenseEdge));
        Assert.True(EmulationDrivers.ReadyFor(onlyVigem, VirtualTarget.DualShock4));
        Assert.Contains(onlyVigem, c => c.Id == "viiper" && c.Url.StartsWith("https://github.com/Alia5/VIIPER"));
    }

    [Fact]
    public void Conversa_com_um_servidor_de_mentira()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var seen = new List<string>();
        var server = Task.Run(() =>
        {
            for (int i = 0; i < 3; i++)
            {
                using var c = listener.AcceptTcpClient();
                var s = c.GetStream();
                var buf = new List<byte>();
                int b;
                while ((b = s.ReadByte()) > 0) buf.Add((byte)b);
                string req = Encoding.UTF8.GetString(buf.ToArray());
                lock (seen) seen.Add(req);
                string resp = req.StartsWith("ping") ? """{"server":"VIIPER","version":"1.2.3"}"""
                    : req.StartsWith("bus/create") ? """{"busId":4}"""
                    : """{"busId":4,"devId":"1","type":"dualsense"}""";
                s.Write(Encoding.UTF8.GetBytes(resp + "\n"));
            }
        });
        var client = new ViiperClient("127.0.0.1", port);
        Assert.Equal("1.2.3", client.Ping(10_000));   // folga para a máquina de CI ocupada
        Assert.Equal(4, client.CreateBus());
        Assert.Equal("1", client.AddDevice(4, "dualsense"));
        Assert.True(server.Wait(20_000));
        listener.Stop();
        Assert.Equal(new[] { "ping", "bus/create", "bus/4/add {\"type\":\"dualsense\"}" }, seen);
        Assert.Null(new ViiperClient("127.0.0.1", port).Ping());   // ninguém escutando
    }
}
