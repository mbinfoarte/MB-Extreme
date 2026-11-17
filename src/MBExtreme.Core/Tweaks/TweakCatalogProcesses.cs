using System.Runtime.Versioning;
using Microsoft.Win32;

namespace MBExtreme.Core.Tweaks;

/// <summary>
/// Ajustes da tela Processos e inicialização: serviços que podem ficar em Manual, telemetria, atualizadores escondidos e
/// processos do Windows. Ideias levantadas a partir do WinUtil de Chris Titus (licença MIT), refeitas aqui com backup e Desfazer.
/// </summary>
[SupportedOSPlatform("windows")]
public static class TweakCatalogProcesses
{
    private const RegistryHive Hklm = RegistryHive.LocalMachine;
    private const RegistryHive Hkcu = RegistryHive.CurrentUser;

    /// <summary>Atualizadores de navegador/Adobe que rodam como tarefa agendada (o próprio programa continua se atualizando quando é aberto).</summary>
    public const string UpdaterTasks = "'GoogleUpdateTask*','MicrosoftEdgeUpdateTask*','Adobe Acrobat Update Task','AdobeGCInvoker*','Opera*scheduled*','BraveSoftwareUpdateTask*'";

    public static IEnumerable<Tweak> Create(TweakStore store)
    {
        yield return new ServiceTweak(store, "proc-svc-wersvc", TweakCategory.Performance, TweakRisk.Low,
            "Relatório de erros do Windows em Manual", "O serviço WerSvc só roda quando algum programa trava, em vez de ficar sempre pronto.",
            new[] { "WerSvc" }, 3) { Effect = "Um serviço a menos e menos envio de relatórios.", Recommended = true };
        yield return new ServiceTweak(store, "proc-svc-csc", TweakCategory.Performance, TweakRisk.Low,
            "Desligar Arquivos offline", "O serviço CscService guarda cópias de pastas de rede de empresa. Em casa quase ninguém usa.",
            new[] { "CscService" }, 4) { Effect = "Um serviço a menos.", Warning = "Pastas de rede marcadas como \"sempre disponível offline\" deixam de sincronizar." };
        yield return new ServiceTweak(store, "proc-svc-sharedaccess", TweakCategory.Performance, TweakRisk.Low,
            "Compartilhar internet em Manual", "O serviço SharedAccess (compartilhar a conexão por cabo) só liga quando for usado.",
            new[] { "SharedAccess" }, 3) { Effect = "Um serviço a menos ao ligar o PC." };
        yield return new ServiceTweak(store, "proc-svc-location", TweakCategory.Performance, TweakRisk.Low,
            "Localização em Manual", "O serviço de localização (lfsvc) só roda quando um app pedir.",
            new[] { "lfsvc" }, 3) { Effect = "Menos rastreio e um serviço a menos.", Recommended = true };
        yield return new ServiceTweak(store, "proc-svc-storsvc", TweakCategory.Performance, TweakRisk.Low,
            "Serviço de armazenamento em Manual", "O StorSvc (configurações de armazenamento) liga só quando precisar.",
            new[] { "StorSvc" }, 3) { Effect = "Um serviço a menos ao ligar o PC." };

        yield return new RegistryTweak(store, "proc-telemetry", TweakCategory.Privacy, TweakRisk.Low,
            "Telemetria e coleta de digitação no mínimo",
            "Diagnóstico no mínimo permitido, sem propaganda personalizada, sem coletar o que você digita ou escreve à mão e sem pedir avaliações.",
            new[]
            {
                new RegistryValueSpec(Hklm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection", "AllowTelemetry", 0),
                new RegistryValueSpec(Hkcu, @"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0),
                new RegistryValueSpec(Hkcu, @"Software\Microsoft\InputPersonalization", "RestrictImplicitInkCollection", 1),
                new RegistryValueSpec(Hkcu, @"Software\Microsoft\InputPersonalization", "RestrictImplicitTextCollection", 1),
                new RegistryValueSpec(Hkcu, @"Software\Microsoft\Input\TIPC", "Enabled", 0),
                new RegistryValueSpec(Hkcu, @"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0),
            }) { Effect = "Menos dados enviados e menos tarefas de coleta rodando.", Recommended = true };
        yield return new RegistryTweak(store, "proc-activity", TweakCategory.Privacy, TweakRisk.Low,
            "Não enviar o histórico de atividades", "O Windows para de publicar e enviar o que você abriu (o histórico da área de transferência continua).",
            new[]
            {
                new RegistryValueSpec(Hklm, @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0),
                new RegistryValueSpec(Hklm, @"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0),
            }) { Effect = "Menos atividade em segundo plano.", Recommended = true };
        yield return new RegistryTweak(store, "proc-consumer-features", TweakCategory.Privacy, TweakRisk.Low,
            "Não instalar apps sugeridos sozinho", "Impede o Windows de instalar jogos e apps patrocinados (Candy Crush, TikTok...) sem pedir.",
            new[] { new RegistryValueSpec(Hklm, @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", 1) })
            { Effect = "Nada de app aparecendo do nada no menu Iniciar.", Recommended = true };
        yield return new RegistryTweak(store, "proc-delivery-optimization", TweakCategory.Network, TweakRisk.Low,
            "Não usar a sua internet para enviar atualizações a outros PCs",
            "O Windows Update baixa só para você, sem repartir pedaços com computadores da internet ou da rede.",
            new[] { new RegistryValueSpec(Hklm, @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0) })
            { Effect = "Menos upload em segundo plano (bom para o ping).", Recommended = true };
        yield return new RegistryTweak(store, "proc-wpbt", TweakCategory.Privacy, TweakRisk.Low,
            "Bloquear programas do fabricante gravados na placa-mãe (WPBT)",
            "Algumas placas instalam programas do fabricante sozinhas a cada inicialização. Isto impede.",
            new[] { new RegistryValueSpec(Hklm, @"SYSTEM\CurrentControlSet\Control\Session Manager", "DisableWpbtExecution", 1) })
            { Effect = "Nenhum programa instalado pela BIOS sem você pedir.", Recommended = true, RequiresRestart = true };
        yield return new RegistryTweak(store, "proc-device-companion", TweakCategory.Privacy, TweakRisk.Low,
            "Não baixar apps de fabricante ao ligar um aparelho",
            "Ao conectar mouse, teclado ou fone, o Windows deixa de baixar sozinho o app do fabricante (Razer, Logitech...).",
            new[] { new RegistryValueSpec(Hklm, @"SOFTWARE\Policies\Microsoft\Windows\Device Metadata", "PreventDeviceMetadataFromNetwork", 1) })
            { Effect = "Nada de instalador abrindo sozinho ao ligar um periférico.", Recommended = true };
        yield return new RegistryTweak(store, "proc-endtask-taskbar", TweakCategory.Visual, TweakRisk.Low,
            "\"Finalizar tarefa\" no botão direito da barra de tarefas", "Fecha um programa travado direto pela barra de tarefas (Windows 11).",
            new[] { new RegistryValueSpec(Hkcu, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings", "TaskbarEndTask", 1) })
            { Effect = "Fechar programa travado sem abrir o Gerenciador de Tarefas.", MinBuild = 22631, Recommended = true };

        // menos processos svchost: junta os serviços em poucos processos (só faz sentido com pouca memória)
        long ramKb = Math.Max(1, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024);
        yield return new CommandTweak(store, "proc-svchost-split", TweakCategory.Performance, TweakRisk.Medium,
            "Menos processos svchost.exe (PC com pouca memória)",
            "Faz o Windows juntar os serviços em menos processos. Recomendado só para PCs com até 12 GB de memória.",
            () => (RegistryAccess.Read(Hklm, @"SYSTEM\CurrentControlSet\Control", "SvcHostSplitThresholdInKB") ?? 3670016).ToString(),
            ramKb.ToString(System.Globalization.CultureInfo.InvariantCulture),
            v => Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control", "SvcHostSplitThresholdInKB", unchecked((int)Math.Min(int.MaxValue, long.Parse(v, System.Globalization.CultureInfo.InvariantCulture))), RegistryValueKind.DWord),
            "3670016")
        {
            Effect = "Dezenas de processos a menos na lista e um pouco menos de memória usada.",
            Warning = "Precisa reiniciar. Pode atrapalhar o app do Xbox; se um serviço travar, ele leva outros junto.",
            RequiresRestart = true,
        };

        // atualizadores escondidos: desliga as tarefas agendadas (os navegadores continuam se atualizando quando abertos)
        yield return new CommandTweak(store, "proc-updater-tasks", TweakCategory.Performance, TweakRisk.Low,
            "Atualizadores só quando o programa abre (Chrome, Edge, Adobe)",
            "Desliga as tarefas agendadas que rodam a cada hora para procurar atualização. Chrome, Edge e Acrobat se atualizam quando você os abre.",
            () =>
            {
                var (code, output, _) = Windows.Shell.Run("powershell.exe", "-NoProfile", "-NonInteractive", "-Command",
                    $"$t = Get-ScheduledTask -TaskName {UpdaterTasks} -ErrorAction SilentlyContinue; if (-not $t) {{ 'none' }} elseif ($t | Where-Object State -ne 'Disabled') {{ 'on' }} else {{ 'off' }}");
                string s = output.Trim();
                return code != 0 || s == "none" || s.Length == 0 ? null : s;
            },
            "off",
            v => Windows.Shell.Run("powershell.exe", "-NoProfile", "-NonInteractive", "-Command",
                $"Get-ScheduledTask -TaskName {UpdaterTasks} -ErrorAction SilentlyContinue | " + (v == "off" ? "Disable-ScheduledTask" : "Enable-ScheduledTask") + " | Out-Null"),
            "on")
        {
            Effect = "Some o processo de atualização que aparece toda hora e puxa CPU e internet.",
            Recommended = true,
        };
    }
}
