using System.Runtime.Versioning;
using MBExtreme.Core.Apps;

namespace MBExtreme.Core.Operations;

/// <summary>Remover um aplicativo da Microsoft Store (sem volta pelo programa: reinstala pela Loja).</summary>
[SupportedOSPlatform("windows")]
public sealed class AppRemovalOperation(BloatApp app) : IOperation
{
    public OperationInfo Info { get; } = new("app.remove." + app.Id, OperationKind.AppRemoval, "Remover aplicativo: " + app.Title,
        app.Level switch { AppRemovalLevel.Dangerous => OperationRisk.High, AppRemovalLevel.Caution => OperationRisk.Medium, _ => OperationRisk.Low });

    public OperationResult Execute(IProgress<string>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        AppRemovalService.Remove(app);
        return OperationResult.Ok("Removido para este usuário. Para ter de volta, instale pela Microsoft Store.");
    }
}

/// <summary>Instalar ou atualizar um programa pelo winget (uma operação por vez na fila).</summary>
[SupportedOSPlatform("windows")]
public sealed class WingetOperation(string id, string title, bool upgrade) : IOperation
{
    public OperationInfo Info { get; } = new((upgrade ? "winget.upgrade." : "winget.install.") + id, OperationKind.Program,
        (upgrade ? "Atualizar: " : "Instalar: ") + title, Typical: TimeSpan.FromMinutes(2));

    public OperationResult Execute(IProgress<string>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var r = upgrade ? WingetService.UpgradeDetailed(id) : WingetService.InstallDetailed(id);
        return r.Ok ? (r.RebootRequired ? OperationResult.Reboot(r.Message) : OperationResult.Ok(r.Message)) : OperationResult.Fail(r.Message);
    }
}

/// <summary>
/// Desinstalar um programa com o desinstalador dele (a janela do próprio programa aparece; você acompanha) ou, se for da Loja, pelo Windows.
/// Sem volta pelo programa.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class UninstallOperation(InstalledProgram program) : IOperation
{
    public OperationInfo Info { get; } = new("uninstall." + program.Name, OperationKind.Program, "Desinstalar: " + program.Name, OperationRisk.Medium, NeedsAdmin: true);

    /// <summary>Como vai remover (silencioso ou com a janela do fabricante).</summary>
    public UninstallPlan? Plan { get; } = SilentUninstall.Plan(program);

    public OperationResult Execute(IProgress<string>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (Plan is null) return OperationResult.Fail("Este programa não informou como desinstalar. Use Configurações > Aplicativos.");
        if (Plan.Kind == UninstallerKind.Store)
        {
            if (Plan.Arguments.IndexOfAny(new[] { '\'', '"', ';', '`' }) >= 0) return OperationResult.Fail("Pacote da Loja sem nome válido.");
            var r = Windows.Shell.PowerShellUtf8($"Remove-AppxPackage -Package '{Plan.Arguments}' -ErrorAction Stop");
            return r.ExitCode == 0 ? OperationResult.Ok("Removido. Para ter de volta, instale pela Microsoft Store.") : OperationResult.Fail(string.IsNullOrWhiteSpace(r.Error) ? "O Windows recusou." : r.Error.Trim());
        }

        var result = Run(Plan, progress);
        // silencioso que não funcionou (o desinstalador não aceitou): tenta de novo com a janela do fabricante
        if (Plan.Silent && result.State == OperationState.Failed && Plan.Kind != UninstallerKind.Msi && InstalledPrograms.SplitCommand(program.UninstallCommand) is { } normal)
        {
            progress?.Report("Não aceitou o modo silencioso: siga a janela do desinstalador de " + program.Name);
            result = Run(new UninstallPlan(UninstallerKind.Unknown, normal.File, normal.Arguments, false), progress);
        }
        return result;
    }

    private OperationResult Run(UninstallPlan plan, IProgress<string>? progress)
    {
        progress?.Report(plan.Silent ? "Removendo " + program.Name + " sem janelas..." : "Siga a janela do desinstalador de " + program.Name);
        var info = new System.Diagnostics.ProcessStartInfo(plan.File, plan.Arguments) { UseShellExecute = false, CreateNoWindow = plan.Silent };
        using var process = System.Diagnostics.Process.Start(info) ?? throw new InvalidOperationException("Não foi possível abrir o desinstalador.");
        // o desinstalador é de outro fabricante: espera até 30 min, sem matar (cortar no meio deixa o programa pela metade)
        if (!process.WaitForExit(TimeSpan.FromMinutes(30))) return OperationResult.Fail("O desinstalador ainda está aberto depois de 30 minutos. Termine nele.");
        var (ok, reboot, cancelled) = SilentUninstall.ReadExit(process.ExitCode);
        if (cancelled) return new OperationResult(OperationState.Cancelled, "Cancelado no desinstalador.");
        if (!ok) return OperationResult.Fail($"O desinstalador terminou com o código {process.ExitCode}.");
        // alguns desinstaladores se copiam para a pasta temporária e saem na hora: espera a entrada sumir (até 3 min)
        if (OperatingSystem.IsWindows())
        {
            var until = DateTime.UtcNow.AddMinutes(3);
            while (InstalledPrograms.StillRegistered(program) && DateTime.UtcNow < until) Thread.Sleep(1000);
        }
        return reboot ? OperationResult.Reboot("Desinstalado; termina depois de reiniciar.") : OperationResult.Ok("Desinstalado.");
    }
}
