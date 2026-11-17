using MBExtreme.Core.Rgb;
using Xunit;

namespace MBExtreme.Core.Tests;

public class RgbTests
{
    private const string Listing = """
        0: ASUS ROG STRIX Z690-A GAMING WIFI D4
          Type:           Motherboard
          Description:    AURA Motherboard
          Version:        AULA3-6K75-0106
          Location:       HID: \\?\HID#VID_0B05
          Serial:         9876
          Modes: [Direct] Static Breathing Flashing 'Spectrum Cycle' 'Rainbow Wave' Off
          Zones: 'Addressable 1' 'Mainboard'
          LEDs: 'Addressable 1' 'LED 1'

        1: Corsair Vengeance RGB Pro
          Type:           DRAM
          Description:    Corsair Vengeance Pro RGB
          Modes: Direct 'Color Shift' ['Static']
          Zones: 'DRAM'

        2: NZXT Kraken X73
          Type:           Cooler
          Modes: [Off] Fixed Fading
        """;

    [Fact]
    public void LeDispositivosTiposEModos()
    {
        var devices = OpenRgb.ParseDevices(Listing);
        Assert.Equal(3, devices.Count);
        Assert.Equal((0, "ASUS ROG STRIX Z690-A GAMING WIFI D4", "Motherboard"), (devices[0].Index, devices[0].Name, devices[0].Type));
        Assert.Equal(new[] { "Direct", "Static", "Breathing", "Flashing", "Spectrum Cycle", "Rainbow Wave", "Off" }, devices[0].Modes.ToArray());
        Assert.Equal("Direct", devices[0].ActiveMode);
        Assert.Equal("Static", devices[1].ActiveMode);            // modo ativo com aspas dentro dos colchetes
        Assert.Contains("Color Shift", devices[1].Modes);
        Assert.Equal("Cooler", devices[2].Type);
        Assert.Equal("Off", devices[2].ActiveMode);
    }

    [Fact]
    public void SaidaVaziaOuSemDispositivosNaoQuebra()
    {
        Assert.Empty(OpenRgb.ParseDevices(""));
        Assert.Empty(OpenRgb.ParseDevices("Nenhum dispositivo\n  Type: x"));
    }

    [Fact]
    public void EscolheOModoEstaticoOuDireto()
    {
        var d = OpenRgb.ParseDevices(Listing);
        Assert.Equal("Static", OpenRgb.StaticMode(d[0]));
        Assert.Equal("Static", OpenRgb.StaticMode(d[1]));
        Assert.Null(OpenRgb.StaticMode(d[2]));
    }

    [Fact]
    public void MontaOsArgumentosParaVariosDispositivos()
    {
        var d = OpenRgb.ParseDevices(Listing);
        var args = OpenRgb.ColorArguments(new[] { (d[0], "ff0000", (string?)null), (d[2], "00FF00", (string?)null) });
        Assert.Equal(new[] { "--device", "0", "--mode", "Static", "--color", "FF0000", "--device", "2", "--color", "00FF00" }, args.ToArray());
    }

    [Fact]
    public void RecusaCorOuModoInvalidosParaNaoVirarComando()
    {
        var d = OpenRgb.ParseDevices(Listing);
        Assert.Throws<ArgumentException>(() => OpenRgb.ColorArguments(new[] { (d[0], "FF00; calc", (string?)null) }));
        Assert.Throws<ArgumentException>(() => OpenRgb.ColorArguments(new[] { (d[0], "FF0000", (string?)"Modo Inventado") }));
        Assert.False(OpenRgb.IsValidHex("#FF0000"));
        Assert.False(OpenRgb.IsValidHex("FFF"));
        Assert.True(OpenRgb.IsValidHex("00ff7A"));
    }

    [Theory]
    [InlineData("FF8040", 100, "FF8040")]
    [InlineData("FF8040", 50, "804020")]
    [InlineData("FF8040", 0, "000000")]
    [InlineData("FF8040", 250, "FF8040")]
    public void BrilhoEscalaACor(string hex, int percent, string expected) => Assert.Equal(expected, OpenRgb.WithBrightness(hex, percent));

    [Fact]
    public void CorPorTemperaturaVaiDoAzulAoVermelho()
    {
        Assert.Equal("0078FF", OpenRgb.TemperatureColor(10));
        Assert.Equal("FF0000", OpenRgb.TemperatureColor(95));
        string mid = OpenRgb.TemperatureColor(52);
        Assert.True(OpenRgb.IsValidHex(mid));
        // mais quente = mais vermelho
        int Red(string h) => Convert.ToInt32(h[..2], 16);
        Assert.True(Red(OpenRgb.TemperatureColor(70)) >= Red(OpenRgb.TemperatureColor(40)));
    }
}

public class MonitoramentoTests
{
    private static IReadOnlyList<MBExtreme.Core.Sensors.SensorGroup> Groups(double cpu, double gpu) => new[]
    {
        new MBExtreme.Core.Sensors.SensorGroup("Ryzen", "Cpu", new[] { new MBExtreme.Core.Sensors.SensorValue("/cpu/t", "CPU Package", "Temperature", cpu, null, null), new MBExtreme.Core.Sensors.SensorValue("/cpu/l", "CPU Total", "Load", 99, null, null) }),
        new MBExtreme.Core.Sensors.SensorGroup("RTX", "GpuNvidia", new[] { new MBExtreme.Core.Sensors.SensorValue("/gpu/t", "GPU Core", "Temperature", gpu, null, null) }),
    };

    [Fact]
    public void AlertaListaSoTemperaturasAcimaDoLimiteDaMaisQuenteParaAMenos()
    {
        var alerts = MBExtreme.Core.Sensors.SensorTools.HotSensors(Groups(92, 88), 85);
        Assert.Equal(new[] { "CPU Package", "GPU Core" }, alerts.Select(a => a.Sensor).ToArray());
        Assert.Empty(MBExtreme.Core.Sensors.SensorTools.HotSensors(Groups(70, 60), 85));
        Assert.Empty(MBExtreme.Core.Sensors.SensorTools.HotSensors(Groups(999, 60), 85));   // leitura absurda do chip é ignorada
    }

    [Fact]
    public void RegistroCsvTemCabecalhoELinhasComDecimalComPonto()
    {
        var log = new MBExtreme.Core.Sensors.SensorTools.CsvLog(Groups(55.5, 40));
        Assert.Equal(3, log.Columns);
        Assert.StartsWith("Hora;Ryzen - CPU Package (Temperature);Ryzen - CPU Total (Load);RTX - GPU Core (Temperature)", log.Header);
        string row = log.Row(new DateTime(2026, 10, 22, 12, 0, 5), Groups(55.5, 40));
        Assert.Equal("2026-10-22 12:00:05;55.5;99;40", row);
        // sensor que sumiu fica em branco
        string partial = log.Row(new DateTime(2026, 10, 22, 12, 0, 6), new[] { Groups(1, 2)[1] });
        Assert.Equal("2026-10-22 12:00:06;;;2", partial);
    }
}
