using System.Threading;

namespace MBExtreme.UI;

/// <summary>Avisa a primeira cópia do programa quando alguém tenta abrir outra, para ela vir para frente.</summary>
internal static class SingleInstance
{
    private const string EventName = @"Local\MBExtreme-Mostrar";
    private static EventWaitHandle? _event;

    public static void SignalFirst()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(EventName, out var handle))
                using (handle) handle.Set();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException or System.IO.IOException) { }
    }

    public static void ListenForOthers(Action show)
    {
        _event = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        var thread = new Thread(() =>
        {
            while (true)
            {
                _event.WaitOne();
                show();
            }
        }) { IsBackground = true, Name = "Outra cópia do programa" };
        thread.Start();
    }
}
