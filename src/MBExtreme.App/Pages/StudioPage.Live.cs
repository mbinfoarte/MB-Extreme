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

    // ---- indicador de gravação/ao vivo, só dentro do programa (nunca entra no vídeo)
    private readonly Border _recBadge = new()
    {
        Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(6), Padding = new Thickness(8, 3, 10, 3), CornerRadius = new CornerRadius(12), IsHitTestVisible = false,
        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 0, 0, 0)),
    };
    private readonly System.Windows.Shapes.Ellipse _recDot = new() { Width = 10, Height = 10, Fill = System.Windows.Media.Brushes.Red, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _recText = new() { FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White, VerticalAlignment = VerticalAlignment.Center };
    private bool _recBuilt;

    private void BuildRecBadge()
    {
        if (_recBuilt) return;
        _recBuilt = true;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_recDot);
        row.Children.Add(_recText);
        _recBadge.Child = row;
        _previewHost.Children.Add(_recBadge);
        var blink = new System.Windows.Media.Animation.DoubleAnimation(1, 0.15, TimeSpan.FromMilliseconds(600))
        {
            AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
        };
        _recDot.BeginAnimation(UIElement.OpacityProperty, blink);
    }

    /// <summary>Bolinha piscando com REC e o tempo (ou AO VIVO) em cima da pré-visualização. Fica só aqui: não vai para o vídeo.</summary>
    private void RefreshRecBadge()
    {
        BuildRecBadge();
        bool running = _studio.IsRunning;
        _recBadge.Visibility = Vis(running);
        if (!running) return;
        var elapsed = DateTime.Now - _studio.Session.StartedAt;
        var request = _studio.Session.Request;
        string kind = request?.Stream is not null ? (request.RecordFile is not null ? "AO VIVO + REC" : "AO VIVO") : request?.RecordFile is not null ? "REC" : "REPLAY";
        _recText.Text = $"{kind}  {(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    private UIElement BuildOptimizeCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Otimizar a live", FontSize = 14, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(Muted("Se a live trava, o mais comum é a taxa de vídeo ser maior do que a internet aguenta com folga. O assistente mede o envio e a conexão até a plataforma (cerca de 20 segundos) e escolhe a melhor qualidade que não trava.", 11.5));
        var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var run = SmallButton("Medir e otimizar a live", async (_, _) => await OptimizeLiveAsync(), true);
        run.Margin = new Thickness(0, 0, 8, 4);
        row.Children.Add(run);
        var light = SmallButton("Modo leve para live (melhor codificador)", async (_, _) => await ApplyLightModeAsync());
        light.Margin = new Thickness(0, 0, 8, 4);
        light.ToolTip = "Escolhe o melhor codificador da sua placa de vídeo e uma qualidade que pesa pouco no jogo.";
        row.Children.Add(light);
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

    /// <summary>Um clique: detecta a placa, escolhe o melhor codificador e deixa a live leve (esforço rápido, resolução e quadros que o PC aguenta).</summary>
    private async Task ApplyLightModeAsync()
    {
        if (_studio.IsRunning) { _optText.Text = "Pare a transmissão para trocar o codificador; depois use o modo leve e inicie de novo."; return; }
        if (_encoders.Count == 0)
        {
            _optText.Text = "Detectando os codificadores da placa de vídeo...";
            await DetectAsync();
        }
        if (_encoders.Count == 0) { _optText.Text = "Nenhum codificador de placa de vídeo funcionou. Atualize o driver da placa (NVIDIA, AMD ou Intel) e tente de novo."; return; }

        int currentHeight = Resolutions[Math.Clamp(_s.StudioResolution, 0, Resolutions.Length - 1)].Height;
        var plan = LiveLight.Plan(_encoders, _tier, CurrentService(), currentHeight, _s.StudioFps);
        _encoderIndex = plan.EncoderIndex;
        _s.StudioEncoderName = _encoders[Math.Clamp(plan.EncoderIndex, 0, _encoders.Count - 1)].FfmpegName;
        _s.StudioEncoderQuality = plan.QualityIndex;
        int index = Array.FindIndex(Resolutions, r => r.Height == plan.Height);
        if (index >= 0) _s.StudioResolution = index;
        _s.StudioFps = plan.Fps;
        _s.StudioBitrateMbps = 0;
        var caps = CapOptions.Where(c => c > 0 && c <= plan.CapMbps).ToList();
        _s.StudioStreamCapMbps = caps.Count > 0 ? caps.Max() : CapOptions.First(c => c > 0);
        BuildVideo();
        BuildStream();
        UpdatePlan();
        var heavy = new List<string>();
        if (_s.StudioVerticalOn || _s.StudioVerticalCopy) heavy.Add("vídeo vertical");
        if (_s.StudioCaptionOn) heavy.Add("legendas ao vivo");
        if (!CurrentFilter().IsNeutral) heavy.Add("filtros de imagem");
        string extra = heavy.Count > 0 ? "\nAtenção: estão ligados " + string.Join(", ", heavy) + ", que usam o processador. Se a live pesar, desligue." : "";
        _optText.Text = "✓ Modo leve aplicado.\n• " + string.Join("\n• ", plan.Notes) + extra;
        SetStatus("Modo leve aplicado.", "OkBrush");
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
