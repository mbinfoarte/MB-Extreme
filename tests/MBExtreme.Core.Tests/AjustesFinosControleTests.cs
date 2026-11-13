using MBExtreme.Core.Gamepads;
using Xunit;

namespace MBExtreme.Core.Tests;

public class AjustesFinosControleTests
{
    private static PadState Pad(double lx = 0, double ly = 0, double rx = 0, double ry = 0, double l2 = 0, double r2 = 0, double yaw = 0, double pitch = 0) =>
        PadState.Empty with { LX = lx, LY = ly, RX = rx, RY = ry, L2 = l2, R2 = r2, GyroYaw = yaw, GyroPitch = pitch };

    [Theory]
    [InlineData(0.3, 0.1)]
    [InlineData(0.05, 0.02)]
    [InlineData(0.9, -0.4)]
    public void Padrao_continua_igual_ao_perfil_antigo(double x, double y)
    {
        var t = new StickTuning { Deadzone = 8, AntiDeadzone = 4, Sensitivity = 110, Curve = StickCurve.Suave };
        var old = PadProfiles.Apply(new PadProfile { Deadzone = 8, AntiDeadzone = 4, Sensitivity = 110, Curve = StickCurve.Suave }, x, y);
        Assert.Equal(old, PadMapper.Stick(x, y, t));
    }

    [Fact]
    public void Zona_morta_externa_chega_no_maximo_antes_da_borda()
    {
        var t = new StickTuning { Deadzone = 0, OuterDeadzone = 10 };
        Assert.Equal(1, PadMapper.Stick(0.9, 0, t).X, 3);
        Assert.Equal(0.5, PadMapper.Stick(0.45, 0, t).X, 3);
        Assert.True(PadMapper.Stick(0.85, 0, new StickTuning { Deadzone = 0 }).X < 0.9);
    }

    [Fact]
    public void Curva_personalizada_forca_vertical_e_inversao()
    {
        var t = new StickTuning { Deadzone = 0, Curve = StickCurve.Personalizada, CurvePower = 200 };
        Assert.Equal(0.25, PadMapper.Stick(0.5, 0, t).X, 3);
        t.CurvePower = 100;
        Assert.Equal(0.5, PadMapper.Stick(0.5, 0, t).X, 3);

        var v = new StickTuning { Deadzone = 0, VerticalScale = 50, Axial = true, InvertX = true };
        var o = PadMapper.Stick(0.4, 0.8, v);
        Assert.Equal(-0.4, o.X, 3);
        Assert.Equal(0.4, o.Y, 3);
    }

    [Fact]
    public void Anti_retorno_segura_a_mola_mas_nao_o_movimento_de_verdade()
    {
        var e = new EmulationSettings();
        e.Left = new StickTuning { Deadzone = 0, AntiSnapback = true };
        var f = new PadFilter();
        PadMapper.Map(Pad(lx: 1), e, f, 0.004);
        // soltou: a mola jogou o analógico para o outro lado
        var snap = PadMapper.Map(Pad(lx: -0.4), e, f, 0.004);
        Assert.Equal(0, snap.LX);
        // passado o instante, o movimento volta a valer
        PadMapper.Map(Pad(lx: -0.05), e, f, 0.03);
        var later = PadMapper.Map(Pad(lx: -0.5), e, f, 0.03);
        Assert.Equal(-0.5, later.LX, 3);
        // empurrar com força para o outro lado não é segurado
        PadMapper.Map(Pad(lx: 1), e, f, 0.004);
        Assert.Equal(-0.9, PadMapper.Map(Pad(lx: -0.9), e, f, 0.004).LX, 3);
        // sem o filtro (sem tempo), nada muda
        Assert.Equal(-0.4, PadMapper.Map(Pad(lx: -0.4), e).LX, 3);
    }

    [Fact]
    public void Suavizacao_segue_o_comando_aos_poucos()
    {
        var e = new EmulationSettings { Right = new StickTuning { Deadzone = 0, Smoothing = 100 } };
        var f = new PadFilter();
        PadMapper.Map(Pad(), e, f, 0.004);
        double first = PadMapper.Map(Pad(rx: 1), e, f, 0.004).RX;
        Assert.InRange(first, 0.01, 0.5);
        double x = first;
        for (int i = 0; i < 100; i++) x = PadMapper.Map(Pad(rx: 1), e, f, 0.004).RX;
        Assert.Equal(1, x, 2);
    }

    [Fact]
    public void Gatilhos_com_curso_curto_e_sensivel()
    {
        var t = new TriggerTuning { Deadzone = 10, Max = 60 };
        Assert.Equal(0, PadMapper.Trigger(0.08, t, 4));
        Assert.Equal(0.5, PadMapper.Trigger(0.35, t, 4), 3);
        Assert.Equal(1, PadMapper.Trigger(0.7, t, 4));
        Assert.Equal(1, PadMapper.Trigger(0.12, new TriggerTuning { Deadzone = 10, HairTrigger = true }, 4));
        // sem ajuste próprio, usa a zona morta geral (comportamento antigo)
        Assert.Equal(PadMapper.Trigger(0.5, 4), PadMapper.Trigger(0.5, new TriggerTuning(), 4));
        var e = new EmulationSettings { R2 = new TriggerTuning { HairTrigger = true } };
        Assert.Equal(1, PadMapper.Map(Pad(r2: 0.2), e).R2);
        Assert.Equal(0.2, PadMapper.Map(Pad(l2: 0.2), new EmulationSettings { TriggerDeadzone = 0 }).L2, 3);
    }

    [Fact]
    public void Giroscopio_ignora_mao_parada_inverte_e_filtra()
    {
        var e = new EmulationSettings { GyroDeadzone = 2, GyroSmoothing = 0, GyroInvertX = true, GyroVertical = 50 };
        Assert.Equal((0.0, 0.0), PadMapper.Gyro(1.5, -1.9, e));
        var (x, y) = PadMapper.Gyro(10, 10, e);
        Assert.Equal(-8, x, 3);
        Assert.Equal(4, y, 3);

        // filtro One Euro: tremida pequena fica bem menor, movimento firme passa
        e = new EmulationSettings { GyroDeadzone = 0, GyroSmoothing = 100 };
        var f = new PadFilter();
        double max = 0;
        for (int i = 0; i < 200; i++)
        {
            var (jx, _) = PadMapper.Gyro(i == 0 ? 0 : i % 2 == 0 ? 1.0 : -1.0, 0, e, f, 0.004);
            if (i > 20) max = Math.Max(max, Math.Abs(jx));
        }
        Assert.True(max < 0.2, $"tremida passou: {max}");
        double v = 0;
        for (int i = 0; i < 200; i++) v = PadMapper.Gyro(100, 0, e, f, 0.004).X;
        Assert.InRange(v, 90, 101);
    }

    [Fact]
    public void Previa_sem_giroscopio_nao_mexe_na_configuracao_real()
    {
        var e = new EmulationSettings { GyroAim = true, GyroOnlyWhenAiming = false };
        var copy = e.WithoutGyro();
        Assert.False(copy.GyroAim);
        Assert.True(e.GyroAim);
        Assert.Same(e.Right, copy.Right);
        Assert.Equal(0, PadMapper.Map(Pad(yaw: 50), copy).RX);
        Assert.NotEqual(0, PadMapper.Map(Pad(yaw: 50), e).RX);
    }

    [Fact]
    public void Configuracao_antiga_sem_os_campos_novos_carrega()
    {
        var json = """{"Target":0,"Left":{"Deadzone":8},"Right":{"Deadzone":6},"TriggerDeadzone":4}""";
        var e = System.Text.Json.JsonSerializer.Deserialize<EmulationSettings>(json)!;
        Assert.NotNull(e.L2);
        Assert.Equal(100, e.Left.VerticalScale);
        Assert.Equal(0.5, PadMapper.Map(Pad(l2: 0.52), e).L2, 1);
    }
}
