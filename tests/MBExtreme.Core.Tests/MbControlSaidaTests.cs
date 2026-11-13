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

public class MbControlDriversTests
{
    [Fact]
    public void Pronto_so_com_o_controle_virtual()
    {
        var none = EmulationDrivers.Check(_ => false, () => false);
        Assert.False(EmulationDrivers.Ready(none));
        Assert.All(none, c => Assert.StartsWith("https://", c.Url));
        var vigem = EmulationDrivers.Check(n => n == "ViGEmBus", () => true);
        Assert.True(EmulationDrivers.Ready(vigem));
        Assert.False(vigem.Single(c => c.Id == "hidhide").Installed);
        Assert.Equal("opcional", vigem.Single(c => c.Id == "fakerinput").Need);
    }
}

public class MbControlHallTests
{
    private static double[] Edge(double r) => Enumerable.Repeat(r, CircularityMap.Size).ToArray();

    [Fact]
    public void Hall_aprovado_e_reprovado()
    {
        var good = StickModules.Evaluate(StickModule.HallEffect, new StickMeasure(0.01, 0.005, 4), Edge(0.99));
        Assert.All(good, c => Assert.True(c.Ok, c.Item));
        var bad = StickModules.Evaluate(StickModule.HallEffect, new StickMeasure(0.04, 0.02, 12), Edge(0.85));
        Assert.Equal(3, bad.Count(c => !c.Ok));
        var over = StickModules.Evaluate(StickModule.Tmr, new StickMeasure(0.01, 0, 3), Edge(1.3));
        Assert.False(over.Single(c => c.Item.StartsWith("Sem passar")).Ok);
    }

    [Fact]
    public void Original_tem_limites_mais_folgados()
    {
        var m = new StickMeasure(0.024, 0.01, 9.5);
        Assert.All(StickModules.Evaluate(StickModule.Original, m, Edge(0.94)), c => Assert.True(c.Ok, c.Item));
        Assert.Contains(StickModules.Evaluate(StickModule.Tmr, m, Edge(0.94)), c => !c.Ok);
        Assert.True(StickModules.LooksInverted(0.8));
    }

    [Fact]
    public void Laudo_mostra_modulo_e_conferencia()
    {
        var r = new PadRepair(DateTime.Now, "DualSense", "S1", null, null, "Cliente", null, null, null, null, null, true, null,
            StickModules.Name(StickModule.HallEffect), new[] { new StickCheck("Centro", true, "0,5%") });
        string html = PadRepairLog.ReportHtml(r);
        Assert.Contains("Hall Effect", html);
        Assert.Contains("Aprovado", html);
    }
}

public class MbControlEmulacaoTests
{
    private static PadState S(PadButtons b = PadButtons.None, double lx = 0, double ly = 0, double l2 = 0) =>
        PadState.Empty with { Buttons = b, LX = lx, LY = ly, L2 = l2 };

    [Fact]
    public void Remapeia_e_desliga_botoes()
    {
        var e = new EmulationSettings();
        e.Remap["Cross"] = "Circle";
        e.Remap["Square"] = "None";
        var m = PadMapper.Map(S(PadButtons.Cross | PadButtons.Square | PadButtons.L1), e);
        Assert.Equal(PadButtons.Circle | PadButtons.L1, m.Buttons);
    }

    [Fact]
    public void Layout_nintendo_troca_botoes()
    {
        var e = new EmulationSettings { NintendoLayout = true };
        Assert.Equal(PadButtons.Circle, PadMapper.Map(S(PadButtons.Cross), e).Buttons);
        Assert.Equal(PadButtons.Square, PadMapper.Map(S(PadButtons.Triangle), e).Buttons);
    }

    [Fact]
    public void Zona_morta_gatilho_e_troca_de_analogicos()
    {
        var e = new EmulationSettings { TriggerDeadzone = 10, SwapSticks = true };
        e.Left.Deadzone = 10;
        var m = PadMapper.Map(S(lx: 0.05, l2: 0.05), e);
        Assert.Equal(0, m.L2);
        Assert.Equal(0, m.RX);           // o esquerdo (dentro da zona morta) foi para o direito
        Assert.Equal(1, PadMapper.Trigger(1, 10), 3);
        e.Left.InvertY = true;
        e.SwapSticks = false;
        Assert.True(PadMapper.Map(S(ly: 0.8), e).LY < 0);
    }

    [Fact]
    public void Eixos_do_xbox_e_do_ps4()
    {
        Assert.Equal(32767, PadMapper.XboxAxis(1));
        Assert.Equal(-32768, PadMapper.XboxAxis(-1));
        Assert.Equal(32767, PadMapper.XboxAxis(-1, invert: true));
        Assert.Equal(128, PadMapper.Ds4Axis(0));
        Assert.Equal(255, PadMapper.Ds4Axis(1));
        Assert.Equal(255, PadMapper.TriggerByte(1));
    }

    [Fact]
    public void Giroscopio_so_mirando()
    {
        var e = new EmulationSettings { GyroAim = true, GyroOnlyWhenAiming = true, GyroSensitivity = 100 };
        var spin = PadState.Empty with { GyroYaw = -100 };
        Assert.Equal(0, PadMapper.Map(spin, e).RX);
        Assert.True(PadMapper.Map(spin with { L2 = 1 }, e).RX > 0);
    }

    [Fact]
    public void Instancia_para_o_hidhide()
    {
        Assert.Equal(@"HID\VID_054C&PID_0CE6&MI_03\8&1A2B&0&0000",
            PadMapper.InstanceId(@"\\?\hid#vid_054c&pid_0ce6&mi_03#8&1a2b&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}"));
        Assert.Null(PadMapper.InstanceId("x"));
    }
}

public class ModoJogoTurboTests
{
    [Fact]
    public void Turbo_so_fecha_janelas_que_podem()
    {
        Assert.True(MBExtreme.Core.Games.GameModeRules.CanCloseWindow("chrome", 1, 1, false, true));
        Assert.False(MBExtreme.Core.Games.GameModeRules.CanCloseWindow("chrome", 1, 1, false, false));        // sem janela
        Assert.False(MBExtreme.Core.Games.GameModeRules.CanCloseWindow("Discord", 1, 1, false, true));        // protegido
        Assert.False(MBExtreme.Core.Games.GameModeRules.CanCloseWindow("game", 1, 1, true, true));            // o jogo
        Assert.False(MBExtreme.Core.Games.GameModeRules.CanCloseWindow("svc", 0, 0, false, true));            // sessão 0
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "chrome" };
        Assert.False(MBExtreme.Core.Games.GameModeRules.CanCloseWindow("chrome", 1, 1, false, true, keep));    // nunca fechar
    }

    [Fact]
    public void Lista_automatica_nao_tem_protegidos()
    {
        Assert.All(MBExtreme.Core.Games.GameModeRules.AutoClose, n => Assert.DoesNotContain(n, MBExtreme.Core.Games.GameModeRules.NeverTouch));
        Assert.Contains("Copilot", MBExtreme.Core.Games.GameModeRules.AutoClose);
    }
}

public class MbControlKbmTests
{
    [Fact]
    public void Teclas_viram_botoes_e_analogico()
    {
        var s = MBExtreme.Core.Gamepads.KbmMapper.Build(new HashSet<string> { "MoveUp", "MoveRight", "Cross", "R2" }, 0.5, -0.2);
        Assert.True(s.Buttons.HasFlag(PadButtons.Cross));
        Assert.True(s.LY < 0 && s.LX > 0);
        Assert.Equal(0.7071, s.LX, 3);
        Assert.Equal(1, s.R2);
        Assert.Equal(0.5, s.RX);
    }

    [Fact]
    public void Mouse_parado_zero_e_mexendo_sai_da_zona_morta()
    {
        var k = new KbmSettings { AntiDeadzone = 20, Curve = StickCurve.Linear, Acceleration = 0, Sensitivity = 50 };
        Assert.Equal((0.0, 0.0), KbmMapper.MouseToStick(0, 0, k));
        var (x, y) = KbmMapper.MouseToStick(0.1, 0, k);
        Assert.True(x >= 0.2 && y == 0);
        Assert.Equal(1, KbmMapper.MouseToStick(100, 0, k).X, 3);
        Assert.Equal(0.5, KbmMapper.Smooth(0, 1, 50), 3);
    }

    [Fact]
    public void Teclas_padrao_existem()
    {
        foreach (var key in KbmMapper.DefaultBindings().Keys) Assert.True(KbmMapper.Keys.ContainsKey(key), key);
        Assert.Equal("Espaço", KbmMapper.KeyLabel("Space"));
        Assert.Equal("1", KbmMapper.KeyLabel("D1"));
    }
}
