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
    private readonly TextBox _filter = new() { Width = 260, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6) };
    private readonly Dictionary<string, UIElement> _rowsById = new();
    private readonly Dictionary<UIElement, (string Name, string Group)> _rowNames = new();
    private SensorTools.CsvLog? _log;
    private string? _logFile;
    private Button? _logButton;
    private StackPanel? _tools;
    private DateTime _lastAlert = DateTime.MinValue;
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
        _logButton = AddButton("Gravar em arquivo (CSV)", (_, _) => ToggleLog(), false);
        Body.Children.Add(_summary);

        // alerta de temperatura e busca de sensores (como nos monitores de hardware completos)
        var tools = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        tools.Children.Add(Muted("Alerta de temperatura (aviso na tela e um som quando algum sensor passar do limite):", 11));
        int[] limits = { 0, 80, 85, 90, 95 };
        var chips = new WrapPanel();
        var buttons = new List<Button>();
        void Paint()
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                buttons[i].Style = (Style)FindResource(limits[i] == AppServices.Settings.TemperatureAlertC ? "PrimaryButton" : "GhostButton");
                buttons[i].Padding = new Thickness(11, 4, 11, 4);
                buttons[i].FontSize = 12;
            }
        }
        foreach (int limit in limits)
        {
            int l = limit;
            var b = new Button { Content = l == 0 ? "Desligado" : $"{l} °C", Margin = new Thickness(0, 0, 6, 6) };
            b.Click += (_, _) => { AppServices.Settings.TemperatureAlertC = l; AppServices.Settings.Save(); Paint(); };
            buttons.Add(b);
            chips.Children.Add(b);
        }
        Paint();
        tools.Children.Add(chips);
        tools.Children.Add(Muted("Buscar sensor:", 11));
        _filter.TextChanged += (_, _) => ApplyFilter();
        tools.Children.Add(_filter);
        _tools = tools;
        Body.Children.Add(tools);

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
            StopLog();
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
            var service = await AppServices.Monitor;
            if (!service.IsAvailable)
            {
                SetStatus("Sensores indisponíveis: " + (service.OpenError ?? "execute como administrador."), "WarnBrush");
                _timer.Stop();
                return;
            }

            var groups = await Task.Run(service.ReadAll);
            if (!IsLoaded) return;
            _last = groups;
            if (!_built) Build(groups);
            Update(groups);
        }
        catch (Exception ex)
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
            if (!ReferenceEquals(Body.Children[i], _summary) && !ReferenceEquals(Body.Children[i], _tools)) Body.Children.RemoveAt(i);

        foreach (var group in groups)
        {
            var groupTitle = Heading(group.Name);
            Body.Children.Add(groupTitle);
            _rowNames[groupTitle] = (group.Name, group.Name);
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
                    _rowsById[sensor.Id] = grid;
                    _rowNames[grid] = (sensor.Name, group.Name);
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

        SetStatus($"{groups.Sum(g => g.Sensors.Count)} sensores · atualizado às {DateTime.Now:HH:mm:ss}" + (_logFile is null ? "" : " · gravando"));

        if (_log is not null && _logFile is not null)
        {
            try { File.AppendAllText(_logFile, _log.Row(DateTime.Now, groups) + Environment.NewLine, System.Text.Encoding.UTF8); }
            catch (IOException) { StopLog(); SetStatus("A gravação parou: não foi possível escrever no arquivo.", "WarnBrush"); }
        }

        int limit = AppServices.Settings.TemperatureAlertC;
        if (limit > 0 && DateTime.Now - _lastAlert > TimeSpan.FromSeconds(30) && SensorTools.HotSensors(groups, limit) is { Count: > 0 } hot)
        {
            _lastAlert = DateTime.Now;
            System.Media.SystemSounds.Exclamation.Play();
            SetStatus($"⚠ {hot[0].Sensor} ({hot[0].Component}) está em {hot[0].Value:0} °C, acima do limite de {limit} °C.", "BadBrush");
        }
    }

    private void ApplyFilter()
    {
        string text = _filter.Text.Trim();
        foreach (var (element, info) in _rowNames)
            element.Visibility = text.Length == 0 || info.Name.Contains(text, StringComparison.OrdinalIgnoreCase) || info.Group.Contains(text, StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ToggleLog()
    {
        if (_log is not null) { StopLog(); SetStatus("Gravação encerrada. O arquivo está em Documentos\\MB Extreme.", "OkBrush"); return; }
        if (_last.Count == 0) { SetStatus("Aguarde a primeira leitura dos sensores.", "WarnBrush"); return; }
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MB Extreme");
            Directory.CreateDirectory(dir);
            _logFile = Path.Combine(dir, $"registro-sensores-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            _log = new SensorTools.CsvLog(_last);
            File.WriteAllText(_logFile, _log.Header, System.Text.Encoding.UTF8);
            if (_logButton is not null) _logButton.Content = "Parar a gravação";
            SetStatus("Gravando um registro por segundo em " + _logFile, "OkBrush");
        }
        catch (IOException ex)
        {
            _log = null;
            _logFile = null;
            SetStatus("Não foi possível criar o arquivo: " + ex.Message, "BadBrush");
        }
    }

    private void StopLog()
    {
        _log = null;
        _logFile = null;
        if (_logButton is not null) _logButton.Content = "Gravar em arquivo (CSV)";
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
