using System.Runtime.Versioning;
using Microsoft.Win32;

namespace MBExtreme.Core.Gamepads;

/// <summary>Estado de um componente necessário (ou útil) para a emulação de controle.</summary>
public sealed record DriverComponent(string Id, string Title, string Purpose, string Need, bool Installed, string Url);

/// <summary>Verifica os drivers da emulação: controle virtual (ViGEmBus), esconder o controle físico (HidHide), teclado/mouse virtuais (FakerInput).</summary>
public static class EmulationDrivers
{
    public const string ViGEmUrl = "https://github.com/nefarius/ViGEmBus/releases/latest";
    public const string HidHideUrl = "https://github.com/nefarius/HidHide/releases/latest";
    public const string FakerInputUrl = "https://github.com/Ryochan7/FakerInput/releases/latest";
    public const string VcRedistUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe";

    /// <summary>Monta a lista a partir de uma função que diz se um serviço/chave existe (permite testar sem Windows).</summary>
    public static IReadOnlyList<DriverComponent> Check(Func<string, bool> serviceExists, Func<bool> vcRedist) => Check(serviceExists, vcRedist, () => false);

    public static IReadOnlyList<DriverComponent> Check(Func<string, bool> serviceExists, Func<bool> vcRedist, Func<bool> viiper) => new[]
    {
        new DriverComponent("vigem", "Controle virtual (ViGEmBus)", "Cria o controle que os jogos enxergam: Xbox 360 ou PS4.", "obrigatório",
            serviceExists("ViGEmBus"), ViGEmUrl),
        new DriverComponent("viiper", "Controle PS5 virtual (VIIPER + USBIP)", "Cria um DualSense (PS5) ou DualSense Edge virtual, com os ícones e recursos de PS5 nos jogos.", "para PS5",
            viiper(), ViiperWire.ReleasesUrl),
        new DriverComponent("hidhide", "Esconder o controle físico (HidHide)", "Evita que o jogo receba cada comando duas vezes (o controle de verdade e o virtual).", "recomendado",
            serviceExists("HidHide"), HidHideUrl),
        new DriverComponent("fakerinput", "Teclado e mouse virtuais (FakerInput)", "Só para jogos que ignoram o teclado e o mouse simulados pelo Windows.", "opcional",
            serviceExists("FakerInput"), FakerInputUrl),
        new DriverComponent("vcredist", "Componentes do Windows (Visual C++)", "Bibliotecas que os drivers usam.", "verificado",
            vcRedist(), VcRedistUrl),
    };

    /// <summary>Pronto para emular quando o controle virtual está instalado.</summary>
    public static bool Ready(IReadOnlyList<DriverComponent> list) => list.Any(c => c.Id == "vigem" && c.Installed);

    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<DriverComponent> CheckWindows() => Check(ServiceExists, VcRedistInstalled,
        () => File.Exists(ViiperClient.DefaultExe) || new ViiperClient().Ping() is not null);

    /// <summary>O controle virtual escolhido tem o que precisa (PS5 pelo VIIPER; Xbox e PS4 pelo ViGEmBus).</summary>
    public static bool ReadyFor(IReadOnlyList<DriverComponent> list, VirtualTarget target) =>
        target is VirtualTarget.DualSense or VirtualTarget.DualSenseEdge
            ? list.Any(c => c.Id == "viiper" && c.Installed)
            : Ready(list);

    [SupportedOSPlatform("windows")]
    private static bool ServiceExists(string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name);
            return key is not null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return false; }
    }

    [SupportedOSPlatform("windows")]
    private static bool VcRedistInstalled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64")
                ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\x64");
            return key?.GetValue("Installed") is int v && v == 1;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return false; }
    }
}
