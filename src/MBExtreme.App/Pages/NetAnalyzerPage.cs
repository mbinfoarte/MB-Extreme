using System.Text;
using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Network;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>Analisador de rede: ping, TCPing e MTR (caminho salto a salto).</summary>
public sealed class NetAnalyzerPage : ToolPageBase
{
    private readonly TextBox _host = new() { Text = "1.1.1.1", Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _port = new() { Text = "443", Width = 80, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _output = new() { FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 13 };
    private readonly AreaChart _chart = new() { Height = 90, Maximum = 200 };
    private readonly Button _start;
    private readonly Button _stop;
    private int _mode;

    public NetAnalyzerPage() : base("Analisador de rede",
        "Ping mostra a latência até um destino; TCPing mede o tempo para abrir uma porta (útil para servidores de jogos); MTR mostra cada salto do caminho com perda e latência, para achar onde a conexão piora.")
    {
        _start = AddButton("Iniciar", async (_, _) => await RunAsync());
        _stop = AddButton("Parar", (_, _) => Cts?.Cancel(), false);
        _stop.IsEnabled = false;

        Body.Children.Add(Heading("Destino (site, endereço ou IP)"));
        Body.Children.Add(Muted("Pode colar o endereço do navegador, como https://www.site.com/pagina; o programa usa só o nome do site."));
        Body.Children.Add(_host);
        Body.Children.Add(Heading("Modo"));
        Body.Children.Add(Choice(new[] { "Ping", "TCPing", "MTR", "Site completo" }, _mode, i => _mode = i));
        Body.Children.Add(Heading("Porta (só TCPing)"));
        Body.Children.Add(_port);
        Body.Children.Add(Heading("Resultado"));
        Body.Children.Add(_chart);
        Body.Children.Add(CardOf(_output));
    }

    private async Task RunAsync()
    {
        var target = NetTarget.Parse(_host.Text);
        if (target is null)
        {
            SetStatus("Digite um site, endereço ou IP válido.", "WarnBrush");
            return;
        }
        string host = target.Host;
        if (target.Port is { } typedPort && _mode == 1) _port.Text = typedPort.ToString();

        if (_mode == 1 && (!int.TryParse(_port.Text, out int port) || port is < 1 or > 65535))
        {
            SetStatus("Porta inválida (1 a 65535).", "WarnBrush");
            return;
        }

        _start.IsEnabled = false;
        _stop.IsEnabled = true;
        Cts = new CancellationTokenSource();
        var token = Cts.Token;
        SetStatus("Medindo... clique em Parar para encerrar.");
        _output.Text = "";
        _chart.Values = null;

        try
        {
            if (_mode == 3) await RunSiteAsync(host, target.Port ?? 443, token);
            else if (_mode == 2) await RunTraceAsync(host, token);
            else await RunPingAsync(host, token);
        }
        catch (OperationCanceledException)
        {
            // parado pelo usuário
        }
        finally
        {
            _start.IsEnabled = true;
            _stop.IsEnabled = false;
            SetStatus("Parado.");
        }
    }

    private async Task RunPingAsync(string host, CancellationToken token)
    {
        var stats = new HopStats { Hop = 1 };
        var recent = new List<double>();
        var lines = new List<string>();
        int.TryParse(_port.Text, out int port);

        while (!token.IsCancellationRequested)
        {
            double? ms = _mode == 1
                ? await NetAnalyzer.TcpPingAsync(host, port, 2000, token)
                : await NetAnalyzer.PingAsync(host);
            stats.Add(ms);

            recent.Add(ms ?? 0);
            if (recent.Count > 60) recent.RemoveAt(0);
            lines.Add(ms is { } v ? $"{DateTime.Now:HH:mm:ss}  {v,6:0.0} ms" : $"{DateTime.Now:HH:mm:ss}  tempo esgotado");
            if (lines.Count > 12) lines.RemoveAt(0);

            _chart.Maximum = Math.Max(50, recent.Max() * 1.2);
            _chart.Values = recent.ToList();
            _output.Text = string.Join("\n", lines) +
                $"\n\nEnviados {stats.Sent} · recebidos {stats.Received} · perda {stats.LossPercent:0.#}%" +
                (stats.Received > 0 ? $"\nmín {stats.BestOrZero:0.0} · média {stats.Average:0.0} · máx {stats.Worst:0.0} ms · jitter {stats.Jitter:0.0} ms" : "");

            await Task.Delay(1000, token);
        }
    }

    /// <summary>Diagnóstico completo do site: endereços, ping (ou TCP), caminho salto a salto e o que isso significa.</summary>
    private async Task RunSiteAsync(string host, int port, CancellationToken token)
    {
        var text = new StringBuilder();
        _output.Text = "Procurando os endereços do site...";
        var addresses = await NetTarget.ResolveAsync(host, token);
        if (addresses.Count == 0)
        {
            _output.Text = $"Não consegui achar o endereço de \"{host}\". Confira o nome ou teste o DNS.";
            return;
        }

        text.AppendLine($"Site: {host}");
        text.AppendLine("Endereços: " + string.Join(", ", addresses.Take(6).Select(a => a.ToString())) + (addresses.Count > 6 ? $" (+{addresses.Count - 6})" : ""));
        text.AppendLine($"Mede o caminho até: {addresses[0]}   ·   porta TCP: {port}");
        _output.Text = text + "\nMedindo...";

        var progress = new Progress<string>(m => _output.Text = text + "\n" + m);
        var result = await RouteProbe.RunAsync(addresses[0].ToString(), port, 15, 6, progress, token);

        text.AppendLine();
        text.AppendLine($"Resultado: {result.Verdict}");
        text.AppendLine($"Ping médio {result.Average:0.0} ms (mín {result.Min:0.0} · máx {result.Max:0.0}) · jitter {result.Jitter:0.0} ms · perda {result.LossPercent:0.#}%");
        text.AppendLine();
        foreach (var f in result.Findings)
            text.AppendLine($"{(f.Level == FindingLevel.Bad ? "✖" : f.Level == FindingLevel.Warn ? "▲" : "•")} {f.Title}: {f.Detail}");
        text.AppendLine();
        text.AppendLine($"{"#",-3} {"Endereço",-32} {"Perda",6} {"Média",7} {"Jitter",7}");
        foreach (var h in result.Hops)
            text.AppendLine($"{h.Hop,-3} {h.Address,-32} {h.LossPercent,5:0}% {(h.Received > 0 ? h.Average.ToString("0.0") : "-"),7} {(h.Received > 0 ? h.Jitter.ToString("0.0") : "-"),7}");
        _output.Text = text.ToString();
        _chart.Values = null;
    }

    private async Task RunTraceAsync(string host, CancellationToken token)
    {
        await NetAnalyzer.TraceAsync(host, 30, hops =>
        {
            var text = new StringBuilder();
            text.AppendLine($"{"#",-3} {"Endereço",-32} {"Perda",6} {"Env",4} {"Últ",7} {"Méd",7} {"Mín",7} {"Máx",7}");
            foreach (var h in hops)
            {
                text.AppendLine($"{h.Hop,-3} {h.Address,-32} {h.LossPercent,5:0}% {h.Sent,4} " +
                                $"{Format(h.Last),7} {(h.Received > 0 ? h.Average.ToString("0.0") : "-"),7} " +
                                $"{(h.Received > 0 ? h.BestOrZero.ToString("0.0") : "-"),7} {(h.Received > 0 ? h.Worst.ToString("0.0") : "-"),7}");
            }

            _output.Text = text.ToString();
        }, token);
    }

    private static string Format(double? ms) => ms is { } v ? v.ToString("0.0") : "-";
}
