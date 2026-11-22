using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Network;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Aba Rede local: quantos aparelhos estão na rede da casa ou empresa (com fabricante e tipo), se há lentidão até o roteador e o
/// ajuste da placa de rede pelas configurações avançadas do driver (só o que existe nela, com volta ao padrão).
/// </summary>
public sealed partial class InternetTestPage
{
    private IReadOnlyList<LanDevice>? _lanDevices;
    private LinkQuality? _lanQuality;
    private IReadOnlyList<NicChange>? _nicChanges;
    private readonly Dictionary<NicChange, CheckBox> _nicBoxes = new();
    private bool _lanBusy;
    private int _lanProgress;
    private CancellationTokenSource? _lanCts;

    private UIElement LanTab()
    {
        var root = new StackPanel();
        var (ip, wifi, description) = LocalNetwork.LocalAddress();

        // ---- números
        var devices = OptimizeKit.Stat("Aparelhos conectados", _lanDevices is null ? "—" : _lanDevices.Count.ToString(),
            _lanDevices is null ? "clique em Escanear a rede" : $"{_lanDevices.Count(d => d.Kind.StartsWith("Celular"))} celulares · {_lanDevices.Count(d => d.Kind == "Computador")} computadores");
        var quality = OptimizeKit.Stat("Até o roteador", _lanQuality?.AverageMs is double ms ? $"{ms:0} ms" : "—",
            _lanQuality is null ? "clique em Medir lentidão" : _lanQuality.Slow ? "lentidão na rede local" : "rede local boa", _lanQuality is null ? "MutedBrush" : _lanQuality.Slow ? "WarnBrush" : "OkBrush");
        var nic = OptimizeKit.Stat("Placa de rede", wifi ? "Wi-Fi" : ip is null ? "—" : "Cabo", description.Length > 0 ? description : "não identificada");
        var tuned = OptimizeKit.Stat("Ajustes da placa", _nicChanges is null ? "—" : _nicChanges.Count == 0 ? "Em dia" : _nicChanges.Count.ToString(),
            _nicChanges is null ? "clique em Analisar a placa" : _nicChanges.Count == 0 ? "nada para melhorar" : "podem melhorar", _nicChanges is { Count: > 0 } ? "WarnBrush" : "MutedBrush");
        root.Children.Add(OptimizeKit.StatRow(devices.Card, quality.Card, nic.Card, tuned.Card));

        // ---- aparelhos
        var scan = new StackPanel();
        var head = new DockPanel();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        actions.Children.Add(Small(_lanDevices is null ? "Escanear a rede" : "Escanear de novo", true, async () => await LanScanAsync(ip)));
        actions.Children.Add(Small("Medir lentidão", false, async () => await LanQualityAsync(wifi)));
        DockPanel.SetDock(actions, Dock.Right);
        head.Children.Add(actions);
        head.Children.Add(Title("Aparelhos na sua rede", "Tudo que está ligado no mesmo roteador: celulares, TVs, videogames, câmeras e outros PCs. Aparelho estranho? Troque a senha do Wi-Fi."));
        scan.Children.Add(head);
        if (_lanBusy)
        {
            scan.Children.Add(new ProgressBar { Value = _lanProgress, Height = 6, Margin = new Thickness(0, 8, 0, 4), Foreground = Brush("AccentBrush"), Background = Brush("Surface2Brush"), BorderThickness = new Thickness(0) });
            scan.Children.Add(Muted($"Procurando aparelhos ({_lanProgress}%)...", 11.5));
        }
        if (_lanQuality is not null) scan.Children.Add(Muted(_lanQuality.Text, 12));
        if (_lanDevices is not null)
        {
            var table = new CompactTable(new[]
            {
                new TableColumn("Aparelho"), new TableColumn("Tipo", 170), new TableColumn("IP", 120), new TableColumn("MAC", 150), new TableColumn("Ping", 70, HorizontalAlignment.Right),
            }, checks: false) { Margin = new Thickness(0, 8, 0, 0) };
            table.SetRows(_lanDevices.Select(d => new TableRow(null, CompactTable.Glyph(d.Kind switch
            {
                "Roteador" => "", "Este PC" => "", "Celular ou tablet" => "", "Videogame" => "", "TV ou streaming" => "",
                "Casa inteligente" => "", "Computador" => "", "Impressora ou servidor" => "", _ => "",
            }, d.IsRouter || d.IsThisPc ? "AccentBrush" : "TextBrush"), new object[]
            {
                d.Name.Length > 0 ? d.Name : d.Vendor.Length > 0 ? d.Vendor : "Aparelho sem nome", d.Kind, d.Ip, d.Mac.Length > 0 ? d.Mac : "—", d.PingMs is double p ? $"{p:0} ms" : "—",
            }, ToolTip: d.Vendor.Length > 0 ? "Fabricante: " + d.Vendor : null)).ToList());
            scan.Children.Add(table);
        }
        root.Children.Add(CardOf(scan));

        // ---- placa de rede
        var card = new StackPanel();
        var nicHead = new DockPanel();
        var nicActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        nicActions.Children.Add(Small("Analisar a placa", _nicChanges is null, LanNicReadAsync));
        if (_nicChanges is { Count: > 0 }) nicActions.Children.Add(Small("Configurar pelo meu hardware", true, LanNicApplyAsync));
        nicActions.Children.Add(Small("Voltar ao padrão do driver", false, LanNicResetAsync));
        DockPanel.SetDock(nicActions, Dock.Right);
        nicHead.Children.Add(nicActions);
        nicHead.Children.Add(Title("Placa de rede (" + (wifi ? "Wi-Fi" : "cabo") + ")",
            "O MB lê as configurações avançadas do driver da sua placa (Intel, Realtek, Killer...) e liga o modo de menos atraso: sem economia de energia, buffers no máximo e, no Wi-Fi, sem troca de antena no meio do jogo."));
        card.Children.Add(nicHead);
        if (_nicChanges is { Count: 0 }) card.Children.Add(Muted("A placa já está com as melhores configurações que o driver oferece.", 12));
        if (_nicChanges is { Count: > 0 } changes)
        {
            _nicBoxes.Clear();
            var table = new CompactTable(new[]
            {
                new TableColumn("Configuração", 240), new TableColumn("Placa", 150), new TableColumn("Agora", 110), new TableColumn("Fica", 70), new TableColumn("Por quê"),
            }) { Margin = new Thickness(0, 8, 0, 0) };
            table.SetRows(changes.Select(c =>
            {
                var box = new CheckBox { IsChecked = true };
                _nicBoxes[c] = box;
                return new TableRow(box, CompactTable.Glyph(wifi ? "" : ""), new object[] { c.Title, c.Adapter, c.From, c.To, c.Why });
            }).ToList());
            card.Children.Add(table);
            card.Children.Add(Muted("Ao aplicar, a internet cai por 2 a 5 segundos enquanto a placa reinicia. Para desfazer, use Voltar ao padrão do driver.", 11.5));
        }
        root.Children.Add(CardOf(card));
        return root;
    }

    private async Task LanScanAsync(string? ip)
    {
        if (_lanBusy) return;
        if (ip is null) { Say("Sem rede local: conecte o cabo ou o Wi-Fi.", "WarnBrush"); return; }
        _lanBusy = true;
        _lanProgress = 0;
        _lanCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        SelectTab("lan");
        int last = -1;
        var progress = new Progress<int>(p => { _lanProgress = p; if (p / 10 != last / 10 && _tab == "lan") { last = p; SelectTab("lan"); } });
        try
        {
            _adapter ??= await Task.Run(NetworkStatus.Active);
            _lanDevices = await LocalNetwork.ScanAsync(ip, _adapter?.Gateway, progress, _lanCts.Token);
            Say($"{_lanDevices.Count} aparelho(s) na rede.", "OkBrush");
        }
        catch (OperationCanceledException) { Say("A varredura demorou demais e foi interrompida.", "WarnBrush"); }
        finally { _lanBusy = false; }
        if (_tab == "lan") SelectTab("lan");
    }

    private async Task LanQualityAsync(bool wifi)
    {
        _adapter ??= await Task.Run(NetworkStatus.Active);
        if (_adapter?.Gateway is not { } gateway) { Say("Roteador não encontrado.", "WarnBrush"); return; }
        Say("Medindo a ligação até o roteador (2 segundos)...");
        _lanQuality = await LocalNetwork.MeasureAsync(gateway, wifi, CancellationToken.None);
        Say(_lanQuality.Text, _lanQuality.Slow ? "WarnBrush" : "OkBrush");
        if (_tab == "lan") SelectTab("lan");
    }

    private async Task LanNicReadAsync()
    {
        Say("Lendo as configurações da placa de rede...");
        try { _nicChanges = LocalNetwork.Recommend(await Task.Run(LocalNetwork.ReadProperties)); }
        catch (InvalidOperationException ex) { Say("Não foi possível ler a placa: " + ex.Message, "BadBrush"); return; }
        Say(_nicChanges.Count == 0 ? "A placa já está otimizada." : $"{_nicChanges.Count} configuração(ões) podem melhorar.", _nicChanges.Count == 0 ? "OkBrush" : "MutedBrush");
        if (_tab == "lan") SelectTab("lan");
    }

    private async Task LanNicApplyAsync()
    {
        var chosen = _nicBoxes.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToList();
        if (chosen.Count == 0) { Say("Marque pelo menos uma configuração.", "WarnBrush"); return; }
        if (MessageBox.Show($"Aplicar {chosen.Count} ajuste(s) na placa de rede?\n\nA internet cai por alguns segundos enquanto a placa reinicia.", "Placa de rede",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Say("Aplicando na placa de rede...");
        var (ok, failed) = await Task.Run(() => LocalNetwork.Apply(chosen));
        foreach (var c in chosen)
            MBExtreme.Core.Operations.OperationJournal.Shared.RecordSetting("nic." + c.Keyword, $"Placa de rede · {c.Title}: {c.From} → {c.To}", true,
                "Para desfazer: Rede › Rede local › Voltar ao padrão do driver.");
        Say($"{ok} ajuste(s) aplicados na placa de rede." + (failed > 0 ? $" {failed} o driver não aceitou." : ""), failed == 0 ? "OkBrush" : "WarnBrush");
        await LanNicReadAsync();
    }

    private async Task LanNicResetAsync()
    {
        if (MessageBox.Show("Voltar todas as configurações avançadas da placa de rede ao padrão do driver?", "Placa de rede", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        bool ok = await Task.Run(LocalNetwork.ResetAll);
        Say(ok ? "Placa de rede voltou ao padrão do driver." : "Não foi possível voltar ao padrão.", ok ? "OkBrush" : "BadBrush");
        _nicChanges = null;
        if (_tab == "lan") SelectTab("lan");
    }
}
