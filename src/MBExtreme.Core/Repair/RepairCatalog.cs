using System.Globalization;

namespace MBExtreme.Core.Repair;

/// <summary>Tarefas de reparo do Windows. Todos os comandos são os mesmos que você digitaria no terminal como administrador.</summary>
public static class RepairCatalog
{
    public const string GroupFiles = "Arquivos do sistema";
    public const string GroupDisk = "Disco";
    public const string GroupUpdate = "Windows Update e loja";
    public const string GroupNetwork = "Rede, ícones e outros";

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
                        "$s=Get-Date -Format yyyyMMddHHmmss; foreach($p in 'SoftwareDistribution','System32\\catroot2'){ $path=Join-Path $env:SystemRoot $p; if(Test-Path $path){ Rename-Item -LiteralPath $path -NewName ((Split-Path $path -Leaf)+'.old-'+$s) -ErrorAction Stop; 'Renomeada: '+$path } else { 'Não existe: '+$path } }"),
                    new RepairStep("Ligando Windows Update", "net.exe", "start wuauserv", StepKind.Generic, true),
                    new RepairStep("Ligando serviços de criptografia", "net.exe", "start cryptSvc", StepKind.Generic, true),
                    new RepairStep("Ligando transferência em segundo plano", "net.exe", "start bits", StepKind.Generic, true),
                    new RepairStep("Ligando instalador do Windows", "net.exe", "start msiserver", StepKind.Generic, true),
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
                "Limpa o cache de DNS e reinicia o Winsock e o TCP/IP. Resolve internet que conecta mas não navega e erros de rede depois de remover VPNs ou antivírus. Exige reiniciar o PC.",
                "Menos de 1 minuto",
                new[]
                {
                    new RepairStep("Limpando o cache de DNS", "ipconfig.exe", "/flushdns", StepKind.Generic, true),
                    new RepairStep("Reiniciando o Winsock", "netsh.exe", "winsock reset"),
                    new RepairStep("Reiniciando o TCP/IP", "netsh.exe", "int ip reset", StepKind.Generic, true),
                }, NeedsRestart: true, AdvisesRestorePoint: true),

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
        };
    }

    public static RepairTask? Find(string id) => All().FirstOrDefault(t => t.Id == id);
}
