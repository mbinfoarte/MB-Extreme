using System.Diagnostics;
using System.IO;
using MBExtreme.Core.Gamepads;
using MBExtreme.UI.Pages;

namespace MBExtreme.UI;

/// <summary>Um controle que o MB Control pode usar.</summary>
internal sealed record PadDevice(string Key, string Name, PadConnection Connection, PadModel? Sony, HidPadInfo? Hid, uint? XInputSlot)
{
    public bool IsSony => Sony is not null && Hid is not null;
}

/// <summary>
/// Lê o controle escolhido numa linha própria (USB e Bluetooth) e guarda o último estado, a taxa de atualização medida,
/// as informações do aparelho e os tempos conectado/parado. As telas do MB Control leem daqui.
/// </summary>
internal sealed class PadService
{
    private readonly object _lock = new();
    private readonly Queue<double> _times = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private CancellationTokenSource? _cts;
    private int _users;

    public IReadOnlyList<PadDevice> Devices { get; private set; } = Array.Empty<PadDevice>();
    public PadDevice? Current { get; private set; }
    public PadState State { get; private set; } = PadState.Empty;
    public PadInfo? Info { get; private set; }
    public DateTime? ConnectedAt { get; private set; }
    public DateTime LastChange { get; private set; } = DateTime.Now;
    public string Status { get; private set; } = "Procurando controles...";

    /// <summary>Cada leitura nova do controle (na linha de leitura): a emulação usa para mandar ao controle virtual.</summary>
    public event Action<PadState>? StateRead;

    /// <summary>Slot XInput do controle virtual criado pelo MB (não aparece na lista para não ler a si mesmo).</summary>
    public int? IgnoreXInputSlot { get; set; }

    /// <summary>Algo mudou (lista, controle escolhido ou informações). Chamado fora da linha da tela.</summary>
    public event Action? Changed;

    /// <summary>Pacotes por segundo nos últimos 2 segundos (0 se o controle está parado ou não manda nada).</summary>
    public double RateHz
    {
        get
        {
            lock (_lock)
            {
                double now = _clock.Elapsed.TotalMilliseconds;
                while (_times.Count > 0 && now - _times.Peek() > 2000) _times.Dequeue();
                if (_times.Count < 10) return 0;
                double span = _times.Last() - _times.Peek();
                return span <= 0 ? 0 : (_times.Count - 1) * 1000.0 / span;
            }
        }
    }

    public string DisplayName(PadDevice d)
    {
        string? key = Info?.BluetoothAddress ?? Info?.Serial;
        return d == Current && key is not null && AppServices.Settings.PadNames.TryGetValue(key, out var name) && name.Length > 0 ? name : d.Name;
    }

    /// <summary>Uma tela começou a usar o serviço (liga a leitura).</summary>
    public void Attach()
    {
        if (Interlocked.Increment(ref _users) != 1) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        new Thread(() => Loop(token)) { IsBackground = true, Name = "MB Control" }.Start();
    }

    /// <summary>A tela fechou; sem telas abertas a leitura para.</summary>
    public void Detach()
    {
        if (Interlocked.Decrement(ref _users) == 0) _cts?.Cancel();
    }

    public void Select(string key)
    {
        _wanted = key;
        _reopen = true;
    }

    private volatile bool _outputDirty = true;
    private volatile bool _resetLight;
    private (byte Strong, byte Weak, DateTime Until) _rumble;
    private byte _sequence;

    /// <summary>A luz/gatilhos mudaram: manda de novo para o controle.</summary>
    public void OutputChanged()
    {
        if (!AppServices.Settings.PadLight.Enabled) _resetLight = true;
        _outputDirty = true;
    }

    /// <summary>Vibração pedida pelo jogo (controle virtual): fica ligada até mudar; 0 e 0 desliga.</summary>
    public void SetRumble(byte strong, byte weak)
    {
        if (strong == 0 && weak == 0) { if (_rumble.Until != default) _rumble = (0, 0, DateTime.Now); }
        else _rumble = (strong, weak, DateTime.Now.AddSeconds(30));
        _outputDirty = true;
    }

    /// <summary>Testa a vibração por <paramref name="ms"/> milissegundos (0-255 em cada motor).</summary>
    public void Rumble(byte strong, byte weak, int ms)
    {
        _rumble = (strong, weak, DateTime.Now.AddMilliseconds(ms));
        _outputDirty = true;
    }

    private DateTime _lastSend;

    private void SendOutput(PadDevice d, FileStream? writer, int length)
    {
        if (writer is null || d.Sony is not { } model) return;
        var light = AppServices.Settings.PadLight;
        var now = DateTime.Now;
        bool rumbling = now < _rumble.Until;
        bool rumbleEnded = !rumbling && _rumble.Until != default;
        bool animated = light.Enabled && light.Mode != LightMode.Static;
        if (!_outputDirty && !rumbling && !rumbleEnded && !_resetLight && !(animated && (now - _lastSend).TotalMilliseconds >= 33)) return;
        if (!light.Enabled && !rumbling && !rumbleEnded && !_resetLight) { _outputDirty = false; return; }
        if ((now - _lastSend).TotalMilliseconds < 15) return;

        PadOutputFrame frame;
        if (light.Enabled)
        {
            var (r, g, b) = PadOutput.LightColor(light, _clock.Elapsed.TotalMilliseconds, State.Battery);
            frame = new PadOutputFrame(r, g, b, PadOutput.PlayerMask(light.PlayerLeds), light.PlayerBrightness, light.Mute, light.Left, light.Right,
                rumbling ? _rumble.Strong : (byte)0, rumbling ? _rumble.Weak : (byte)0);
        }
        else
        {
            // sem personalização: azul padrão, jogador 1, gatilhos livres (só a vibração de teste, se houver)
            frame = new PadOutputFrame(0, 0, 64, PadOutput.PlayerMask(1), 0, MuteLed.Off, new TriggerEffect(), new TriggerEffect(),
                rumbling ? _rumble.Strong : (byte)0, rumbling ? _rumble.Weak : (byte)0);
        }
        bool bt = d.Connection == PadConnection.Bluetooth;
        byte[] report = model == PadModel.DualShock4 ? PadOutput.DualShock4(frame, bt) : PadOutput.DualSense(frame, bt, _sequence++);
        var buffer = new byte[Math.Max(report.Length, length)];
        report.CopyTo(buffer, 0);
        try { writer.Write(buffer, 0, buffer.Length); }
        catch (IOException) { }
        _lastSend = now;
        _outputDirty = false;
        _resetLight = false;
        if (rumbleEnded) _rumble = default;
    }

    private volatile string? _wanted;
    private volatile bool _reopen;

    private static List<PadDevice> Scan()
    {
        var list = new List<PadDevice>();
        try
        {
            foreach (var p in WinHid.FindPads())
            {
                var conn = PadDetails.ConnectionOf(p.InputLength, p.Path);
                list.Add(new PadDevice(p.Path, PadIds.Name(p.Model), conn, p.Model, p, null));
            }
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or DllNotFoundException) { }
        for (uint i = 0; i < 4; i++)
            if (AppServices.Pads.IgnoreXInputSlot != (int)i && XInput.TryState(i, out _)) list.Add(new PadDevice("xinput" + i, $"Controle Xbox {i + 1}", PadConnection.Unknown, null, null, i));
        return list;
    }

    private void Loop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var devices = Scan();
            Devices = devices;
            var pick = devices.FirstOrDefault(d => d.Key == _wanted) ?? devices.FirstOrDefault();
            _reopen = false;
            if (pick is null)
            {
                Current = null;
                Info = null;
                ConnectedAt = null;
                Status = "Nenhum controle encontrado. Ligue pelo cabo USB ou pareie pelo Bluetooth do Windows.";
                Changed?.Invoke();
                Sleep(1500, token);
                continue;
            }
            if (Current?.Key != pick.Key) { ConnectedAt = DateTime.Now; Info = null; }
            Current = pick;
            Status = "";
            Changed?.Invoke();
            try
            {
                if (pick.IsSony) ReadSony(pick, token);
                else if (pick.XInputSlot is { } slot) ReadXInput(slot, token);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                Status = ex.Message;
                Changed?.Invoke();
                Sleep(1000, token);
            }
        }
    }

    private static void Sleep(int ms, CancellationToken token) => token.WaitHandle.WaitOne(ms);

    private void Mark(PadState s)
    {
        lock (_lock)
        {
            _times.Enqueue(_clock.Elapsed.TotalMilliseconds);
            if (_times.Count > 4000) _times.Dequeue();
        }
        if (!Same(s, State)) LastChange = DateTime.Now;
        State = s;
        StateRead?.Invoke(s);
    }

    private static bool Same(PadState a, PadState b) =>
        a.Buttons == b.Buttons && Math.Abs(a.LX - b.LX) < 0.02 && Math.Abs(a.LY - b.LY) < 0.02 && Math.Abs(a.RX - b.RX) < 0.02 &&
        Math.Abs(a.RY - b.RY) < 0.02 && Math.Abs(a.L2 - b.L2) < 0.02 && Math.Abs(a.R2 - b.R2) < 0.02;

    private void ReadSony(PadDevice d, CancellationToken token)
    {
        using var hid = WinHid.OpenPad(d.Hid!);
        using var writer = WinHid.OpenOutput(d.Hid!.Path, out int outLength);
        _outputDirty = true;
        // no Bluetooth o controle só manda o relatório completo (com sensores e bateria) depois deste pedido
        try { hid.GetFeature(d.Sony == PadModel.DualShock4 ? (byte)0x02 : (byte)0x05); }
        catch (IOException) { }
        _ = Task.Run(() =>
        {
            try { Info = new SonyPad(hid, d.Sony!.Value).ReadInfo(); Changed?.Invoke(); }
            catch (Exception ex) when (ex is IOException or PadCommandException or TimeoutException or ObjectDisposedException) { }
        }, token);
        int misses = 0;
        while (!token.IsCancellationRequested && !_reopen)
        {
            SendOutput(d, writer, outLength);
            var report = hid.ReadInput(100);
            if (report is null)
            {
                if (++misses > 30) return;   // sumiu (desligou ou tirou o cabo): procura de novo
                continue;
            }
            misses = 0;
            if (PadInput.ParseAny(d.Sony!.Value, report) is { } state) Mark(state);
        }
    }

    private void ReadXInput(uint slot, CancellationToken token)
    {
        uint last = uint.MaxValue;
        int misses = 0;
        while (!token.IsCancellationRequested && !_reopen)
        {
            if (!XInput.TryState(slot, out var x))
            {
                if (++misses > 200) return;
                Thread.Sleep(5);
                continue;
            }
            misses = 0;
            if (x.PacketNumber != last)
            {
                last = x.PacketNumber;
                Mark(FromXInput(x));
            }
            Thread.Sleep(1);
        }
    }

    internal static PadState FromXInput(XInput.State x)
    {
        var b = PadButtons.None;
        ushort w = x.Buttons;
        if ((w & 0x0001) != 0) b |= PadButtons.Up;
        if ((w & 0x0002) != 0) b |= PadButtons.Down;
        if ((w & 0x0004) != 0) b |= PadButtons.Left;
        if ((w & 0x0008) != 0) b |= PadButtons.Right;
        if ((w & 0x0010) != 0) b |= PadButtons.Options;
        if ((w & 0x0020) != 0) b |= PadButtons.Create;
        if ((w & 0x0040) != 0) b |= PadButtons.L3;
        if ((w & 0x0080) != 0) b |= PadButtons.R3;
        if ((w & 0x0100) != 0) b |= PadButtons.L1;
        if ((w & 0x0200) != 0) b |= PadButtons.R1;
        if ((w & 0x1000) != 0) b |= PadButtons.Cross;
        if ((w & 0x2000) != 0) b |= PadButtons.Circle;
        if ((w & 0x4000) != 0) b |= PadButtons.Square;
        if ((w & 0x8000) != 0) b |= PadButtons.Triangle;
        static double A(short v) => Math.Round(Math.Clamp(v / 32767.0, -1, 1), 4);
        return new PadState(A(x.LX), -A(x.LY), A(x.RX), -A(x.RY), x.LeftTrigger / 255.0, x.RightTrigger / 255.0, b, 0, 0, 0, 0, 0, 0, -1, false, false);
    }
}
