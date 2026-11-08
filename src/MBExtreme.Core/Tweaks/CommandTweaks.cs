using System.Runtime.Versioning;
using Microsoft.Win32;
using MBExtreme.Core.Windows;

namespace MBExtreme.Core.Tweaks;

/// <summary>Ajustes feitos por comandos do Windows. A leitura de estado independe do idioma do sistema.</summary>
[SupportedOSPlatform("windows")]
public static class CommandTweaks
{
    private static string? PowerShell(string script)
    {
        var (code, output, _) = Shell.Run("powershell.exe", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script);
        return code == 0 ? output.Trim() : null;
    }

    private static string? Bcd(string name)
    {
        var (code, output, _) = Shell.Run("bcdedit.exe", "/enum", "{current}");
        return code == 0 ? CommandParsers.BcdValue(output, name) ?? "-" : null;
    }

    private static void BcdWrite(string name, string value)
    {
        if (name == "/timeout")
        {
            var (c, _, e) = Shell.Run("bcdedit.exe", "/timeout", value == "-" ? "30" : value);
            if (c != 0) throw new InvalidOperationException($"bcdedit /timeout: {e}".Trim());
            return;
        }
        var (code, _, error) = value == "-"
            ? Shell.Run("bcdedit.exe", "/deletevalue", name)
            : Shell.Run("bcdedit.exe", "/set", name, value);
        // apagar um valor que não existe não é erro para nós
        if (code != 0 && value != "-") throw new InvalidOperationException($"bcdedit {name}: {error}".Trim());
    }

    /// <summary>Cópia de segurança do BCD antes de mexer. Restaurar: bcdedit /import "arquivo".</summary>
    private static void BcdBackup()
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "bcd");
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, $"bcd-{DateTime.Now:yyyyMMdd-HHmmss}.bak");
        var (code, _, error) = Shell.Run("bcdedit.exe", "/export", file);
        if (code != 0) throw new InvalidOperationException("Não foi possível guardar o backup do BCD: " + error);
    }

    public static IEnumerable<Tweak> Create(TweakStore store)
    {
        yield return new CommandTweak(store, "boot-menu-timeout", TweakCategory.Boot, TweakRisk.Low,
            "Menu de inicialização em 3 segundos",
            "Quando há mais de um sistema (ou opções de recuperação), o menu de escolha espera 3 segundos em vez de 30.",
            () =>
            {
                var (code, output, _) = Shell.Run("bcdedit.exe", "/enum", "{bootmgr}");
                return code == 0 ? CommandParsers.BcdValue(output, "timeout") : null;
            },
            "3", v => BcdWrite("/timeout", v), "30", BcdBackup)
        {
            Effect = "O PC liga mais rápido quando o menu de inicialização aparece. Sem o menu, nada muda.",
            Details = "Roda bcdedit /timeout 3. O valor original fica guardado para desfazer e o app exporta um backup do BCD antes.",
            Badge = TweakCatalogExtra.New,
        };

        yield return new CommandTweak(store, "storage-trim", TweakCategory.Storage, TweakRisk.Low,
            "Garantir o TRIM do SSD",
            "Liga o envio do comando TRIM ao SSD, que mantém a escrita rápida ao longo do tempo.",
            () => CommandParsers.FsutilValue(Shell.Run("fsutil.exe", "behavior", "query", "DisableDeleteNotify").Output, "DisableDeleteNotify"),
            "0", v => Shell.Run("fsutil.exe", "behavior", "set", "DisableDeleteNotify", v), "0")
        {
            Effect = "Mantém a velocidade de escrita do SSD; em HD não faz diferença.",
            Recommended = true,
        };

        yield return new CommandTweak(store, "storage-hibernate-off", TweakCategory.Storage, TweakRisk.Medium,
            "Desativar a hibernação",
            "Desliga a hibernação e apaga o arquivo hiberfil.sys, que ocupa vários GB do disco.",
            () => RegistryAccess.Read(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled")?.ToString(),
            "0", v => Shell.Run("powercfg.exe", "/hibernate", v == "0" ? "off" : "on"), "1")
        {
            Effect = "Libera espaço (geralmente 40% a 75% da RAM) e remove a inicialização rápida.",
            Warning = "Em notebook você perde hibernar e retomar de onde parou com a bateria fraca. O Windows deixa de oferecer a inicialização rápida.",
            RequiresRestart = true,
        };

        yield return new CommandTweak(store, "net-rss-on", TweakCategory.Network, TweakRisk.Low,
            "Receive Side Scaling (RSS) ligado",
            "Distribui o processamento de pacotes de rede entre os núcleos da CPU.",
            () => PowerShell("(Get-NetOffloadGlobalSetting).ReceiveSideScaling"),
            "Enabled", v => PowerShell($"Set-NetOffloadGlobalSetting -ReceiveSideScaling {v}"), "Enabled")
        {
            Effect = "Evita que um único núcleo seja o gargalo em conexões rápidas. É o padrão na maioria dos PCs.",
        };

        yield return new CommandTweak(store, "net-rsc-off", TweakCategory.Network, TweakRisk.Low,
            "Desligar a junção de segmentos recebidos (RSC)",
            "Entrega os pacotes recebidos na hora em vez de juntá-los antes, o que pode reduzir um pouco a latência em jogos online.",
            () => PowerShell("(Get-NetOffloadGlobalSetting).ReceiveSegmentCoalescing"),
            "Disabled", v => PowerShell($"Set-NetOffloadGlobalSetting -ReceiveSegmentCoalescing {v}"), "Enabled")
        {
            Effect = "Latência de recepção um pouco menor, com mais trabalho para a CPU em downloads muito rápidos.",
            Warning = "Meça o ping e a velocidade antes e depois; em alguns PCs o download rápido usa mais CPU.",
        };

        yield return new CommandTweak(store, "boot-timer-bcd", TweakCategory.Boot, TweakRisk.High,
            "Temporizador do sistema (bcdedit)",
            "Define useplatformtick, disabledynamictick e tscsyncpolicy no BCD. Pode melhorar a consistência de tempo de quadro em algumas CPUs e piorar em outras.",
            () =>
            {
                string? a = Bcd("useplatformtick"), b = Bcd("disabledynamictick"), c = Bcd("tscsyncpolicy");
                return a is null || b is null || c is null ? null : $"{a}|{b}|{c}".ToLowerInvariant();
            },
            "yes|yes|enhanced",
            v =>
            {
                var parts = v.Split('|');
                string[] names = { "useplatformtick", "disabledynamictick", "tscsyncpolicy" };
                for (int i = 0; i < names.Length; i++) BcdWrite(names[i], parts[i]);
            },
            "-|-|-", BcdBackup)
        {
            Effect = "Resultado depende da CPU e do jogo. Teste com o Diagnóstico de latência antes e depois.",
            Warning = "Mexe na configuração de inicialização do Windows. O app guarda um backup do BCD em %AppData%\\MBExtreme\\bcd. Sem ganho comprovado em geral; use só se medir melhora.",
            RequiresRestart = true,
        };

        yield return new CommandTweak(store, "boot-hypervisor-off", TweakCategory.Boot, TweakRisk.High,
            "Desligar o hipervisor na inicialização",
            "Define hypervisorlaunchtype como off, para o Windows não iniciar o Hyper-V junto.",
            () => Bcd("hypervisorlaunchtype")?.ToLowerInvariant(),
            "off", v => BcdWrite("hypervisorlaunchtype", v == "-" ? "auto" : v), "auto", BcdBackup)
        {
            Effect = "Remove a camada de virtualização do Windows, que pode custar FPS em alguns PCs.",
            Warning = "QUEBRA o WSL2, Docker, Hyper-V, Sandbox e a Integridade da Memória. Use só em PC dedicado a jogos. O app guarda um backup do BCD.",
            RequiresRestart = true,
        };
    }
}
