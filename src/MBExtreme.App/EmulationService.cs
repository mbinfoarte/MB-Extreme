using System.Diagnostics;
using System.IO;
using MBExtreme.Core.Gamepads;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace MBExtreme.UI;

/// <summary>
/// Motor de emulação do MB Control: cria um controle virtual (Xbox 360 ou PS4) pelo ViGEmBus e repassa o controle físico,
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

    public bool Running => _xbox is not null || _ds4 is not null;
    public string Status { get; private set; } = "Desligada.";
    public event Action? Changed;

    public bool Start()
    {
        Stop();
        var e = AppServices.Settings.Emulation;
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

    public void Stop()
    {
        bool was = Running;
        AppServices.Pads.StateRead -= Send;
        lock (_lock)
        {
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
        Status = "Desligada.";
        Changed?.Invoke();
    }

    private void Send(PadState physical)
    {
        var s = PadMapper.Map(physical, AppServices.Settings.Emulation);
        lock (_lock)
        {
            try
            {
                if (_xbox is { } x) SendXbox(x, s);
                else if (_ds4 is { } d) SendDs4(d, s);
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
