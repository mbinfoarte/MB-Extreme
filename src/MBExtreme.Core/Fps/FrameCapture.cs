namespace MBExtreme.Core.Fps;

/// <summary>Estado ao vivo de uma captura: o programa escolhido, quantos quadros já foram e os últimos tempos de quadro (para o gráfico).</summary>
public sealed record LiveCapture(int ProcessId, string Name, int Frames, double ElapsedSeconds, double CurrentFps, IReadOnlyList<double> RecentFrameMs);

public sealed record CaptureResult(int ProcessId, string Name, IReadOnlyList<double> PresentSeconds);

/// <summary>
/// Guarda todos os instantes de apresentação de quadros dos programas durante uma captura e escolhe qual é o jogo: o que apresenta mais quadros agora
/// (ou o que tem o nome pedido). Sem dependência do Windows, para poder ser testado.
/// </summary>
public sealed class FrameCapture
{
    private const double RecentSeconds = 3.0;
    private const int MaxFramesPerProcess = 2_000_000;

    private readonly object _lock = new();
    private readonly Dictionary<int, List<double>> _times = new();
    private readonly string? _wantedName;
    private readonly Func<int, bool> _ignore;
    private readonly Func<int, string> _name;
    private double _first = double.NaN, _latest;
    private int? _chosen;

    public FrameCapture(string? wantedProcessName, Func<int, bool> ignore, Func<int, string> name)
    {
        _wantedName = string.IsNullOrWhiteSpace(wantedProcessName) ? null : wantedProcessName.Trim();
        _ignore = ignore;
        _name = name;
    }

    public void Record(int processId, double seconds)
    {
        lock (_lock)
        {
            if (double.IsNaN(_first)) _first = seconds;
            _latest = Math.Max(_latest, seconds);
            if (!_times.TryGetValue(processId, out var list))
            {
                if (_ignore(processId)) return;
                _times[processId] = list = new List<double>(4096);
            }
            if (list.Count < MaxFramesPerProcess) list.Add(seconds);
        }
    }

    private int RecentCount(List<double> list)
    {
        int count = 0;
        for (int i = list.Count - 1; i >= 0 && list[i] >= _latest - RecentSeconds; i--) count++;
        return count;
    }

    private bool MatchesWanted(int pid) =>
        _wantedName is null || _name(pid).Equals(_wantedName, StringComparison.OrdinalIgnoreCase)
        || _name(pid).Equals(Path.GetFileNameWithoutExtension(_wantedName), StringComparison.OrdinalIgnoreCase);

    /// <summary>Escolhe o jogo. Mantém a escolha enquanto o programa continuar apresentando quadros, para não pular entre janelas.</summary>
    private int? Choose()
    {
        if (_chosen is { } kept && _times.TryGetValue(kept, out var keptList) && RecentCount(keptList) >= 5) return kept;
        int? best = null;
        int bestCount = 9;   // abaixo de 10 quadros em 3 s não é jogo
        foreach (var (pid, list) in _times)
        {
            if (!MatchesWanted(pid)) continue;
            int count = RecentCount(list);
            if (count > bestCount) { best = pid; bestCount = count; }
        }
        return _chosen = best ?? _chosen;
    }

    public LiveCapture? Live(int recentFrames = 600)
    {
        lock (_lock)
        {
            if (Choose() is not { } pid || !_times.TryGetValue(pid, out var list) || list.Count < 2) return null;
            int from = Math.Max(0, list.Count - recentFrames - 1);
            var ms = new List<double>(list.Count - from);
            for (int i = from + 1; i < list.Count; i++)
            {
                double delta = list[i] - list[i - 1];
                if (delta > 0 && delta <= FrameAnalysis.MaxGapSeconds) ms.Add(delta * 1000.0);
            }
            int lastSecond = 0;
            for (int i = list.Count - 1; i >= 0 && list[i] >= _latest - 1.0; i--) lastSecond++;
            return new LiveCapture(pid, _name(pid), list.Count, _latest - list[0], lastSecond, ms);
        }
    }

    public CaptureResult? Finish()
    {
        lock (_lock)
        {
            if (Choose() is not { } pid || !_times.TryGetValue(pid, out var list) || list.Count < 2) return null;
            return new CaptureResult(pid, _name(pid), list.ToArray());
        }
    }
}
