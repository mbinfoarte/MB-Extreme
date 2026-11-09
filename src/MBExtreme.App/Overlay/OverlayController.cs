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
    public int Corner { get; set; }
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
        _window.Place(Corner);
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
            string fps = reading is null ? "-- FPS" : $"{reading.Fps:0} FPS";
            var parts = new List<string>();
            if (reading is not null) parts.Add($"{reading.FrameTimeMs:0.0} ms · 1% {reading.Low1PercentFps:0}");
            try
            {
                var sensors = await AppServices.Sensors;
                var s = await Task.Run(sensors.Read);
                if (s.CpuTemperature is { } ct) parts.Add($"CPU {ct:0}°C" + (s.CpuLoad is { } cl ? $" {cl:0}%" : ""));
                if (s.GpuTemperature is { } gt) parts.Add($"GPU {gt:0}°C" + (s.GpuLoad is { } gl ? $" {gl:0}%" : ""));
            }
            catch (Exception ex) when (ex is InvalidOperationException or AggregateException)
            {
                // sem sensores: mostra só o FPS
            }

            if (AppServices.Bottleneck.IsRecording && AppServices.Bottleneck.CurrentLabel is { } label) parts.Add(label);
            _window.SetText(fps, string.Join("\n", parts));
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
