using System.Text.RegularExpressions;
using MBExtreme.Core.Games;
using MBExtreme.Core.Startup;

namespace MBExtreme.Core.Tools;

/// <summary>O que o botão "Otimizar" faz com cada item: mexe sozinho, sugere ao usuário ou nunca toca.</summary>
public enum OptimizeAdvice { Safe, IfUnused, Keep, Unknown }

/// <summary>Um serviço que o "Otimizar serviços" conhece: nome interno, nome amigável, o que faz e a recomendação.</summary>
public sealed record KnownService(string Name, string Title, string WhatIs, OptimizeAdvice Advice, bool Windows);

/// <summary>
/// Regras do botão "Otimizar" das telas de Inicialização, Processos abertos e Serviços. Só mexe no que é seguro: nada do Windows
/// essencial, antivírus, áudio, rede, driver de vídeo, controle, antitrapaça ou loja de jogos.
/// </summary>
public static class OptimizeAdvisor
{
    // ---------------------------------------------------------------- inicialização

    /// <summary>O "Otimizar inicialização" desliga o que pode desligar e o que só serve se a pessoa usa (ela pode religar depois).</summary>
    public static bool StartupOneClick(StartupAdvice advice) => advice is StartupAdvice.CanDisable or StartupAdvice.DisableIfUnused;

    /// <summary>Peso do programa na hora de ligar (para a coluna Impacto).</summary>
    public static string StartupImpact(string name, string command)
    {
        string text = (name + " " + command).ToLowerInvariant();
        if (Regex.IsMatch(text, "teams|onedrive|steam|epic|eadesktop|origin|battle\\.net|ubisoft|creative cloud|adobe|docker|discord|spotify|dropbox|googledrive|icloud"))
            return "Alto";
        if (Regex.IsMatch(text, "update|updater|helper|tray|agent|launcher|sync"))
            return "Médio";
        return "Baixo";
    }

    // ---------------------------------------------------------------- processos abertos

    /// <summary>Processo que o "Otimizar processos" fecha sozinho: ajudantes de fundo (atualizadores, widgets, sincronização) sem uso.</summary>
    public static bool ProcessOneClick(string name) => GameModeRules.CanAutoClose(name);

    /// <summary>Nunca encerrar (sistema, segurança, drivers, antitrapaça, lojas de jogos, voz e captura, o próprio MB).</summary>
    public static bool ProcessProtected(string name) => ProcessList.IsProtectedName(name) || GameModeRules.NeverTouch.Contains(name);

    /// <summary>Tipo do processo para a coluna: Windows, Aplicativo (tem janela) ou Segundo plano.</summary>
    public static string ProcessKind(string name, bool hasWindow) =>
        ProcessProtected(name) && !hasWindow ? "Windows" : hasWindow ? "Aplicativo" : "Segundo plano";

    // ---------------------------------------------------------------- serviços

    /// <summary>Serviços conhecidos. Os do Windows só entram aqui quando quase ninguém usa; os outros ficam fora da tela.</summary>
    public static IReadOnlyList<KnownService> Services { get; } = new KnownService[]
    {
        // Windows: dá para deixar em Manual (o Windows ainda inicia se algum programa precisar)
        new("DiagTrack", "Telemetria do Windows", "Envia dados de uso para a Microsoft.", OptimizeAdvice.Safe, true),
        new("dmwappushservice", "Mensagens WAP (telemetria)", "Apoio da telemetria.", OptimizeAdvice.Safe, true),
        new("WerSvc", "Relatório de erros", "Manda relatório para a Microsoft a cada erro.", OptimizeAdvice.Safe, true),
        new("MapsBroker", "Mapas baixados", "Atualiza mapas offline.", OptimizeAdvice.Safe, true),
        new("lfsvc", "Localização", "Informa a localização do PC aos aplicativos.", OptimizeAdvice.IfUnused, true),
        new("RetailDemo", "Modo de demonstração de loja", "Só serve em PC de vitrine.", OptimizeAdvice.Safe, true),
        new("Fax", "Fax", "Envio de fax pelo PC.", OptimizeAdvice.Safe, true),
        new("WMPNetworkSvc", "Compartilhamento do Windows Media Player", "Compartilha músicas e vídeos na rede.", OptimizeAdvice.Safe, true),
        new("CscService", "Arquivos offline", "Cópias de pastas da rede da empresa.", OptimizeAdvice.IfUnused, true),
        new("SharedAccess", "Compartilhar internet", "Hotspot pelo cabo.", OptimizeAdvice.IfUnused, true),
        new("RemoteRegistry", "Registro remoto", "Deixa outro PC mexer no registro deste.", OptimizeAdvice.Safe, true),
        new("PhoneSvc", "Telefone", "Ligações pelo PC.", OptimizeAdvice.IfUnused, true),
        new("TabletInputService", "Teclado virtual e caneta", "Tela touch e caneta.", OptimizeAdvice.IfUnused, true),
        new("WSearch", "Indexação da pesquisa", "Deixa a busca do Windows mais rápida, mas lê o disco em segundo plano.", OptimizeAdvice.IfUnused, true),
        new("SysMain", "SysMain (pré-carregamento)", "Pré-carrega programas na memória. Em SSD ajuda pouco.", OptimizeAdvice.IfUnused, true),
        // outros fabricantes: atualizadores que rodam o tempo todo
        new("gupdate", "Google Update", "Atualizador do Google. O Chrome se atualiza ao abrir.", OptimizeAdvice.Safe, false),
        new("gupdatem", "Google Update (sob demanda)", "Atualizador do Google.", OptimizeAdvice.Safe, false),
        new("GoogleUpdaterService", "Google Updater", "Atualizador do Google.", OptimizeAdvice.Safe, false),
        new("edgeupdate", "Microsoft Edge Update", "Atualizador do Edge. Ele se atualiza ao abrir.", OptimizeAdvice.Safe, false),
        new("edgeupdatem", "Microsoft Edge Update (sob demanda)", "Atualizador do Edge.", OptimizeAdvice.Safe, false),
        new("AdobeARMservice", "Adobe Acrobat Update", "Atualizador do Adobe Reader.", OptimizeAdvice.Safe, false),
        new("MozillaMaintenance", "Mozilla Maintenance", "Atualizador do Firefox.", OptimizeAdvice.Safe, false),
        new("brave", "Brave Update", "Atualizador do Brave.", OptimizeAdvice.Safe, false),
        new("bravem", "Brave Update (sob demanda)", "Atualizador do Brave.", OptimizeAdvice.Safe, false),
        new("AGSService", "Adobe Genuine Software", "Verificação de licença da Adobe.", OptimizeAdvice.Safe, false),
        new("AdobeUpdateService", "Adobe Update", "Atualizador da Adobe.", OptimizeAdvice.Safe, false),
        new("ClickToRunSvc", "Office Click-to-Run", "Atualiza o Office. Sem ele, o Office pode não abrir.", OptimizeAdvice.Keep, false),
    };

    private static readonly Regex KeepService = new(
        "antivir|defender|security|kaspersky|avast|avg|norton|mcafee|eset|bitdefender|malwarebytes|nvidia|nvdisplay|amd|radeon|intel|realtek|audio|sound|" +
        "bluetooth|wlan|wifi|network|vpn|steam|epic|easyanticheat|battleye|vanguard|vgc|faceit|xbox|gaming|razer|logitech|lghub|corsair|steelseries|asus|armoury|msi|" +
        "mbextreme|rustdesk|anydesk|teamviewer|printer|spooler|vmware|virtualbox|hyper-v",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Recomendação para um serviço pelo nome interno e o nome que aparece.</summary>
    public static OptimizeAdvice ServiceAdvice(string name, string display)
    {
        var known = Services.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (known is not null) return known.Advice;
        if (KeepService.IsMatch(name + " " + display)) return OptimizeAdvice.Keep;
        if (Regex.IsMatch(name + " " + display, "update|updater|telemetry|crash|feedback", RegexOptions.IgnoreCase)) return OptimizeAdvice.Safe;
        return OptimizeAdvice.Unknown;
    }

    /// <summary>O "Otimizar serviços" passa para Manual só os seguros que estão em Automático.</summary>
    public static bool ServiceOneClick(OptimizeAdvice advice, bool automatic) => automatic && advice == OptimizeAdvice.Safe;

    public static string AdviceText(OptimizeAdvice advice) => advice switch
    {
        OptimizeAdvice.Safe => "Pode otimizar",
        OptimizeAdvice.IfUnused => "Se não usa",
        OptimizeAdvice.Keep => "Deixe como está",
        _ => "Confira antes",
    };

    public static string AdviceBrush(OptimizeAdvice advice) => advice switch
    {
        OptimizeAdvice.Safe => "OkBrush",
        OptimizeAdvice.IfUnused => "WarnBrush",
        OptimizeAdvice.Keep => "BadBrush",
        _ => "MutedBrush",
    };
}
