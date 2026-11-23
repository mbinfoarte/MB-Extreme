using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MBExtreme.Core.NetDiag;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Aba "Diagnóstico" da Rede: um botão que testa em ordem o PC, o roteador, a internet, o DNS, a página de teste e o caminho, e
/// explica o resultado em linguagem simples. Os detalhes técnicos ficam guardados atrás de um botão. Só lê: nada é alterado.
/// </summary>
public sealed partial class InternetTestPage
{
    private NetDiagReport? _diag;
    private bool _diagRunning, _diagDetails;
    private string _diagStep = "";
    private CancellationTokenSource? _diagCts;

    private UIElement DiagTab()
    {
        var panel = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(Title("Diagnóstico da rede",
            "Testa em ordem o PC, o roteador, a internet, o DNS e o caminho, e diz onde está o problema. Só lê: nada é alterado na sua rede."));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        if (_diagRunning) actions.Children.Add(Small("Parar", false, () => { _diagCts?.Cancel(); return Task.CompletedTask; }));
        else actions.Children.Add(Small(_diag is null ? "Diagnosticar" : "Diagnosticar de novo", true, DiagRunAsync));
        Grid.SetColumn(actions, 1);
        head.Children.Add(actions);
        panel.Children.Add(head);

        if (_diagRunning)
        {
            panel.Children.Add(new TextBlock { Text = _diagStep, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brush("AccentBrush"), Margin = new Thickness(0, 8, 0, 4) });
            panel.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 6, Foreground = Brush("AccentBrush"), Background = Brush("Surface2Brush"), BorderThickness = new Thickness(0) });
            panel.Children.Add(Muted("Leva uns 30 a 60 segundos.", 11.5));
            return CardOf(panel);
        }
        if (_diag is not { } r)
        {
            panel.Children.Add(Muted("Use quando a internet estiver lenta, caindo ou sem abrir sites. Não precisa de administrador.", 12));
            return CardOf(panel);
        }

        panel.Children.Add(Muted($"Feito às {r.FinishedUtc.ToLocalTime():HH:mm} · placa: {r.Evidence.Primary?.Name ?? "nenhuma"}", 11.5));
        foreach (var f in r.Findings) panel.Children.Add(FindingCard(f));

        var tools = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        tools.Children.Add(Small(_diagDetails ? "Esconder detalhes técnicos" : "Ver detalhes técnicos", false, () => { _diagDetails = !_diagDetails; SelectTab("diag"); return Task.CompletedTask; }));
        tools.Children.Add(Small("Relatório para o cliente", false, () => { SaveDiag(masked: true); return Task.CompletedTask; }));
        tools.Children.Add(Small("Relatório completo", false, () => { SaveDiag(masked: false); return Task.CompletedTask; }));
        panel.Children.Add(tools);
        if (_diagDetails) panel.Children.Add(DiagDetails(r));
        return CardOf(panel);
    }

    private Border FindingCard(Finding f)
    {
        var color = f.Severity switch { FindingSeverity.Problema => Color.FromRgb(0xFF, 0x7B, 0x7B), FindingSeverity.Atencao => Color.FromRgb(0xFF, 0xC4, 0x00), _ => Color.FromRgb(0x3D, 0xDC, 0x97) };
        string status = f.Status switch { FindingStatus.Confirmado => "Confirmado", FindingStatus.Provavel => "Provável", FindingStatus.Indeterminado => "Não deu para saber", _ => "Não testado" };
        var st = new StackPanel();
        var top = new WrapPanel();
        top.Children.Add(new TextBlock { Text = f.Title, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 0), TextWrapping = TextWrapping.Wrap });
        top.Children.Add(Pill(status, "#1C1C22", "#D4D4D8"));
        st.Children.Add(top);
        foreach (string ev in f.Evidence.Take(3)) st.Children.Add(new TextBlock { Text = "• " + ev, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Brush("MutedBrush"), Margin = new Thickness(0, 2, 0, 0) });
        if (f.Counter.Count > 0) st.Children.Add(new TextBlock { Text = "Pode não ser isso: " + string.Join(" ", f.Counter), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Foreground = Brush("MutedBrush"), FontStyle = FontStyles.Italic, Margin = new Thickness(0, 3, 0, 0) });
        st.Children.Add(new TextBlock { Text = "O que fazer: " + f.Next, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        return new Border
        {
            Child = st, Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 8, 0, 0), CornerRadius = new CornerRadius(10),
            Background = Brush("Surface2Brush"), BorderBrush = new SolidColorBrush(color), BorderThickness = new Thickness(4, 1, 1, 1),
        };
    }

    private StackPanel DiagDetails(NetDiagReport r)
    {
        var e = r.Evidence;
        var st = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        void Line(string label, string value)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var l = new TextBlock { Text = label, Width = 210, FontSize = 12, Foreground = Brush("MutedBrush") };
            DockPanel.SetDock(l, Dock.Left);
            row.Children.Add(l);
            row.Children.Add(new TextBlock { Text = value, FontSize = 12, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas") });
            st.Children.Add(row);
        }
        if (e.Primary is { } p)
        {
            Line("Placa", $"{p.Name} ({p.Kind}) · {p.Description}");
            Line("IP / gateway", $"{string.Join(", ", p.Ipv4.Select((ip, i) => $"{ip}/{(i < p.Ipv4Prefix.Count ? p.Ipv4Prefix[i] : 0)}"))} · {string.Join(", ", p.Gateways)}");
            Line("DNS da placa", string.Join(", ", p.Dns) + (p.Dhcp ? " · DHCP" + (p.DhcpServer is { } d ? " " + d : "") : " · IP fixo"));
            Line("Velocidade / MTU / MAC", $"{(p.SpeedBps > 0 ? p.SpeedBps / 1_000_000 + " Mbps" : "—")} · {p.Mtu} · {p.Mac}");
        }
        if (e.Loopback is { } lo) Line("Pilha do PC (127.0.0.1)", lo.Text);
        if (e.Gateway is { } gw) Line($"Roteador ({e.GatewayAddress})", gw.Text);
        foreach (var (t, s) in e.External) Line($"Internet ({t})", s.Text);
        foreach (var d in e.DnsSystem.Concat(e.DnsAlternate)) Line($"DNS {d.Resolver} · {d.Name}", d.Ok ? $"ok em {d.Ms:0} ms" : d.Detail);
        if (e.Http is { } h) Line("Página de teste", h.Ok ? "abriu" : h.Detail);
        foreach (var hop in e.Hops) Line($"Caminho · salto {hop.Ttl}", $"{hop.Address ?? "sem resposta"} · {hop.Rtt.Text}");
        foreach (string n in r.NotTested) Line("Não testado", n);
        st.Children.Add(Muted("Variação = média da diferença entre respostas seguidas. Perda em salto do meio sem perda no destino é limite de resposta do equipamento, não falha.", 11));
        return st;
    }

    private async Task DiagRunAsync()
    {
        if (_diagRunning) return;
        _diagRunning = true;
        _diagStep = "Começando...";
        _diagCts = new CancellationTokenSource();
        SelectTab("diag");
        var progress = new Progress<string>(s => { _diagStep = s; if (_tab == "diag") SelectTab("diag"); });
        try
        {
            _diag = await Task.Run(() => new NetDiagRunner(new SystemNetProbe()).RunAsync(new NetDiagOptions(), progress, _diagCts.Token));
        }
        catch (OperationCanceledException) { Say("Diagnóstico parado."); }
        finally
        {
            _diagRunning = false;
            if (_tab == "diag") SelectTab("diag");
        }
    }

    /// <summary>Salva o relatório em Documentos\MB Extreme\Diagnósticos (HTML e JSON) e abre o HTML.</summary>
    private void SaveDiag(bool masked)
    {
        if (_diag is not { } r) return;
        try
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MB Extreme", "Diagnósticos");
            Directory.CreateDirectory(folder);
            string stem = Path.Combine(folder, $"rede-{r.StartedUtc.ToLocalTime():yyyy-MM-dd-HHmm}{(masked ? "-cliente" : "")}");
            File.WriteAllText(stem + ".html", NetDiagReportWriter.ToHtml(r, masked));
            if (!masked) File.WriteAllText(stem + ".json", NetDiagReportWriter.ToJson(r));
            Process.Start(new ProcessStartInfo(stem + ".html") { UseShellExecute = true });
            Say("Relatório salvo em Documentos › MB Extreme › Diagnósticos.", "OkBrush");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Say("Não foi possível salvar o relatório: " + ex.Message, "BadBrush");
        }
    }
}
