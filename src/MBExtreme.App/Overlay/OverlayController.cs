using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using MBExtreme.Core.Fps;

namespace MBExtreme.UI.Overlay;

/// <summary>Liga e desliga o monitor de FPS e o overlay. Atalho global: Ctrl + Shift + F10.</summary>
internal sealed class OverlayController
{
    private const int HotkeyId = 0x4D42;
    private const int WmHotkey = 0x0312;
    private const uint ModControl = 0x2, ModShift = 0x4, ModNoRepeat = 0x4000;
    private const uint VkF10 = 0x79;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    private readonly FpsMonitor _monitor = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private FpsOverlayWindow? _window;
    private IntPtr _hotkeyWindow;
    private HwndSource? _source;

    public OverlayController() => _timer.Tick += async (_, _) => await RefreshAsync();

    public bool IsMonitoring => _monitor.IsRunning;
    public bool OverlayVisible => _window is { IsVisible: true };
    /// <summary>Canto antigo (0 a 3) da tela do medidor de FPS, ligado à posição nova (0 a 8).</summary>
    public int Corner
    {
        get => AppServices.Settings.OverlayPosition switch { 2 => 1, 6 => 2, 8 => 3, _ => 0 };
        set { AppServices.Settings.OverlayPosition = value switch { 1 => 2, 2 => 6, 3 => 8, _ => 0 }; AppServices.Settings.Save(); Reapply(); }
    }
    public FpsReading? Last { get; private set; }
    public string? LastProcess { get; private set; }
    public event EventHandler? Changed;

    public void StartMonitoring()
    {
        if (_monitor.IsRunning) return;
        _monitor.Start();
        _timer.Start();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void StopMonitoring()
    {
        _timer.Stop();
        HideOverlay();
        _monitor.Stop();
        Last = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private bool _monitorStartedForCapture;
    private long? _ping;
    private DateTime _pingAt;

    private async Task PingAsync()
    {
        try
        {
            using var p = new System.Net.NetworkInformation.Ping();
            var r = await p.SendPingAsync("1.1.1.1", 1000);
            _ping = r.Status == System.Net.NetworkInformation.IPStatus.Success ? r.RoundtripTime : null;
        }
        catch (Exception ex) when (ex is System.Net.NetworkInformation.PingException or InvalidOperationException) { _ping = null; }
    }

    /// <summary>Aplica posição e aparência novas no contador aberto (tela de ajuste).</summary>
    public void Reapply()
    {
        if (_window is { IsVisible: true }) _window.Place(AppServices.Settings.OverlayPosition);
    }

    public bool IsCapturing => _monitor.IsCapturing;

    /// <summary>Começa a guardar todos os quadros do jogo (análise de sessão). Liga o monitor se ainda não estiver ligado.</summary>
    public void BeginCapture(string? processName)
    {
        if (!_monitor.IsRunning)
        {
            StartMonitoring();
            _monitorStartedForCapture = true;
        }
        _monitor.BeginCapture(processName);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public LiveCapture? CaptureLive(int recentFrames = 300) => _monitor.CaptureLive(recentFrames);

    public CaptureResult? EndCapture()
    {
        var result = _monitor.EndCapture();
        if (_monitorStartedForCapture)
        {
            _monitorStartedForCapture = false;
            if (!OverlayVisible) StopMonitoring();
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return result;
    }

    public void ShowOverlay()
    {
        StartMonitoring();
        _window ??= new FpsOverlayWindow();
        _window.Show();
        _window.Place(AppServices.Settings.OverlayPosition);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void HideOverlay()
    {
        _window?.Hide();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleOverlay()
    {
        if (OverlayVisible) HideOverlay();
        else ShowOverlay();
    }

    /// <summary>Registra o atalho global na janela principal (uma vez).</summary>
    public void AttachHotkey(Window main)
    {
        if (_hotkeyWindow != IntPtr.Zero) return;
        _hotkeyWindow = new WindowInteropHelper(main).EnsureHandle();
        _source = HwndSource.FromHwnd(_hotkeyWindow);
        _source?.AddHook(Hook);
        RegisterHotKey(_hotkeyWindow, HotkeyId, ModControl | ModShift | ModNoRepeat, VkF10);
    }

    public void Shutdown()
    {
        if (_hotkeyWindow != IntPtr.Zero) UnregisterHotKey(_hotkeyWindow, HotkeyId);
        _source?.RemoveHook(Hook);
        _window?.Close();
        _timer.Stop();
        _monitor.Dispose();
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            ToggleOverlay();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private async Task RefreshAsync()
    {
        var reading = _monitor.Current();
        Last = reading;
        LastProcess = reading is null ? null : _monitor.ProcessName(reading.ProcessId);

        if (_window is { IsVisible: true })
        {
            var items = AppServices.Settings.OverlayItems;
            bool Has(string k) => items.Contains(k);
            var lines = new List<OverlayLine>();
            var muted = System.Windows.Media.Color.FromRgb(0xA1, 0xA1, 0xAA);
            if (reading is not null)
            {
                if (Has("low1")) lines.Add(new OverlayLine("1%", $"{reading.Low1PercentFps:0}", muted));
                if (Has("low01") && CaptureLive(600) is { } live && live.RecentFrameMs.Count > 200)
                    lines.Add(new OverlayLine("0,1%", $"{FrameAnalysis.Compute(live.RecentFrameMs).Low01Fps:0}", muted));
                if (Has("frametime")) lines.Add(new OverlayLine("FT", $"{reading.FrameTimeMs:0.0} ms", muted));
            }
            try
            {
                var sensors = await AppServices.Sensors;
                var s = await Task.Run(sensors.Read);
                var cpuC = System.Windows.Media.Color.FromRgb(0x6C, 0xB6, 0xFF);
                var gpuC = System.Windows.Media.Color.FromRgb(0x3D, 0xDC, 0x97);
                if (Has("cpu")) lines.Add(new OverlayLine("CPU", $"{s.CpuLoad:0}%" + (s.CpuTemperature is { } ct ? $" {ct:0}°C" : ""), cpuC));
                if (Has("gpu")) lines.Add(new OverlayLine("GPU", $"{s.GpuLoad:0}%" + (s.GpuTemperature is { } gt ? $" {gt:0}°C" : ""), gpuC));
                if (Has("gpuclock") && s.GpuClockMhz is { } clock) lines.Add(new OverlayLine("CLK", $"{clock:0} MHz", gpuC));
                if (Has("vram") && s.Detail?.GpuVramUsedMb is { } used) lines.Add(new OverlayLine("VRAM", $"{used / 1024:0.0}" + (s.Detail.GpuVramTotalMb is { } tot ? $"/{tot / 1024:0} GB" : " GB"), gpuC));
                if (Has("power") && (s.CpuPowerWatts ?? 0) + (s.GpuPowerWatts ?? 0) > 0) lines.Add(new OverlayLine("W", $"{(s.CpuPowerWatts ?? 0) + (s.GpuPowerWatts ?? 0):0}", muted));
            }
            catch (Exception ex) when (ex is InvalidOperationException or AggregateException) { }
            if (Has("ram"))
            {
                var mem = MBExtreme.Core.Memory.MemoryService.GetStatus();
                lines.Add(new OverlayLine("RAM", $"{100 - mem.FreePercent:0}%", System.Windows.Media.Color.FromRgb(0xC0, 0x84, 0xFC)));
            }
            if (Has("ping") && _ping is { } ms) lines.Add(new OverlayLine("PING", $"{ms} ms", muted));
            if (Has("clock")) lines.Add(new OverlayLine("", DateTime.Now.ToString("HH:mm"), muted));
            if (AppServices.Bottleneck.IsRecording && AppServices.Bottleneck.CurrentLabel is { } label) lines.Add(new OverlayLine("", label, muted));
            _window.Show(Has("fps") ? reading?.Fps ?? 0 : null, reading?.FrameTimeMs, lines, Has("graph"));
            if (Has("ping") && DateTime.Now - _pingAt > TimeSpan.FromSeconds(5)) { _pingAt = DateTime.Now; _ = PingAsync(); }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
