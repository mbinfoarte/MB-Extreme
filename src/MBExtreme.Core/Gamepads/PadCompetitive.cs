using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MBExtreme.Core.Hardware;
using MBExtreme.Core.Tweaks;
using MBExtreme.Core.Windows;
using Microsoft.Win32;

namespace MBExtreme.Core.Gamepads;

/// <summary>Níveis do Controle competitivo (modo futebol e corrida).</summary>
public enum CompetitiveLevel { Basico, Avancado, Extreme }

/// <summary>
/// O que cada nível do Controle competitivo liga. Tudo é ajuste da Central Desfazer (tem volta) ou opção do próprio programa.
/// </summary>
public static class PadCompetitive
{
    public const string UsbPower = "pad-usb-power", BluetoothPower = "pad-bt-power", InternalBluetoothOff = "pad-bt-internal-off", UsbMsi = "pad-usb-msi";

    /// <summary>Ajustes do Windows do nível (cada nível inclui os de baixo).</summary>
    public static IReadOnlyList<string> TweakIds(CompetitiveLevel level)
    {
        var ids = new List<string> { "power-usb-suspend", UsbPower, BluetoothPower };
        if (level >= CompetitiveLevel.Avancado) ids.AddRange(new[] { InternalBluetoothOff, "game-mmcss", "game-sysresponsiveness" });
        if (level >= CompetitiveLevel.Extreme) ids.Add(UsbMsi);
        return ids;
    }

    /// <summary>Opções do próprio programa: leitura direta (Avançado) e prioridade máxima com relógio de 1 ms (Extreme).</summary>
    public static (bool DirectRead, bool HighPriority) ProgramOptions(CompetitiveLevel level) =>
        (level >= CompetitiveLevel.Avancado, level >= CompetitiveLevel.Extreme);

    /// <summary>O que aparece em cada cartão de nível.</summary>
    public static IReadOnlyList<string> Items(CompetitiveLevel level) => level switch
    {
        CompetitiveLevel.Basico => new[]
        {
            "USB, hubs e controle sem economia de energia", "Adaptador Bluetooth sem economia de energia", "Avisos de fone Bluetooth e Wi-Fi em 2,4 GHz",
        },
        CompetitiveLevel.Avancado => new[]
        {
            "Tudo do Básico", "Desliga o Bluetooth interno quando há adaptador USB", "Prioridade de jogos no agendador do Windows",
            "Leitura direta do controle no programa", "Dicas no jogo: V-Sync, fps, Reflex/Anti-Lag e modo jogo da TV",
        },
        _ => new[] { "Tudo do Avançado", "Modo MSI nos controladores USB", "Prioridade máxima e relógio de 1 ms na leitura do controle", "Overclock 1000 Hz (se ativado na aba dele)" },
    };

    /// <summary>Dicas de configuração dentro do jogo (o que mais reduz atraso em futebol e corrida a 60 fps).</summary>
    public static IReadOnlyList<string> GameTips { get; } = new[]
    {
        "V-Sync desligado no jogo; use o G-Sync/FreeSync do monitor se tiver.",
        "Limite de fps alto (ou o dobro da taxa da TV) em vez de 60 fixo.",
        "NVIDIA Reflex ou AMD Anti-Lag ligado quando o jogo oferecer.",
        "Na TV: modo Jogo (ALLM) ligado e processamento de imagem desligado.",
    };
}

/// <summary>Estado do overclock pelo que foi medido e instalado.</summary>
public enum OverclockState { NotSupported, DriverMissing, NotActive, Partial, Active }

/// <summary>
/// Overclock da taxa do DualSense (PS5) e do DualShock 4 (PS4) no cabo, de 250 Hz para 1000 Hz, pelo driver hidusbf (gratuito, do autor
/// LordOfMice). As versões atuais são assinadas pela Microsoft: funcionam com o Secure Boot ligado e sem modo de teste. A taxa é escolhida
/// na janela do próprio driver (o programa mostra qual linha é o controle) e confirmada pela medição.
/// </summary>
public static class PadOverclock
{
    public const string RepositoryUrl = "https://github.com/LordOfMice/hidusbf";
    public const string DownloadUrl = "https://github.com/LordOfMice/hidusbf/raw/master/hidusbf.zip";
    public const int TargetHz = 1000;

    /// <summary>Só estes controles: DualShock 4 (as duas versões) e DualSense. Nenhum outro.</summary>
    public static bool IsSupported(ushort vendor, ushort product) => vendor == 0x054C && product is 0x05C4 or 0x09CC or 0x0CE6;

    public static string ModelName(ushort product) => product == 0x0CE6 ? "DualSense (PS5)" : "DualShock 4 (PS4)";

    /// <summary>Texto da linha do controle na janela do hidusbf, para a pessoa achar.</summary>
    public static string SetupLine(ushort product) => $"VID_054C&PID_{product:X4} ({(product == 0x0CE6 ? "Wireless Controller / DualSense" : "Wireless Controller")})";

    public static OverclockState StateFrom(bool supported, bool driverInstalled, double? measuredHz)
    {
        if (!supported) return OverclockState.NotSupported;
        if (!driverInstalled) return OverclockState.DriverMissing;
        if (measuredHz is null || measuredHz < 400) return OverclockState.NotActive;
        return measuredHz >= 800 ? OverclockState.Active : OverclockState.Partial;
    }

    public static string StateText(OverclockState state, double? measuredHz) => state switch
    {
        OverclockState.NotSupported => "Só o DualSense e o DualShock 4 no cabo USB.",
        OverclockState.DriverMissing => "Driver ainda não instalado.",
        OverclockState.NotActive => measuredHz is { } hz ? $"Medido: {hz:0} Hz. Escolha 1000 na janela do driver e meça de novo." : "Meça para conferir a taxa atual.",
        OverclockState.Partial => $"Medido: {measuredHz:0} Hz. Confira se escolheu 1000 e se o controle está direto numa porta traseira.",
        _ => $"Ativo: {measuredHz:0} Hz (um comando a cada {1000 / measuredHz!.Value:0.0} ms).",
    };

    /// <summary>Avisos antes de ativar: o que verificar e o que pode dar problema.</summary>
    public static IReadOnlyList<(bool Ok, string Text)> Checks(bool driverInstalled, bool memoryIntegrityOn, bool usbConnected)
    {
        return new List<(bool, string)>
        {
            (usbConnected, usbConnected ? "Controle ligado no cabo USB." : "Ligue o controle no cabo USB (no Bluetooth a taxa não muda)."),
            (true, "Driver assinado pela Microsoft: o Secure Boot continua ligado, sem modo de teste."),
            (true, memoryIntegrityOn ? "Integridade de memória ligada: use a versão sem patch, que funciona com ela." : "Integridade de memória desligada."),
            (driverInstalled, driverInstalled ? "Driver hidusbf instalado." : "Driver ainda não instalado: o programa baixa do repositório oficial do autor."),
            (false, "Anti-cheat: é um driver de filtro de terceiros. Depois de ativar, abra seus jogos uma vez para conferir."),
        };
    }
}

/// <summary>Leituras do Windows para o Controle competitivo e o Overclock.</summary>
[SupportedOSPlatform("windows")]
public static class PadSystem
{
    /// <summary>O serviço do hidusbf existe (o driver foi instalado).</summary>
    public static bool OverclockDriverInstalled()
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = root.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\hidusbf", false);
        return key is not null;
    }

    public static bool MemoryIntegrityOn()
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = root.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", false);
        return key?.GetValue("Enabled") is int n && n == 1;
    }

    /// <summary>Fones e caixas de som Bluetooth ligados agora (um aparelho conta uma vez, mesmo com microfone).</summary>
    public static int BluetoothAudioDevices()
    {
        try
        {
            var enumeratorKey = new NAudio.CoreAudioApi.PropertyKey(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 24);
            var interfaceName = new NAudio.CoreAudioApi.PropertyKey(new Guid("026e516e-b814-414b-83cd-856d6fef4822"), 2);
            using var devices = new NAudio.CoreAudioApi.MMDeviceEnumerator();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in devices.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.All, NAudio.CoreAudioApi.DeviceState.Active))
            {
                using (d)
                {
                    var props = d.Properties;
                    if (!props.Contains(enumeratorKey) || props[enumeratorKey].Value is not string enumerator) continue;
                    if (!enumerator.StartsWith("BTH", StringComparison.OrdinalIgnoreCase)) continue;
                    names.Add(props.Contains(interfaceName) && props[interfaceName].Value is string n ? n : d.FriendlyName);
                }
            }
            return names.Count;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException) { return 0; }
    }

    public static bool WifiOn24GHz()
    {
        var wifi = MBExtreme.Core.Network.WifiService.Read();
        return wifi is { Connected: true } && (wifi.Band.Contains("2,4") || wifi.Band.Contains("2.4") || wifi.Channel is > 0 and <= 14);
    }

    /// <summary>Instâncias dos adaptadores Bluetooth internos (na placa-mãe ou na placa Wi-Fi) quando também há um adaptador USB ligado.</summary>
    public static IReadOnlyList<string> InternalRadiosWhenDongle(IReadOnlyList<UsbPortInfo> ports)
    {
        var bt = ports.Where(p => p.Use == UsbUse.BluetoothAdapter && p.InstanceId is not null).ToList();
        if (!bt.Any(p => p.Panel != UsbPanel.Internal)) return Array.Empty<string>();
        return bt.Where(p => p.Panel == UsbPanel.Internal).Select(p => p.InstanceId!).ToList();
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern int CM_Locate_DevNodeW(out int devInst, string id, int flags);
    [DllImport("cfgmgr32.dll")] private static extern int CM_Get_DevNode_Status(out int status, out int problem, int devInst, int flags);

    /// <summary>true = desativado no Gerenciador de Dispositivos; null = não encontrado.</summary>
    public static bool? IsDisabled(string instanceId)
    {
        try
        {
            if (CM_Locate_DevNodeW(out int inst, instanceId, 0) != 0) return null;
            if (CM_Get_DevNode_Status(out _, out int problem, inst, 0) != 0) return null;
            return problem == 22; // CM_PROB_DISABLED
        }
        catch (DllNotFoundException) { return null; }
    }

    public static void SetEnabled(string instanceId, bool enabled)
    {
        var result = Shell.Run("pnputil.exe", enabled ? "/enable-device" : "/disable-device", instanceId);
        if (result.ExitCode != 0) throw new InvalidOperationException((result.Output + " " + result.Error).Trim());
    }
}

/// <summary>Desativa aparelhos (ex.: o Bluetooth interno) e guarda quais foram, para religar no Desfazer.</summary>
[SupportedOSPlatform("windows")]
public sealed class DisableDevicesTweak : Tweak
{
    private readonly Func<IReadOnlyList<string>> _targets;

    public DisableDevicesTweak(TweakStore store, string id, TweakCategory category, TweakRisk risk, string title, string description, Func<IReadOnlyList<string>> targets)
        : base(store, id, category, risk, title, description) => _targets = targets;

    private string Prefix => Id + "|dev|";

    private IReadOnlyList<string> Saved() => Store.KeysWithPrefix(Prefix).Select(k => k[Prefix.Length..]).ToList();

    private IReadOnlyList<string> SafeTargets()
    {
        try { return _targets(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or DllNotFoundException or EntryPointNotFoundException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    public override bool? IsApplied()
    {
        var saved = Saved();
        if (saved.Count > 0) return saved.All(s => PadSystem.IsDisabled(s) != false);
        return SafeTargets().Count == 0 ? null : false;
    }

    public override void Apply()
    {
        var targets = SafeTargets();
        if (targets.Count == 0) throw new InvalidOperationException("Não há Bluetooth interno para desligar (é preciso ter também um adaptador Bluetooth USB ligado).");
        foreach (string id in targets)
        {
            PadSystem.SetEnabled(id, false);
            Store.Set(Prefix + id, "1");
        }
    }

    public override void Revert()
    {
        foreach (string id in Saved())
        {
            try { PadSystem.SetEnabled(id, true); }
            catch (InvalidOperationException) { /* aparelho removido: nada a religar */ }
            Store.Remove(Prefix + id);
        }
    }
}
