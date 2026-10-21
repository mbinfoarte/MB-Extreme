using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MBExtreme.Core.Hardware;
using MBExtreme.Core.Sensors;

namespace MBExtreme.UI.Pages;

/// <summary>Sensores em tempo real: clock e uso de cada núcleo, temperaturas, tensões, potência, ventoinhas, memória e discos.</summary>
public sealed class LiveMonitorPage : ToolPageBase
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<string, TextBlock> _cells = new();
    private readonly StackPanel _summary = new();
    private HardwareMonitorService? _service;
    private IReadOnlyList<SensorGroup> _last = Array.Empty<SensorGroup>();
    private bool _busy;
    private bool _built;

    public LiveMonitorPage() : base("Monitor em tempo real",
        "Clock e uso de cada núcleo, temperaturas, tensões, potência, ventoinhas, memória e discos, atualizados a cada segundo. Tensões da placa-mãe e da CPU dependem do hardware e do driver PawnIO; quando não há leitura, o sensor não aparece.")
    {
        AddButton("Pausar/Continuar", (_, _) =>
        {
            if (_timer.IsEnabled) _timer.Stop();
            else _timer.Start();
        }, false);
        AddButton("Exportar CSV", (_, _) => Export(), false);
        Body.Children.Add(_summary);

        _timer.Tick += async (_, _) => await TickAsync();
        Loaded += async (_, _) =>
        {
            await FillSummaryAsync();
            _timer.Start();
            await TickAsync();
        };
        Unloaded += (_, _) =>
        {
            _timer.Stop();
            _service?.Dispose();
            _service = null;
            _built = false;
            _cells.Clear();
        };
    }

    private async Task FillSummaryAsync()
    {
        try
        {
            var hw = await AppServices.Hardware();
            _summary.Children.Clear();
            _summary.Children.Add(Row(hw.Cpu.Name,
                $"{hw.Cpu.Cores} núcleos físicos · {hw.Cpu.Threads} threads" +
                (hw.Cpu.MaxMhz > 0 ? $" · clock máximo de fábrica {hw.Cpu.MaxMhz / 1000.0:0.0} GHz" : "") +
                (hw.Cpu.L3Kb > 0 ? $" · cache L3 {hw.Cpu.L3Kb / 1024} MB" : "")));
        }
        catch (Exception ex) when (ex is InvalidOperationException or AggregateException)
        {
            // o resumo é opcional
        }
    }

    private async Task TickAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            _service ??= await Task.Run(() => new HardwareMonitorService());
            if (!_service.IsAvailable)
            {
                SetStatus("Sensores indisponíveis: " + (_service.OpenError ?? "execute como administrador."), "WarnBrush");
                _timer.Stop();
                return;
            }

            var groups = await Task.Run(() => _service.ReadAll());
            _last = groups;
            if (!_built) Build(groups);
            Update(groups);
        }
        catch (Exception ex) when (ex is InvalidOperationException or AggregateException)
        {
            SetStatus("Falha ao ler os sensores: " + ex.Message, "WarnBrush");
        }
        finally
        {
            _busy = false;
        }
    }

    private void Build(IReadOnlyList<SensorGroup> groups)
    {
        _built = true;
        for (int i = Body.Children.Count - 1; i >= 0; i--)
            if (!ReferenceEquals(Body.Children[i], _summary)) Body.Children.RemoveAt(i);

        foreach (var group in groups)
        {
            Body.Children.Add(Heading(group.Name));
            var panel = new StackPanel();
            foreach (var kind in group.Sensors.GroupBy(s => s.Kind).OrderBy(k => HardwareMonitorService.KindOrder(k.Key)))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = HardwareMonitorService.KindTitle(kind.Key),
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 8, 0, 4),
                    Foreground = Brush("MutedBrush"),
                });
                foreach (var sensor in kind)
                {
                    var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    grid.ColumnDefinitions.Add(new ColumnDefinition());
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
                    grid.Children.Add(new TextBlock { Text = sensor.Name, FontSize = 13 });
                    var value = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Right };
                    var range = new TextBlock { FontSize = 12, Foreground = Brush("MutedBrush"), TextAlignment = TextAlignment.Right };
                    Grid.SetColumn(value, 1);
                    Grid.SetColumn(range, 2);
                    grid.Children.Add(value);
                    grid.Children.Add(range);
                    panel.Children.Add(grid);
                    _cells[sensor.Id + "#v"] = value;
                    _cells[sensor.Id + "#r"] = range;
                }
            }

            Body.Children.Add(CardOf(panel));
        }
    }

    private void Update(IReadOnlyList<SensorGroup> groups)
    {
        foreach (var group in groups)
        {
            foreach (var s in group.Sensors)
            {
                if (_cells.TryGetValue(s.Id + "#v", out var value)) value.Text = HardwareMonitorService.Format(s.Kind, s.Value);
                if (_cells.TryGetValue(s.Id + "#r", out var range))
                    range.Text = $"mín {HardwareMonitorService.Format(s.Kind, s.Min)} · máx {HardwareMonitorService.Format(s.Kind, s.Max)}";
            }
        }

        SetStatus($"{groups.Sum(g => g.Sensors.Count)} sensores · atualizado às {DateTime.Now:HH:mm:ss}");
    }

    private void Export()
    {
        if (_last.Count == 0) return;
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MB Extreme");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, $"sensores-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            File.WriteAllText(file, HardwareMonitorService.ToCsv(_last), System.Text.Encoding.UTF8);
            SetStatus("Salvo em " + file, "OkBrush");
        }
        catch (IOException ex)
        {
            SetStatus("Não foi possível salvar: " + ex.Message, "BadBrush");
        }
    }
}
