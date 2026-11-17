using MBExtreme.Core.Fans;
using Xunit;

namespace MBExtreme.Core.Tests;

public class VentoinhasTests
{
    private sealed class FakeBackend : IFanBackend
    {
        public Dictionary<string, double> Temps = new() { ["cpu"] = 40, ["gpu"] = 40 };
        public Dictionary<string, double> Fans = new();
        public List<string> Autos = new();
        public bool RestoredAll;
        public bool RefuseSet;
        public List<FanChannel> Chans = new()
        {
            new("fan1", "Fan Control #1", "Placa", "Motherboard", 800, 50, true, false, FanChannelKind.Case),
            new("pump", "Pump Control", "AIO", "Cooler", 2000, 80, true, false, FanChannelKind.Pump),
            new("ro", "Fan #5", "Placa", "Motherboard", 700, null, false, false, FanChannelKind.Case),
        };
        public IReadOnlyList<FanChannel> Channels() => Chans;
        public IReadOnlyList<TemperatureSource> Temperatures() => Temps.Select(t => new TemperatureSource(t.Key, t.Key == "cpu" ? "CPU Package" : "GPU Core", t.Value)).ToList();
        public double? Temperature(string id) => Temps.TryGetValue(id, out var v) ? v : null;
        public bool SetPercent(string id, double p) { if (RefuseSet) return false; Fans[id] = p; return true; }
        public void SetAuto(string id) { Autos.Add(id); Fans.Remove(id); }
        public void RestoreAll() => RestoredAll = true;
    }

    private static FanChannelConfig Curve(string id, FanChannelKind kind = FanChannelKind.Case, string? source = "cpu") =>
        new() { ChannelId = id, Kind = kind, Mode = "curve", SourceSensorId = source, Curve = FanCurve.Balanced.Points };

    [Fact]
    public void CurvaInterpolaEFicaNosExtremos()
    {
        var curve = new FanCurve(new[] { new CurvePoint(30, 30), new CurvePoint(70, 70) });
        Assert.Equal(30, curve.Evaluate(10));
        Assert.Equal(50, curve.Evaluate(50), 5);
        Assert.Equal(70, curve.Evaluate(90));
        Assert.Equal(100, new FanCurve().Evaluate(50));   // sem pontos: não arrisca
    }

    [Fact]
    public void CurvaNuncaDesceQuandoATemperaturaSobe()
    {
        var curve = new FanCurve(new[] { new CurvePoint(30, 80), new CurvePoint(60, 40), new CurvePoint(90, 100), new CurvePoint(200, 150), new CurvePoint(-5, -3) });
        Assert.All(curve.Points, p => Assert.InRange(p.Percent, 0, 100));
        for (int i = 1; i < curve.Points.Count; i++) Assert.True(curve.Points[i].Percent >= curve.Points[i - 1].Percent);
        Assert.InRange(curve.Points[^1].TemperatureC, 0, 110);
    }

    [Theory]
    [InlineData(FanChannelKind.Pump, 20, 40, 60)]      // bomba nunca abaixo de 60%
    [InlineData(FanChannelKind.Cpu, 20, 40, 30)]
    [InlineData(FanChannelKind.Case, 5, 40, 20)]
    [InlineData(FanChannelKind.Gpu, 0, 40, 0)]         // placa de vídeo pode parar em repouso
    [InlineData(FanChannelKind.Gpu, 0, 60, 30)]
    [InlineData(FanChannelKind.Case, 30, 82, 80)]      // quente: no mínimo 80%
    [InlineData(FanChannelKind.Case, 30, 91, 100)]     // muito quente: 100%
    public void RegrasDeSegurancaVencemACurva(FanChannelKind kind, double wanted, double temp, double expected) =>
        Assert.Equal(expected, FanSafety.Clamp(wanted, kind, temp));

    [Fact]
    public void SobeNaHoraEDesceDevagarComHisterese()
    {
        Assert.Equal(80, FanSafety.Smooth(50, 80, 70, 60));                 // sobe direto
        Assert.Equal(60, FanSafety.Smooth(60, 55, 59.5, 60));              // queda pequena da temperatura: mantém
        Assert.Equal(55, FanSafety.Smooth(60, 30, 40, 60), 0.01);          // desce no máximo 5 por passo
    }

    [Fact]
    public void MotorAplicaACurvaPelaTemperaturaDaFonte()
    {
        var b = new FakeBackend();
        b.Temps["cpu"] = 70;
        var engine = new FanEngine(b, () => new[] { Curve("fan1") });
        var step = engine.Step();
        Assert.Equal(75, b.Fans["fan1"]);
        Assert.Equal("curve", step[0].Mode);
    }

    [Fact]
    public void SemLeituraDaTemperaturaVaiA100PorSeguranca()
    {
        var b = new FakeBackend();
        var engine = new FanEngine(b, () => new[] { Curve("fan1", source: "sensor-inexistente") });
        var step = engine.Step();
        Assert.Equal(100, b.Fans["fan1"]);
        Assert.Contains("segurança", step[0].Note);
    }

    [Fact]
    public void BombaNuncaFicaAbaixoDe60MesmoNoManual()
    {
        var b = new FakeBackend();
        var engine = new FanEngine(b, () => new[] { new FanChannelConfig { ChannelId = "pump", Kind = FanChannelKind.Pump, Mode = "manual", ManualPercent = 10 } });
        engine.Step();
        Assert.Equal(60, b.Fans["pump"]);   // manual vale na hora, já com o mínimo da bomba
    }

    [Fact]
    public void ManualNaoIgnoraTemperaturaAlta()
    {
        var b = new FakeBackend();
        b.Temps["gpu"] = 92;
        var engine = new FanEngine(b, () => new[] { new FanChannelConfig { ChannelId = "fan1", Kind = FanChannelKind.Case, Mode = "manual", ManualPercent = 25 } });
        engine.Step();
        Assert.Equal(100, b.Fans["fan1"]);
    }

    [Fact]
    public void VoltarParaAutomaticoDevolveAoFirmwareUmaVez()
    {
        var b = new FakeBackend();
        var config = Curve("fan1");
        var engine = new FanEngine(b, () => new[] { config });
        engine.Step();
        Assert.True(b.Fans.ContainsKey("fan1"));
        config.Mode = "auto";
        engine.Step();
        engine.Step();
        Assert.Equal(new[] { "fan1" }, b.Autos);   // só uma vez
    }

    [Fact]
    public void IgnoraCanaisSemControleEHardwareQueRecusa()
    {
        var b = new FakeBackend { RefuseSet = true };
        var engine = new FanEngine(b, () => new[] { Curve("ro"), Curve("fan1") });
        var results = engine.Step();
        Assert.Single(results);                       // "ro" só lê, não controla
        Assert.Contains("não aceitou", results[0].Note);
    }

    [Fact]
    public void PararDevolveTudoAoFirmware()
    {
        var b = new FakeBackend();
        var engine = new FanEngine(b, () => new[] { Curve("fan1") });
        engine.Step();
        engine.Stop();
        Assert.True(b.RestoredAll);
    }

    [Fact]
    public void DesceDevagarQuandoEsfria()
    {
        var b = new FakeBackend();
        var engine = new FanEngine(b, () => new[] { Curve("fan1") });
        b.Temps["cpu"] = 80; engine.Step();
        Assert.Equal(100, b.Fans["fan1"]);
        b.Temps["cpu"] = 35; b.Temps["gpu"] = 35; engine.Step();
        Assert.Equal(95, b.Fans["fan1"]);           // desce 5 por passo, não de uma vez
    }

    [Fact]
    public void PerfisFicamEmDiscoENormalizados()
    {
        string file = Path.Combine(Path.GetTempPath(), "mbx-fan-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new FanProfileStore(file);
            store.Save(new FanChannelConfig { ChannelId = "x", Kind = FanChannelKind.Cpu, Mode = "curve", Curve = new List<CurvePoint> { new(60, 40), new(30, 90) } });
            var again = new FanProfileStore(file).Get("x", FanChannelKind.Case);
            Assert.Equal("curve", again.Mode);
            Assert.Equal(30, again.Curve[0].TemperatureC);
            Assert.True(again.Curve[1].Percent >= again.Curve[0].Percent);
            Assert.Equal("auto", new FanProfileStore(file).Get("outro", FanChannelKind.Case).Mode);
        }
        finally { File.Delete(file); }
    }

    [Theory]
    [InlineData("Motherboard", "Fan Control #1", FanChannelKind.Case)]
    [InlineData("GpuNvidia", "GPU Fan", FanChannelKind.Gpu)]
    [InlineData("Cooler", "Pump", FanChannelKind.Pump)]
    [InlineData("Motherboard", "CPU Fan Control", FanChannelKind.Cpu)]
    public void AdivinhaOTipoDaVentoinha(string hardware, string name, FanChannelKind expected) => Assert.Equal(expected, FanControlService.GuessKind(hardware, name));
}

public class NvidiaSmiTests
{
    [Fact]
    public void LeLimitesDePotenciaEClock()
    {
        string csv = "0, NVIDIA GeForce RTX 3060 Ti, 200.00, 200.00, 100.00, 220.00, 2100, 47\r\n1, NVIDIA GeForce GT 1030, [N/A], [N/A], [N/A], [N/A], 1700, 40";
        var gpus = NvidiaSmi.ParseLimits(csv);
        Assert.Equal(2, gpus.Count);
        Assert.Equal("NVIDIA GeForce RTX 3060 Ti", gpus[0].Name);
        Assert.Equal((200, 200, 100, 220), (gpus[0].PowerLimitW, gpus[0].PowerDefaultW, gpus[0].PowerMinW, gpus[0].PowerMaxW));
        Assert.Equal(2100, gpus[0].MaxGraphicsClockMhz);
        Assert.Null(gpus[1].PowerLimitW);                 // placa que não deixa mexer na potência
        Assert.Null(NvidiaSmi.PowerRange(gpus[1]));
        Assert.Equal((100, 200), NvidiaSmi.PowerRange(gpus[0]));   // nunca acima do padrão
    }

    [Fact]
    public void LeClocksSemRepetirEIgnoraLixo()
    {
        Assert.Equal(new[] { 210, 1500, 2100 }, NvidiaSmi.ParseClocks("2100\n2100\n1500\n210\n[N/A]\n\n99999").ToArray());
    }

    [Fact]
    public void ArgumentosSaoValidadosParaNaoVirarComando()
    {
        Assert.Equal(new[] { "-i", "0", "-pl", "180" }, NvidiaSmi.PowerArguments(0, 180.4));
        Assert.Equal(new[] { "-i", "1", "-lgc", "210,1800" }, NvidiaSmi.ClockArguments(1, 210, 1800));
        Assert.Throws<ArgumentException>(() => NvidiaSmi.ClockArguments(0, 1800, 210));
        Assert.Throws<ArgumentException>(() => NvidiaSmi.ClockArguments(0, 210, 90000));
        Assert.Equal(new[] { "-i", "0", "-rgc" }, NvidiaSmi.ResetClockArguments(0));
    }
}
