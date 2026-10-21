using MBExtreme.Core.Network;
using MBExtreme.Core.Sensors;
using MBExtreme.Core.Tweaks;
using MBExtreme.UI.Overlay;
using MBExtreme.UI.Recording;

namespace MBExtreme.UI;

/// <summary>Objetos compartilhados entre as páginas (o backup dos ajustes é um só).</summary>
internal static class AppServices
{
    public static TweakStore TweakStore { get; } = new();

    public static IReadOnlyList<Tweak> Tweaks { get; } = TweakCatalog.Create(TweakStore);

    public static NetworkMonitor Network { get; } = new();

    public static OverlayController Overlay { get; } = new();

    public static ScreenRecorder Recorder { get; } = new();

    // Abrir os sensores carrega um driver e pode demorar: é feito em segundo plano, uma única vez.
    private static readonly Lazy<Task<SensorService>> SensorsLazy = new(() => Task.Run(() => new SensorService()));

    public static Task<SensorService> Sensors => SensorsLazy.Value;

    public static void Shutdown()
    {
        Overlay.Shutdown();
        if (Recorder.IsRecording) Recorder.Stop();
        if (SensorsLazy.IsValueCreated && SensorsLazy.Value.IsCompletedSuccessfully)
            SensorsLazy.Value.Result.Dispose();
    }
}
