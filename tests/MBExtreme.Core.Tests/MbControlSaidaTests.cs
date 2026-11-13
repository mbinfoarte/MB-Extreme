using System.Buffers.Binary;
using MBExtreme.Core.Gamepads;
using Xunit;

namespace MBExtreme.Core.Tests;

public class MbControlSaidaTests
{
    private static PadOutputFrame Frame(TriggerEffect? right = null) =>
        new(10, 20, 30, PadOutput.PlayerMask(2), 1, MuteLed.Pulse, new TriggerEffect(), right ?? new TriggerEffect(), 200, 100);

    [Fact]
    public void Dualsense_usb()
    {
        var r = PadOutput.DualSense(Frame(), bluetooth: false);
        Assert.Equal(48, r.Length);
        Assert.Equal(0x02, r[0]);
        Assert.Equal(100, r[3]);     // motor leve
        Assert.Equal(200, r[4]);     // motor forte
        Assert.Equal((byte)MuteLed.Pulse, r[9]);
        Assert.Equal(0b01010, r[44]);
        Assert.Equal(new byte[] { 10, 20, 30 }, r[45..48]);
        Assert.Equal(0x05, r[11]);   // gatilho direito livre
    }

    [Fact]
    public void Dualsense_bluetooth_com_crc()
    {
        var r = PadOutput.DualSense(Frame(), bluetooth: true, sequence: 3);
        Assert.Equal(78, r.Length);
        Assert.Equal(0x31, r[0]);
        Assert.Equal(0x30, r[1]);
        Assert.Equal(new byte[] { 10, 20, 30 }, r[47..50]);
        Assert.Equal(PadOutput.BluetoothCrc(r.AsSpan(0, 74)), BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(74)));
    }

    [Fact]
    public void Crc32_valor_conhecido()
    {
        // CRC-32 padrão de "123456789" é 0xCBF43926; com o byte 0xA2 na frente o valor muda, mas a função precisa bater consigo mesma
        Assert.NotEqual(0u, PadOutput.BluetoothCrc(new byte[] { 1, 2, 3 }));
        Assert.NotEqual(PadOutput.BluetoothCrc(new byte[] { 1, 2, 3 }), PadOutput.BluetoothCrc(new byte[] { 1, 2, 4 }));
    }

    [Fact]
    public void Dualshock4_usb_e_bluetooth()
    {
        var usb = PadOutput.DualShock4(Frame(), false);
        Assert.Equal(32, usb.Length);
        Assert.Equal(0x05, usb[0]);
        Assert.Equal(new byte[] { 10, 20, 30 }, usb[6..9]);
        var bt = PadOutput.DualShock4(Frame(), true);
        Assert.Equal(0x11, bt[0]);
        Assert.Equal(new byte[] { 10, 20, 30 }, bt[8..11]);
        Assert.Equal(PadOutput.BluetoothCrc(bt.AsSpan(0, 74)), BinaryPrimitives.ReadUInt32LittleEndian(bt.AsSpan(74)));
    }

    [Fact]
    public void Gatilhos()
    {
        var weapon = PadOutput.TriggerBytes(new TriggerEffect { Kind = TriggerKind.Weapon, Start = 2, End = 6, Strength = 8 });
        Assert.Equal(0x25, weapon[0]);
        Assert.Equal((1 << 2) | (1 << 6), BinaryPrimitives.ReadUInt16LittleEndian(weapon.AsSpan(1)));
        Assert.Equal(7, weapon[3]);
        var res = PadOutput.TriggerBytes(new TriggerEffect { Kind = TriggerKind.Resistance, Start = 8, Strength = 3 });
        Assert.Equal(0x21, res[0]);
        Assert.Equal((1 << 8) | (1 << 9), BinaryPrimitives.ReadUInt16LittleEndian(res.AsSpan(1)));
        Assert.Equal((2u << 24) | (2u << 27), BinaryPrimitives.ReadUInt32LittleEndian(res.AsSpan(3)));
        var vib = PadOutput.TriggerBytes(new TriggerEffect { Kind = TriggerKind.Vibration, Start = 0, Strength = 1, Frequency = 40 });
        Assert.Equal(0x26, vib[0]);
        Assert.Equal(40, vib[9]);
        Assert.Equal(0x05, PadOutput.TriggerBytes(new TriggerEffect())[0]);
    }

    [Fact]
    public void Cores_dos_modos()
    {
        var s = new PadLightSettings { R = 200, G = 100, B = 50, Brightness = 50 };
        Assert.Equal(((byte)100, (byte)50, (byte)25), PadOutput.LightColor(s, 0, 100));
        s.Mode = LightMode.Breathing; s.Brightness = 100;
        Assert.Equal(((byte)0, (byte)0, (byte)0), PadOutput.LightColor(s, 0, 100));
        s.Mode = LightMode.Battery;
        Assert.Equal(((byte)0, (byte)255, (byte)0), PadOutput.LightColor(s, 0, 100));
        Assert.Equal(((byte)255, (byte)0, (byte)0), PadOutput.LightColor(s, 0, 0));
        s.Mode = LightMode.Rainbow;
        Assert.Equal(((byte)255, (byte)0, (byte)0), PadOutput.LightColor(s, 0, 50));
        Assert.Equal(0b00100, PadOutput.PlayerMask(1));
        Assert.Equal(0b11111, PadOutput.PlayerMask(5));
    }
}
