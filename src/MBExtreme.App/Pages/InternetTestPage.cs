using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
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
    private readonly TextBox _name = new() { Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 4, 0, 10) };
    private readonly UniformGrid _steps = new() { Columns = 6, Margin = new Thickness(0, 6, 0, 6) };
    private readonly Dictionary<string, TextBlock> _stepValues = new();

    private static readonly int[] Connections = { 1, 4, 8 };
    private readonly MBExtreme.UI.Controls.SpeedGauge _gauge = new() { Width = 340, Height = 340 };
    private readonly Grid _gaugeHost = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) };
    private readonly TextBlock _downBig = new() { Text = "—", FontSize = 34, FontWeight = FontWeights.Light, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _upBig = new() { Text = "—", FontSize = 34, FontWeight = FontWeights.Light, HorizontalAlignment = HorizontalAlignment.Center };
    private Button? _go;

    /// <summary>Topo do teste, como os testes de velocidade conhecidos: download e upload grandes, o velocímetro e o botão redondo de começar.</summary>
    private UIElement GaugeCard()
    {
        var top = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 4), HorizontalAlignment = HorizontalAlignment.Center, Width = 420 };
        foreach (var (title, value, color) in new[] { ("↓ DOWNLOAD Mbps", _downBig, "#FFC400"), ("↑ UPLOAD Mbps", _upBig, "#B57CFF") })
        {
            var cell = new StackPanel();
            cell.Children.Add(new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)) });
            if (value.Parent is Panel old) old.Children.Remove(value);
            cell.Children.Add(value);
            top.Children.Add(cell);
        }
        if (_gauge.Parent is Panel g) g.Children.Remove(_gauge);
        _gaugeHost.Children.Clear();
        _gaugeHost.Children.Add(_gauge);
        _go = new Button { Width = 132, Height = 132, Cursor = System.Windows.Input.Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var t = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(66));
        border.SetValue(Border.BackgroundProperty, Application.Current.FindResource("ButtonBrush"));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        t.VisualTree = border;
        _go.Template = t;
        _go.Content = new TextBlock { Text = "INICIAR", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Brushes.Black };
        System.Windows.Automation.AutomationProperties.SetName(_go, "Iniciar o teste de internet");
        _go.Click += async (_, _) => await RunAsync();
        _gaugeHost.Children.Add(_go);
        _gauge.BeginAnimation(OpacityProperty, null);
        _gauge.Opacity = 0.35;
        Motion.Breathe(_go, 1.05, 1.5);
        var card = new StackPanel();
        card.Children.Add(top);
        card.Children.Add(_gaugeHost);
        return card;
    }

    /// <summary>Some com o botão e acende o velocímetro (o teste começou).</summary>
    private void GaugeRunning()
    {
        if (_go is not null) { Motion.Stop(_go); _gaugeHost.Children.Remove(_go); _go = null; }
        _gauge.Reset();
        Motion.To(_gauge, OpacityProperty, 1, Motion.Normal);
    }

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
        Body.Children.Add(GaugeCard());
        if (result is not null) { _downBig.Text = Fmt(result.DownloadMbps).Replace(" Mbps", ""); _upBig.Text = Fmt(result.UploadMbps).Replace(" Mbps", ""); }
        if (_adapter is not null)
            Body.Children.Add(Muted($"Conectado por {_adapter.Name}" + (_adapter.LinkSpeedBitsPerSecond > 0 ? $" · link de {_adapter.LinkSpeedBitsPerSecond / 1_000_000} Mbps" : "")
                + (_adapter.Gateway is null ? "" : $" · roteador {_adapter.Gateway}") + (_adapter.Dns.Count > 0 ? $" · DNS {string.Join(", ", _adapter.Dns.Take(2))}" : ""), 12));
        Body.Children.Add(Muted("Nome do teste (opcional, para comparar depois. Ex.: antes de trocar o roteador):", 12));
        System.Windows.Automation.AutomationProperties.SetName(_name, "Nome do teste");
        if (_name.Parent is Panel p) p.Children.Remove(_name);
        Body.Children.Add(_name);
        _steps.Children.Clear();
        _stepValues.Clear();
        foreach (var (key, label) in new[] { ("router", "Roteador"), ("ping", "Ping"), ("dns", "DNS"), ("down", "Download"), ("up", "Upload"), ("load", "Sob carga") })
        {
            var value = new TextBlock { Text = "—", FontSize = 18, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
            _stepValues[key] = value;
            var cell = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            cell.Children.Add(new TextBlock { Text = label, FontSize = 11.5, Foreground = Brush("MutedBrush"), HorizontalAlignment = HorizontalAlignment.Center });
            cell.Children.Add(value);
            _steps.Children.Add(new Border { Child = cell, Background = Brush("SurfaceBrush"), CornerRadius = new CornerRadius(10), Padding = new Thickness(8), Margin = new Thickness(0, 0, 6, 0) });
        }
        Body.Children.Add(_steps);
        if (result is not null)
        {
            Set("router", Ms(result.RouterMs)); Set("ping", Ms(result.PingMs)); Set("dns", Ms(result.DnsMs));
            Set("down", Fmt(result.DownloadMbps)); Set("up", Fmt(result.UploadMbps)); Set("load", Ms(result.LoadedPingMs));
            var (grade, text) = SpeedMath.Bufferbloat(result.PingMs, result.LoadedPingMs);
            Body.Children.Add(Row($"Ping sob carga: nota {grade}", text, null, grade is "A+" or "A" or "B" ? "OkBrush" : grade == "?" ? "MutedBrush" : "WarnBrush"));
        }
        Body.Children.Add(Muted("Conexões ao mesmo tempo (mais conexões medem melhor internet acima de 300 Mbps):", 12));
        Body.Children.Add(Choice(Connections.Select(c => c == 1 ? "Automático" : c + " conexões").ToList(), Array.IndexOf(Connections, _connections), i => _connections = Connections[i]));
        Body.Children.Add(_stage);
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
                Body.Children.Add(Muted($"{h.At.ToLocalTime():dd/MM HH:mm}{(h.Name is { Length: > 0 } n ? " · " + n : "")} · ↓ {Fmt(h.DownloadMbps)} · ↑ {Fmt(h.UploadMbps)} · {Ms(h.PingMs)}{(h.LoadedPingMs is double l ? $" (sob carga {l:0} ms)" : "")} · {h.ServerCode ?? "?"}", 12));
        }
    }

    private ActiveAdapter? _adapter;

    private void Set(string key, string text) { if (_stepValues.TryGetValue(key, out var t)) t.Text = text; }

    private static string Fmt(double? mbps) => mbps is double v ? $"{v:0.0} Mbps" : "—";

    private static string Ms(double? ms) => ms is double v ? $"{v:0} ms" : "—";

    private async Task RunAsync()
    {
        _start.IsEnabled = false;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var test = new SpeedTest(Http);
        int connections = _connections == 1 ? 4 : _connections;
        _adapter = await Task.Run(NetworkStatus.Active);
        Build(null);
        GaugeRunning();
        _downBig.Text = _upBig.Text = "—";
        var live = new Progress<double>(v => _gauge.Show(v));
        try
        {
            _stage.Text = "Ping até o roteador...";
            double? router = _adapter?.Gateway is { } gw ? await NetAnalyzer.PingAsync(gw, 1000) : null;
            Set("router", Ms(router));
            _stage.Text = "Tempo de resposta do DNS...";
            double? dns = await SpeedTest.DnsTimeAsync(token);
            Set("dns", Ms(dns));
            _stage.Text = "Achando o servidor...";
            var (code, city) = await test.ServerAsync(token);
            _stage.Text = $"Latência ({code ?? "servidor"}{(city is null ? "" : ", " + city)})...";
            var (ping, jitter) = await test.LatencyAsync(12, token);
            if (ping is null) { SetStatus("Sem resposta do servidor de teste. Confira a internet.", "BadBrush"); return; }
            _gauge.CenterText = $"{ping:0}";
            _gauge.Caption = "ms de ping";
            _gauge.InvalidateVisual();
            Set("ping", Ms(ping));
            _stage.Text = $"Download com {connections} conexões (2 s de aquecimento + 8 s de medição) e ping sob carga...";
            _gauge.CenterText = null;
            _gauge.Caption = "Mbps";
            _gauge.Upload = false;
            var (down, loaded) = await test.DownloadWithLoadedPingAsync(connections, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8), live, token);
            Set("down", Fmt(down));
            Set("load", Ms(loaded));
            _downBig.Text = down is double dv ? $"{dv:0.0}" : "—";
            Motion.Flash(_downBig);
            _stage.Text = $"Upload com {connections} conexões (2 s de aquecimento + 8 s de medição)...";
            _gauge.Reset();
            _gauge.Upload = true;
            var up = await test.UploadAsync(connections, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8), live, token);
            var result = new SpeedResult(DateTimeOffset.Now, down, up, ping, jitter, code, city, connections, loaded, router, dns, _name.Text.Trim());
            _history.Add(result);
            _stage.Text = "Pronto.";
            _upBig.Text = up is double uv ? $"{uv:0.0}" : "—";
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
