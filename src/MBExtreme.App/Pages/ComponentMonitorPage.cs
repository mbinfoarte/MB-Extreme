using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using MBExtreme.Core.Hardware;
using MBExtreme.Core.Sensors;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>Processador ou placa de vídeo em tempo real: nome, núcleos, e gráficos de utilização, temperatura, velocidade e energia.</summary>
public sealed class ComponentMonitorPage : ToolPageBase
{
    private static readonly Regex CoreName = new(@"^CPU Core #(\d+)$", RegexOptions.Compiled);
    private static readonly Regex ClockName = new(@"^(?:CPU )?Core #(\d+)$", RegexOptions.Compiled);

    private readonly bool _gpu;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly TextBlock _info = new() { TextWrapping = TextWrapping.Wrap, FontSize = 14 };
    private readonly StackPanel _extra = new();
    private readonly MetricTile _load;
    private readonly MetricTile _temp;
    private readonly MetricTile _clock;
    private readonly MetricTile _power;
    private bool _busy;
    private int _tick;

    public ComponentMonitorPage(bool gpu) : base(gpu ? "Placa de vídeo" : "Processador",
        gpu ? "Utilização, temperatura, velocidade, energia e memória de vídeo da placa em tempo real."
            : "Utilização, temperatura, velocidade e energia do processador em tempo real, com o uso de cada núcleo.")
    {
        _gpu = gpu;
        var yellow = Color.FromRgb(0xFF, 0xC4, 0x00);
        _load = new MetricTile("Utilização", yellow, 100, v => $"{v:0}%");
        _temp = new MetricTile("Temperatura", Color.FromRgb(0xFF, 0xA9, 0x4D), 110, v => $"{v:0} °C");
        _clock = new MetricTile("Velocidade", Color.FromRgb(0x4D, 0xA3, 0xFF), 0, v => $"{v / 1000:0.0} GHz");
        _power = new MetricTile("Energia", Color.FromRgb(0x3D, 0xDC, 0x97), 0, v => $"{v:0} W");

        Body.Children.Add(CardOf(_info));
        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 12, 0, 0) };
        _load.Margin = new Thickness(0, 0, 6, 12);
        _temp.Margin = new Thickness(6, 0, 0, 12);
        _clock.Margin = new Thickness(0, 0, 6, 12);
        _power.Margin = new Thickness(6, 0, 0, 12);
        grid.Children.Add(_load);
        grid.Children.Add(_temp);
        grid.Children.Add(_clock);
        grid.Children.Add(_power);
        Body.Children.Add(grid);
        Body.Children.Add(_extra);

        _timer.Tick += async (_, _) => await UpdateAsync();
        Loaded += async (_, _) =>
        {
            _timer.Start();
            await LoadInfoAsync();
            await UpdateAsync();
        };
        Unloaded += (_, _) => _timer.Stop();
    }

    private async Task LoadInfoAsync()
    {
        try
        {
            var hw = await AppServices.Hardware();
            if (_gpu)
            {
                var gpu = hw.Gpus.OrderByDescending(g => g.VramBytes).FirstOrDefault();
                _info.Text = gpu is null
                    ? "Placa de vídeo não identificada."
                    : $"{gpu.Name}\n{(gpu.VramBytes > 0 ? $"{gpu.VramBytes / 1_073_741_824.0:0.#} GB de memória de vídeo · " : "")}driver {gpu.DriverVersion}";
            }
            else
            {
                var cpu = hw.Cpu;
                _info.Text = $"{cpu.Name}\n{cpu.Cores} núcleos · {cpu.Threads} threads · até {cpu.MaxMhz / 1000.0:0.0} GHz" +
                             (cpu.L3Kb > 0 ? $" · cache L3 de {cpu.L3Kb / 1024} MB" : "");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or AggregateException)
        {
            _info.Text = "Não foi possível identificar o componente: " + ex.Message;
        }
    }

    private async Task UpdateAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var sensors = await AppServices.Sensors;
            var s = await Task.Run(sensors.Read);

            double? load = _gpu ? s.GpuLoad : s.CpuLoad;
            double? temp = _gpu ? s.GpuTemperature : s.CpuTemperature;
            double? clock = _gpu ? s.GpuClockMhz : s.CpuClockMhz;
            double? power = _gpu ? s.GpuPowerWatts : s.CpuPowerWatts;

            _load.Push(load, $"{load:0}%");
            _temp.Push(temp, $"{temp:0} °C", temp is null ? s.Message ?? "" : temp >= 90 ? "Muito quente" : temp >= 78 ? "Quente" : "Normal");
            _clock.Push(clock, clock >= 1000 ? $"{clock / 1000:0.00} GHz" : $"{clock:0} MHz");
            _power.Push(power, $"{power:0} W");

            if (_tick++ % 2 == 0)
            {
                var monitor = await AppServices.Monitor;
                var groups = await Task.Run(monitor.ReadAll);
                if (_gpu) ShowGpuExtra(groups);
                else ShowCores(groups);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or AggregateException)
        {
            SetStatus("Não foi possível ler todos os sensores: " + ex.Message, "WarnBrush");
        }
        finally
        {
            _busy = false;
        }
    }

    private void ShowCores(IReadOnlyList<SensorGroup> groups)
    {
        var cpu = groups.FirstOrDefault(g => g.Type == "Cpu");
        if (cpu is null) return;

        var loads = cpu.Sensors.Where(x => x.Kind == "Load" && CoreName.IsMatch(x.Name)).OrderBy(x => int.Parse(CoreName.Match(x.Name).Groups[1].Value)).ToList();
        var clocks = cpu.Sensors.Where(x => x.Kind == "Clock" && ClockName.IsMatch(x.Name))
            .GroupBy(x => int.Parse(ClockName.Match(x.Name).Groups[1].Value)).ToDictionary(g => g.Key, g => g.First().Value);
        if (loads.Count == 0) return;

        _extra.Children.Clear();
        _extra.Children.Add(Heading("Uso por núcleo"));
        var grid = new UniformGrid { Columns = 2 };
        foreach (var core in loads)
        {
            double percent = core.Value ?? 0;
            string clock = clocks.TryGetValue(int.Parse(CoreName.Match(core.Name).Groups[1].Value), out var mhz) && mhz is double m ? $" · {m / 1000:0.00} GHz" : "";
            var cell = new StackPanel { Margin = new Thickness(0, 0, 12, 10) };
            cell.Children.Add(new TextBlock { Text = $"Núcleo {CoreName.Match(core.Name).Groups[1].Value}{clock}", FontSize = 12 });
            cell.Children.Add(UsageBar.Thin(percent, Color.FromRgb(0xFF, 0xC4, 0x00)));
            cell.Children.Add(new TextBlock { Text = $"{percent:0}%", FontSize = 11, Foreground = Brush("MutedBrush") });
            grid.Children.Add(cell);
        }
        _extra.Children.Add(CardOf(grid));
    }

    private void ShowGpuExtra(IReadOnlyList<SensorGroup> groups)
    {
        // com placa integrada + dedicada, mostra a dedicada (a mesma que o Painel usa)
        var gpu = groups.Where(g => g.Type is "GpuNvidia" or "GpuAmd" or "GpuIntel")
            .OrderBy(g => MBExtreme.Core.Sensors.UsageRules.LooksIntegrated(g.Type, g.Name) ? 1 : 0)
            .ThenBy(g => g.Type == "GpuNvidia" ? 0 : 1)
            .FirstOrDefault();
        if (gpu is null) return;

        _extra.Children.Clear();
        _extra.Children.Add(Heading("Detalhes"));

        double? Find(string kind, params string[] names) =>
            gpu.Sensors.FirstOrDefault(x => x.Kind == kind && names.Any(n => x.Name.Contains(n, StringComparison.OrdinalIgnoreCase)))?.Value;

        double? used = Find("SmallData", "Memory Used");
        double? total = Find("SmallData", "Memory Total");
        if (used is double u && total is double t && t > 0)
        {
            var card = new StackPanel();
            card.Children.Add(new TextBlock { Text = $"Memória de vídeo: {u / 1024:0.0} GB de {t / 1024:0.0} GB", FontSize = 14, FontWeight = FontWeights.SemiBold });
            card.Children.Add(UsageBar.Create(100 * u / t));
            _extra.Children.Add(CardOf(card));
        }

        var lines = new List<string>();
        if (Find("Fan", "GPU") is double fan) lines.Add($"Ventoinha: {fan:0} rpm");
        else if (Find("Control", "Fan") is double fanPercent) lines.Add($"Ventoinha: {fanPercent:0}%");
        if (Find("Clock", "Memory") is double memClock) lines.Add($"Clock da memória: {memClock:0} MHz");
        if (Find("Voltage", "GPU") is double volts) lines.Add($"Tensão: {volts:0.000} V");
        if (Find("Load", "Memory Controller") is double ctrl) lines.Add($"Controlador de memória: {ctrl:0}%");
        if (Find("Load", "Video Engine") is double video) lines.Add($"Codificador de vídeo: {video:0}%");
        if (lines.Count > 0) _extra.Children.Add(CardOf(new TextBlock { Text = string.Join("\n", lines), LineHeight = 22 }));
    }
}
