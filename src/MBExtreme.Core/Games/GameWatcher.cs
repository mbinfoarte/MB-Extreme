using System.Diagnostics;
using System.Runtime.Versioning;

namespace MBExtreme.Core.Games;

/// <summary>Vigia se algum programa da lista de jogos abriu ou fechou (verifica a cada poucos segundos).</summary>
[SupportedOSPlatform("windows")]
public sealed class GameWatcher : IDisposable
{
    private readonly Timer _timer;
    private Func<IReadOnlyList<string>> _games = () => Array.Empty<string>();
    private string? _current;
    private int _busy;

    public GameWatcher() => _timer = new Timer(_ => Check(), null, Timeout.Infinite, Timeout.Infinite);

    /// <summary>Um jogo da lista abriu (nome, id do processo).</summary>
    public event Action<string, int>? GameStarted;

    /// <summary>O jogo que estava aberto fechou.</summary>
    public event Action<string>? GameStopped;

    public string? CurrentGame => _current;

    public void Start(Func<IReadOnlyList<string>> games)
    {
        _games = games;
        _timer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(3));
    }

    public void Stop()
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        if (_current is { } name)
        {
            _current = null;
            GameStopped?.Invoke(name);
        }
    }

    private void Check()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            CheckCore();
        }
        catch (Exception)
        {
            // Uma falha ao listar processos não pode derrubar o programa; a próxima verificação tenta de novo.
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private void CheckCore()
    {
        {
            if (_current is { } running)
            {
                if (!IsRunning(running, out _))
                {
                    _current = null;
                    GameStopped?.Invoke(running);
                }

                return;
            }

            foreach (string name in _games())
            {
                if (name.Length > 0 && IsRunning(name, out int pid))
                {
                    _current = name;
                    GameStarted?.Invoke(name, pid);
                    return;
                }
            }
        }
    }

    private static bool IsRunning(string name, out int pid)
    {
        pid = 0;
        try
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    pid = process.Id;
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // lista de processos indisponível neste instante
        }

        return false;
    }

    public void Dispose() => _timer.Dispose();
}
