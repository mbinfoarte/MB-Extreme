using MBExtreme.Core.Hardware;
using MBExtreme.Core.Network;
using MBExtreme.Core.Recording;
using MBExtreme.Core.Sensors;
using MBExtreme.Core.Sensors;
using MBExtreme.Core.Games;
using MBExtreme.Core.Tweaks;
using MBExtreme.UI.Overlay;
using MBExtreme.UI.Recording;

namespace MBExtreme.UI;

/// <summary>Objetos compartilhados entre as páginas (o backup dos ajustes é um só).</summary>
internal static class AppServices
{
    public static TweakStore TweakStore { get; } = new();

    public static IReadOnlyList<Tweak> Tweaks { get; } = TweakCatalog.Create(TweakStore);

    /// <summary>Ajustes que a pessoa pode aplicar no plano dela (os das páginas Pro ficam de fora no plano grátis).</summary>
    public static IReadOnlyList<Tweak> AllowedTweaks()
    {
        bool pro = !License.Config.Enforce || MBExtreme.Core.Licensing.LicenseExpiry.IsPro(License.Status);
        return pro ? Tweaks : Tweaks.Where(t => !MBExtreme.Core.Licensing.LicenseExpiry.ProTweakCategories.Contains(t.Category)).ToList();
    }

    /// <summary>Redes, ofertas, parceiros e suporte da MB InfoArt (arquivo online com cópia local; atualizado ao abrir).</summary>
    public static MBExtreme.Core.Social.ChannelConfig Channel { get; set; } = MBExtreme.Core.Social.ChannelConfig.Load();

    public static readonly System.Net.Http.HttpClient Web = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>Vídeo novo do canal (feed do YouTube).</summary>
    public static VideoWatcher Videos { get; } = new();

    /// <summary>Configurações do usuário: uma única cópia, para um salvamento não apagar o do outro.</summary>
    public static MBExtreme.Core.Settings.AppSettings Settings { get; } = LoadSettings();

    private static MBExtreme.Core.Settings.AppSettings LoadSettings()
    {
        var settings = MBExtreme.Core.Settings.AppSettings.Load();
        MBExtreme.Core.Cleanup.CleanupService.Excluded = settings.CleanupExcluded;   // pastas que a limpeza nunca toca
        return settings;
    }

    public static NetworkMonitor Network { get; } = new();

    public static OverlayController Overlay { get; } = new();

    public static BoostController Boost { get; } = new();
    public static GameLibraryStore GameLibrary { get; } = new();
    public static BottleneckController Bottleneck { get; } = new();
    internal static FanController Fans { get; } = new();
    internal static MBExtreme.UI.Remote.RemoteSupportController RemoteSupport { get; } = new();
    public static GamePriorityService GamePriority { get; } = new(TweakStore);
    public static GameOptimizationService GameOptimization { get; } = new(Tweaks, GameLibrary);
    public static PerfStore Perf { get; } = new();
    internal static ProLock ProLock { get; } = new();
    public static GameQosService GameQos { get; } = new();
    public static MBExtreme.Core.Network.NetReportStore NetReports { get; } = new();
    internal static GameMeasureController GameMeasure { get; } = new();
    public static NvApiDrs NvidiaDriver { get; } = new();
    public static GameDriverService GameDriver { get; } = new(NvidiaDriver, new RegistryGpuPreferenceStore(),
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "game-driver.json"));

    public static HotkeyManager Hotkeys { get; } = new();

    public static MemoryAutoCleaner AutoMemory { get; } = new();

    public static LicenseManager License { get; } = new();

    public static StudioController Studio { get; } = new();

    public static ScreenRecorder Recorder { get; } = new();

    /// <summary>Câmeras abertas uma vez só e compartilhadas entre a prévia e a gravação/live (relé, como no OBS).</summary>
    public static CameraHub Cameras { get; } = new();

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
        License.Shutdown();
        AutoMemory.Shutdown();
        Studio.Shutdown();
        Hotkeys.Shutdown();
        Fans.Shutdown();
        RemoteSupport.Stop();
        Bottleneck.Shutdown();
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
