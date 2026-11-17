using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using MBExtreme.Core.Gamepads;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace MBExtreme.UI;

/// <summary>
/// Motor de emulação do Controle: cria um controle virtual (Xbox 360 ou PS4) pelo ViGEmBus e repassa o controle físico,
/// já com remapeamento, zonas mortas, curvas e mira pelo giroscópio. A vibração do jogo volta para o controle físico.
/// Com o HidHide instalado, o controle físico fica escondido dos jogos (o MB continua lendo).
/// </summary>
internal sealed class EmulationService
{
    private ViGEmClient? _client;
    private IXbox360Controller? _xbox;
    private IDualShock4Controller? _ds4;
    private string? _hiddenInstance;
    private readonly object _lock = new();

    private TcpClient? _viiperStream;
    private int _viiperBus = -1;

    public bool Running => _xbox is not null || _ds4 is not null || _viiperStream is not null;
    public string Status { get; private set; } = "Desligada.";
    public event Action? Changed;

    public bool Start()
    {
        Stop();
        var e = AppServices.Settings.Emulation;
        if (e.Target is VirtualTarget.DualSense or VirtualTarget.DualSenseEdge) return StartViiper(e);
        try
        {
            _client = new ViGEmClient();
        }
        catch (VigemBusNotFoundException)
        {
            Status = "O driver do controle virtual (ViGEmBus) não está instalado. Instale no passo 1 e reinicie o PC se pedir.";
            Changed?.Invoke();
            return false;
        }
        catch (Exception ex) when (ex is VigemBusAccessFailedException or VigemBusVersionMismatchException)
        {
            Status = "O driver do controle virtual não respondeu: " + ex.Message;
            Changed?.Invoke();
            return false;
        }

        try
        {
            if (e.Target == VirtualTarget.DualShock4)
            {
                var ds4 = _client.CreateDualShock4Controller();
                ds4.AutoSubmitReport = false;
                ds4.FeedbackReceived += (_, f) => AppServices.Pads.SetRumble(f.LargeMotor, f.SmallMotor);
                ds4.Connect();
                _ds4 = ds4;
            }
            else
            {
                var x = _client.CreateXbox360Controller();
                x.AutoSubmitReport = false;
                x.FeedbackReceived += (_, f) => AppServices.Pads.SetRumble(f.LargeMotor, f.SmallMotor);
                x.Connect();
                _xbox = x;
                try { AppServices.Pads.IgnoreXInputSlot = x.UserIndex; } catch (Exception) { /* índice só sai depois de o Windows montar o controle */ }
            }
        }
        catch (Exception ex) when (ex is VigemTargetUninitializedException or VigemAlreadyConnectedException or VigemNoFreeSlotException or InvalidOperationException)
        {
            Status = "Não foi possível criar o controle virtual: " + ex.Message;
            Stop();
            return false;
        }

        AppServices.Pads.Attach();
        AppServices.Pads.StateRead += Send;
        if (e.HidePhysical) Hide();
        Status = (e.Target == VirtualTarget.DualShock4 ? "Ligada: os jogos veem um controle de PS4." : "Ligada: os jogos veem um controle de Xbox 360.")
                 + (_hiddenInstance is not null ? " O controle físico está escondido." : e.HidePhysical ? " (HidHide não encontrado: o jogo pode ver os dois controles.)" : "");
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// PS5 virtual: o VIIPER (programa à parte, instalado pela pessoa) cria um DualSense ou DualSense Edge por USBIP; o MB manda o estado
    /// do controle pela API local e recebe a vibração do jogo de volta.
    /// </summary>
    private bool StartViiper(EmulationSettings e)
    {
        var client = new ViiperClient();
        if (client.Ping() is null)
        {
            // instalado e parado: abre o servidor escondido e espera ele responder
            string exe = ViiperClient.DefaultExe;
            if (File.Exists(exe))
            {
                try { Process.Start(new ProcessStartInfo(exe, "server --update-notify=none") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden })?.Dispose(); }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
                for (int i = 0; i < 16 && client.Ping() is null; i++) Thread.Sleep(250);
            }
            if (client.Ping() is null)
            {
                Status = File.Exists(exe)
                    ? "O VIIPER (controle PS5 virtual) não abriu. Abra o VIIPER pelo menu Iniciar e ligue de novo."
                    : "Para o PS5 virtual, instale o VIIPER (passo 1). Ele instala também o USBIP; o Windows pode pedir para reiniciar.";
                Changed?.Invoke();
                return false;
            }
        }

        var before = SafePads();
        try
        {
            _viiperBus = client.CreateBus();
            string dev = client.AddDevice(_viiperBus, ViiperWire.DeviceType(e.Target));
            _viiperStream = client.OpenStream(_viiperBus, dev);
        }
        catch (Exception ex) when (ex is IOException or SocketException or AggregateException or InvalidOperationException or System.Text.Json.JsonException or KeyNotFoundException)
        {
            Status = "Não foi possível criar o PS5 virtual: " + ex.Message;
            Stop();
            return false;
        }

        // o DualSense virtual aparece como um controle de verdade: o MB não pode ler ele (senão leria a própria saída)
        _ = Task.Run(async () =>
        {
            for (int i = 0; i < 20 && _viiperStream is not null; i++)
            {
                await Task.Delay(300);
                foreach (string path in SafePads().Except(before, StringComparer.OrdinalIgnoreCase)) PadService.IgnoredPaths[path] = 0;
            }
        });

        // vibração pedida pelo jogo → controle físico
        var stream = _viiperStream;
        var reader = new Thread(() =>
        {
            var buffer = new byte[ViiperWire.DualSenseFeedbackSize];
            try
            {
                var net = stream.GetStream();
                while (true)
                {
                    int got = 0;
                    while (got < buffer.Length)
                    {
                        int n = net.Read(buffer, got, buffer.Length - got);
                        if (n <= 0) return;
                        got += n;
                    }
                    if (ViiperWire.ParseFeedback(buffer) is { } f) AppServices.Pads.SetRumble(f.RumbleLarge, f.RumbleSmall);
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException or SocketException) { }
        }) { IsBackground = true, Name = "MB VIIPER retorno" };
        reader.Start();

        AppServices.Pads.Attach();
        AppServices.Pads.StateRead += Send;
        if (e.HidePhysical) Hide();
        Status = (e.Target == VirtualTarget.DualSenseEdge ? "Ligada: os jogos veem um DualSense Edge (PS5)." : "Ligada: os jogos veem um controle de PS5 (DualSense).")
                 + (_hiddenInstance is not null ? " O controle físico está escondido." : e.HidePhysical ? " (HidHide não encontrado: o jogo pode ver os dois controles.)" : "");
        Changed?.Invoke();
        return true;
    }

    private static HashSet<string> SafePads()
    {
        try { return WinHid.FindPads().Select(p => p.Path).ToHashSet(StringComparer.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or DllNotFoundException) { return new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
    }

    public void Stop()
    {
        bool was = Running;
        AppServices.Pads.StateRead -= Send;
        lock (_lock)
        {
            try { _viiperStream?.Dispose(); } catch (Exception) { }
            _viiperStream = null;
            if (_viiperBus >= 0) { int bus = _viiperBus; _ = Task.Run(() => new ViiperClient().RemoveBus(bus)); }
            _viiperBus = -1;
            try { _xbox?.Disconnect(); } catch (Exception) { }
            try { _ds4?.Disconnect(); } catch (Exception) { }
            _xbox = null;
            _ds4 = null;
            _client?.Dispose();
            _client = null;
        }
        AppServices.Pads.IgnoreXInputSlot = null;
        AppServices.Pads.SetRumble(0, 0);
        Unhide();
        if (was) AppServices.Pads.Detach();
        _filter.Reset();
        _lastTicks = 0;
        Status = "Desligada.";
        Changed?.Invoke();
    }

    private readonly PadFilter _filter = new();
    private long _lastTicks;

    private void Send(PadState physical)
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        double dt = _lastTicks == 0 ? 0 : Math.Min(0.1, (now - _lastTicks) / (double)System.Diagnostics.Stopwatch.Frequency);
        _lastTicks = now;
        var s = PadMapper.Map(physical, AppServices.Settings.Emulation, _filter, dt);
        lock (_lock)
        {
            try
            {
                if (_xbox is { } x) SendXbox(x, s);
                else if (_ds4 is { } d) SendDs4(d, s);
                else if (_viiperStream is { } v) v.GetStream().Write(ViiperWire.DualSense(s));
            }
            catch (Exception) { /* controle virtual removido no meio: o próximo Start recria */ }
        }
    }

    private static bool B(PadState s, PadButtons b) => (s.Buttons & b) != 0;

    internal static void SendXbox(IXbox360Controller x, PadState s)
    {
        x.SetButtonState(Xbox360Button.A, B(s, PadButtons.Cross));
        x.SetButtonState(Xbox360Button.B, B(s, PadButtons.Circle));
        x.SetButtonState(Xbox360Button.X, B(s, PadButtons.Square));
        x.SetButtonState(Xbox360Button.Y, B(s, PadButtons.Triangle));
        x.SetButtonState(Xbox360Button.LeftShoulder, B(s, PadButtons.L1));
        x.SetButtonState(Xbox360Button.RightShoulder, B(s, PadButtons.R1));
        x.SetButtonState(Xbox360Button.Back, B(s, PadButtons.Create) || B(s, PadButtons.Touchpad));
        x.SetButtonState(Xbox360Button.Start, B(s, PadButtons.Options));
        x.SetButtonState(Xbox360Button.LeftThumb, B(s, PadButtons.L3));
        x.SetButtonState(Xbox360Button.RightThumb, B(s, PadButtons.R3));
        x.SetButtonState(Xbox360Button.Guide, B(s, PadButtons.PS));
        x.SetButtonState(Xbox360Button.Up, B(s, PadButtons.Up));
        x.SetButtonState(Xbox360Button.Down, B(s, PadButtons.Down));
        x.SetButtonState(Xbox360Button.Left, B(s, PadButtons.Left));
        x.SetButtonState(Xbox360Button.Right, B(s, PadButtons.Right));
        x.SetAxisValue(Xbox360Axis.LeftThumbX, PadMapper.XboxAxis(s.LX));
        x.SetAxisValue(Xbox360Axis.LeftThumbY, PadMapper.XboxAxis(s.LY, invert: true));
        x.SetAxisValue(Xbox360Axis.RightThumbX, PadMapper.XboxAxis(s.RX));
        x.SetAxisValue(Xbox360Axis.RightThumbY, PadMapper.XboxAxis(s.RY, invert: true));
        x.SetSliderValue(Xbox360Slider.LeftTrigger, PadMapper.TriggerByte(s.L2));
        x.SetSliderValue(Xbox360Slider.RightTrigger, PadMapper.TriggerByte(s.R2));
        x.SubmitReport();
    }

    internal static void SendDs4(IDualShock4Controller d, PadState s)
    {
        d.SetButtonState(DualShock4Button.Cross, B(s, PadButtons.Cross));
        d.SetButtonState(DualShock4Button.Circle, B(s, PadButtons.Circle));
        d.SetButtonState(DualShock4Button.Square, B(s, PadButtons.Square));
        d.SetButtonState(DualShock4Button.Triangle, B(s, PadButtons.Triangle));
        d.SetButtonState(DualShock4Button.ShoulderLeft, B(s, PadButtons.L1));
        d.SetButtonState(DualShock4Button.ShoulderRight, B(s, PadButtons.R1));
        d.SetButtonState(DualShock4Button.TriggerLeft, s.L2 > 0.05);
        d.SetButtonState(DualShock4Button.TriggerRight, s.R2 > 0.05);
        d.SetButtonState(DualShock4Button.Share, B(s, PadButtons.Create));
        d.SetButtonState(DualShock4Button.Options, B(s, PadButtons.Options));
        d.SetButtonState(DualShock4Button.ThumbLeft, B(s, PadButtons.L3));
        d.SetButtonState(DualShock4Button.ThumbRight, B(s, PadButtons.R3));
        d.SetButtonState(DualShock4SpecialButton.Ps, B(s, PadButtons.PS));
        d.SetButtonState(DualShock4SpecialButton.Touchpad, B(s, PadButtons.Touchpad));
        bool up = B(s, PadButtons.Up), down = B(s, PadButtons.Down), left = B(s, PadButtons.Left), right = B(s, PadButtons.Right);
        d.SetDPadDirection(up && left ? DualShock4DPadDirection.Northwest : up && right ? DualShock4DPadDirection.Northeast
            : down && left ? DualShock4DPadDirection.Southwest : down && right ? DualShock4DPadDirection.Southeast
            : up ? DualShock4DPadDirection.North : down ? DualShock4DPadDirection.South : left ? DualShock4DPadDirection.West
            : right ? DualShock4DPadDirection.East : DualShock4DPadDirection.None);
        d.SetAxisValue(DualShock4Axis.LeftThumbX, PadMapper.Ds4Axis(s.LX));
        d.SetAxisValue(DualShock4Axis.LeftThumbY, PadMapper.Ds4Axis(s.LY));
        d.SetAxisValue(DualShock4Axis.RightThumbX, PadMapper.Ds4Axis(s.RX));
        d.SetAxisValue(DualShock4Axis.RightThumbY, PadMapper.Ds4Axis(s.RY));
        d.SetSliderValue(DualShock4Slider.LeftTrigger, PadMapper.TriggerByte(s.L2));
        d.SetSliderValue(DualShock4Slider.RightTrigger, PadMapper.TriggerByte(s.R2));
        d.SubmitReport();
    }

    // ------------------------------------------------------------ HidHide (esconder o controle físico)

    public static string? HidHideCli()
    {
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        foreach (string p in new[] { Path.Combine(pf, "Nefarius Software Solutions", "HidHide", "x64", "HidHideCLI.exe"), Path.Combine(pf, "Nefarius Software Solutions", "HidHide", "HidHideCLI.exe") })
            if (File.Exists(p)) return p;
        return null;
    }

    private static bool RunCli(string args)
    {
        string? cli = HidHideCli();
        if (cli is null) return false;
        try
        {
            using var p = Process.Start(new ProcessStartInfo(cli, args) { CreateNoWindow = true, UseShellExecute = false });
            return p is not null && p.WaitForExit(8000) && p.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return false; }
    }

    private void Hide()
    {
        string? instance = PadMapper.InstanceId(AppServices.Pads.Current?.Hid?.Path);
        if (instance is null || HidHideCli() is null) return;
        string exe = Environment.ProcessPath ?? "";
        if (exe.Length > 0) RunCli($"--app-reg \"{exe}\"");
        if (RunCli($"--dev-hide \"{instance}\"") && RunCli("--cloak-on")) _hiddenInstance = instance;
    }

    private void Unhide()
    {
        if (_hiddenInstance is null) return;
        RunCli($"--dev-unhide \"{_hiddenInstance}\"");
        _hiddenInstance = null;
    }
}
