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

    public OperationResult Execute(IProgress<string>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (program.Source == ProgramSource.Store)
        {
            if (string.IsNullOrWhiteSpace(program.StoreFullName) || program.StoreFullName.IndexOfAny(new[] { '\'', '"', ';', '`' }) >= 0)
                return OperationResult.Fail("Pacote da Loja sem nome válido.");
            var r = Windows.Shell.PowerShellUtf8($"Remove-AppxPackage -Package '{program.StoreFullName}' -ErrorAction Stop");
            return r.ExitCode == 0 ? OperationResult.Ok("Removido. Para ter de volta, instale pela Microsoft Store.") : OperationResult.Fail(string.IsNullOrWhiteSpace(r.Error) ? "O Windows recusou." : r.Error.Trim());
        }

        if (InstalledPrograms.SplitCommand(program.UninstallCommand) is not { } command) return OperationResult.Fail("Este programa não informou como desinstalar. Use Configurações > Aplicativos.");
        progress?.Report("Siga a janela do desinstalador de " + program.Name);
        var info = new System.Diagnostics.ProcessStartInfo(command.File, command.Arguments) { UseShellExecute = false };
        using var process = System.Diagnostics.Process.Start(info) ?? throw new InvalidOperationException("Não foi possível abrir o desinstalador.");
        // o desinstalador é de outro fabricante: espera até 30 min, sem matar (cortar no meio deixa o programa pela metade)
        if (!process.WaitForExit(TimeSpan.FromMinutes(30))) return OperationResult.Fail("O desinstalador ainda está aberto depois de 30 minutos. Termine nele.");
        return process.ExitCode switch
        {
            0 => OperationResult.Ok("Desinstalado."),
            3010 => OperationResult.Reboot("Desinstalado; termina depois de reiniciar."),
            1602 => new OperationResult(OperationState.Cancelled, "Cancelado no desinstalador."),
            _ => OperationResult.Fail($"O desinstalador terminou com o código {process.ExitCode}."),
        };
    }
}
