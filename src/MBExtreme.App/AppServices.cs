using MBExtreme.Core.Network;
using MBExtreme.Core.Sensors;
using MBExtreme.Core.Tweaks;

namespace MBExtreme.UI;

/// <summary>Objetos compartilhados entre as páginas (o backup dos ajustes é um só).</summary>
internal static class AppServices
{
    public static TweakStore TweakStore { get; } = new();

    public static IReadOnlyList<Tweak> Tweaks { get; } = TweakCatalog.Create(TweakStore);

    public static NetworkMonitor Network { get; } = new();

    // Abrir os sensores carrega um driver e pode demorar: é feito em segundo plano, uma única vez.
    private static readonly Lazy<Task<SensorService>> SensorsLazy = new(() => Task.Run(() => new SensorService()));

    public static Task<SensorService> Sensors => SensorsLazy.Value;

    public static void Shutdown()
    {
        if (SensorsLazy.IsValueCreated && SensorsLazy.Value.IsCompletedSuccessfully)
            SensorsLazy.Value.Result.Dispose();
    }
}
