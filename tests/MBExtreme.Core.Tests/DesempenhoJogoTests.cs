using MBExtreme.Core.Games;
using Xunit;

namespace MBExtreme.Core.Tests;

public class DesempenhoJogoTests
{
    private static PerfSession S(bool opt, double avg, double low, int secs = 120, int day = 1) =>
        new() { StartedAt = new DateTime(2026, 10, day), Optimized = opt, AvgFps = avg, Low1Fps = low, Seconds = secs };

    [Fact]
    public void Gravador_SessaoCurta_NaoValeComoMedida()
    {
        var r = new SessionRecorder();
        for (int i = 0; i < SessionRecorder.MinimumSeconds - 1; i++) r.Add(100, 80, 10);
        Assert.Null(r.Finish(false, DateTime.Now));
    }

    [Fact]
    public void Gravador_ResumeMediaMinimoEUmPorCentoPior()
    {
        var r = new SessionRecorder();
        for (int i = 0; i < 99; i++) r.Add(100, 90, 10);
        r.Add(40, 40, 25);
        var s = r.Finish(true, DateTime.Now)!;
        Assert.Equal(100, s.Seconds);
        Assert.Equal(99.4, s.AvgFps);
        Assert.Equal(40, s.MinFps);
        Assert.Equal(100, s.MaxFps);
        Assert.Equal(40, s.Low1Fps);
        Assert.True(s.Optimized);
    }

    [Fact]
    public void Gravador_IgnoraLeiturasInvalidas()
    {
        var r = new SessionRecorder();
        r.Add(0, 0, 0); r.Add(double.NaN, 1, 1); r.Add(double.PositiveInfinity, 1, 1);
        Assert.Equal(0, r.Count);
    }

    [Fact]
    public void Comparacao_SemDados_PedeParaJogar()
    {
        Assert.Equal(Verdict.NotEnoughData, PerfCompare.Build(Array.Empty<PerfSession>()).Verdict);
        var soAntes = PerfCompare.Build(new[] { S(false, 80, 60) });
        Assert.Equal(Verdict.NotEnoughData, soAntes.Verdict);
        Assert.Contains("Otimize", soAntes.Message);
    }

    [Fact]
    public void Comparacao_Ganho_CalculaPorcentagem()
    {
        var c = PerfCompare.Build(new[] { S(false, 80, 55, day: 1), S(true, 100, 70, day: 2) });
        Assert.Equal(Verdict.Better, c.Verdict);
        Assert.Equal(25, c.AvgChangePercent);
        Assert.Equal(27.3, c.LowChangePercent);
        Assert.Contains("80", c.Message);
    }

    [Fact]
    public void Comparacao_Piora_AvisaParaDesfazer()
    {
        var c = PerfCompare.Build(new[] { S(false, 100, 80, day: 1), S(true, 85, 60, day: 2) });
        Assert.Equal(Verdict.Worse, c.Verdict);
        Assert.Contains("Desfaça", c.Message);
    }

    [Fact]
    public void Comparacao_DiferencaPequena_EhVariacaoNormal()
    {
        var c = PerfCompare.Build(new[] { S(false, 100, 80, day: 1), S(true, 101, 81, day: 2) });
        Assert.Equal(Verdict.Same, c.Verdict);
        Assert.Contains(c.Warnings, w => w.Contains("variação normal"));
    }

    [Fact]
    public void Comparacao_SessoesDiferentes_Avisa()
    {
        var c = PerfCompare.Build(new[] { S(false, 80, 60, 40, 1), S(true, 100, 70, 300, 2) });
        Assert.Contains(c.Warnings, w => w.Contains("durações"));
        Assert.Contains(PerfCompare.Build(new[] { S(false, 80, 60, 30, 1), S(true, 100, 70, 30, 2) }).Warnings, w => w.Contains("menos de 1 minuto"));
    }

    [Fact]
    public void Comparacao_UsaAUltimaMedidaDeAntesAnteriorAoDepois()
    {
        var c = PerfCompare.Build(new[] { S(false, 50, 40, day: 1), S(false, 80, 60, day: 2), S(true, 100, 75, day: 3), S(false, 90, 70, day: 4) });
        Assert.Equal(80, c.Before!.AvgFps);
        Assert.Equal(100, c.After!.AvgFps);
    }

    [Fact]
    public void Armazenamento_GuardaEMantemAsUltimas20()
    {
        var store = new PerfStore(Path.Combine(Path.GetTempPath(), $"mbx-perf-{Guid.NewGuid():N}.json"));
        for (int i = 0; i < 25; i++) store.Add("g", S(i % 2 == 0, 100 + i, 80, day: 1 + i % 28));
        Assert.Equal(20, store.Get("g").Count);
        Assert.Empty(store.Get("outro"));
        store.Clear("g");
        Assert.Empty(store.Get("g"));
    }
}
