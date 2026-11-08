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

        var tweaks = new Tweak[]
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

            // Recall: política oficial "Desativar o salvamento de instantâneos" (Policy CSP WindowsAI/DisableAIDataAnalysis)
            new RegistryTweak(store, "priv-recall", TweakCategory.Privacy, TweakRisk.Low,
                "Desativar o Recall",
                "Impede o Windows de tirar e guardar capturas da tela para a busca do Recall (política oficial da Microsoft).",
                new[]
                {
                    new RegistryValueSpec(Hklm, @"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1),
                    new RegistryValueSpec(Hkcu, @"Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1),
                })
            {
                Effect = "Nenhuma captura da tela é guardada; mais privacidade e menos disco usado.",
                Recommended = true,
                MinBuild = 26100,
            },

            // Widgets do Windows 11: política oficial "Permitir widgets" (Policy CSP NewsAndInterests/AllowNewsAndInterests)
            new RegistryTweak(store, "priv-widgets", TweakCategory.Privacy, TweakRisk.Low,
                "Desativar os Widgets",
                "Desliga o painel de Widgets (notícias, clima e anúncios) da barra de tarefas pela política oficial.",
                new[] { new RegistryValueSpec(Hklm, @"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0) })
            {
                Effect = "Menos processos em segundo plano e nada de notícias na barra.",
                Recommended = true,
                MinBuild = 22000,
            },

            // Windows 10: "Notícias e interesses" (política oficial "Habilitar notícias e interesses na barra de tarefas")
            new RegistryTweak(store, "priv-news-interests", TweakCategory.Privacy, TweakRisk.Low,
                "Desativar Notícias e interesses",
                "Tira o painel de notícias e clima da barra de tarefas do Windows 10 pela política oficial.",
                new[] { new RegistryValueSpec(Hklm, @"SOFTWARE\Policies\Microsoft\Windows\Windows Feeds", "EnableFeeds", 0) })
            {
                Effect = "Menos processos em segundo plano e nada de notícias na barra.",
                Recommended = true,
                MaxBuild = 21999,
            },

            // as mesmas opções de Configurações > Personalização > Tela de bloqueio
            new RegistryTweak(store, "priv-lockscreen-tips", TweakCategory.Privacy, TweakRisk.Low,
                "Sem dicas e curiosidades na tela de bloqueio",
                "Desliga \"Obter fatos divertidos, dicas, truques e muito mais na tela de bloqueio\" (a mesma opção das Configurações).",
                new[]
                {
                    new RegistryValueSpec(Hkcu, contentDelivery, "RotatingLockScreenOverlayEnabled", 0),
                    new RegistryValueSpec(Hkcu, contentDelivery, "SubscribedContent-338387Enabled", 0),
                })
            {
                Effect = "Tela de bloqueio limpa, sem propaganda.",
                Recommended = true,
            },

            // Configurações > Personalização > Iniciar (Windows 11 22H2 em diante)
            new RegistryTweak(store, "priv-start-promos", TweakCategory.Privacy, TweakRisk.Low,
                "Sem recomendações e avisos de conta no Iniciar",
                "Desliga \"Mostrar recomendações de dicas, atalhos, novos aplicativos e muito mais\" e as notificações da conta Microsoft no Iniciar (as mesmas opções das Configurações).",
                new[]
                {
                    new RegistryValueSpec(Hkcu, explorerAdvanced, "Start_IrisRecommendations", 0),
                    new RegistryValueSpec(Hkcu, explorerAdvanced, "Start_AccountNotifications", 0),
                })
            {
                Effect = "Iniciar sem propaganda.",
                Recommended = true,
                MinBuild = 22621,
            },

            // Opções de Pasta > Modo de exibição > "Mostrar notificações do provedor de sincronização"
            new RegistryTweak(store, "priv-explorer-ads", TweakCategory.Privacy, TweakRisk.Low,
                "Sem propaganda no Explorador de Arquivos",
                "Desliga \"Mostrar notificações do provedor de sincronização\" (os avisos de OneDrive e Microsoft 365 no Explorador), a mesma opção das Opções de Pasta.",
                new[] { new RegistryValueSpec(Hkcu, explorerAdvanced, "ShowSyncProviderNotifications", 0) })
            {
                Effect = "Explorador sem banners de propaganda.",
                Recommended = true,
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

            // ---------------------------------------------------------------- Energia para jogos (na tomada)
            new PowerSettingTweak(store, "power-min-cpu", TweakCategory.Gaming, TweakRisk.Low,
                "Processador sempre em desempenho máximo (na tomada)",
                "Define o estado mínimo do processador em 100% quando o PC está ligado na tomada, evitando que o clock caia entre uma tarefa e outra.",
                "54533251-82be-4824-96c1-47b60b740d00", "893dee8e-2bef-41e0-89c6-b55d0929964c", 100)
            {
                Effect = "Resposta mais constante; evita a queda de clock em jogos leves.",
                Warning = "Gasta mais energia e esquenta mais em repouso. Só vale na tomada; na bateria nada muda.",
            },

            new PowerSettingTweak(store, "power-core-parking", TweakCategory.Gaming, TweakRisk.Low,
                "Não estacionar núcleos do processador (na tomada)",
                "Impede o Windows de \"adormecer\" núcleos quando a carga é baixa; todos ficam disponíveis na hora.",
                "54533251-82be-4824-96c1-47b60b740d00", "0cc5b647-c1df-4637-891a-dec35c318583", 100)
            {
                Effect = "Evita o atraso para acordar núcleos quando o jogo pede mais threads.",
                Warning = "Sem teste independente que comprove ganho em PCs atuais. Use a medição antes e depois.",
            },

            new PowerSettingTweak(store, "power-pcie-aspm", TweakCategory.Gaming, TweakRisk.Low,
                "PCI Express sem economia de energia (na tomada)",
                "Desliga a economia de energia do link PCI Express (ASPM), que pode atrasar a placa de vídeo e discos NVMe ao acordar.",
                "501a4d13-42af-4429-9fd1-a8218c268e20", "ee12f906-d277-404b-b6da-e5fa1a576df5", 0)
            {
                Effect = "Latência mais estável nos dispositivos PCI Express.",
                Warning = "Consome um pouco mais de energia. O efeito é pequeno; meça antes e depois.",
            },

            new PowerSettingTweak(store, "power-usb-suspend", TweakCategory.Gaming, TweakRisk.Low,
                "USB sem suspensão seletiva (na tomada)",
                "Impede o Windows de desligar portas USB ociosas, o que pode causar atraso no primeiro movimento de mouse e teclado.",
                "2a737441-1930-4402-8d77-b2bebba308a3", "48e6b7a6-50f5-4782-a5d4-53bb8f07e226", 0)
            {
                Effect = "Mouse, teclado e controles respondem sem o atraso de reativação.",
                Warning = "Consome um pouco mais de energia.",
                Recommended = true,
            },

            new RegistryTweak(store, "perf-power-throttling-off", TweakCategory.Performance, TweakRisk.Medium,
                "Desligar a limitação de energia por processo (EcoQoS)",
                "O Windows pode colocar programas em \"modo de eficiência\" (menos clock). Este ajuste desliga essa limitação no PC inteiro.",
                new[] { new RegistryValueSpec(Hklm, @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 1) })
            {
                Effect = "Evita que processos de jogo e de captura sejam desacelerados por engano.",
                Warning = "Experimental: sem teste independente de ganho. Em notebook aumenta consumo e calor. O Modo Jogo faz o mesmo só para o jogo aberto, sem mexer no PC todo.",
                RequiresRestart = true,
            },

            new RegistryTweak(store, "perf-vbs-off", TweakCategory.Performance, TweakRisk.High,
                "Desligar a Integridade da Memória e a VBS",
                "Desliga a proteção do kernel por virtualização (Integridade da Memória/HVCI). Testes publicados mostram perda de cerca de 2 a 10% de FPS com ela ligada, mais nos 1% pior.",
                new[]
                {
                    new RegistryValueSpec(Hklm, @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled", 0),
                    new RegistryValueSpec(Hklm, @"SYSTEM\CurrentControlSet\Control\DeviceGuard", "EnableVirtualizationBasedSecurity", 0),
                })
            {
                Effect = "Mais FPS (principalmente nos 1% pior) em CPUs onde a proteção pesa mais.",
                Warning = "REDUZ A SEGURANÇA: drivers maliciosos ficam mais fáceis de carregar no kernel. Algumas atualizações do Windows ligam de novo. Meça antes e depois e decida.",
                RequiresRestart = true,
            },

            // ---------------------------------------------------------------- Efeitos visuais
            // Windows 10: "Usar botões pequenos da barra de tarefas" (o Windows 11 não tem mais essa opção)
            new RegistryTweak(store, "visual-taskbar-small", TweakCategory.Visual, TweakRisk.Low,
                "Barra de tarefas menor",
                "Liga \"Usar botões pequenos da barra de tarefas\" (a mesma opção das Configurações do Windows 10).",
                new[] { new RegistryValueSpec(Hkcu, explorerAdvanced, "TaskbarSmallIcons", 1) })
            {
                Effect = "Mais espaço na tela.",
                MaxBuild = 21999,
            },

            new RegistryTweak(store, "visual-transparency", TweakCategory.Visual, TweakRisk.Low,
                "Desligar a transparência do Windows",
                "Remove o efeito de vidro na barra de tarefas, no Iniciar e nas janelas.",
                new[] { new RegistryValueSpec(Hkcu, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0) })
            {
                Effect = "Menos uso de GPU na área de trabalho; ajuda PCs com vídeo integrado.",
                Recommended = true,
            },

            new RegistryTweak(store, "visual-animations", TweakCategory.Visual, TweakRisk.Low,
                "Desligar animações de janelas e da barra de tarefas",
                "Remove as animações ao abrir, minimizar e trocar de janela.",
                new[]
                {
                    new RegistryValueSpec(Hkcu, @"Control Panel\Desktop\WindowMetrics", "MinAnimate", "0", Str),
                    new RegistryValueSpec(Hkcu, explorerAdvanced, "TaskbarAnimations", 0),
                    new RegistryValueSpec(Hkcu, explorerAdvanced, "ListviewAlphaSelect", 0),
                    new RegistryValueSpec(Hkcu, explorerAdvanced, "ListviewShadow", 0),
                })
            {
                Effect = "O Windows parece mais rápido, principalmente em PCs fracos.",
                RequiresRestart = true,
                Recommended = true,
            },

            new RegistryTweak(store, "visual-menu-delay", TweakCategory.Visual, TweakRisk.Low,
                "Menus abrem sem atraso",
                "Tira o pequeno atraso antes de os menus aparecerem e de arrastar janelas mostrando o conteúdo.",
                new[]
                {
                    new RegistryValueSpec(Hkcu, @"Control Panel\Desktop", "MenuShowDelay", "0", Str),
                    new RegistryValueSpec(Hkcu, @"Control Panel\Desktop", "DragFullWindows", "0", Str),
                })
            {
                Effect = "Menus mais ágeis.",
                Warning = "Arrastar janelas passa a mostrar só o contorno.",
                RequiresRestart = true,
            },

            new RegistryTweak(store, "visual-aero-peek", TweakCategory.Visual, TweakRisk.Low,
                "Desligar a visualização ao passar o mouse na barra de tarefas",
                "Desliga o Aero Peek (prévia da janela ao passar o mouse).",
                new[] { new RegistryValueSpec(Hkcu, @"Software\Microsoft\Windows\DWM", "EnableAeroPeek", 0) })
            {
                Effect = "Menos trabalho para a GPU e para o gerenciador de janelas.",
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

            // ---------------------------------------------------------------- Etapa 8: estudo do pacote Lobo Alpha (ver docs/ANALISE-LOBO-ALPHA.md)
            new RegistryTweak(store, "game-fse", TweakCategory.Gaming, TweakRisk.Low,
                "Tela cheia sem a camada do Game Bar",
                "Faz os jogos em tela cheia usarem o modo exclusivo, sem a camada de composição do Game DVR.",
                new[]
                {
                    new RegistryValueSpec(Hkcu, @"System\GameConfigStore", "GameDVR_FSEBehaviorMode", 2),
                    new RegistryValueSpec(Hkcu, @"System\GameConfigStore", "GameDVR_FSEBehavior", 2),
                    new RegistryValueSpec(Hkcu, @"System\GameConfigStore", "GameDVR_HonorUserFSEBehaviorMode", 1),
                    new RegistryValueSpec(Hkcu, @"System\GameConfigStore", "GameDVR_DXGIHonorFSEWindowsCompatible", 1),
                })
            {
                Effect = "Pode reduzir a latência de entrada em jogos DirectX em tela cheia. O efeito varia por jogo.",
                Warning = "Se um jogo piorar (alt+tab lento, tela preta), desfaça.",
            },

            new RegistryTweak(store, "game-sysresponsiveness", TweakCategory.Gaming, TweakRisk.Low,
                "Menos tempo reservado a tarefas de fundo multimídia",
                "Reduz a fatia de CPU que o agendador multimídia reserva para tarefas em segundo plano (padrão 20%).",
                new[] { new RegistryValueSpec(Hklm, systemProfile, "SystemResponsiveness", 10) })
            {
                Effect = "Um pouco mais de CPU para o jogo; áudio e vídeo em segundo plano têm margem menor.",
            },

            new RegistryTweak(store, "game-mpo-off", TweakCategory.Gaming, TweakRisk.Medium,
                "Desativar o Multiplane Overlay (MPO)",
                "Desliga a composição por planos de sobreposição do gerenciador de janelas, que causa tela piscando e travadas em alguns drivers.",
                new[] { new RegistryValueSpec(Hklm, @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode", 5) })
            {
                Effect = "Resolve piscadas, tela preta ao alternar janelas e travadas em algumas combinações de driver (NVIDIA, AMD e Intel).",
                Warning = "Só use se você tem o problema; pode aumentar um pouco o uso de GPU na área de trabalho.",
                RequiresRestart = true,
            },

            new SubKeyValueTweak(store, "game-amd-ulps-off", TweakCategory.Gaming, TweakRisk.Medium,
                "AMD: desativar o ULPS",
                "Impede o modo de energia ultrabaixa (ULPS) da placa de vídeo AMD, que causa quedas de clock e travadas em alguns PCs.",
                Hklm, GpuClass.ClassPath, new (string, object, RegistryValueKind)[] { ("EnableUlps", 0, RegistryValueKind.DWord) },
                key => GpuClass.IsVendorKey(key, "AMD", "Radeon"))
            {
                Effect = "Evita quedas de clock e piscadas em PCs com placa AMD (mais comum em notebooks e CrossFire).",
                Warning = "A placa AMD fica sem esse modo de economia; mais consumo em repouso.",
                RequiresRestart = true,
                AppliesTo = GpuClass.Amd,
            },

            new RegistryTweak(store, "game-amd-telemetry-off", TweakCategory.Gaming, TweakRisk.Low,
                "AMD: desligar conteúdo da web e atualização automática do painel",
                "Desliga o conteúdo da web e a atualização automática do AMD Software.",
                new[]
                {
                    new RegistryValueSpec(Hklm, @"SOFTWARE\AMD\CN", "AllowWebContent", 0),
                    new RegistryValueSpec(Hklm, @"SOFTWARE\AMD\CN", "AutoUpdate", 0),
                })
            {
                Effect = "Painel da AMD mais leve e sem consultas à internet.",
                AppliesTo = GpuClass.Amd,
            },

            new ServiceTweak(store, "game-amd-crash-defender", TweakCategory.Gaming, TweakRisk.Medium,
                "AMD: desativar o Crash Defender",
                "Desativa o serviço de coleta de relatórios de falha do driver AMD.",
                new[] { "AMD Crash Defender Service" }, 4)
            {
                Effect = "Um serviço a menos; o driver continua funcionando.",
                Warning = "A AMD deixa de receber relatórios de falha do seu driver.",
                AppliesTo = GpuClass.Amd,
            },

            new ServiceTweak(store, "game-nvidia-telemetry-off", TweakCategory.Gaming, TweakRisk.Medium,
                "NVIDIA: desativar a telemetria",
                "Desativa o serviço de telemetria da NVIDIA (NvTelemetryContainer), quando o driver o instala.",
                new[] { "NvTelemetryContainer" }, 4)
            {
                Effect = "Menos atividade em segundo plano. O driver e o painel continuam funcionando.",
                Warning = "O serviço principal (NVDisplay.ContainerLocalSystem) não é alterado, para o painel da NVIDIA continuar funcionando.",
                AppliesTo = GpuClass.Nvidia,
            },

            new RegistryTweak(store, "storage-lastaccess-off", TweakCategory.Storage, TweakRisk.Low,
                "Não gravar a data do último acesso aos arquivos",
                "Desliga a atualização do \"último acesso\" do NTFS a cada leitura de arquivo.",
                new[] { new RegistryValueSpec(Hklm, @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisableLastAccessUpdate", unchecked((int)0x80000001)) })
            {
                Effect = "Menos escritas pequenas no disco, principalmente em HD e em pastas com muitos arquivos.",
                Warning = "Programas de backup e de busca que usam o \"último acesso\" deixam de ter essa informação.",
                RequiresRestart = true,
                Recommended = true,
            },

            new RegistryTweak(store, "storage-8dot3-off", TweakCategory.Storage, TweakRisk.Medium,
                "Não criar nomes curtos 8.3 nos arquivos",
                "Desliga a criação de nomes de arquivo no formato antigo (ARQUIV~1.TXT) para arquivos novos.",
                new[] { new RegistryValueSpec(Hklm, @"SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisable8dot3NameCreation", 1) })
            {
                Effect = "Criar e listar arquivos em pastas muito grandes fica um pouco mais rápido.",
                Warning = "Instaladores muito antigos (16 bits) podem falhar. Arquivos que já existem não mudam.",
                RequiresRestart = true,
            },

            new SubKeyValueTweak(store, "net-tcp-nodelay", TweakCategory.Network, TweakRisk.Medium,
                "TCP sem espera de confirmação (Nagle desligado)",
                "Envia pacotes pequenos na hora, sem esperar juntar mais dados, em todas as conexões do PC.",
                Hklm, @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces",
                new (string, object, RegistryValueKind)[] { ("TcpAckFrequency", 1, RegistryValueKind.DWord), ("TCPNoDelay", 1, RegistryValueKind.DWord) },
                _ => true)
            {
                Effect = "Pode baixar o ping em alguns jogos online que usam TCP.",
                Warning = "Gasta mais banda com pacotes pequenos. Jogos modernos usam UDP, onde isso não muda nada. Meça o ping antes e depois.",
                RequiresRestart = true,
            },

            new SubKeyValueTweak(store, "net-adapter-power-off", TweakCategory.Network, TweakRisk.Low,
                "Placas de rede sem economia de energia",
                "Impede o Windows de desligar a placa de rede para economizar energia.",
                Hklm, @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}",
                new (string, object, RegistryValueKind)[] { ("PnPCapabilities", 24, RegistryValueKind.DWord) },
                key => key.GetValue("NetCfgInstanceId") is not null && key.GetValue("DriverDesc") is string d
                    && !d.Contains("Virtual", StringComparison.OrdinalIgnoreCase) && !d.Contains("Miniport", StringComparison.OrdinalIgnoreCase)
                    && !d.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase))
            {
                Effect = "Evita quedas e atraso ao acordar a conexão (Wi-Fi e cabo).",
                Warning = "Em notebook com bateria gasta um pouco mais de energia.",
                RequiresRestart = true,
                Recommended = true,
            },

            new RegistryTweak(store, "periph-keyboard-response", TweakCategory.Peripherals, TweakRisk.Low,
                "Teclado com repetição rápida",
                "Define o atraso mínimo e a velocidade máxima de repetição das teclas.",
                new[]
                {
                    new RegistryValueSpec(Hkcu, @"Control Panel\Keyboard", "KeyboardDelay", "0", Str),
                    new RegistryValueSpec(Hkcu, @"Control Panel\Keyboard", "KeyboardSpeed", "31", Str),
                })
            {
                Effect = "Teclas repetem mais cedo e mais rápido (movimento contínuo em jogos e edição de texto).",
                Warning = "Se você digita letras repetidas sem querer, volte ao padrão.",
                RequiresRestart = true,
            },

            // ---------------------------------------------------------------- Navegadores (políticas; desfazer remove a política)
            BrowserTweak(store, "browser-chrome", "Google Chrome", @"SOFTWARE\Policies\Google\Chrome"),
            BrowserTweak(store, "browser-edge", "Microsoft Edge", @"SOFTWARE\Policies\Microsoft\Edge"),
            BrowserTweak(store, "browser-brave", "Brave", @"SOFTWARE\Policies\BraveSoftware\Brave"),
        };

        return tweaks.Concat(CommandTweaks.Create(store)).Concat(TweakCatalogExtra.Create(store)).ToList();
    }

    private static RegistryTweak BrowserTweak(TweakStore store, string id, string name, string policyPath) =>
        new(store, id, TweakCategory.Privacy, TweakRisk.Low,
            $"{name}: menos segundo plano e telemetria",
            $"Define políticas do {name}: não continuar rodando em segundo plano ao fechar, sem pré-carregar páginas, sem sugestões de busca e sem relatórios de uso.",
            new[]
            {
                new RegistryValueSpec(RegistryHive.LocalMachine, policyPath, "BackgroundModeEnabled", 0),
                new RegistryValueSpec(RegistryHive.LocalMachine, policyPath, "NetworkPredictionOptions", 2),
                new RegistryValueSpec(RegistryHive.LocalMachine, policyPath, "SearchSuggestEnabled", 0),
                new RegistryValueSpec(RegistryHive.LocalMachine, policyPath, "MetricsReportingEnabled", 0),
            })
        {
            Effect = "Menos processos, RAM e consultas em segundo plano.",
            Warning = $"O {name} passa a mostrar \"gerenciado pela sua organização\" (é a política). Desfazer remove isso.",
        };

}
