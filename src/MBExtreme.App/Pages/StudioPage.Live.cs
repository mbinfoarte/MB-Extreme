using System.Net.Http;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Network;
using MBExtreme.Core.Recording;

namespace MBExtreme.UI.Pages;

/// <summary>Assistente "Otimizar a live": mede a internet até a plataforma e escolhe resolução, quadros e taxa que não travam.</summary>
public sealed partial class StudioPage
{
    private readonly TextBlock _optText = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 6, 0, 0) };
    private Button? _optApply;
    private LiveAdvice? _advice;
    private bool _optBusy;

    private UIElement BuildOptimizeCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Otimizar a live", FontSize = 14, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(Muted("Se a live trava, o mais comum é a taxa de vídeo ser maior do que a internet aguenta com folga. O assistente mede o envio e a conexão até a plataforma (cerca de 20 segundos) e escolhe a melhor qualidade que não trava.", 11.5));
        var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var run = SmallButton("Medir e otimizar a live", async (_, _) => await OptimizeLiveAsync(), true);
        run.Margin = new Thickness(0, 0, 8, 4);
        row.Children.Add(run);
        _optApply = SmallButton("Aplicar a recomendação", (_, _) => ApplyAdvice());
        _optApply.Margin = new Thickness(0, 0, 8, 4);
        _optApply.Visibility = Vis(_advice is not null);
        row.Children.Add(_optApply);
        panel.Children.Add(row);
        _optText.Foreground = Brush("TextBrush");
        (_optText.Parent as Panel)?.Children.Remove(_optText);
        panel.Children.Add(_optText);
        var card = CardOf(panel);
        card.Padding = new Thickness(12, 10, 12, 8);
        return card;
    }

    private static string IngestHost(StreamService service, string customUrl) => service switch
    {
        StreamService.Twitch => "live.twitch.tv",
        StreamService.YouTube => "a.rtmp.youtube.com",
        StreamService.Facebook => "live-api-s.facebook.com",
        _ => Uri.TryCreate(customUrl, UriKind.Absolute, out var u) ? u.Host : "",
    };

    private static bool OnWifiOnly()
    {
        try
        {
            bool wifi = false, wired = false;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) wifi = true;
                else if (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet && !nic.Description.Contains("virtual", StringComparison.OrdinalIgnoreCase)) wired = true;
            }
            return wifi && !wired;
        }
        catch (NetworkInformationException) { return false; }
    }

    private async Task OptimizeLiveAsync()
    {
        if (_optBusy) return;
        if (_studio.IsRunning) { _optText.Text = "Pare a transmissão antes de medir: a medição usa toda a internet por alguns segundos e atrapalharia a live."; return; }
        _optBusy = true;
        var service = CurrentService();
        string host = IngestHost(service, _customUrl.Text.Trim());
        try
        {
            _optText.Text = "1/2 Medindo o envio da internet (não feche)...";
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            double? upload = await UploadTest.RunAsync(http, 7, CancellationToken.None);

            double ping = 0, jitter = 0, loss = 0;
            bool responded = false;
            if (host.Length > 0)
            {
                _optText.Text = $"2/2 Medindo a conexão até {host}...";
                var result = await RouteProbe.RunAsync(host, 443, 15, 1, null, CancellationToken.None);
                responded = result.Average > 0 && result.Verdict != "Sem resposta";
                ping = result.Average; jitter = result.Jitter; loss = result.LossPercent;
            }

            _advice = LiveOptimizer.Recommend(new LiveProbe(upload, ping, jitter, loss, responded, OnWifiOnly(), _tier), service, _s.StudioAudioKbps);
            var text = new System.Text.StringBuilder();
            text.AppendLine(_advice.Level switch { AdviceLevel.Good => "✓ Dá para transmitir bem.", AdviceLevel.Warning => "▲ Dá para transmitir, com cuidados.", _ => "✖ A internet vai limitar a qualidade." });
            foreach (string r in _advice.Reasons) text.AppendLine("• " + r);
            if (_advice.Tips.Count > 0) { text.AppendLine(); text.AppendLine("Dicas:"); foreach (string t in _advice.Tips) text.AppendLine("• " + t); }
            _optText.Text = text.ToString().TrimEnd();
            if (_optApply is not null) _optApply.Visibility = Visibility.Visible;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _optText.Text = "Não foi possível medir agora: " + ex.Message;
        }
        finally
        {
            _optBusy = false;
        }
    }

    private void ApplyAdvice()
    {
        if (_advice is not { } a) return;
        int index = Array.FindIndex(Resolutions, r => r.Height == a.Height);
        if (index >= 0) _s.StudioResolution = index;
        _s.StudioFps = a.Fps;
        _s.StudioBitrateMbps = 0;                       // automático, dentro do limite abaixo
        var caps = CapOptions.Where(c => c > 0 && c <= a.CapMbps).ToList();
        _s.StudioStreamCapMbps = caps.Count > 0 ? caps.Max() : CapOptions.First(c => c > 0);
        BuildVideo();
        BuildStream();
        UpdatePlan();
        SetStatus($"Aplicado: {a.Height}p a {a.Fps} fps, taxa até {_s.StudioStreamCapMbps} Mbps. Se a live estiver no ar, use \"Aplicar agora\" para reiniciar a captura.", "OkBrush");
    }
}
