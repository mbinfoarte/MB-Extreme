using System.Runtime.Versioning;
using System.Text.Json;
using LibreHardwareMonitor.Hardware;

namespace MBExtreme.Core.Fans;

public sealed record FanChannel(string ControlId, string Name, string HardwareName, string HardwareType, double? Rpm, double? Percent, bool Controllable, bool Manual, FanChannelKind Kind);

public sealed record TemperatureSource(string Id, string Name, double? Value);

/// <summary>
/// Leitura e controle das ventoinhas (placa-mãe, placa de vídeo e controladores compatíveis) pela biblioteca de sensores.
/// Só o que o hardware permitir aparece como controlável. Ao fechar, devolve todas ao controle automático do firmware.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FanControlService : IFanBackend, IDisposable
{
    private readonly Computer? _computer;
    private readonly Dictionary<string, ISensor> _controls = new();
    private readonly object _gate = new();

    public FanControlService()
    {
        try
        {
            _computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsMotherboardEnabled = true, IsControllerEnabled = true, IsMemoryEnabled = false, IsStorageEnabled = false };
            _computer.Open();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            OpenError = ex.Message;
            _computer = null;
        }
    }

    public string? OpenError { get; }
    public bool IsAvailable => _computer is not null;

    private static IEnumerable<IHardware> Flatten(IEnumerable<IHardware> hardware)
    {
        foreach (var h in hardware)
        {
            yield return h;
            foreach (var c in Flatten(h.SubHardware)) yield return c;
        }
    }

    public static FanChannelKind GuessKind(string hardwareType, string name)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("pump") || n.Contains("bomba") || n.Contains("aio")) return FanChannelKind.Pump;
        if (hardwareType.StartsWith("Gpu", StringComparison.OrdinalIgnoreCase) || n.Contains("gpu")) return FanChannelKind.Gpu;
        if (n.Contains("cpu") || n.Contains("cooler")) return FanChannelKind.Cpu;
        return FanChannelKind.Case;
    }

    /// <summary>Lista as ventoinhas lidas e, para cada uma, o controle correspondente quando existe.</summary>
    public IReadOnlyList<FanChannel> Channels()
    {
        lock (_gate)
        {
        var result = new List<FanChannel>();
        if (_computer is null) return result;
        _controls.Clear();

        foreach (var hw in Flatten(_computer.Hardware))
        {
            hw.Update();
            var fans = hw.Sensors.Where(s => s.SensorType == SensorType.Fan).ToList();
            var controls = hw.Sensors.Where(s => s.SensorType == SensorType.Control && s.Control is not null).ToList();
            string type = hw.HardwareType.ToString();

            foreach (var control in controls)
            {
                // "Fan Control #2" ↔ "Fan #2"; placas de vídeo: "GPU Fan" ↔ "GPU Fan"
                var fan = fans.FirstOrDefault(f => Suffix(f.Name) == Suffix(control.Name)) ?? (fans.Count == 1 && controls.Count == 1 ? fans[0] : null);
                string id = control.Identifier.ToString();
                _controls[id] = control;
                result.Add(new FanChannel(id, control.Name, hw.Name, type, fan?.Value, control.Value, true,
                    control.Control!.ControlMode == ControlMode.Software, GuessKind(type, control.Name + " " + (fan?.Name ?? ""))));
            }

            foreach (var fan in fans.Where(f => !controls.Any(c => Suffix(c.Name) == Suffix(f.Name)) && !(controls.Count == 1 && fans.Count == 1)))
                result.Add(new FanChannel(fan.Identifier.ToString(), fan.Name, hw.Name, type, fan.Value, null, false, false, GuessKind(type, fan.Name)));
        }

        return result;
            }
    }

    private static string Suffix(string name)
    {
        int hash = name.LastIndexOf('#');
        return hash >= 0 ? name[(hash + 1)..].Trim() : name.Replace("Control", "", StringComparison.OrdinalIgnoreCase).Trim().ToLowerInvariant();
    }

    public IReadOnlyList<TemperatureSource> Temperatures()
    {
        lock (_gate)
        {
        var list = new List<TemperatureSource>();
        if (_computer is null) return list;
        foreach (var hw in Flatten(_computer.Hardware))
        {
            hw.Update();
            foreach (var s in hw.Sensors.Where(s => s.SensorType == SensorType.Temperature && s.Value is not null))
                list.Add(new TemperatureSource(s.Identifier.ToString(), $"{hw.Name} · {s.Name}", s.Value));
        }
        return list;
            }
    }

    public double? Temperature(string sensorId)
    {
        lock (_gate)
        {
        if (_computer is null) return null;
        foreach (var hw in Flatten(_computer.Hardware))
            foreach (var s in hw.Sensors)
                if (s.SensorType == SensorType.Temperature && s.Identifier.ToString() == sensorId)
                {
                    hw.Update();
                    return s.Value;
                }
        return null;
            }
    }

    public bool SetPercent(string controlId, double percent)
    {
        lock (_gate)
        {
        if (!_controls.TryGetValue(controlId, out var sensor) || sensor.Control is not { } control) return false;
        try
        {
            float value = (float)Math.Clamp(percent, Math.Max(0, control.MinSoftwareValue), Math.Min(100, control.MaxSoftwareValue));
            control.SetSoftware(value);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or UnauthorizedAccessException) { return false; }
            }
    }

    public void SetAuto(string controlId)
    {
        lock (_gate)
        {
        if (_controls.TryGetValue(controlId, out var sensor) && sensor.Control is { } control)
        {
            try { control.SetDefault(); }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or UnauthorizedAccessException) { }
        }
            }
    }

    /// <summary>Devolve tudo ao controle do firmware (BIOS).</summary>
    public void RestoreAll()
    {
        foreach (var id in _controls.Keys.ToList()) SetAuto(id);
    }

    public void Dispose()
    {
        RestoreAll();
        _computer?.Close();
    }
}

/// <summary>Perfis de ventoinha guardados em %AppData%\MBExtreme\fan-profiles.json.</summary>
public sealed class FanProfileStore
{
    private readonly string _path;
    private readonly object _lock = new();
    private List<FanChannelConfig> _items;

    public FanProfileStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "fan-profiles.json");
        _items = Load();
    }

    private List<FanChannelConfig> Load()
    {
        try
        {
            if (File.Exists(_path)) return JsonSerializer.Deserialize<List<FanChannelConfig>>(File.ReadAllText(_path)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new();
    }

    public FanChannelConfig Get(string channelId, FanChannelKind kind)
    {
        lock (_lock) return _items.FirstOrDefault(i => i.ChannelId == channelId) ?? new FanChannelConfig { ChannelId = channelId, Kind = kind };
    }

    public IReadOnlyList<FanChannelConfig> All()
    {
        lock (_lock) return _items.ToList();
    }

    public void Save(FanChannelConfig config)
    {
        lock (_lock)
        {
            config.Curve = FanCurve.Normalize(config.Curve);
            _items.RemoveAll(i => i.ChannelId == config.ChannelId);
            _items.Add(config);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(_items, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
