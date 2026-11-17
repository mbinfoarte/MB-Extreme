using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Dns;
using MBExtreme.Core.Network;

namespace MBExtreme.UI.Pages;

/// <summary>Abas DNS (testar e trocar aqui mesmo), Roteador e rota, e Histórico.</summary>
public sealed partial class InternetTestPage
{
    // ================================================================ DNS

    private readonly StackPanel _dnsRows = new();
    private IReadOnlyList<DnsResult>? _dnsResults;
    private bool _dnsRunning;

    private string? CurrentDns() => _adapter?.Dns.FirstOrDefault(d => System.Net.IPAddress.TryParse(d, out var a) && a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);

    /// <summary>Nome do DNS pelo endereço (Cloudflare, Google...), ou "Provedor" para o do roteador.</summary>
    private static string? DnsName(string? address)
    {
        if (address is null) return null;
        var known = DnsProviders.Defaults.FirstOrDefault(p => p.Primary == address || p.Secondary == address);
        return known?.Name ?? "Provedor";
    }

    private UIElement DnsTab()
    {
        var panel = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(Title("DNS mais rápido agora",
            "O DNS transforma nomes (google.com) em endereços; um DNS lento atrasa o começo de cada site e jogo. Teste e troque aqui mesmo; dá para voltar ao automático do provedor."));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        actions.Children.Add(Small(_dnsResults is null ? "Testar DNS" : "Testar de novo", true, DnsTestAsync));
        actions.Children.Add(Small("Voltar ao automático", false, async () => await ChangeDnsAsync(null)));
        Grid.SetColumn(actions, 1);
        head.Children.Add(actions);
        panel.Children.Add(head);
        panel.Children.Add(Muted("Em uso agora: " + (CurrentDns() is { } d ? $"{DnsName(d)} ({string.Join(", ", _adapter!.Dns.Take(2))})" : "não identificado"), 12));
        panel.Children.Add(Free(_dnsRows));
        FillDnsRows();
        if (_dnsResults is null && !_dnsRunning) _ = DnsTestAsync();
        return CardOf(panel);
    }

    private async Task DnsTestAsync()
    {
        if (_dnsRunning) return;
        _dnsRunning = true;
        _dnsRows.Children.Clear();
        _dnsRows.Children.Add(Muted("Testando (cada DNS responde a 5 sites, 2 vezes)...", 12));
        _adapter ??= await Task.Run(NetworkStatus.Active);
        var current = CurrentDns();
        var providers = new List<DnsProvider>();
        if (current is not null && !DnsProviders.Defaults.Any(p => p.Primary == current))
            providers.Add(new DnsProvider("Provedor (em uso)", current, "", current));
        providers.AddRange(DnsProviders.Defaults);
        try
        {
            _dnsResults = await DnsBenchmark.RunAsync(providers, DnsBenchmark.DefaultDomains, 2, TimeSpan.FromSeconds(1.5));
        }
        finally { _dnsRunning = false; }
        FillDnsRows();
    }

    private void FillDnsRows()
    {
        if (_dnsResults is null) return;
        _dnsRows.Children.Clear();
        var current = CurrentDns();
        var ranked = _dnsResults.OrderBy(r => r.AverageMs ?? double.MaxValue).ToList();
        var best = ranked.FirstOrDefault(r => r.AverageMs is not null);
        double worst = ranked.Where(r => r.AverageMs is not null).Select(r => r.AverageMs!.Value).DefaultIfEmpty(1).Max();
        foreach (var r in ranked)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            bool inUse = r.Provider.Primary == current;
            bool isBest = ReferenceEquals(r, best);

            var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(new TextBlock { Text = $"{r.Provider.Name}  {r.Provider.Primary}", FontSize = 13 });
            if (isBest) line.Children.Add(Pill("mais rápido", "#16301F", "#3DDC97"));
            if (inUse) line.Children.Add(Pill("em uso", "#2A2410", "#FFC400"));
            name.Children.Add(line);
            if (r.Provider.Note.Length > 0 && r.Provider.Note != r.Provider.Primary)
                name.Children.Add(new TextBlock { Text = r.Provider.Note + $" · respondeu {r.Successes} de {r.Attempts}", FontSize = 10.5, Foreground = Brush("MutedBrush") });
            g.Children.Add(name);

            int level = r.AverageMs switch { null => 3, < 15 => 0, < 30 => 1, < 60 => 2, _ => 3 };
            double fraction = r.AverageMs is double ms ? Math.Clamp(ms / Math.Max(worst, 1), 0.04, 1) : 1;
            var barHost = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            barHost.Children.Add(new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = Brush("Surface2Brush") });
            var bar = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = LevelBrush(level), HorizontalAlignment = HorizontalAlignment.Left };
            barHost.Children.Add(bar);
            barHost.SizeChanged += (_, e) => bar.Width = e.NewSize.Width * fraction;
            Grid.SetColumn(barHost, 1);
            g.Children.Add(barHost);

            var value = new TextBlock { Text = r.AverageMs is double v ? $"{v:0} ms" : "sem resposta", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = LevelBrush(level), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(value, 2);
            g.Children.Add(value);

            UIElement action;
            if (inUse) action = new TextBlock { Text = "em uso", FontSize = 12, Foreground = Brush("MutedBrush"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            else if (r.Provider.Secondary.Length == 0 || r.AverageMs is null) action = new TextBlock();
            else
            {
                var provider = r.Provider;
                var use = new Button { Content = "Usar", Style = (Style)FindResource(isBest ? "PrimaryButton" : "GhostButton"), Padding = new Thickness(14, 4, 14, 4), FontSize = 12.5 };
                use.Click += async (_, _) => await ChangeDnsAsync(provider);
                action = use;
            }
            Grid.SetColumn(action, 3);
            g.Children.Add(action);
            _dnsRows.Children.Add(new Border { Child = g, Padding = new Thickness(2, 6, 2, 6), BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Brush("Surface2Brush") });
        }

        var mine = ranked.FirstOrDefault(r => r.Provider.Primary == current);
        if (best is not null && mine?.AverageMs is double now && best.AverageMs is double fast)
            _dnsRows.Children.Add(Muted(best.Provider.Primary == current ? "O DNS que você usa já é o mais rápido agora."
                : $"Trocando para {best.Provider.Name}, cada site ou jogo começa uns {now - fast:0} ms mais rápido.", 12));
    }

    /// <summary>Troca o DNS dos adaptadores ativos (nulo = volta ao automático do provedor) e atualiza a tela.</summary>
    private async Task ChangeDnsAsync(DnsProvider? provider)
    {
        try
        {
            if (provider is null) await DnsApplier.RestoreAsync();
            else await DnsApplier.ApplyAsync(provider);
            Say(provider is null ? "DNS voltou para o automático do provedor." : $"DNS trocado para {provider.Name}. Para desfazer, use Voltar ao automático.", "OkBrush");
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception or System.IO.IOException)
        {
            Say("Não foi possível trocar o DNS: " + ex.Message, "BadBrush");
        }
        _adapter = await Task.Run(NetworkStatus.Active);
        Set("dns", DnsName(CurrentDns()) ?? "—");
        UpdateConnLine();
        if (_tab == "dns") SelectTab("dns");
    }

    // ================================================================ Roteador e rota

    private PingTarget? _routeTarget;
    private readonly StackPanel _routeRows = new();
    private CancellationTokenSource? _routeCts;

    private UIElement RouteTab()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var info = new StackPanel();
        info.Children.Add(Title("Sua conexão", ""));
        void Line(string label, string? value)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Brush("MutedBrush") });
            var v = new TextBlock { Text = string.IsNullOrWhiteSpace(value) ? "—" : value, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(v, 1);
            g.Children.Add(v);
            info.Children.Add(g);
        }
        Line("Você", _city);
        Line("Provedor", _isp);
        Line("Adaptador", _adapter is null ? null : _adapter.Name + (_adapter.LinkSpeedBitsPerSecond > 0 ? $" · link de {_adapter.LinkSpeedBitsPerSecond / 1_000_000} Mbps" : ""));
        Line("Roteador", _adapter?.Gateway);
        Line("Ping do roteador", _routerMs is double rm ? $"{rm:0} ms" + (rm > 10 ? " (alto: Wi-Fi fraco ou roteador ocupado)" : "") : null);
        Line("IP local (na sua rede)", LocalIp());
        Line("IP real (na internet)", _publicIp);
        Line("DNS", _adapter is null ? null : string.Join(", ", _adapter.Dns.Take(2)));
        Line("Servidor de teste", _libre is not null ? "LibreSpeed · " + _libre.Name : _colo is null ? null : "Cloudflare " + _colo.Name);
        var routerButton = Small("Medir o roteador", false, async () =>
        {
            _adapter = await Task.Run(NetworkStatus.Active);
            _routerMs = _adapter?.Gateway is { } gw ? await NetAnalyzer.PingAsync(gw, 1000) : null;
            SelectTab("route");
        });
        routerButton.HorizontalAlignment = HorizontalAlignment.Left;
        routerButton.Margin = new Thickness(0, 10, 0, 0);
        info.Children.Add(routerButton);
        info.Children.Add(Muted("A cidade vem do seu IP público (é a do provedor, aproximada) e não é guardada.", 10.5));
        grid.Children.Add(CardOf(info));

        var route = new StackPanel();
        var targets = new List<(string Name, string Host)> { ("Servidor de teste (Cloudflare)", "speed.cloudflare.com") };
        if (_routeTarget is { } rt) targets.Insert(0, (ShortName(rt), rt.Host));
        foreach (var t in _catalog.Where(t => _results.ContainsKey(t.Key) && t.Key != _routeTarget?.Key).Take(4)) targets.Add((ShortName(t), t.Host));
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(Title("Rota até o servidor", "Cada salto é um roteador no caminho. Um salto com tempo alto que continua alto até o fim mostra onde o atraso começa."));
        var pick = new ComboBox { MinWidth = 220, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Top };
        foreach (var t in targets) pick.Items.Add(t.Name);
        pick.SelectedIndex = 0;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        actions.Children.Add(pick);
        actions.Children.Add(Small("Traçar rota", true, async () => await TraceAsync(targets[Math.Max(0, pick.SelectedIndex)].Host)));
        Grid.SetColumn(actions, 1);
        head.Children.Add(actions);
        route.Children.Add(head);
        route.Children.Add(Free(_routeRows));
        var routeCard = CardOf(route);
        routeCard.Margin = new Thickness(14, 0, 0, 0);
        Grid.SetColumn(routeCard, 1);
        grid.Children.Add(routeCard);
        return grid;
    }

    /// <summary>Traceroute por uns 10 segundos (várias rodadas), mostrando cada salto com média, perda e pior tempo.</summary>
    private async Task TraceAsync(string host)
    {
        _routeCts?.Cancel();
        _routeCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var token = _routeCts.Token;
        _routeRows.Children.Clear();
        _routeRows.Children.Add(Muted($"Traçando a rota até {host} (10 segundos)...", 12));
        void Show(IReadOnlyList<HopStats> hops) => Dispatcher.Invoke(() =>
        {
            _routeRows.Children.Clear();
            foreach (var h in hops)
            {
                var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                foreach (var w in new[] { 34.0, -1, 80, 80, 80 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
                double? avg = h.Received > 0 ? h.Average : null;
                double loss = h.Sent == 0 ? 0 : 100.0 * (h.Sent - h.Received) / h.Sent;
                string[] cells = { h.Hop.ToString(), h.Address, avg is double a ? $"{a:0} ms" : "*", $"{loss:0}% perda", h.Received > 0 ? $"pior {h.Worst:0}" : "" };
                for (int i = 0; i < cells.Length; i++)
                {
                    var t = new TextBlock { Text = cells[i], FontSize = 12, Foreground = i == 2 ? LevelBrush(GameServers.Level(avg)) : Brush(i == 0 || i >= 3 ? "MutedBrush" : "TextBrush") };
                    Grid.SetColumn(t, i);
                    g.Children.Add(t);
                }
                _routeRows.Children.Add(g);
            }
        });
        try { await NetAnalyzer.TraceAsync(host, 30, Show, token); }
        catch (OperationCanceledException) { }
        catch (System.Net.NetworkInformation.PingException ex) { _routeRows.Children.Add(Muted("Não foi possível traçar: " + ex.Message, 12)); }
        _routeRows.Children.Add(Muted("Pronto. Asteriscos (*) são roteadores que não respondem ao teste; isso é normal se os saltos seguintes respondem.", 11.5));
    }

    // ================================================================ Histórico

    private UIElement HistoryTab()
    {
        var panel = new StackPanel();
        panel.Children.Add(Title("Testes anteriores", "Os últimos testes deste PC. Dê nome a um teste (aba Velocidade) para comparar antes e depois de uma mudança."));
        var history = _history.Load();
        if (SpeedHistory.Compare(history) is { } compare) panel.Children.Add(Muted(compare, 12.5));
        if (history.Count == 0) { panel.Children.Add(Muted("Nenhum teste ainda. Clique em INICIAR TESTE.", 12)); return CardOf(panel); }
        double maxDown = history.Take(15).Select(h => h.DownloadMbps ?? 0).DefaultIfEmpty(1).Max();
        foreach (var h in history.Take(15))
        {
            var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            foreach (var w in new[] { 150.0, -1, 120, 120, 110 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
            var when = new StackPanel();
            when.Children.Add(new TextBlock { Text = h.At.ToLocalTime().ToString("dd/MM HH:mm"), FontSize = 12.5 });
            if (h.Name is { Length: > 0 } n) when.Children.Add(new TextBlock { Text = n, FontSize = 10.5, Foreground = Brush("MutedBrush") });
            g.Children.Add(when);
            double fraction = Math.Clamp((h.DownloadMbps ?? 0) / Math.Max(maxDown, 1), 0.02, 1);
            var barHost = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            barHost.Children.Add(new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = Brush("Surface2Brush") });
            var bar = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = (System.Windows.Media.Brush)FindResource("AccentGradientBrush"), HorizontalAlignment = HorizontalAlignment.Left };
            barHost.Children.Add(bar);
            barHost.SizeChanged += (_, e) => bar.Width = e.NewSize.Width * fraction;
            Grid.SetColumn(barHost, 1);
            g.Children.Add(barHost);
            string[] cells = { "↓ " + Fmt(h.DownloadMbps), "↑ " + Fmt(h.UploadMbps), Ms(h.PingMs) + (h.LoadedPingMs is double l ? $" · carga {l:0}" : "") };
            for (int i = 0; i < cells.Length; i++)
            {
                var t = new TextBlock { Text = cells[i], FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(t, i + 2);
                g.Children.Add(t);
            }
            panel.Children.Add(new Border { Child = g, Padding = new Thickness(2, 4, 2, 4), BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Brush("Surface2Brush") });
        }
        return CardOf(panel);
    }
}
