using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MBExtreme.Core.Fans;

namespace MBExtreme.UI.Pages;

/// <summary>Ventoinhas e refrigeração: lê as ventoinhas da placa-mãe, da placa de vídeo e dos controladores compatíveis e controla a velocidade por temperatura.</summary>
public sealed class FansPage : ToolPageBase
{
    private sealed class Row
    {
        public required FanChannel Channel;
        public required TextBlock Live;
        public required StackPanel Settings;
    }

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly StackPanel _list = new();
    private readonly List<Row> _rows = new();
    private readonly StackPanel _gpuLimits = new();
    private IReadOnlyList<TemperatureSource> _temps = Array.Empty<TemperatureSource>();
    private bool _busy;

    public FansPage() : base("Ventoinhas e refrigeração",
        "Veja a velocidade de cada ventoinha (placa-mãe, placa de vídeo, cooler e bomba de líquido) e controle por temperatura: automático, manual ou curva. O que o seu hardware não deixa controlar aparece só para leitura.")
    {
        AddButton("Atualizar", async (_, _) => await LoadAsync());
        AddButton("Tudo em automático (BIOS)", (_, _) =>
        {
            AppServices.Fans.ReleaseAll();
            SetStatus("Todas as ventoinhas voltaram ao controle automático da placa.", "OkBrush");
            _ = LoadAsync();
        }, false);

        Body.Children.Add(CardOf(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("WarnBrush"),
            FontSize = 12,
            Text = "Segurança: o controle vale enquanto o MB Extreme estiver aberto; ao fechar, as ventoinhas voltam ao automático. Por proteção, a bomba de líquido nunca fica abaixo de 60%, temperaturas acima de 80 °C forçam 80% e acima de 90 °C forçam 100%, e se a temperatura de referência não puder ser lida a ventoinha vai a 100%. Se o programa for encerrado à força, a velocidade pode ficar fixa até você reiniciar o PC.",
        }));
        Body.Children.Add(_list);
        Body.Children.Add(_gpuLimits);
        Body.Children.Add(Muted("Dica: para a placa-mãe aceitar o controle, deixe o conector da ventoinha em modo PWM (ou \"Auto\") na BIOS e desligue o modo \"Full Speed\". Alguns chips (principalmente de placas recentes) bloqueiam o controle por programas; nesse caso a ventoinha fica só para leitura. O ajuste da curva de clock da placa de vídeo (overclock/undervolt) não faz parte desta versão.", 11));

        _timer.Tick += async (_, _) => await RefreshLiveAsync();
        Loaded += async (_, _) =>
        {
            await LoadAsync();
            _ = LoadGpuLimitsAsync();
            _timer.Start();
        };
        Unloaded += (_, _) => _timer.Stop();
    }

    private async Task LoadAsync()
    {
        _list.Children.Clear();
        _rows.Clear();
        _list.Children.Add(Muted("Lendo as ventoinhas..."));
        await AppServices.Fans.EnsureStartedAsync();
        var service = AppServices.Fans.Service;
        if (service is null)
        {
            _list.Children.Clear();
            _list.Children.Add(Row("Sensores indisponíveis", (AppServices.Fans.Error ?? "Execute o MB Extreme como administrador.") + " Em alguns PCs é preciso instalar o driver PawnIO (veja o Início).", null, "WarnBrush"));
            return;
        }

        var channels = await Task.Run(service.Channels);
        _temps = await Task.Run(service.Temperatures);
        _list.Children.Clear();
        if (channels.Count == 0)
        {
            _list.Children.Add(Row("Nenhuma ventoinha encontrada", "Este PC não expõe ventoinhas para leitura. Em placas recentes, instale o driver PawnIO e abra o programa como administrador.", null, "WarnBrush"));
            return;
        }

        foreach (var channel in channels.OrderBy(c => c.HardwareName).ThenBy(c => c.Name)) _list.Children.Add(BuildChannel(channel));
    }

    // ---- limites da placa NVIDIA (potência e clock máximo)

    private async Task LoadGpuLimitsAsync()
    {
        _gpuLimits.Children.Clear();
        string? exe = await Task.Run(NvidiaSmi.Locate);
        if (exe is null) return;   // sem placa NVIDIA ou sem o nvidia-smi do driver
        var gpus = await Task.Run(() => NvidiaSmi.Query(exe));
        if (gpus.Count == 0) return;

        _gpuLimits.Children.Add(Heading("Placa NVIDIA: limites de potência e clock"));
        _gpuLimits.Children.Add(Muted("Reduz o calor e o consumo limitando a potência e o clock máximo. Não faz overclock: os limites nunca passam do padrão da placa. Volta ao padrão ao reiniciar o PC ou em \"Restaurar padrão\". Exige administrador.", 11));
        foreach (var gpu in gpus) _gpuLimits.Children.Add(await BuildGpuLimitAsync(exe, gpu));
    }

    private async Task<UIElement> BuildGpuLimitAsync(string exe, GpuLimits gpu)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = gpu.Name, FontSize = 14, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(Muted($"Potência atual {gpu.PowerLimitW?.ToString("0") ?? "—"} W (padrão {gpu.PowerDefaultW?.ToString("0") ?? "—"} W) · clock máximo {gpu.MaxGraphicsClockMhz?.ToString("0") ?? "—"} MHz · {gpu.TemperatureC?.ToString("0") ?? "—"} °C", 11));

        double? chosenPower = null;
        if (NvidiaSmi.PowerRange(gpu) is { } range)
        {
            var label = new TextBlock { Text = $"{gpu.PowerLimitW ?? range.Max:0} W", Width = 60, VerticalAlignment = VerticalAlignment.Center };
            var slider = new Slider { Minimum = range.Min, Maximum = range.Max, Value = Math.Clamp(gpu.PowerLimitW ?? range.Max, range.Min, range.Max), Width = 240, IsSnapToTickEnabled = true, TickFrequency = 5, VerticalAlignment = VerticalAlignment.Center };
            slider.ValueChanged += (_, e) => { label.Text = $"{e.NewValue:0} W"; chosenPower = e.NewValue; };
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            line.Children.Add(new TextBlock { Text = "Limite de potência", Width = 130, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
            line.Children.Add(slider);
            line.Children.Add(label);
            panel.Children.Add(line);
        }
        else panel.Children.Add(Muted("Esta placa não permite alterar o limite de potência.", 11));

        int? chosenClock = null;
        var clocks = await Task.Run(() => NvidiaSmi.SupportedClocks(exe, gpu.Index));
        if (clocks.Count > 1)
        {
            int current = clocks.OrderBy(c => Math.Abs(c - (gpu.MaxGraphicsClockMhz ?? clocks[^1]))).First();
            var label = new TextBlock { Text = $"{current} MHz", Width = 70, VerticalAlignment = VerticalAlignment.Center };
            var slider = new Slider { Minimum = 0, Maximum = clocks.Count - 1, Value = clocks.ToList().IndexOf(current), Width = 240, IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center };
            slider.ValueChanged += (_, e) => { chosenClock = clocks[(int)e.NewValue]; label.Text = $"{chosenClock} MHz"; };
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            line.Children.Add(new TextBlock { Text = "Clock máximo", Width = 130, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
            line.Children.Add(slider);
            line.Children.Add(label);
            panel.Children.Add(line);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var apply = SmallButton("Aplicar limites", async (_, _) =>
        {
            var messages = new List<string>();
            bool ok = true;
            if (chosenPower is { } w && NvidiaSmi.PowerRange(gpu) is { } r && w >= r.Min && w <= r.Max)
            {
                var (code, output) = await Task.Run(() => NvidiaSmi.Run(exe, NvidiaSmi.PowerArguments(gpu.Index, w)));
                ok &= code == 0;
                messages.Add(code == 0 ? $"potência {w:0} W" : "potência: " + output);
            }
            if (chosenClock is { } c)
            {
                var (code, output) = await Task.Run(() => NvidiaSmi.Run(exe, NvidiaSmi.ClockArguments(gpu.Index, clocks[0], c)));
                ok &= code == 0;
                messages.Add(code == 0 ? $"clock máx. {c} MHz" : "clock: " + output);
            }
            SetStatus(messages.Count == 0 ? "Nada a alterar." : (ok ? "Aplicado: " : "Falhou (execute como administrador): ") + string.Join(" · ", messages), ok ? "OkBrush" : "BadBrush");
        }, true);
        apply.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(apply);
        buttons.Children.Add(SmallButton("Restaurar padrão", async (_, _) =>
        {
            var (c1, o1) = await Task.Run(() => NvidiaSmi.Run(exe, NvidiaSmi.ResetClockArguments(gpu.Index)));
            int ok2 = 0;
            if (gpu.PowerDefaultW is { } def) { var (c2, _) = await Task.Run(() => NvidiaSmi.Run(exe, NvidiaSmi.PowerArguments(gpu.Index, def))); ok2 = c2; }
            SetStatus(c1 == 0 && ok2 == 0 ? "Padrão restaurado." : "Não foi possível restaurar tudo (execute como administrador). " + o1, c1 == 0 && ok2 == 0 ? "OkBrush" : "BadBrush");
            await LoadGpuLimitsAsync();
        }));
        panel.Children.Add(buttons);
        return CardOf(panel);
    }

    private UIElement BuildChannel(FanChannel channel)
    {
        var panel = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel();
        title.Children.Add(new TextBlock { Text = channel.Name, FontSize = 14, FontWeight = FontWeights.SemiBold });
        title.Children.Add(Muted($"{channel.HardwareName} · {Kind(channel.Kind)}", 11));
        head.Children.Add(title);
        var live = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(live, 1);
        head.Children.Add(live);
        panel.Children.Add(head);

        var settings = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        panel.Children.Add(settings);
        var row = new Row { Channel = channel, Live = live, Settings = settings };
        _rows.Add(row);
        live.Text = LiveText(channel);

        if (!channel.Controllable)
        {
            settings.Children.Add(Muted("Somente leitura: esta ventoinha não aceita controle por software.", 11));
            return CardOf(panel);
        }

        BuildControls(row);
        return CardOf(panel);
    }

    private static string Kind(FanChannelKind kind) => kind switch { FanChannelKind.Cpu => "cooler do processador", FanChannelKind.Gpu => "placa de vídeo", FanChannelKind.Pump => "bomba de líquido", _ => "gabinete" };

    private static string LiveText(FanChannel c) =>
        (c.Rpm is { } rpm ? $"{rpm:0} RPM" : "— RPM") + (c.Percent is { } p ? $" · {p:0}%" : "");

    private void BuildControls(Row row)
    {
        var channel = row.Channel;
        var cfg = AppServices.Fans.Profiles.Get(channel.ControlId, channel.Kind);
        cfg.Kind = channel.Kind;
        row.Settings.Children.Clear();

        void Save()
        {
            AppServices.Fans.Profiles.Save(cfg);
            _ = AppServices.Fans.EnsureStartedAsync();
        }

        string[] modes = { "auto", "manual", "curve" };
        row.Settings.Children.Add(Chips(new[] { "Automático (BIOS)", "Manual", "Curva por temperatura" }, Math.Max(0, Array.IndexOf(modes, cfg.Mode)), i =>
        {
            cfg.Mode = modes[i];
            if (cfg.Mode == "curve" && cfg.SourceSensorId is null) cfg.SourceSensorId = DefaultSource(channel.Kind);
            Save();
            BuildControls(row);
        }));

        if (cfg.Mode == "manual")
        {
            double min = channel.Kind == FanChannelKind.Pump ? FanSafety.PumpMinimum : 0;
            var value = new TextBlock { Text = $"{Math.Max(cfg.ManualPercent, min):0}%", Width = 48, VerticalAlignment = VerticalAlignment.Center };
            var slider = new Slider { Minimum = min, Maximum = 100, Value = Math.Max(cfg.ManualPercent, min), TickFrequency = 5, IsSnapToTickEnabled = true, Width = 260, VerticalAlignment = VerticalAlignment.Center };
            slider.ValueChanged += (_, e) => { value.Text = $"{e.NewValue:0}%"; cfg.ManualPercent = e.NewValue; };
            slider.PreviewMouseLeftButtonUp += (_, _) => Save();
            slider.KeyUp += (_, _) => Save();
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            line.Children.Add(slider);
            line.Children.Add(value);
            row.Settings.Children.Add(line);
            row.Settings.Children.Add(Muted(channel.Kind == FanChannelKind.Pump ? "A bomba nunca fica abaixo de 60%." : "Mesmo no manual, temperaturas altas aceleram a ventoinha por segurança.", 11));
        }
        else if (cfg.Mode == "curve")
        {
            row.Settings.Children.Add(Muted("Predefinição da curva:", 11));
            row.Settings.Children.Add(Chips(new[] { "Silenciosa", "Equilibrada", "Desempenho" }, -1, i =>
            {
                cfg.Curve = (i == 0 ? FanCurve.Silent : i == 1 ? FanCurve.Balanced : FanCurve.Performance).Points;
                Save();
                BuildControls(row);
            }));

            row.Settings.Children.Add(Muted("Temperatura de referência:", 11));
            var sources = _temps.OrderByDescending(t => t.Name.Contains("Package") || t.Name.Contains("GPU Core")).Take(8).ToList();
            row.Settings.Children.Add(Chips(sources.Select(s => s.Name.Length > 38 ? s.Name[..38] + "…" : s.Name).ToList(),
                Math.Max(0, sources.FindIndex(s => s.Id == cfg.SourceSensorId)), i =>
                {
                    cfg.SourceSensorId = sources[i].Id;
                    Save();
                }));

            var points = new StackPanel();
            var boxes = new List<(TextBox T, TextBox P)>();
            foreach (var p in FanCurve.Normalize(cfg.Curve))
            {
                var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                var t = new TextBox { Text = p.TemperatureC.ToString("0"), Width = 50 };
                var v = new TextBox { Text = p.Percent.ToString("0"), Width = 50, Margin = new Thickness(8, 0, 0, 0) };
                line.Children.Add(new TextBlock { Text = "a partir de", Width = 72, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
                line.Children.Add(t);
                line.Children.Add(new TextBlock { Text = " °C →", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0), FontSize = 12 });
                line.Children.Add(v);
                line.Children.Add(new TextBlock { Text = " %", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
                points.Children.Add(line);
                boxes.Add((t, v));
            }
            var apply = SmallButton("Aplicar pontos", (_, _) =>
            {
                var list = new List<CurvePoint>();
                foreach (var (t, v) in boxes)
                    if (double.TryParse(t.Text, out double tc) && double.TryParse(v.Text, out double pc)) list.Add(new CurvePoint(tc, pc));
                if (list.Count < 2) { SetStatus("A curva precisa de ao menos 2 pontos válidos.", "WarnBrush"); return; }
                cfg.Curve = list;
                Save();
                SetStatus("Curva aplicada.", "OkBrush");
                BuildControls(row);
            });
            apply.Margin = new Thickness(0, 6, 0, 0);
            apply.Padding = new Thickness(11, 4, 11, 4);
            apply.FontSize = 12;
            points.Children.Add(apply);
            row.Settings.Children.Add(new Expander { Header = "Editar os pontos da curva", Content = points, Foreground = Brush("TextBrush") });
            row.Settings.Children.Add(Muted("A ventoinha sobe na hora quando esquenta e desce aos poucos quando esfria, para não ficar oscilando.", 11));
        }
    }

    private string? DefaultSource(FanChannelKind kind)
    {
        var pick = kind == FanChannelKind.Gpu
            ? _temps.FirstOrDefault(t => t.Name.Contains("GPU Core", StringComparison.OrdinalIgnoreCase))
            : _temps.FirstOrDefault(t => t.Name.Contains("Package", StringComparison.OrdinalIgnoreCase)) ?? _temps.FirstOrDefault(t => t.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase));
        return (pick ?? _temps.FirstOrDefault())?.Id;
    }

    private WrapPanel Chips(IReadOnlyList<string> options, int selected, Action<int> onChange)
    {
        var panel = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
        var buttons = new List<Button>();
        void Paint(int index)
        {
            for (int i = 0; i < buttons.Count; i++) buttons[i].Style = (Style)FindResource(i == index ? "PrimaryButton" : "GhostButton");
            foreach (var b in buttons) { b.Padding = new Thickness(11, 4, 11, 4); b.FontSize = 12; }
        }
        for (int i = 0; i < options.Count; i++)
        {
            int index = i;
            var button = new Button { Content = options[i], Margin = new Thickness(0, 0, 6, 6) };
            button.Click += (_, _) => { Paint(index); onChange(index); };
            buttons.Add(button);
            panel.Children.Add(button);
        }
        Paint(selected);
        return panel;
    }

    private async Task RefreshLiveAsync()
    {
        if (_busy || AppServices.Fans.Service is not { } service || _rows.Count == 0) return;
        _busy = true;
        try
        {
            var channels = await Task.Run(service.Channels);
            foreach (var row in _rows)
                if (channels.FirstOrDefault(c => c.ControlId == row.Channel.ControlId) is { } fresh) row.Live.Text = LiveText(fresh);
            _temps = await Task.Run(service.Temperatures);
            var hottest = FanEngine.HottestCoreTemperature(_temps);
            SetStatus(hottest is { } t ? $"Temperatura mais alta (CPU/GPU): {t:0} °C" + (AppServices.Fans.Running ? " · controle ativo" : "") : "");
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException) { }
        finally
        {
            _busy = false;
        }
    }
}
