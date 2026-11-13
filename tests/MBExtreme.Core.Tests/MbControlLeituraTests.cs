using MBExtreme.Core.Gamepads;
using Xunit;

namespace MBExtreme.Core.Tests;

public class MbControlLeituraTests
{
    private static byte[] UsbDs5()
    {
        var r = new byte[64];
        r[0] = 0x01; r[1] = 255; r[2] = 128; r[3] = 128; r[4] = 0; r[5] = 0; r[6] = 255;
        r[8] = 0x28;          // direcional parado (8) + Cross
        r[9] = 0x01;          // L1
        r[53] = 0x15;         // carregando (1) com nível 5
        return r;
    }

    [Fact]
    public void Bluetooth_dualsense_igual_ao_usb()
    {
        var usb = PadInput.Parse(PadModel.DualSense, UsbDs5())!;
        var bt = new byte[78];
        bt[0] = 0x31; bt[1] = 0x10;
        Array.Copy(UsbDs5(), 1, bt, 2, 63);
        var viaBt = PadInput.ParseAny(PadModel.DualSense, bt)!;
        Assert.Equal(usb.LX, viaBt.LX);
        Assert.Equal(usb.R2, viaBt.R2);
        Assert.Equal(usb.Buttons, viaBt.Buttons);
        Assert.Equal(usb.Battery, viaBt.Battery);
        Assert.True(viaBt.Charging);
        Assert.True(viaBt.Buttons.HasFlag(PadButtons.Cross));
        Assert.True(viaBt.Buttons.HasFlag(PadButtons.L1));
    }

    [Fact]
    public void Bluetooth_dualshock4_tira_cabecalho_de_3_bytes()
    {
        var usb = new byte[64];
        usb[0] = 0x01; usb[1] = 0; usb[2] = 255; usb[5] = 0x18; usb[9] = 200; usb[30] = 0x05;
        var bt = new byte[547];
        bt[0] = 0x11; bt[1] = 0xC0; bt[2] = 0x00;
        Array.Copy(usb, 1, bt, 3, 63);
        var s = PadInput.ParseAny(PadModel.DualShock4, bt)!;
        Assert.Equal(-0.9961, s.LX, 3);
        Assert.True(s.Buttons.HasFlag(PadButtons.Square));
        Assert.Equal(200 / 255.0, s.R2, 3);
        Assert.Equal(55, s.Battery);
    }

    [Fact]
    public void Relatorio_curto_do_bluetooth_sem_bateria()
    {
        var r = new byte[10];
        r[0] = 0x01; r[1] = 128; r[2] = 128; r[3] = 0; r[4] = 255; r[5] = 0x88; r[6] = 0x02; r[8] = 0; r[9] = 128;
        var s = PadInput.ParseAny(PadModel.DualSense, r)!;
        Assert.True(s.Buttons.HasFlag(PadButtons.Triangle));
        Assert.True(s.Buttons.HasFlag(PadButtons.R1));
        Assert.Equal(-1, s.Battery);
        Assert.Equal(128 / 255.0, s.R2, 3);
    }

    [Fact]
    public void Ligacao_e_recursos()
    {
        Assert.Equal(PadConnection.Bluetooth, PadDetails.ConnectionOf(78));
        Assert.Equal(PadConnection.Usb, PadDetails.ConnectionOf(64));
        var bt = PadDetails.Features(PadModel.DualSense, PadConnection.Bluetooth);
        Assert.False(bt.Single(f => f.Name.StartsWith("Vibração háptica")).Supported);
        Assert.True(PadDetails.Features(PadModel.DualSense, PadConnection.Usb).Single(f => f.Name.StartsWith("Vibração háptica")).Supported);
        Assert.False(PadDetails.Features(PadModel.DualShock4, PadConnection.Usb).Single(f => f.Name == "Gatilhos adaptáveis").Supported);
        Assert.Contains(PadDetails.Features(PadModel.DualSenseEdge, PadConnection.Usb), f => f.Name.Contains("FN"));
    }

    [Fact]
    public void Mac_duracao_e_texto()
    {
        Assert.Equal(0xE8473A3F843CUL, PadDetails.ParseMac("E8:47:3A:3F:84:3C"));
        Assert.Equal(0UL, PadDetails.ParseMac("xx"));
        Assert.Equal("01:02:03", PadDetails.Duration(new TimeSpan(1, 2, 3)));
        string text = PadDetails.ToText("MB", new[] { ("Ligação", "USB") }, new[] { new PadFeature("Touchpad", true) });
        Assert.Contains("Ligação: USB", text);
        Assert.Contains("[x] Touchpad", text);
    }
}
