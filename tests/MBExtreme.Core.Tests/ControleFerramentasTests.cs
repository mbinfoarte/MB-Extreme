using MBExtreme.Core.Gamepads;
using Xunit;

namespace MBExtreme.Core.Tests;

public class ControleFerramentasTests
{
    [Fact]
    public void Marca_e_ligacao_pelo_caminho()
    {
        Assert.Equal("Sony PlayStation", ControllerCatalog.Brand(0x054C));
        Assert.Equal("Genérico", ControllerCatalog.Brand(0x1234));
        Assert.Equal(PadConnection.Bluetooth, ControllerCatalog.ConnectionOf(@"\\?\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&0002054c_pid&0ce6#9&1"));
        Assert.Equal(PadConnection.Usb, ControllerCatalog.ConnectionOf(@"\\?\hid#vid_054c&pid_0ce6&mi_03#8&1"));
        Assert.Equal(PadConnection.Unknown, ControllerCatalog.ConnectionOf(null));
    }

    [Fact]
    public void Recursos_oficiais_so_https_ou_loja()
    {
        foreach (ushort v in new ushort[] { ControllerCatalog.Sony, ControllerCatalog.Microsoft, ControllerCatalog.EightBitDo, ControllerCatalog.Nintendo })
        {
            Assert.NotEmpty(ControllerCatalog.Resources(v));
            Assert.All(ControllerCatalog.Resources(v), r => Assert.True(r.Url.StartsWith("https://") || r.Url.StartsWith("ms-windows-store://")));
        }
        Assert.Empty(ControllerCatalog.Resources(0x1234));
    }

    [Fact]
    public void Taxa_de_atualizacao_250Hz()
    {
        var times = Enumerable.Range(0, 200).Select(i => i * 4.0).ToList();
        var stats = PollingStats.From(times)!;
        Assert.Equal(250, stats.RateHz);
        Assert.Equal(4, stats.AverageMs);
        Assert.Equal(0, stats.JitterMs);
        Assert.Contains("250", stats.Verdict);
    }

    [Fact]
    public void Taxa_ignora_pausas_e_poucos_pacotes()
    {
        Assert.Null(PollingStats.From(new double[] { 0, 1, 2 }));
        var times = Enumerable.Range(0, 50).Select(i => i * 1.0).Concat(Enumerable.Range(0, 50).Select(i => 2000 + i * 1.0)).ToList();
        Assert.Equal(1000, PollingStats.From(times)!.RateHz);
    }

    [Fact]
    public void Perfil_zona_morta_e_curva()
    {
        var p = new PadProfile { Deadzone = 10, Sensitivity = 100, Curve = StickCurve.Linear };
        Assert.Equal((0.0, 0.0), PadProfiles.Apply(p, 0.05, 0));
        Assert.Equal(1.0, PadProfiles.Apply(p, 1, 0).X, 3);
        Assert.Equal(0.5, PadProfiles.Apply(p, 0.55, 0).X, 3);
        p.Curve = StickCurve.Suave;
        Assert.Equal(0.25, PadProfiles.Apply(p, 0.55, 0).X, 3);
        p.AntiDeadzone = 20;
        Assert.True(PadProfiles.Apply(p, 0.11, 0).X >= 0.2);
        Assert.All(PadProfiles.Defaults, d => Assert.False(string.IsNullOrWhiteSpace(d.Notes)));
    }
}
