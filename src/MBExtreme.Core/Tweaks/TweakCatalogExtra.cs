using System.Runtime.Versioning;
using Microsoft.Win32;

namespace MBExtreme.Core.Tweaks;

/// <summary>
/// Ajustes de qualidade de vida, Explorador, tela de bloqueio, inicialização e dispositivos (rede e MSI).
/// Só chaves documentadas pela Microsoft ou pelos fabricantes; cada uma guarda o valor original para desfazer.
/// </summary>
[SupportedOSPlatform("windows")]
public static class TweakCatalogExtra
{
    private const RegistryHive Hklm = RegistryHive.LocalMachine;
    private const RegistryHive Hkcu = RegistryHive.CurrentUser;
    private const RegistryValueKind Str = RegistryValueKind.String;
    private const string ExplorerAdvanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    public const string New = "Novo";

    public static IEnumerable<Tweak> Create(TweakStore store)
    {
        // ---------------------------------------------------------------- Explorador e menus
        yield return new ClassicContextMenuTweak(store, "qol-classic-context-menu", TweakCategory.Visual, TweakRisk.Low,
            "Menu clássico do botão direito (Windows 11)",
            "Volta o menu completo do botão direito, sem precisar clicar em \"Mostrar mais opções\".")
        {
            Effect = "Todas as opções do menu aparecem de uma vez (7-Zip, WinRAR, editores...).",
            Details = "Cria a chave vazia HKCU\\Software\\Classes\\CLSID\\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\\InprocServer32, que faz o Explorador usar o menu antigo. Vale só para a sua conta. Desfazer apaga a chave. O efeito aparece depois de sair e entrar na conta (ou de reiniciar o Explorador em Reparar o Windows).",
            MinBuild = 22000, RequiresRestart = true, Badge = New, Recommended = true,
        };

        yield return new RegistryTweak(store, "qol-shortcut-suffix", TweakCategory.Visual, TweakRisk.Low,
            "Atalhos sem o \"- Atalho\" no nome",
            "Ao criar um atalho, o Windows deixa de acrescentar \"- Atalho\" no final do nome.",
            new[] { new RegistryValueSpec(Hkcu, @"Software\Microsoft\Windows\CurrentVersion\Explorer\NamingTemplates", "ShortcutNameTemplate", "%s.lnk", Str) })
        {
            Effect = "Atalhos novos ficam com o mesmo nome do arquivo ou programa.",
            Details = "Grava ShortcutNameTemplate = \"%s.lnk\" em HKCU\\...\\Explorer\\NamingTemplates. Atalhos que já existem não mudam.",
            Badge = New, Recommended = true,
        };

        yield return new RegistryTweak(store, "qol-explorer-compact", TweakCategory.Visual, TweakRisk.Low,
            "Explorador de Arquivos em modo compacto (Windows 11)",
            "Diminui o espaço entre os itens das pastas, como era no Windows 10.",
            new[] { new RegistryValueSpec(Hkcu, ExplorerAdvanced, "UseCompactMode", 1) })
        {
            Effect = "Mais arquivos cabem na tela sem rolar.",
            Details = "Mesmo que marcar Exibir > Modo de exibição compacto. Grava UseCompactMode = 1.",
            MinBuild = 22000, Badge = New,
        };

        yield return new RegistryTweak(store, "qol-explorer-thispc", TweakCategory.Visual, TweakRisk.Low,
            "Explorador abre em \"Este Computador\"",
            "O Explorador de Arquivos abre mostrando os discos em vez de Início/Acesso rápido.",
            new[] { new RegistryValueSpec(Hkcu, ExplorerAdvanced, "LaunchTo", 1) })
        {
            Effect = "Chega aos discos com um clique a menos.",
            Details = "Mesmo que Opções de pasta > Abrir o Explorador de Arquivos em: Este Computador. Grava LaunchTo = 1.",
            Badge = New,
        };

        yield return new RegistryTweak(store, "qol-show-extensions", TweakCategory.Visual, TweakRisk.Low,
            "Mostrar as extensões dos arquivos",
            "Mostra o final do nome do arquivo (.exe, .pdf, .jpg).",
            new[] { new RegistryValueSpec(Hkcu, ExplorerAdvanced, "HideFileExt", 0) })
        {
            Effect = "Fica fácil ver que \"foto.jpg.exe\" é um programa e não uma imagem (proteção contra vírus).",
            Goals = TweakGoal.QualityOfLife | TweakGoal.Privacy,
            Badge = New, Recommended = true,
        };

        // ---------------------------------------------------------------- Visual
        yield return new RegistryTweak(store, "visual-jpeg-quality", TweakCategory.Visual, TweakRisk.Low,
            "Papel de parede sem perda de qualidade",
            "Impede o Windows de recomprimir a imagem do papel de parede (o padrão é 85% de qualidade).",
            new[] { new RegistryValueSpec(Hkcu, @"Control Panel\Desktop", "JPEGImportQuality", 100) })
        {
            Effect = "Papel de parede nítido, sem borrões e faixas de cor.",
            Details = "Grava JPEGImportQuality = 100. Vale para o próximo papel de parede escolhido: escolha a imagem de novo depois de aplicar.",
            Badge = New, Recommended = true,
        };

        yield return new RegistryTweak(store, "visual-lockscreen-blur", TweakCategory.Visual, TweakRisk.Low,
            "Tela de login sem desfoque",
            "Mostra a imagem de fundo nítida na tela onde você digita a senha.",
            new[] { new RegistryValueSpec(Hklm, @"SOFTWARE\Policies\Microsoft\Windows\System", "DisableAcrylicBackgroundOnLogon", 1) })
        {
            Effect = "Tela de login um pouco mais leve e com a imagem nítida.",
            Details = "Política \"Mostrar plano de fundo claro na tela de entrada\" (DisableAcrylicBackgroundOnLogon = 1).",
            Badge = New,
        };

        yield return new RegistryTweak(store, "qol-lockscreen-off", TweakCategory.Visual, TweakRisk.Low,
            "Ir direto para a senha (sem a tela de bloqueio)",
            "Pula a tela com relógio e imagem: ao ligar ou bloquear, já aparece o campo de senha ou PIN.",
            new[] { new RegistryValueSpec(Hklm, @"SOFTWARE\Policies\Microsoft\Windows\Personalization", "NoLockScreen", 1) })
        {
            Effect = "Um clique (ou tecla) a menos toda vez que liga ou desbloqueia o PC.",
            Warning = "A senha continua sendo pedida. Você deixa de ver notificações e o relógio na tela de bloqueio.",
            Details = "Política \"Não exibir a tela de bloqueio\" (NoLockScreen = 1). Em algumas edições Home o Windows pode ignorar a política.",
            Badge = New,
        };

        // ---------------------------------------------------------------- Notificações e funções do Windows
        yield return new RegistryTweak(store, "qol-notifications-off", TweakCategory.Privacy, TweakRisk.Low,
            "Desligar as notificações dos aplicativos",
            "Para os balões de notificação (toasts) de todos os aplicativos.",
            new[] { new RegistryValueSpec(Hkcu, @"Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled", 0) })
        {
            Effect = "Nada aparece no canto da tela durante o jogo ou o trabalho.",
            Warning = "Você deixa de ver avisos de e-mail, mensagens e lembretes. Para só durante os jogos, use a opção do Modo Jogo.",
            Goals = TweakGoal.QualityOfLife | TweakGoal.FpsLatency,
            RequiresRestart = true, Badge = New,
        };

        yield return new RegistryTweak(store, "qol-storage-sense-off", TweakCategory.Storage, TweakRisk.Low,
            "Desligar o Sensor de Armazenamento",
            "Impede o Windows de apagar sozinho arquivos da Lixeira, da pasta Downloads e do OneDrive.",
            new[] { new RegistryValueSpec(Hklm, @"SOFTWARE\Policies\Microsoft\Windows\StorageSense", "AllowStorageSenseGlobal", 0) })
        {
            Effect = "Nenhum arquivo some sem você mandar. A Limpeza do MB Extreme faz o mesmo trabalho com prévia.",
            Details = "Política \"Permitir o Sensor de Armazenamento\" = desativada (AllowStorageSenseGlobal = 0).",
            Badge = New,
        };

        yield return new RegistryTweak(store, "qol-insider-hide", TweakCategory.Privacy, TweakRisk.Low,
            "Esconder o Programa Windows Insider",
            "Tira a página do Insider das Configurações do Windows, para ninguém entrar em versões de teste sem querer.",
            new[] { new RegistryValueSpec(Hklm, @"SOFTWARE\Microsoft\WindowsSelfHost\UI\Visibility", "HideInsiderPage", 1) })
        {
            Effect = "Evita trocar o Windows estável por uma versão de teste.",
            Details = "Grava HideInsiderPage = 1. Não muda as atualizações normais do Windows.",
            Goals = TweakGoal.QualityOfLife,
            Badge = New,
        };

        yield return new RegistryTweak(store, "qol-tray-icons", TweakCategory.Visual, TweakRisk.Low,
            "Mostrar todos os ícones da bandeja (Windows 10)",
            "Mostra todos os ícones perto do relógio, sem a setinha que esconde os outros.",
            new[] { new RegistryValueSpec(Hkcu, @"Software\Microsoft\Windows\CurrentVersion\Explorer", "EnableAutoTray", 0) })
        {
            Effect = "Você vê na hora quais programas estão rodando em segundo plano.",
            MaxBuild = 21999, RequiresRestart = true, Badge = New,
        };

        yield return new RegistryTweak(store, "qol-photo-viewer", TweakCategory.Visual, TweakRisk.Low,
            "Visualizador de Fotos clássico no \"Abrir com\"",
            "Volta o Visualizador de Fotos do Windows (rápido e leve) para a lista \"Abrir com\" de imagens.",
            new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".ico", ".dib", ".jfif" }
                .Select(ext => new RegistryValueSpec(Hklm, @"SOFTWARE\Microsoft\Windows Photo Viewer\Capabilities\FileAssociations", ext, "PhotoViewer.FileAssoc.Tiff", Str))
                .ToArray())
        {
            Effect = "Imagens abrem na hora, sem esperar o aplicativo Fotos carregar.",
            Details = "Registra os tipos de imagem no Visualizador de Fotos que já vem no Windows. Depois clique com o botão direito numa imagem > Abrir com > Escolher outro aplicativo > Visualizador de Fotos do Windows > Sempre. O app não troca o programa padrão sozinho (o Windows protege essa escolha).",
            Badge = New,
        };

        // ---------------------------------------------------------------- Inicialização
        yield return new RegistryTweak(store, "boot-verbose-status", TweakCategory.Boot, TweakRisk.Low,
            "Mostrar o que o Windows está fazendo ao ligar e desligar",
            "Troca \"Aguarde\" por mensagens detalhadas (\"Aplicando configurações do usuário\", \"Parando serviços\"...).",
            new[] { new RegistryValueSpec(Hklm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "VerboseStatus", 1) })
        {
            Effect = "Ajuda a descobrir o que deixa a inicialização ou o desligamento lento.",
            Details = "Política \"Exibir mensagens de status altamente detalhadas\" (VerboseStatus = 1).",
            Badge = New,
        };

        // ---------------------------------------------------------------- NVIDIA
        yield return new RegistryTweak(store, "game-nvidia-dlss-indicator", TweakCategory.Gaming, TweakRisk.Low,
            "NVIDIA: indicador do DLSS na tela",
            "Mostra no canto do jogo se o DLSS está ligado, a versão e o modo. Serve para conferir se a opção do jogo funcionou.",
            new[] { new RegistryValueSpec(Hklm, @"SOFTWARE\NVIDIA Corporation\Global\NGXCore", "ShowDlssIndicator", 0x400) })
        {
            Effect = "Só informação na tela; não muda o desempenho. Desligue depois de conferir.",
            Details = "Valor ShowDlssIndicator = 1024 na chave do NGX do driver NVIDIA. Funciona em jogos com DLSS 2 ou mais novo.",
            AppliesTo = GpuClass.Nvidia, Badge = New,
        };

        // ---------------------------------------------------------------- Rede (propriedades do driver)
        yield return new TargetedRegistryTweak(store, "net-nic-buffers", TweakCategory.Network, TweakRisk.Medium,
            "Buffers de recebimento e envio da placa de rede no máximo",
            "Aumenta as filas de pacotes da placa de rede até o máximo que o driver aceita.",
            Hklm, () => DeviceRegistry.AdapterProperties(new[] { "*ReceiveBuffers", "*TransmitBuffers" }, DeviceRegistry.NumericMax))
        {
            Effect = "Menos pacotes perdidos em picos de tráfego (download pesado junto com jogo ou live).",
            Warning = "Usa alguns MB a mais de memória. A placa de rede reinicia na próxima inicialização; se a conexão ficar instável, desfaça.",
            Details = "Lê o máximo declarado pelo próprio driver (Ndi\\Params\\*ReceiveBuffers\\max) em cada placa de rede física e grava esse valor. Placas sem a propriedade numérica ficam como estão.",
            RequiresRestart = true, Badge = New,
        };

        yield return new TargetedRegistryTweak(store, "net-lso-off", TweakCategory.Network, TweakRisk.Medium,
            "Desligar o Large Send Offload (LSO)",
            "A CPU passa a dividir os pacotes grandes de envio, em vez da placa de rede.",
            Hklm, () => DeviceRegistry.AdapterProperties(new[] { "*LsoV2IPv4", "*LsoV2IPv6" }, DeviceRegistry.EnumOffOption))
        {
            Effect = "Em algumas placas (principalmente Realtek e Intel antigas) reduz picos de ping durante uploads.",
            Warning = "Usa um pouco mais de CPU em uploads rápidos. Meça o ping e a velocidade antes e depois.",
            Details = "Grava *LsoV2IPv4 = 0 e *LsoV2IPv6 = 0 nas placas físicas que oferecem a opção \"Desativado\" no driver.",
            RequiresRestart = true, Badge = New,
        };

        // ---------------------------------------------------------------- Interrupções (MSI)
        yield return new TargetedRegistryTweak(store, "game-msi-mode", TweakCategory.Gaming, TweakRisk.Medium,
            "Modo MSI na placa de vídeo e na placa de rede",
            "Liga as interrupções por mensagem (Message Signaled Interrupts) nos dispositivos PCI de vídeo e rede.",
            Hklm, () => DeviceRegistry.MsiTargets(DeviceRegistry.DisplayClass, DeviceRegistry.NetClass))
        {
            Effect = "Interrupções sem compartilhamento de linha: pode reduzir a latência (DPC) e engasgos.",
            Warning = "Muitos drivers modernos já usam MSI. Se aparecer tela preta ou o dispositivo sumir, desfaça (pelo Modo de Segurança, se preciso). Crie um ponto de restauração antes.",
            Details = "Grava MSISupported = 1 em Enum\\PCI\\...\\Device Parameters\\Interrupt Management\\MessageSignaledInterruptProperties. Se o dispositivo não suportar MSI, o Windows ignora o valor. Confira o efeito com o Diagnóstico de latência.",
            RequiresRestart = true, Badge = New,
        };
    }
}
