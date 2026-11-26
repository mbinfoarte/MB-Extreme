using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MBExtreme.Core.NetDiag;
using MBExtreme.Core.Network;

namespace MBExtreme.UI.Pages;

public sealed partial class NetCenterPage
{
    private DnsSuiteResult? _dns;
    private IReadOnlyList<LanDevice>? _lan;
    private IReadOnlyList<IpConflict> _conflicts = Array.Empty<IpConflict>();
    private IReadOnlyList<(string Mac, IReadOnlyList<string> Ips)> _multiIp = Array.Empty<(string, IReadOnlyList<string>)>();
    private RouteDiagnosis? _route;
    private string _routeHost = "www.google.com";
    private string _wifiBand = WifiRules.B24;
    private string _repairResult = "";

    private static TextBlock Head(string title) => new() { Text = title, FontSize = 19, FontWeight = FontWeights.Bold };

    private StackPanel PageTop(string title, string sub, params UIElement[] actions)
    {
        var page = new StackPanel();
        var d = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var a = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        foreach (var x in actions) a.Children.Add(x);
        DockPanel.SetDock(a, Dock.Right);
        d.Children.Add(a);
        var t = new StackPanel();
        t.Children.Add(Head(title));
        t.Children.Add(Muted(sub, 12.5));
        d.Children.Add(t);
        page.Children.Add(d);
        return page;
    }

    /// <summary>Tabela simples: cabeçalho e linhas de texto em colunas de largura fixa (a última estica).</summary>
    private Border Table(string[] head, double[] widths, IEnumerable<(string[] Cells, Color? Color, bool Bold)> rows)
    {
        var st = new StackPanel();
        Grid Line(string[] cells, Color? color, bool header, bool bold)
        {
            var g = new Grid { Margin = new Thickness(0, header ? 0 : 1, 0, 1) };
            for (int i = 0; i < cells.Length; i++)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = i < widths.Length && widths[i] > 0 ? new GridLength(widths[i]) : new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < cells.Length; i++)
            {
                var tb = new TextBlock
                {
                    Text = cells[i], FontSize = header ? 11.5 : 12.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 8, 3),
                    FontWeight = header || bold ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = header ? Brush("MutedBrush") : color is { } c && (i == cells.Length - 1 || i == 0) ? new SolidColorBrush(c) : Brush("TextBrush"),
                    FontFamily = new FontFamily(!header && cells[i].Length > 0 && (char.IsDigit(cells[i][0]) || cells[i].StartsWith('-')) ? "Consolas" : "Segoe UI"),
                };
                Grid.SetColumn(tb, i);
                g.Children.Add(tb);
            }
            return g;
        }
        st.Children.Add(Line(head, null, true, false));
        st.Children.Add(new Border { Height = 1, Background = Brush("BorderBrush"), Margin = new Thickness(0, 2, 0, 2) });
        int n = 0;
        foreach (var (cells, color, bold) in rows) { st.Children.Add(Line(cells, color, false, bold)); n++; }
        if (n == 0) st.Children.Add(Muted("Nada por aqui ainda.", 12));
        return new Border { Child = new ScrollViewer { Content = st, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled } };
    }

    // ================================================================= DNS

    private UIElement DnsSection()
    {
        var page = PageTop("DNS e resolução", "Tempo de resposta dos servidores, cache do PC e se nomes inexistentes estão sendo desviados. Só leitura.",
            Btn(_dns is null ? "Testar DNS" : "Testar de novo", async () => await RunDnsAsync(), true),
            Btn("Limpar cache DNS", async () => await RepairAsync(RepairKind.FlushDns)),
            Btn("Trocar DNS", () => { MainWindow.Current?.Go("dns"); return Task.CompletedTask; }));
        if (_dns is not { } d) { page.Children.Add(Panel("Ainda sem teste", Muted("Clique em \"Testar DNS\". Leva uns 10 segundos.", 12.5), "")); return page; }
        double max = Math.Max(1, d.Resolvers.Where(r => r.Ok).Select(r => r.Ms ?? 0).DefaultIfEmpty(1).Max());
        var bars = new StackPanel();
        foreach (var r in d.Resolvers.OrderBy(r => r.Ok ? r.Ms ?? 9999 : 99999))
        {
            var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            g.Children.Add(new TextBlock { Text = r.Resolver, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
            bool best = r == d.Fastest;
            var track = new Border { Height = 10, CornerRadius = new CornerRadius(5), Background = Brush("Surface2Brush"), VerticalAlignment = VerticalAlignment.Center };
            var fill = new Border { Height = 10, CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(!r.Ok ? Red : best ? Green : Yellow) };
            var host = new Grid(); host.Children.Add(track); host.Children.Add(fill);
            host.SizeChanged += (_, e) => fill.Width = r.Ok ? Math.Max(6, e.NewSize.Width * (r.Ms ?? 0) / max) : e.NewSize.Width;
            Grid.SetColumn(host, 1); g.Children.Add(host);
            var ms = new TextBlock { Text = r.Ok ? $"{r.Ms:0} ms" : "falhou", FontSize = 12.5, FontFamily = new FontFamily("Consolas"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(!r.Ok ? Red : best ? Green : Colors.White) };
            Grid.SetColumn(ms, 2); g.Children.Add(ms);
            bars.Children.Add(g);
        }
        page.Children.Add(Panel("Servidores (www.google.com)", bars, "", d.Fastest is { } f ? Pill("Mais rápido: " + f.Resolver, Green) : null));
        page.Children.Add(Wrap(
            Tile("1ª consulta (sem cache)", d.First is { Ok: true } a ? $"{a.Ms:0} ms" : "falhou", "DNS do PC", d.First is { Ok: true } ? Green : Red, ""),
            Tile("2ª consulta (com cache)", d.Cached is { Ok: true } b ? $"{b.Ms:0} ms" : "falhou", "devia ser quase 0", d.Cached is { Ok: true, Ms: < 10 } ? Green : Orange, ""),
            Tile("Nome inexistente", d.Hijack == true ? "Desviado" : "Ok", d.Hijack == true ? "a operadora redireciona erros" : "erro devolvido certinho", d.Hijack == true ? Orange : Green, ""),
            Tile("Cache do PC", d.CacheEntries is { } n ? $"{n} nomes" : "—", d.CacheEntries is null ? "não deu para ler" : "guardados agora", Brush2(), "")));
        var fs = new StackPanel();
        foreach (var x in d.Findings) fs.Children.Add(FindingRow(x));
        page.Children.Add(Panel("Resultado", fs, ""));
        return page;
    }

    private async Task RunDnsAsync() => await Busy("Testando DNS...", async ct =>
    {
        var probe = new SystemNetProbe();
        var nic = NetDiagRunner.PickPrimary(probe.Interfaces())?.Dns ?? (IReadOnlyList<string>)Array.Empty<string>();
        int? cache = await Task.Run(NetCenterProbe.DnsCacheEntries, ct);
        _dns = await Task.Run(() => DnsSuite.RunAsync(probe, nic, cache, ct), ct);
    });

    // ================================================================= Rede local e conflitos

    private UIElement LanSection()
    {
        var page = PageTop("Rede local e conflitos de IP", "Aparelhos da sua rede (IP, MAC, fabricante) e IP duplicado. Varre só a rede privada da casa ou empresa, até /22.",
            Btn(_lan is null ? "Varrer a rede" : "Varrer de novo", async () => await RunLanAsync(), true));
        if (_lan is null) { page.Children.Add(Panel("Ainda sem varredura", Muted("Clique em \"Varrer a rede\". São duas passadas (uns 20 segundos) para comparar quem respondeu em cada IP.", 12.5), "")); return page; }
        var conf = new StackPanel();
        if (_conflicts.Count == 0) conf.Children.Add(new TextBlock { Text = "Nenhum conflito de IP encontrado.", FontSize = 13, Foreground = new SolidColorBrush(Green) });
        foreach (var c in _conflicts)
        {
            var st = new StackPanel();
            st.Children.Add(new TextBlock { Text = $"{c.Ip}  ·  {(c.Macs.Count > 0 ? string.Join("  e  ", c.Macs) : "este PC")}", FontSize = 13.5, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Consolas"), Foreground = new SolidColorBrush(Red) });
            st.Children.Add(Muted(c.Evidence, 12));
            st.Children.Add(new TextBlock { Text = "Pode não ser isso: " + c.Counter, FontSize = 11, FontStyle = FontStyles.Italic, Foreground = Brush("MutedBrush"), TextWrapping = TextWrapping.Wrap });
            st.Children.Add(new TextBlock { Text = "O que fazer: no roteador, confira a lista do DHCP e as reservas; tire IP fixo repetido dos aparelhos.", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
            conf.Children.Add(new Border { Child = st, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 6), CornerRadius = new CornerRadius(10), Background = Brush("Surface2Brush"),
                BorderBrush = new SolidColorBrush(Red), BorderThickness = new Thickness(4, 1, 1, 1) });
        }
        foreach (var (mac, ips) in _multiIp)
            conf.Children.Add(Muted($"Mesmo aparelho ({mac}) em {ips.Count} IPs: {string.Join(", ", ips)}. Normal em roteador; num aparelho comum pode ser IP fixo e DHCP ao mesmo tempo.", 11.5));
        page.Children.Add(Panel("Conflitos de IP", conf, "", _conflicts.Count > 0 ? Pill($"{_conflicts.Count} conflito(s)", Red) : Pill("Nenhum", Green)));

        var conflictIps = _conflicts.Select(c => c.Ip).ToHashSet();
        page.Children.Add(Panel($"Aparelhos ({_lan.Count})", Table(new[] { "IP", "MAC", "Fabricante", "Nome", "Tipo", "Ping" }, new double[] { 130, 150, 150, 0, 120, 70 },
            _lan.Select(d => (new[]
            {
                d.Ip, d.Mac, d.Vendor, d.Name.Length > 0 ? d.Name : d.IsThisPc ? "Este PC" : d.IsRouter ? "Roteador" : "—",
                d.IsRouter ? "Roteador" : d.IsThisPc ? "Este PC" : LocalNetwork.KindOf(d.Vendor, d.Name), d.PingMs is { } ms ? $"{ms:0} ms" : "—",
            }, conflictIps.Contains(d.Ip) ? Red : d.IsRouter || d.IsThisPc ? Yellow : (Color?)null, d.IsRouter || d.IsThisPc))), ""));
        return page;
    }

    private async Task RunLanAsync() => await Busy("Varrendo a rede local (2 passadas)...", async ct =>
    {
        var (devices, conflicts, multi) = await Task.Run(() => NetCenterProbe.ScanLanAsync(null, ct), ct);
        _lan = devices; _conflicts = conflicts; _multiIp = multi;
    });

    // ================================================================= Rotas

    private UIElement RouteSection()
    {
        var hostBox = new TextBox { Text = _routeHost, Width = 280, Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
        hostBox.TextChanged += (_, _) => _routeHost = hostBox.Text.Trim();
        var page = PageTop("Rastreio de rotas", "O caminho até o destino, salto a salto: onde aumenta a latência e onde há perda.");
        page.Children.Add(Panel("Destino (site, IP ou servidor)", Wrap(hostBox, Btn("Executar rastreio", async () => await RunRouteAsync(), true),
            Btn("Google", () => { _routeHost = "www.google.com"; Show("route"); return Task.CompletedTask; }),
            Btn("Cloudflare", () => { _routeHost = "1.1.1.1"; Show("route"); return Task.CompletedTask; })), ""));
        if (_route is not { } r) { page.Children.Add(Muted("Escolha o destino e clique em \"Executar rastreio\". Leva de 20 a 60 segundos.", 12.5)); return page; }

        var labels = RouteLabels.Label(r.Hops.Select(h => string.IsNullOrEmpty(h.Address) || h.Address == "*" ? null : h.Address).ToList());
        // caminho desenhado
        var path = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        void Node(string title, string sub, Color c, string glyph)
        {
            var st = new StackPanel { Width = 120, HorizontalAlignment = HorizontalAlignment.Center };
            st.Children.Add(new Border
            {
                Width = 52, Height = 52, CornerRadius = new CornerRadius(26), BorderBrush = new SolidColorBrush(c), BorderThickness = new Thickness(2.5), Background = Brush("Surface2Brush"),
                Child = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 20, Foreground = new SolidColorBrush(c), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
            st.Children.Add(new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            st.Children.Add(new TextBlock { Text = sub, FontSize = 11, FontFamily = new FontFamily("Consolas"), Foreground = Brush("MutedBrush"), HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            path.Children.Add(st);
        }
        void Link(Color c) => path.Children.Add(new Border { Width = 40, Height = 4, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(c), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 24, 0, 0) });
        Color HopColor(HopSummary h) => h.Received == 0 ? Color.FromRgb(0x55, 0x5B, 0x66) : h.LossPercent >= 5 ? Red : h.LossPercent >= 1 ? Orange : Green;
        Node("Este computador", _diag?.Evidence.Primary?.Ipv4.FirstOrDefault() ?? "", Green, "");
        var shown = r.Hops.Select((h, i) => (h, i)).Where(x => x.i == 0 || labels[x.i] == "Provedor" && labels.Take(x.i).Count(l => l == "Provedor") == 0 || x.i == r.Hops.Count - 1).ToList();
        int skipped = r.Hops.Count - shown.Count;
        for (int k = 0; k < shown.Count; k++)
        {
            var (h, i) = shown[k];
            Link(HopColor(h));
            if (k == shown.Count - 1 && skipped > 0) { Node($"{skipped} saltos", "intermediários", Brush2(), ""); Link(HopColor(h)); }
            Node(i == r.Hops.Count - 1 ? r.Host : labels[i], h.Address, HopColor(h), i == r.Hops.Count - 1 ? "" : i == 0 ? "" : "");
        }
        page.Children.Add(Panel($"Caminho da comunicação ({r.Hops.Count} saltos)", new ScrollViewer { Content = path, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }, ""));

        // tabela + resultado
        var two = new Grid();
        two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
        two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        two.ColumnDefinitions.Add(new ColumnDefinition());
        two.Children.Add(Panel("Saltos", Table(new[] { "#", "Endereço", "Média", "Variação", "Perda", "Onde" }, new double[] { 30, 150, 70, 75, 60, 0 },
            r.Hops.Select((h, i) => (new[]
            {
                h.Hop.ToString(), string.IsNullOrEmpty(h.Address) ? "sem resposta" : h.Address, h.Received > 0 ? $"{h.Average:0} ms" : "—",
                h.Received > 0 ? $"{h.Jitter:0.#} ms" : "—", h.Received > 0 ? $"{h.LossPercent:0.#}%" : "—", labels[i],
            }, (Color?)HopColor(h), i == r.Hops.Count - 1))), ""));
        var res = new StackPanel();
        bool stable = r.LossPercent < 1 && r.Findings.All(f => f.Level != FindingLevel.Bad);
        res.Children.Add(new TextBlock { Text = stable ? "Rota estável" : "Rota com problemas", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(stable ? Green : Orange) });
        res.Children.Add(Muted(r.Verdict, 12.5));
        foreach (var (k, v) in new[] { ("Latência média (fim a fim)", $"{r.Average:0} ms"), ("Perda de pacotes", $"{r.LossPercent:0.#}%"), ("Saltos", r.Hops.Count.ToString()), ("Variação", $"{r.Jitter:0.#} ms") })
        {
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var val = new TextBlock { Text = v, FontSize = 13, FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Consolas"), Foreground = new SolidColorBrush(Green) };
            DockPanel.SetDock(val, Dock.Right); row.Children.Add(val);
            row.Children.Add(new TextBlock { Text = k, FontSize = 12.5 });
            res.Children.Add(row);
        }
        foreach (var f in r.Findings)
            res.Children.Add(new TextBlock { Text = (f.Level == FindingLevel.Bad ? "✖ " : f.Level == FindingLevel.Warn ? "! " : "• ") + f.Title + ": " + f.Detail, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
                Foreground = new SolidColorBrush(f.Level == FindingLevel.Bad ? Red : f.Level == FindingLevel.Warn ? Orange : Colors.White) });
        var rp = Panel("Resultado do rastreio", res, "");
        Grid.SetColumn(rp, 2); two.Children.Add(rp);
        page.Children.Add(two);

        // gráficos
        var answered = r.Hops.Where(h => h.Received > 0).ToList();
        var charts = new Grid();
        charts.ColumnDefinitions.Add(new ColumnDefinition());
        charts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        charts.ColumnDefinitions.Add(new ColumnDefinition());
        charts.Children.Add(Panel("Latência por salto (ms)", LineChart(answered.Select(h => h.Average).ToList(), answered.Select(h => h.Average).DefaultIfEmpty(1).Max() * 1.2, Green, "ms", answered.Select(h => h.Hop.ToString()).ToList()), ""));
        var lp = Panel("Perda de pacotes por salto (%)", LineChart(answered.Select(h => h.LossPercent).ToList(), Math.Max(5, answered.Select(h => h.LossPercent).DefaultIfEmpty(0).Max() * 1.2), Orange, "%", answered.Select(h => h.Hop.ToString()).ToList()), "");
        Grid.SetColumn(lp, 2); charts.Children.Add(lp);
        page.Children.Add(charts);
        page.Children.Add(Muted("Perda só num salto do meio, com o destino respondendo 100%, é limite de resposta do equipamento, não falha.", 11.5));
        return page;
    }

    private async Task RunRouteAsync()
    {
        string host = _routeHost.Length > 0 ? _routeHost : "www.google.com";
        await Busy($"Rastreando o caminho até {host}...", async ct =>
        {
            var progress = new Progress<string>(s => { _busyText = s; if (IsLoaded) Show(_section); });
            _route = await Task.Run(() => RouteProbe.RunAsync(host, 443, 20, 3, progress, ct), ct);
        });
    }

    // ================================================================= Wi-Fi

    private UIElement WifiSection()
    {
        var page = PageTop("Análise de Wi-Fi", "Sua conexão sem fio, redes próximas, canais e congestionamento, com recomendações.",
            Btn(_wifi is null ? "Analisar Wi-Fi" : "Atualizar", async () => await Busy("Lendo as redes Wi-Fi...", async _ => await LoadWifiAsync()), true));
        if (_wifi is not { } scan || _wifiA is not { } a)
        {
            page.Children.Add(Panel("Ainda sem análise", Muted("Clique em \"Analisar Wi-Fi\". Se o PC não tem Wi-Fi, esta parte fica vazia.", 12.5), ""));
            return page;
        }
        var cur = scan.Current;
        page.Children.Add(Wrap(
            Tile("Rede conectada", cur?.Ssid ?? "nenhuma", cur is null ? "" : $"Banda {WifiRules.Band(cur.FrequencyMhz)} · canal {WifiRules.Channel(cur.FrequencyMhz)}", cur is null ? Orange : Green, ""),
            Tile("Sinal (RSSI)", cur is null ? "—" : $"{cur.Rssi} dBm", cur is null ? "" : WifiRules.Quality(cur.Rssi), cur is null ? Orange : cur.Rssi >= -60 ? Green : cur.Rssi >= -70 ? Yellow : Red, ""),
            Tile("Nota do Wi-Fi", cur is null ? "—" : $"{a.Score}/100", cur is null ? "" : NetOverviewRules.QualityText(a.Score), a.Score >= 75 ? Green : a.Score >= 50 ? Yellow : Red, ""),
            Tile("Taxa de conexão", scan.RxMbps is { } rx ? $"{rx:0} Mbps" : "—", scan.TxMbps is { } tx ? $"envio {tx:0} Mbps" : "", Blue, ""),
            Tile("Segurança", cur?.Security ?? "—", cur is null ? "" : cur.Security is "Aberta" or "WEP" ? "fraca" : "ok", cur?.Security is "Aberta" or "WEP" ? Red : Green, "")));

        if (!Has(NetCenterAccess.WifiFullPlan))
        {
            page.Children.Add(Locked("Wi-Fi completo", NetCenterAccess.WifiFullPlan,
                $"Lista das {scan.Networks.Count} redes vizinhas, gráfico de canais, melhor canal e interferências. Sua rede, o sinal e o canal já aparecem acima."));
            return page;
        }
        page.Children.Add(Panel($"Redes próximas ({scan.Networks.Count} encontradas)", Table(new[] { "SSID", "Banda", "Canal", "Sinal", "Segurança" }, new double[] { 0, 90, 70, 110, 100 },
            scan.Networks.Take(40).Select(n => (new[]
            {
                (n.Connected ? "● " : "") + (n.Ssid.Length > 0 ? n.Ssid : "(oculta)"), WifiRules.Band(n.FrequencyMhz), WifiRules.Channel(n.FrequencyMhz).ToString(), $"{n.Rssi} dBm", n.Security,
            }, (Color?)(n.Connected ? Yellow : n.Rssi >= -60 ? Green : n.Rssi >= -75 ? Colors.White : Brush2()), n.Connected))), ""));

        // canais
        var bands = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (string b in new[] { WifiRules.B24, WifiRules.B5, WifiRules.B6 })
        {
            string bb = b;
            var btn = Btn(b, () => { _wifiBand = bb; Show("wifi"); return Task.CompletedTask; }, b == _wifiBand);
            btn.IsEnabled = scan.Networks.Any(n => WifiRules.Band(n.FrequencyMhz) == b);
            bands.Children.Add(btn);
        }
        var chan = new StackPanel();
        chan.Children.Add(bands);
        chan.Children.Add(ChannelChart(scan, _wifiBand));
        if (a.BestChannel.TryGetValue(_wifiBand, out int best))
            chan.Children.Add(new TextBlock { Text = $"Canal menos ocupado em {_wifiBand}: {best}", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Green), Margin = new Thickness(0, 6, 0, 0) });
        page.Children.Add(Panel("Congestionamento de canais", chan, ""));

        var fs = new StackPanel();
        foreach (var f in a.Findings) fs.Children.Add(FindingRow(f));
        fs.Children.Add(Muted("Mapa de calor da cobertura: precisa da planta do local e de medições andando pelo ambiente; o MB não inventa esse mapa.", 11.5));
        page.Children.Add(Panel("Interferências e recomendações", fs, ""));
        return page;
    }

    /// <summary>Redes por canal: cada rede é uma "montanha" na largura que ocupa; a sua em amarelo.</summary>
    private Viewbox ChannelChart(WifiScan scan, string band)
    {
        const double W = 620, H = 190, L = 40, B = 24, T = 10;
        var nets = scan.Networks.Where(n => WifiRules.Band(n.FrequencyMhz) == band).ToList();
        var chs = band == WifiRules.B24 ? Enumerable.Range(1, 13).ToList() : nets.Select(n => WifiRules.Channel(n.FrequencyMhz)).Distinct().OrderBy(c => c).ToList();
        if (chs.Count == 0) chs.Add(1);
        int minC = chs.Min() - (band == WifiRules.B24 ? 0 : 4), maxC = chs.Max() + (band == WifiRules.B24 ? 0 : 4);
        double X(double ch) => L + (W - L - 10) * (ch - minC) / Math.Max(1, maxC - minC);
        double Y(int dbm) => H - B - (H - B - T) * Math.Clamp(dbm + 100, 0, 70) / 70.0;
        var c = new Canvas { Width = W, Height = H };
        foreach (int dbm in new[] { -30, -50, -70, -90 })
        {
            double y = Y(dbm);
            c.Children.Add(new Line { X1 = L, X2 = W, Y1 = y, Y2 = y, Stroke = new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255)) });
            var tb = new TextBlock { Text = dbm.ToString(), FontSize = 10, Foreground = Brush("MutedBrush") };
            Canvas.SetLeft(tb, 4); Canvas.SetTop(tb, y - 7); c.Children.Add(tb);
        }
        foreach (int ch in chs)
        {
            var tb = new TextBlock { Text = ch.ToString(), FontSize = 10, Foreground = Brush("MutedBrush") };
            Canvas.SetLeft(tb, X(ch) - 5); Canvas.SetTop(tb, H - B + 5); c.Children.Add(tb);
        }
        Color[] palette = { Blue, Color.FromRgb(0xB0, 0x7B, 0xFF), Green, Orange, Color.FromRgb(0xFF, 0x7A, 0xC8), Color.FromRgb(0x7D, 0xE3, 0xF0) };
        int k = 0;
        foreach (var n in nets.OrderBy(n => n.Connected))
        {
            double ch = WifiRules.Channel(n.FrequencyMhz), half = band == WifiRules.B24 ? 2 : 2;
            Color col = n.Connected ? Yellow : palette[k++ % palette.Length];
            var fig = new PathFigure { StartPoint = new Point(X(ch - half), H - B) };
            fig.Segments.Add(new QuadraticBezierSegment(new Point(X(ch), Y(n.Rssi) - (H - B - Y(n.Rssi)) * 0.35), new Point(X(ch + half), H - B), true));
            c.Children.Add(new Path
            {
                Data = new PathGeometry(new[] { fig }), Stroke = new SolidColorBrush(col), StrokeThickness = n.Connected ? 3 : 1.6,
                Fill = new SolidColorBrush(Color.FromArgb(n.Connected ? (byte)0x44 : (byte)0x22, col.R, col.G, col.B)),
            });
            if (n.Connected)
            {
                var me = new TextBlock { Text = n.Ssid + " (sua)", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Yellow) };
                Canvas.SetLeft(me, X(ch) + 4); Canvas.SetTop(me, Math.Max(0, Y(n.Rssi) - 22)); c.Children.Add(me);
            }
        }
        return new Viewbox { Child = c, Stretch = Stretch.Uniform, MaxHeight = 260, HorizontalAlignment = HorizontalAlignment.Left };
    }

    private async Task LoadWifiAsync()
    {
        _wifi = await Task.Run(NetCenterProbe.ScanWifi);
        _wifiA = _wifi is null ? null : WifiRules.Analyze(_wifi);
        if (_wifi is null) SetStatus("Não foi possível ler o Wi-Fi (o PC não tem placa Wi-Fi ou o serviço de Wi-Fi está desligado).", "WarnBrush");
    }

    // ================================================================= Reparação

    private UIElement RepairSection()
    {
        var page = PageTop("Reparação de rede", "Ações seguras para problemas de conexão, DNS e DHCP. Uma por vez, com confirmação, foto de antes e registro no diário.");
        if (_repairResult.Length > 0)
            page.Children.Add(new Border
            {
                Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 12), CornerRadius = new CornerRadius(10), BorderBrush = new SolidColorBrush(Yellow), BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = _repairResult, FontSize = 13, TextWrapping = TextWrapping.Wrap },
            });
        foreach (var a in NetRepairCatalog.All)
        {
            var st = new StackPanel();
            var top = new DockPanel();
            bool can = Has(NetCenterAccess.RepairPlan(a.Kind));
            var run = can ? Btn("Executar", async () => await RepairAsync(a.Kind), true)
                : Btn("Plano Técnico", () => { MainWindow.Current?.Go("license"); return Task.CompletedTask; });
            DockPanel.SetDock(run, Dock.Right); top.Children.Add(run);
            var tt = new StackPanel();
            tt.Children.Add(new TextBlock { Text = a.Title, FontSize = 15, FontWeight = FontWeights.SemiBold });
            tt.Children.Add(Muted(a.What, 12.5));
            top.Children.Add(tt);
            st.Children.Add(top);
            Color ic = a.Impact switch { "Baixo" => Green, "Médio" => Orange, _ => Red };
            var info = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            info.Children.Add(Muted("Autorização: Administrador", 12));
            info.Children.Add(Pill("Impacto: " + a.Impact, ic));
            info.Children.Add(new TextBlock { Text = "  " + a.ImpactText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            st.Children.Add(info);
            var card = CardOf(st);
            card.Margin = new Thickness(0, 0, 0, 10);
            page.Children.Add(card);
        }
        page.Children.Add(Panel("Recomendação de uso", Muted("Rode uma correção por vez e, depois de cada uma, o MB testa a conexão de novo. Se nada mudar, passe para a próxima.", 12.5), ""));
        return page;
    }

    private async Task RepairAsync(RepairKind kind)
    {
        if (!Has(NetCenterAccess.RepairPlan(kind))) { Show("repair"); return; }
        var a = NetRepairCatalog.Get(kind);
        if (MessageBox.Show($"{a.Title}?\n\n{a.What}\n\nImpacto {a.Impact.ToLowerInvariant()}: {a.ImpactText}\n\nAntes, o MB guarda uma foto da configuração de rede.",
                "Reparação de rede", MessageBoxButton.YesNo, a.Impact == "Alto" ? MessageBoxImage.Warning : MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await Busy(a.Title + "...", async ct =>
        {
            var (ok, msg, snap) = await Task.Run(() => NetCenterProbe.Repair(kind, _diag?.Evidence), ct);
            string after = "";
            if (ok && !a.NeedsRestart)
            {
                var check = await Task.Run(() => new NetDiagRunner(new SystemNetProbe()).RunAsync(new NetDiagOptions(Pings: 5, Trace: false), null, ct), ct);
                var o = NetOverviewRules.From(check);
                after = $" Teste depois: {o.Headline.ToLowerInvariant()} ({o.Quality}%).";
                _diag = check; UpdateLast();
            }
            _repairResult = (ok ? "✔ " : "✖ ") + msg + after + (snap is null ? "" : " Foto de antes guardada.");
            SetStatus(_repairResult, ok ? "OkBrush" : "BadBrush");
        });
    }

    // ================================================================= Relatórios e histórico

    private UIElement ReportsSection()
    {
        var page = PageTop("Relatórios e histórico", "Cada diagnóstico fica guardado. Gere o relatório para o cliente (IPs e MACs escondidos) ou o completo.");
        if (_diag is { } r)
            page.Children.Add(Panel("Diagnóstico atual", Wrap(
                Btn("Relatório para o cliente", () => { OpenHtml(NetDiagReportWriter.ToHtml(r, masked: true), $"rede-{r.StartedUtc.ToLocalTime():yyyy-MM-dd-HHmm}-cliente"); return Task.CompletedTask; }, true),
                Btn("Relatório completo", () => { OpenHtml(NetDiagReportWriter.ToHtml(r, masked: false), $"rede-{r.StartedUtc.ToLocalTime():yyyy-MM-dd-HHmm}"); return Task.CompletedTask; })), ""));
        var hist = NetHistory.List(30);
        if (hist.Count == 0) { page.Children.Add(Panel("Histórico", Muted("Ainda vazio. Cada \"Executar diagnóstico completo\" entra aqui.", 12.5), "")); return page; }
        var ordered = hist.OrderBy(h => h.Report.StartedUtc).ToList();
        var quality = ordered.Select(h => (double)NetOverviewRules.From(h.Report).Quality).ToList();
        page.Children.Add(Panel("Qualidade ao longo do tempo (%)", LineChart(quality, 100, Yellow, "%", ordered.Select(h => h.Report.StartedUtc.ToLocalTime().ToString("dd/MM")).ToList()), ""));
        var list = new StackPanel();
        foreach (var (file, rep) in hist)
        {
            var o = NetOverviewRules.From(rep);
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var open = Btn("Abrir", () => { OpenHtml(NetDiagReportWriter.ToHtml(rep, masked: false), System.IO.Path.GetFileNameWithoutExtension(file)); return Task.CompletedTask; });
            DockPanel.SetDock(open, Dock.Right); row.Children.Add(open);
            var t = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
            t.Inlines.Add(new System.Windows.Documents.Run($"{rep.StartedUtc.ToLocalTime():dd/MM/yyyy HH:mm}   ") { FontFamily = new FontFamily("Consolas") });
            t.Inlines.Add(new System.Windows.Documents.Run($"{o.Headline} · {o.QualityText} ({o.Quality}%)") { Foreground = new SolidColorBrush(SevColor(o.Level)), FontWeight = FontWeights.SemiBold });
            row.Children.Add(t);
            list.Children.Add(row);
        }
        page.Children.Add(Panel($"Histórico ({hist.Count})", list, ""));
        return page;
    }
}
