using MBExtreme.Core.Hardware;
using MBExtreme.Core.Network;
using MBExtreme.Core.Recording;
using MBExtreme.Core.Sensors;
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

    /// <summary>Configurações do usuário: uma única cópia, para um salvamento não apagar o do outro.</summary>
    public static MBExtreme.Core.Settings.AppSettings Settings { get; } = MBExtreme.Core.Settings.AppSettings.Load();

    public static NetworkMonitor Network { get; } = new();

    public static OverlayController Overlay { get; } = new();

    public static BoostController Boost { get; } = new();

    public static HotkeyManager Hotkeys { get; } = new();

    public static MemoryAutoCleaner AutoMemory { get; } = new();

    public static StudioController Studio { get; } = new();

    public static ScreenRecorder Recorder { get; } = new();

    /// <summary>Codificadores de GPU já testados nesta sessão (o teste leva alguns segundos).</summary>
    public static IReadOnlyList<EncoderOption>? CachedEncoders { get; set; }

    // Abrir os sensores carrega um driver e pode demorar: é feito em segundo plano, uma única vez.
    private static readonly Lazy<Task<SensorService>> SensorsLazy = new(() => Task.Run(() => new SensorService()));

    public static Task<SensorService> Sensors => SensorsLazy.Value;

    // Instância única do monitor completo, aberta uma vez e mantida até o fim: fechar a biblioteca de sensores
    // ao sair da página poderia descarregar o driver usado pelo Painel e pelo overlay.
    private static readonly Lazy<Task<HardwareMonitorService>> MonitorLazy = new(() => Task.Run(() =>
    {
        var monitor = new HardwareMonitorService();
        if (monitor.IsAvailable) monitor.ReadAll(); // leitura de aquecimento: alguns sensores só têm valor na segunda atualização
        return monitor;
    }));

    public static Task<HardwareMonitorService> Monitor => MonitorLazy.Value;

    private static Task<HardwareInventory>? _hardware;

    /// <summary>Leitura do hardware, feita uma vez e reaproveitada pelas páginas. <paramref name="refresh"/> lê de novo.</summary>
    public static Task<HardwareInventory> Hardware(bool refresh = false)
    {
        if (refresh || _hardware is null) _hardware = Task.Run(HardwareService.Collect);
        return _hardware;
    }

    public static void Shutdown()
    {
        AutoMemory.Shutdown();
        Studio.Shutdown();
        Hotkeys.Shutdown();
        Overlay.Shutdown();
        Boost.Shutdown();
        if (Recorder.IsRecording) Recorder.Stop();
        try
        {
            if (MonitorLazy.IsValueCreated && MonitorLazy.Value.IsCompletedSuccessfully)
                MonitorLazy.Value.Result.Dispose();
            if (SensorsLazy.IsValueCreated && SensorsLazy.Value.IsCompletedSuccessfully)
                SensorsLazy.Value.Result.Dispose();
        }
        catch (Exception)
        {
            // fechando o programa: falha ao liberar o driver de sensores não deve mostrar erro
        }
    }
}
