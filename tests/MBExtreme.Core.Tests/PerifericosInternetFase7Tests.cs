using System.Net;
using MBExtreme.Core.Devices;
using MBExtreme.Core.Network;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>Fase 7: contas dos testes de controle e mouse, e o teste de internet (com um servidor de mentira, sem rede).</summary>
public class PerifericosInternetFase7Tests
{
    [Fact]
    public void Deriva_e_zona_morta_sugerida()
    {
        var rest = new[] { new StickSample(0.01, 0.02), new StickSample(-0.12, 0.03), new StickSample(0.05, -0.01) };
        double drift = StickMath.Drift(rest);
        Assert.InRange(drift, 0.12, 0.13);
        Assert.True(StickMath.HasDrift(drift));
        Assert.Equal(0.18, StickMath.SuggestedDeadzone(drift), 2);
        Assert.Equal(0.05, StickMath.SuggestedDeadzone(0));        // nunca menos que 5%
        Assert.Equal(0.30, StickMath.SuggestedDeadzone(0.9));      // nem mais que 30%
        Assert.False(StickMath.HasDrift(0.03));
    }

    [Fact]
    public void Circularidade_do_circulo_perfeito_e_do_quadrado()
    {
        var circle = Enumerable.Range(0, 720).Select(i => { double a = i * Math.PI / 360; return new StickSample(Math.Cos(a), Math.Sin(a)); }).ToList();
        var (circleError, circleCoverage) = StickMath.Circularity(circle);
        Assert.True(circleError < 0.5, $"círculo: {circleError}");
        Assert.Equal(100, circleCoverage);

        // analógico com curso quadrado (comum em controles baratos): nas diagonais passa de 1
        var square = circle.Select(s => { double m = Math.Max(Math.Abs(s.X), Math.Abs(s.Y)); return new StickSample(s.X / m, s.Y / m); }).ToList();
        Assert.True(StickMath.Circularity(square).ErrorPercent > 15);

        var half = circle.Take(360).ToList();   // só meia volta
        Assert.InRange(StickMath.Circularity(half).Coverage, 45, 55);
        Assert.Equal(-1.0, StickSample.Normalize(short.MinValue));
        Assert.Equal(1.0, StickSample.Normalize(short.MaxValue));
    }

    [Fact]
    public void Taxa_do_mouse_ignora_as_pausas_e_arredonda_para_a_de_fabrica()
    {
        var times = new List<double>();
        double t = 0;
        for (int i = 0; i < 200; i++) { times.Add(t); t += i == 100 ? 400 : 1.0; }   // 1000 Hz com uma pausa no meio
        double hz = MouseMath.PollingRate(times)!.Value;
        Assert.InRange(hz, 990, 1010);
        Assert.Equal(1000, MouseMath.NearestStandard(hz));
        Assert.Equal(125, MouseMath.NearestStandard(130));
        Assert.Equal(500, MouseMath.NearestStandard(470));
        Assert.Null(MouseMath.PollingRate(new[] { 0.0, 1 }));
        Assert.True(MouseMath.IsChatter(35));
        Assert.False(MouseMath.IsChatter(250));
    }

    [Fact]
    public void Contas_do_teste_de_internet()
    {
        Assert.Equal(100, SpeedMath.Mbps(12_500_000, 1), 3);
        Assert.Equal(20, SpeedMath.Median(new double[] { 30, 10, 20 }));
        Assert.Equal(15, SpeedMath.Median(new double[] { 10, 20 }));
        Assert.Equal(5, SpeedMath.Jitter(new double[] { 20, 25, 20, 25 }));
        var trace = SpeedMath.ParseTrace("fl=1\nh=speed.cloudflare.com\nip=200.1.2.3\ncolo=GRU\nloc=BR\n");
        Assert.Equal("GRU", trace["colo"]);
        Assert.Equal("São Paulo", SpeedMath.CityOf("gru"));
        Assert.Null(SpeedMath.CityOf("XYZ"));
    }

    /// <summary>Servidor de mentira: entrega bytes no download, aceita o upload e responde o trace.</summary>
    private sealed class FakeServer : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Delay(2, ct);
            string url = request.RequestUri!.PathAndQuery;
            if (url.StartsWith("/cdn-cgi/trace")) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("colo=POA\nloc=BR\n") };
            if (url.StartsWith("/__up")) return new HttpResponseMessage(HttpStatusCode.OK);
            long bytes = long.Parse(url[(url.IndexOf('=') + 1)..]);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[Math.Min(bytes, 2_000_000)]) };
        }
    }

    [Fact]
    public async Task Teste_de_internet_completo_com_servidor_de_mentira()
    {
        var test = new SpeedTest(new HttpClient(new FakeServer()), "https://teste.local");
        var (code, city) = await test.ServerAsync(CancellationToken.None);
        Assert.Equal(("POA", "Porto Alegre"), (code, city));
        var (ping, jitter) = await test.LatencyAsync(5, CancellationToken.None);
        Assert.NotNull(ping);
        Assert.NotNull(jitter);
        var readings = new List<double>();
        var down = await test.DownloadAsync(3, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(800), new SyncProgress(readings.Add), CancellationToken.None);
        Assert.True(down > 0);
        Assert.NotEmpty(readings);
        // o envio só conta bytes quando um POST inteiro termina; com a máquina de CI ocupada (testes em paralelo), janela curta pode ficar vazia
        var up = await test.UploadAsync(2, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(2000), null, CancellationToken.None);
        Assert.True(up > 0);
    }

    [Fact]
    public async Task Parar_o_teste_de_internet()
    {
        var test = new SpeedTest(new HttpClient(new FakeServer()), "https://teste.local");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => test.DownloadAsync(2, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), null, cts.Token));
    }

    [Fact]
    public void Historico_compara_com_a_media_dos_anteriores()
    {
        string path = Path.Combine(Path.GetTempPath(), $"mbx-internet-{Guid.NewGuid():N}.json");
        var history = new SpeedHistory(path);
        SpeedResult R(double down) => new(DateTimeOffset.Now, down, 50, 10, 1, "GRU", "São Paulo", 4);
        history.Add(R(100));
        history.Add(R(100));
        var list = history.Add(R(150));
        Assert.Equal(150, list[0].DownloadMbps);
        Assert.Equal("Download 50% mais rápido que a média dos últimos 2 testes.", SpeedHistory.Compare(list));
        Assert.Equal("Download igual à média dos últimos 3 testes.", SpeedHistory.Compare(history.Add(R(118))));
        Assert.Null(SpeedHistory.Compare(new[] { R(10) }));
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
