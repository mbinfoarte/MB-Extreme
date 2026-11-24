using System.Text.RegularExpressions;
using MBExtreme.Core.Windows;

namespace MBExtreme.Core.Startup;

public enum StartupAdvice { CanDisable, DisableIfUnused, KeepOn, CheckFirst }

/// <summary>Medição do tempo para ligar, segundo o próprio Windows.</summary>
public sealed record BootTiming(DateTime At, TimeSpan Boot, TimeSpan? MainPath);

/// <summary>
/// "O que é" de cada programa da inicialização e se dá para desligar, a partir de uma lista dos mais comuns (nome do programa ou do arquivo).
/// Desconhecidos ficam como "confira antes". E o tempo de ligar que o Windows anota (evento 100 do diagnóstico de desempenho).
/// </summary>
public static class StartupAdvisor
{
    private static readonly (string Pattern, string WhatIs, StartupAdvice Advice)[] Known =
    {
        (@"onedrive", "Sincroniza seus arquivos com o OneDrive. Desligado, só sincroniza quando você abrir.", StartupAdvice.DisableIfUnused),
        (@"googleupdate|google updater|googleupdater", "Procura atualização do Chrome e de outros programas do Google. O Chrome se atualiza sozinho quando você abre.", StartupAdvice.CanDisable),
        (@"msedge|microsoft edge", "Deixa o Edge pré-carregado para abrir mais rápido.", StartupAdvice.CanDisable),
        (@"teams", "Abre o Microsoft Teams com o Windows.", StartupAdvice.DisableIfUnused),
        (@"discord", "Abre o Discord com o Windows. Desligado, você só não recebe mensagens até abrir.", StartupAdvice.DisableIfUnused),
        (@"spotify", "Abre o Spotify com o Windows.", StartupAdvice.CanDisable),
        (@"steam", "Abre a Steam com o Windows (e atualiza jogos em segundo plano).", StartupAdvice.DisableIfUnused),
        (@"epicgames|epic games", "Abre a Epic Games com o Windows.", StartupAdvice.CanDisable),
        (@"eadesktop|ea app|electronic arts|origin", "Loja de jogos da EA. Os jogos atualizam quando você abre.", StartupAdvice.CanDisable),
        (@"ubisoft|uplay", "Loja de jogos da Ubisoft.", StartupAdvice.CanDisable),
        (@"battle\.net|blizzard", "Loja de jogos da Blizzard.", StartupAdvice.CanDisable),
        (@"cortana", "Assistente de voz da Microsoft. Quase ninguém usa.", StartupAdvice.CanDisable),
        (@"docker", "Ferramenta de programação que usa bastante memória. Abra quando for usar.", StartupAdvice.CanDisable),
        (@"skype", "Abre o Skype com o Windows.", StartupAdvice.CanDisable),
        (@"zoom", "Abre o Zoom com o Windows.", StartupAdvice.CanDisable),
        (@"adobe.*(arm|updater|acrotray|creative cloud|ccx)", "Atualizador e serviços da Adobe. Os programas da Adobe funcionam sem isso.", StartupAdvice.CanDisable),
        (@"itunes|ituneshelper", "Ajuda o iTunes a reconhecer o iPhone na hora.", StartupAdvice.DisableIfUnused),
        (@"canva", "Abre o Canva com o Windows.", StartupAdvice.CanDisable),
        (@"clickup|notion|slack", "Abre o aplicativo de trabalho com o Windows.", StartupAdvice.DisableIfUnused),
        (@"anydesk|teamviewer|rustdesk", "Deixa o acesso remoto pronto para receber conexões.", StartupAdvice.DisableIfUnused),
        (@"epson|hp.*(scan|print)|canon|brother", "Programa da impressora (avisos de tinta e digitalização). A impressora imprime sem ele.", StartupAdvice.DisableIfUnused),
        (@"securityhealth|windows security|windowsdefender", "Ícone da Segurança do Windows (antivírus). Deixe ligado.", StartupAdvice.KeepOn),
        (@"rtkaud|realtek|nahimic|waves|maxxaudio|dolby", "Painel de áudio do fabricante. Alguns efeitos e o conector frontal dependem dele.", StartupAdvice.KeepOn),
        (@"igfx|intel.*graphics|radeonsoftware|amd.*(software|noise)|nvidia|nvbackend|nvcontainer", "Programa da placa de vídeo (atalhos, gravação, ajustes). O driver funciona sem ele, mas alguns recursos somem.", StartupAdvice.CheckFirst),
        (@"logitech|lghub|razer|synapse|corsair|icue|steelseries|armoury|aura", "Programa de mouse, teclado ou luzes RGB. Sem ele, perfis e cores podem voltar ao padrão.", StartupAdvice.CheckFirst),
        (@"synaptics|elan|touchpad", "Driver do touchpad do notebook. Deixe ligado.", StartupAdvice.KeepOn),
        (@"bluetooth", "Ajuda o Bluetooth a reconectar os aparelhos.", StartupAdvice.KeepOn),
        (@"kaspersky|avast|avg|norton|mcafee|eset|bitdefender|malwarebytes", "Antivírus. Deixe ligado.", StartupAdvice.KeepOn),
        (@"mbextreme|mb extreme", "O MB Extreme (Modo Jogo, limpeza automática e monitoramento).", StartupAdvice.KeepOn),
        (@"whatsapp|telegram", "Abre o aplicativo de mensagens com o Windows.", StartupAdvice.DisableIfUnused),
        (@"dropbox|googledrivefs|google drive", "Sincroniza arquivos com a nuvem.", StartupAdvice.DisableIfUnused),
        (@"lively|wallpaper engine|translucenttb|rainmeter|startallback|explorerpatcher", "Personalização da área de trabalho ou da barra. Sem ele, o visual volta ao padrão.", StartupAdvice.CheckFirst),
        (@"msi.*(center|afterburner)|rtss|rivatuner", "Monitor ou ajuste da placa de vídeo. Se você usa curva de ventoinha ou overclock, deixe ligado.", StartupAdvice.CheckFirst),
        (@"java.*update|jusched", "Procura atualização do Java. Pode desligar.", StartupAdvice.CanDisable),
        (@"ccleaner|driverbooster|iobit|advanced systemcare", "Programa de \"otimização\" que fica rodando o tempo todo. Pode desligar.", StartupAdvice.CanDisable),
    };

    public static (string WhatIs, StartupAdvice Advice) Describe(string name, string command)
    {
        string text = (name + " " + command).ToLowerInvariant();
        foreach (var (pattern, what, advice) in Known)
            if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase)) return (what, advice);
        return ("O MB Extreme não conhece este programa. Veja o nome do arquivo antes de desligar.", StartupAdvice.CheckFirst);
    }

    public static string AdviceText(StartupAdvice advice) => advice switch
    {
        StartupAdvice.CanDisable => "Pode desligar",
        StartupAdvice.DisableIfUnused => "Desligue se não usa",
        StartupAdvice.KeepOn => "Melhor deixar ligado",
        _ => "Confira antes",
    };

    /// <summary>Lê o XML do wevtutil com os eventos 100 (BootTime e MainPathBootTime, em milissegundos).</summary>
    public static IReadOnlyList<BootTiming> ParseBootEvents(string xml)
    {
        var list = new List<BootTiming>();
        foreach (Match ev in Regex.Matches(xml, @"<Event[\s>].*?</Event>", RegexOptions.Singleline))
        {
            string e = ev.Value;
            var time = Regex.Match(e, @"SystemTime=['""]([^'""]+)['""]");
            var boot = Regex.Match(e, @"<Data Name=['""]BootTime['""]>(\d+)</Data>");
            if (!boot.Success) continue;
            var main = Regex.Match(e, @"<Data Name=['""]MainPathBootTime['""]>(\d+)</Data>");
            DateTime at = time.Success && DateTime.TryParse(time.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal, out var t) ? t.ToLocalTime() : DateTime.MinValue;
            list.Add(new BootTiming(at, TimeSpan.FromMilliseconds(long.Parse(boot.Groups[1].Value)),
                main.Success ? TimeSpan.FromMilliseconds(long.Parse(main.Groups[1].Value)) : null));
        }
        return list.OrderByDescending(b => b.At).ToList();
    }

    public static IReadOnlyList<BootTiming> ReadBootTimes()
    {
        var (_, output, _) = Shell.RunUtf8("wevtutil.exe", "qe", "Microsoft-Windows-Diagnostics-Performance/Operational",
            "/q:*[System[(EventID=100)]]", "/c:5", "/rd:true", "/f:xml");
        return ParseBootEvents(output);
    }
}
