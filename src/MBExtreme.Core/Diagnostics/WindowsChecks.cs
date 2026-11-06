using System.Runtime.Versioning;
using Microsoft.Win32;
using MBExtreme.Core.Cleanup;
using MBExtreme.Core.Operations;
using MBExtreme.Core.Repair;
using MBExtreme.Core.Startup;
using MBExtreme.Core.Storage;
using MBExtreme.Core.Tweaks;
using MBExtreme.Core.Windows;

namespace MBExtreme.Core.Diagnostics;

/// <summary>Verificação feita de uma função (as verificações do Windows são todas assim).</summary>
public sealed class DelegateCheck(string id, string title, CheckDepth depth, Func<CancellationToken, IReadOnlyList<Finding>> run) : IDiagnosticCheck
{
    public string Id { get; } = id;
    public string Title { get; } = title;
    public CheckDepth Depth { get; } = depth;
    public IReadOnlyList<Finding> Run(CancellationToken ct) => run(ct);
}

/// <summary>
/// As verificações do Painel inteligente. Todas só leem: nenhuma altera o Windows. As correções que elas oferecem
/// rodam depois, pela fila, e só com a sua confirmação.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsChecks
{
    private static string SystemDrive => (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd('\\');

    /// <param name="tweaks">Ajustes que o usuário pode aplicar no plano dele (os Pro ficam de fora no plano grátis).</param>
    public static IReadOnlyList<IDiagnosticCheck> All(IReadOnlyList<Tweak> tweaks) => new IDiagnosticCheck[]
    {
        new DelegateCheck("disk-space", "Espaço livre nos discos", CheckDepth.Quick, _ =>
            StorageService.Volumes().Select(v => DiagnosticRules.DiskSpace(v, SystemDrive)).OfType<Finding>().ToList()),

        new DelegateCheck("junk", "Arquivos temporários e cache", CheckDepth.Quick, ct =>
        {
            var categories = CleanupCatalog.Create().Where(c => c.DefaultSelected).ToList();
            var items = CleanupService.ScanAll(categories, null, ct);
            long total = items.Sum(i => i.Bytes);
            var severity = DiagnosticRules.Junk(total);
            if (severity == Severity.Ok) return Array.Empty<Finding>();
            return new[]
            {
                new Finding("junk", $"{DiagnosticRules.Size(total)} em arquivos descartáveis",
                    "Temporários do Windows e dos programas e cache dos navegadores (sem cookies, senhas nem histórico). Arquivos em uso ficam onde estão.",
                    severity, new FixPlan("Limpar agora", () => new IOperation[] { new CleanupOperation(categories) })),
            };
        }),

        new DelegateCheck("security", "Proteção do Windows", CheckDepth.Quick, _ =>
            SecurityService.Evaluate(SecurityService.Read(), DateTime.Now)
                .Where(i => i.Level != SecurityLevel.Ok)
                .Select(i => new Finding("security-" + i.Title, i.Title, i.Detail, DiagnosticRules.Security(i.Level),
                    new FixPlan("Ver em Segurança", Page: "security", ChangesSystem: false)))
                .ToList()),

        new DelegateCheck("startup", "Programas que abrem com o Windows", CheckDepth.Quick, _ =>
        {
            int enabled = StartupService.List().Count(e => e.Enabled);
            var severity = DiagnosticRules.Startup(enabled);
            return severity == Severity.Ok ? Array.Empty<Finding>() : new[]
            {
                new Finding("startup", $"{enabled} programas abrem junto com o Windows", "Cada um atrasa a inicialização e fica gastando memória. Desligue os que você não usa logo ao ligar o PC.",
                    severity, new FixPlan("Escolher na Inicialização", Page: "startup", ChangesSystem: false)),
            };
        }),

        new DelegateCheck("tweaks", "Ajustes recomendados", CheckDepth.Quick, ct =>
        {
            var pending = tweaks.Where(t => t.Recommended && t.Risk == TweakRisk.Low && t.IsAvailable).Where(t => { ct.ThrowIfCancellationRequested(); return t.IsApplied() == false; }).ToList();
            return pending.Count == 0 ? Array.Empty<Finding>() : new[]
            {
                new Finding("tweaks", $"{pending.Count} ajuste(s) recomendado(s) ainda não aplicado(s)",
                    string.Join(", ", pending.Take(6).Select(t => t.Title)) + (pending.Count > 6 ? "..." : "") + ". Todos de risco baixo e com volta pela Central Desfazer.",
                    Severity.Info, new FixPlan("Aplicar os recomendados", () => pending.Select(t => (IOperation)new TweakOperation(t, apply: true)).ToList())),
            };
        }),

        new DelegateCheck("trim", "TRIM dos SSDs", CheckDepth.Quick, _ =>
        {
            bool? on = DiagnosticRules.TrimEnabled(Shell.Run("fsutil.exe", "behavior", "query", "DisableDeleteNotify").Output);
            if (on is null) throw new CheckUnavailableException("O Windows não informou o estado do TRIM.");
            if (on == true) return Array.Empty<Finding>();
            var trim = tweaks.FirstOrDefault(t => t.Id == "storage-trim");
            return new[]
            {
                new Finding("trim", "TRIM desligado", "Sem o TRIM, o SSD fica mais lento com o tempo.", Severity.Problem,
                    trim is null ? new FixPlan("Ver ajustes de armazenamento", Page: "tweaks-storage") : new FixPlan("Ligar o TRIM", () => new IOperation[] { new TweakOperation(trim, apply: true) })),
            };
        }),

        new DelegateCheck("optimize-task", "Otimização automática dos discos", CheckDepth.Quick, _ =>
        {
            var r = Shell.PowerShellUtf8("(Get-ScheduledTask -TaskPath '\\Microsoft\\Windows\\Defrag\\' -TaskName 'ScheduledDefrag' -ErrorAction Stop).State");
            string state = r.Output.Trim();
            if (r.ExitCode != 0 || state.Length == 0) throw new CheckUnavailableException("Não deu para ler a tarefa de otimização do Windows.");
            return !state.Equals("Disabled", StringComparison.OrdinalIgnoreCase) ? Array.Empty<Finding>() : new[]
            {
                new Finding("optimize-task", "Otimização automática dos discos desligada",
                    "O Windows não está fazendo o TRIM (SSD) nem a desfragmentação (HD) agendados.", Severity.Warning,
                    new FixPlan("Ligar de novo", () => new IOperation[] { new CommandOperation("diag.optimize-task", "Ligar a otimização automática dos discos",
                        "Enable-ScheduledTask -TaskPath '\\Microsoft\\Windows\\Defrag\\' -TaskName 'ScheduledDefrag' | Out-Null",
                        "Para desfazer: Otimizar unidades > Alterar configurações.") })),
            };
        }),

        new DelegateCheck("restore", "Proteção do Sistema (pontos de restauração)", CheckDepth.Quick, _ =>
        {
            var r = Shell.PowerShellUtf8("(Get-CimInstance -Namespace root/default -ClassName SystemRestoreConfig -ErrorAction Stop).RPSessionInterval");
            if (r.ExitCode != 0 || !int.TryParse(r.Output.Trim(), out int interval)) throw new CheckUnavailableException("Este Windows não informa a Proteção do Sistema.");
            return interval != 0 ? Array.Empty<Finding>() : new[]
            {
                new Finding("restore", "Proteção do Sistema desligada",
                    "Sem ela não há pontos de restauração: se algo der errado no Windows, não dá para voltar no tempo.", Severity.Warning,
                    new FixPlan("Ligar no disco do Windows", () => new IOperation[] { new CommandOperation("diag.restore", $"Ligar a Proteção do Sistema em {SystemDrive}",
                        $"Enable-ComputerRestore -Drive '{SystemDrive}\\' -ErrorAction Stop",
                        "Para desfazer: Propriedades do Sistema > Proteção do Sistema.") })),
            };
        }),

        new DelegateCheck("disk-health", "Saúde dos discos (SMART)", CheckDepth.Quick, _ =>
            StorageService.PhysicalDisks().Select(DiagnosticRules.DiskHealth).OfType<Finding>().ToList()),

        new DelegateCheck("reboot", "Reinício pendente", CheckDepth.Quick, _ =>
        {
            return !RebootState.IsPending() ? Array.Empty<Finding>() : new[]
            {
                new Finding("reboot", "O Windows está esperando reiniciar", "Uma atualização ou instalação só termina depois de reiniciar. Reinicie quando puder, antes de reparar o Windows.",
                    Severity.Info),
            };
        }),

        new DelegateCheck("clock", "Data e hora do Windows", CheckDepth.Quick, _ =>
        {
            var findings = new List<Finding>();
            if (ClockCheck.YearLooksWrong(DateTime.Now))
                findings.Add(new Finding("clock-year", "O relógio do PC está num ano errado",
                    $"O Windows acha que é {DateTime.Now:dd/MM/yyyy}. Costuma ser a bateria da placa-mãe fraca. Sites seguros e atualizações falham assim.",
                    Severity.Critical, SyncFix()));
            var r = Shell.Run("w32tm.exe", "/stripchart", "/computer:time.windows.com", "/samples:1", "/dataonly");
            double? offset = ClockCheck.ParseOffset(r.Output);
            if (offset is null)
            {
                if (findings.Count == 0) throw new CheckUnavailableException("Não deu para comparar com o servidor de hora (sem internet ou serviço de hora parado).");
                return findings;
            }
            var severity = ClockCheck.Severity(offset.Value);
            if (severity != Severity.Ok)
                findings.Add(new Finding("clock-offset", "Relógio do Windows fora da hora certa", $"Está {ClockCheck.Describe(offset.Value)} em relação ao servidor de hora da Microsoft.",
                    severity, SyncFix()));
            return findings;
        }),

        new DelegateCheck("windows-update", "Windows Update", CheckDepth.Quick, _ =>
        {
            var findings = new List<Finding>();
            foreach (var (name, title) in UpdateCheck.Services)
            {
                var start = Shell.PowerShellUtf8($"(Get-Service -Name '{name}' -ErrorAction Stop).StartType");
                if (start.ExitCode == 0 && start.Output.Trim().Equals("Disabled", StringComparison.OrdinalIgnoreCase))
                    findings.Add(new Finding("wu-service-" + name, $"Serviço \"{title}\" desativado",
                        "Com ele desativado o Windows não baixa nem instala atualizações (às vezes é coisa de \"otimizador\" antigo).", Severity.Problem,
                        RepairFix("wu-reset", "Reiniciar o Windows Update (religa os serviços)")));
            }
            var last = Shell.PowerShellUtf8(
                "$s = New-Object -ComObject Microsoft.Update.Session; $h = $s.CreateUpdateSearcher(); $n = $h.GetTotalHistoryCount(); " +
                "if ($n -gt 0) { $h.QueryHistory(0, [Math]::Min($n, 100)) | Where-Object { $_.ResultCode -eq 2 } | Sort-Object Date -Descending | Select-Object -First 1 | ForEach-Object { $_.Date.ToUniversalTime().ToString('o') } }");
            if (last.ExitCode == 0 && UpdateCheck.ParseDate(last.Output) is { } date)
            {
                int days = (int)(DateTimeOffset.UtcNow - date).TotalDays;
                var severity = UpdateCheck.Age(days);
                if (severity != Severity.Ok)
                    findings.Add(new Finding("wu-age", $"Nenhuma atualização instalada há {days} dias",
                        $"A última foi em {date.ToLocalTime():dd/MM/yyyy}. Atualizações trazem correções de segurança. Veja em Configurações > Windows Update se há erro.",
                        severity, RepairFix("wu-reset", "Reiniciar o Windows Update")));
            }
            return findings;
        }),

        DeepRepairCheck("deep-dism", "Imagem do Windows (DISM, só verificar)", "dism-scan",
            "A imagem do Windows tem arquivos corrompidos", "dism-restore", "Reparar a imagem do Windows (DISM) e depois os arquivos (SFC)", "sfc"),
        DeepRepairCheck("deep-sfc", "Arquivos do sistema (SFC, só verificar)", "sfc-verify",
            "Arquivos do sistema com problema", "sfc", "Reparar os arquivos do sistema (SFC)"),
        DeepRepairCheck("deep-disk", $"Disco do Windows ({SystemDrive}, só verificar)", "chkdsk-scan",
            $"O sistema de arquivos de {SystemDrive} tem erros", null, null),
    };

    private static FixPlan SyncFix() => new("Acertar o relógio agora", () => new IOperation[]
    {
        new CommandOperation("diag.clock", "Acertar o relógio pelo servidor de hora",
            "Set-Service -Name W32Time -StartupType Manual -ErrorAction SilentlyContinue; Start-Service W32Time -ErrorAction SilentlyContinue; " +
            "w32tm /config /update | Out-Null; w32tm /resync /force | Out-Null; if ($LASTEXITCODE -ne 0) { throw 'O servidor de hora não respondeu.' }",
            "Para desfazer: Configurações > Hora e idioma."),
    });

    private static FixPlan RepairFix(string taskId, string text) => new(text, () =>
        RepairCatalog.All().Where(t => t.Id == taskId).Select(t => (IOperation)new RepairOperation(t)).ToList());

    /// <summary>Verificação profunda pelos utilitários do Windows em modo "só verificar"; a correção (que altera) é oferecida à parte.</summary>
    private static IDiagnosticCheck DeepRepairCheck(string id, string title, string verifyTask, string problem, string? fixTask, string? fixText, string? thenTask = null) =>
        new DelegateCheck(id, title, CheckDepth.Deep, ct =>
        {
            var tasks = RepairCatalog.All();
            var task = tasks.FirstOrDefault(t => t.Id == verifyTask) ?? throw new CheckUnavailableException("Verificação indisponível nesta versão.");
            var result = new RepairRunner().RunAsync(task, new Progress<RepairUpdate>(_ => { }), ct).GetAwaiter().GetResult();
            ct.ThrowIfCancellationRequested();
            if (result.Cancelled) throw new OperationCanceledException();
            if (result.Success) return Array.Empty<Finding>();
            FixPlan fix = fixTask is null
                ? new FixPlan("Agendar o reparo do disco", Page: "repair")
                : new FixPlan(fixText!, () => new[] { fixTask, thenTask }.OfType<string>()
                    .Select(t => tasks.FirstOrDefault(x => x.Id == t)).OfType<RepairTask>().Select(t => (IOperation)new RepairOperation(t)).ToList());
            return new[] { new Finding(id, problem, result.Summary, Severity.Problem, fix) };
        });
}

/// <summary>Limpeza dos temporários como operação da fila (sem volta: o que é apagado não retorna).</summary>
[SupportedOSPlatform("windows")]
public sealed class CleanupOperation(IReadOnlyList<CleanupCategory> categories) : IOperation
{
    public OperationInfo Info { get; } = new("cleanup.safe", OperationKind.Cleanup, "Limpar temporários e cache");

    public OperationResult Execute(IProgress<string>? progress, CancellationToken ct)
    {
        long freed = 0;
        int deleted = 0, skipped = 0;
        foreach (var category in categories)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report("Limpando " + category.Title);
            var r = CleanupService.Clean(category, DateTime.UtcNow, ct);
            freed += r.FreedBytes;
            deleted += r.FilesDeleted;
            skipped += r.FilesSkipped;
        }
        return OperationResult.Ok($"{DiagnosticRules.Size(freed)} liberados ({deleted} arquivos). {(skipped > 0 ? $"{skipped} em uso por programas abertos ficaram — isso é normal." : "")}".Trim());
    }
}

/// <summary>Uma tarefa de reparo do Windows como operação da fila (sem volta).</summary>
[SupportedOSPlatform("windows")]
public sealed class RepairOperation(RepairTask task) : IOperation
{
    public OperationInfo Info { get; } = new("repair." + task.Id, OperationKind.Repair, task.Title, OperationRisk.Medium, NeedsAdmin: true);

    public OperationResult Execute(IProgress<string>? progress, CancellationToken ct)
    {
        var result = new RepairRunner().RunAsync(task, new Progress<RepairUpdate>(u => progress?.Report(u.Message)), ct).GetAwaiter().GetResult();
        if (result.Cancelled) return new OperationResult(OperationState.Cancelled, "Cancelado.");
        if (!result.Success) return OperationResult.Fail(result.Summary);
        return result.NeedsRestart ? OperationResult.Reboot(result.Summary) : OperationResult.Ok(result.Summary);
    }
}

/// <summary>Um comando do PowerShell que liga um recurso do Windows (com a dica de como desfazer pelo próprio Windows).</summary>
[SupportedOSPlatform("windows")]
public sealed class CommandOperation(string id, string title, string script, string undoHint) : IOperation
{
    public OperationInfo Info { get; } = new(id, OperationKind.Setting, title, NeedsAdmin: true);

    public OperationResult Execute(IProgress<string>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var r = Shell.PowerShellUtf8(script);
        return r.ExitCode == 0 ? new OperationResult(OperationState.Succeeded, undoHint) : OperationResult.Fail(string.IsNullOrWhiteSpace(r.Error) ? "O Windows recusou." : r.Error.Trim());
    }
}
