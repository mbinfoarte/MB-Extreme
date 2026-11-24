using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MBExtreme.Core.Gamepads;
using MBExtreme.Core.Hardware;
using MBExtreme.Core.Operations;
using MBExtreme.Core.Settings;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>Peças das abas de desempenho do Controle: cores, cartão de número e o gráfico de barras dos intervalos.</summary>
internal static class PerfUi
{
    public static readonly Color Yellow = Color.FromRgb(0xFF, 0xC4, 0x00), Green = Color.FromRgb(0x3D, 0xDC, 0x97), Orange = Color.FromRgb(0xFF, 0xB0, 0x20),
        Red = Color.FromRgb(0xFF, 0x7B, 0x7B), Cyan = Color.FromRgb(0x4C, 0xD7, 0xF6);

    public static SolidColorBrush B(Color c) => new(c);

    public static Color LevelColor(int level) => level switch { 0 => Green, 1 => Green, 2 => Orange, _ => Red };

    /// <summary>Gráfico: cada barra é o tempo até o próximo pacote; acima de 8 ms fica vermelho, acima de 2x o normal fica laranja.</summary>
    public static Border Bars(IReadOnlyList<double> bars, double height = 90)
    {
        var grid = new Grid { Height = height };
        if (bars.Count > 0)
        {
            double normal = bars.OrderBy(b => b).ElementAt(bars.Count / 2);
            double max = Math.Max(bars.Max(), normal * 4);
            for (int i = 0; i < bars.Count; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                double v = bars[i];
                var color = v > PadLatencyReport.SpikeMs && v > normal * 2 ? Red : v > normal * 2 ? Orange : Yellow;
                var bar = new Border
                {
                    Background = B(color), CornerRadius = new CornerRadius(2, 2, 0, 0), Margin = new Thickness(1, 0, 1, 0), VerticalAlignment = VerticalAlignment.Bottom,
                    Height = Math.Max(2, (height - 6) * v / max), ToolTip = $"{v:0.0} ms",
                };
                Grid.SetColumn(bar, i);
                grid.Children.Add(bar);
            }
        }
        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x10)), BorderBrush = PadUi.Res("BorderBrush"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(6), Child = grid,
        };
    }

    public static Border Tile(string value, string label, Color? color = null)
    {
        var st = new StackPanel();
        st.Children.Add(new TextBlock { Text = value, FontSize = 19, FontWeight = FontWeights.Bold, Foreground = color is { } c ? B(c) : PadUi.Res("TextBrush") });
        st.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = PadUi.Res("MutedBrush"), TextWrapping = TextWrapping.Wrap });
        return new Border
        {
            Background = PadUi.Res("Surface2Brush"), BorderBrush = PadUi.Res("BorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 8, 0), Child = st,
        };
    }

    public static Grid TileRow(params Border[] tiles)
    {
        var g = new Grid { Margin = new Thickness(0, 8, -8, 8) };
        for (int i = 0; i < tiles.Length; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(tiles[i], i);
            g.Children.Add(tiles[i]);
        }
        return g;
    }

    public static TextBlock Line(string text, Color dot) => new()
    {
        Inlines = { new System.Windows.Documents.Run("● ") { Foreground = B(dot) }, new System.Windows.Documents.Run(text) },
        FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2),
    };

    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint period);

    /// <summary>Relógio do Windows em 1 ms enquanto mede (sem isso o Sleep(1) dura até 15 ms e a leitura do XInput parece lenta).</summary>
    public static IDisposable FineTimer()
    {
        bool on = false;
        try { on = timeBeginPeriod(1) == 0; } catch (DllNotFoundException) { }
        return new Release(() => { if (on) timeEndPeriod(1); });
    }

    private sealed class Release(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}

/// <summary>Um controle que dá para medir no Analisador.</summary>
internal sealed record MeasurablePad(string Name, ushort Vendor, ushort Product, PadLink Link, string? HidPath, int InputLength, uint? XInputSlot)
{
    public string Key => $"{Vendor:X4}{Product:X4}";

    /// <summary>
    /// Mede por <paramref name="ms"/> milissegundos. Controle Xbox no cabo: primeiro pela leitura HID direta (vê todo pacote); se ela
    /// não trouxer pacotes, pelo XInput com o relógio de 1 ms (aí só vê pacotes que mudam algo, então não conta perdidos).
    /// </summary>
    public PadLatencyReport? Measure(int ms, CancellationToken token)
    {
        if (HidPath is not null)
        {
            try
            {
                var times = WinHid.CaptureReports(HidPath, InputLength, ms, token);
                if (PadLatencyReport.From(Link, times) is { } r) return r;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && XInputSlot is not null) { /* tenta pelo XInput */ }
        }
        if (XInputSlot is { } slot)
        {
            return PadLatencyReport.From(Link, XInput.Capture(slot, ms, token), countLost: false);
        }
        return null;
    }

    public static IReadOnlyList<MeasurablePad> Find()
    {
        var list = new List<MeasurablePad>();
        IReadOnlyList<GameController> hid;
        try { hid = WinHid.FindGameControllers(); }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or DllNotFoundException) { hid = Array.Empty<GameController>(); }
        int xinputUsed = 0;
        var slots = Enumerable.Range(0, 4).Select(i => (uint)i).Where(i => XInput.TryState(i, out _)).ToList();
        foreach (var pad in hid)
        {
            var link = pad.Connection == PadConnection.Bluetooth ? PadLink.Bluetooth : PadLink.Usb;
            uint? slot = pad.Vendor == ControllerCatalog.Microsoft && xinputUsed < slots.Count ? slots[xinputUsed++] : null;
            list.Add(new MeasurablePad(pad.Name, pad.Vendor, pad.Product, link, pad.Path, pad.InputLength, slot));
        }
        for (int i = xinputUsed; i < slots.Count; i++)
            list.Add(new MeasurablePad($"Controle Xbox / XInput {slots[i] + 1}", ControllerCatalog.Microsoft, 0, PadLink.Usb, null, 0, slots[i]));
        return list;
    }
}

/// <summary>Aba "Analisador": mede cabo e Bluetooth (15 s cada), mostra lado a lado, o gráfico dos engasgos e diz qual usar.</summary>
internal sealed class PadAnalyzerPage : UserControl
{
    private const int MeasureMs = 15000;
    private readonly ComboBox _pads = new() { MinWidth = 320, Margin = new Thickness(0, 0, 10, 0) };
    private readonly TextBlock _status = PadUi.Muted("", 12.5);
    private readonly Grid _columns = new() { Margin = new Thickness(0, 4, 0, 8) };
    private readonly StackPanel _verdict = new();
    private readonly Button _measure = PadUi.Button("Medir agora (15 s)", primary: true);
    private readonly Button _loaded = PadUi.Button("Medir com a internet em uso");
    private IReadOnlyList<MeasurablePad> _found = Array.Empty<MeasurablePad>();
    private CancellationTokenSource? _cts;
    private PadLinkContext _context = new();

    public PadAnalyzerPage()
    {
        var root = PadUi.Root("Analisador do controle",
            "Mede quanto tempo passa entre um comando e o próximo, a variação e os engasgos. Meça no cabo, depois no Bluetooth, e o programa diz qual é melhor para o seu controle.", out var scroll);
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var refresh = PadUi.Button("Procurar controles");
        refresh.Click += (_, _) => Load();
        _measure.Click += async (_, _) => await MeasureAsync(loaded: false);
        _loaded.Click += async (_, _) => await MeasureAsync(loaded: true);
        _loaded.ToolTip = "Baixa da internet durante a medição, para ver se o Wi-Fi atrapalha o Bluetooth.";
        _pads.SelectionChanged += (_, _) => Render();
        bar.Children.Add(_pads);
        bar.Children.Add(_measure);
        bar.Children.Add(_loaded);
        bar.Children.Add(refresh);
        root.Children.Add(PadUi.Card(new StackPanel { Children = { bar, _status } }));
        _columns.ColumnDefinitions.Add(new ColumnDefinition());
        _columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        _columns.ColumnDefinitions.Add(new ColumnDefinition());
        root.Children.Add(_columns);
        root.Children.Add(_verdict);
        Content = scroll;
        Loaded += (_, _) => { if (_found.Count == 0) Load(); };
        Unloaded += (_, _) => _cts?.Cancel();
    }

    private MeasurablePad? Selected => _pads.SelectedIndex >= 0 && _pads.SelectedIndex < _found.Count ? _found[_pads.SelectedIndex] : null;

    private async void Load()
    {
        _status.Text = "Procurando controles...";
        _found = await Task.Run(() => MeasurablePad.Find());
        _context = await Task.Run(() =>
        {
            var tweak = AppServices.Tweaks.FirstOrDefault(t => t.Id == "power-usb-suspend");
            int radios;
            try { radios = WinUsbTopology.Read().Count(p => p.Use == UsbUse.BluetoothAdapter); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or DllNotFoundException or EntryPointNotFoundException) { radios = 1; }
            return new PadLinkContext(PadSystem.BluetoothAudioDevices(), PadSystem.WifiOn24GHz(), Math.Max(1, radios), false, tweak?.IsApplied() == false);
        });
        _pads.Items.Clear();
        foreach (var p in _found) _pads.Items.Add($"{p.Name} · {PadAnalyzer.Name(p.Link)}");
        if (_found.Count > 0) _pads.SelectedIndex = 0;
        _status.Text = _found.Count == 0
            ? "Nenhum controle encontrado. Ligue pelo cabo USB ou pareie pelo Bluetooth do Windows e clique em Procurar controles."
            : "Clique em Medir agora e mexa o analógico em círculos sem parar até terminar.";
        Render();
    }

    private async Task MeasureAsync(bool loaded)
    {
        if (Selected is not { } pad) return;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _measure.IsEnabled = _loaded.IsEnabled = false;
        _status.Foreground = PadUi.Res("WarnBrush");
        _status.Text = (loaded ? "Baixando da internet e medindo. " : "") + "Mexa o analógico em círculos sem parar por 15 segundos...";
        CancellationTokenSource? download = null;
        try
        {
            if (loaded)
            {
                download = CancellationTokenSource.CreateLinkedTokenSource(token);
                var http = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
                var test = new MBExtreme.Core.Network.SpeedTest(http);
                _ = Task.Run(async () =>
                {
                    try { await test.DownloadAsync(4, TimeSpan.Zero, TimeSpan.FromMilliseconds(MeasureMs + 2000), null, download.Token); }
                    catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException) { }
                    finally { http.Dispose(); }
                });
                await Task.Delay(1500, token);
            }
            var report = await Task.Run(() => pad.Measure(MeasureMs, token), token);
            _status.Foreground = PadUi.Res("MutedBrush");
            if (report is null)
            {
                _status.Text = "Chegaram poucos comandos. Mexa o analógico o tempo todo durante a medição e tente de novo.";
                return;
            }
            AppServices.Settings.PadMeasurements[PadLatencyReport.RecordKey(pad.Product, pad.Link)] = report.ToRecord(DateTime.Now);
            AppServices.Settings.Save();
            _status.Text = report.Link == PadLink.Usb
                ? "Pronto. Agora tire o cabo, ligue o controle pelo Bluetooth, clique em Procurar controles e meça de novo para comparar."
                : "Pronto. Para comparar, ligue o cabo USB, clique em Procurar controles e meça de novo.";
            Render();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            _status.Foreground = PadUi.Res("BadBrush");
            _status.Text = ex.Message;
        }
        finally
        {
            download?.Cancel();
            _measure.IsEnabled = _loaded.IsEnabled = true;
        }
    }

    private PadLatencyReport? Saved(MeasurablePad pad, PadLink link) =>
        AppServices.Settings.PadMeasurements.TryGetValue(PadLatencyReport.RecordKey(pad.Product, link), out var r) ? PadLatencyReport.FromRecord(link, r) : null;

    private void Render()
    {
        _columns.Children.Clear();
        _verdict.Children.Clear();
        if (Selected is not { } pad) return;
        var usb = Saved(pad, PadLink.Usb);
        var bt = Saved(pad, PadLink.Bluetooth);
        var best = PadAnalyzer.Best(usb, bt);
        var left = Column(PadLink.Usb, usb, usb is not null && bt is not null && best == PadLink.Usb, pad);
        var right = Column(PadLink.Bluetooth, bt, usb is not null && bt is not null && best == PadLink.Bluetooth, pad);
        Grid.SetColumn(right, 2);
        _columns.Children.Add(left);
        _columns.Children.Add(right);

        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = PadAnalyzer.Verdict(usb, bt), FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var current = pad.Link == PadLink.Usb ? usb : bt;
        if (current is not null)
            foreach (string tip in PadAnalyzer.Tips(current, _context with { BehindHub = false }))
                text.Children.Add(PerfUi.Line(tip, PerfUi.Yellow));
        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var apply = PadUi.Button("Aplicar Controle competitivo", primary: true);
        apply.Click += (_, _) => MainWindow.Current?.Go("pad-competitive");
        var ports = PadUi.Button("Ver a melhor porta USB");
        ports.Click += (_, _) => MainWindow.Current?.Go("usb-ports");
        buttons.Children.Add(apply);
        buttons.Children.Add(ports);
        if (PadOverclock.IsSupported(pad.Vendor, pad.Product))
        {
            var oc = PadUi.Button("Overclock 1000 Hz");
            oc.Click += (_, _) => MainWindow.Current?.Go("pad-overclock");
            buttons.Children.Add(oc);
        }
        text.Children.Add(buttons);
        _verdict.Children.Add(PadUi.Card(text));
    }

    private static Border Column(PadLink link, PadLatencyReport? r, bool winner, MeasurablePad pad)
    {
        var st = new StackPanel();
        var head = new DockPanel();
        var badge = new TextBlock
        {
            Text = r is null ? "NÃO MEDIDO" : winner ? "MELHOR ESCOLHA" : r.Quality, FontSize = 11.5, FontWeight = FontWeights.Bold,
            Foreground = r is null ? PadUi.Res("MutedBrush") : PerfUi.B(winner ? PerfUi.Green : PerfUi.LevelColor(r.Level)),
        };
        DockPanel.SetDock(badge, Dock.Right);
        head.Children.Add(badge);
        head.Children.Add(PadUi.Head(link == PadLink.Usb ? "CABO USB" : "BLUETOOTH"));
        st.Children.Add(head);
        if (r is null)
        {
            st.Children.Add(PadUi.Muted(link == pad.Link
                ? "Clique em Medir agora para medir esta conexão."
                : link == PadLink.Usb ? "Ligue o cabo USB, clique em Procurar controles e meça." : "Ligue pelo Bluetooth, clique em Procurar controles e meça.", 12.5));
        }
        else
        {
            st.Children.Add(PerfUi.TileRow(
                PerfUi.Tile($"{r.RateHz:0} Hz", "taxa"),
                PerfUi.Tile($"{r.AverageMs:0.0} ms", "média entre pacotes"),
                PerfUi.Tile($"{r.JitterMs:0.0} ms", "variação", r.JitterMs <= 0.6 ? PerfUi.Green : r.JitterMs <= 1.5 ? PerfUi.Yellow : PerfUi.Orange),
                PerfUi.Tile($"{r.Spikes} · {r.Lost}", $"picos acima de {PadLatencyReport.SpikeMs:0} ms · perdidos", r.Spikes + r.Lost == 0 ? PerfUi.Green : PerfUi.Red)));
            st.Children.Add(PerfUi.Bars(r.Bars(60)));
            st.Children.Add(PadUi.Muted($"Cada barra é o tempo até o próximo pacote; pior: {r.WorstMs:0.0} ms. Barras vermelhas são os engasgos que se sentem no jogo.", 11));
        }
        var card = PadUi.Card(st);
        if (winner) card.BorderBrush = PerfUi.B(PerfUi.Green);
        return card;
    }
}

/// <summary>Aba "Portas USB": o desenho das portas do PC (traseiras, frontais), o tipo de cada uma, o que está ligado e onde ligar cada coisa.</summary>
internal sealed class UsbPortsPage : UserControl
{
    private readonly StackPanel _body = new();

    public UsbPortsPage()
    {
        var root = PadUi.Root("Portas USB",
            "As portas do seu PC lidas da placa-mãe: o tipo, de onde cada uma vem e o que está ligado nela. Em verde, amarelo e azul, onde ligar o controle, o adaptador Bluetooth e a placa de captura.", out var scroll);
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var refresh = PadUi.Button("Ler de novo");
        refresh.Click += (_, _) => Load();
        bar.Children.Add(refresh);
        foreach (var (color, text) in new[] { (Color.FromRgb(0x2A, 0x2A, 0x30), "USB 2.0"), (Color.FromRgb(0x2F, 0x6B, 0xFF), "USB 3.x · 5 Gb/s"), (Color.FromRgb(0xE5, 0x48, 0x4D), "USB 3.2 · 10 Gb/s"), (Color.FromRgb(0xC9, 0xCB, 0xD1), "Tipo C") })
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            item.Children.Add(new Border { Width = 14, Height = 8, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(color), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            item.Children.Add(new TextBlock { Text = text, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center });
            bar.Children.Add(item);
        }
        root.Children.Add(bar);
        root.Children.Add(_body);
        Content = scroll;
        Loaded += (_, _) => { if (_body.Children.Count == 0) Load(); };
    }

    private async void Load()
    {
        _body.Children.Clear();
        _body.Children.Add(PadUi.Muted("Lendo as portas...", 13));
        IReadOnlyList<UsbPortInfo> ports;
        try { ports = await Task.Run(() => WinUsbTopology.Read()); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or DllNotFoundException or EntryPointNotFoundException or IOException) { ports = Array.Empty<UsbPortInfo>(); }
        _body.Children.Clear();
        if (ports.Count == 0)
        {
            _body.Children.Add(PadUi.Card(PadUi.Muted("Não foi possível ler as portas USB. Abra o programa como administrador e clique em Ler de novo.", 13)));
            return;
        }
        var advice = UsbPorts.Recommend(ports);
        var marks = new Dictionary<string, Color>();
        foreach (var a in advice)
            if (a.Port is not null && !marks.ContainsKey(a.Port.Id))
                marks[a.Port.Id] = a.For switch { UsbUse.Controller => PerfUi.Green, UsbUse.BluetoothAdapter => PerfUi.Yellow, _ => PerfUi.Cyan };

        foreach (var group in ports.GroupBy(p => p.Panel).OrderBy(g => g.Key switch { UsbPanel.Back => 0, UsbPanel.Front => 1, UsbPanel.Unknown => 2, _ => 3 }))
        {
            string title = group.Key switch
            {
                UsbPanel.Back => "PAINEL TRASEIRO",
                UsbPanel.Front => "PAINEL FRONTAL · GABINETE",
                UsbPanel.Internal => "INTERNAS DA PLACA-MÃE (Bluetooth, RGB e afins)",
                _ => "PORTAS (A PLACA-MÃE NÃO INFORMA O LADO)",
            };
            var head = PadUi.Head(title);
            head.Margin = new Thickness(0, 6, 0, 6);
            _body.Children.Add(head);
            var panel = new WrapPanel();
            foreach (var p in group) panel.Children.Add(PortTile(p, marks.TryGetValue(p.Id, out var m) ? m : null));
            _body.Children.Add(new Border
            {
                Background = new LinearGradientBrush(Color.FromRgb(0x26, 0x26, 0x2B), Color.FromRgb(0x18, 0x18, 0x1B), 90), BorderBrush = PadUi.Res("BorderBrush"),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 12, 4, 4), Margin = new Thickness(0, 0, 0, 8), Child = panel,
            });
        }
        if (ports.Any(p => p.Panel == UsbPanel.Unknown))
            _body.Children.Add(PadUi.Muted("Para saber o lado de uma porta sem informação, ligue um pendrive nela e clique em Ler de novo: o nome do pendrive aparece na porta certa.", 11.5));

        var recs = new List<(UIElement, double)>();
        foreach (var a in advice)
        {
            var color = a.For switch { UsbUse.Controller => PerfUi.Green, UsbUse.BluetoothAdapter => PerfUi.Yellow, _ => PerfUi.Cyan };
            var st = new StackPanel();
            var h = PadUi.Head(UsbPorts.UseText(a.For).ToUpperInvariant());
            h.Foreground = PerfUi.B(color);
            st.Children.Add(h);
            st.Children.Add(new TextBlock
            {
                Text = a.Port is null ? "Sem porta livre" : $"{a.Port.Name} · {a.Port.KindText} · {a.Port.SourceText}" + (a.AlreadyThere ? " (já está nela)" : ""),
                FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 2),
            });
            st.Children.Add(PadUi.Muted(a.Why, 11.5));
            var card = PadUi.Card(st);
            card.BorderBrush = PerfUi.B(color);
            recs.Add((card, 1));
        }
        _body.Children.Add(PadUi.Columns(recs.ToArray()));
    }

    private static Border PortTile(UsbPortInfo p, Color? mark)
    {
        var tongue = p.Kind switch { UsbPortKind.Usb2 => Color.FromRgb(0x2A, 0x2A, 0x30), UsbPortKind.Usb3 => Color.FromRgb(0x2F, 0x6B, 0xFF), _ => Color.FromRgb(0xE5, 0x48, 0x4D) };
        var metal = new SolidColorBrush(Color.FromRgb(0xC9, 0xCB, 0xD1));
        FrameworkElement shape = p.TypeC
            ? new Border { Width = 46, Height = 18, CornerRadius = new CornerRadius(9), Background = metal, Child = new Border { Width = 30, Height = 5, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(tongue), VerticalAlignment = VerticalAlignment.Center } }
            : new Border { Width = 56, Height = 26, CornerRadius = new CornerRadius(3), Background = metal, Padding = new Thickness(4), Child = new Border { Height = 9, CornerRadius = new CornerRadius(1), Background = new SolidColorBrush(tongue), VerticalAlignment = VerticalAlignment.Top } };
        shape.HorizontalAlignment = HorizontalAlignment.Center;
        shape.Margin = new Thickness(0, 2, 0, 6);
        var st = new StackPanel();
        st.Children.Add(shape);
        st.Children.Add(new TextBlock { Text = p.Name, FontSize = 12, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center });
        st.Children.Add(new Border
        {
            Background = PadUi.Res("Surface2Brush"), BorderBrush = PadUi.Res("BorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(999),
            Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 3, 0, 3), HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock { Text = p.KindText, FontSize = 10.5, FontWeight = FontWeights.SemiBold },
        });
        st.Children.Add(new TextBlock
        {
            Text = p.Device ?? "Vazia", FontSize = 11, Foreground = PadUi.Res("MutedBrush"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            MaxHeight = 30, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        return new Border
        {
            Width = 128, Padding = new Thickness(6, 10, 6, 10), Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x10)), BorderThickness = new Thickness(mark is null ? 1 : 2),
            BorderBrush = mark is { } m ? new SolidColorBrush(m) : p.BehindHub ? PerfUi.B(PerfUi.Orange) : PadUi.Res("BorderBrush"),
            ToolTip = $"{p.Name}\n{p.KindText}\nVem de: {p.SourceText} ({p.ControllerName})" + (p.BehindHub ? "\nPassa por um hub: evite para o controle." : "")
                + (p.Device is null ? "" : $"\nLigado: {p.Device} ({UsbPorts.UseText(p.Use)})"),
            Child = st,
        };
    }
}

/// <summary>Aba "Controle competitivo": Básico, Avançado e Extreme (modo futebol e corrida), tudo com volta na Central Desfazer.</summary>
internal sealed class PadCompetitivePage : UserControl
{
    private readonly WrapPanel _checks = new();
    private readonly Grid _levels = new();
    private readonly TextBlock _status = PadUi.Muted("", 12.5);
    private CompetitiveLevel _picked;

    public PadCompetitivePage()
    {
        _picked = AppServices.Settings.PadCompetitiveLevel ?? CompetitiveLevel.Avancado;
        var root = PadUi.Root("Controle competitivo",
            "Tira tudo o que atrasa o controle no cabo e no Bluetooth: economia de energia, interferência e a leitura do programa. Feito para futebol e corrida (EA FC, eFootball, F1 e afins). Cada ajuste tem volta na Central Desfazer.", out var scroll);
        var head = PadUi.Head("O QUE ESTÁ EM VOLTA DO CONTROLE AGORA");
        head.Margin = new Thickness(0, 0, 0, 6);
        root.Children.Add(head);
        root.Children.Add(PadUi.Card(_checks));
        root.Children.Add(_levels);
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 6) };
        var apply = PadUi.Button("Aplicar o nível escolhido", primary: true);
        apply.Click += async (_, _) => await ApplyAsync();
        var undo = PadUi.Button("Desfazer tudo");
        undo.Click += async (_, _) => await UndoAsync();
        var analyzer = PadUi.Button("Medir antes e depois");
        analyzer.Click += (_, _) => MainWindow.Current?.Go("pad-analyzer");
        buttons.Children.Add(apply);
        buttons.Children.Add(undo);
        buttons.Children.Add(analyzer);
        root.Children.Add(buttons);
        root.Children.Add(_status);
        var tips = new StackPanel();
        tips.Children.Add(PadUi.Head("NO JOGO (O QUE MAIS TIRA ATRASO A 60 FPS)"));
        foreach (string t in PadCompetitive.GameTips) tips.Children.Add(PerfUi.Line(t, PerfUi.Yellow));
        var tipsCard = PadUi.Card(tips);
        tipsCard.Margin = new Thickness(0, 10, 0, 8);
        root.Children.Add(tipsCard);
        Content = scroll;
        Loaded += async (_, _) => { RenderLevels(); await LoadChecksAsync(); };
    }

    private async Task LoadChecksAsync()
    {
        _checks.Children.Clear();
        _checks.Children.Add(PadUi.Muted("Verificando...", 12.5));
        var (audio, wifi, radios, internalBt) = await Task.Run(() =>
        {
            IReadOnlyList<UsbPortInfo> ports;
            try { ports = WinUsbTopology.Read(); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or DllNotFoundException or EntryPointNotFoundException) { ports = Array.Empty<UsbPortInfo>(); }
            return (PadSystem.BluetoothAudioDevices(), PadSystem.WifiOn24GHz(), ports.Count(p => p.Use == UsbUse.BluetoothAdapter), PadSystem.InternalRadiosWhenDongle(ports).Count);
        });
        _checks.Children.Clear();
        var list = new StackPanel();
        list.Children.Add(PerfUi.Line(audio == 0 ? "Nenhum fone Bluetooth ligado junto com o controle." : $"{audio} fone(s) ou caixa(s) Bluetooth ligados: dividem o rádio com o controle.", audio == 0 ? PerfUi.Green : PerfUi.Orange));
        list.Children.Add(PerfUi.Line(wifi ? "Wi-Fi em 2,4 GHz (mesma faixa do Bluetooth): prefira a rede de 5 GHz." : "Wi-Fi fora da faixa de 2,4 GHz (ou cabo de rede).", wifi ? PerfUi.Orange : PerfUi.Green));
        list.Children.Add(PerfUi.Line(radios switch
        {
            0 => "Nenhum adaptador Bluetooth encontrado nas portas USB.",
            1 => "Um adaptador Bluetooth.",
            _ => internalBt > 0 ? $"{radios} adaptadores Bluetooth: o Avançado desliga o interno." : $"{radios} adaptadores Bluetooth.",
        }, radios > 1 ? PerfUi.Orange : PerfUi.Green));
        bool sleeping = await Task.Run(() => AppServices.Tweaks.FirstOrDefault(t => t.Id == "power-usb-suspend")?.IsApplied() == false);
        list.Children.Add(PerfUi.Line(sleeping ? "O Windows ainda pode pôr o USB para dormir." : "USB sem suspensão.", sleeping ? PerfUi.Orange : PerfUi.Green));
        _checks.Children.Add(list);
    }

    private void RenderLevels()
    {
        _levels.Children.Clear();
        _levels.ColumnDefinitions.Clear();
        var levels = new[] { (CompetitiveLevel.Basico, "Básico · seguro"), (CompetitiveLevel.Avancado, "Avançado · recomendado"), (CompetitiveLevel.Extreme, "Extreme") };
        for (int i = 0; i < levels.Length; i++)
        {
            if (i > 0) _levels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            _levels.ColumnDefinitions.Add(new ColumnDefinition());
            var (level, name) = levels[i];
            bool on = level == _picked;
            var st = new StackPanel();
            var title = new TextBlock { Text = name, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = on ? PerfUi.B(PerfUi.Yellow) : PadUi.Res("TextBrush") };
            st.Children.Add(title);
            foreach (string item in PadCompetitive.Items(level)) st.Children.Add(new TextBlock { Text = "• " + item, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
            if (AppServices.Settings.PadCompetitiveLevel == level)
                st.Children.Add(new TextBlock { Text = "APLICADO", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = PerfUi.B(PerfUi.Green), Margin = new Thickness(0, 8, 0, 0) });
            var card = new Border
            {
                Background = on ? new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xC4, 0x00)) : PadUi.Res("Surface2Brush"),
                BorderBrush = on ? PerfUi.B(PerfUi.Yellow) : PadUi.Res("BorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 12, 14, 12), Cursor = System.Windows.Input.Cursors.Hand, Child = st,
            };
            card.MouseLeftButtonUp += (_, _) => { _picked = level; RenderLevels(); };
            Grid.SetColumn(card, i * 2);
            _levels.Children.Add(card);
        }
    }

    private async Task ApplyAsync()
    {
        var ids = PadCompetitive.TweakIds(_picked);
        _status.Foreground = PadUi.Res("WarnBrush");
        _status.Text = "Verificando o que falta aplicar...";
        // o que já está aplicado fica de fora; o Bluetooth interno só sai se houver outro adaptador (sem ele o ajuste não tem alvo)
        var tweaks = await Task.Run(() => AppServices.Tweaks.Where(t => ids.Contains(t.Id))
            .Select(t => (Tweak: t, State: t.IsApplied()))
            .Where(x => x.State != true && !(x.Tweak.Id == PadCompetitive.InternalBluetoothOff && x.State is null))
            .Select(x => x.Tweak).ToList());
        if (_picked == CompetitiveLevel.Extreme && tweaks.Any(t => t.Id == PadCompetitive.UsbMsi)
            && MessageBox.Show("O Extreme liga o modo MSI nos controladores USB. Se algum USB parar de funcionar, desfaça pela Central Desfazer (no Modo de Segurança, se preciso).\n\nCrie um ponto de restauração antes. Continuar?",
                "Controle competitivo", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        _status.Foreground = PadUi.Res("WarnBrush");
        _status.Text = "Aplicando...";
        var results = await Task.Run(() => OperationQueue.Shared.Run(tweaks.Select(t => (IOperation)new TweakOperation(t, apply: true)).ToList()));
        var (direct, high) = PadCompetitive.ProgramOptions(_picked);
        AppServices.Settings.PadHighPriorityRead = direct;
        AppServices.Settings.PadTimerResolution = high;
        AppServices.Settings.PadCompetitiveLevel = _picked;
        AppServices.Settings.Save();
        int failed = results.Count(r => r.State == OperationState.Failed);
        bool restart = results.Any(r => r.State == OperationState.RebootRequired);
        _status.Foreground = PadUi.Res(failed > 0 ? "BadBrush" : "OkBrush");
        _status.Text = (failed > 0 ? $"{failed} ajuste(s) não foram aplicados: {string.Join("; ", results.Where(r => r.State == OperationState.Failed).Select(r => r.Message))}" : "Pronto.")
            + (restart ? " Reinicie o PC para valer tudo." : "") + " Meça de novo no Analisador para ver a diferença.";
        RenderLevels();
        await LoadChecksAsync();
    }

    private async Task UndoAsync()
    {
        var ids = PadCompetitive.TweakIds(CompetitiveLevel.Extreme).Where(id => id is not ("power-usb-suspend" or "game-mmcss" or "game-sysresponsiveness")).ToList();
        _status.Text = "Desfazendo...";
        var tweaks = await Task.Run(() => AppServices.Tweaks.Where(t => ids.Contains(t.Id) && t.IsApplied() == true).ToList());
        await Task.Run(() => OperationQueue.Shared.Run(tweaks.Select(t => (IOperation)new TweakOperation(t, apply: false)).ToList()));
        AppServices.Settings.PadHighPriorityRead = false;
        AppServices.Settings.PadTimerResolution = false;
        AppServices.Settings.PadCompetitiveLevel = null;
        AppServices.Settings.Save();
        _status.Foreground = PadUi.Res("OkBrush");
        _status.Text = "Desfeito. A suspensão do USB e a prioridade de jogos ficam como estão (são ajustes gerais; desfaça pela Central Desfazer se quiser).";
        RenderLevels();
        await LoadChecksAsync();
    }
}

/// <summary>
/// Aba "Overclock 1000 Hz": só DualSense (PS5) e DualShock 4 (PS4) no cabo, já configurado em 1000 Hz. O driver hidusbf é baixado do
/// repositório oficial do autor; a taxa é escolhida na janela dele (o programa mostra a linha do controle) e confirmada medindo.
/// </summary>
internal sealed class PadOverclockPage : UserControl
{
    private static readonly string ToolDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MBExtreme", "Tools", "hidusbf");
    private readonly StackPanel _pads = new();
    private readonly StackPanel _checks = new();
    private readonly TextBlock _status = PadUi.Muted("", 12.5);

    public PadOverclockPage()
    {
        var root = PadUi.Root("Overclock 1000 Hz",
            "Só para o DualSense (PS5) e o DualShock 4 (PS4), no cabo USB: de 250 Hz (um comando a cada 4 ms) para 1000 Hz (a cada 1 ms). Já vem configurado em 1000 Hz para esses dois controles. No Bluetooth nada muda.", out var scroll);
        var steps = new StackPanel();
        steps.Children.Add(PadUi.Head("PASSO A PASSO"));
        steps.Children.Add(PerfUi.Line("1. Instalar o driver: o programa baixa o hidusbf do repositório oficial do autor e abre a janela dele.", PerfUi.Yellow));
        steps.Children.Add(PerfUi.Line("2. Na janela do driver: escolha a linha do controle (mostrada abaixo), marque Filter On Device, escolha 1000 em Rate, clique Install Service (só na primeira vez) e Restart.", PerfUi.Yellow));
        steps.Children.Add(PerfUi.Line("3. Medir: o programa confere se chegou a 1000 Hz e guarda.", PerfUi.Yellow));
        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var install = PadUi.Button("Instalar driver e abrir a janela", primary: true);
        install.Click += async (_, _) => await InstallAsync();
        var measure = PadUi.Button("Medir agora");
        measure.Click += async (_, _) => await MeasureAsync();
        var undo = PadUi.Button("Voltar para 250 Hz (desfazer)");
        undo.Click += (_, _) => Undo();
        var site = PadUi.Button("Site oficial do driver");
        site.Click += (_, _) => PadUi.Open(PadOverclock.RepositoryUrl);
        buttons.Children.Add(install);
        buttons.Children.Add(measure);
        buttons.Children.Add(undo);
        buttons.Children.Add(site);
        steps.Children.Add(buttons);
        steps.Children.Add(_status);
        root.Children.Add(PadUi.Columns((new StackPanel { Children = { PadUi.Card(_pads), PadUi.Card(steps) } }, 1.15), (PadUi.Card(_checks), 1)));
        Content = scroll;
        Loaded += (_, _) => Load();
    }

    private static IReadOnlyList<GameController> SupportedPads()
    {
        try { return WinHid.FindGameControllers().Where(p => PadOverclock.IsSupported(p.Vendor, p.Product)).ToList(); }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or DllNotFoundException) { return Array.Empty<GameController>(); }
    }

    private async void Load()
    {
        var pads = await Task.Run(() => SupportedPads());
        var (driver, hvci) = await Task.Run(() => (PadSystem.OverclockDriverInstalled(), PadSystem.MemoryIntegrityOn()));
        _pads.Children.Clear();
        _pads.Children.Add(PadUi.Head("SÓ DUALSENSE E DUALSHOCK 4, NO CABO"));
        foreach (ushort product in new ushort[] { 0x0CE6, 0x09CC })
        {
            var connected = pads.FirstOrDefault(p => p.Product == product || (product == 0x09CC && p.Product == 0x05C4));
            string key = (connected?.Product ?? product).ToString("X4");
            double? measured = AppServices.Settings.PadOverclockMeasured.TryGetValue(key, out var hz) ? hz : null;
            var state = PadOverclock.StateFrom(true, driver, measured);
            var st = new StackPanel();
            st.Children.Add(new TextBlock { Text = PadOverclock.ModelName(product), FontSize = 14, FontWeight = FontWeights.SemiBold });
            st.Children.Add(PadUi.Muted(connected is null ? "Não está ligado agora." : $"Ligado · {connected.ConnectionText}" + (connected.Connection == PadConnection.Bluetooth ? " (o overclock vale só no cabo)" : ""), 12));
            st.Children.Add(PadUi.Muted($"Configurado: {AppServices.Settings.PadOverclockHz} Hz · linha na janela do driver: {PadOverclock.SetupLine(connected?.Product ?? product)}", 11.5));
            st.Children.Add(new TextBlock
            {
                Text = PadOverclock.StateText(state, measured), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
                Foreground = PerfUi.B(state == OverclockState.Active ? PerfUi.Green : state == OverclockState.Partial ? PerfUi.Orange : PerfUi.Yellow),
            });
            var card = new Border
            {
                Background = PadUi.Res("Surface2Brush"), BorderBrush = state == OverclockState.Active ? PerfUi.B(PerfUi.Green) : PadUi.Res("BorderBrush"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 8, 0, 0), Child = st,
            };
            _pads.Children.Add(card);
        }
        _pads.Children.Add(PadUi.Muted("Controle Xbox, DualSense Edge e os outros não entram: usam outro driver ou não foram testados com este método.", 11.5));

        _checks.Children.Clear();
        _checks.Children.Add(PadUi.Head("VERIFICAÇÃO ANTES DE ATIVAR"));
        bool usb = pads.Any(p => p.Connection == PadConnection.Usb);
        foreach (var (ok, text) in PadOverclock.Checks(driver, hvci, usb))
            _checks.Children.Add(PerfUi.Line(text, ok ? PerfUi.Green : PerfUi.Orange));
        _checks.Children.Add(PadUi.Muted("O driver é gratuito e do autor (LordOfMice); o MB Extreme não inclui o driver, só baixa do repositório oficial quando você pede.", 11.5));
    }

    private async Task InstallAsync()
    {
        if (MessageBox.Show("O programa vai baixar o driver hidusbf do repositório oficial do autor (github.com/LordOfMice/hidusbf) e abrir a janela dele.\n\n" +
                "É um driver de filtro de terceiros: depois de ativar, abra seus jogos uma vez para conferir se o anti-cheat aceita. Para desfazer, use o botão Voltar para 250 Hz.\n\nContinuar?",
                "Overclock 1000 Hz", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            string? setup = FindSetup();
            if (setup is null)
            {
                _status.Foreground = PadUi.Res("WarnBrush");
                _status.Text = "Baixando o driver...";
                Directory.CreateDirectory(ToolDir);
                string zip = System.IO.Path.Combine(ToolDir, "hidusbf.zip");
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) })
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("MBExtreme");
                    var bytes = await http.GetByteArrayAsync(PadOverclock.DownloadUrl);
                    await File.WriteAllBytesAsync(zip, bytes);
                }
                await Task.Run(() => ZipFile.ExtractToDirectory(zip, ToolDir, overwriteFiles: true));
                setup = FindSetup();
            }
            if (setup is null)
            {
                _status.Foreground = PadUi.Res("BadBrush");
                _status.Text = "O pacote baixado não tem o Setup.exe. Abra o site oficial do driver e baixe manualmente.";
                return;
            }
            OpenSetup(setup);
            _status.Foreground = PadUi.Res("MutedBrush");
            _status.Text = "Na janela do driver: escolha a linha do controle, marque Filter On Device, escolha 1000 em Rate, Install Service e Restart. Depois clique em Medir agora.";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException or UnauthorizedAccessException)
        {
            _status.Foreground = PadUi.Res("BadBrush");
            _status.Text = "Não foi possível baixar ou abrir o driver: " + ex.Message;
        }
    }

    private static string? FindSetup() =>
        Directory.Exists(ToolDir) ? Directory.EnumerateFiles(ToolDir, "setup.exe", SearchOption.AllDirectories).OrderBy(p => p.Length).FirstOrDefault() : null;

    private void OpenSetup(string setup)
    {
        try { Process.Start(new ProcessStartInfo(setup) { UseShellExecute = true, Verb = "runas", WorkingDirectory = System.IO.Path.GetDirectoryName(setup)! }); }
        catch (System.ComponentModel.Win32Exception) { _status.Text = "A janela do driver precisa de permissão de administrador."; }
    }

    private async Task MeasureAsync()
    {
        var pad = (await Task.Run(() => SupportedPads())).FirstOrDefault(p => p.Connection == PadConnection.Usb && p.Path is not null);
        if (pad is null)
        {
            _status.Foreground = PadUi.Res("WarnBrush");
            _status.Text = "Ligue o DualSense ou o DualShock 4 no cabo USB e tente de novo.";
            return;
        }
        _status.Foreground = PadUi.Res("WarnBrush");
        _status.Text = "Mexa o analógico em círculos sem parar por 5 segundos...";
        var report = await Task.Run(() => PadLatencyReport.From(PadLink.Usb, WinHid.CaptureReports(pad.Path!, pad.InputLength, 5000)));
        if (report is null)
        {
            _status.Text = "Chegaram poucos comandos. Mexa o analógico o tempo todo e tente de novo.";
            return;
        }
        AppServices.Settings.PadOverclockMeasured[pad.Product.ToString("X4")] = report.RateHz;
        AppServices.Settings.PadMeasurements[PadLatencyReport.RecordKey(pad.Product, PadLink.Usb)] = report.ToRecord(DateTime.Now);
        AppServices.Settings.Save();
        var state = PadOverclock.StateFrom(true, PadSystem.OverclockDriverInstalled(), report.RateHz);
        _status.Foreground = PadUi.Res(state == OverclockState.Active ? "OkBrush" : "WarnBrush");
        _status.Text = PadOverclock.StateText(state, report.RateHz);
        Load();
    }

    private void Undo()
    {
        string? setup = FindSetup();
        if (setup is null)
        {
            MessageBox.Show(PadSystem.OverclockDriverInstalled()
                    ? "Abra a janela do hidusbf (Setup.exe da pasta onde ele foi instalado) e clique em UnInstall Service. O controle volta para 250 Hz depois de religar o cabo."
                    : "O driver não está instalado: o controle já está na taxa de fábrica (250 Hz).",
                "Overclock 1000 Hz", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        MessageBox.Show("Na janela do driver: escolha a linha do controle, desmarque Filter On Device e clique Restart (volta para 250 Hz). Para tirar o driver de vez, clique em UnInstall Service.",
            "Overclock 1000 Hz", MessageBoxButton.OK, MessageBoxImage.Information);
        OpenSetup(setup);
        foreach (string key in AppServices.Settings.PadOverclockMeasured.Keys.ToList()) AppServices.Settings.PadOverclockMeasured.Remove(key);
        AppServices.Settings.Save();
    }
}
