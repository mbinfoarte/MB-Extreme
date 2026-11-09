using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MBExtreme.Core.Devices;

namespace MBExtreme.UI.Pages;

/// <summary>Lista tudo que está conectado ao PC e avisa quando algo USB é plugado ou removido.</summary>
public sealed class DevicesPage : ToolPageBase
{
    private IReadOnlyList<DeviceInfo> _devices = Array.Empty<DeviceInfo>();
    private bool _usbOnly = true;
    private readonly StackPanel _log = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private HashSet<string>? _usbIds;
    private bool _polling;

    public DevicesPage() : base("Dispositivos conectados",
        "Tudo que o Windows enxerga neste PC. Com o filtro USB, você vê o que está plugado e é avisado quando algo conecta ou desconecta.")
    {
        AddButton("Atualizar", async (_, _) => await LoadAsync());
        AddButton("Só USB", (_, _) => { _usbOnly = true; Render(); }, false);
        AddButton("Todos os dispositivos", (_, _) => { _usbOnly = false; Render(); }, false);

        _timer.Tick += async (_, _) => await PollAsync();
        Loaded += async (_, _) =>
        {
            await LoadAsync();
            _timer.Start();
        };
        Unloaded += (_, _) => _timer.Stop();
    }

    private async Task LoadAsync()
    {
        SetStatus("Listando dispositivos...");
        try
        {
            _devices = await Task.Run(DeviceService.ListPresent);
        }
        catch (Exception ex)
        {
            SetStatus("Não foi possível listar: " + ex.Message, "BadBrush");
            return;
        }

        _usbIds = _devices.Where(DeviceService.IsUsb).Select(d => d.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Render();
    }

    private async Task PollAsync()
    {
        if (_polling || _usbIds is null) return;
        _polling = true;
        try
        {
            var list = await Task.Run(DeviceService.ListPresent);
            var now = list.Where(DeviceService.IsUsb).ToDictionary(d => d.InstanceId, StringComparer.OrdinalIgnoreCase);
            bool changed = false;
            foreach (var (id, device) in now)
            {
                if (_usbIds.Contains(id)) continue;
                AddLog("Conectado", device);
                changed = true;
            }
            foreach (var id in _usbIds.Where(i => !now.ContainsKey(i)))
            {
                var old = _devices.FirstOrDefault(d => string.Equals(d.InstanceId, id, StringComparison.OrdinalIgnoreCase));
                if (old is not null) AddLog("Removido", old);
                changed = true;
            }

            if (changed)
            {
                _devices = list;
                _usbIds = now.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
                Render();
            }
        }
        catch (Exception)
        {
            // A próxima verificação tenta de novo; falha pontual não precisa de aviso.
        }
        finally
        {
            _polling = false;
        }
    }

    private void AddLog(string what, DeviceInfo device)
    {
        string text = $"{DateTime.Now:HH:mm:ss}  {what}: {device.Name}";
        _log.Children.Insert(0, new TextBlock
        {
            Text = text,
            Margin = new Thickness(0, 2, 0, 2),
            Foreground = Brush(what == "Conectado" ? "OkBrush" : "WarnBrush"),
        });
        while (_log.Children.Count > 30) _log.Children.RemoveAt(_log.Children.Count - 1);
    }

    private void Render()
    {
        if (_log.Parent is Border old) old.Child = null;
        Body.Children.Clear();
        Body.Children.Add(Heading("Eventos USB desta sessão"));
        Body.Children.Add(_log.Children.Count == 0
            ? CardOf(Muted("Plugue ou remova um dispositivo USB e ele aparece aqui."))
            : CardOf(_log));

        var shown = _devices.Where(d => !_usbOnly || DeviceService.IsUsb(d)).ToList();
        SetStatus($"{shown.Count} dispositivo(s)" + (_usbOnly ? " USB" : ""));

        foreach (var group in shown.GroupBy(d => string.IsNullOrWhiteSpace(d.Class) ? "Outros" : d.Class).OrderBy(g => g.Key))
        {
            Body.Children.Add(Heading(group.Key));
            foreach (var d in group.OrderBy(x => x.Name))
            {
                string ids = DeviceService.UsbIds(d);
                string detail = string.Join(" · ", new[] { d.Manufacturer, ids, d.Status }.Where(s => !string.IsNullOrWhiteSpace(s)));
                Body.Children.Add(Row(d.Name, detail));
            }
        }
    }
}
