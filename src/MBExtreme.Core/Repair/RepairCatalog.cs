using System.Globalization;

namespace MBExtreme.Core.Repair;

/// <summary>Tarefas de reparo do Windows. Todos os comandos são os mesmos que você digitaria no terminal como administrador.</summary>
public static class RepairCatalog
{
    public const string GroupFiles = "Arquivos do sistema";
    public const string GroupDisk = "Disco";
    public const string GroupUpdate = "Windows Update e loja";
    public const string GroupNetwork = "Rede, ícones e outros";
    public const string GroupDevices = "Áudio, Bluetooth, Wi-Fi e jogos";

    public static string SystemDrive =>
        Environment.GetEnvironmentVariable("SystemDrive") is { Length: > 0 } drive ? drive : "C:";

    public static IReadOnlyList<RepairTask> All() => Create(SystemDrive, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

    /// <summary>Letra usada pelo chkdsk para responder "sim" à pergunta de agendar a verificação.</summary>
    public static string YesAnswer(string twoLetterLanguage) => twoLetterLanguage switch
    {
        "pt" or "es" or "it" => "S",
        "fr" => "O",
        "de" => "J",
        "tr" => "E",
        _ => "Y",
    };

    private static RepairStep Ps(string title, string script, bool continueOnError = false) =>
        new(title, "powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"",
            StepKind.Generic, continueOnError);

    public static IReadOnlyList<RepairTask> Create(string systemDrive, string language)
    {
        var sfc = new RepairStep("Verificando e reparando arquivos do sistema (SFC)", "sfc.exe", "/scannow", StepKind.Sfc);
        var dismRestore = new RepairStep("Reparando a imagem do Windows (DISM)", "DISM.exe",
            "/Online /Cleanup-Image /RestoreHealth", StepKind.Dism);
        var chkdskScan = new RepairStep("Verificando o disco do sistema", "chkdsk.exe", $"{systemDrive} /scan", StepKind.Chkdsk);

        string yes = YesAnswer(language);

        return new RepairTask[]
        {
            new("full", GroupFiles, "Reparo completo recomendado",
                "Sequência indicada pela Microsoft: repara a imagem do Windows (DISM), depois os arquivos do sistema (SFC) e por fim verifica o disco. Resolve a maioria dos erros de travamentos, tela azul e recursos que deixaram de funcionar.",
                "20 a 60 minutos",
                new[] { dismRestore, sfc, chkdskScan }, AdvisesRestorePoint: true, NeedsInternet: true),

            new("sfc", GroupFiles, "Verificar e reparar arquivos do sistema (SFC)",
                "Compara cada arquivo protegido do Windows com a cópia original e troca os que estiverem corrompidos.",
                "5 a 15 minutos", new[] { sfc }),

            new("sfc-verify", GroupFiles, "Somente verificar arquivos do sistema",
                "Faz a mesma verificação do SFC, mas não altera nada. Útil para saber se há problema antes de reparar.",
                "5 a 15 minutos",
                new[] { new RepairStep("Verificando arquivos do sistema (sem reparar)", "sfc.exe", "/verifyonly", StepKind.Sfc) }),

            new("dism-scan", GroupFiles, "Verificar a imagem do Windows (DISM)",
                "Confere se o repositório de componentes do Windows está íntegro. Não altera nada.",
                "2 a 10 minutos",
                new[]
                {
                    new RepairStep("Verificação rápida da imagem", "DISM.exe", "/Online /Cleanup-Image /CheckHealth", StepKind.DismCheck),
                    new RepairStep("Verificação completa da imagem", "DISM.exe", "/Online /Cleanup-Image /ScanHealth", StepKind.DismCheck),
                }),

            new("dism-restore", GroupFiles, "Reparar a imagem do Windows (DISM)",
                "Baixa pelo Windows Update as cópias boas dos componentes corrompidos e as restaura. Rode antes do SFC quando o SFC não consegue reparar.",
                "10 a 30 minutos", new[] { dismRestore }, AdvisesRestorePoint: true, NeedsInternet: true),

            new("dism-cleanup", GroupFiles, "Limpar componentes antigos do Windows",
                "Remove versões substituídas de componentes (pasta WinSxS) e libera espaço. Não usa o /ResetBase, então continua sendo possível desinstalar atualizações.",
                "5 a 20 minutos",
                new[] { new RepairStep("Limpando componentes antigos", "DISM.exe", "/Online /Cleanup-Image /StartComponentCleanup", StepKind.Dism) }),

            new("chkdsk-scan", GroupDisk, $"Verificar o disco do sistema ({systemDrive})",
                "Procura erros no sistema de arquivos com o Windows em uso e corrige o que for possível. Não precisa reiniciar.",
                "2 a 15 minutos", new[] { chkdskScan }),

            new("chkdsk-fix", GroupDisk, $"Reparo profundo do disco ({systemDrive}) no próximo reinício",
                "Agenda o CHKDSK com /f /r, que também procura setores defeituosos. Roda antes do Windows abrir, na próxima vez que o PC reiniciar, e em discos grandes pode levar horas. Não desligue o PC durante a verificação.",
                "Agendamento imediato; a verificação ocorre no reinício",
                new[] { new RepairStep("Agendando verificação do disco", "chkdsk.exe", $"{systemDrive} /f /r",
                    StepKind.ChkdskSchedule, false, yes) },
                NeedsRestart: true),

            new("wu-reset", GroupUpdate, "Reiniciar o Windows Update",
                "Para os serviços de atualização, renomeia as pastas de cache (SoftwareDistribution e catroot2, mantidas com o final .old) e liga os serviços de novo. Resolve atualizações presas ou que falham sempre com o mesmo erro.",
                "1 a 3 minutos",
                new[]
                {
                    new RepairStep("Parando Windows Update", "net.exe", "stop wuauserv", StepKind.Generic, true),
                    new RepairStep("Parando serviços de criptografia", "net.exe", "stop cryptSvc", StepKind.Generic, true),
                    new RepairStep("Parando transferência em segundo plano", "net.exe", "stop bits", StepKind.Generic, true),
                    new RepairStep("Parando instalador do Windows", "net.exe", "stop msiserver", StepKind.Generic, true),
                    Ps("Renomeando as pastas de cache",
                        "$s=Get-Date -Format yyyyMMddHHmmss; foreach($p in 'SoftwareDistribution','System32\\catroot2'){ $path=Join-Path $env:SystemRoot $p; if(Test-Path $path){ Rename-Item -LiteralPath $path -NewName ((Split-Path $path -Leaf)+'.old-'+$s) -ErrorAction Stop; 'Renomeada: '+$path } else { 'Não existe: '+$path } }", true),
                    new RepairStep("Ligando Windows Update", "net.exe", "start wuauserv", StepKind.Generic, true, null, true),
                    new RepairStep("Ligando serviços de criptografia", "net.exe", "start cryptSvc", StepKind.Generic, true, null, true),
                    new RepairStep("Ligando transferência em segundo plano", "net.exe", "start bits", StepKind.Generic, true, null, true),
                    new RepairStep("Ligando instalador do Windows", "net.exe", "start msiserver", StepKind.Generic, true, null, true),
                }, AdvisesRestorePoint: true),

            new("store", GroupUpdate, "Reparar a Microsoft Store",
                "Registra de novo a Microsoft Store e seus componentes. Use quando a loja não abre, fecha sozinha ou não baixa aplicativos.",
                "1 a 3 minutos",
                new[]
                {
                    Ps("Registrando a Microsoft Store",
                        "Get-AppxPackage -AllUsers Microsoft.WindowsStore | ForEach-Object { Add-AppxPackage -DisableDevelopmentMode -Register ($_.InstallLocation + '\\AppXManifest.xml') }"),
                    Ps("Registrando o instalador de aplicativos",
                        "Get-AppxPackage -AllUsers Microsoft.DesktopAppInstaller | ForEach-Object { Add-AppxPackage -DisableDevelopmentMode -Register ($_.InstallLocation + '\\AppXManifest.xml') }", true),
                }, AdvisesRestorePoint: true),

            new("wmi", GroupNetwork, "Verificar e reparar o WMI",
                "O WMI é a base de onde o Windows e vários programas leem informações do PC. Se estiver inconsistente, programas de monitoramento e instaladores falham.",
                "1 a 2 minutos",
                new[]
                {
                    new RepairStep("Verificando o repositório WMI", "winmgmt.exe", "/verifyrepository", StepKind.Generic, true),
                    new RepairStep("Recuperando o repositório WMI", "winmgmt.exe", "/salvagerepository", StepKind.Generic, true),
                }),

            new("net-reset", GroupNetwork, "Reiniciar a pilha de rede",
                "Limpa o cache de DNS e reinicia o Winsock e o TCP/IP. Resolve internet que conecta mas não navega e erros de rede depois de remover VPNs ou antivírus. Apaga IP fixo, DNS e gateway manuais. Exige reiniciar o PC.",
                "Menos de 1 minuto",
                new[]
                {
                    new RepairStep("Limpando o cache de DNS", "ipconfig.exe", "/flushdns", StepKind.Generic, true),
                    new RepairStep("Reiniciando o Winsock", "netsh.exe", "winsock reset"),
                    new RepairStep("Reiniciando o TCP/IP", "netsh.exe", "int ip reset", StepKind.Generic, true),
                }, NeedsRestart: true, AdvisesRestorePoint: true,
                Caution: "O reinício do TCP/IP apaga IP fixo, DNS e gateway configurados manualmente (inclusive o DNS definido pelo MB Extreme). Anote-os antes se usar configuração fixa."),

            new("icons", GroupNetwork, "Reconstruir o cache de ícones",
                "Apaga os arquivos de cache de ícones e miniaturas. O Windows recria na hora. Resolve ícones em branco ou trocados. A barra de tarefas pisca por alguns segundos.",
                "Menos de 1 minuto",
                new[]
                {
                    new RepairStep("Fechando o Explorador de Arquivos", "taskkill.exe", "/F /IM explorer.exe", StepKind.Generic, true),
                    Ps("Apagando o cache de ícones e miniaturas",
                        "$d=Join-Path $env:LOCALAPPDATA 'Microsoft\\Windows\\Explorer'; Remove-Item -LiteralPath (Join-Path $env:LOCALAPPDATA 'IconCache.db') -Force -ErrorAction SilentlyContinue; Get-ChildItem -LiteralPath $d -Filter 'iconcache_*.db' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue; Get-ChildItem -LiteralPath $d -Filter 'thumbcache_*.db' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue; 'Cache apagado.'", true),
                    Ps("Reabrindo o Explorador de Arquivos",
                        "Start-Sleep -Seconds 3; if(-not (Get-Process explorer -ErrorAction SilentlyContinue)){ Start-Process explorer.exe }; 'Explorador reaberto.'", true),
                }),

            // ---- Etapa 8: correções comuns (técnicas estudadas no pacote Lobo Alpha, reescritas como tarefas do app)
            new("dns-renew", GroupNetwork, "Limpar o DNS e renovar o IP",
                "Limpa o cache de DNS e pede um IP novo ao roteador. Mais leve que reiniciar a pilha de rede: não apaga configurações manuais nem exige reiniciar.",
                "Menos de 1 minuto",
                new[]
                {
                    new RepairStep("Limpando o cache de DNS", "ipconfig.exe", "/flushdns", StepKind.Generic, true),
                    new RepairStep("Renovando o IP", "ipconfig.exe", "/renew", StepKind.Generic, true),
                }),

            new("audio", GroupDevices, "Reiniciar o áudio do Windows",
                "Reinicia os serviços de áudio. Resolve som que sumiu, chiado, dispositivo que não aparece ou áudio travado depois de suspender o PC.",
                "Menos de 1 minuto",
                new[]
                {
                    new RepairStep("Parando o serviço de áudio", "net.exe", "stop Audiosrv /y", StepKind.Generic, true),
                    new RepairStep("Parando o construtor de pontos de extremidade", "net.exe", "stop AudioEndpointBuilder /y", StepKind.Generic, true),
                    new RepairStep("Ligando o construtor de pontos de extremidade", "net.exe", "start AudioEndpointBuilder", StepKind.Generic, true, null, true),
                    new RepairStep("Ligando o serviço de áudio", "net.exe", "start Audiosrv", StepKind.Generic, true, null, true),
                }),

            new("bluetooth", GroupDevices, "Reiniciar o Bluetooth",
                "Deixa o serviço de Bluetooth em modo manual (como o Windows instala) e o reinicia. Resolve fones e controles que não conectam ou o Bluetooth que sumiu.",
                "Menos de 1 minuto",
                new[]
                {
                    new RepairStep("Restaurando o tipo de início do serviço", "sc.exe", "config bthserv start= demand", StepKind.Generic, true),
                    new RepairStep("Parando o serviço", "net.exe", "stop bthserv /y", StepKind.Generic, true),
                    new RepairStep("Ligando o serviço", "net.exe", "start bthserv", StepKind.Generic, true, null, true),
                }),

            new("wifi", GroupDevices, "Reiniciar o Wi-Fi e o modo avião",
                "Reinicia o serviço de Wi-Fi e o gerenciador de rádio. Resolve Wi-Fi que não aparece e notebook preso no modo avião.",
                "Menos de 1 minuto",
                new[]
                {
                    new RepairStep("Restaurando o serviço de rádio", "sc.exe", "config RmSvc start= auto", StepKind.Generic, true),
                    new RepairStep("Restaurando o serviço de Wi-Fi", "sc.exe", "config WlanSvc start= auto", StepKind.Generic, true),
                    new RepairStep("Parando o Wi-Fi", "net.exe", "stop WlanSvc /y", StepKind.Generic, true),
                    new RepairStep("Ligando o gerenciador de rádio", "net.exe", "start RmSvc", StepKind.Generic, true, null, true),
                    new RepairStep("Ligando o Wi-Fi", "net.exe", "start WlanSvc", StepKind.Generic, true, null, true),
                }),

            new("explorer", GroupDevices, "Reiniciar o Explorador, o menu Iniciar e a barra de tarefas",
                "Fecha e reabre o Explorador de Arquivos. Resolve menu Iniciar que não abre, barra de tarefas travada e tela preta com cursor depois de entrar no Windows.",
                "Menos de 1 minuto",
                new[]
                {
                    new RepairStep("Fechando o Explorador de Arquivos", "taskkill.exe", "/F /IM explorer.exe", StepKind.Generic, true),
                    Ps("Reabrindo o Explorador de Arquivos",
                        "Start-Sleep -Seconds 2; if(-not (Get-Process explorer -ErrorAction SilentlyContinue)){ Start-Process explorer.exe }; 'Explorador reaberto.'", true),
                }),

            new("xbox", GroupDevices, "Reparar o Xbox e o Game Pass",
                "Deixa os serviços do Xbox em modo manual, liga-os e registra de novo os aplicativos do Xbox. Resolve jogos do Game Pass que não instalam ou não abrem.",
                "1 a 3 minutos",
                new[]
                {
                    new RepairStep("Restaurando XblAuthManager", "sc.exe", "config XblAuthManager start= demand", StepKind.Generic, true),
                    new RepairStep("Restaurando XblGameSave", "sc.exe", "config XblGameSave start= demand", StepKind.Generic, true),
                    new RepairStep("Restaurando XboxNetApiSvc", "sc.exe", "config XboxNetApiSvc start= demand", StepKind.Generic, true),
                    new RepairStep("Restaurando XboxGipSvc", "sc.exe", "config XboxGipSvc start= demand", StepKind.Generic, true),
                    Ps("Registrando os aplicativos do Xbox",
                        "Get-AppxPackage -AllUsers *Xbox* | ForEach-Object { Add-AppxPackage -DisableDevelopmentMode -Register ($_.InstallLocation + '\\AppXManifest.xml') -ErrorAction SilentlyContinue }; 'Concluído.'", true),
                    new RepairStep("Ligando XblAuthManager", "net.exe", "start XblAuthManager", StepKind.Generic, true, null, true),
                    new RepairStep("Ligando XboxNetApiSvc", "net.exe", "start XboxNetApiSvc", StepKind.Generic, true, null, true),
                }, AdvisesRestorePoint: true),

            new("anticheat", GroupDevices, "Antitrapaça bloqueando o jogo",
                "Remove ajustes de segurança desligados por outros programas de otimização (política que desliga o Defender e mitigações do BCD) e liga a proteção em tempo real. Antitrapaças como Vanguard, Easy Anti-Cheat e BattlEye exigem isso.",
                "Menos de 1 minuto",
                new[]
                {
                    new RepairStep("Removendo a política que desliga o Defender", "reg.exe", "delete \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows Defender\" /v DisableAntiSpyware /f", StepKind.Generic, true),
                    new RepairStep("Removendo a política de proteção em tempo real", "reg.exe", "delete \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows Defender\\Real-Time Protection\" /v DisableRealtimeMonitoring /f", StepKind.Generic, true),
                    new RepairStep("Removendo mitigações desligadas no BCD", "bcdedit.exe", "/deletevalue nx", StepKind.Generic, true),
                    Ps("Ligando a proteção em tempo real", "Set-MpPreference -DisableRealtimeMonitoring $false; 'Concluído.'", true),
                }, NeedsRestart: true, AdvisesRestorePoint: true,
                Caution: "Alguns antitrapaças também exigem Inicialização Segura (Secure Boot) e TPM ligados na BIOS, o que este reparo não altera."),

            new("shutdown-fix", GroupDevices, "PC que não desliga (só fica encerrando)",
                "Desativa a inicialização rápida, que costuma travar o desligamento, e confere o estado da hibernação. Exige reiniciar.",
                "Menos de 1 minuto",
                new[]
                {
                    new RepairStep("Desativando a inicialização rápida", "reg.exe",
                        "add \"HKLM\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Power\" /v HiberbootEnabled /t REG_DWORD /d 0 /f", StepKind.Generic, true),
                }, NeedsRestart: true),

            new("eventlogs", GroupFiles, "Limpar os logs de eventos do Windows",
                "Apaga o conteúdo dos registros de eventos (Aplicativo, Sistema, Segurança...). Libera espaço e começa uma lista limpa para diagnosticar um problema novo.",
                "Menos de 1 minuto",
                new[]
                {
                    Ps("Limpando os registros de eventos",
                        "wevtutil.exe el | ForEach-Object { wevtutil.exe cl $_ 2>$null }; 'Registros limpos.'", true),
                },
                Caution: "Apaga o histórico que ajuda a entender travamentos e telas azuis passadas. Só faça se não precisar investigar nada antigo."),
        };
    }

    public static RepairTask? Find(string id) => All().FirstOrDefault(t => t.Id == id);
}
