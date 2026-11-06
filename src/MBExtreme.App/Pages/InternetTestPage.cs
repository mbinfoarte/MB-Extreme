using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Network;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Teste de internet: latência e variação, download e upload com várias conexões e aquecimento, o servidor usado e a cidade,
/// e o histórico para comparar. Usa o servidor público de velocidade da Cloudflare.
/// </summary>
public sealed class InternetTestPage : ToolPageBase
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly SpeedHistory _history = new();
    private readonly TextBlock _live = new() { FontSize = 30, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _stage = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap };
    private readonly Button _start;
    private CancellationTokenSource? _cts;
    private int _connections = 1;

    private static readonly int[] Connections = { 1, 4, 8 };

    public InternetTestPage() : base("Teste de internet",
        "Mede a latência, a variação, o download e o upload com várias conexões ao mesmo tempo, descartando os primeiros segundos (aquecimento). " +
        "O servidor é o teste público da Cloudflare, perto de você; os números podem diferir do Speedtest, que usa outros servidores e outro método.")
    {
        _start = AddButton("Testar", async (_, _) => await RunAsync());
        AddButton("Parar", (_, _) => _cts?.Cancel(), false);
        Loaded += (_, _) => { if (Body.Children.Count == 0) Build(null); };
    }

    private void Build(SpeedResult? result)
    {
        Body.Children.Clear();
        Body.Children.Add(Muted("Conexões ao mesmo tempo (mais conexões medem melhor internet acima de 300 Mbps):", 12));
        Body.Children.Add(Choice(Connections.Select(c => c == 1 ? "Automático" : c + " conexões").ToList(), Array.IndexOf(Connections, _connections), i => _connections = Connections[i]));
        Body.Children.Add(_stage);
        Body.Children.Add(_live);
        if (result is not null)
        {
            Body.Children.Add(Row($"Download {Fmt(result.DownloadMbps)} · Upload {Fmt(result.UploadMbps)}",
                $"Latência {Ms(result.PingMs)} · variação {Ms(result.JitterMs)} · servidor {result.ServerCode ?? "?"}{(result.City is null ? "" : " (" + result.City + ")")} · {result.Connections} conexões"));
        }
        var history = _history.Load();
        if (SpeedHistory.Compare(history) is { } compare) Body.Children.Add(Muted(compare, 12.5));
        if (history.Count > 0)
        {
            Body.Children.Add(Heading("Testes anteriores"));
            foreach (var h in history.Take(10))
                Body.Children.Add(Muted($"{h.At.ToLocalTime():dd/MM HH:mm} · ↓ {Fmt(h.DownloadMbps)} · ↑ {Fmt(h.UploadMbps)} · {Ms(h.PingMs)} · {h.ServerCode ?? "?"}", 12));
        }
    }

    private static string Fmt(double? mbps) => mbps is double v ? $"{v:0.0} Mbps" : "—";

    private static string Ms(double? ms) => ms is double v ? $"{v:0} ms" : "—";

    private async Task RunAsync()
    {
        _start.IsEnabled = false;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var test = new SpeedTest(Http);
        int connections = _connections == 1 ? 4 : _connections;
        Build(null);
        var live = new Progress<double>(v => _live.Text = $"{v:0.0} Mbps");
        try
        {
            _stage.Text = "Achando o servidor...";
            var (code, city) = await test.ServerAsync(token);
            _stage.Text = $"Latência ({code ?? "servidor"}{(city is null ? "" : ", " + city)})...";
            var (ping, jitter) = await test.LatencyAsync(12, token);
            if (ping is null) { SetStatus("Sem resposta do servidor de teste. Confira a internet.", "BadBrush"); return; }
            _live.Text = $"{ping:0} ms";
            _stage.Text = $"Download com {connections} conexões (2 s de aquecimento + 8 s de medição)...";
            var down = await test.DownloadAsync(connections, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8), live, token);
            _stage.Text = $"Upload com {connections} conexões (2 s de aquecimento + 8 s de medição)...";
            var up = await test.UploadAsync(connections, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8), live, token);
            var result = new SpeedResult(DateTimeOffset.Now, down, up, ping, jitter, code, city, connections);
            _history.Add(result);
            _stage.Text = "Pronto.";
            _live.Text = "";
            Build(result);
            SetStatus($"Download {Fmt(down)}, upload {Fmt(up)}, latência {Ms(ping)}.", "OkBrush");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Teste parado.", "WarnBrush");
        }
        catch (HttpRequestException ex)
        {
            SetStatus("Não foi possível testar: " + ex.Message, "BadBrush");
        }
        finally
        {
            _start.IsEnabled = true;
        }
    }
}
