using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using MBExtreme.Core.Gamepads;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using Nefarius.ViGEm.Client.Targets;

namespace MBExtreme.UI;

/// <summary>
/// Teclado e mouse → controle virtual (Xbox 360 ou PS4). O movimento do mouse vem pelo Raw Input (funciona mesmo com o
/// cursor parado na borda e com o MB em segundo plano) e vira o analógico direito; as teclas viram botões.
/// Uma tecla (F8) liga e pausa a conversão a qualquer momento.
/// </summary>
internal sealed class KbmService
{
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices(RawInputDevice[] devices, uint count, uint size);
    [DllImport("user32.dll")] private static extern uint GetRawInputData(IntPtr hRawInput, uint command, IntPtr data, ref uint size, uint headerSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice { public ushort UsagePage, Usage; public uint Flags; public IntPtr Target; }

    private const int WM_INPUT = 0x00FF;
    private const uint RID_INPUT = 0x10000003, RIDEV_INPUTSINK = 0x100;

    private ViGEmClient? _client;
    private IXbox360Controller? _xbox;
    private IDualShock4Controller? _ds4;
    private HwndSource? _sink;
    private CancellationTokenSource? _cts;
    private long _dx, _dy;

    public bool Running => _cts is not null;
    /// <summary>Ligado mas pausado pela tecla de atalho (manda um controle parado).</summary>
    public bool Paused { get; private set; }
    public string Status { get; private set; } = "Desligado.";
    public PadState Last { get; private set; } = PadState.Empty;
    public event Action? Changed;

    public bool Start()
    {
        Stop();
        var s = AppServices.Settings.Kbm;
        try
        {
            _client = new ViGEmClient();
            if (s.Target == VirtualTarget.DualShock4)
            {
                _ds4 = _client.CreateDualShock4Controller();
                _ds4.AutoSubmitReport = false;
                _ds4.Connect();
            }
            else
            {
                _xbox = _client.CreateXbox360Controller();
                _xbox.AutoSubmitReport = false;
                _xbox.Connect();
            }
        }
        catch (Exception ex) when (ex is VigemBusNotFoundException or VigemBusAccessFailedException or VigemBusVersionMismatchException or VigemNoFreeSlotException)
        {
            Status = ex is VigemBusNotFoundException
                ? "O driver do controle virtual (ViGEmBus) não está instalado. Instale em MB Control › Emulação de controle (passo 1)."
                : "O driver do controle virtual não respondeu: " + ex.Message;
            Stop(silent: true);
            Changed?.Invoke();
            return false;
        }

        // Raw Input: movimento do mouse mesmo com o MB minimizado
        _sink = new HwndSource(new HwndSourceParameters("MB Teclado e mouse") { Width = 0, Height = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3) });
        _sink.AddHook(Hook);
        var dev = new[] { new RawInputDevice { UsagePage = 1, Usage = 2, Flags = RIDEV_INPUTSINK, Target = _sink.Handle } };
        RegisterRawInputDevices(dev, 1, (uint)Marshal.SizeOf<RawInputDevice>());

        Paused = false;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        new Thread(() => Loop(token)) { IsBackground = true, Name = "MB Teclado e mouse", Priority = ThreadPriority.AboveNormal }.Start();
        Status = $"Ligado. {s.ToggleKey} pausa e volta.";
        Changed?.Invoke();
        return true;
    }

    public void Stop() => Stop(false);

    private void Stop(bool silent)
    {
        _cts?.Cancel();
        _cts = null;
        try { _xbox?.Disconnect(); } catch (Exception) { }
        try { _ds4?.Disconnect(); } catch (Exception) { }
        _xbox = null;
        _ds4 = null;
        _client?.Dispose();
        _client = null;
        _sink?.Dispose();
        _sink = null;
        Last = PadState.Empty;
        if (!silent) { Status = "Desligado."; Changed?.Invoke(); }
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_INPUT) return IntPtr.Zero;
        uint size = 0;
        uint header = (uint)(8 + 2 * IntPtr.Size);
        GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, header);
        if (size == 0 || size > 512) return IntPtr.Zero;
        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(lParam, RID_INPUT, buffer, ref size, header) != size) return IntPtr.Zero;
            if (Marshal.ReadInt32(buffer) != 0) return IntPtr.Zero;               // 0 = mouse
            int baseOffset = (int)header;
            ushort flags = (ushort)Marshal.ReadInt16(buffer, baseOffset);
            if ((flags & 1) != 0) return IntPtr.Zero;                             // posição absoluta (tablet, área de trabalho remota): ignora
            Interlocked.Add(ref _dx, Marshal.ReadInt32(buffer, baseOffset + 12));
            Interlocked.Add(ref _dy, Marshal.ReadInt32(buffer, baseOffset + 16));
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return IntPtr.Zero;
    }

    private static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private void Loop(CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        double last = 0, sx = 0, sy = 0;
        bool toggleWas = false;
        var actions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (!token.IsCancellationRequested)
        {
            Thread.Sleep(2);
            var s = AppServices.Settings.Kbm;
            double now = watch.Elapsed.TotalMilliseconds, dt = Math.Max(0.5, now - last);
            last = now;
            long dx = Interlocked.Exchange(ref _dx, 0), dy = Interlocked.Exchange(ref _dy, 0);

            // tecla de pausa
            bool toggle = KbmMapper.Keys.TryGetValue(s.ToggleKey, out int tvk) && Down(tvk);
            if (toggle && !toggleWas) { Paused = !Paused; Status = Paused ? $"Pausado ({s.ToggleKey} volta)." : $"Ligado. {s.ToggleKey} pausa e volta."; Changed?.Invoke(); }
            toggleWas = toggle;

            PadState state = PadState.Empty;
            if (!Paused)
            {
                actions.Clear();
                foreach (var (key, action) in s.Bindings)
                    if (KbmMapper.Keys.TryGetValue(key, out int vk) && Down(vk)) actions.Add(action);
                var (tx, ty) = KbmMapper.MouseToStick(dx / dt, dy / dt, s);
                sx = KbmMapper.Smooth(sx, tx, s.Smoothing);
                sy = KbmMapper.Smooth(sy, ty, s.Smoothing);
                if (Math.Abs(sx) < 0.01) sx = 0;
                if (Math.Abs(sy) < 0.01) sy = 0;
                state = KbmMapper.Build(actions, sx, sy);
            }
            else { sx = sy = 0; }
            Last = state;
            try
            {
                if (_xbox is { } x) EmulationService.SendXbox(x, state);
                else if (_ds4 is { } d) EmulationService.SendDs4(d, state);
            }
            catch (Exception) { /* controle virtual removido */ }
        }
    }
}
