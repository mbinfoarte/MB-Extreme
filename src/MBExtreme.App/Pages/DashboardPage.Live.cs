using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MBExtreme.Core.Hardware;
using MBExtreme.Core.Memory;
using MBExtreme.Core.Sensors;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Cartões "Ao vivo" do Painel: processador (temperatura e uso, clock, tensão, consumo, núcleos e refrigeração), placa de vídeo
/// (temperatura e uso, clocks, tensão, consumo, ponto mais quente, memória de vídeo e ventoinhas) e memória (uso, temperatura, módulos).
/// </summary>
public partial class DashboardPage
{
    private readonly Dictionary<string, TextBlock> _tiles = new();
    private string _cpuFanKey = "", _gpuFanKey = "";
    private readonly Dictionary<string, (TextBlock Rpm, RotateTransform Spin, double Rpm0)> _fanRows = new();
    private HardwareInventory? _inventory;
    private IReadOnlyList<(string Name, double Celsius)> _memTemps = Array.Empty<(string, double)>();

    private void BuildLiveCards()
    {
        AddTiles(CpuStats, "cpu-clock", "Clock", "cpu-volt", "Tensão (Vcore)", "cpu-power", "Consumo");
        AddTiles(GpuStats, "gpu-clock", "Clock núcleo", "gpu-mem", "Clock memória", "gpu-volt", "Tensão", "gpu-power", "Consumo", "gpu-hot", "Ponto mais quente");
        AddTiles(RamStats, "ram-total", "Total", "ram-used", "Em uso", "ram-free", "Livre");
        CpuCoolingTitle.Text = "Refrigeração";
        GpuCoolingTitle.Text = "Ventoinhas da placa";
        _ = LoadInventoryAsync();
    }

    private void AddTiles(UniformGrid grid, params string[] pairs)
    {
        for (int i = 0; i + 1 < pairs.Length; i += 2)
        {
            var value = new TextBlock { Text = "—", FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            var st = new StackPanel();
            st.Children.Add(new TextBlock { Text = pairs[i + 1], FontSize = 10.5, Foreground = (Brush)FindResource("MutedBrush"), TextTrimming = TextTrimming.CharacterEllipsis });
            st.Children.Add(value);
            grid.Children.Add(new Border { Background = (Brush)FindResource("Surface2Brush"), CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 6, 9, 6), Margin = new Thickness(0, 0, 6, 6), Child = st });
            _tiles[pairs[i]] = value;
        }
    }

    private void Tile(string key, string? text) { if (_tiles.TryGetValue(key, out var t)) t.Text = string.IsNullOrEmpty(text) ? "—" : text; }

    private async Task LoadInventoryAsync()
    {
        try { _inventory = await AppServices.Hardware(); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Management.ManagementException or AggregateException) { return; }
        var hw = _inventory;
        CpuName.Text = $"{hw.Cpu.Name} · {hw.Cpu.Cores} núcleos / {hw.Cpu.Threads} threads";
        if (hw.Gpus.OrderByDescending(g => g.VramBytes).FirstOrDefault() is { } gpu)
            GpuName.Text = gpu.Name + (gpu.VramBytes > 0 ? $" · {gpu.VramBytes / BytesPerGb:0.#} GB" : "");
        ShowModules();
    }

    // ------------------------------------------------------------ processador e placa de vídeo

    private void UpdateLive(SensorSnapshot s)
    {
        var d = s.Detail;
        CpuUseGauge.Value = s.CpuLoad;
        GpuUseGauge.Value = s.GpuLoad;
        Tile("cpu-clock", s.CpuClockMhz is double cc ? $"{cc / 1000:0.00} GHz" : null);
        Tile("cpu-volt", d?.CpuVoltage is double cv ? $"{cv:0.000} V" : null);
        Tile("cpu-power", s.CpuPowerWatts is double cp ? $"{cp:0} W" : null);
        Tile("gpu-clock", s.GpuClockMhz is double gc ? $"{gc:0} MHz" : null);
        Tile("gpu-mem", d?.GpuMemoryClockMhz is double gm ? $"{gm:0} MHz" : null);
        Tile("gpu-volt", d?.GpuVoltage is double gv ? $"{gv:0.000} V" : null);
        Tile("gpu-power", s.GpuPowerWatts is double gp ? $"{gp:0} W" : null);
        Tile("gpu-hot", d?.GpuHotspot is double gh ? $"{gh:0} °C" : null);
        if (_inventory is null && d?.CpuName is { Length: > 0 } cn) CpuName.Text = cn;

        // memória de vídeo
        if (d?.GpuVramUsedMb is double used && d.GpuVramTotalMb is double total && total > 0)
        {
            VramText.Text = $"{used / 1024:0.0} de {total / 1024:0.#} GB";
            double width = ((FrameworkElement)VramBar.Parent).ActualWidth * Math.Clamp(used / total, 0, 1);
            Motion.To(VramBar, WidthProperty, Math.Max(0, width));
        }
        else VramText.Text = "sem leitura";

        ShowCores(d?.CoreLoads ?? Array.Empty<double>());

        // refrigeração: water cooler (bomba) ou cooler a ar; gabinete junto; placa de vídeo à parte
        var fans = FanGroups.Split(s.Fans);
        bool liquid = SensorPick.IsLiquid(s.Fans.Where(f => !f.IsGpu));
        CpuCoolingTitle.Text = fans.Cpu.Count + fans.Case.Count == 0 ? "Refrigeração · sem leitura das ventoinhas"
            : liquid ? "Refrigeração · water cooler" : "Refrigeração · cooler a ar";
        ShowFans(CpuCooling, ref _cpuFanKey, fans.Cpu.Concat(fans.Case.Take(4)).ToList(), "cpu");
        GpuCoolingTitle.Text = fans.Gpu.Count == 0 ? "Ventoinhas da placa · paradas ou sem leitura" : "Ventoinhas da placa";
        ShowFans(GpuCooling, ref _gpuFanKey, fans.Gpu, "gpu");

        if (d is not null && d.MemoryTemperatures.Count > 0) { _memTemps = d.MemoryTemperatures; ShowModules(); }
        RamTempGauge.Value = _memTemps.Count > 0 ? _memTemps.Max(t => t.Celsius) : null;
        RamTempLabel.Text = _memTemps.Count > 0 ? "Temperatura" : "Temperatura (sem sensor)";
    }

    private readonly List<(TextBlock Value, Border Bar)> _coreCells = new();
    private readonly Border _hogNotice = new() { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 4, 0, 4), Visibility = Visibility.Collapsed };
    private DateTime? _busySince;
    private DateTime _lastHogCheck;

    private static Color LoadColor(double v) =>
        v >= 90 ? Color.FromRgb(0xFF, 0x5A, 0x5F) : v >= 60 ? Color.FromRgb(0xFF, 0xA9, 0x4D) : v >= 30 ? Color.FromRgb(0xFF, 0xC4, 0x00) : Color.FromRgb(0x3D, 0xDC, 0x97);

    /// <summary>Um quadrinho por thread: núcleo e thread, a porcentagem em número e a barra na cor da carga.</summary>
    private void ShowCores(IReadOnlyList<double> loads)
    {
        if (loads.Count == 0)
        {
            CpuCoresTitle.Text = "Uso por núcleo: sem leitura (precisa abrir como administrador)";
            CpuCores.Children.Clear();
            _coreCells.Clear();
            return;
        }
        int cores = _inventory?.Cpu.Cores ?? 0;
        bool smt = cores > 0 && loads.Count == cores * 2;
        if (_coreCells.Count != loads.Count)
        {
            CpuCores.Children.Clear();
            _coreCells.Clear();
            CpuCores.Columns = loads.Count <= 8 ? loads.Count : loads.Count <= 16 ? 8 : loads.Count <= 24 ? 12 : 16;
            bool small = loads.Count > 16;
            for (int i = 0; i < loads.Count; i++)
            {
                string name = smt ? $"N{i / 2 + 1} · T{i % 2 + 1}" : $"T{i + 1}";
                var value = new TextBlock { FontSize = small ? 13 : 16, FontWeight = FontWeights.Bold };
                var bar = new Border { CornerRadius = new CornerRadius(3), Height = 4, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
                var track = new Grid { Height = 4, Margin = new Thickness(0, 3, 0, 0) };
                track.Children.Add(new Border { Background = (Brush)FindResource("BorderBrush"), CornerRadius = new CornerRadius(3) });
                track.Children.Add(bar);
                var st = new StackPanel();
                st.Children.Add(new TextBlock { Text = name, FontSize = 9.5, Foreground = (Brush)FindResource("MutedBrush") });
                st.Children.Add(value);
                st.Children.Add(track);
                var cell = new Border
                {
                    Background = (Brush)FindResource("Surface2Brush"), CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 5, 7, 6), Margin = new Thickness(0, 0, 5, 5),
                    Child = st,
                };
                track.SizeChanged += (_, e) => bar.Tag = e.NewSize.Width;
                CpuCores.Children.Add(cell);
                _coreCells.Add((value, bar));
            }
            if (_hogNotice.Parent is null && CpuCores.Parent is Panel host) host.Children.Insert(host.Children.IndexOf(CpuCores) + 1, _hogNotice);
        }
        double avg = loads.Average();
        int hi = 0, lo = 0;
        for (int i = 1; i < loads.Count; i++) { if (loads[i] > loads[hi]) hi = i; if (loads[i] < loads[lo]) lo = i; }
        string label(int i) => smt ? $"N{i / 2 + 1}" : $"T{i + 1}";
        CpuCoresTitle.Text = (cores > 0 && cores != loads.Count ? $"Uso por núcleo ({cores} núcleos, {loads.Count} threads)" : $"Uso por núcleo ({loads.Count})")
            + $" · média {avg:0}% · mais alto {label(hi)} {loads[hi]:0}% · mais livre {label(lo)} {loads[lo]:0}%";
        for (int i = 0; i < loads.Count; i++)
        {
            double v = Math.Clamp(loads[i], 0, 100);
            var (text, bar) = _coreCells[i];
            var brush = new SolidColorBrush(LoadColor(v));
            text.Text = $"{v:0}%";
            text.Foreground = brush;
            bar.Background = brush;
            bar.Width = bar.Tag is double w ? Math.Max(2, w * v / 100) : 0;
        }
        WatchBusyCpu(avg);
    }

    /// <summary>Processador acima de 90% por 3 minutos sem jogo aberto: mostra quem está usando (atualizadores, por exemplo).</summary>
    private void WatchBusyCpu(double avg)
    {
        bool gaming = AppServices.Boost.IsActive;
        if (avg < 90 || gaming) { _busySince = null; _hogNotice.Visibility = Visibility.Collapsed; return; }
        _busySince ??= DateTime.Now;
        if (DateTime.Now - _busySince < TimeSpan.FromMinutes(3) || DateTime.Now - _lastHogCheck < TimeSpan.FromSeconds(30)) return;
        _lastHogCheck = DateTime.Now;
        _ = Task.Run(() => CpuHogs.Top(3)).ContinueWith(t =>
        {
            if (t.Result.Count == 0) return;
            var row = new DockPanel();
            var go = new Button { Content = "Ver processos", Style = (Style)FindResource("GhostButton"), Padding = new Thickness(10, 3, 10, 3), FontSize = 11.5, Margin = new Thickness(8, 0, 0, 0) };
            go.Click += (_, _) => MainWindow.Current?.Go("hub-startup");
            DockPanel.SetDock(go, Dock.Right);
            row.Children.Add(go);
            row.Children.Add(new TextBlock
            {
                Text = $"⚠ Processador acima de 90% há {(DateTime.Now - _busySince!.Value).TotalMinutes:0} min sem jogo aberto. Quem está usando: " + string.Join(", ", t.Result.Select(h => $"{h.Name} {h.Percent:0}%")),
                FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
            });
            _hogNotice.Child = row;
            _hogNotice.Background = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0x5A, 0x5F));
            _hogNotice.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0x5A, 0x5F));
            _hogNotice.BorderThickness = new Thickness(1);
            _hogNotice.Visibility = Visibility.Visible;
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Linhas de ventoinha/bomba com o ícone girando na velocidade real (mais RPM, gira mais rápido).</summary>
    private void ShowFans(StackPanel panel, ref string key, IReadOnlyList<FanReading> fans, string prefix)
    {
        string newKey = string.Join("|", fans.Select(f => f.Name));
        if (newKey != key)
        {
            key = newKey;
            panel.Children.Clear();
            foreach (var old in _fanRows.Keys.Where(k => k.StartsWith(prefix + ":")).ToList()) _fanRows.Remove(old);
            foreach (var f in fans)
            {
                bool pump = f.Name.Contains("pump", StringComparison.OrdinalIgnoreCase);
                var spin = new RotateTransform(0);
                // ventoinha desenhada; a bomba do water cooler em azul
                var icon = GlassFx.Fan(pump ? Color.FromRgb(0x6C, 0xB6, 0xFF) : Color.FromRgb(0xD4, 0xD4, 0xD8), 20);
                icon.RenderTransform = spin;
                icon.VerticalAlignment = VerticalAlignment.Center;
                var rpm = new TextBlock { FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
                var row = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
                DockPanel.SetDock(icon, Dock.Left);
                DockPanel.SetDock(rpm, Dock.Right);
                row.Children.Add(icon);
                row.Children.Add(rpm);
                row.Children.Add(new TextBlock
                {
                    Text = pump ? "Bomba do water cooler" : FanGroups.Label(f), FontSize = 12, Margin = new Thickness(10, 0, 8, 0),
                    Foreground = (Brush)FindResource("MutedBrush"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                });
                panel.Children.Add(row);
                _fanRows[prefix + ":" + f.Name] = (rpm, spin, -1);
            }
        }
        foreach (var f in fans)
        {
            if (!_fanRows.TryGetValue(prefix + ":" + f.Name, out var row)) continue;
            row.Rpm.Text = $"{f.Rpm:N0} RPM".Replace(',', '.');
            // só muda a velocidade do giro quando a rotação muda mais de 10% (evita reiniciar a animação a cada segundo)
            if (row.Rpm0 > 0 && Math.Abs(f.Rpm - row.Rpm0) / row.Rpm0 < 0.1) continue;
            _fanRows[prefix + ":" + f.Name] = (row.Rpm, row.Spin, f.Rpm);
            if (!Motion.Enabled || f.Rpm <= 0) { row.Spin.BeginAnimation(RotateTransform.AngleProperty, null); continue; }
            double seconds = Math.Clamp(60.0 / f.Rpm * 12, 0.25, 4);
            row.Spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(seconds))) { RepeatBehavior = RepeatBehavior.Forever });
        }
    }

    // ------------------------------------------------------------ memória

    private void UpdateRamTiles(MemoryStatus memory)
    {
        Tile("ram-total", memory.TotalBytes > 0 ? $"{memory.TotalBytes / BytesPerGb:0.#} GB" : null);
        Tile("ram-used", memory.TotalBytes > 0 ? $"{memory.UsedBytes / BytesPerGb:0.0} GB" : null);
        Tile("ram-free", memory.TotalBytes > 0 ? $"{memory.AvailableBytes / BytesPerGb:0.0} GB" : null);
    }

    /// <summary>Um cartão por pente: slot, modelo, tamanho, tipo e velocidade, e a temperatura quando o pente tem sensor.</summary>
    private void ShowModules()
    {
        if (_inventory is not { } hw) return;
        var mods = hw.Memory;
        if (mods.Count > 0)
        {
            var first = mods[0];
            string brand = MemoryBrand(first);
            int mhz = first.ConfiguredMhz > 0 ? first.ConfiguredMhz : first.RatedMhz;
            RamName.Text = Join(brand, first.Type, mhz > 0 ? mhz + " MHz" : null) + $" · {mods.Count} {(mods.Count == 1 ? "módulo" : "módulos")}";
        }
        RamModulesTitle.Text = mods.Count == 0 ? "Módulos: sem leitura" : $"Módulos instalados ({mods.Count})";
        RamModules.Children.Clear();
        for (int i = 0; i < mods.Count; i++)
        {
            var m = mods[i];
            int mhz = m.ConfiguredMhz > 0 ? m.ConfiguredMhz : m.RatedMhz;
            double? temp = i < _memTemps.Count ? _memTemps[i].Celsius : null;
            var grid = new DockPanel();
            var slot = new TextBlock { Text = string.IsNullOrWhiteSpace(m.Slot) ? $"#{i + 1}" : m.Slot, Width = 64, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("MutedBrush"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var tempText = new TextBlock
            {
                Text = temp is double t ? $"{t:0} °C" : "", FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource(temp is >= 75 ? "BadBrush" : temp is >= 60 ? "WarnBrush" : "OkBrush"),
            };
            DockPanel.SetDock(slot, Dock.Left);
            DockPanel.SetDock(tempText, Dock.Right);
            grid.Children.Add(slot);
            grid.Children.Add(tempText);
            var text = new StackPanel { Margin = new Thickness(6, 0, 6, 0) };
            text.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(m.PartNumber) ? MemoryBrand(m) : m.PartNumber.Trim(), FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = Join($"{m.CapacityBytes / BytesPerGb:0.#} GB", m.Type, mhz > 0 ? mhz + " MHz" : null, MemoryBrand(m)), FontSize = 11, Foreground = (Brush)FindResource("MutedBrush"), TextTrimming = TextTrimming.CharacterEllipsis });
            grid.Children.Add(text);
            var card = new Border { Background = (Brush)FindResource("Surface2Brush"), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 0, 5), Child = grid };
            RamModules.Children.Add(card);
        }
    }

    private static string MemoryBrand(MemoryModule m)
    {
        string brand = (m.Manufacturer ?? "").Trim();
        // alguns PCs informam o código JEDEC em vez do nome
        return brand switch
        {
            "" or "Unknown" or "Undefined" => "",
            "04CD" or "Kingston" or "0198" => "Kingston",
            "859B" or "Crucial" => "Crucial",
            "CE00" or "80CE" => "Samsung",
            "AD00" or "80AD" => "SK hynix",
            "2C00" or "802C" => "Micron",
            "029E" or "Corsair" => "Corsair",
            "04CB" => "ADATA",
            "0B6F" => "G.Skill",
            _ => brand,
        };
    }
}
