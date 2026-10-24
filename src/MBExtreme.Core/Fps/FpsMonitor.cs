using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace MBExtreme.Core.Fps;

/// <summary>
/// Mede o FPS pelos eventos de apresentação de quadros do DXGI (Direct3D 9/10/11/12 em modo janela ou sem borda),
/// recebidos pelo ETW do Windows. Não injeta nada no jogo. Precisa de administrador.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FpsMonitor : IDisposable
{
    private const string SessionName = "MBExtremeFps";
    private static readonly Guid Dxgi = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private const int PresentStart = 42;

    private readonly PresentTracker _tracker = new();
    private readonly Dictionary<int, string> _names = new();
    private TraceEventSession? _session;
    private Task? _worker;
    private double _lastEvent;
    private volatile FrameCapture? _capture;

    public bool IsRunning => _session is not null;

    public void Start()
    {
        if (_session is not null) return;

        try { TraceEventSession.GetActiveSession(SessionName)?.Stop(); }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException) { /* sessão antiga não existe */ }

        var session = new TraceEventSession(SessionName) { StopOnDispose = true };
        session.EnableProvider(Dxgi);
        session.Source.AllEvents += OnEvent;
        _session = session;
        _worker = Task.Run(() => session.Source.Process());
    }

    private void OnEvent(TraceEvent data)
    {
        if (data.ProviderGuid != Dxgi || (int)(ushort)data.ID != PresentStart) return;
        double seconds = data.TimeStampRelativeMSec / 1000.0;
        _lastEvent = seconds;
        _tracker.Record(data.ProcessID, seconds);
        _capture?.Record(data.ProcessID, seconds);
    }

    /// <summary>Começa a guardar todos os quadros (para uma sessão de análise). Com <paramref name="processName"/>, só esse programa; sem nome, o que mais apresentar quadros.</summary>
    public void BeginCapture(string? processName = null)
    {
        if (_session is null) throw new InvalidOperationException("O monitor de FPS não está ligado.");
        _capture = new FrameCapture(processName, IsIgnored, ProcessName);
    }

    public bool IsCapturing => _capture is not null;

    public LiveCapture? CaptureLive(int recentFrames = 600) => _capture?.Live(recentFrames);

    /// <summary>Termina a captura e devolve todos os instantes de quadros do jogo (nulo se nenhum jogo apresentou quadros).</summary>
    public CaptureResult? EndCapture()
    {
        var capture = _capture;
        _capture = null;
        return capture?.Finish();
    }

    /// <summary>O programa que mais apresenta quadros agora (ignora o desktop e este aplicativo).</summary>
    public FpsReading? Current()
    {
        double now = _lastEvent;
        FpsReading? best = null;
        foreach (int pid in _tracker.Processes())
        {
            if (pid == Environment.ProcessId || IsIgnored(pid)) continue;
            var reading = _tracker.Read(pid, now);
            if (reading is { Fps: >= 1 } && (best is null || reading.Fps > best.Fps)) best = reading;
        }

        return best;
    }

    public string ProcessName(int processId)
    {
        lock (_names)
        {
            if (_names.TryGetValue(processId, out var cached)) return cached;
        }
        string name;
        try { name = Process.GetProcessById(processId).ProcessName; }
        catch (ArgumentException) { name = "processo " + processId; }
        lock (_names) return _names[processId] = name;
    }

    private bool IsIgnored(int pid)
    {
        string name = ProcessName(pid);
        return name.Equals("dwm", StringComparison.OrdinalIgnoreCase)
            || name.Equals("explorer", StringComparison.OrdinalIgnoreCase)
            || name.Equals("MBExtreme", StringComparison.OrdinalIgnoreCase);
    }

    public void Stop()
    {
        var session = _session;
        _session = null;
        session?.Dispose();
        try { _worker?.Wait(2000); }
        catch (AggregateException) { /* a sessão foi encerrada */ }
        _worker = null;
    }

    public void Dispose() => Stop();
}
