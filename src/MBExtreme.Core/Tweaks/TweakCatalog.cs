using System.Runtime.Versioning;
using Microsoft.Win32;

namespace MBExtreme.Core.Tweaks;

/// <summary>
/// Catálogo de ajustes. Cada item é reversível. Ajustes de risco alto nunca são "recomendados".
/// Valores baseados em técnicas conhecidas de otimização do Windows (veja docs/CATALOGO-AJUSTES.md).
/// </summary>
[SupportedOSPlatform("windows")]
public static class TweakCatalog
{
    private const RegistryHive Hklm = RegistryHive.LocalMachine;
    private const RegistryHive Hkcu = RegistryHive.CurrentUser;
    private const RegistryValueKind Str = RegistryValueKind.String;

    public static IReadOnlyList<Tweak> Create(TweakStore store)
    {
        const string systemProfile = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
        const string gamesTask = systemProfile + @"\Tasks\Games";
        const string contentDelivery = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
        const string explorerAdvanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        const string explorer = @"Software\Microsoft\Windows\CurrentVersion\Explorer";

        return new Tweak[]
        {
            // ---------------------------------------------------------------- Desempenho
            new RegistryTweak(store, "perf-priority-separation", TweakCategory.Performance, TweakRisk.Low,
                "Prioridade de CPU para o programa em primeiro plano",
                "Ajusta o agendador do Windows para dar mais tempo de CPU ao programa que você está usando (o jogo).",
                new[] { new RegistryValueSpec(Hklm, @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 38) })
            {
                Effect = "Mais resposta no programa em primeiro plano; programas de fundo recebem menos CPU.",
                Recommended = true,
            },

            new PowerPlanTweak(store, "perf-power-ultimate", TweakCategory.Performance, TweakRisk.Low,
                "Plano de energia: Desempenho Máximo",
                "Cria e ativa o plano de energia de desempenho máximo do Windows, sem economia agressiva de energia.")
            {
                Effect = "Menos engasgos e clocks mais estáveis.",
                Warning = "Mais consumo e calor. Em notebooks reduz a bateria.",
                Recommended = true,
            },

            new RegistryTweak(store, "perf-background-apps", TweakCategory.Performance, TweakRisk.Low,
                "Impedir aplicativos da Microsoft Store em segundo plano",
                "Desliga a permissão para os aplicativos da Loja rodarem em segundo plano.",
                new[] { new RegistryValueSpec(Hkcu, @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1) })
            {
                Effect = "Libera um pouco de CPU e RAM.",
                Recommended = true,
            },

            new ServiceTweak(store, "perf-svc-diagtrack", TweakCategory.Performance, TweakRisk.Low,
                "Telemetria do Windows sob demanda",
                "Muda o serviço de experiências conectadas e telemetria (DiagTrack) de automático para manual.",
                new[] { "DiagTrack" }, 3)
            {
                Effect = "Menos atividade em segundo plano e menos envio de dados.",
                Recommended = true,
            },

            new ServiceTweak(store, "perf-svc-maps", TweakCategory.Performance, TweakRisk.Low,
                "Serviço de mapas sob demanda",
                "Muda o serviço de mapas baixados (MapsBroker) para manual.",
                new[] { "MapsBroker" }, 3)
            {
                Effect = "Um serviço a menos iniciando com o Windows.",
                Recommended = true,
            },

            new ServiceTweak(store, "perf-svc-sysmain", TweakCategory.Performance, TweakRisk.Medium,
                "Desativar o SysMain (Superfetch)",
                "Para e desativa o serviço que pré-carrega programas na RAM.",
                new[] { "SysMain" }, 4)
            {
                Effect = "Libera RAM e reduz leitura de disco em segundo plano.",
                Warning = "Só vale a pena em SSD. Em HD, o início dos programas tende a ficar mais lento.",
            },

            new ServiceTweak(store, "perf-svc-search", TweakCategory.Performance, TweakRisk.Medium,
                "Desativar a indexação de arquivos (Windows Search)",
                "Para e desativa o serviço que indexa arquivos para a busca do Windows.",
                new[] { "WSearch" }, 4)
            {
                Effect = "Menos uso de disco e CPU em segundo plano.",
                Warning = "A busca do Windows e do Outlook fica mais lenta.",
            },

            new ServiceTweak(store, "perf-svc-spooler", TweakCategory.Performance, TweakRisk.Medium,
                "Desativar o spooler de impressão",
                "Para e desativa o serviço de impressão.",
                new[] { "Spooler" }, 4)
            {
                Effect = "Libera um pouco de RAM e reduz a superfície de ataque.",
                Warning = "Nenhuma impressora (inclusive PDF) funciona até reativar.",
            },

            // ---------------------------------------------------------------- Jogos e GPU
            new RegistryTweak(store, "game-dvr", TweakCategory.Gaming, TweakRisk.Low,
                "Desativar a gravação em segundo plano (Game DVR)",
                "Desliga a captura contínua do Xbox Game Bar, que grava os últimos minutos do jogo.",
                new[]
                {
                    new RegistryValueSpec(Hkcu, @"System\GameConfigStore", "GameDVR_Enabled", 0),
                    new RegistryValueSpec(Hkcu, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0),
                })
            {
                Effect = "Recupera alguns FPS e reduz uso de GPU e disco.",
                Warning = "Você perde o \"gravar o que aconteceu\" do Game Bar.",
                Recommended = true,
            },

            new RegistryTweak(store, "game-mmcss", TweakCategory.Gaming, TweakRisk.Low,
                "Prioridade de CPU e GPU para jogos (agendador multimídia)",
                "Define o perfil \"Games\" do agendador multimídia com prioridade alta de CPU, GPU e disco.",
                new[]
                {
                    new RegistryValueSpec(Hklm, gamesTask, "Affinity", 0),
                    new RegistryValueSpec(Hklm, gamesTask, "Background Only", "False", Str),
                    new RegistryValueSpec(Hklm, gamesTask, "Clock Rate", 0x2710),
                    new RegistryValueSpec(Hklm, gamesTask, "Scheduling Category", "High", Str),
                    new RegistryValueSpec(Hklm, gamesTask, "SFIO Priority", "High", Str),
                    new RegistryValueSpec(Hklm, gamesTask, "GPU Priority", 8),
                    new RegistryValueSpec(Hklm, gamesTask, "Priority", 6),
                })
            {
                Effect = "O jogo recebe prioridade sobre os programas de fundo.",
                Recommended = true,
            },

            new RegistryTweak(store, "game-hags-off", TweakCategory.Gaming, TweakRisk.Medium,
                "Desativar o agendamento de GPU acelerado por hardware (HAGS)",
                "A CPU volta a gerenciar a fila de trabalho da placa de vídeo. Alguns jogos e drivers têm menos travadas assim.",
                new[] { new RegistryValueSpec(Hklm, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 1) })
            {
                Effect = "Pode reduzir travadas em combinações específicas de jogo e driver.",
                Warning = "Desativa o Frame Generation do DLSS 3/4 e pode aumentar a latência. Teste antes e depois.",
                RequiresRestart = true,
            },

            new RegistryTweak(store, "game-driver-updates", TweakCategory.Gaming, TweakRisk.Medium,
                "Impedir o Windows Update de trocar drivers",
                "Exclui drivers das atualizações de qualidade do Windows Update.",
                new[] { new RegistryValueSpec(Hklm, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate", 1) })
            {
                Effect = "Evita que um driver de vídeo problemático seja instalado sem você pedir.",
                Warning = "Drivers não serão atualizados automaticamente. Atualize pelo fabricante ou pela aba Drivers.",
            },

            // ---------------------------------------------------------------- Rede
            new RegistryTweak(store, "net-throttling", TweakCategory.Network, TweakRisk.Low,
                "Remover o limite de pacotes do agendador multimídia",
                "Desliga o índice de estrangulamento de rede do agendador multimídia.",
                new[] { new RegistryValueSpec(Hklm, systemProfile, "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF)) })
            {
                Effect = "Remove um limite artificial de pacotes por intervalo. O ganho varia entre máquinas.",
                Recommended = true,
            },

            new RegistryTweak(store, "net-qos-limit", TweakCategory.Network, TweakRisk.Low,
                "Remover o limite de banda reservada do QoS",
                "Define o limite de tráfego \"não melhor esforço\" do QoS como zero.",
                new[] { new RegistryValueSpec(Hklm, @"SOFTWARE\Policies\Microsoft\Windows\Psched", "NonBestEffortLimit", 0) })
            {
                Effect = "Libera para os aplicativos a banda que o QoS poderia reservar.",
                Recommended = true,
            },

            new RegistryTweak(store, "net-ipv6-off", TweakCategory.Network, TweakRisk.Medium,
                "Desativar o IPv6",
                "Desliga todos os componentes do IPv6.",
                new[] { new RegistryValueSpec(Hklm, @"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters", "DisabledComponents", 255) })
            {
                Effect = "Pode resolver atrasos de conexão em redes com IPv6 mal configurado.",
                Warning = "Alguns serviços e provedores dependem do IPv6. Reative se algo parar de funcionar.",
                RequiresRestart = true,
            },

            // ---------------------------------------------------------------- Privacidade
            new RegistryTweak(store, "priv-ads", TweakCategory.Privacy, TweakRisk.Low,
                "Desativar anúncios e sugestões do Windows",
                "Desliga sugestões no Iniciar, dicas e conteúdo promovido.",
                new[]
                {
                    new RegistryValueSpec(Hkcu, contentDelivery, "SystemPaneSuggestionsEnabled", 0),
                    new RegistryValueSpec(Hkcu, contentDelivery, "SoftLandingEnabled", 0),
                    new RegistryValueSpec(Hkcu, contentDelivery, "SubscribedContent-338393Enabled", 0),
                    new RegistryValueSpec(Hkcu, contentDelivery, "SubscribedContent-353694Enabled", 0),
                    new RegistryValueSpec(Hkcu, contentDelivery, "SubscribedContent-353696Enabled", 0),
                    new RegistryValueSpec(Hkcu, contentDelivery, "SubscribedContent-338389Enabled", 0),
                    new RegistryValueSpec(Hkcu, contentDelivery, "SubscribedContent-310093Enabled", 0),
                    new RegistryValueSpec(Hkcu, contentDelivery, "SubscribedContent-353698Enabled", 0),
                })
            {
                Effect = "Menos propaganda e ruído no Windows.",
                Recommended = true,
            },

            new RegistryTweak(store, "priv-recent", TweakCategory.Privacy, TweakRisk.Low,
                "Não registrar programas e documentos recentes",
                "Desliga o histórico de programas e documentos usados no Iniciar.",
                new[]
                {
                    new RegistryValueSpec(Hkcu, explorerAdvanced, "Start_TrackProgs", 0),
                    new RegistryValueSpec(Hkcu, explorerAdvanced, "Start_TrackDocs", 0),
                    new RegistryValueSpec(Hkcu, explorer, "ShowRecent", 0),
                    new RegistryValueSpec(Hkcu, explorer, "ShowRecommended", 0),
                })
            {
                Effect = "Mais privacidade; o Iniciar não mostra itens recentes.",
            },

            new RegistryTweak(store, "priv-copilot", TweakCategory.Privacy, TweakRisk.Low,
                "Desativar o Copilot",
                "Desliga o Copilot por política e esconde o botão da barra de tarefas.",
                new[]
                {
                    new RegistryValueSpec(Hklm, @"SOFTWARE\Policies\Microsoft\Windows\Windows Copilot", "TurnOffWindowsCopilot", 1),
                    new RegistryValueSpec(Hkcu, explorerAdvanced, "ShowCopilotButton", 0),
                })
            {
                Effect = "Menos processos e menos envio de dados.",
            },

            new RegistryTweak(store, "priv-cortana", TweakCategory.Privacy, TweakRisk.Low,
                "Desativar a Cortana",
                "Desliga a Cortana por política.",
                new[] { new RegistryValueSpec(Hklm, @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana", 0) })
            {
                Effect = "Menos atividade em segundo plano.",
            },

            new RegistryTweak(store, "priv-smartscreen", TweakCategory.Privacy, TweakRisk.High,
                "Desativar o SmartScreen do Explorador de Arquivos",
                "Desliga a verificação de reputação que avisa ao abrir programas baixados.",
                new[] { new RegistryValueSpec(Hklm, explorer, "SmartScreenEnabled", "Off", Str) })
            {
                Effect = "Menos avisos ao abrir programas baixados.",
                Warning = "É uma proteção contra programas maliciosos. Só desative se você sabe a origem de tudo que abre. Não afeta o Defender.",
            },

            // ---------------------------------------------------------------- Periféricos
            new RegistryTweak(store, "periph-mouse-accel", TweakCategory.Peripherals, TweakRisk.Low,
                "Desligar a aceleração do mouse",
                "Desmarca \"Aprimorar precisão do ponteiro\": o cursor anda sempre a mesma distância para o mesmo movimento.",
                new[]
                {
                    new RegistryValueSpec(Hkcu, @"Control Panel\Mouse", "MouseSpeed", "0", Str),
                    new RegistryValueSpec(Hkcu, @"Control Panel\Mouse", "MouseThreshold1", "0", Str),
                    new RegistryValueSpec(Hkcu, @"Control Panel\Mouse", "MouseThreshold2", "0", Str),
                })
            {
                Effect = "Mira consistente em jogos de tiro. Não muda FPS.",
                Recommended = true,
                RequiresRestart = true,
            },

            new RegistryTweak(store, "periph-sticky-keys", TweakCategory.Peripherals, TweakRisk.Low,
                "Desativar Teclas de Aderência, Filtro e Alternância",
                "Desliga os atalhos de acessibilidade que abrem janelas ao apertar Shift várias vezes.",
                new[]
                {
                    new RegistryValueSpec(Hkcu, @"Control Panel\Accessibility\StickyKeys", "Flags", "506", Str),
                    new RegistryValueSpec(Hkcu, @"Control Panel\Accessibility\Keyboard Response", "Flags", "122", Str),
                    new RegistryValueSpec(Hkcu, @"Control Panel\Accessibility\ToggleKeys", "Flags", "58", Str),
                })
            {
                Effect = "Evita que o jogo perca o foco ao apertar Shift ou Ctrl rapidamente.",
                Warning = "Se você usa esses recursos de acessibilidade, não desative.",
                Recommended = true,
            },

            // ---------------------------------------------------------------- Inicialização e boot
            new RegistryTweak(store, "boot-fast-startup-off", TweakCategory.Boot, TweakRisk.Low,
                "Desativar a inicialização rápida",
                "Faz o \"Desligar\" realmente desligar, em vez de hibernar parte do sistema.",
                new[] { new RegistryValueSpec(Hklm, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 0) })
            {
                Effect = "Resolve PC que não desliga direito e drivers que não reiniciam; o boot pode levar alguns segundos a mais.",
                RequiresRestart = true,
            },
        };
    }
}
